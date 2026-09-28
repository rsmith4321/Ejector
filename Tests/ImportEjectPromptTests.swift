import AppKit

@main struct ImportEjectPromptTests {
    @MainActor static func main() {
        _ = NSApplication.shared
        for hasMedia in [true, false] {
            let alert = ImportEjectPrompt.make(hasMedia: hasMedia, deviceName: "Test camera")
            precondition(alert.messageText == (hasMedia ? "Import complete. Eject now?" : "No media found. Eject now?"))
            precondition(alert.buttons.map(\.title) == ["Keep Connected", "Eject Now"])
            precondition(alert.buttons[0].keyEquivalent == "\r")
            precondition(alert.buttons[1].keyEquivalent.isEmpty)
            precondition(alert.informativeText.contains("Test camera") && alert.informativeText.contains("all its partitions"))
            if !hasMedia { precondition(alert.informativeText.contains("Photos, skipped previews")) }
        }
        let grouped = ImportEjectPrompt.make(hasMedia: false, deviceName: "Insta360", sourceName: "Internal", storageSources: ["Internal", "SD Card"])
        precondition(grouped.buttons.map(\.title) == ["Keep Connected", "Eject Camera"])
        precondition(grouped.buttons[0].keyEquivalent == "\r" && grouped.buttons[1].keyEquivalent.isEmpty)
        precondition(grouped.informativeText.contains("No matching media on Internal."))
        precondition(grouped.informativeText.contains("SD Card") && grouped.informativeText.contains("files to import"))
        for button in [0, 1] {
            let alert = ImportEjectPrompt.make(hasMedia: button == 0, deviceName: "Test camera")
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
                if let directory = ProcessInfo.processInfo.environment["EJECT_PROMPT_RENDER_DIRECTORY"] {
                    let capture = Process()
                    capture.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
                    capture.arguments = ["-x", "-l", String(alert.window.windowNumber), URL(fileURLWithPath: directory).appendingPathComponent(button == 0 ? "import-complete-prompt.png" : "no-media-prompt.png").path]
                    try? capture.run(); capture.waitUntilExit()
                }
                alert.buttons[button].performClick(nil)
            }
            let response = alert.runModal()
            precondition(response == (button == 0 ? .alertFirstButtonReturn : .alertSecondButtonReturn))
        }
        print("PASS import-complete and no-media prompts; only explicit second button ejects; default keeps connected")
    }
}
