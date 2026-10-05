import AppKit
import Combine
import UserNotifications

final class LogManager {
    static let shared = LogManager()
    func log(_ message: String) { print(message); fflush(stdout) }
}

/// Own signed fixture only: generated MP4, disposable disk image, isolated support/defaults.
@main struct ManualQuickShareIntegrationTests {
    @MainActor static func main() {
        _ = NSApplication.shared
        setbuf(stdout, nil)
        guard CommandLine.arguments.count == 3 else { fatalError("Pass fixture volume and isolated support directory") }
        let source = URL(fileURLWithPath: CommandLine.arguments[1])
        let support = URL(fileURLWithPath: CommandLine.arguments[2])
        precondition(source.lastPathComponent.hasPrefix("EE-QuickShare-Test-"))
        precondition(support.lastPathComponent == "support" && support.deletingLastPathComponent().lastPathComponent.hasPrefix("ee-quick-share-integration-"))
        UserDefaults.standard.set(false, forKey: "showEjectNotifications")
        UserDefaults.standard.set(false, forKey: ImportDefaults.automaticImportKey)
        let manager = DroneImportManager(supportDirectory: support)
        let destination = support.appendingPathComponent("destination")
        try! FileManager.default.createDirectory(at: destination, withIntermediateDirectories: true)
        var profile = DroneProfile(id: try! ImportVolumes.identity(source), name: "Generated Luna fixture",
            mediaPath: "DCIM", destinationBookmark: try! destination.bookmarkData(),
            destinationVolumeID: try! ImportVolumes.identity(destination), destinationLabel: destination.path,
            autoEject: true, videosOnly: true, cleanLayout: true, sharingPreset: .hd, sharingColor: .lunaILog)
        manager.save(profile)
        var importDialogSeen = false
        var shareDialogSeen = false
        var makeCopies = false
        var choose4K = false
        var keptConnectedAfterShare = false
        let timer = Timer(timeInterval: 0.05, repeats: true) { _ in
            MainActor.assumeIsolated {
                var buttons: [NSButton] = []
                var sizes: [NSPopUpButton] = []
                func visit(_ view: NSView) {
                    if let size = view as? NSPopUpButton { sizes.append(size) }
                    if let button = view as? NSButton { buttons.append(button) }
                    for child in view.subviews { visit(child) }
                }
                for window in NSApp.windows { if let view = window.contentView { visit(view) } }
                if let button = buttons.first(where: { $0.title == "Make Quick Share Copies" }) {
                    shareDialogSeen = true
                    precondition(button.keyEquivalent.isEmpty)
                    if makeCopies {
                        sizes.first?.selectItem(at: choose4K ? 1 : 0)
                        buttons.first(where: { $0.title == "Apply Insta360 Luna I-Log LUT" })?.state = choose4K ? .off : .on
                        button.performClick(nil)
                    } else {
                        let cancel = buttons.first(where: { $0.title == "Cancel" })!
                        print("Cancel key at presentation:", cancel.keyEquivalent.debugDescription)
                        cancel.performClick(nil)
                    }
                } else if let button = buttons.first(where: { $0.title == "Quick Share…" }) {
                    importDialogSeen = true
                    precondition(button.keyEquivalent.isEmpty)
                    print("Keep Connected key at presentation:", buttons.first(where: { $0.title == "Keep Connected" })!.keyEquivalent.debugDescription)
                    if makeCopies {
                        keptConnectedAfterShare = true
                        buttons.first(where: { $0.title == "Keep Connected" })!.performClick(nil)
                    } else { button.performClick(nil) }
                }
            }
        }
        RunLoop.main.add(timer, forMode: .common); RunLoop.main.add(timer, forMode: .modalPanel)
        Task { @MainActor in
            @MainActor func waitUntil(_ condition: () -> Bool) async {
                let deadline = Date().addingTimeInterval(40)
                while !condition() {
                    if Date() >= deadline { fatalError("Timed out: \(manager.message)") }
                    try! await Task.sleep(for: .milliseconds(30))
                }
            }
            manager.refresh(); manager.volumes = [source]
            manager.importNow(profile)
            await waitUntil { importDialogSeen && shareDialogSeen && !manager.busy && manager.completedImports.count == 1 }
            let batch = manager.completedImports[0]
            let original = batch.originals[0]
            precondition(!manager.hasError && manager.pendingEjects.count == 1)
            precondition(!FileManager.default.fileExists(atPath: batch.folder.appendingPathComponent("Sharing Copies").path))
            precondition(FileManager.default.fileExists(atPath: source.appendingPathComponent("DCIM/VID_generated.mp4").path))
            let logURL = support.appendingPathComponent("imports.log")
            precondition(!(try! String(contentsOf: logURL, encoding: .utf8)).contains("User requested Quick Share"))
            print("PASS enabled LUT import offers third action but never exports automatically; cancelling Quick Share leaves source connected and originals unchanged")
            profile.sharingPreset = nil; manager.save(profile)
            precondition(manager.quickShareImports.isEmpty)
            profile.sharingPreset = .hd; manager.save(profile)
            precondition(manager.quickShareImports.count == 1)
            print("PASS enabling LUT after completion makes the exact completed batch available; disabling hides the action")
            makeCopies = true
            manager.quickShareCompletedImport(batch.id, returnToEject: true)
            await waitUntil { !manager.busy && keptConnectedAfterShare }
            precondition(!manager.hasError && FileManager.default.fileExists(atPath: source.path) && manager.pendingEjects.count == 1)
            print("PASS successful Quick Share returns to a fresh eject choice without ejecting automatically")
            manager.ejectCompletedImport(batch.id)
            await waitUntil { !manager.busy && !FileManager.default.fileExists(atPath: source.path) }
            precondition(manager.completedImports == [batch] && manager.pendingEjects.isEmpty)
            let restored = DroneImportManager(supportDirectory: support)
            precondition(restored.completedImports == [batch] && restored.pendingEjects.isEmpty && restored.quickShareImports.count == 1)
            print("PASS source ejection and manager restart retain verified destination manifest without restoring eject authority")
            makeCopies = true
            restored.quickShareCompletedImport(batch.id)
            precondition(restored.busy && restored.blocksEject(try! ImportVolumes.root(destination)))
            restored.quickShareCompletedImport(batch.id) // Busy repeated action cannot start another dialog/job.
            await waitUntil { !restored.busy }
            precondition(!restored.hasError, restored.message)
            let tools = try! EasyShareTools.installed()
            let engine = EasyShareEngine(tools: tools, preset: .hd, color: .lunaILog, cancellation: ImportCancellation(), validate: {}, report: { _ in }, audit: { _ in })
            let hd = batch.folder.appendingPathComponent("Sharing Copies/VID_generated-sharing-1080p.mp4")
            let hdReceipt = try! JSONDecoder().decode(EasyShareEngine.Receipt.self, from: Data(contentsOf: EasyShareEngine.receiptURL(hd)))
            precondition(hdReceipt.color == .lunaILog && hdReceipt.lutSHA256 == EasyShareTools.officialLUTSHA)
            precondition(try! MediaImportEngine.hash(original.url, cancellation: ImportCancellation(), progress: { _ in }) == original.sha256)
            print("PASS manual 1080p action applies official LUT after ejection, reserves destination, keeps originals and completes validated export")
            choose4K = true
            restored.quickShareCompletedImport(batch.id)
            await waitUntil { !restored.busy }
            precondition(!restored.hasError, restored.message)
            let uhd = batch.folder.appendingPathComponent("Sharing Copies/VID_generated-sharing-4K.mp4")
            let uhdReceipt = try! JSONDecoder().decode(EasyShareEngine.Receipt.self, from: Data(contentsOf: EasyShareEngine.receiptURL(uhd)))
            precondition(uhdReceipt.preset == .uhd && uhdReceipt.color == .standard && uhdReceipt.lutSHA256 == nil)
            precondition(try! engine.probe(uhd).video?.width == 640) // No upscaling.
            print("PASS same manual dialog chooses 4K with LUT off for normal color; existing 1080p copy retained")
            let changed = try! FileHandle(forWritingTo: original.url)
            try! changed.seekToEnd(); try! changed.write(contentsOf: Data("fixture changed".utf8)); try! changed.close()
            restored.quickShareCompletedImport(batch.id)
            await waitUntil { !restored.busy }
            precondition(restored.hasError && restored.message.contains("changed before Easy Share"))
            precondition(restored.quickShareImports.count == 1 && FileManager.default.fileExists(atPath: hd.path))
            print("PASS altered saved original stops manual export and preserves prior copies and retry history")
            let grouped = ImportEjectPrompt.make(hasMedia: true, deviceName: "Fixture", storageSources: ["SD card", "Internal storage"], quickShare: true)
            precondition(grouped.buttons.map(\.title) == ["Keep Connected", "Eject Camera", "Quick Share…"])
            precondition(ImportEjectPrompt.make(hasMedia: true, deviceName: "Other").buttons.count == 2)
            print("PASS grouped-camera eject keeps explicit second-button authorization; non-LUT devices retain two-button dialog")
            timer.invalidate()
            print("8 manual Quick Share integration checks passed")
            exit(0)
        }
        NSApp.run()
    }
}
