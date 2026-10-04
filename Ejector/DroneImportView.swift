import SwiftUI

struct DroneImportView: View {
    @ObservedObject var importer: DroneImportManager
    @State private var editing: DroneProfile?
    @State private var selectedSource = ""
    @State private var setupError: String?
    @State private var showingDefaults = false
    @StoredImportDefaults private var defaults

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                HStack {
                    Label("Device media imports", systemImage: "arrow.down.doc")
                        .font(.title2.bold())
                    Spacer()
                    Button("Import defaults…") { showingDefaults = true }
                    Button("Refresh") { importer.refresh() }.disabled(importer.busy)
                }
                GroupBox {
                    VStack(alignment: .leading, spacing: 8) {
                        HStack {
                            if !importer.busy && importer.hasError {
                                Label(importer.isIssueDismissed ? "Last issue (dismissed)" : "Needs attention",
                                      systemImage: importer.isIssueDismissed ? "info.circle" : "exclamationmark.triangle")
                                    .font(.headline)
                            } else {
                                Text(importer.busy ? "\(importer.activeName) · \(importer.progress.phase == "Checking" ? "Importing" : importer.progress.phase)" : importer.progress.phase).font(.headline)
                            }
                            Spacer()
                            if importer.busy { Button("Stop import") { importer.cancel() }.disabled(importer.progress.phase == "Ejecting") }
                            else if importer.needsAttention {
                                Button("Dismiss") { importer.dismissIssue() }
                                    .help("Hide the warning icon. The explanation stays here; nothing is retried.")
                            }
                        }
                        if importer.busy {
                            if importer.progress.total > 0 {
                                ProgressView(value: importer.progress.fraction)
                                Text("\(importer.progress.completed) of \(importer.progress.total) files completed · \(importer.progress.file)")
                                    .font(.caption).lineLimit(2)
                            } else { ProgressView().controlSize(.small) }
                        } else {
                            Text(importer.message).foregroundStyle(importer.needsAttention ? .orange : .secondary)
                                .id(importer.message)
                                .textSelection(.enabled).fixedSize(horizontal: false, vertical: true)
                        }
                        HStack {
                            Button("Open import folder") { importer.openFolder() }.disabled(importer.lastFolder == nil)
                            Button("Show import log") { importer.openLog() }
                        }
                        ForEach(importer.pendingEjects) { completion in
                            Button("Eject Now · \(completion.deviceName)") { importer.ejectCompletedImport(completion.id) }
                                .disabled(importer.busy)
                                .help("Eject this device and all its partitions. Leave connected to import photos in Lightroom.")
                        }
                    }.frame(maxWidth: .infinity, alignment: .leading).padding(6)
                }
                VStack(alignment: .leading, spacing: 12) {
                        if importer.profiles.isEmpty {
                            ContentUnavailableView("No devices enrolled", systemImage: "sdcard", description: Text("Choose a connected device below. New profiles follow your import defaults."))
                                .frame(maxWidth: .infinity)
                                .multilineTextAlignment(.center)
                        }
                        ForEach(importer.profiles) { storedProfile in
                            let profile = storedProfile.resolved(using: defaults)
                            GroupBox {
                                VStack(alignment: .leading, spacing: 6) {
                                    HStack {
                                        Text(profile.name).font(.headline)
                                        Spacer()
                                        Text((profile.followsImportDefaults ? "Uses defaults · " : "Custom · ") + (profile.enabled ? "Automatic" : "Manual")).font(.caption).foregroundStyle(.secondary)
                                    }
                                    Text("\(profile.mediaPath) → \(profile.destinationLabel)/YYYY-MM-DD").font(.caption).lineLimit(2)
                                    Text((profile.videosOnly == true ? "Videos only · " : "All media · ") + (profile.deleteOriginals ? "Delete imported originals after verification" : "Keep originals on device")).font(.caption)
                                    HStack {
                                        Button("Import now") { importer.importNow(storedProfile) }
                                        Button("Edit") { editing = storedProfile }
                                        Spacer()
                                        Button("Remove profile") { importer.remove(profile.id) }
                                    }.disabled(importer.busy)
                                }.frame(maxWidth: .infinity, alignment: .leading).padding(4)
                            }
                        }
                }.frame(maxWidth: .infinity)
                Divider()
                VStack(alignment: .leading, spacing: 10) {
                    Text("Add an import device").font(.headline)
                    Text("Connect a camera, memory card, or FPV device. If several drives have the same name, connect only the device you want to set up.")
                        .font(.callout).foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                    HStack(alignment: .firstTextBaseline, spacing: 12) {
                        Text("Device")
                        Picker("Device", selection: $selectedSource) {
                            Text("Choose a device…").tag("")
                            ForEach(importer.volumes, id: \.path) { volume in
                                Text(volume.lastPathComponent).tag(volume.path)
                            }
                        }
                        .labelsHidden()
                        .accessibilityLabel("Device")
                        .frame(maxWidth: .infinity, alignment: .leading)
                        Button("Set up import…", action: enroll).disabled(selectedSource.isEmpty || importer.busy)
                    }
                    Text("First choose the device’s recording folder, then where to save copies. Review the settings before saving; setup does not start an import.")
                        .font(.callout).foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }.disabled(importer.busy)
                Text("Your saved device is recognized even as Untitled 2. Its recording folder alone does not identify it. Set up again after formatting, and connect the destination before importing.")
                    .font(.callout).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(22)
        }
        .frame(minWidth: 620, minHeight: 580)
        .onChange(of: importer.volumes) { _, volumes in
            if !volumes.contains(where: { $0.path == selectedSource }) { selectedSource = "" }
        }
        .sheet(isPresented: $showingDefaults) {
            ImportDefaultsSheet()
        }
        .sheet(item: $editing) { profile in
            DroneProfileEditor(profile: profile) { importer.save($0); editing = nil }
        }
        .alert("Import setup", isPresented: Binding(get: { setupError != nil }, set: { if !$0 { setupError = nil } })) {
            Button("OK") { setupError = nil }
        } message: { Text(setupError ?? "") }
    }

    private func enroll() {
        NSApp.activate()
        do {
            let source = URL(fileURLWithPath: selectedSource)
            let id = try ImportVolumes.identity(source)
            if let existing = importer.profiles.first(where: { $0.id == id }) { editing = existing; return }
            guard let window = NSApp.windows.first(where: { $0.identifier?.rawValue == "importsWindow" }) else { return }
            let media = FolderSelectionPanel.make()
            media.title = "Step 1 of 2: Choose recordings"
            // Long message lines squeeze the native picker's sidebar, even in a wide panel.
            media.message = "Step 1 of 2: Choose the recording folder.\nSelect DCIM or VIDEO for ordinary clips.\nFor structured cinema formats, choose the whole card.\nNext, choose where to save copies."
            media.prompt = "Use recording folder"
            media.canChooseDirectories = true; media.canChooseFiles = false
            media.directoryURL = source
            media.beginSheetModal(for: window) { response in
                guard response == .OK, let folder = media.url else { return }
                do {
                    try MediaImportEngine.checkPath(folder)
                    guard MediaImportEngine.isWithin(folder, source) else { throw ImportFailure("Select a media folder or the whole chosen device.") }
                    DispatchQueue.main.async {
                        let destination = ImportFolderPanels.destination(
                            message: "Step 2 of 2: Choose where to save copies.\nUse your Mac or another drive.\nCopies go into dated subfolders.")
                        destination.beginSheetModal(for: window) { response in
                            guard response == .OK, let target = destination.url else { return }
                            do {
                                try MediaImportEngine.checkPath(target)
                                let destinationID = try ImportVolumes.identity(target)
                                guard destinationID != id else { throw ImportFailure("Choose a destination on a different disk.") }
                                let bookmark = try target.bookmarkData(options: [.withSecurityScope], includingResourceValuesForKeys: nil, relativeTo: nil)
                                editing = ImportDefaults().newProfile(id: id, name: source.lastPathComponent,
                                    mediaPath: String(folder.path.dropFirst(source.path.count + 1)), destinationBookmark: bookmark,
                                    destinationVolumeID: destinationID, destinationLabel: target.path,
                                    sourceBookmark: try folder.bookmarkData(options: [.withSecurityScope], includingResourceValuesForKeys: nil, relativeTo: nil))
                            } catch { setupError = error.localizedDescription }
                        }
                    }
                } catch { setupError = error.localizedDescription }
            }
        } catch { setupError = error.localizedDescription }
    }

}

struct DroneProfileEditor: View {
    @State var profile: DroneProfile
    let save: (DroneProfile) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var error: String?
    @State private var showingPermanentDeletionConfirmation = false
    @State private var showingTrashConfirmation = false
    @StoredImportDefaults private var defaults
    var body: some View {
        VStack(spacing: 0) {
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    Text("Import profile").font(.title2.bold())
                    TextField("Device name", text: $profile.name)
                    Text("Give this profile a recognizable name, such as Insta360, Sony, or DJI O4. The card's name in Finder can change.")
                        .font(.callout).foregroundStyle(.secondary)
                    Text("Media folder: \(profile.mediaPath.isEmpty ? "Whole device" : profile.mediaPath)").font(.callout)
                    Text("Save copies to: \(profile.destinationLabel)/YYYY-MM-DD").font(.caption).textSelection(.enabled)
                    Button("Change destination…", action: chooseDestination)
                    Button("Change media folder…", action: chooseSource)
                    Divider()
                    GroupBox {
                        VStack(alignment: .leading, spacing: 12) {
                            Picker("Import settings", selection: Binding(
                                get: { !profile.followsImportDefaults },
                                set: { profile.setCustomImportSettings($0, defaults: defaults) }
                            )) {
                                Text("Use defaults").tag(false)
                                Text("Use custom settings").tag(true)
                            }
                            .pickerStyle(.radioGroup)
                            Text(profile.followsImportDefaults
                                ? "Follows your import defaults."
                                : "Applies only to this device.")
                                .font(.caption).foregroundStyle(.secondary)
                            Divider()
                            VStack(alignment: .leading, spacing: 12) {
                                Picker("Import", selection: Binding(get: { profile.videosOnly ?? false }, set: { value in
                                    // Broadening a video-only profile must not silently enable photo deletion.
                                    if !value && profile.videosOnly == true { profile.deleteOriginals = false; profile.recoverTrash = false }
                                    profile.videosOnly = value
                                })) {
                                    Text("All media and sidecars").tag(false)
                                    Text("Videos only").tag(true)
                                }
                                Text(profile.videosOnly == true
                                    ? "Using Lightroom for photos? Import videos and their recognized companions here. Photos, photo sidecars, and unrecognized files stay on the device, even with deletion or Trash recovery enabled. Recognized camera packages include their support files; ambiguous packages require All media."
                                    : "Copies media and sidecars. Unfamiliar formats keep their camera folders and remain on the device. The Insta360 device index is backed up separately and retained.")
                                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                                Toggle("Include camera previews", isOn: Binding(get: { profile.includePreviews ?? true }, set: { profile.includePreviews = $0 }))
                                Text("Includes LRV, LRF, THM and recognized proxy folders. Turn off for full-quality recordings without optional previews. Skipped previews stay on the device. Required camera-package files are always kept; some editor/playback features need previews.")
                                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                                Toggle("Clean folder layout", isOn: Binding(get: { profile.cleanLayout ?? false }, set: { profile.cleanLayout = $0 }))
                                Text("Ordinary media goes directly in the dated folder with original names and companions. Conflicts use Additional media; structured or unfamiliar formats keep Camera originals folders. Turn off to preserve ordinary camera folders too.")
                                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                                Toggle("Import automatically when connected", isOn: $profile.enabled)
                                Toggle("Ask to eject after import", isOn: $profile.autoEject)
                                Text("After import or when no media matches your options, a dialog asks whether to eject. Keep Connected is the default so you can finish importing in Lightroom. Choose Eject Now and wait for confirmation that the device is safe to unplug. Eject is also available in the menu.")
                                    .font(.caption).foregroundStyle(.secondary)
                                Text("Choosing Eject Now also unmounts the other partitions on that disk. Keep it connected until you have finished all desired imports.")
                                    .font(.caption).foregroundStyle(.secondary)
                            }
                            .disabled(profile.followsImportDefaults)
                            .foregroundStyle(profile.followsImportDefaults ? .secondary : .primary)
                        }.padding(6)
                    }
                    #if !APP_STORE
                    EasyShareProfileOptions(profile: $profile)
                    #endif
                    Text("Originals on this device").font(.headline)
                    if profile.followsImportDefaults {
                        Text("These choices also follow your import defaults. Choose Use custom settings to change them for this device.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    Group {
                        Toggle(profile.videosOnly == true ? "Permanently delete imported videos" : "Permanently delete imported originals", isOn: Binding(
                            get: { profile.deleteOriginals },
                            set: { enabled in
                                if enabled && !profile.deleteOriginals {
                                    showingPermanentDeletionConfirmation = true
                                } else if !enabled {
                                    profile.deleteOriginals = false
                                }
                            }
                        ))
                        VStack(alignment: .leading, spacing: 4) {
                            Text(profile.deleteOriginals ? "Permanent deletion is on" : "Keep originals is on (recommended)")
                                .font(.caption.weight(.semibold))
                            Text(profile.deleteOriginals
                                ? "Only recognized imported media and companions are removed after the selected batch passes saved-copy verification. They skip Trash and cannot be restored from it. Use only for unimportant or replaceable footage."
                                : "Easy Eject saves verified copies and leaves the files on your device. Keep this setting for client work and important photos or videos.")
                                .font(.caption).foregroundStyle(.secondary)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                        Toggle("Recover and clear this device’s Trash", isOn: Binding(get: { profile.recoverTrash }, set: {
                            if $0 { showingTrashConfirmation = true } else { profile.recoverTrash = false }
                        }))
                        Text("Recovers and verifies files matching your import choice before removing them from this device’s Trash. Videos only leaves photos and unrecognized files in Trash.")
                            .font(.caption).foregroundStyle(.secondary)
                        #if APP_STORE
                        Text("Requires authorization for this card, in addition to the media folder. If permission is unavailable, import stops without claiming completion.")
                            .font(.caption).foregroundStyle(.secondary)
                        #endif
                    }.disabled(profile.followsImportDefaults)
                    if let error { Text(error).foregroundStyle(.orange) }
                }.padding(24)
            }
            Divider()
            VStack(alignment: .leading, spacing: 10) {
                Text("Saving applies to the next connection. Use Import now to start with the connected device.")
                    .font(.caption).foregroundStyle(.secondary)
                HStack {
                    Button("Cancel") { dismiss() }.keyboardShortcut(.cancelAction)
                    Spacer()
                    Button("Save profile") { save(profile) }.buttonStyle(.borderedProminent)
                        .keyboardShortcut(.defaultAction)
                        .disabled(profile.name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                }
            }.padding(.horizontal, 24).padding(.vertical, 16)
        }.frame(width: 520, height: 700)
        .onAppear { profile = profile.resolved(using: defaults) }
        .onChange(of: defaults) { _, value in profile = profile.resolved(using: value) }
        .alert("Recover and clear device Trash?", isPresented: $showingTrashConfirmation) {
            Button("Cancel", role: .cancel) { }
            Button("Enable Verified Trash Recovery", role: .destructive) {
                #if APP_STORE
                do {
                    guard let volume = ImportVolumes.mounted().first(where: { (try? ImportVolumes.identity($0)) == profile.id }) else {
                        throw ImportFailure("Connect the original card before authorizing Trash recovery.")
                    }
                    let permission = try CardAccess.shared.require(volume)
                    permission.close()
                    profile.recoverTrash = true
                } catch { self.error = error.localizedDescription }
                #else
                profile.recoverTrash = true
                #endif
            }
        } message: {
            Text("On each import, files matching your import choice in your Trash on this device are copied to the destination and verified before being permanently removed from the device's Trash. Keep independent backups. No recovery runs until you save this profile and start an import.")
        }
        .alert("Enable permanent deletion?", isPresented: $showingPermanentDeletionConfirmation) {
            Button("Cancel", role: .cancel) { }
                .keyboardShortcut(.defaultAction)
            Button("Enable permanent deletion", role: .destructive) {
                profile.deleteOriginals = true
            }
        } message: {
            Text("After the selected batch passes saved-copy verification, Easy Eject removes only imported originals directly from the device. Videos only leaves photos and unrecognized files untouched. This skips Trash and cannot be undone. Skipped preview files remain on the device; use the camera to clear any leftover previews.\n\nUse this only for unimportant or replaceable media. Keep originals for client work and other important photos or videos, and keep independent backups.\n\nSony and some other cameras maintain a media database. Deleting outside the camera may leave that database inconsistent and require its recovery function. Keep originals if unsure; use the camera to erase or format after backup.\n\nThe developer is not responsible for lost files.")
        }
    }
    private func chooseSource() {
        NSApp.activate()
        guard let parent = NSApp.windows.first(where: { $0.identifier?.rawValue == "importsWindow" }) else { return }
        let window = parent.attachedSheet ?? parent
        let panel = FolderSelectionPanel.make(); panel.canChooseDirectories = true; panel.canChooseFiles = false
        panel.title = "Choose this device’s media folder"
        panel.message = "Choose this device’s recording folder.\nSelect DCIM or VIDEO for ordinary clips.\nFor structured recordings, choose the full camera folder\nor the whole card."
        panel.beginSheetModal(for: window) { response in
            guard response == .OK, let folder = panel.url else { return }
            do {
                try MediaImportEngine.checkPath(folder)
                let volume = try ImportVolumes.root(folder)
                guard try ImportVolumes.identity(folder) == profile.id else {
                    throw ImportFailure("Choose a media folder or the whole original device. Enroll formatted devices again.")
                }
                profile.sourceBookmark = try folder.bookmarkData(options: [.withSecurityScope], includingResourceValuesForKeys: nil, relativeTo: nil)
                profile.mediaPath = String(folder.path.dropFirst(volume.path.count + 1)); error = nil
            } catch { self.error = error.localizedDescription }
        }
    }
    private func chooseDestination() {
        NSApp.activate()
        guard let parent = NSApp.windows.first(where: { $0.identifier?.rawValue == "importsWindow" }) else { return }
        let window = parent.attachedSheet ?? parent
        let panel = ImportFolderPanels.destination(
            message: "Choose where to save copies.\nUse your Mac or another drive.\nCopies go into dated subfolders.",
            currentFolder: URL(fileURLWithPath: profile.destinationLabel))
        panel.beginSheetModal(for: window) { response in
            guard response == .OK, let url = panel.url else { return }
            do {
                try MediaImportEngine.checkPath(url)
                let id = try ImportVolumes.identity(url)
                guard id != profile.id else { throw ImportFailure("Choose a different disk.") }
                profile.destinationBookmark = try url.bookmarkData(options: [.withSecurityScope], includingResourceValuesForKeys: nil, relativeTo: nil)
                profile.destinationVolumeID = id; profile.destinationLabel = url.path; error = nil
            } catch { self.error = error.localizedDescription }
        }
    }
}

@MainActor private enum ImportFolderPanels {
    static func destination(message: String, currentFolder: URL? = nil) -> NSOpenPanel {
        let panel = FolderSelectionPanel.make()
        panel.title = "Choose where to save copies"
        // Sheet titles are not always visible, so instructions must be in the message.
        panel.message = message
        panel.prompt = "Save copies here"
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        // A fresh panel otherwise inherits the previous source chooser's DCIM folder.
        if let currentFolder, FileManager.default.fileExists(atPath: currentFolder.path) {
            panel.directoryURL = currentFolder
        } else {
            panel.directoryURL = FileManager.default.homeDirectoryForCurrentUser
        }
        return panel
    }
}
