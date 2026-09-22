import AppKit
import Carbon
import Combine

/// Registers only the selected shortcut. No event tap or Accessibility permission.
@MainActor final class GlobalHotkeyManager: ObservableObject {
    static let shared = GlobalHotkeyManager()
    static let availableKeys: [(name: String, code: Int)] = [
        ("D", 2), ("E", 14), ("F", 3), ("G", 5), ("K", 40),
        ("M", 46), ("R", 15), ("T", 17), ("X", 7)
    ]
    @Published private(set) var status = "Global eject shortcut is off."
    private var hotKey: EventHotKeyRef?
    private var handler: EventHandlerRef?

    func start() {
        stop()
        guard UserDefaults.standard.bool(forKey: "isShortcutEnabled") else { return }
        let selected = UserDefaults.standard.object(forKey: "shortcutKeyCode") as? Int ?? 14
        guard let key = Self.availableKeys.first(where: { $0.code == selected }) else {
            status = "Choose a supported shortcut letter."; return
        }
        var kind = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        let installed = InstallEventHandler(GetApplicationEventTarget(), { _, event, _ in
            var id = EventHotKeyID()
            guard GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                                    nil, MemoryLayout<EventHotKeyID>.size, nil, &id) == noErr,
                  id.signature == 0x45455041, id.id == 1 else { return OSStatus(eventNotHandledErr) }
            DispatchQueue.main.async {
                NotificationCenter.default.post(name: .init("TriggerGlobalEject"), object: nil)
            }
            return noErr
        }, 1, &kind, nil, &handler)
        let result = installed == noErr ? RegisterEventHotKey(UInt32(key.code), UInt32(cmdKey | controlKey | optionKey),
            EventHotKeyID(signature: 0x45455041, id: 1), GetApplicationEventTarget(), 0, &hotKey) : installed
        if result != noErr {
            stop(); status = "Shortcut unavailable (\(result)). Choose another letter or use the menu."
        } else { status = "⌃⌥⌘\(key.name) ejects all camera and emulator cards." }
        LogManager.shared.log(status)
    }
    func stop() {
        if let hotKey { UnregisterEventHotKey(hotKey) }; hotKey = nil
        if let handler { RemoveEventHandler(handler) }; handler = nil
        status = "Global eject shortcut is off."
    }
}
