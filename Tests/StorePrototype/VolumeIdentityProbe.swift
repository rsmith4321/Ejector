import AppKit

/// Manual signed-sandbox integration probe. Uses only generated disk images.
/// Enroll EE Identity Fixture/DCIM, quit, mount a same-name decoy first, then relaunch.
@MainActor final class IdentityProbeDelegate: NSObject, NSApplicationDelegate {
    var window: NSWindow!
    var output: NSTextView!
    let state = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("identity-state.plist")
    let results = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("identity-results.json")
    func applicationDidFinishLaunching(_ notification: Notification) {
        window = NSWindow(contentRect: NSRect(x: 100, y: 100, width: 720, height: 420), styleMask: [.titled, .closable], backing: .buffered, defer: false)
        window.title = "Generated Volume Identity Test"
        let stack = NSStackView(); stack.orientation = .vertical
        stack.addArrangedSubview(NSButton(title: "Authorize generated DCIM", target: self, action: #selector(enroll)))
        stack.addArrangedSubview(NSButton(title: "Verify saved device", target: self, action: #selector(verify)))
        output = NSTextView(); output.isEditable = false
        stack.addArrangedSubview(output); window.contentView = stack
        window.makeKeyAndOrderFront(nil); NSApp.activate()
    }
    func report(_ values: [String: String]) {
        output.string = values.keys.sorted().map { "\($0): \(values[$0]!)" }.joined(separator: "\n")
        try? JSONSerialization.data(withJSONObject: values, options: [.prettyPrinted, .sortedKeys]).write(to: results)
    }
    @objc func enroll() {
        let expected = URL(fileURLWithPath: "/Volumes/EE Identity Fixture/DCIM")
        let panel = NSOpenPanel(); panel.canChooseDirectories = true; panel.canChooseFiles = false
        panel.directoryURL = expected; panel.prompt = "Authorize Fixture"
        guard panel.runModal() == .OK, let folder = panel.url, folder.standardizedFileURL == expected.standardizedFileURL else { return }
        do {
            guard try String(contentsOf: folder.appendingPathComponent("DJI_001/identity.txt"), encoding: .utf8) == "ENROLLED FIXTURE\n" else { throw ImportFailure("Wrong fixture") }
            let values: [String: Any] = ["id": try ImportVolumes.identity(folder), "path": try ImportVolumes.root(folder).path,
                "bookmark": try folder.bookmarkData(options: [.withSecurityScope], includingResourceValuesForKeys: nil, relativeTo: nil)]
            try PropertyListSerialization.data(fromPropertyList: values, format: .binary, options: 0).write(to: state)
            report(["enrollment": "PASS", "id": values["id"] as! String])
        } catch { report(["error": String(describing: error)]) }
    }
    @objc func verify() {
        do {
            let saved = try PropertyListSerialization.propertyList(from: Data(contentsOf: state), format: nil) as! [String: Any]
            let id = saved["id"] as! String
            let matches = ImportVolumes.mounted().filter { (try? ImportVolumes.identity($0)) == id }
            guard matches.count == 1, let source = matches.first, source.path != saved["path"] as! String else { throw ImportFailure("Expected one matching UUID at a changed mount path") }
            let access = try ScopedFolder(saved["bookmark"] as! Data); defer { access.close() }
            guard try ImportVolumes.identity(access.url) == id,
                  try ImportVolumes.root(access.url).standardizedFileURL == source.standardizedFileURL,
                  MediaImportEngine.isWithin(access.url, source), access.url != source else { throw ImportFailure("Wrong bookmark target") }
            let destination = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("VerifiedImports")
            try FileManager.default.createDirectory(at: destination, withIntermediateDirectories: true)
            let result = try MediaImportEngine(source: source, mediaFolder: access.url, destination: destination,
                deleteOriginals: false, recoverTrash: false, cancellation: ImportCancellation(), validate: {
                    guard try ImportVolumes.identity(source) == id else { throw ImportFailure("Source changed") }
                }, report: { _ in }, audit: { _ in }).run()
            let original = try Data(contentsOf: access.url.appendingPathComponent("DJI_001/identity.txt"))
            let copied = try Data(contentsOf: result.folder.appendingPathComponent("DJI_001/identity.txt"))
            guard result.files == 1, copied == original, String(data: copied, encoding: .utf8) == "ENROLLED FIXTURE\n" else { throw ImportFailure("Wrong import contents") }
            report(["result": "PASS", "oldMount": saved["path"] as! String, "newMount": source.path,
                "resolvedFolder": access.url.path, "uuid": id, "savedBookmarkAfterRelaunch": "PASS",
                "verifiedCopy": "PASS", "originalKept": "PASS", "decoyNotImported": "PASS"])
        } catch { report(["error": String(describing: error)]) }
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}
@main struct VolumeIdentityProbe {
    @MainActor static func main() {
        let app = NSApplication.shared; let delegate = IdentityProbeDelegate()
        app.delegate = delegate; app.setActivationPolicy(.regular); app.run()
        withExtendedLifetime(delegate) { }
    }
}
