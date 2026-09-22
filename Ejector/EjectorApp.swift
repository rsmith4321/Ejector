//
//  EjectorApp.swift
//  Ejector
//

import SwiftUI
import Cocoa
import Combine
import DiskArbitration
import ServiceManagement
import UserNotifications

// MARK: - 0.5 Log Manager
class LogManager: ObservableObject {
    static let shared = LogManager()
    @Published var logs: String = ""
    private static let maxLines = 500

    private let formatter: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "HH:mm:ss"
        return f
    }()

    func log(_ message: String) {
        DispatchQueue.main.async {
            let timestamp = self.formatter.string(from: Date())
            self.logs += "[\(timestamp)] \(message)\n"
            print("[\(timestamp)] \(message)")

            let lines = self.logs.components(separatedBy: "\n")
            if lines.count > Self.maxLines {
                self.logs = lines.suffix(Self.maxLines).joined(separator: "\n")
            }
        }
    }
    
    func clear() {
        DispatchQueue.main.async {
            self.logs = ""
        }
    }
}

// MARK: - 2. Drive Manager (The Brains)
class DriveManager: NSObject, ObservableObject, UNUserNotificationCenterDelegate {
    static let shared = DriveManager()
    @Published var drives: [Drive] = []
    @Published private(set) var ejectOperations = 0
    @Published private(set) var bulkEjecting = false
    var isEjecting: Bool { ejectOperations > 0 || bulkEjecting }
    private var cancellables = Set<AnyCancellable>()

    private struct CachedClassification {
        let hasCameraStructure: Bool
        let isEmulatorCard: Bool
    }
    private var classificationCache: [String: CachedClassification] = [:]

    private var isDebugEnabled: Bool {
        UserDefaults.standard.bool(forKey: "enableDebugLogs")
    }
    
    override init() {
        super.init()

        UserDefaults.standard.register(defaults: ["showEjectNotifications": true])

        let center = NSWorkspace.shared.notificationCenter
        center.publisher(for: NSWorkspace.didMountNotification)
            .merge(with: center.publisher(for: NSWorkspace.didUnmountNotification))
            .merge(with: center.publisher(for: NSWorkspace.didRenameVolumeNotification))
            .debounce(for: .milliseconds(300), scheduler: RunLoop.main)
            .sink { [weak self] _ in
                self?.fetchDrives(clearCache: true)
            }
            .store(in: &cancellables)

        NotificationCenter.default.publisher(for: NSNotification.Name("TriggerGlobalEject"))
            .sink { [weak self] _ in
                self?.ejectAllCards(clean: UserDefaults.standard.bool(forKey: "cleanCardsOnEject"))
            }
            .store(in: &cancellables)

        DroneImportManager.shared.$profiles.dropFirst().sink { [weak self] _ in
            self?.fetchDrives(clearCache: true)
        }.store(in: &cancellables)
        NotificationCenter.default.publisher(for: .init("CardAccessChanged"))
            .sink { [weak self] _ in self?.fetchDrives(clearCache: true) }.store(in: &cancellables)

        if UserDefaults.standard.bool(forKey: "isShortcutEnabled") {
            GlobalHotkeyManager.shared.start()
        }

        self.fetchDrives()

        let notificationCenter = UNUserNotificationCenter.current()
        notificationCenter.delegate = self
        notificationCenter.requestAuthorization(options: [.alert, .sound]) { granted, error in
            if let error = error {
                LogManager.shared.log("❌ Notification auth error: \(error.localizedDescription)")
            } else if !granted {
                LogManager.shared.log("ℹ️ Notification permission not granted")
            }
        }
    }

    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification, withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner])
    }

    private func sendNotification(title: String, body: String) {
        guard UserDefaults.standard.bool(forKey: "showEjectNotifications") else { return }
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        content.sound = .default
        let request = UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil)
        UNUserNotificationCenter.current().add(request) { error in
            if let error = error {
                LogManager.shared.log("❌ Notification error: \(error.localizedDescription)")
            }
        }
    }
    
    private func diskDescription(for volumeURL: URL) -> [String: Any]? {
        guard let session = DASessionCreate(kCFAllocatorDefault),
              let disk = DADiskCreateFromVolumePath(kCFAllocatorDefault, session, volumeURL as CFURL),
              let desc = DADiskCopyDescription(disk) as? [String: Any] else {
            return nil
        }
        return desc
    }

    private func detectCardType(from desc: [String: Any]?) -> CardType {
        guard let desc = desc else { return .unknown }

        let model = (desc[kDADiskDescriptionDeviceModelKey as String] as? String) ?? ""
        let vendor = (desc[kDADiskDescriptionDeviceVendorKey as String] as? String) ?? ""
        let proto = (desc[kDADiskDescriptionDeviceProtocolKey as String] as? String) ?? ""
        let bus = (desc[kDADiskDescriptionBusNameKey as String] as? String) ?? ""

        let haystack = (model + " " + vendor + " " + proto + " " + bus).lowercased()

        if haystack.contains("secure digital") || haystack.contains("sdxc") || haystack.contains("sdhc") || haystack.contains(" sd ") || bus.lowercased() == "sd" {
            return .sd
        }
        if haystack.contains("cfexpress") { return .cfexpress }
        if haystack.contains("xqd") { return .xqd }

        return .unknown
    }
    
    func fetchDrives(clearCache: Bool = false) {
        if clearCache {
            classificationCache.removeAll()
        }
        let keys: [URLResourceKey] = [.volumeNameKey, .volumeIsRemovableKey, .volumeIsEjectableKey, .volumeIsInternalKey]
        
        guard let paths = FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: keys, options: [.skipHiddenVolumes]) else {
            return
        }
        
        var foundDrives: [Drive] = []
        
        if isDebugEnabled {
            LogManager.shared.log("--- Starting Drive Scan ---")
        }
        
        for url in paths {
            guard let components = try? url.resourceValues(forKeys: Set(keys)) else { continue }
            
            let isEjectable = components.volumeIsEjectable ?? false
            let isRemovable = components.volumeIsRemovable ?? false
            let isInternal = components.volumeIsInternal ?? false
            let name = components.volumeName ?? url.lastPathComponent
            
            if isDebugEnabled {
                LogManager.shared.log("🔎 Inspecting: \(name)")
                LogManager.shared.log("   Path: \(url.path)")
                LogManager.shared.log("   Internal: \(isInternal) | Removable: \(isRemovable) | Ejectable: \(isEjectable)")
            }
            
            if isInternal && !isRemovable && !isEjectable {
                if isDebugEnabled {
                    LogManager.shared.log("   ❌ Skipped (Internal System Drive)")
                    LogManager.shared.log("-------------------------------------------------")
                }
                continue
            }
            
            if url.path == "/" { continue }
            let isUnderVolumes = url.path.hasPrefix("/Volumes/")
            guard isUnderVolumes else { continue }
            
            let desc = self.diskDescription(for: url)
            let hardwareType = self.detectCardType(from: desc)
            let proto = (desc?[kDADiskDescriptionDeviceProtocolKey as String] as? String) ?? ""
            if isDebugEnabled {
                let model = (desc?[kDADiskDescriptionDeviceModelKey as String] as? String) ?? "(none)"
                let vendor = (desc?[kDADiskDescriptionDeviceVendorKey as String] as? String) ?? "(none)"
                let bus = (desc?[kDADiskDescriptionBusNameKey as String] as? String) ?? "(none)"
                LogManager.shared.log("   Hardware: Model: \(model) | Vendor: \(vendor) | Protocol: \(proto.isEmpty ? "(none)" : proto) | Bus: \(bus)")
            }
            let isHardwareCamera = (hardwareType == .sd || hardwareType == .cfexpress || hardwareType == .xqd)

            let classificationKey = (try? ImportVolumes.identity(url)) ?? url.path
            let protoLower = proto.lowercased()
            let canBeCard = isHardwareCamera || (isInternal && isRemovable) || protoLower.contains("pci") || protoLower.contains("secure digital") || isEjectable

            var hasCameraStructure = false
            var isEmulatorCard = false
            #if APP_STORE
            let readAccess = try? CardAccess.shared.open(url)
            defer { readAccess?.close() }
            let mayInspectFolders = readAccess != nil
            #else
            let mayInspectFolders = true
            #endif
            if canBeCard && mayInspectFolders {
                if let cached = classificationCache[classificationKey] {
                    hasCameraStructure = cached.hasCameraStructure
                    isEmulatorCard = cached.isEmulatorCard
                    if isDebugEnabled {
                        LogManager.shared.log("   📦 Using cached classification")
                    }
                } else {
                    let cameraFolderNames = [
                        "DCIM", "PRIVATE", "AVCHD", "MP_ROOT",
                        "XDROOT", "BPAV", "NIKON", "CANONMSC", "FUJI", "GOPRO", "SONY"
                    ]
                    hasCameraStructure = cameraFolderNames.contains { folder in
                        let folderURL = url.appendingPathComponent(folder, isDirectory: true)
                        var isDirectory: ObjCBool = false
                        return FileManager.default.fileExists(atPath: folderURL.path, isDirectory: &isDirectory) && isDirectory.boolValue
                    }

                    if hardwareType == .sd {
                        let emulatorFolderNames = ["roms", "retroarch", "bios", ".emulationstation"]
                        let dirContents = (try? FileManager.default.contentsOfDirectory(atPath: url.path)) ?? []
                        let dirContentsLower = Set(dirContents.map { $0.lowercased() })
                        let emulatorHits = emulatorFolderNames.filter { dirContentsLower.contains($0) }.count
                        isEmulatorCard = emulatorHits >= 2
                    }

                    classificationCache[classificationKey] = CachedClassification(hasCameraStructure: hasCameraStructure, isEmulatorCard: isEmulatorCard)
                }
            }
            let isCameraCard = !isEmulatorCard && (isHardwareCamera || hasCameraStructure || (isInternal && isRemovable))

            var finalCardType: CardType? = isCameraCard ? hardwareType : nil
            if isCameraCard && hardwareType == .unknown {
                if isInternal && isRemovable {
                    finalCardType = .sd
                } else if hasCameraStructure && protoLower.contains("pci") {
                    finalCardType = .cfexpress
                }
            }

            if isDebugEnabled {
                if isCameraCard {
                    LogManager.shared.log("   📸 Classified as Camera Card (\(finalCardType?.rawValue ?? "Unknown Format"))")
                } else if isEmulatorCard {
                    LogManager.shared.log("   🎮 Classified as Emulator Card")
                } else {
                    LogManager.shared.log("   💾 Classified as Standard External Volume")
                }
                LogManager.shared.log("-------------------------------------------------")
            }

            foundDrives.append(Drive(name: name, url: url, isCameraCard: isCameraCard, isEmulatorCard: isEmulatorCard, cardType: finalCardType, isEjectable: isEjectable, isRemovable: isRemovable, isInternal: isInternal))
        }
        
        let mountedPaths = Set(paths.map { (try? ImportVolumes.identity($0)) ?? $0.path })
        classificationCache = classificationCache.filter { mountedPaths.contains($0.key) }

        foundDrives.sort { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
        
        DispatchQueue.main.async {
            self.drives = foundDrives
            let cards = foundDrives.filter { $0.isCameraCard || $0.isEmulatorCard }
            UserDefaults.standard.set(Set(cards.map { ImportVolumes.physicalID($0.url) ?? $0.url.path }).count, forKey: "cameraCardCount")
        }
    }
    
    // MARK: - 2.5 Metadata Cleanup Logic

    func eject(drive: Drive, clean: Bool = false, notify: Bool = true,
               cleanupCards: [Drive]? = nil, completion: ((Bool, Int) -> Void)? = nil) {
        let roots = cleanupCards ?? [drive]
        // Obtain all permissions before reserving the disk or deleting anything.
        #if APP_STORE
        let accesses: [ScopedFolder]
        do { accesses = clean ? try roots.map { try CardAccess.shared.require($0.url) } : [] }
        catch { operationFailed(error); completion?(false, 0); return }
        #endif
        guard DroneImportManager.shared.reserveManualEject(drive.url) else {
            #if APP_STORE
            accesses.forEach { $0.close() }
            #endif
            operationFailed(ImportFailure("Disk is busy importing or ejecting.")); completion?(false, 0); return
        }
        let operationKey = ImportVolumes.physicalID(drive.url) ?? drive.url.path
        let identities: [(URL, String)]
        do { identities = try roots.map { ($0.url, try ImportVolumes.identity($0.url)) } }
        catch {
            #if APP_STORE
            accesses.forEach { $0.close() }
            #endif
            DroneImportManager.shared.finishManualEject(operationKey)
            operationFailed(error); completion?(false, 0); return
        }
        ejectOperations += 1
        DispatchQueue.global(qos: .userInitiated).async {
            var cleanedCount = 0
            let releaseAccess: () -> Void = {
                #if APP_STORE
                accesses.forEach { $0.close() }
                #endif
            }
            do {
                let validate: () throws -> Void = {
                    for (root, expected) in identities {
                        guard try ImportVolumes.identity(root) == expected,
                              try ImportVolumes.root(root).standardizedFileURL == root.standardizedFileURL,
                              (ImportVolumes.physicalID(root) ?? root.path) == operationKey else {
                            throw ImportFailure("The mounted disk changed. Cleanup and eject stopped.")
                        }
                    }
                }
                try validate()
                if clean {
                    for (root, _) in identities { cleanedCount += try MetadataCleaner.clean(root, validate: validate) }
                }
                try validate()
            } catch {
                releaseAccess()
                DispatchQueue.main.async {
                    self.operationFailed(error)
                    DroneImportManager.shared.finishManualEject(operationKey)
                    self.ejectOperations -= 1
                    completion?(false, cleanedCount)
                }
                return
            }
            FileManager.default.unmountVolume(at: drive.url, options: [.allPartitionsAndEjectDisk, .withoutUI]) { error in
                releaseAccess()
                DispatchQueue.main.async {
                    DroneImportManager.shared.recordManualEject(drive.url, error: error)
                    if let error {
                        LogManager.shared.log("Could not eject \(drive.name): \(error.localizedDescription)")
                        if notify { self.sendNotification(title: "Could not eject \(drive.name)", body: error.localizedDescription) }
                    } else {
                        self.fetchDrives(clearCache: true)
                        let body = clean ? "Removed \(cleanedCount) metadata files and ejected \(drive.name)." : "Ejected \(drive.name)."
                        LogManager.shared.log(body)
                        if notify { self.sendNotification(title: "Easy Eject", body: body) }
                    }
                    DroneImportManager.shared.finishManualEject(operationKey)
                    self.ejectOperations -= 1
                    completion?(error == nil, cleanedCount)
                }
            }
        }
    }

    private func operationFailed(_ error: Error) {
        LogManager.shared.log(error.localizedDescription)
        DroneImportManager.shared.recordOperationFailure(error)
        sendNotification(title: "Drive needs attention", body: error.localizedDescription)
    }

    func ejectAllCards(clean: Bool = false) {
        guard !isEjecting else { return }
        let groups: [[Drive]]
        #if APP_STORE
        let preflightAccess: [ScopedFolder]
        #endif
        do {
            let plan = try BulkEjectPolicy.plan(drives.map { drive in
                .init(id: drive.id, disk: ImportVolumes.physicalID(drive.url),
                      isCard: drive.isCameraCard || drive.isEmulatorCard,
                      blocked: DroneImportManager.shared.blocksEject(drive.url))
            })
            groups = plan.map { ids in drives.filter { ids.contains($0.id) } }
            #if APP_STORE
            preflightAccess = clean ? try groups.flatMap { $0 }.map { try CardAccess.shared.require($0.url) } : []
            #endif
        } catch { operationFailed(error); return }
        guard !groups.isEmpty else { return }
        bulkEjecting = true
        var ejected = 0
        var cleaned = 0
        func next(_ index: Int) {
            guard index < groups.count else {
                #if APP_STORE
                preflightAccess.forEach { $0.close() }
                #endif
                bulkEjecting = false
                let summary = "Ejected \(ejected) of \(groups.count) card disks. Removed \(cleaned) metadata files."
                LogManager.shared.log(summary)
                sendNotification(title: "Easy Eject", body: summary)
                return
            }
            let group = groups[index]
            eject(drive: group[0], clean: clean, notify: false, cleanupCards: group) { success, count in
                if success { ejected += 1 }
                cleaned += count
                // Stop a batch on failure; never hide partial success.
                if !success {
                    #if APP_STORE
                    preflightAccess.forEach { $0.close() }
                    #endif
                    self.bulkEjecting = false
                    self.sendNotification(title: "Eject all stopped", body: "Ejected \(ejected) of \(groups.count) disks before an error. Review the import window or log.")
                    return
                }
                next(index + 1)
            }
        }
        next(0)
    }

}

// MARK: - 3. The Debug Window View
struct DebugLogView: View {
    @ObservedObject private var logManager = LogManager.shared
    
    var body: some View {
        VStack(spacing: 0) {
            ScrollView {
                Text(logManager.logs.isEmpty ? "No logs captured yet. Try hitting 'Refresh List' from the menu bar." : logManager.logs)
                    .font(.system(.body, design: .monospaced))
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding()
                    .textSelection(.enabled)
            }
            .background(Color(NSColor.textBackgroundColor))
            
            Divider()
            
            HStack {
                Button("Clear") {
                    logManager.clear()
                }
                Spacer()
                Button("Copy to Clipboard") {
                    let pasteboard = NSPasteboard.general
                    pasteboard.clearContents()
                    pasteboard.setString(logManager.logs, forType: .string)
                }
                .keyboardShortcut("c", modifiers: [.command])
            }
            .padding()
            .background(Color(NSColor.windowBackgroundColor))
        }
        .frame(minWidth: 500, minHeight: 400)
    }
}

// MARK: - 6. The App Entry Point
@main
struct EjectorApp: App {
    @NSApplicationDelegateAdaptor(EjectorLifecycle.self) private var lifecycle
    @StateObject private var importer = DroneImportManager.shared
    var body: some Scene {
        Window("Air Unit & Camera Imports", id: "importsWindow") {
            ImportsWindow(lifecycle: lifecycle, importer: importer)
        }.defaultSize(width: 650, height: 690)
        #if !APP_STORE
        Window("Welcome to Easy Eject", id: "welcomeWindow") { WelcomeView() }
            .defaultSize(width: 480, height: 420).defaultPosition(.center)
        #endif
        Window("Easy Eject: Help & Instructions", id: "helpWindow") { HelpView(lifecycle: lifecycle) }
            .defaultSize(width: 600, height: 690)
        Window("Easy Eject Debug Logs", id: "debugWindow") { DebugLogView() }
            .defaultSize(width: 550, height: 400)
        Settings { SettingsView() }
    }
}

private struct ImportsWindow: View {
    let lifecycle: EjectorLifecycle
    @ObservedObject var importer: DroneImportManager
    @Environment(\.openWindow) private var openWindow
    @Environment(\.openSettings) private var openSettings
    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Button("Eject menu") { lifecycle.showMenu() }
                SettingsLink { Text("Settings…") }
                Spacer()
            }.padding([.horizontal, .top], 16)
            DroneImportView(importer: importer)
        }.onAppear {
            lifecycle.configure(openWindow: { id in openWindow(id: id) }, openSettings: { openSettings() })
        }
    }
}

// MARK: - 7. Help View
struct HelpView: View {
    let lifecycle: EjectorLifecycle
    @Environment(\.dismiss) var dismiss

    var body: some View {
        VStack(spacing: 0) {
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {

                    helpSection("Start Here: Eject Your Card", icon: "eject.fill") {
                        Text("Click the eject icon in your Mac's menu bar, choose your card, and wait for ejection to succeed before unplugging. Import profiles are optional.")
                        Button("Open Eject Menu") { lifecycle.showMenu() }
                    }
                    #if APP_STORE
                    helpSection("Folder Authorization", icon: "folder.badge.plus") {
                        FolderAuthorizationInstructions()
                        Button("Authorize a Card…") { CardAccess.shared.authorize() }
                    }
                    #endif

                    helpSection("Smart Sorting", icon: "sdcard") {
                        Text("Automatically detects camera folders (DCIM, GOPRO, NIKON, etc.) and card reader hardware (SD, CFexpress, XQD) to separate media cards from permanent SSDs. Especially helpful for CFexpress cards, which macOS often mistakes for standard hard drives.")
                        Text("Note: macOS does not identify CFexpress cards directly. Easy Eject infers CFexpress by detecting camera folders on a PCI-Express/NVMe drive. In rare cases, a non-CFexpress NVMe drive with camera folders may be labeled as CFexpress.")
                            .padding(.top, 2)
                    }

                    helpSection("Smart Grouping", icon: "rectangle.3.group") {
                        Text("Many cameras label all cards as \"Untitled.\" Easy Eject displays the card type next to the name: e.g., \"Untitled (CFexpress)\" and \"Untitled (SD)\": so you always know which card you're ejecting.")
                    }

                    helpSection("Camera Cards", icon: "eject") {
                        Text("Camera cards (SD, CFexpress, XQD) appear as single buttons for fast ejection. Enable \"Clean Cards Before Ejecting\" in Settings to automatically remove hidden macOS junk files before ejecting.")
                    }

                    helpSection("Emulator Cards", icon: "gamecontroller") {
                        Text("SD cards with emulator folder structures (Roms, RetroArch, BIOS, EmulationStation) are automatically detected and grouped separately from camera cards. Clean & Eject removes ._ files that cause phantom game entries in ROM libraries.")
                    }

                    helpSection("Clean & Eject", icon: "sparkles") {
                        Text("Removes invisible macOS files that cause problems on other systems:")
                            .padding(.bottom, 2)
                        Text("• .**_ AppleDouble files**: the #1 cause of ghost games on retro consoles\n• **.DS_Store**: Mac folder settings that clutter Windows and Linux\n• **Empty __MACOSX folders**: removes the folder only when empty; keeps user files\n• **.apdisk**: Apple disk identification files created for network sharing")
                        Text("Non-camera drives (SSDs, thumb drives) show a submenu with both Eject and Clean & Eject options.")
                            .padding(.top, 2)
                        Text("If your card has thousands of metadata files, cleaning may take several seconds. You'll receive a notification when it's done.")
                            .padding(.top, 2)
                    }

                    helpSection("Keyboard Shortcut", icon: "keyboard") {
                        Text("Press ⌃⌥⌘ + your chosen letter to instantly eject all camera and emulator cards from any app. Configure the shortcut key in Settings.")
                    }

                    helpSection("Notifications", icon: "bell") {
                        Text("A macOS notification confirms each eject and shows how many hidden files were cleaned. Useful when ejecting via the keyboard shortcut from inside another app. Toggle in Settings.")
                    }

                    helpSection("Menu Bar Badge", icon: "number.circle") {
                        Text("When recognized camera or emulator cards are connected, the menu bar counts each physical disk once. Progress and errors take priority.")
                    }

                    helpSection("Permissions", icon: "lock.shield") {
                        VStack(alignment: .leading, spacing: 6) {
                            Text("The keyboard shortcut uses a registered key combination and does not require Accessibility access.")
                            #if APP_STORE
                            Text("Choose Authorize a Card to grant access for folder detection and Clean & Eject. Permission is remembered for this volume; formatting it requires authorization again. Imported media folders have separate permissions.")
                            #else
                            Text("Full Disk Access may be needed when macOS denies access to files. Enable it in System Settings > Privacy & Security if required.")
                            #endif
                        }
                    }

                    helpSection("Refresh List", icon: "arrow.clockwise") {
                        Text("Easy Eject remembers how it classified each drive to avoid repeatedly waking spinning disks. If a drive isn't showing up correctly, click \"Refresh List\" to clear the cache and re-scan all connected drives.")
                    }

                    helpSection("Debug Logging", icon: "ladybug") {
                        Text("Enable in Settings to see how your Mac identifies each drive. Use \"Show Debug Window\" to view detailed logs for troubleshooting.")
                    }
                }
                .padding(24)
            }

            Divider()

            HStack {
                Button("Visit Website") {
                    if let url = URL(string: "https://easyeject.com/") {
                        NSWorkspace.shared.open(url)
                    }
                }
                Spacer()
                Button("Done") {
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
            }
            .padding(16)
        }
    }

    @ViewBuilder
    private func helpSection(_ title: String, icon: String, @ViewBuilder content: () -> some View) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Label(title, systemImage: icon)
                .font(.headline)
            content()
                .font(.callout)
                .foregroundColor(.secondary)
        }
    }
}

#if !APP_STORE
// MARK: - 8. Welcome & Disclaimer View
struct WelcomeView: View {
    @AppStorage("hasAcceptedDisclaimer") private var hasAcceptedDisclaimer = false
    @Environment(\.dismiss) var dismiss
    
    @State private var hasReadToBottom = false

    var body: some View {
        VStack(spacing: 20) {
            Image(systemName: "eject.circle.fill")
                .resizable()
                .frame(width: 60, height: 60)
                .foregroundColor(.blue)

            Text("Welcome to Easy Eject")
                .font(.title)
                .fontWeight(.bold)

            ScrollView {
                LazyVStack(alignment: .leading, spacing: 10) {
                    Text("""
                    Easy Eject lives in your menu bar and makes it easy to safely eject camera cards, emulator cards, and external drives.

                    **How it works:**
                    • Click the ⏏ icon in your menu bar to see all connected drives
                    • Camera cards (SD, CFexpress, XQD) are automatically detected and grouped
                    • Emulator cards (RetroArch, EmulationStation, ROM libraries) are detected and grouped separately
                    • Cards display their type (e.g., "Untitled (CFexpress)") so you can tell them apart
                    • One click to eject, or eject all cards at once
                    • Enable "Clean & Eject" in Settings to remove hidden macOS junk files (.DS_Store, ._ files, .apdisk, __MACOSX) that cause errors on cameras, emulators, and PCs
                    • Set up a keyboard shortcut to eject from any app

                    **Important Disclaimer:**
                    This software is provided "as is", without warranty of any kind, express or implied. In no event shall the developer be liable for any claim, damages, or other liability, including but not limited to data loss or hardware issues, arising from the use of this software.

                    The "Clean & Eject" feature involves the automated deletion of hidden macOS metadata files. Please ensure you have backups of your critical data before using this utility. If your card contains thousands of metadata files, cleaning may take several seconds: you'll receive a notification when it's done.
                    """)
                    .font(.subheadline)
                    .multilineTextAlignment(.leading)
                    
                    Spacer().frame(height: 20)

                    Color.clear
                        .frame(height: 1)
                        .onAppear {
                            hasReadToBottom = true
                        }
                }
                .padding()
            }
            .frame(height: 160)
            .background(Color(NSColor.textBackgroundColor).opacity(0.5))
            .cornerRadius(8)
            .overlay(
                RoundedRectangle(cornerRadius: 8)
                    .stroke(Color.secondary.opacity(0.3), lineWidth: 1)
            )

            Button(hasReadToBottom ? "I Accept & Understand" : "Please scroll to the bottom...") {
                hasAcceptedDisclaimer = true
                dismiss() // Closes the welcome window
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            .disabled(!hasReadToBottom) // Disable until scrolled
            .padding(.bottom, 10)
        }
        .padding(30)
    }
}
#endif
