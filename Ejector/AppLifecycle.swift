import AppKit
import Combine

/// One native menu and lifecycle for both distribution targets.
@MainActor final class EjectorLifecycle: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var item: NSStatusItem?
    private var observations = Set<AnyCancellable>()
    private var openWindow: ((String) -> Void)?
    private var openSettings: (() -> Void)?
    private var configured = false
    private let importer = DroneImportManager.shared
    private let manager = DriveManager.shared

    func applicationDidFinishLaunching(_ notification: Notification) {
        let status = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        let menu = NSMenu(); menu.autoenablesItems = false; menu.delegate = self
        status.menu = menu; item = status
        importer.objectWillChange.sink { [weak self] _ in
            DispatchQueue.main.async { self?.updateStatus() }
        }.store(in: &observations)
        manager.objectWillChange.sink { [weak self] _ in
            DispatchQueue.main.async { self?.updateStatus() }
        }.store(in: &observations)
        updateStatus()
    }
    func configure(openWindow: @escaping (String) -> Void, openSettings: @escaping () -> Void) {
        self.openWindow = openWindow; self.openSettings = openSettings
        guard !configured else { return }; configured = true
        let seen = UserDefaults.standard.bool(forKey: "hasSeenImportSetup")
        UserDefaults.standard.set(true, forKey: "hasSeenImportSetup")
        if seen {
            DispatchQueue.main.async { NSApp.windows.first { $0.identifier?.rawValue == "importsWindow" }?.orderOut(nil) }
        }
        #if APP_STORE
        if !UserDefaults.standard.bool(forKey: "hasSeenCardAuthorizationGuide") {
            UserDefaults.standard.set(true, forKey: "hasSeenCardAuthorizationGuide")
            openWindow("helpWindow")
            DispatchQueue.main.async { NSApp.windows.first { $0.identifier?.rawValue == "importsWindow" }?.orderOut(nil) }
        }
        #else
        if !UserDefaults.standard.bool(forKey: "hasAcceptedDisclaimer") { openWindow("welcomeWindow") }
        #endif
    }
    private func updateStatus() {
        let symbol = importer.busy ? "arrow.down.circle" : (importer.needsAttention ? "exclamationmark.triangle" : "eject.fill")
        let image = NSImage(systemSymbolName: symbol, accessibilityDescription: importer.needsAttention ? "Easy Eject: needs attention" : "Easy Eject")
        image?.isTemplate = true; item?.button?.image = image
        item?.button?.imagePosition = .imageLeading
        item?.button?.font = .monospacedDigitSystemFont(ofSize: NSFont.systemFontSize, weight: .regular)
        let cards = manager.drives.filter { $0.isCameraCard || $0.isEmulatorCard }
        let count = Set(cards.map { ImportVolumes.physicalID($0.url) ?? $0.url.path }).count
        item?.button?.title = importer.menuTitle.isEmpty ? (count > 0 ? "\(count)" : "") : importer.menuTitle
        item?.button?.toolTip = importer.needsAttention
            ? "Easy Eject: choose View Issue for details."
            : "Easy Eject: \(count) recognized card disks"
    }
    private func heading(_ title: String, _ menu: NSMenu) {
        let row = menu.addItem(withTitle: title, action: nil, keyEquivalent: ""); row.isEnabled = false
    }
    @discardableResult private func add(_ title: String, _ action: Selector, _ menu: NSMenu, icon: String? = nil) -> NSMenuItem {
        let row = menu.addItem(withTitle: title, action: action, keyEquivalent: ""); row.target = self
        if let icon { row.image = NSImage(systemSymbolName: icon, accessibilityDescription: nil) }
        return row
    }
    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        heading("Easy Eject", menu); menu.addItem(.separator())
        let cards = manager.drives.filter(\.isCameraCard)
        let emulators = manager.drives.filter(\.isEmulatorCard)
        let others = manager.drives.filter { !$0.isCameraCard && !$0.isEmulatorCard }
        let clean = UserDefaults.standard.bool(forKey: "cleanCardsOnEject")
        if manager.drives.isEmpty { heading("No external drives found", menu) }
        if !cards.isEmpty || !emulators.isEmpty {
            add(clean ? "Clean & Eject All Cards" : "Eject All Cards", #selector(ejectAll), menu, icon: "eject.fill")
                .isEnabled = !manager.isEjecting && !importer.busy
        }
        for (title, group) in [("Camera Cards", cards), ("Emulator Cards", emulators)] where !group.isEmpty {
            heading(title, menu)
            for drive in group {
                let row = add("\(clean ? "Clean & Eject" : "Eject") \(drive.displayName)", #selector(ejectCard(_:)), menu, icon: drive.iconName)
                row.representedObject = drive
                row.isEnabled = !manager.isEjecting && !importer.blocksEject(drive.url)
            }
        }
        if !others.isEmpty {
            if !cards.isEmpty || !emulators.isEmpty { menu.addItem(.separator()) }
            heading("Other External Volumes", menu)
            for drive in others {
                let row = menu.addItem(withTitle: drive.name, action: nil, keyEquivalent: "")
                row.image = NSImage(systemSymbolName: drive.iconName, accessibilityDescription: nil)
                let submenu = NSMenu(); submenu.autoenablesItems = false
                let eject = add("Eject", #selector(ejectOther(_:)), submenu, icon: "eject")
                eject.representedObject = drive
                let cleanup = add("Clean & Eject", #selector(cleanOther(_:)), submenu, icon: "sparkles")
                cleanup.representedObject = drive
                row.submenu = submenu
                let available = !manager.isEjecting && !importer.blocksEject(drive.url)
                row.isEnabled = available; eject.isEnabled = available; cleanup.isEnabled = available
            }
        }
        menu.addItem(.separator())
        if importer.busy { heading("\(importer.activeName): \(importer.menuTitle)", menu) }
        else if importer.needsAttention {
            add("View Issue…", #selector(imports), menu, icon: "exclamationmark.triangle")
        }
        add("Air Unit & Camera Imports…", #selector(imports), menu)
        #if APP_STORE
        add("Authorize a Card…", #selector(authorize), menu)
        #endif
        add("Help & Instructions", #selector(help), menu)
        add("Settings…", #selector(settings), menu)
        if UserDefaults.standard.bool(forKey: "enableDebugLogs") { add("Show Debug Window", #selector(logs), menu) }
        menu.addItem(.separator())
        add("Refresh List", #selector(refresh), menu)
        add("Check for Updates…", #selector(updates), menu)
        add("About Easy Eject", #selector(about), menu)
        add("Quit Easy Eject", #selector(quit), menu).isEnabled = !importer.busy && !manager.isEjecting
    }
    func showMenu() { item?.button?.performClick(nil) }
    @objc private func ejectAll() { manager.ejectAllCards(clean: UserDefaults.standard.bool(forKey: "cleanCardsOnEject")) }
    @objc private func ejectCard(_ sender: NSMenuItem) {
        guard let drive = sender.representedObject as? Drive else { return }
        manager.eject(drive: drive, clean: UserDefaults.standard.bool(forKey: "cleanCardsOnEject"))
    }
    private func other(_ drive: Drive, clean: Bool) {
        if UserDefaults.standard.object(forKey: "warnBeforeEjectingSSD") as? Bool ?? true {
            let alert = NSAlert(); alert.messageText = "Confirm Ejection"
            alert.informativeText = "\(drive.name) is not recognized as a camera card. \(clean ? "Clean metadata and eject" : "Eject") this disk and all its partitions?"
            alert.addButton(withTitle: "Cancel")
            alert.addButton(withTitle: clean ? "Clean & Eject" : "Eject")
            NSApp.activate(); guard alert.runModal() == .alertSecondButtonReturn else { return }
        }
        manager.eject(drive: drive, clean: clean)
    }
    @objc private func ejectOther(_ sender: NSMenuItem) {
        guard let drive = sender.representedObject as? Drive else { return }
        sender.menu?.cancelTracking(); item?.menu?.cancelTracking()
        other(drive, clean: false)
    }
    @objc private func cleanOther(_ sender: NSMenuItem) {
        guard let drive = sender.representedObject as? Drive else { return }
        sender.menu?.cancelTracking(); item?.menu?.cancelTracking()
        other(drive, clean: true)
    }
    @objc private func imports() { openWindow?("importsWindow"); NSApp.activate() }
    @objc private func help() { openWindow?("helpWindow"); NSApp.activate() }
    @objc private func logs() { openWindow?("debugWindow"); NSApp.activate() }
    @objc private func settings() { openSettings?(); NSApp.activate() }
    @objc private func refresh() { manager.fetchDrives(clearCache: true); importer.refresh() }
    @objc private func updates() { UpdateChannel.checkForUpdates() }
    @objc private func about() { NSApp.activate(); NSApp.orderFrontStandardAboutPanel() }
    @objc private func quit() { NSApp.terminate(nil) }
    #if APP_STORE
    @objc private func authorize() {
        item?.menu?.cancelTracking()
        CardAccess.shared.authorize()
    }
    #endif
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { imports(); return true }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        if importer.busy { importer.cancel(); imports(); return .terminateCancel }
        if manager.isEjecting { return .terminateCancel }
        return .terminateNow
    }
    func applicationWillTerminate(_ notification: Notification) { GlobalHotkeyManager.shared.stop() }
}
