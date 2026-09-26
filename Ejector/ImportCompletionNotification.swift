import Foundation
import UserNotifications

/// Tokens are kept only for this process and this connection, never reconstructed from a notification payload.
nonisolated struct ImportCompletion: Identifiable, Sendable {
    let id: String
    let source: URL
    let volumeID: String
    let physicalID: String?
    let connection: UUID
    let deviceName: String
    let hasMedia: Bool
    let folder: URL
    let destinationBookmark: Data
    let destinationVolumeID: String
}

nonisolated struct ImportCompletionRegistry {
    private(set) var entries: [String: ImportCompletion] = [:]

    mutating func insert(_ completion: ImportCompletion) -> [String] {
        let removed = invalidate(source: completion.source)
        entries[completion.id] = completion
        return removed
    }

    @discardableResult mutating func invalidate(source: URL) -> [String] {
        let ids = entries.values.filter { $0.source == source }.map(\.id)
        for id in ids { entries.removeValue(forKey: id) }
        return ids
    }

    @discardableResult mutating func remove(_ id: String) -> ImportCompletion? {
        entries.removeValue(forKey: id)
    }

    func matches(_ id: String, volumeID: String?, physicalID: String?, connection: UUID, root: URL?) -> Bool {
        guard let entry = entries[id], let originalDisk = entry.physicalID else { return false }
        return entry.volumeID == volumeID && originalDisk == physicalID
            && entry.connection == connection && entry.source.standardizedFileURL == root?.standardizedFileURL
    }
}

nonisolated enum ImportCompletionNotification {
    static let category = "IMPORT_COMPLETE_EJECT"
    static let eject = "IMPORT_EJECT_NOW"
    static let openFolder = "IMPORT_OPEN_FOLDER"
    static let showImports = Notification.Name("ShowDeviceMediaImports")

    static func makeCategory() -> UNNotificationCategory {
        UNNotificationCategory(identifier: category, actions: [
            UNNotificationAction(identifier: eject, title: "Eject Now", options: []),
            UNNotificationAction(identifier: openFolder, title: "Open Import Folder", options: [.foreground])
        ], intentIdentifiers: [], options: [])
    }

    static func canShowActions(enabled: Bool, authorization: UNAuthorizationStatus,
                               alerts: UNNotificationSetting, style: UNAlertStyle) -> Bool {
        enabled && authorization == .authorized && alerts == .enabled && style != .none
    }

    static func content(hasMedia: Bool, message: String) -> UNMutableNotificationContent {
        let content = UNMutableNotificationContent()
        content.title = hasMedia ? "Import complete" : "No media found"
        content.body = message + (hasMedia ? "" : " Photos or skipped files may still be on the card.")
            + " Eject Now ejects the device and all its partitions. Ignore to keep connected."
        content.categoryIdentifier = category
        return content
    }
}
