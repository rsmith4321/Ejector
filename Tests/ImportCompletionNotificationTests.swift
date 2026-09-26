import Foundation
import UserNotifications

@main struct ImportCompletionNotificationTests {
    static func main() {
        var count = 0
        func check(_ value: Bool, _ name: String) { precondition(value, name); count += 1; print("PASS \(name)") }
        func entry(_ id: String = "one", source: String = "/Volumes/TestCamera", volume: String = "camera", disk: String? = "disk9", connection: UUID) -> ImportCompletion {
            ImportCompletion(id: id, source: URL(fileURLWithPath: source), volumeID: volume,
                physicalID: disk, connection: connection, deviceName: "Test camera", hasMedia: true,
                folder: URL(fileURLWithPath: "/tmp/import"), destinationBookmark: Data(), destinationVolumeID: "destination")
        }
        let connection = UUID(), original = entry(connection: connection)
        var registry = ImportCompletionRegistry()
        check(registry.insert(original).isEmpty, "first completion")
        func matches(_ id: String = "one", volume: String? = "camera", disk: String? = "disk9", generation: UUID? = nil, root: URL? = URL(fileURLWithPath: "/Volumes/TestCamera")) -> Bool {
            registry.matches(id, volumeID: volume, physicalID: disk, connection: generation ?? connection, root: root)
        }
        check(matches(), "same connected source accepted")
        check(!matches(volume: "replacement"), "replacement volume rejected")
        check(!matches(disk: "disk10"), "changed physical disk rejected")
        check(!matches(disk: nil), "unconfirmed physical disk rejected")
        check(!matches(root: URL(fileURLWithPath: "/")), "unmounted path rejected")
        check(!matches(root: nil), "missing root rejected")
        check(!matches(generation: UUID()), "reconnected same UUID and BSD name rejected")
        check(!matches("unknown"), "unknown payload token rejected")
        check(registry.insert(entry("two", connection: connection)) == ["one"], "new import supersedes previous action")
        check(!matches(), "superseded action rejected")
        _ = registry.insert(entry("other", source: "/Volumes/Other", volume: "other", disk: "disk10", connection: connection))
        check(registry.entries.count == 2, "two devices can await a choice independently")
        check(registry.invalidate(source: original.source) == ["two"], "disconnect invalidates source action")
        check(registry.entries["other"] != nil, "disconnect does not remove other device action")
        check(registry.remove("other") != nil && registry.remove("other") == nil, "eject action consumed once")
        check(ImportCompletionRegistry().entries.isEmpty, "restart cannot restore eject authority from a notification")
        _ = registry.insert(entry(disk: nil, connection: connection))
        check(!matches(disk: nil), "missing original disk identity never authorizes eject")
        for enabled in [true, false] {
            for auth: UNAuthorizationStatus in [.authorized, .denied, .notDetermined, .provisional] {
                for alerts: UNNotificationSetting in [.enabled, .disabled, .notSupported] {
                    for style: UNAlertStyle in [.none, .banner, .alert] {
                        let result = ImportCompletionNotification.canShowActions(enabled: enabled, authorization: auth, alerts: alerts, style: style)
                        check(result == (enabled && auth == .authorized && alerts == .enabled && style != .none), "notification route \(enabled)/\(auth.rawValue)/\(alerts.rawValue)/\(style.rawValue)")
                    }
                }
            }
        }
        let category = ImportCompletionNotification.makeCategory()
        check(category.identifier == ImportCompletionNotification.category, "registered category matches payload")
        check(category.actions.map(\.identifier) == [ImportCompletionNotification.eject, ImportCompletionNotification.openFolder], "both actions registered")
        check(category.actions.map(\.title) == ["Eject Now", "Open Import Folder"], "clear action labels")
        for hasMedia in [true, false] {
            let content = ImportCompletionNotification.content(hasMedia: hasMedia, message: "Test camera")
            check(content.title == (hasMedia ? "Import complete" : "No media found"), "correct outcome title")
            check(content.categoryIdentifier == category.identifier, "outcome has actionable category")
            check(content.body.contains("all its partitions") && content.body.contains("keep connected"), "whole disk and keep connected explanation")
            check(content.userInfo.isEmpty, "no disk authority in untrusted payload")
            if !hasMedia { check(content.body.contains("Photos or skipped files"), "filtered selection is not claimed empty") }
        }
        print("PASS \(count) notification routing, connection identity, stale action and presentation checks")
    }
}
