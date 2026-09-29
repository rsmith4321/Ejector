import AppKit

@MainActor enum FolderSelectionPanel {
    static func make() -> NSOpenPanel {
        let panel = NSOpenPanel()
        // Give the sidebar, file list and instructions room without inheriting a tiny chooser.
        // Leave space for window chrome and keep the panel resizable on smaller displays.
        let available = (NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900))
            .insetBy(dx: 40, dy: 40).size
        panel.contentMinSize = NSSize(width: min(800, available.width), height: min(520, available.height))
        panel.setContentSize(NSSize(width: min(960, available.width), height: min(680, available.height)))
        return panel
    }
}
