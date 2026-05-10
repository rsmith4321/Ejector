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

// MARK: - 0. Global Hotkey Manager
class GlobalHotkeyManager {
    static let shared = GlobalHotkeyManager()
    
    private var eventTap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?
    
    func isTrusted(promptSystem: Bool) -> Bool {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: promptSystem] as CFDictionary
        return AXIsProcessTrustedWithOptions(options)
    }
    
    func start() {
        guard isTrusted(promptSystem: false) else { return }
        
        // If it's already running, stop it first so we can restart with a new key
        if eventTap != nil { stop() }
        
        let eventMask = (1 << CGEventType.keyDown.rawValue)
        guard let tap = CGEvent.tapCreate(
            tap: .cgSessionEventTap,
            place: .headInsertEventTap,
            options: .defaultTap,
            eventsOfInterest: CGEventMask(eventMask),
            callback: { (proxy, type, event, refcon) -> Unmanaged<CGEvent>? in
                
                let keyCode = event.getIntegerValueField(.keyboardEventKeycode)
                let flags = event.flags
                
                // THE FIX: Read the user's chosen keycode INSIDE the closure
                // to avoid capturing external context.
                let savedKey = UserDefaults.standard.integer(forKey: "shortcutKeyCode")
                let targetKeyCode = savedKey == 0 ? 14 : savedKey
                
                // Compare against our dynamic targetKeyCode
                if flags.contains(.maskCommand) && flags.contains(.maskControl) && flags.contains(.maskAlternate) && keyCode == targetKeyCode {
                    DispatchQueue.main.async {
                        NotificationCenter.default.post(name: NSNotification.Name("TriggerGlobalEject"), object: nil)
                    }
                    return nil // Swallow the key
                }
                return Unmanaged.passUnretained(event)
            }, userInfo: nil) else { return }
        
        self.eventTap = tap
        self.runLoopSource = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        
        if let runLoopSource = self.runLoopSource {
            CFRunLoopAddSource(CFRunLoopGetCurrent(), runLoopSource, .commonModes)
            CGEvent.tapEnable(tap: tap, enable: true)
            LogManager.shared.log("✅ Global shortcut activated.")
        }
    }
    
    func stop() {
        if let tap = eventTap, let source = runLoopSource {
            CGEvent.tapEnable(tap: tap, enable: false)
            CFRunLoopRemoveSource(CFRunLoopGetCurrent(), source, .commonModes)
            LogManager.shared.log("ℹ️ Global shortcut disabled.")
        }
        eventTap = nil
        runLoopSource = nil
    }
}

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

// MARK: - Card Type Classification
enum CardType: String {
    case sd = "SD"
    case cfexpress = "CFexpress"
    case xqd = "XQD"
    case unknown = "Unknown"
}

// MARK: - 1. Drive Model
struct Drive: Identifiable {
    var id: String { url.path }
    let name: String
    let url: URL
    let isCameraCard: Bool
    let isEmulatorCard: Bool
    let cardType: CardType?
    let isEjectable: Bool
    let isRemovable: Bool
    let isInternal: Bool

    var displayName: String {
        if let cardType = cardType, cardType != .unknown {
            return "\(name) (\(cardType.rawValue))"
        }
        return name
    }

    var iconName: String {
        if isCameraCard {
            return "sdcard"
        } else if isEmulatorCard {
            return "gamecontroller"
        } else {
            return "externaldrive"
        }
    }
}

// MARK: - 2. Drive Manager (The Brains)
class DriveManager: NSObject, ObservableObject, UNUserNotificationCenterDelegate {
    @Published var drives: [Drive] = []
    private var cancellables = Set<AnyCancellable>()
    
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
                self?.fetchDrives()
            }
            .store(in: &cancellables)

        NotificationCenter.default.publisher(for: NSNotification.Name("TriggerGlobalEject"))
            .sink { [weak self] _ in
                self?.ejectAllCards()
            }
            .store(in: &cancellables)

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
    
    private func detectCardType(for volumeURL: URL) -> CardType {
        guard let session = DASessionCreate(kCFAllocatorDefault),
              let disk = DADiskCreateFromVolumePath(kCFAllocatorDefault, session, volumeURL as CFURL),
              let desc = DADiskCopyDescription(disk) as? [String: Any] else {
            return .unknown
        }
        
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
    
    func fetchDrives() {
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
            
            let cameraFolderNames = [
                "DCIM", "PRIVATE", "MISC", "AVCHD", "MP_ROOT", "CONTENTS",
                "XDROOT", "BPAV", "NIKON", "CANONMSC", "FUJI", "GOPRO", "SONY"
            ]
            let hasCameraStructure = cameraFolderNames.contains { folder in
                let folderURL = url.appendingPathComponent(folder, isDirectory: true)
                return FileManager.default.fileExists(atPath: folderURL.path)
            }

            let emulatorFolderNames = ["roms", "retroarch", "bios", ".emulationstation"]
            let dirContents = (try? FileManager.default.contentsOfDirectory(atPath: url.path)) ?? []
            let dirContentsLower = Set(dirContents.map { $0.lowercased() })
            let emulatorHits = emulatorFolderNames.filter { dirContentsLower.contains($0) }.count
            let hasEmulatorStructure = emulatorHits >= 2

            let hardwareType = self.detectCardType(for: url)
            if isDebugEnabled {
                if let session = DASessionCreate(kCFAllocatorDefault),
                   let disk = DADiskCreateFromVolumePath(kCFAllocatorDefault, session, url as CFURL),
                   let desc = DADiskCopyDescription(disk) as? [String: Any] {
                    let model = (desc[kDADiskDescriptionDeviceModelKey as String] as? String) ?? "(none)"
                    let vendor = (desc[kDADiskDescriptionDeviceVendorKey as String] as? String) ?? "(none)"
                    let proto = (desc[kDADiskDescriptionDeviceProtocolKey as String] as? String) ?? "(none)"
                    let bus = (desc[kDADiskDescriptionBusNameKey as String] as? String) ?? "(none)"
                    LogManager.shared.log("   Hardware — Model: \(model) | Vendor: \(vendor) | Protocol: \(proto) | Bus: \(bus)")
                }
            }
            let isHardwareCamera = (hardwareType == .sd || hardwareType == .cfexpress || hardwareType == .xqd)

            let isEmulatorCard = hasEmulatorStructure && hardwareType == .sd
            let isCameraCard = !isEmulatorCard && (isHardwareCamera || hasCameraStructure || (isInternal && isRemovable))

            var finalCardType: CardType? = isCameraCard ? hardwareType : nil
            if isCameraCard && hardwareType == .unknown {
                if isInternal && isRemovable {
                    finalCardType = .sd
                } else if hasCameraStructure {
                    if let session = DASessionCreate(kCFAllocatorDefault),
                       let disk = DADiskCreateFromVolumePath(kCFAllocatorDefault, session, url as CFURL),
                       let desc = DADiskCopyDescription(disk) as? [String: Any] {
                        let proto = (desc[kDADiskDescriptionDeviceProtocolKey as String] as? String) ?? ""
                        if proto.lowercased().contains("pci") {
                            finalCardType = .cfexpress
                        }
                    }
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
        
        foundDrives.sort { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
        
        DispatchQueue.main.async {
            self.drives = foundDrives
            UserDefaults.standard.set(foundDrives.filter { $0.isCameraCard || $0.isEmulatorCard }.count, forKey: "cameraCardCount")
        }
    }
    
    // MARK: - 2.5 Metadata Cleanup Logic

    // Uses POSIX readdir to list filenames in a directory, bypassing
    // macOS's filtering of ._ AppleDouble files on FAT/exFAT volumes.
    private func rawFileNames(in path: String) -> [String] {
        guard let dir = opendir(path) else { return [] }
        defer { closedir(dir) }
        var names: [String] = []
        while let entry = readdir(dir) {
            let name = withUnsafePointer(to: entry.pointee.d_name) {
                String(cString: UnsafeRawPointer($0).assumingMemoryBound(to: CChar.self))
            }
            if name != "." && name != ".." { names.append(name) }
        }
        return names
    }

    @discardableResult
    private func cleanHiddenMetadata(at volURL: URL) -> Int {
        let fileManager = FileManager.default
        let skipDirectories: Set<String> = [".Spotlight-V100", ".Trashes", ".fseventsd", ".TemporaryItems"]
        var deletedCount = 0

        if self.isDebugEnabled { LogManager.shared.log("🧹 Starting metadata cleanup for: \(volURL.lastPathComponent)") }

        // Walk the volume tree, deleting .DS_Store and __MACOSX and collecting directories.
        var directories: [URL] = [volURL]
        let keys: [URLResourceKey] = [.isDirectoryKey]
        guard let enumerator = fileManager.enumerator(at: volURL, includingPropertiesForKeys: keys, options: [.skipsPackageDescendants]) else { return 0 }

        for case let fileURL as URL in enumerator {
            let fileName = fileURL.lastPathComponent

            if skipDirectories.contains(fileName) {
                enumerator.skipDescendants()
                continue
            }

            if let isDir = try? fileURL.resourceValues(forKeys: [.isDirectoryKey]).isDirectory, isDir {
                directories.append(fileURL)
            }

            if fileName == ".DS_Store" || fileName == "__MACOSX" || fileName == ".apdisk" {
                do {
                    try fileManager.removeItem(at: fileURL)
                    deletedCount += 1
                    if self.isDebugEnabled { LogManager.shared.log("🗑️ Deleted: \(fileName)") }
                } catch {
                    if self.isDebugEnabled { LogManager.shared.log("⚠️ Could not delete \(fileName): \(error.localizedDescription)") }
                }
            }
        }

        // macOS hides ._ AppleDouble files from FileManager on FAT/exFAT.
        // Use POSIX readdir on each directory to find them all, including orphans.
        for dirURL in directories {
            for name in rawFileNames(in: dirURL.path) where name.hasPrefix("._") {
                let fileURL = dirURL.appendingPathComponent(name)
                do {
                    try fileManager.removeItem(at: fileURL)
                    deletedCount += 1
                    if self.isDebugEnabled { LogManager.shared.log("🗑️ Deleted: \(name)") }
                } catch {
                    if self.isDebugEnabled { LogManager.shared.log("⚠️ Could not delete \(name): \(error.localizedDescription)") }
                }
            }
        }

        if self.isDebugEnabled { LogManager.shared.log("✨ Cleanup complete. Removed \(deletedCount) file\(deletedCount == 1 ? "" : "s").") }
        return deletedCount
    }
    
    func eject(drive: Drive, clean: Bool = false, notify: Bool = true, completion: ((Bool, Int) -> Void)? = nil) {
        DispatchQueue.global(qos: .userInitiated).async {
            var cleanedCount = 0
            if clean {
                cleanedCount = self.cleanHiddenMetadata(at: drive.url)
                if self.isDebugEnabled { LogManager.shared.log("⏳ Waiting for macOS file system to settle...") }
                Thread.sleep(forTimeInterval: 0.5)
            }

            FileManager.default.unmountVolume(at: drive.url, options: [.allPartitionsAndEjectDisk, .withoutUI]) { error in
                DispatchQueue.main.async {
                    var success = false
                    if let error = error {
                        if self.isDebugEnabled { LogManager.shared.log("⚠️ FileManager eject failed for \(drive.name): \(error.localizedDescription). Trying NSWorkspace...") }
                        do {
                            try NSWorkspace.shared.unmountAndEjectDevice(at: drive.url)
                            if self.isDebugEnabled { LogManager.shared.log("✅ Successfully ejected \(drive.name) via NSWorkspace") }
                            success = true
                        } catch let ejectError {
                            if self.isDebugEnabled { LogManager.shared.log("❌ Failed to eject \(drive.name) via NSWorkspace: \(ejectError.localizedDescription)") }
                        }
                    } else {
                        if self.isDebugEnabled { LogManager.shared.log("✅ Successfully ejected \(drive.name)") }
                        success = true
                    }

                    if success {
                        self.fetchDrives()
                        if notify {
                            let body: String
                            if clean && cleanedCount > 0 {
                                body = "Removed \(cleanedCount) hidden file\(cleanedCount == 1 ? "" : "s") and ejected \(drive.name)"
                            } else if clean {
                                body = "No hidden files found — ejected \(drive.name)"
                            } else {
                                body = "Ejected \(drive.name)"
                            }
                            self.sendNotification(title: "Easy Eject", body: body)
                        }
                    }
                    completion?(success, cleanedCount)
                }
            }
        }
    }

    func ejectAllCards(clean: Bool = false) {
        let cards = drives.filter { $0.isCameraCard || $0.isEmulatorCard }
        guard !cards.isEmpty else { return }
        if cards.count == 1 {
            eject(drive: cards[0], clean: clean)
            return
        }

        let totalCards = cards.count
        var ejectedCount = 0
        var totalCleaned = 0
        var completed = 0

        for drive in cards {
            eject(drive: drive, clean: clean, notify: false) { success, cleanedCount in
                if success { ejectedCount += 1 }
                totalCleaned += cleanedCount
                completed += 1

                if completed == totalCards {
                    let noun = ejectedCount == 1 ? "card" : "cards"
                    let body: String
                    if clean && totalCleaned > 0 {
                        body = "Removed \(totalCleaned) hidden file\(totalCleaned == 1 ? "" : "s") and ejected \(ejectedCount) camera \(noun)"
                    } else if clean {
                        body = "No hidden files found — ejected \(ejectedCount) camera \(noun)"
                    } else {
                        body = "Ejected \(ejectedCount) camera \(noun)"
                    }
                    self.sendNotification(title: "Easy Eject", body: body)
                }
            }
        }
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

// MARK: - 4. The Menu Bar View
struct EjectorMenuView: View {
    @StateObject private var manager = DriveManager()
    
    @AppStorage("warnBeforeEjectingSSD") private var warnBeforeEjectingSSD = true
    @AppStorage("cleanCardsOnEject") private var cleanCardsOnEject = true
    @AppStorage("enableDebugLogs") private var enableDebugLogs = false
    @AppStorage("shortcutKeyCode") private var shortcutKeyCode = 14
    
    let availableKeys: [(name: String, code: Int)] = [
        ("D", 2), ("E", 14), ("F", 3), ("G", 5), ("K", 40),
        ("M", 46), ("R", 15), ("T", 17), ("X", 7)
    ]
    
    @Environment(\.openWindow) private var openWindow
    
    var cameraCards: [Drive] { manager.drives.filter { $0.isCameraCard } }
    var emulatorCards: [Drive] { manager.drives.filter { $0.isEmulatorCard } }
    var otherExternalVolumes: [Drive] { manager.drives.filter { !$0.isCameraCard && !$0.isEmulatorCard } }
    
    // Dynamically finds the character for the visual menu hint
    var currentShortcutLetter: KeyEquivalent {
        let matchedKey = availableKeys.first(where: { $0.code == shortcutKeyCode })?.name ?? "E"
        return KeyEquivalent(Character(matchedKey.lowercased()))
    }
    
    func checkForUpdates() {
        guard let url = URL(string: "https://api.github.com/repos/rsmith4321/Ejector/releases/latest") else { return }
        URLSession.shared.dataTask(with: url) { data, _, error in
            guard let data = data,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let tagName = json["tag_name"] as? String else {
                DispatchQueue.main.async {
                    let alert = NSAlert()
                    alert.messageText = "Unable to Check for Updates"
                    alert.informativeText = "Could not connect to GitHub. Check your internet connection and try again."
                    alert.alertStyle = .warning
                    alert.addButton(withTitle: "OK")
                    NSApp.activate()
                    alert.runModal()
                }
                return
            }

            let remoteVersion = tagName.trimmingCharacters(in: CharacterSet(charactersIn: "vV"))
            let currentVersion = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0"

            DispatchQueue.main.async {
                if remoteVersion.compare(currentVersion, options: .numeric) == .orderedDescending {
                    let alert = NSAlert()
                    alert.messageText = "Update Available"
                    alert.informativeText = "Easy Eject v\(remoteVersion) is available. You're currently running v\(currentVersion)."
                    alert.alertStyle = .informational
                    alert.addButton(withTitle: "Download")
                    alert.addButton(withTitle: "Later")
                    NSApp.activate()
                    if alert.runModal() == .alertFirstButtonReturn {
                        if let downloadURL = URL(string: "https://github.com/rsmith4321/Ejector/releases/latest") {
                            NSWorkspace.shared.open(downloadURL)
                        }
                    }
                } else {
                    let alert = NSAlert()
                    alert.messageText = "You're Up to Date"
                    alert.informativeText = "Easy Eject v\(currentVersion) is the latest version."
                    alert.alertStyle = .informational
                    alert.addButton(withTitle: "OK")
                    NSApp.activate()
                    alert.runModal()
                }
            }
        }.resume()
    }

    func confirmAndEject(drive: Drive, clean: Bool = false) {
        let alert = NSAlert()
        alert.messageText = "Confirm Ejection"
        alert.informativeText = "Are you sure you want to \(clean ? "clean & eject" : "eject") '\(drive.name)'? It is not recognized as a camera card."
        alert.alertStyle = .warning

        alert.addButton(withTitle: clean ? "Clean & Eject" : "Eject")
        alert.addButton(withTitle: "Cancel")

        NSApp.activate()

        let response = alert.runModal()

        if response == .alertFirstButtonReturn {
            manager.eject(drive: drive, clean: clean)
        }
    }
    
    @ViewBuilder
    private func driveMenu(for drive: Drive, isCameraCard: Bool) -> some View {
        Menu {
            Button(action: {
                if !isCameraCard && warnBeforeEjectingSSD {
                    confirmAndEject(drive: drive)
                } else {
                    manager.eject(drive: drive)
                }
            }) {
                Label("Eject", systemImage: "eject")
            }
            Button(action: {
                if !isCameraCard && warnBeforeEjectingSSD {
                    confirmAndEject(drive: drive, clean: true)
                } else {
                    manager.eject(drive: drive, clean: true)
                }
            }) {
                Label("Clean & Eject", systemImage: "sparkles")
            }
        } label: {
            Label(drive.name, systemImage: drive.iconName)
        }
    }

    var body: some View {
        VStack(alignment: .leading) {

            // --- The boldest native title possible ---
            Text("Easy Eject")
                .font(.headline)
                .fontWeight(.bold)
                .padding(.horizontal)
                .padding(.top, 4)
                .padding(.bottom, 2)
            
            Divider()
            
            if manager.drives.isEmpty {
                Text("No external drives found")
                    .foregroundColor(.secondary)
            } else {

                if !cameraCards.isEmpty || !emulatorCards.isEmpty {
                    Button(action: { manager.ejectAllCards(clean: cleanCardsOnEject) }) {
                        Label(cleanCardsOnEject ? "Clean & Eject All Cards" : "Eject All Cards", systemImage: cleanCardsOnEject ? "sparkles" : "eject.fill")
                    }
                    .keyboardShortcut(currentShortcutLetter, modifiers: [.control, .option, .command])
                }

                if !cameraCards.isEmpty {
                    Section("Camera Cards") {
                        ForEach(cameraCards) { drive in
                            Button(action: { manager.eject(drive: drive, clean: cleanCardsOnEject) }) {
                                Label(cleanCardsOnEject ? "Clean & Eject \(drive.displayName)" : "Eject \(drive.displayName)", systemImage: drive.iconName)
                            }
                        }
                    }
                }

                if !emulatorCards.isEmpty {
                    Section("Emulator Cards") {
                        ForEach(emulatorCards) { drive in
                            Button(action: { manager.eject(drive: drive, clean: cleanCardsOnEject) }) {
                                Label(cleanCardsOnEject ? "Clean & Eject \(drive.displayName)" : "Eject \(drive.displayName)", systemImage: drive.iconName)
                            }
                        }
                    }
                }

                if (!cameraCards.isEmpty || !emulatorCards.isEmpty) && !otherExternalVolumes.isEmpty {
                    Divider()
                }

                if !otherExternalVolumes.isEmpty {
                    Section("Other External Volumes") {
                        ForEach(otherExternalVolumes) { drive in
                            driveMenu(for: drive, isCameraCard: false)
                        }
                    }
                }
            }
            
            Divider()

            Button("Help & Instructions") {
                openWindow(id: "helpWindow")
                NSApp.activate()
            }

            SettingsLink {
                Text("Settings...")
            }

            if enableDebugLogs {
                Button("Show Debug Window") {
                    openWindow(id: "debugWindow")
                    NSApp.activate()
                }
            }

            Divider()

            Button("Refresh List") {
                manager.fetchDrives()
            }

            Button("Check for Updates...") {
                checkForUpdates()
            }

            Button("About Easy Eject") {
                NSApp.activate()
                NSApp.orderFrontStandardAboutPanel()
            }

            Button("Quit Easy Eject") {
                NSApplication.shared.terminate(nil)
            }
        }
    }
}

// MARK: - 5. The Settings Window
struct SettingsView: View {
    @State private var launchAtLogin = SMAppService.mainApp.status == .enabled
    @AppStorage("cleanCardsOnEject") private var cleanCardsOnEject = true
    @AppStorage("warnBeforeEjectingSSD") private var warnBeforeEjectingSSD = true
    @AppStorage("isShortcutEnabled") private var isShortcutEnabled = false
    @AppStorage("shortcutKeyCode") private var shortcutKeyCode = 14
    @AppStorage("enableDebugLogs") private var enableDebugLogs = false
    @AppStorage("showEjectNotifications") private var showEjectNotifications = true

    @State private var showingAccessibilityAlert = false
    @State private var showingFullDiskAccessAlert = false
    @State private var showingNotificationAlert = false
    
    let availableKeys: [(name: String, code: Int)] = [
        ("D", 2), ("E", 14), ("F", 3), ("G", 5), ("K", 40),
        ("M", 46), ("R", 15), ("T", 17), ("X", 7)
    ]
    
    let timer = Timer.publish(every: 1.0, on: .main, in: .common).autoconnect()

    private func hasFullDiskAccess() -> Bool {
        FileManager.default.isReadableFile(atPath: "/Library/Application Support/com.apple.TCC/TCC.db")
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 20) {

            // --- 1. General Settings ---
            Text("General")
                .font(.headline)
            VStack(alignment: .leading, spacing: 12) {
                Toggle("Launch at Login", isOn: $launchAtLogin)
                    .onChange(of: launchAtLogin) { oldValue, newValue in
                        do {
                            if newValue {
                                try SMAppService.mainApp.register()
                                LogManager.shared.log("✅ Launch at Login enabled")
                            } else {
                                try SMAppService.mainApp.unregister()
                                LogManager.shared.log("✅ Launch at Login disabled")
                            }
                        } catch {
                            LogManager.shared.log("❌ Failed to toggle login item: \(error.localizedDescription)")
                            launchAtLogin = (SMAppService.mainApp.status == .enabled)
                        }
                    }
                
                VStack(alignment: .leading, spacing: 4) {
                    Toggle("Clean Cards Before Ejecting", isOn: $cleanCardsOnEject)
                        .toggleStyle(.switch)
                        .onChange(of: cleanCardsOnEject) { oldValue, newValue in
                            if newValue && !hasFullDiskAccess() {
                                showingFullDiskAccessAlert = true
                            }
                        }
                    Text("Removes hidden macOS files (.DS_Store, ._ files) that cause errors on cameras and phantom entries on emulators. Applies to all camera and emulator card buttons and the keyboard shortcut.")
                        .font(.caption)
                        .foregroundColor(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .alert("Full Disk Access Recommended", isPresented: $showingFullDiskAccessAlert) {
                    Button("Open Settings & Show App") {
                        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles") {
                            NSWorkspace.shared.open(url)
                        }
                        NSWorkspace.shared.activateFileViewerSelecting([Bundle.main.bundleURL])
                    }
                    Button("Later", role: .cancel) { }
                } message: {
                    Text("To clean hidden files from your drives, Easy Eject needs Full Disk Access.\n\n1. Click 'Open Settings & Show App' below.\n2. Drag Easy Eject from the Finder window into the Full Disk Access list.\n3. Toggle the switch next to Easy Eject to turn it on.\n4. macOS will ask you to quit and reopen the app for it to take effect.")
                }

                Toggle("Confirm Before Ejecting SSDs", isOn: $warnBeforeEjectingSSD)

                Toggle("Show Eject Notifications", isOn: $showEjectNotifications)
                    .onChange(of: showEjectNotifications) { oldValue, newValue in
                        if newValue {
                            UNUserNotificationCenter.current().getNotificationSettings { settings in
                                DispatchQueue.main.async {
                                    if settings.authorizationStatus == .denied {
                                        showingNotificationAlert = true
                                    }
                                }
                            }
                        }
                    }
                    .alert("Notifications Not Allowed", isPresented: $showingNotificationAlert) {
                        Button("Open Notification Settings") {
                            if let url = URL(string: "x-apple.systempreferences:com.apple.Notifications-Settings") {
                                NSWorkspace.shared.open(url)
                            }
                        }
                        Button("Later", role: .cancel) { }
                    } message: {
                        Text("macOS has notifications turned off for Easy Eject. To receive eject confirmations, open Notification Settings and enable notifications for this app.")
                    }
            }

            Divider()

            // --- 2. Shortcut Settings ---
            Text("Keyboard Shortcut")
                .font(.headline)
            VStack(alignment: .leading, spacing: 12) {
                Toggle("Enable Global Eject Shortcut", isOn: $isShortcutEnabled)
                    .onChange(of: isShortcutEnabled) { oldValue, newValue in
                        if newValue {
                            if GlobalHotkeyManager.shared.isTrusted(promptSystem: true) {
                                GlobalHotkeyManager.shared.start()
                            } else {
                                showingAccessibilityAlert = true
                                isShortcutEnabled = false
                            }
                        } else {
                            GlobalHotkeyManager.shared.stop()
                        }
                    }
                    .alert("Accessibility Permission Required", isPresented: $showingAccessibilityAlert) {
                        Button("Open System Settings") {
                            if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
                                NSWorkspace.shared.open(url)
                            }
                        }
                        Button("Cancel", role: .cancel) { }
                    } message: {
                        Text("To use the global keyboard shortcut from inside other apps, macOS requires Easy Eject to have Accessibility permission.\n\nIn System Settings, find Easy Eject in the list and toggle it on. If it's not in the list, click the '+' button and add it from your Applications folder.\n\nIt will begin working immediately without restarting the app.")
                    }
                
                if isShortcutEnabled {
                    HStack {
                        Text("Shortcut Letter (⌃⌥⌘ +)")
                        Picker("", selection: $shortcutKeyCode) {
                            ForEach(availableKeys, id: \.code) { key in
                                Text(key.name).tag(key.code)
                            }
                        }
                        .labelsHidden()
                        .frame(width: 80)
                    }
                    .padding(.leading, 18)
                    .onChange(of: shortcutKeyCode) { oldValue, newValue in
                        if GlobalHotkeyManager.shared.isTrusted(promptSystem: false) {
                            GlobalHotkeyManager.shared.start()
                        }
                    }
                }
            }
            // --- NEW: The Magic Auto-Detector ---
            .onReceive(timer) { _ in
                // If they are currently looking at the alert AND they grant permission...
                if showingAccessibilityAlert && GlobalHotkeyManager.shared.isTrusted(promptSystem: false) {
                    showingAccessibilityAlert = false // Dismiss the alert
                    isShortcutEnabled = true          // Flip the toggle ON
                    GlobalHotkeyManager.shared.start() // Start the hotkey!
                }
            }
            
            Divider()

            // --- 3. Debug ---
            Text("Debug")
                .font(.headline)
            VStack(alignment: .leading, spacing: 12) {
                Toggle("Enable Debug Logging", isOn: $enableDebugLogs)
            }

            Divider()

            HStack {
                Spacer()
                Button("Done") {
                    NSApp.keyWindow?.close()
                }
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(24)
        .frame(width: 400)
    }
}

// MARK: - 6. The App Entry Point
@main
struct EjectorApp: App {
    
    @AppStorage("hasAcceptedDisclaimer") private var hasAcceptedDisclaimer = false
    @AppStorage("cameraCardCount") private var cameraCardCount = 0
    @Environment(\.openWindow) private var openWindow
    
    // Add this init block to enforce a strict single-instance rule
    init() {
        // Safely get the app's unique Bundle Identifier
        if let bundleID = Bundle.main.bundleIdentifier {
            // Check macOS for any currently running apps with this exact ID
            let runningApps = NSRunningApplication.runningApplications(withBundleIdentifier: bundleID)
            
            // If the count is greater than 1, it means the original is already running
            // and THIS one is a duplicate trying to start.
            if runningApps.count > 1 {
                print("🚫 Another instance of Ejector is already running. Terminating duplicate.")
                NSApplication.shared.terminate(nil)
            }
        }
    }
    
    var body: some Scene {
        MenuBarExtra {
            EjectorMenuView()
                .onAppear {
                    if !hasAcceptedDisclaimer {
                        openWindow(id: "welcomeWindow")
                        NSApp.activate()
                    }
                }
        } label: {
            Image(systemName: "eject.fill")
            if cameraCardCount > 0 {
                Text("\(cameraCardCount)")
            }
        }
        
        // --- NEW: The Welcome & Disclaimer Window ---
        Window("Welcome to Easy Eject", id: "welcomeWindow") {
            WelcomeView()
        }
        .windowStyle(.hiddenTitleBar)
        .defaultSize(width: 480, height: 420) // Slightly taller window
        
        Window("Help & Instructions", id: "helpWindow") {
            HelpView()
        }
        .defaultSize(width: 500, height: 520)

        WindowGroup("Easy Eject Debug Logs", id: "debugWindow") {
            DebugLogView()
        }
        .defaultSize(width: 550, height: 400)
        
        // --- NEW: Register the Settings Window ---
        Settings {
            SettingsView()
        }
    }
}

// MARK: - 7. Help View
struct HelpView: View {
    @Environment(\.dismiss) var dismiss

    var body: some View {
        VStack(spacing: 0) {
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {

                    helpSection("Smart Sorting", icon: "sdcard") {
                        Text("Automatically detects camera folders (DCIM, GOPRO, NIKON, etc.) and card reader hardware (SD, CFexpress, XQD) to separate media cards from permanent SSDs. Especially helpful for CFexpress cards, which macOS often mistakes for standard hard drives.")
                        Text("Note: macOS does not identify CFexpress cards directly. Easy Eject infers CFexpress by detecting camera folders on a PCI-Express/NVMe drive. In rare cases, a non-CFexpress NVMe drive with camera folders may be labeled as CFexpress.")
                            .padding(.top, 2)
                    }

                    helpSection("Smart Grouping", icon: "rectangle.3.group") {
                        Text("Many cameras label all cards as \"Untitled.\" Easy Eject displays the card type next to the name — e.g., \"Untitled (CFexpress)\" and \"Untitled (SD)\" — so you always know which card you're ejecting.")
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
                        Text("• .**_ AppleDouble files** — the #1 cause of ghost games on retro consoles\n• **.DS_Store** — Mac folder settings that clutter Windows and Linux\n• **__MACOSX folders** — junk created when extracting zip files\n• **.apdisk** — Apple disk identification files created for network sharing")
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
                        Text("When camera or emulator cards are connected, a count appears next to the menu bar icon showing how many cards are mounted.")
                    }

                    helpSection("Permissions", icon: "lock.shield") {
                        VStack(alignment: .leading, spacing: 6) {
                            Text("**Accessibility** — Required only for the global keyboard shortcut to work inside other apps.")
                            Text("**Full Disk Access** — Required for Clean & Eject to scan and delete hidden files. Enable in System Settings > Privacy & Security.")
                        }
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
                    if let url = URL(string: "https://www.ryansmithphotography.com/easyejector") {
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

// MARK: - 8. Welcome & Disclaimer View
struct WelcomeView: View {
    @AppStorage("hasAcceptedDisclaimer") private var hasAcceptedDisclaimer = false
    @Environment(\.dismiss) var dismiss
    
    // --- NEW: State to track if they reached the bottom ---
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
                // MUST be a LazyVStack so the bottom sensor doesn't load immediately
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

                    The "Clean & Eject" feature involves the automated deletion of hidden macOS metadata files. Please ensure you have backups of your critical data before using this utility. If your card contains thousands of metadata files, cleaning may take several seconds — you'll receive a notification when it's done.
                    """)
                    .font(.subheadline)
                    .multilineTextAlignment(.leading)
                    
                    // Extra padding to ensure enough scrollable content
                    Spacer().frame(height: 20)
                    
                    // --- NEW: The Invisible Sensor ---
                    // Triggers the state change ONLY when scrolled into view
                    Color.clear
                        .frame(height: 1)
                        .onAppear {
                            hasReadToBottom = true
                        }
                }
                .padding()
            }
            // Force the scroll view to be smaller than the text to guarantee scrolling
            .frame(height: 160)
            .background(Color(NSColor.textBackgroundColor).opacity(0.5))
            .cornerRadius(8)
            // Add a subtle border to make it look like a text document box
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
