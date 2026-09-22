import SwiftUI

struct DroneImportView: View {
    @ObservedObject var importer: DroneImportManager
    @State private var editing: DroneProfile?
    @State private var selectedSource = ""
    @State private var setupError: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack {
                Label("Air unit & camera imports", systemImage: "arrow.down.doc")
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
                            Text(importer.busy ? "\(importer.activeName) · \(importer.progress.phase)" : importer.progress.phase).font(.headline)
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
                                Text(profile.deleteOriginals ? "Delete originals after verification" : "Keep originals on device").font(.caption)
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
                Text("Connect your DJI air unit, camera, or memory card. If several drives have the same name, connect only the device you want to set up.")
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
            let media = NSOpenPanel()
            media.title = "Choose the media folder on \(source.lastPathComponent)"
            media.message = "Select DCIM, VIDEO, or the folder where this device stores recordings. All regular files inside are imported; hidden items are skipped."
            media.canChooseDirectories = true; media.canChooseFiles = false
            media.directoryURL = source
            media.beginSheetModal(for: window) { response in
                guard response == .OK, let folder = media.url else { return }
                do {
                    try MediaImportEngine.checkPath(folder)
                    guard MediaImportEngine.isWithin(folder, source), folder != source else { throw ImportFailure("Select a media folder inside the chosen device.") }
                    DispatchQueue.main.async {
                        let destination = NSOpenPanel()
                        destination.title = "Choose where imports will be saved"
                        destination.canChooseDirectories = true; destination.canChooseFiles = false; destination.canCreateDirectories = true
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
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("Import profile").font(.title2.bold())
            TextField("Device name", text: $profile.name)
            Text("Give this profile a recognizable name, such as DJI O4. The card's name in Finder can change.")
                .font(.callout).foregroundStyle(.secondary)
            Text("Media folder: \(profile.mediaPath)").font(.callout)
            Text(profile.destinationLabel).font(.caption).textSelection(.enabled)
            Button("Change destination…", action: chooseDestination)
            #if APP_STORE
            Button("Authorize media folder again…", action: chooseSource)
            #endif
            Divider()
            Toggle("Import automatically when connected", isOn: $profile.enabled)
            Toggle("Permanently delete originals after import", isOn: Binding(
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
                    ? "Originals are removed directly after saved-copy verification. They skip Trash and cannot be restored from it. Use only for unimportant or replaceable footage."
                    : "Easy Eject saves verified copies and leaves the files on your device. Keep this setting for client work and important photos or videos.")
                    .font(.caption).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Toggle("Recover and clear this device’s Trash", isOn: Binding(get: { profile.recoverTrash }, set: {
                if $0 { showingTrashConfirmation = true } else { profile.recoverTrash = false }
            }))
            Text("Saves files from your Trash on this device into Recovered Device Trash, verifies the copies, then removes the recovered files from this device's Trash. Other drives' Trash is untouched.")
                .font(.caption).foregroundStyle(.secondary)
            #if APP_STORE
            Text("Requires authorization for this card, in addition to the media folder. If permission is unavailable, import stops without claiming completion.")
                .font(.caption).foregroundStyle(.secondary)
            #endif
            Toggle("Eject after verified import", isOn: $profile.autoEject)
            Text("Optional. Ejects this device only after an import finishes successfully. Leave off to eject from the menu yourself.")
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
        }.padding(24).frame(width: 480)
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
            Text("On each import, files in your Trash on this device are copied to the destination and verified before being permanently removed from the device's Trash. Keep independent backups. No recovery runs until you save this profile and start an import.")
        }
        .alert("Enable permanent deletion?", isPresented: $showingPermanentDeletionConfirmation) {
            Button("Cancel", role: .cancel) { }
                .keyboardShortcut(.defaultAction)
            Button("Enable permanent deletion", role: .destructive) {
                profile.deleteOriginals = true
            }
        } message: {
            Text("After saved copies pass verification, Easy Eject removes the originals directly from the device. This skips Trash and cannot be undone.\n\nUse this only for unimportant or replaceable FPV footage. Keep originals for client work and other important photos or videos, and keep independent backups.\n\nThe developer is not responsible for lost files.")
        }
    }
    #if APP_STORE
    private func chooseSource() {
        NSApp.activate()
        guard let parent = NSApp.windows.first(where: { $0.identifier?.rawValue == "importsWindow" }) else { return }
        let window = parent.attachedSheet ?? parent
        let panel = NSOpenPanel(); panel.canChooseDirectories = true; panel.canChooseFiles = false
        panel.title = "Authorize this device’s media folder"
        panel.beginSheetModal(for: window) { response in
            guard response == .OK, let folder = panel.url else { return }
            do {
                try MediaImportEngine.checkPath(folder)
                let volume = try ImportVolumes.root(folder)
                guard try ImportVolumes.identity(folder) == profile.id, folder != volume else {
                    throw ImportFailure("Choose a media folder inside the original device. Enroll formatted devices again.")
                }
                profile.sourceBookmark = try folder.bookmarkData(options: [.withSecurityScope], includingResourceValuesForKeys: nil, relativeTo: nil)
                profile.mediaPath = String(folder.path.dropFirst(volume.path.count + 1)); error = nil
            } catch { self.error = error.localizedDescription }
        }
    }
    #endif
    private func chooseDestination() {
        NSApp.activate()
        guard let parent = NSApp.windows.first(where: { $0.identifier?.rawValue == "importsWindow" }) else { return }
        let window = parent.attachedSheet ?? parent
        let panel = NSOpenPanel(); panel.canChooseDirectories = true; panel.canChooseFiles = false; panel.canCreateDirectories = true
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
