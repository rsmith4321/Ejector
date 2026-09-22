import SwiftUI

/// Run in a separately signed sandbox app; never enrolls a device or touches media.
@main struct IssueNoticeProbe: App {
    @NSApplicationDelegateAdaptor(EjectorLifecycle.self) private var lifecycle
    @StateObject private var importer = DroneImportManager.shared
    init() {
        UserDefaults.standard.set(true, forKey: "hasSeenCardAuthorizationGuide")
        UserDefaults.standard.set(false, forKey: "hasSeenImportSetup")
        let importer = DroneImportManager.shared
        precondition(importer.profiles.isEmpty)
        let profile = Self.disconnectedProfile
        let before = importer.profiles
        importer.importNow(profile)
        precondition(importer.hasError && importer.needsAttention && importer.menuTitle.isEmpty)
        let explanation = importer.message
        importer.dismissIssue()
        precondition(importer.hasError && !importer.needsAttention && importer.isIssueDismissed)
        precondition(importer.message == explanation && importer.progress.phase == "Needs attention")
        precondition(!importer.busy && importer.profiles == before)
        importer.refresh()
        precondition(!importer.needsAttention && importer.message == explanation)
        importer.importNow(profile)
        precondition(importer.needsAttention && !importer.isIssueDismissed)
        importer.busy = true
        importer.dismissIssue()
        precondition(importer.needsAttention && !importer.isIssueDismissed)
        importer.busy = false
        importer.dismissIssue()
        importer.recordOperationFailure(ImportFailure("Generated second failure"))
        precondition(importer.needsAttention && importer.message == "Generated second failure")
        importer.hasError = false
        precondition(!importer.needsAttention && !importer.isIssueDismissed)
        importer.message = "Generated test only. No devices enrolled."
        importer.progress = ImportProgress(phase: "Ready")
        let result = "PASS: compact idle title; dismissal retains explanation and failure phase; no retry or profile mutation; refresh preserves dismissal; repeated/new failure restores notice; busy dismissal ignored; success resets dismissal."
        try! result.write(to: FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("issue-tests.txt"), atomically: true, encoding: .utf8)
    }
    static var disconnectedProfile: DroneProfile {
        DroneProfile(id: "GENERATED-NOT-A-VOLUME-UUID", name: "Generated disconnected device", mediaPath: "DCIM",
                     destinationBookmark: Data(), destinationVolumeID: "GENERATED-NOT-A-DESTINATION", destinationLabel: "Generated destination")
    }
    var body: some Scene {
        Window("Issue Notice Test", id: "importsWindow") {
            ProbeContents(importer: importer, lifecycle: lifecycle)
        }.defaultSize(width: 680, height: 760)
        Settings { SettingsView() }
    }
}

private struct ProbeContents: View {
    @ObservedObject var importer: DroneImportManager
    let lifecycle: EjectorLifecycle
    @Environment(\.openWindow) private var openWindow
    @Environment(\.openSettings) private var openSettings
    var body: some View {
        VStack {
            HStack {
                Button("Generate disconnected-device issue") { importer.importNow(IssueNoticeProbe.disconnectedProfile) }
                Button("Show eject menu") { lifecycle.showMenu() }
            }.padding(.top)
            DroneImportView(importer: importer)
        }.onAppear {
            lifecycle.configure(openWindow: { openWindow(id: $0) }, openSettings: { openSettings() })
        }
    }
}
