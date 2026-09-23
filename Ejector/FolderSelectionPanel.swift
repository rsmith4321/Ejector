import AppKit

@MainActor enum FolderSelectionPanel {
    static func make() -> NSOpenPanel {
        let panel = NSOpenPanel()
        // Do not inherit a very wide, shallow size from a previous chooser.
        // This is a starting size only; the native panel remains resizable.
        panel.setContentSize(NSSize(width: 800, height: 600))
        return panel
    }
}
