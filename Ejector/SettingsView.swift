import SwiftUI
import ServiceManagement
import UserNotifications

struct SettingsView: View {
    @AppStorage("cleanCardsOnEject") private var cleanCards = false
    @AppStorage("warnBeforeEjectingSSD") private var warnSSD = true
    @AppStorage("isShortcutEnabled") private var shortcutEnabled = false
    @AppStorage("shortcutKeyCode") private var shortcutKey = 14
    @AppStorage("showEjectNotifications") private var notifications = true
    @AppStorage("enableDebugLogs") private var debug = false
    @ObservedObject private var shortcut = GlobalHotkeyManager.shared
    #if APP_STORE
    @ObservedObject private var access = CardAccess.shared
    #endif
    @Environment(\.scenePhase) private var scenePhase
    @State private var loginStatus = SMAppService.mainApp.status
    @State private var loginError: String?
    @State private var notificationStatus = ""
    @State private var confirmCleanup = false
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("General").font(.headline)
                Toggle("Launch at Login", isOn: Binding(get: { loginStatus == .enabled }, set: { enabled in
                    do {
                        if enabled { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
                        loginError = nil
                    } catch { loginError = error.localizedDescription }
                    loginStatus = SMAppService.mainApp.status
                }))
                if loginStatus == .requiresApproval {
                    Button("Review Login Permission") { SMAppService.openSystemSettingsLoginItems() }
                }
                if let loginError { Text(loginError).foregroundStyle(.orange) }
                Toggle("Clean Cards Before Ejecting", isOn: Binding(get: { cleanCards }, set: {
                    if $0 { confirmCleanup = true } else { cleanCards = false }
                }))
                Text("Removes hidden macOS metadata from cards. Keeps your photos and videos.")
                    .font(.caption).foregroundStyle(.secondary)
                Toggle("Confirm Before Ejecting SSDs", isOn: $warnSSD)
                Toggle("Show Eject Notifications", isOn: $notifications).onChange(of: notifications) { _, enabled in
                    if enabled {
                        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { allowed, error in
                            DispatchQueue.main.async { notificationStatus = error?.localizedDescription ?? (allowed ? "Notifications enabled." : "Enable Easy Eject in System Settings > Notifications.") }
                        }
                    }
                }
                if !notificationStatus.isEmpty { Text(notificationStatus).font(.caption) }
                Divider()
                Text("Keyboard Shortcut").font(.headline)
                Toggle("Enable Global Eject Shortcut", isOn: $shortcutEnabled).onChange(of: shortcutEnabled) { _, _ in shortcut.start() }
                HStack {
                    Text("Shortcut Letter (⌃⌥⌘ +)")
                    Picker("Shortcut Letter", selection: $shortcutKey) {
                        ForEach(GlobalHotkeyManager.availableKeys, id: \.code) { key in Text(key.name).tag(key.code) }
                    }.labelsHidden().frame(width: 90)
                }.disabled(!shortcutEnabled).onChange(of: shortcutKey) { _, _ in shortcut.start() }
                Text(shortcut.status).font(.caption).foregroundStyle(.secondary)
                Divider()
                Text("Permissions").font(.headline)
                #if APP_STORE
                FolderAuthorizationInstructions()
                Button("Authorize a Card…") { access.authorize() }
                ForEach(access.grants) { grant in
                    HStack { Text(grant.name); Spacer(); Button("Forget") { access.forget(grant.id) } }
                }
                if let error = access.error { Text(error).font(.caption).foregroundStyle(.orange) }
                #else
                Text("The website version does not need Authorize a Card for detection or cleaning, including after camera formatting. macOS may still ask to access removable or network drives. If access is denied, check Privacy & Security → Files and Folders in System Settings.")
                    .font(.system(size: 14)).fixedSize(horizontal: false, vertical: true)
                Button("Open Privacy Settings") {
                    NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy")!)
                }
                #endif
                Divider()
                Toggle("Enable Debug Logging", isOn: $debug)
                HStack {
                    Link("Privacy", destination: URL(string: "https://easyeject.com/privacy/")!)
                    Link("Website", destination: URL(string: "https://easyeject.com/")!)
                    Spacer()
                    Button("Done") {
                        NSApp.windows.first { $0.identifier?.rawValue == "com_apple_SwiftUI_Settings_window" }?.close()
                    }.keyboardShortcut(.defaultAction)
                }
            }.padding(24)
        }.frame(width: 480, height: 640)
        .alert("Clean cards before ejecting?", isPresented: $confirmCleanup) {
            Button("Cancel", role: .cancel) { }
            Button("Enable Metadata Cleanup", role: .destructive) { cleanCards = true }
        } message: {
            Text("This permanently removes hidden macOS metadata from cards when you eject them. It does not delete original photos or recordings. Keep backups of important data.")
        }
        .onChange(of: scenePhase) { _, phase in if phase == .active { loginStatus = SMAppService.mainApp.status } }
    }
}
