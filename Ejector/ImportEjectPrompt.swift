import AppKit

/// A successful scan offers a choice; only the second button authorizes ejection.
@MainActor enum ImportEjectPrompt {
    static func make(hasMedia: Bool, deviceName: String) -> NSAlert {
        let alert = NSAlert()
        alert.messageText = hasMedia ? "Import complete. Eject now?" : "No media found. Eject now?"
        alert.informativeText = (hasMedia
            ? "The selected files from \(deviceName) have been imported and verified."
            : "No media on \(deviceName) matches your import options. Photos, skipped previews, or other files may still be on the card.")
            + "\n\nChoose Keep Connected to import photos in Lightroom or finish other imports. Eject Now safely ejects the device and all its partitions."
        alert.addButton(withTitle: "Keep Connected").keyEquivalent = "\r"
        alert.addButton(withTitle: "Eject Now").keyEquivalent = ""
        return alert
    }
}
