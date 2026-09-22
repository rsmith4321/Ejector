#if APP_STORE
import SwiftUI
import Combine
import Carbon
import ServiceManagement
import DiskArbitration

@main struct StoreApp: App {
    @NSApplicationDelegateAdaptor(StoreDelegate.self) private var delegate
    @StateObject private var importer = DroneImportManager.shared
    var body: some Scene {
        Window("Easy Eject Store Prototype", id: "importsWindow") {
            VStack(spacing: 0) {
                StoreSettings()
                Divider()
                DroneImportView(importer: importer)
            }
        }.defaultSize(width: 680, height: 790)
    }
}

@MainActor final class LogManager {
    static let shared = LogManager()
    func log(_ message: String) { NSLog("%@", message) }
}

@MainActor final class StoreDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var item: NSStatusItem?
    private var hotKey: EventHotKeyRef?
    private var handler: EventHandlerRef?
    private var observations = Set<AnyCancellable>()
    private let importer = DroneImportManager.shared
    private var connectedCardCount = 0

    func applicationDidFinishLaunching(_ notification: Notification) {
        let status = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        let menu = NSMenu(); menu.autoenablesItems = false; menu.delegate = self; status.menu = menu; item = status
        importer.$progress.combineLatest(importer.$busy, importer.$hasError).sink { [weak self] _, _, _ in
            DispatchQueue.main.async { self?.updateStatusItem() }
        }.store(in: &observations)
        importer.$volumes.combineLatest(importer.$profiles).sink { [weak self] volumes, profiles in
            self?.connectedCardCount = Self.cardCount(volumes: volumes, profiles: profiles)
            self?.updateStatusItem()
        }.store(in: &observations)
        NotificationCenter.default.publisher(for: .init("StoreOpenEjectMenu"))
            .sink { [weak self] _ in self?.item?.button?.performClick(nil) }
            .store(in: &observations)
        // A registered key combination does not observe other keystrokes or need Accessibility.
        var kind = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        let installed = InstallEventHandler(GetApplicationEventTarget(), { _, event, _ in
            var id = EventHotKeyID()
            guard GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID), nil,
                                    MemoryLayout<EventHotKeyID>.size, nil, &id) == noErr,
                  id.signature == 0x45455350, id.id == 1 else { return OSStatus(eventNotHandledErr) }
            DispatchQueue.main.async {
                NotificationCenter.default.post(name: .init("StoreOpenEjectMenu"), object: nil)
                LogManager.shared.log("Registered shortcut received")
            }
            return noErr
        }, 1, &kind, nil, &handler)
        let registered = installed == noErr ? RegisterEventHotKey(UInt32(kVK_ANSI_J), UInt32(cmdKey | controlKey | shiftKey),
                               EventHotKeyID(signature: 0x45455350, id: 1), GetApplicationEventTarget(), 0, &hotKey) : installed
        StorePreferences.shared.shortcutStatus = registered == noErr ? "Eject menu: ⌃⇧⌘J" : "Shortcut unavailable (\(registered)). Use the menu bar."
        LogManager.shared.log("Shortcut registration: \(registered)")
        if !UserDefaults.standard.bool(forKey: "storeOnboarded") {
            showWindow()
        } else {
            DispatchQueue.main.async { NSApp.windows.first(where: { $0.identifier?.rawValue == "importsWindow" })?.orderOut(nil) }
        }
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    private func updateStatusItem() {
        guard let button = item?.button else { return }
        let symbol = importer.busy ? "arrow.down.circle" : (importer.hasError ? "exclamationmark.triangle" : "eject.fill")
        let image = NSImage(systemSymbolName: symbol, accessibilityDescription: "Easy Eject")?
            .withSymbolConfiguration(.init(pointSize: 18, weight: .semibold))
        image?.size = NSSize(width: 18, height: 18)
        image?.isTemplate = true
        button.image = image
        button.imagePosition = .imageLeading
        button.font = .monospacedDigitSystemFont(ofSize: NSFont.systemFontSize, weight: .regular)
        button.title = !importer.menuTitle.isEmpty ? importer.menuTitle : (connectedCardCount > 0 ? "\(connectedCardCount)" : "")
        button.toolTip = "Easy Eject Store Prototype: \(connectedCardCount) connected cards or enrolled air units"
    }

    /// Use public disk metadata and enrolled identities, without scanning unauthorized folders.
    private static func cardCount(volumes: [URL], profiles: [DroneProfile]) -> Int {
        let enrolled = Set(profiles.map(\.id))
        var disks = Set<String>()
        let session = DASessionCreate(nil)
        for volume in volumes {
            let known = (try? ImportVolumes.identity(volume)).map { enrolled.contains($0) } ?? false
            var hardwareCard = false
            if let session, let disk = DADiskCreateFromVolumePath(nil, session, volume as CFURL),
               let description = DADiskCopyDescription(disk) as? [String: Any] {
                let keys = [kDADiskDescriptionDeviceModelKey, kDADiskDescriptionDeviceVendorKey,
                            kDADiskDescriptionDeviceProtocolKey, kDADiskDescriptionBusNameKey]
                let text = keys.compactMap { description[$0 as String] as? String }.joined(separator: " ").lowercased()
                hardwareCard = ["secure digital", "sdxc", "sdhc", " sd ", "cfexpress", "xqd"].contains { text.contains($0) }
                    || (description[kDADiskDescriptionBusNameKey as String] as? String)?.lowercased() == "sd"
            }
            if known || hardwareCard { disks.insert(ImportVolumes.physicalID(volume) ?? volume.path) }
        }
        return disks.count
    }

    func applicationWillTerminate(_ notification: Notification) {
        if let hotKey { UnregisterEventHotKey(hotKey) }
        if let handler { RemoveEventHandler(handler) }
    }
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        if importer.busy { importer.cancel(); showWindow(); return .terminateCancel }
        return .terminateNow
    }
    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        add("Open imports…", #selector(showWindow), to: menu)
        if !importer.message.isEmpty {
            let info = menu.addItem(withTitle: importer.busy ? importer.menuTitle : importer.message, action: nil, keyEquivalent: "")
            info.isEnabled = false
        }
        menu.addItem(.separator())
        for volume in importer.volumes {
            let row = add("Eject \(volume.lastPathComponent)", #selector(eject(_:)), to: menu)
            row.representedObject = volume; row.isEnabled = !importer.blocksEject(volume)
        }
        menu.addItem(.separator())
        add("Website / Compare editions", #selector(website), to: menu)
        add("Quit Store Prototype", #selector(quit), to: menu).isEnabled = !importer.busy
    }
    @discardableResult private func add(_ title: String, _ action: Selector, to menu: NSMenu) -> NSMenuItem {
        let row = menu.addItem(withTitle: title, action: action, keyEquivalent: ""); row.target = self; return row
    }
    @objc private func showWindow() {
        NSApp.activate()
        NSApp.windows.first(where: { $0.identifier?.rawValue == "importsWindow" })?.makeKeyAndOrderFront(nil)
    }
    @objc private func website() { NSWorkspace.shared.open(URL(string: "https://easyeject.com")!) }
    @objc private func quit() { NSApp.terminate(nil) }
    @objc private func eject(_ sender: NSMenuItem) {
        guard let volume = sender.representedObject as? URL, importer.reserveManualEject(volume) else { return }
        let key = ImportVolumes.physicalID(volume) ?? volume.path
        FileManager.default.unmountVolume(at: volume, options: [.allPartitionsAndEjectDisk, .withoutUI]) { error in
            DispatchQueue.main.async {
                self.importer.finishManualEject(key)
                self.importer.hasError = error != nil
                self.importer.message = error.map { "Could not eject \(volume.lastPathComponent): \($0.localizedDescription)" } ?? "\(volume.lastPathComponent): safe to unplug."
                LogManager.shared.log(self.importer.message)
            }
        }
    }
}

@MainActor final class StorePreferences: ObservableObject {
    static let shared = StorePreferences()
    @Published var shortcutStatus = "Registering shortcut…"
}

private struct StoreSettings: View {
    @AppStorage("storeOnboarded") private var onboarded = false
    @ObservedObject private var preferences = StorePreferences.shared
    @Environment(\.scenePhase) private var scenePhase
    @State private var loginStatus = SMAppService.mainApp.status
    @State private var loginError: String?
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            if !onboarded {
                Text("Welcome to Easy Eject").font(.title2.bold())
                Text("Choose a media folder and a destination to save verified, dated copies. Originals stay on your device by default. This edition accesses the folders you authorize; device Trash recovery and whole-drive cleanup are available in the separate website edition.")
                    .fixedSize(horizontal: false, vertical: true)
                Button("Get started") { onboarded = true }
            }
            HStack {
                Toggle("Launch at login", isOn: Binding(get: { loginStatus == .enabled }, set: { enabled in
                    do {
                        if enabled { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
                        loginError = nil
                    } catch { loginError = error.localizedDescription }
                    loginStatus = SMAppService.mainApp.status
                }))
                Spacer()
                Link("Website / Compare editions", destination: URL(string: "https://easyeject.com")!)
            }
            if loginStatus == .requiresApproval {
                Button("Review login permission in System Settings") { SMAppService.openSystemSettingsLoginItems() }
            }
            if let loginError { Text(loginError).foregroundStyle(.orange) }
            HStack {
                Button("Eject menu") { NotificationCenter.default.post(name: .init("StoreOpenEjectMenu"), object: nil) }
                Text(preferences.shortcutStatus).font(.caption).foregroundStyle(.secondary)
            }
        }.padding(18)
        .onChange(of: scenePhase) { _, phase in if phase == .active { loginStatus = SMAppService.mainApp.status } }
    }
}
#endif
