import AppKit
import Combine
import UserNotifications

final class LogManager {
    static let shared = LogManager()
    func log(_ message: String) { print(message); fflush(stdout) }
}

/// Run only in the dedicated signed fixture app, with an explicitly provided disposable volume.
@main struct ImportNotificationIntegrationTests {
    @MainActor static func main() {
        _ = NSApplication.shared
        guard CommandLine.arguments.count == 3 else { fatalError("Pass fixture volume and isolated support directory") }
        let source = URL(fileURLWithPath: CommandLine.arguments[1])
        let support = URL(fileURLWithPath: CommandLine.arguments[2])
        precondition(source.lastPathComponent.hasPrefix("EE-Notification-Test-"))
        precondition(support.lastPathComponent == "support" && support.deletingLastPathComponent().lastPathComponent.hasPrefix("ee-notification-integration-"))
        UserDefaults.standard.set(false, forKey: "showEjectNotifications")
        let manager = DroneImportManager(supportDirectory: support)
        let destination = support.appendingPathComponent("destination")
        try! FileManager.default.createDirectory(at: destination, withIntermediateDirectories: true)
        let video = source.appendingPathComponent("DCIM/TEST001.MP4")
        try! FileManager.default.createDirectory(at: video.deletingLastPathComponent(), withIntermediateDirectories: true)
        try! Data(repeating: 7, count: 1024).write(to: video)
        let profile = DroneProfile(id: try! ImportVolumes.identity(source), name: "Disposable notification test",
            mediaPath: "DCIM", destinationBookmark: try! destination.bookmarkData(),
            destinationVolumeID: try! ImportVolumes.identity(destination), destinationLabel: destination.path,
            autoEject: true, cleanLayout: true)
        manager.profiles = [profile] // Never persist a profile; production support is never opened.
        var alertSeen = false
        let timer = Timer(timeInterval: 0.05, repeats: true) { _ in
            MainActor.assumeIsolated {
                func visit(_ view: NSView) {
                    if let button = view as? NSButton, button.title == "Keep Connected" {
                        alertSeen = true; button.performClick(nil); return
                    }
                    for child in view.subviews { visit(child) }
                }
                for window in NSApp.windows { if let view = window.contentView { visit(view) } }
            }
        }
        RunLoop.main.add(timer, forMode: .common)
        RunLoop.main.add(timer, forMode: .modalPanel)
        Task { @MainActor in
            @MainActor func waitUntil(_ condition: () -> Bool) async {
                let deadline = Date().addingTimeInterval(30)
                while !condition() {
                    if Date() >= deadline { print("TIMEOUT", manager.message, "busy", manager.busy, "alerts", alertSeen, "pending", manager.pendingEjects.count, "volumes", manager.volumes.map(\.path)); fflush(stdout); fatalError("Timed out") }
                    try! await Task.sleep(for: .milliseconds(50))
                }
            }
            manager.refresh()
            manager.volumes = [source] // Disk images need not classify as removable camera hardware.
            manager.importNow(profile)
            await waitUntil { alertSeen && !manager.busy && manager.pendingEjects.count == 1 }
            let first = manager.pendingEjects[0]
            precondition(FileManager.default.fileExists(atPath: video.path))
            precondition(try! Data(contentsOf: first.folder.appendingPathComponent("TEST001.MP4")) == Data(repeating: 7, count: 1024))
            print("PASS actual importer + disabled-notification popup defaults Keep Connected; copies verified; source retained; busy released")
            manager.handleImportNotification(id: first.id, action: UNNotificationDismissActionIdentifier)
            precondition(manager.pendingEjects.count == 1 && FileManager.default.fileExists(atPath: video.path))
            print("PASS dismiss does not eject or consume pending menu action")
            alertSeen = false
            manager.volumes = [source]
            manager.importNow(profile)
            await waitUntil { alertSeen && !manager.busy && manager.pendingEjects.count == 1 }
            precondition(manager.pendingEjects[0].id != first.id)
            manager.ejectCompletedImport(first.id)
            precondition(!manager.busy && FileManager.default.fileExists(atPath: video.path))
            print("PASS superseded completion cannot eject; already-verified import gets new choice")
            let second = manager.pendingEjects[0]
            manager.handleImportNotification(id: second.id, action: ImportCompletionNotification.eject)
            manager.ejectCompletedImport(second.id)
            await waitUntil { !manager.busy }
            precondition(!manager.hasError && !FileManager.default.fileExists(atPath: source.path))
            print("PASS explicit notification-handler Eject Now unmounts only disposable fixture; duplicate click ignored")
            timer.invalidate()
            fflush(stdout)
            exit(0)
        }
        NSApp.run()
    }
}
