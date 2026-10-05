import AppKit

/// A successful scan offers a choice; only the second button authorizes ejection.
@MainActor enum ImportEjectPrompt {
    static func make(hasMedia: Bool, deviceName: String, sourceName: String? = nil, storageSources: [String] = [], quickShare: Bool = false) -> NSAlert {
        let alert = NSAlert()
        alert.messageText = hasMedia ? "Import complete. Eject now?" : "No media found. Eject now?"
        alert.informativeText = (hasMedia
            ? "The selected files from \(deviceName) have been imported and verified."
            : "No media on \(deviceName) matches your import options. Photos, skipped previews, or other files may still be on the card.")
            + "\n\nChoose Keep Connected to import photos in Lightroom or finish other imports. Eject Now safely ejects the device and all its partitions."
        if !storageSources.isEmpty {
            let source = sourceName ?? deviceName
            alert.messageText = "Eject camera?"
            let result = hasMedia ? "Import from \(source) is complete." : "No matching media on \(source)."
            alert.informativeText = "\(result)\n\nEjects \(ListFormatter.localizedString(byJoining: storageSources)) together. Other storage may still have files to import."
        }
        alert.addButton(withTitle: "Keep Connected").keyEquivalent = "\r"
        alert.addButton(withTitle: storageSources.isEmpty ? "Eject Now" : "Eject Camera").keyEquivalent = ""
        if quickShare {
            alert.informativeText += "\n\nQuick Share makes smaller copies from the saved originals. Choose the size and whether to apply the Luna LUT. You can also do this later in Device Media Imports."
            alert.addButton(withTitle: "Quick Share…").keyEquivalent = ""
        }
        return alert
    }
}
