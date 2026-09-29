import AppKit

@MainActor enum UpdateChannel {
    static func start() {
        #if !APP_STORE
        WebsiteUpdater.shared.start()
        #endif
    }

    static func checkForUpdates() {
        #if APP_STORE
        NSWorkspace.shared.open(URL(string: "macappstore://apps.apple.com/app/id6767951388")!)
        #else
        WebsiteUpdater.shared.checkForUpdates()
        #endif
    }
}

#if !APP_STORE
import Sparkle
import Combine
import SwiftUI

@MainActor final class WebsiteUpdater: NSObject, ObservableObject, SPUUpdaterDelegate {
    static let shared = WebsiteUpdater()
    @Published private(set) var canCheck = false
    @Published private(set) var automaticallyChecks = true
    @Published private(set) var waitingForIdle = false
    private lazy var controller = SPUStandardUpdaterController(startingUpdater: false, updaterDelegate: self, userDriverDelegate: nil)
    private var resumeTimer: Timer?
    private var started = false

    private var canRestart: Bool {
        !DroneImportManager.shared.busy && !DriveManager.shared.isEjecting &&
        NSApp.modalWindow == nil && !NSApp.windows.contains { !$0.sheets.isEmpty }
    }

    func start() {
        guard !started else { return }
        started = true
        controller.updater.publisher(for: \.canCheckForUpdates).assign(to: &$canCheck)
        controller.updater.publisher(for: \.automaticallyChecksForUpdates).assign(to: &$automaticallyChecks)
        // Installation always requires the user's choice; never collect a system profile.
        controller.updater.automaticallyDownloadsUpdates = false
        controller.updater.sendsSystemProfile = false
        controller.startUpdater()
    }

    func setAutomaticChecks(_ enabled: Bool) {
        controller.updater.automaticallyChecksForUpdates = enabled
    }

    func checkForUpdates() {
        guard canCheck else { return }
        controller.checkForUpdates(nil)
    }

    func updater(_ updater: SPUUpdater, mayPerform updateCheck: SPUUpdateCheck) throws {
        guard canRestart else {
            throw NSError(domain: "EasyEject.Update", code: 1,
                          userInfo: [NSLocalizedDescriptionKey: "Finish the current import, eject, or open dialog before checking for updates."])
        }
    }

    func updater(_ updater: SPUUpdater, shouldPostponeRelaunchForUpdate item: SUAppcastItem,
                 untilInvokingBlock installHandler: @escaping () -> Void) -> Bool {
        guard !canRestart else { return false }
        waitingForIdle = true
        resumeTimer?.invalidate()
        resumeTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] timer in
            MainActor.assumeIsolated {
                guard let self else { timer.invalidate(); return }
                guard self.canRestart else { return }
                timer.invalidate(); self.resumeTimer = nil; self.waitingForIdle = false
                installHandler()
            }
        }
        return true
    }

    // Do not use updaterShouldRelaunchApplication as an activity guard: Sparkle
    // calls it before the postponement hook and would abort the approved update.
    // applicationShouldTerminate performs the final busy check without cancelling work.
}

struct UpdateSettingsView: View {
    @ObservedObject private var updater = WebsiteUpdater.shared
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("App updates").font(.headline)
            Toggle("Automatically check for updates", isOn: Binding(
                get: { updater.automaticallyChecks }, set: { updater.setAutomaticChecks($0) }))
            Text("Checks GitHub for signed updates. You choose when to install. Easy Eject waits for imports, ejection and open dialogs to finish before restarting.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            if updater.waitingForIdle {
                Text("Update ready. Waiting for Easy Eject to finish its current work.").font(.caption)
            }
            Button("Check for Updates…") { updater.checkForUpdates() }.disabled(!updater.canCheck)
        }
    }
}
#endif
