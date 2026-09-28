import AppKit

/// A successful scan offers a choice; only the second button authorizes ejection.
@MainActor enum ImportEjectPrompt {
    static func make(hasMedia: Bool, deviceName: String, storageSources: [String] = []) -> NSAlert {
        let alert = NSAlert()
        alert.messageText = hasMedia ? "Import complete. Eject now?" : "No media found. Eject now?"
        alert.informativeText = (hasMedia
            ? "The selected files from \(deviceName) have been imported and verified."
            : "No media on \(deviceName) matches your import options. Photos, skipped previews, or other files may still be on the card.")
            + "\n\nChoose Keep Connected to import photos in Lightroom or finish other imports. Eject Now safely ejects the device and all its partitions."
        if !storageSources.isEmpty {
            alert.messageText = hasMedia ? "Import complete. Eject all camera storage?" : "No media on this source. Eject all camera storage?"
            alert.informativeText = "The import checked only \(deviceName). This camera has these connected storage sources: \(storageSources.joined(separator: ", ")).\n\nOther sources may contain media that has not been imported. Each source needs its own import profile. Choose Keep Connected to set up or finish those imports, or Eject All Camera Storage to safely unmount every listed source. Wait for confirmation before unplugging."
        }
        alert.addButton(withTitle: "Keep Connected").keyEquivalent = "\r"
        alert.addButton(withTitle: storageSources.isEmpty ? "Eject Now" : "Eject All Camera Storage").keyEquivalent = ""
        return alert
    }
}
