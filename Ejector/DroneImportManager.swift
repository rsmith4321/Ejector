import SwiftUI
import Combine
import DiskArbitration
import UserNotifications

nonisolated struct DroneProfile: Codable, Identifiable, Equatable, Sendable {
    var id: String
    var name: String
    var mediaPath: String
    var destinationBookmark: Data
    var destinationVolumeID: String
    var destinationLabel: String
    var sourceBookmark: Data? = nil
    var enabled = false
    var deleteOriginals = false
    var recoverTrash = false
    // Legacy persisted key; true now asks for confirmation and never ejects automatically.
    var autoEject = false
    var videosOnly: Bool? = nil
    var cleanLayout: Bool? = nil
    var includePreviews: Bool? = nil
}


@MainActor final class DroneImportManager: ObservableObject {
    static let shared = DroneImportManager()
    @Published var profiles: [DroneProfile] = []
    @Published var progress = ImportProgress(phase: "Ready")
    @Published var busy = false
    @Published var message = "Connect a camera, memory card, or FPV device to set up automatic imports."
    @Published var activeName = ""
    @Published var lastFolder: URL?
    @Published var hasError = false {
        didSet { isIssueDismissed = false }
    }
    @Published private(set) var isIssueDismissed = false
    var needsAttention: Bool { hasError && !isIssueDismissed }
    @Published var volumes: [URL] = []
    private var attempted = Set<String>()
    private var waiting = Set<String>()
    private var mountedIDs = Set<String>()
    private var completedEjectID: String?
    private var sourceRoot: URL?
    private var destinationRoot: URL?
    private var protectedPhysicalIDs = Set<String>()
    private var manualOperations = Set<String>()
    private var cancellation: ImportCancellation?
    private var observations = Set<AnyCancellable>()
    private let queue = DispatchQueue(label: "com.ryansmithphotography.Ejector.import", qos: .utility)
    private let support: URL

    init() {
        let supportName = Bundle.main.bundleIdentifier?.hasSuffix(".preview") == true ? "Easy Eject Preview" : "Easy Eject"
        support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent(supportName)
        do {
            try FileManager.default.createDirectory(at: support, withIntermediateDirectories: true)
            let file = support.appendingPathComponent("drone-profiles.json")
            if FileManager.default.fileExists(atPath: file.path) {
                profiles = try JSONDecoder().decode([DroneProfile].self, from: Data(contentsOf: file))
            }
        } catch { message = "Could not load import profiles: \(error.localizedDescription)"; hasError = true }
        let center = NSWorkspace.shared.notificationCenter
        center.publisher(for: NSWorkspace.didMountNotification)
            .merge(with: center.publisher(for: NSWorkspace.didUnmountNotification))
            .merge(with: center.publisher(for: NSWorkspace.didRenameVolumeNotification))
            .debounce(for: .milliseconds(700), scheduler: RunLoop.main)
            .sink { [weak self] _ in
                guard let self else { return }
                self.attempted.subtract(self.waiting); self.waiting.removeAll(); self.refresh()
            }.store(in: &observations)
        // Monitoring exists from app launch, even when the menu has never been opened.
        DispatchQueue.main.async { self.refresh() }
    }

    var menuTitle: String {
        if busy {
            if ["Importing", "Checking", "Verifying copy", "Verifying", "Imported"].contains(progress.phase) {
                return "Importing \(Int(progress.fraction * 100))%"
            }
            return progress.phase
        }
        return ""
    }

    /// Acknowledging a notice never retries an import or changes its saved options.
    /// Keep the explanation visible until the next operation replaces it.
    func dismissIssue() {
        guard !busy, hasError else { return }
        isIssueDismissed = true
    }

    func save(_ profile: DroneProfile) {
        guard !busy else { return }
        var updated = profiles
        if let i = updated.firstIndex(where: { $0.id == profile.id }) { updated[i] = profile }
        else { updated.append(profile) }
        persist(updated)
    }
    func remove(_ id: String) { guard !busy else { return }; persist(profiles.filter { $0.id != id }) }
    private func persist(_ updated: [DroneProfile]) {
        do {
            try JSONEncoder().encode(updated).write(to: support.appendingPathComponent("drone-profiles.json"), options: .atomic)
            profiles = updated
            // Saving settings never imports immediately. Use Import now or the next connection.
        } catch { message = "Cannot save settings: \(error.localizedDescription)"; hasError = true }
    }
    func cancel() { cancellation?.cancel(); message = "Stopping safely. Completed files are already saved." }
    func openFolder() { if let lastFolder { NSWorkspace.shared.open(lastFolder) } }
    func openLog() { NSWorkspace.shared.open(support.appendingPathComponent("imports.log")) }

    func blocksEject(_ url: URL) -> Bool {
        if busy {
            if url == sourceRoot || url == destinationRoot { return true }
            if let key = ImportVolumes.physicalID(url), protectedPhysicalIDs.contains(key) { return true }
        }
        return false
    }
    func reserveManualEject(_ url: URL) -> Bool {
        let key = ImportVolumes.physicalID(url) ?? url.path
        guard !blocksEject(url), !manualOperations.contains(key) else { return false }
        manualOperations.insert(key); return true
    }
    func recordOperationFailure(_ error: Error) {
        guard !busy else { LogManager.shared.log(error.localizedDescription); return }
        hasError = true; message = error.localizedDescription
        progress = ImportProgress(phase: "Needs attention")
    }
    func recordManualEject(_ volume: URL, error: Error?) {
        guard !busy else { return }
        hasError = error != nil
        message = error.map { "Could not eject \(volume.lastPathComponent): \($0.localizedDescription)" } ?? "\(volume.lastPathComponent): safe to unplug."
        // The volume may already be gone; reset success on the next mount event as well.
        progress = ImportProgress(phase: error == nil ? "Ejected" : "Needs attention")
    }
    func finishManualEject(_ key: String) { manualOperations.remove(key); refresh() }

    func refresh() {
        volumes = ImportVolumes.mounted().filter {
            guard $0.path.hasPrefix("/Volumes/"), let v = try? $0.resourceValues(forKeys: [.volumeIsInternalKey, .volumeIsRemovableKey, .volumeIsEjectableKey]) else { return false }
            return v.volumeIsInternal != true || v.volumeIsRemovable == true || v.volumeIsEjectable == true
        }
        let ids = Set(volumes.compactMap { try? ImportVolumes.identity($0) })
        if !busy, (completedEjectID.map { ids.contains($0) } ?? false) || (!ids.subtracting(mountedIDs).isEmpty && progress.phase == "Ejected") {
            self.completedEjectID = nil
            message = "Device connected again. Eject it before unplugging."
            progress = ImportProgress(phase: "Ready")
        }
        attempted.subtract(mountedIDs.subtracting(ids)); mountedIDs = ids
        guard !busy else { return }
        for profile in profiles where profile.enabled && !attempted.contains(profile.id) {
            if let source = volumes.first(where: { (try? ImportVolumes.identity($0)) == profile.id }) {
                start(profile, source: source)
                if busy { break }
            }
        }
    }

    func importNow(_ profile: DroneProfile) {
        guard !busy else { return }
        guard let source = volumes.first(where: { (try? ImportVolumes.identity($0)) == profile.id }) else {
            message = "Connect \(profile.name) first."; hasError = true
            progress = ImportProgress(phase: "Needs attention"); return
        }
        start(profile, source: source)
    }

    private func start(_ profile: DroneProfile, source: URL) {
        attempted.insert(profile.id)
        #if !APP_STORE
        let legacy = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/O4 Media Importer/enabled")
        if FileManager.default.fileExists(atPath: legacy.path) {
            message = "The standalone O4 importer is still enabled. Pause it before starting imports in Easy Eject."; hasError = true
            progress = ImportProgress(phase: "Needs attention"); return
        }
        #endif
        do {
            #if APP_STORE
            guard let bookmark = profile.sourceBookmark else { throw ImportFailure("Choose the media folder again to grant access.") }
            // Trash is outside the selected media folder. Never broaden access silently.
            let trashAccess = profile.recoverTrash ? try CardAccess.shared.open(source) : nil
            let sourceAccess = try ScopedFolder(bookmark)
            let destinationAccess = try ScopedFolder(profile.destinationBookmark)
            let media = sourceAccess.url
            let destination = destinationAccess.url
            guard try ImportVolumes.identity(media) == profile.id,
                  try ImportVolumes.root(media).standardizedFileURL == source.standardizedFileURL,
                  MediaImportEngine.isWithin(media, source) else {
                throw ImportFailure("The authorized media folder no longer belongs to this device. Choose it again.")
            }
            if sourceAccess.refreshedBookmark != nil || destinationAccess.refreshedBookmark != nil {
                var updated = profile
                updated.sourceBookmark = sourceAccess.refreshedBookmark ?? bookmark
                updated.destinationBookmark = destinationAccess.refreshedBookmark ?? profile.destinationBookmark
                updated.mediaPath = String(media.path.dropFirst(source.path.count + 1))
                updated.destinationLabel = destination.path
                save(updated)
                guard profiles.contains(updated) else { throw ImportFailure("Cannot save refreshed folder permissions. Import stopped.") }
            }
            #else
            var stale = false
            let destination = try URL(resolvingBookmarkData: profile.destinationBookmark, options: [.withoutUI, .withoutMounting], bookmarkDataIsStale: &stale)
            guard !stale else { throw ImportFailure("Choose the destination folder again to refresh its permission.") }
            let accessed = destination.startAccessingSecurityScopedResource()
            #endif
            do {
                let destinationVolume = try ImportVolumes.root(destination)
                guard try ImportVolumes.identity(destination) == profile.destinationVolumeID else { throw ImportFailure("The selected destination drive is unavailable or changed.") }
                guard try ImportVolumes.identity(source) != profile.destinationVolumeID,
                      ImportVolumes.physicalID(source) != ImportVolumes.physicalID(destinationVolume) else {
                    throw ImportFailure("Choose a destination on a different disk from the device.")
                }
                #if !APP_STORE
                let media = source.appendingPathComponent(profile.mediaPath).standardizedFileURL
                guard MediaImportEngine.isWithin(media, source),
                      !profile.mediaPath.split(separator: "/").contains(".."), !profile.mediaPath.hasPrefix("/") else {
                    throw ImportFailure("Choose a media folder inside the device.")
                }
                #endif
                let originalSourceDisk = ImportVolumes.physicalID(source)
                let diskKeys = Set([originalSourceDisk ?? source.path, ImportVolumes.physicalID(destinationVolume) ?? destinationVolume.path])
                guard manualOperations.isDisjoint(with: diskKeys) else { throw ImportFailure("A disk is being ejected. Reconnect it before importing.") }
                let releaseAccess: @Sendable () -> Void = {
                    #if APP_STORE
                    sourceAccess.close(); destinationAccess.close(); trashAccess?.close()
                    #else
                    if accessed { destination.stopAccessingSecurityScopedResource() }
                    #endif
                }
                sourceRoot = source; destinationRoot = destinationVolume; protectedPhysicalIDs = diskKeys
                busy = true; hasError = false; activeName = profile.name; message = "Preparing import"; progress = ImportProgress(phase: "Scanning")
                let token = ImportCancellation(); cancellation = token
                let logURL = support.appendingPathComponent("imports.log")
                queue.async {
                    let validate: () throws -> Void = {
                        try token.check()
                        guard try ImportVolumes.identity(source) == profile.id,
                              try ImportVolumes.root(source).standardizedFileURL == source.standardizedFileURL,
                              try ImportVolumes.identity(destination) == profile.destinationVolumeID,
                              try ImportVolumes.root(destination).standardizedFileURL == destinationVolume.standardizedFileURL else {
                            throw ImportFailure("A source or destination volume disconnected or changed. Remaining originals were kept.")
                        }
                    }
                    let audit: (String) throws -> Void = { line in
                        let text = "\(ISO8601DateFormatter().string(from: Date())) \(line)\n"
                        if !FileManager.default.fileExists(atPath: logURL.path) { FileManager.default.createFile(atPath: logURL.path, contents: nil) }
                        let h = try FileHandle(forWritingTo: logURL); defer { try? h.close() }
                        try h.seekToEnd(); try h.write(contentsOf: Data(text.utf8)); try h.synchronize()
                    }
                    var lastReport = Date.distantPast
                    var lastPhase = ""
                    let engine = MediaImportEngine(source: source, mediaFolder: media, destination: destination,
                        deleteOriginals: profile.deleteOriginals, recoverTrash: profile.recoverTrash, cancellation: token,
                        validate: validate, report: { value in
                            // A native UI update at most ten times per second keeps large transfers lightweight.
                            if value.phase != lastPhase || Date().timeIntervalSince(lastReport) >= 0.1 || value.completed == value.total {
                                lastReport = Date(); lastPhase = value.phase
                                DispatchQueue.main.async { self.progress = value }
                            }
                        }, audit: audit, videosOnly: profile.videosOnly ?? false, cleanLayout: profile.cleanLayout ?? false, sourceIdentifier: profile.id, includePreviews: profile.includePreviews ?? true)
                    do {
                        let result = try engine.run()
                        try validate()
                        try audit("Completed \(result.files) files, \(result.bytes) bytes")
                        DispatchQueue.main.async {
                            self.lastFolder = result.files > 0 ? result.folder : destination
                            let importNote = result.note.isEmpty ? "" : " " + result.note
                            let completion = result.hasSelectedMedia
                                ? "\(profile.name): \(result.files) files imported and verified."
                                : "\(profile.name): no media found matching your import options."
                            guard profile.autoEject else {
                                releaseAccess()
                                self.finish(error: nil, message: completion + " Device remains connected." + importNote)
                                return
                            }
                            do { try validate() }
                            catch {
                                releaseAccess()
                                self.finish(error: error, message: error.localizedDescription)
                                return
                            }
                            self.progress.phase = "Awaiting eject choice"
                            self.message = completion + " Choose whether to eject or keep the device connected."
                            let alert = ImportEjectPrompt.make(hasMedia: result.hasSelectedMedia, deviceName: profile.name)
                            NSApp.activate()
                            let response = alert.runModal()
                            guard response == .alertSecondButtonReturn else {
                                let connected = (try? ImportVolumes.identity(source)) == profile.id
                                releaseAccess()
                                self.finish(error: nil, message: completion + (connected ? " Device remains connected." : " Device is no longer connected.") + importNote)
                                return
                            }
                            do {
                                // The user may leave the prompt open while changing connected disks.
                                // Recheck after the choice; never eject a replacement at the old path.
                                try validate()
                                guard let originalSourceDisk,
                                      ImportVolumes.physicalID(source) == originalSourceDisk else {
                                    throw ImportFailure("The device's disk identity changed or could not be confirmed. Eject it from the menu after checking the connected device.")
                                }
                            } catch {
                                releaseAccess()
                                self.finish(error: error, message: error.localizedDescription)
                                return
                            }
                            self.progress.phase = "Ejecting"
                            FileManager.default.unmountVolume(at: source, options: [.allPartitionsAndEjectDisk, .withoutUI]) { error in
                                DispatchQueue.main.async {
                                    releaseAccess()
                                    if error == nil { self.completedEjectID = profile.id }
                                    self.finish(error: error, message: error == nil
                                        ? completion + " Safe to unplug." + importNote
                                        : completion + " The device could not eject: \(error!.localizedDescription)")
                                }
                            }
                        }
                    } catch {
                        try? audit("Stopped: \(error.localizedDescription)")
                        DispatchQueue.main.async {
                            releaseAccess()
                            self.finish(error: error, message: error.localizedDescription)
                        }
                    }
                }
            } catch {
                #if !APP_STORE
                if accessed { destination.stopAccessingSecurityScopedResource() }
                #endif
                throw error
            }
        } catch {
            waiting.insert(profile.id); hasError = true; activeName = profile.name
            progress = ImportProgress(phase: "Waiting"); message = error.localizedDescription
        }
    }

    private func finish(error: Error?, message: String) {
        busy = false; cancellation = nil; sourceRoot = nil; destinationRoot = nil; protectedPhysicalIDs.removeAll()
        hasError = error != nil; self.message = message; progress.phase = error == nil ? "Complete" : "Needs attention"
        LogManager.shared.log(message)
        if UserDefaults.standard.bool(forKey: "showEjectNotifications") {
            let content = UNMutableNotificationContent(); content.title = error == nil ? "Easy Eject import complete" : "Easy Eject import needs attention"
            content.body = message
            UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
        }
        refresh()
    }
}
