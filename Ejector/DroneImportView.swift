import SwiftUI

struct DroneImportView: View {
    @ObservedObject var importer: DroneImportManager
    @State private var editing: DroneProfile?
    @State private var selectedSource = ""
    @State private var setupError: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack {
                Label("Device media imports", systemImage: "arrow.down.doc")
                    .font(.title2.bold())
                Spacer()
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
                }.frame(maxWidth: .infinity, alignment: .leading).padding(6)
            }
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 12) {
                    if importer.profiles.isEmpty {
                        ContentUnavailableView("No devices enrolled", systemImage: "sdcard", description: Text("Choose a connected device below. Automatic importing and original deletion start off."))
                            .frame(maxWidth: .infinity)
                            .multilineTextAlignment(.center)
                    }
                    ForEach(importer.profiles) { profile in
                        GroupBox {
                            VStack(alignment: .leading, spacing: 6) {
                                HStack {
                                    Text(profile.name).font(.headline)
                                    Spacer()
                                    Text(profile.enabled ? "Automatic" : "Manual").font(.caption).foregroundStyle(.secondary)
                                }
                                Text("\(profile.mediaPath) → \(profile.destinationLabel)/YYYY-MM-DD").font(.caption).lineLimit(2)
                                Text((profile.videosOnly == true ? "Videos only · " : "All media · ") + (profile.deleteOriginals ? "Delete imported originals after verification" : "Keep originals on device")).font(.caption)
                                HStack {
                                    Button("Import now") { importer.importNow(profile) }
                                    Button("Edit") { editing = profile }
                                    Spacer()
                                    Button("Remove profile") { importer.remove(profile.id) }
                                }.disabled(importer.busy)
                            }.frame(maxWidth: .infinity, alignment: .leading).padding(4)
                        }
                    }
                }.frame(maxWidth: .infinity)
            }
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
        .padding(22).frame(minWidth: 620, minHeight: 580)
        .onChange(of: importer.volumes) { _, volumes in
            if !volumes.contains(where: { $0.path == selectedSource }) { selectedSource = "" }
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
            media.message = "Step 1 of 2: Choose the recording folder on \(source.lastPathComponent).\nSelect DCIM or VIDEO for ordinary clips; choose the whole card for structured cinema formats. Next, choose where to save copies."
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
                            message: "Step 2 of 2: Choose where to save copies of \(folder.lastPathComponent).\nUse your Mac or another drive. Copies go into dated subfolders.")
                        destination.beginSheetModal(for: window) { response in
                            guard response == .OK, let target = destination.url else { return }
                            do {
                                try MediaImportEngine.checkPath(target)
                                let destinationID = try ImportVolumes.identity(target)
                                guard destinationID != id else { throw ImportFailure("Choose a destination on a different disk.") }
                                let bookmark = try target.bookmarkData(options: [.withSecurityScope], includingResourceValuesForKeys: nil, relativeTo: nil)
                                editing = DroneProfile(id: id, name: source.lastPathComponent,
                                    mediaPath: String(folder.path.dropFirst(source.path.count + 1)), destinationBookmark: bookmark,
                                    destinationVolumeID: destinationID, destinationLabel: target.path,
                                    sourceBookmark: try folder.bookmarkData(options: [.withSecurityScope], includingResourceValuesForKeys: nil, relativeTo: nil), cleanLayout: true)
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
    var body: some View {
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
            Toggle("Eject after verified import", isOn: $profile.autoEject)
            Text("Optional. Leave off to import photos in Lightroom next, or keep on and unplug/reconnect the device before the Lightroom import. Ejects only after a successful import.")
                .font(.caption).foregroundStyle(.secondary)
            Text("Ejecting also unmounts the other partitions. If multiple enrolled partitions share a disk, automatic eject is paused. Import each partition, then eject from the menu.")
                .font(.caption).foregroundStyle(.secondary)
            if let error { Text(error).foregroundStyle(.orange) }
            HStack {
                Button("Cancel") { dismiss() }
                Spacer()
                Button("Save profile") { save(profile) }.buttonStyle(.borderedProminent)
                    .disabled(profile.name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
            Text("Saving applies to the next connection. Use Import now to start with the connected device.")
                .font(.caption).foregroundStyle(.secondary)
        }.padding(24)
        }.frame(width: 520, height: 700)
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
        panel.message = "Choose DCIM or VIDEO for ordinary clips. For structured recordings, choose the complete camera folder or whole card."
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
            message: "Choose where to save copies from \(profile.name).\nUse your Mac or another drive. Copies go into dated subfolders.",
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
