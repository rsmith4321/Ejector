// AirDrop Inbox settings, folder watcher and migration (Website edition only).
// The watcher runs AirDropInbox.Processor passes on a background queue whenever
// ~/Downloads changes, and publishes the results to Settings and the menu.
#if !APP_STORE
import AppKit
import Combine

@MainActor
final class AirDropInboxModel: ObservableObject {
    static let shared = AirDropInboxModel()
    typealias OriginalsMode = AirDropInbox.OriginalsMode

    enum Status: Equatable {
        case off, watching
        case needsAccess(URL)        // macOS refused access to Downloads or the destination
        case destinationMissing(URL) // for example an unplugged drive
        case standaloneRunning       // the old Automatic HEIC to JPEG app is still handling AirDrops
    }

    /// The standalone app this feature replaced. While it runs, Easy Eject leaves AirDrops to it
    /// so the two never handle the same file.
    static let standaloneBundleID = "io.github.rsmith4321.automatic-heic-to-jpeg"
    static let standaloneAppPath = "/Applications/Automatic HEIC to JPEG.app"
    static let defaultDestination = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Desktop/AirDrop").path

    private enum Key {
        static let enabled = "airDropInbox.enabled"
        static let useDestination = "airDropInbox.useDestination"
        static let destinationPath = "airDropInbox.destinationPath"
        static let convert = "airDropInbox.convertHEIC"
        static let originals = "airDropInbox.originals"
        static let quality = "airDropInbox.jpegQuality"
        static let convertOther = "airDropInbox.convertOtherHEIC"
        static let watchingSince = "airDropInbox.watchingSince"
        static let activity = "airDropInbox.activity"
        static let migrationChecked = "airDropInbox.migrationChecked"
        static let migratedFrom = "airDropInbox.migratedFrom"
        /// Hidden, for testing against a scratch folder instead of ~/Downloads.
        static let watchFolder = "airDropInbox.watchFolder"
    }
    private let defaults = UserDefaults.standard

    // MARK: Settings

    @Published var enabled: Bool { didSet { defaults.set(enabled, forKey: Key.enabled) } }
    @Published var useDestination: Bool { didSet { defaults.set(useDestination, forKey: Key.useDestination) } }
    @Published var destinationPath: String { didSet { defaults.set(destinationPath, forKey: Key.destinationPath) } }
    @Published var convert: Bool { didSet { defaults.set(convert, forKey: Key.convert) } }
    @Published var originals: OriginalsMode { didSet { defaults.set(originals.rawValue, forKey: Key.originals) } }
    @Published var quality: Double { didSet { defaults.set(quality, forKey: Key.quality) } }
    @Published var convertOtherHEIC: Bool { didSet { defaults.set(convertOtherHEIC, forKey: Key.convertOther) } }

    // MARK: State

    @Published private(set) var status = Status.off
    @Published private(set) var activity: [AirDropInbox.Entry] = []
    /// AirDrops that were already in Downloads before the inbox was turned on.
    @Published private(set) var earlier = 0
    @Published private(set) var migratedFrom: String?

    var watchFolder: URL {
        if let custom = defaults.string(forKey: Key.watchFolder), !custom.isEmpty {
            return URL(fileURLWithPath: (custom as NSString).expandingTildeInPath, isDirectory: true).standardizedFileURL
        }
        return FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Downloads", isDirectory: true).standardizedFileURL
    }

    var destinationURL: URL? {
        guard useDestination else { return nil }
        let url = URL(fileURLWithPath: (destinationPath as NSString).expandingTildeInPath, isDirectory: true).standardizedFileURL
        return AirDropInbox.sameFolder(url, watchFolder) ? nil : url
    }

    /// The folder "Open AirDrop Folder" shows: the destination, or Downloads.
    var outputFolder: URL { destinationURL ?? watchFolder }

    private var watchingSince: Date? {
        get { defaults.object(forKey: Key.watchingSince) as? Date }
        set { defaults.set(newValue, forKey: Key.watchingSince) }
    }

    private let queue = DispatchQueue(label: "easy-eject.airdrop-inbox")
    private let processor = AirDropInbox.Processor()   // used only on `queue`
    private var source: DispatchSourceFileSystemObject?
    private var heartbeat: Timer?
    private var pending: DispatchWorkItem?
    private var bag = Set<AnyCancellable>()
    let logURL = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        .appendingPathComponent(Bundle.main.bundleIdentifier?.hasSuffix(".preview") == true ? "Easy Eject Preview" : "Easy Eject")
        .appendingPathComponent("airdrop-inbox.log")

    private init() {
        defaults.register(defaults: [
            Key.enabled: false, Key.useDestination: true, Key.destinationPath: Self.defaultDestination,
            Key.convert: true, Key.originals: OriginalsMode.trash.rawValue, Key.quality: 0.95, Key.convertOther: false,
        ])
        enabled = defaults.bool(forKey: Key.enabled)
        useDestination = defaults.bool(forKey: Key.useDestination)
        destinationPath = defaults.string(forKey: Key.destinationPath) ?? Self.defaultDestination
        convert = defaults.bool(forKey: Key.convert)
        originals = OriginalsMode(rawValue: defaults.string(forKey: Key.originals) ?? "") ?? .trash
        quality = min(1, max(0.5, defaults.double(forKey: Key.quality)))
        convertOtherHEIC = defaults.bool(forKey: Key.convertOther)
        migratedFrom = defaults.string(forKey: Key.migratedFrom)
        if let data = defaults.data(forKey: Key.activity), let saved = try? JSONDecoder().decode([AirDropInbox.Entry].self, from: data) {
            activity = saved
        }
    }

    // MARK: Lifecycle

    func launch() {
        migrateFromStandaloneIfNeeded()
        // Any settings change: rescan with the new options.
        Publishers.MergeMany(
            $enabled.map { _ in () }.eraseToAnyPublisher(), $useDestination.map { _ in () }.eraseToAnyPublisher(),
            $destinationPath.map { _ in () }.eraseToAnyPublisher(), $convert.map { _ in () }.eraseToAnyPublisher(),
            $originals.map { _ in () }.eraseToAnyPublisher(), $convertOtherHEIC.map { _ in () }.eraseToAnyPublisher())
            .dropFirst(6)
            .debounce(for: .milliseconds(300), scheduler: RunLoop.main)
            .sink { [weak self] in self?.restart() }
            .store(in: &bag)
        // Pick up AirDrops promptly once the standalone app quits.
        NSWorkspace.shared.notificationCenter.publisher(for: NSWorkspace.didTerminateApplicationNotification)
            .compactMap { $0.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication }
            .filter { $0.bundleIdentifier == Self.standaloneBundleID }
            .sink { [weak self] _ in self?.restart() }
            .store(in: &bag)
        restart()
    }

    func restart() {
        stopWatching()
        guard enabled else { status = .off; return }
        // Only AirDrops that arrive from now on are handled automatically;
        // older ones wait for "Process Earlier AirDrops".
        if watchingSince == nil { watchingSince = Date() }
        status = .watching
        startWatching()
        runScan(processEarlier: false, probe: true)
    }

    private func startWatching() {
        let fd = open(watchFolder.path, O_EVTONLY)
        if fd >= 0 {
            let src = DispatchSource.makeFileSystemObjectSource(fileDescriptor: fd, eventMask: [.write, .rename, .delete, .link], queue: .main)
            src.setEventHandler { [weak self] in self?.scheduleScan(after: 1) }
            src.setCancelHandler { close(fd) }
            src.resume()
            source = src
        }
        // A slow heartbeat catches anything the folder events miss, and retries
        // after a permission or drive problem.
        heartbeat = Timer.scheduledTimer(withTimeInterval: 30, repeats: true) { [weak self] _ in
            Task { @MainActor in
                guard let self else { return }
                if self.source == nil { self.restart() } else { self.scheduleScan(after: 0) }
            }
        }
    }

    private func stopWatching() {
        source?.cancel(); source = nil
        heartbeat?.invalidate(); heartbeat = nil
        pending?.cancel(); pending = nil
    }

    private var standaloneRunning: Bool {
        !NSRunningApplication.runningApplications(withBundleIdentifier: Self.standaloneBundleID).isEmpty
    }

    private func options() -> AirDropInbox.Options {
        AirDropInbox.Options(watchFolder: watchFolder, destination: destinationURL, convert: convert, originals: originals,
                             quality: quality, convertOtherHEIC: convertOtherHEIC, since: watchingSince)
    }

    func scheduleScan(after delay: Double) {
        guard enabled else { return }
        pending?.cancel()
        let item = DispatchWorkItem { [weak self] in self?.runScan(processEarlier: false) }
        pending = item
        DispatchQueue.main.asyncAfter(deadline: .now() + delay, execute: item)
    }

    private func runScan(processEarlier: Bool, probe: Bool = false) {
        guard !standaloneRunning else { status = .standaloneRunning; return }
        let opts = options()
        let processor = processor
        queue.async { [weak self] in
            let result = processor.scan(opts, processEarlier: processEarlier, probe: probe)
            DispatchQueue.main.async { self?.apply(result, processEarlier: processEarlier) }
        }
    }

    private func apply(_ result: AirDropInbox.ScanResult, processEarlier: Bool) {
        guard enabled else { return }
        if let problem = result.watchProblem ?? result.destinationProblem {
            switch problem {
            case .noAccess(let url): status = .needsAccess(url)
            case .missing(let url): status = .destinationMissing(url)
            }
        } else {
            status = .watching
        }
        if !processEarlier { earlier = result.earlier }
        if !result.entries.isEmpty { record(result.entries) }
        if result.settling || processEarlier { scheduleScan(after: 2) }
    }

    /// The one-time action for AirDrops that were already in Downloads before the inbox was turned on.
    func processEarlier() {
        guard enabled else { return }
        earlier = 0
        runScan(processEarlier: true)
    }

    // MARK: Activity

    private func record(_ entries: [AirDropInbox.Entry]) {
        activity = Array((entries.reversed() + activity).prefix(20))
        if let data = try? JSONEncoder().encode(activity) { defaults.set(data, forKey: Key.activity) }
        let stamp = ISO8601DateFormatter()
        let lines = entries.map { "\(stamp.string(from: $0.date)) [\($0.kind.rawValue)] \($0.title): \($0.detail)\n" }.joined()
        try? FileManager.default.createDirectory(at: logURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        if let handle = try? FileHandle(forWritingTo: logURL) {
            handle.seekToEndOfFile(); handle.write(Data(lines.utf8)); try? handle.close()
        } else {
            try? Data(lines.utf8).write(to: logURL)
        }
    }

    func clearActivity() {
        activity = []
        defaults.removeObject(forKey: Key.activity)
    }

    // MARK: Migration from the standalone app

    /// Runs once. If Automatic HEIC to JPEG was set up on this Mac, its destination and
    /// options are copied and the inbox is turned on (unless it was paused there).
    /// Otherwise the inbox stays off until it is turned on in Settings.
    private func migrateFromStandaloneIfNeeded() {
        guard !defaults.bool(forKey: Key.migrationChecked) else { return }
        defaults.set(true, forKey: Key.migrationChecked)
        let info = URL(fileURLWithPath: Self.standaloneAppPath).appendingPathComponent("Contents/Info.plist")
        let bundleID = (NSDictionary(contentsOf: info)?["CFBundleIdentifier"] as? String) ?? Self.standaloneBundleID
        guard let old = UserDefaults(suiteName: bundleID), old.bool(forKey: "setupDone") else { return }
        if old.object(forKey: "useDestination") != nil { useDestination = old.bool(forKey: "useDestination") }
        if let path = old.string(forKey: "destinationPath"), !path.isEmpty { destinationPath = path }
        if old.object(forKey: "convertHEIC") != nil { convert = old.bool(forKey: "convertHEIC") }
        if let mode = old.string(forKey: "originals").flatMap(OriginalsMode.init(rawValue:)) { originals = mode }
        if old.object(forKey: "jpegQuality") != nil { quality = min(1, max(0.5, old.double(forKey: "jpegQuality"))) }
        if old.object(forKey: "convertOtherHEIC") != nil { convertOtherHEIC = old.bool(forKey: "convertOtherHEIC") }
        // Keep its start date, so AirDrops it chose to leave alone stay "earlier" here too.
        if let since = old.object(forKey: "watchingSince") as? Date { watchingSince = since }
        enabled = !old.bool(forKey: "paused")
        migratedFrom = bundleID
        defaults.set(bundleID, forKey: Key.migratedFrom)
        record([AirDropInbox.Entry(kind: .moved, title: "Settings imported",
                                   detail: "From Automatic HEIC to JPEG (\(bundleID)); AirDrop Inbox \(enabled ? "on" : "off")")])
    }

    // MARK: Helpers for the UI

    func openPrivacySettings() {
        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_FilesAndFolders")!)
    }

    func openOutputFolder() {
        try? FileManager.default.createDirectory(at: outputFolder, withIntermediateDirectories: true)
        NSWorkspace.shared.open(outputFolder)
    }

    func openLog() {
        if FileManager.default.fileExists(atPath: logURL.path) { NSWorkspace.shared.open(logURL) }
    }

    func chooseDestination() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.prompt = "Use This Folder"
        panel.message = "Choose where AirDrops should go."
        panel.directoryURL = URL(fileURLWithPath: (destinationPath as NSString).expandingTildeInPath).deletingLastPathComponent()
        NSApp.activate()
        if panel.runModal() == .OK, let url = panel.url {
            destinationPath = url.path
            useDestination = true
        }
    }

    var statusText: String {
        switch status {
        case .off: return "Off"
        case .watching: return "Watching Downloads for AirDrops"
        case .needsAccess(let url): return "macOS hasn't allowed access to \((url.path as NSString).abbreviatingWithTildeInPath). Allow Easy Eject in Privacy & Security → Files and Folders."
        case .destinationMissing(let url): return "\((url.path as NSString).abbreviatingWithTildeInPath) isn't available. AirDrops stay in Downloads until it is."
        case .standaloneRunning: return "Automatic HEIC to JPEG is still running, so it handles AirDrops. Quit it to let Easy Eject take over."
        }
    }
}
#endif
