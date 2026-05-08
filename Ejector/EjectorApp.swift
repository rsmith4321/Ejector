//
//  EjectorApp.swift
//  Ejector
//

import SwiftUI
import Cocoa
import Combine
import DiskArbitration
import ServiceManagement

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
                return Unmanaged.passRetained(event)
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
    
    func log(_ message: String) {
        DispatchQueue.main.async {
            let formatter = DateFormatter()
            formatter.dateFormat = "HH:mm:ss"
            let timestamp = formatter.string(from: Date())
            self.logs += "[\(timestamp)] \(message)\n"
            print("[\(timestamp)] \(message)")
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
    var id: String { url.path } // <-- The Fix: A stable, permanent ID
    let name: String
    let url: URL
    let isCameraCard: Bool
    let cardType: CardType?
    let isEjectable: Bool
    let isRemovable: Bool
    let isInternal: Bool
    
    // SF Symbol for representing this drive type
    var iconName: String {
        if isCameraCard {
            // Apple doesn't have a CFexpress symbol, so we use
            // the universal "sdcard" for all camera media.
            return "sdcard"
        } else {
            return "externaldrive"
        }
    }
}

// MARK: - 2. Drive Manager (The Brains)
class DriveManager: ObservableObject {
    @Published var drives: [Drive] = []
    private var cancellables = Set<AnyCancellable>()
    
    private var isDebugEnabled: Bool {
        UserDefaults.standard.bool(forKey: "enableDebugLogs")
    }
    
    init() {
        let center = NSWorkspace.shared.notificationCenter
        center.publisher(for: NSWorkspace.didMountNotification)
            .merge(with: center.publisher(for: NSWorkspace.didUnmountNotification))
            .merge(with: center.publisher(for: NSWorkspace.didRenameVolumeNotification))
            .debounce(for: .milliseconds(300), scheduler: RunLoop.main)
            .sink { [weak self] _ in
                self?.fetchDrives()
            }
            .store(in: &cancellables)
        
        // Listen for the Global Hotkey Notification
        NotificationCenter.default.publisher(for: NSNotification.Name("TriggerGlobalEject"))
            .sink { [weak self] _ in
                self?.ejectAllCameraCards()
            }
            .store(in: &cancellables)
        
        if UserDefaults.standard.bool(forKey: "isShortcutEnabled") {
            GlobalHotkeyManager.shared.start()
        }
        
        // --- NEW: Perform the initial scan when the app first launches ---
        self.fetchDrives()
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
            LogManager.shared.clear()
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
            
            let hardwareType = self.detectCardType(for: url)
            let isHardwareCamera = (hardwareType == .sd || hardwareType == .cfexpress || hardwareType == .xqd)
            
            let isCameraCard = isHardwareCamera || hasCameraStructure || (isInternal && isRemovable)
            
            var finalCardType: CardType? = isCameraCard ? hardwareType : nil
            if isInternal && isRemovable && hardwareType == .unknown {
                finalCardType = .sd
            }
            
            if isDebugEnabled {
                if isCameraCard {
                    LogManager.shared.log("   📸 Classified as Camera Card (\(finalCardType?.rawValue ?? "Unknown Format"))")
                } else {
                    LogManager.shared.log("   💾 Classified as Standard External Volume")
                }
                LogManager.shared.log("-------------------------------------------------")
            }
            
            foundDrives.append(Drive(name: name, url: url, isCameraCard: isCameraCard, cardType: finalCardType, isEjectable: isEjectable, isRemovable: isRemovable, isInternal: isInternal))
        }
        
        foundDrives.sort { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
        
        DispatchQueue.main.async {
            self.drives = foundDrives
        }
    }
    
    func eject(drive: Drive) {
        FileManager.default.unmountVolume(at: drive.url, options: [.allPartitionsAndEjectDisk, .withoutUI]) { error in
            DispatchQueue.main.async {
                if let error = error {
                    if self.isDebugEnabled { LogManager.shared.log("⚠️ FileManager eject failed for \(drive.name): \(error.localizedDescription). Trying NSWorkspace...") }
                    do {
                        try NSWorkspace.shared.unmountAndEjectDevice(at: drive.url)
                        if self.isDebugEnabled { LogManager.shared.log("✅ Successfully ejected \(drive.name) via NSWorkspace") }
                        self.fetchDrives()
                    } catch let ejectError {
                        if self.isDebugEnabled { LogManager.shared.log("❌ Failed to eject \(drive.name) via NSWorkspace: \(ejectError.localizedDescription)") }
                    }
                } else {
                    if self.isDebugEnabled { LogManager.shared.log("✅ Successfully ejected \(drive.name)") }
                    self.fetchDrives()
                }
            }
        }
    }
    
    func ejectAllCameraCards() {
        let cards = drives.filter { $0.isCameraCard }
        for drive in cards {
            eject(drive: drive)
        }
    }
}

// MARK: - 3. The Debug Window View
struct DebugLogView: View {
    @StateObject private var logManager = LogManager.shared
    
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
    @AppStorage("enableDebugLogs") private var enableDebugLogs = false
    @AppStorage("isShortcutEnabled") private var isShortcutEnabled = false
    
    @AppStorage("shortcutKeyCode") private var shortcutKeyCode = 14
    let availableKeys: [(name: String, code: Int)] = [
        ("D", 2), ("E", 14), ("F", 3), ("G", 5), ("K", 40),
        ("M", 46), ("R", 15), ("T", 17), ("X", 7)
    ]
    
    @State private var launchAtLogin = SMAppService.mainApp.status == .enabled
    @State private var showingAccessibilityAlert = false
    
    @Environment(\.openWindow) private var openWindow
    
    var cameraCards: [Drive] { manager.drives.filter { $0.isCameraCard } }
    var otherExternalVolumes: [Drive] { manager.drives.filter { !$0.isCameraCard } }
    
    // Dynamically finds the character for the visual menu hint
    var currentShortcutLetter: KeyEquivalent {
        let matchedKey = availableKeys.first(where: { $0.code == shortcutKeyCode })?.name ?? "E"
        return KeyEquivalent(Character(matchedKey.lowercased()))
    }
    
    func showInstructions() {
        let alert = NSAlert()
        alert.messageText = "Easy Ejector Help & Instructions"
        alert.informativeText = """
                • Smart Sorting: Ejector automatically finds camera folders (DCIM, etc.) to separate media cards from permanent SSDs. This is very useful for CFExpress cards that are not viewed as media by the OS.
                
                • Manual Ejecting: You can always click any drive in this menu to safely unmount it. No special permissions are required for this.
                
                • Global Keyboard Shortcut (Optional): Press ⌃⌥⌘ + your chosen letter to instantly eject all Camera Cards from any app.
                
                • Why does it need 'Accessibility' Permission?: To use the eject shortcut while you are using other apps (like Photoshop), macOS requires 'Accessibility' permission. If you don't want to use the keyboard shortcut, you do not need to enable this.
                
                • Confirm Before Eject SSD: This option warns you if you are ejecting a SSD or Hard Disk that is NOT detected as a camera card.
                
                • Troubleshooting: If a drive is missing, enable 'Debug Logging' and use the 'Show Debug Window' to see how your Mac identifies the hardware.
                """
        alert.alertStyle = .informational
        
        // 1. Add the buttons (The first one added becomes the primary "Return" key button)
        alert.addButton(withTitle: "Got It")
        alert.addButton(withTitle: "Visit Website")
        
        NSApp.activate()
        
        // 2. Capture which button the user clicks
        let response = alert.runModal()
        
        // 3. If they clicked the second button, open your website
        if response == .alertSecondButtonReturn {
            if let url = URL(string: "https://www.ryansmithphotography.com/easyejector") {
                NSWorkspace.shared.open(url)
            }
        }
    }
    func confirmAndEject(drive: Drive) {
        let alert = NSAlert()
        alert.messageText = "Confirm Ejection"
        alert.informativeText = "Are you sure you want to unmount '\(drive.name)'? It is not recognized as a camera card."
        alert.alertStyle = .warning
        
        alert.addButton(withTitle: "Eject")
        alert.addButton(withTitle: "Cancel")
        
        // This is crucial: it brings the alert to the front of the screen
        NSApp.activate()
        
        let response = alert.runModal()
        
        // If they click the first button ("Eject")
        if response == .alertFirstButtonReturn {
            manager.eject(drive: drive)
        }
    }
    
    var body: some View {
        VStack(alignment: .leading) {
            
            // --- The boldest native title possible ---
            Text("Easy Ejector")
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
                
                if !cameraCards.isEmpty {
                    Button(action: { manager.ejectAllCameraCards() }) {
                        Label("Eject All Cards", systemImage: "eject.fill")
                    }
                    .keyboardShortcut(currentShortcutLetter, modifiers: [.control, .option, .command])
                }
                
                if !cameraCards.isEmpty {
                    // --- Reverted to native Section for Apple's standard rendering ---
                    Section("Camera Cards") {
                        ForEach(cameraCards) { drive in
                            Button(action: { manager.eject(drive: drive) }) {
                                Label("Eject \(drive.name)", systemImage: drive.iconName)
                            }
                        }
                    }
                }
                
                if (!cameraCards.isEmpty && !otherExternalVolumes.isEmpty) {
                    Divider()
                }
                
                if !otherExternalVolumes.isEmpty {
                    // --- Reverted to native Section for Apple's standard rendering ---
                    Section("Other External Volumes") {
                        ForEach(otherExternalVolumes) { drive in
                            Button(action: {
                                if warnBeforeEjectingSSD {
                                    // Call the new native NSAlert function
                                    confirmAndEject(drive: drive)
                                } else {
                                    // Eject immediately if the user disabled the warning
                                    manager.eject(drive: drive)
                                }
                            }) {
                                Label("Eject \(drive.name)", systemImage: drive.iconName)
                            }
                        }
                    }
                }
            }
            
            Divider()
            
            Button("Help & Instructions") {
                showInstructions()
            }
            
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
                    Text("To use the global keyboard shortcut from inside other apps, macOS requires Ejector to have Accessibility permission.\n\nPlease enable it in System Settings, then try turning this shortcut on again.")
                }
            
            if isShortcutEnabled {
                Picker("Shortcut Letter (⌃⌥⌘ +)", selection: $shortcutKeyCode) {
                    ForEach(availableKeys, id: \.code) { key in
                        Text(key.name).tag(key.code)
                    }
                }
                .padding(.horizontal)
                .onChange(of: shortcutKeyCode) { oldValue, newValue in
                    if GlobalHotkeyManager.shared.isTrusted(promptSystem: false) {
                        GlobalHotkeyManager.shared.start()
                    }
                }
            }
            Toggle("Confirm Before Ejecting SSDs", isOn: $warnBeforeEjectingSSD)
            
            Toggle("Enable Debug Logging", isOn: $enableDebugLogs)
            
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
            
            Button("Quit Ejector") {
                NSApplication.shared.terminate(nil)
            }
        }
        
    }
}

// MARK: - 5. The App Entry Point
@main
struct EjectorApp: App {
    
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
        MenuBarExtra("Ejector", systemImage: "eject.fill") {
            EjectorMenuView()
        }
        
        WindowGroup("Ejector Debug Logs", id: "debugWindow") {
            DebugLogView()
        }
        .defaultSize(width: 550, height: 400)
    }
}
