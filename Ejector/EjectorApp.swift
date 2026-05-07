//
//  EjectorApp.swift
//  Ejector
//

import SwiftUI
import Cocoa
import Combine
import DiskArbitration
import ServiceManagement // Required for Launch at Login

// MARK: - 0. Log Manager
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
    let id = UUID()
    let name: String
    let url: URL
    let isCameraCard: Bool
    let cardType: CardType?
    let isEjectable: Bool
    let isRemovable: Bool
    let isInternal: Bool
    
    var iconName: String {
        if isCameraCard {
            switch cardType ?? .unknown {
            case .sd:
                return "sdcard"
            case .cfexpress, .xqd:
                return "memorychip"
            case .unknown:
                return "externaldrive.badge.questionmark"
            }
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
        
        Timer.publish(every: 15, on: .main, in: .common)
            .autoconnect()
            .sink { [weak self] _ in
                self?.fetchDrives()
            }
            .store(in: &cancellables)
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
    @AppStorage("enableDebugLogs") private var enableDebugLogs = false
    
    // Check macOS system status for the login item
    @State private var launchAtLogin = SMAppService.mainApp.status == .enabled
    
    @Environment(\.openWindow) private var openWindow
    
    var cameraCards: [Drive] { manager.drives.filter { $0.isCameraCard } }
    var otherExternalVolumes: [Drive] { manager.drives.filter { !$0.isCameraCard } }
    
    func showInstructions() {
        let alert = NSAlert()
        alert.messageText = "Ejector Help & Instructions"
        alert.informativeText = """
        • Smart Sorting: Ejector automatically looks for camera folders (like DCIM) to separate your media cards from permanent SSDs.
        
        • Ejecting: Click any drive in the list to safely unmount it.
        
        • Bulk Eject Shortcut: Press ⌃⌥⌘E (Control+Option+Command+E) at any time to instantly eject all Camera Cards without touching your external SSDs.
        
        • Troubleshooting: If a drive isn't showing up correctly, check the 'Enable Debug Logging' box in the menu, then click 'Show Debug Window' to see exactly how your Mac is identifying the drive.
        """
        alert.alertStyle = .informational
        alert.addButton(withTitle: "Got It")
        NSApp.activate(ignoringOtherApps: true)
        alert.runModal()
    }
    
    var body: some View {
        VStack(alignment: .leading) {
            if manager.drives.isEmpty {
                Text("No external drives found")
                    .foregroundColor(.secondary)
            } else {
                if !cameraCards.isEmpty {
                    Button(action: { manager.ejectAllCameraCards() }) {
                        Label("Eject All Camera Cards", systemImage: "eject.fill")
                    }
                    .keyboardShortcut("e", modifiers: [.control, .option, .command])
                }
                
                if (!cameraCards.isEmpty || !otherExternalVolumes.isEmpty) {
                    Divider()
                }
                
                if !cameraCards.isEmpty {
                    Text("Camera Cards")
                        .font(.caption)
                        .foregroundColor(.secondary)
                        .padding(.horizontal)
                        .padding(.top, 4)

                    ForEach(cameraCards) { drive in
                        Button(action: { manager.eject(drive: drive) }) {
                            Label("Eject \(drive.name)", systemImage: drive.iconName)
                        }
                    }
                }
                
                if !cameraCards.isEmpty && !otherExternalVolumes.isEmpty {
                    Divider()
                }
                
                if !otherExternalVolumes.isEmpty {
                    Text("Other External Volumes")
                        .font(.caption)
                        .foregroundColor(.secondary)
                        .padding(.horizontal)
                        .padding(.top, 4)
                    
                    ForEach(otherExternalVolumes) { drive in
                        Button(action: { manager.eject(drive: drive) }) {
                            Label("Eject \(drive.name)", systemImage: drive.iconName)
                        }
                    }
                }
            }
            
            Divider()
            
            Button("Help & Instructions") {
                showInstructions()
            }
            
            // The Launch at Login Toggle
            Toggle("Launch at Login", isOn: $launchAtLogin)
                .onChange(of: launchAtLogin) { newValue in
                    do {
                        if newValue {
                            try SMAppService.mainApp.register()
                        } else {
                            try SMAppService.mainApp.unregister()
                        }
                    } catch {
                        print("Failed to toggle login item: \(error)")
                        // Revert the toggle visually if macOS blocks the action
                        launchAtLogin = SMAppService.mainApp.status == .enabled
                    }
                }
            
            Toggle("Enable Debug Logging", isOn: $enableDebugLogs)
            
            if enableDebugLogs {
                Button("Show Debug Window") {
                    openWindow(id: "debugWindow")
                    NSApp.activate(ignoringOtherApps: true)
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
        .onAppear {
            manager.fetchDrives()
        }
    }
}

// MARK: - 5. The App Entry Point
@main
struct EjectorApp: App {
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
