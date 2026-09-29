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
        UserDefaults.standard.set(false, forKey: ImportDefaults.deleteOriginalsKey)
        UserDefaults.standard.set(false, forKey: ImportDefaults.recoverTrashKey)
        let manager = DroneImportManager(supportDirectory: support)
        let destination = support.appendingPathComponent("destination")
        try! FileManager.default.createDirectory(at: destination, withIntermediateDirectories: true)
        let video = source.appendingPathComponent("DCIM/TEST001.MP4")
        try! FileManager.default.createDirectory(at: video.deletingLastPathComponent(), withIntermediateDirectories: true)
        try! Data(repeating: 7, count: 1024).write(to: video)
        let photo = source.appendingPathComponent("DCIM/PHOTO001.JPG")
        try! Data(repeating: 3, count: 128).write(to: photo)
        UserDefaults.standard.set(true, forKey: ImportDefaults.videosOnlyKey)
        UserDefaults.standard.set(true, forKey: ImportDefaults.cleanLayoutKey)
        UserDefaults.standard.set(true, forKey: ImportDefaults.askToEjectKey)
        let profile = DroneProfile(id: try! ImportVolumes.identity(source), name: "Disposable notification test",
            mediaPath: "DCIM", destinationBookmark: try! destination.bookmarkData(),
            destinationVolumeID: try! ImportVolumes.identity(destination), destinationLabel: destination.path,
            deleteOriginals: true, recoverTrash: true, autoEject: false,
            videosOnly: false, cleanLayout: false, usesImportDefaults: true)
        manager.profiles = [profile] // Never persist a profile; production support is never opened.
        var alertSeen = false
        var chooseEject = false
        let timer = Timer(timeInterval: 0.05, repeats: true) { _ in
            MainActor.assumeIsolated {
                func visit(_ view: NSView) {
                    if let button = view as? NSButton, button.title == (chooseEject ? "Eject Now" : "Keep Connected") {
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
            precondition(FileManager.default.fileExists(atPath: photo.path))
            precondition(!FileManager.default.fileExists(atPath: first.folder.appendingPathComponent("PHOTO001.JPG").path))
            print("PASS actual importer resolves shared video/layout/eject defaults; destructive flags ignored; photo not imported; originals retained")
            manager.handleImportNotification(id: first.id, action: UNNotificationDismissActionIdentifier)
            precondition(manager.pendingEjects.count == 1 && FileManager.default.fileExists(atPath: video.path))
            print("PASS dismiss does not eject or consume pending menu action")
            UserDefaults.standard.set(true, forKey: "showEjectNotifications")
            alertSeen = false
            manager.volumes = [source]
            manager.importNow(profile)
            await waitUntil { alertSeen && !manager.busy && manager.pendingEjects.count == 1 }
            precondition(manager.pendingEjects[0].id != first.id)
            manager.ejectCompletedImport(first.id)
            precondition(!manager.busy && FileManager.default.fileExists(atPath: video.path))
            print("PASS superseded completion cannot eject; already-verified import gets new choice")
            // Enable shared removal defaults only for generated files on the disposable fixture volume.
            let trash = source.appendingPathComponent(".Trashes/\(getuid())")
            try! FileManager.default.createDirectory(at: trash, withIntermediateDirectories: true)
            let trashVideo = trash.appendingPathComponent("RECOVER001.MP4")
            let trashPhoto = trash.appendingPathComponent("RETAIN001.JPG")
            try! Data(repeating: 9, count: 256).write(to: trashVideo)
            try! Data(repeating: 5, count: 64).write(to: trashPhoto)
            UserDefaults.standard.set(true, forKey: ImportDefaults.deleteOriginalsKey)
            UserDefaults.standard.set(true, forKey: ImportDefaults.recoverTrashKey)
            alertSeen = false
            manager.volumes = [source]
            manager.importNow(profile)
            await waitUntil { alertSeen && !manager.busy && manager.pendingEjects.count == 1 }
            precondition(!manager.hasError && !FileManager.default.fileExists(atPath: video.path))
            precondition(!FileManager.default.fileExists(atPath: trashVideo.path))
            precondition(FileManager.default.fileExists(atPath: photo.path) && FileManager.default.fileExists(atPath: trashPhoto.path))
            let removalFolder = manager.pendingEjects[0].folder
            precondition(try! Data(contentsOf: removalFolder.appendingPathComponent("TEST001.MP4")) == Data(repeating: 7, count: 1024))
            precondition(try! Data(contentsOf: removalFolder.appendingPathComponent("Recovered Device Trash/RECOVER001.MP4")) == Data(repeating: 9, count: 256))
            print("PASS actual importer inherits deletion and Trash defaults; verified video copies retained; only fixture videos removed; photos retained")
            // Empty/filtered selections must also show the dialog, even with notifications enabled.
            alertSeen = false
            manager.volumes = [source]
            manager.importNow(profile)
            await waitUntil { alertSeen && !manager.busy && manager.pendingEjects.count == 1 }
            let empty = manager.pendingEjects[0]
            precondition(!empty.hasMedia && FileManager.default.fileExists(atPath: source.path))
            print("PASS no-media dialog with notifications enabled; Keep Connected leaves fixture mounted")
            // Even a legacy notification eject action requires a fresh dialog choice.
            alertSeen = false
            manager.handleImportNotification(id: empty.id, action: ImportCompletionNotification.eject)
            precondition(alertSeen && !manager.busy && FileManager.default.fileExists(atPath: source.path))
            print("PASS legacy notification cannot eject without dialog confirmation")
            chooseEject = true
            manager.handleImportNotification(id: empty.id, action: ImportCompletionNotification.eject)
            manager.ejectCompletedImport(empty.id)
            await waitUntil { !manager.busy }
            precondition(!manager.hasError && !FileManager.default.fileExists(atPath: source.path))
            print("PASS explicit dialog Eject Now unmounts only disposable fixture; duplicate click ignored")
            timer.invalidate()
            fflush(stdout)
            exit(0)
        }
        NSApp.run()
    }
}
