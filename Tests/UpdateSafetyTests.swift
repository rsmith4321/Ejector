import AppKit
import Sparkle

@MainActor final class DroneImportManager {
    static let shared = DroneImportManager()
    var busy = false
}
@MainActor final class DriveManager {
    static let shared = DriveManager()
    var isEjecting = false
}

@main struct UpdateSafetyTests {
    @MainActor static func main() throws {
        _ = NSApplication.shared
        let delegate = WebsiteUpdater.shared
        let controller = SPUStandardUpdaterController(startingUpdater: false, updaterDelegate: nil, userDriverDelegate: nil)
        let updater = controller.updater
        let item = SUAppcastItem(dictionary: ["title": "Fixture", "enclosure": ["url": "https://example.invalid/update.zip", "sparkle:version": "2"]])!
        try delegate.updater(updater, mayPerform: .updates)
        for importing in [true, false] {
            DroneImportManager.shared.busy = importing
            DriveManager.shared.isEjecting = !importing
            do { try delegate.updater(updater, mayPerform: .updatesInBackground); fatalError("Busy update check allowed") }
            catch { precondition((error as NSError).domain == "EasyEject.Update") }
            var resumed = 0
            precondition(delegate.updater(updater, shouldPostponeRelaunchForUpdate: item, untilInvokingBlock: { resumed += 1 }))
            RunLoop.current.run(until: Date().addingTimeInterval(1.1))
            precondition(resumed == 0 && delegate.waitingForIdle)
            DroneImportManager.shared.busy = false; DriveManager.shared.isEjecting = false
            RunLoop.current.run(until: Date().addingTimeInterval(1.1))
            precondition(resumed == 1 && !delegate.waitingForIdle)
            RunLoop.current.run(until: Date().addingTimeInterval(1.1))
            precondition(resumed == 1)
        }
        let window = NSWindow(contentRect: NSRect(x: 0,y: 0,width: 200,height: 100), styleMask: [.titled], backing: .buffered, defer: false)
        let sheet = NSWindow(contentRect: NSRect(x: 0,y: 0,width: 100,height: 80), styleMask: [.titled], backing: .buffered, defer: false)
        window.beginSheet(sheet)
        precondition(delegate.updater(updater, shouldPostponeRelaunchForUpdate: item, untilInvokingBlock: {}))
        window.endSheet(sheet)
        RunLoop.current.run(until: Date().addingTimeInterval(0.4))
        print("PASS: manual/background idle gate, active import/eject relaunch protection, single deferred resume, unsaved sheet guard")
    }
}
