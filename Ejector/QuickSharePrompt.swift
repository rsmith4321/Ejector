#if !APP_STORE
import AppKit

@MainActor enum QuickSharePrompt {
    struct Choice { let preset: EasySharePreset; let color: EasyShareColor }
    @MainActor struct Controls {
        let alert: NSAlert
        let size: NSPopUpButton
        let applyLUT: NSButton
        let camera: EasyShareCamera
        var choice: Choice {
            Choice(preset: size.indexOfSelectedItem == 1 ? .uhd : .hd,
                   color: applyLUT.state == .on ? camera.logColor : .standard)
        }
    }

    static func make(deviceName: String, preset: EasySharePreset, color: EasyShareColor, camera: EasyShareCamera = .luna) -> Controls {
        let alert = NSAlert()
        alert.messageText = "Make Quick Share copies?"
        alert.informativeText = "Create compressed MP4 copies for \(deviceName) in Sharing Copies. Full-quality imported originals stay intact.\n\nApply the \(camera == .luna ? "Luna LUT only to I-Log" : "DJI O4 LUT only to D-Log M") footage. For normal-color footage, turn it off."
        alert.addButton(withTitle: "Cancel").keyEquivalent = "\r"
        alert.addButton(withTitle: "Make Quick Share Copies").keyEquivalent = ""
        let view = NSView(frame: NSRect(x: 0, y: 0, width: 350, height: 78))
        let label = NSTextField(labelWithString: "Sharing size")
        label.frame = NSRect(x: 0, y: 47, width: 100, height: 22)
        let size = NSPopUpButton(frame: NSRect(x: 105, y: 42, width: 240, height: 30), pullsDown: false)
        size.addItems(withTitles: ["1080p · smaller files", "4K · more detail"])
        size.selectItem(at: preset == .hd ? 0 : 1)
        size.setAccessibilityLabel("Sharing size")
        let apply = NSButton(checkboxWithTitle: camera == .luna ? "Apply Insta360 Luna I-Log LUT" : "Apply DJI O4 Pro D-Log M LUT", target: nil, action: nil)
        apply.state = color == camera.logColor ? .on : .off
        apply.frame = NSRect(x: 0, y: 5, width: 345, height: 26)
        view.addSubview(label); view.addSubview(size); view.addSubview(apply)
        alert.accessoryView = view
        return Controls(alert: alert, size: size, applyLUT: apply, camera: camera)
    }

    static func choose(deviceName: String, preset: EasySharePreset, color: EasyShareColor, camera: EasyShareCamera = .luna) -> Choice? {
        let controls = make(deviceName: deviceName, preset: preset, color: color, camera: camera)
        guard controls.alert.runModal() == .alertSecondButtonReturn else { return nil }
        return controls.choice
    }
}
#endif
