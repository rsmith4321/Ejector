import AppKit

@MainActor enum UpdateChannel {
    static func checkForUpdates() {
        #if APP_STORE
        NSWorkspace.shared.open(URL(string: "macappstore://apps.apple.com/app/id6767951388")!)
        #else
        guard let url = URL(string: "https://api.github.com/repos/rsmith4321/Ejector/releases/latest") else { return }
        URLSession.shared.dataTask(with: url) { data, _, error in
            guard let data = data,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let tagName = json["tag_name"] as? String else {
                DispatchQueue.main.async {
                    let alert = NSAlert()
                    alert.messageText = "Unable to Check for Updates"
                    alert.informativeText = "Could not connect to GitHub. Check your internet connection and try again."
                    alert.alertStyle = .warning
                    alert.addButton(withTitle: "OK")
                    NSApp.activate()
                    alert.runModal()
                }
                return
            }

            let remoteVersion = tagName.trimmingCharacters(in: CharacterSet(charactersIn: "vV"))
            let currentVersion = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0"

            DispatchQueue.main.async {
                if remoteVersion.compare(currentVersion, options: .numeric) == .orderedDescending {
                    let alert = NSAlert()
                    alert.messageText = "Update Available"
                    alert.informativeText = "Easy Eject v\(remoteVersion) is available. You're currently running v\(currentVersion)."
                    alert.alertStyle = .informational
                    alert.addButton(withTitle: "Download")
                    alert.addButton(withTitle: "Later")
                    NSApp.activate()
                    if alert.runModal() == .alertFirstButtonReturn {
                        if let downloadURL = URL(string: "https://github.com/rsmith4321/Ejector/releases/latest") {
                            NSWorkspace.shared.open(downloadURL)
                        }
                    }
                } else {
                    let alert = NSAlert()
                    alert.messageText = "You're Up to Date"
                    alert.informativeText = "Easy Eject v\(currentVersion) is the latest version."
                    alert.alertStyle = .informational
                    alert.addButton(withTitle: "OK")
                    NSApp.activate()
                    alert.runModal()
                }
            }
        }.resume()
        #endif
    }

}
