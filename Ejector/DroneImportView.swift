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
                        Text(importer.busy ? "\(importer.activeName) · \(importer.progress.phase)" : importer.progress.phase).font(.headline)
                        Spacer()
                        if importer.busy { Button("Stop import") { importer.cancel() }.disabled(importer.progress.phase == "Ejecting") }
                    }
                    if importer.busy {
                        if importer.progress.total > 0 {
                            ProgressView(value: importer.progress.fraction)
                            Text("\(importer.progress.completed) of \(importer.progress.total) files completed · \(importer.progress.file)")
                                .font(.caption).lineLimit(2)
                        } else { ProgressView().controlSize(.small) }
                    } else {
                        Text(importer.message).foregroundStyle(importer.hasError ? .orange : .secondary)
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
                Text("Connect your DJI air unit, camera, or memory card to this Mac. It must appear as a drive in Finder.")
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
            Text("Profiles recognize the volume, not its name. After formatting a device, enroll it again. Destination drives must be connected before importing.")
                .font(.caption).foregroundStyle(.secondary)
        }
        .padding(22).frame(minWidth: 620, minHeight: 580)
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
                                    destinationVolumeID: destinationID, destinationLabel: target.path)
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
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("Import profile").font(.title2.bold())
            TextField("Device name", text: $profile.name)
            Text("Media folder: \(profile.mediaPath)").font(.callout)
            Text(profile.destinationLabel).font(.caption).textSelection(.enabled)
            Button("Change destination…", action: chooseDestination)
            Divider()
            Toggle("Import automatically when connected", isOn: $profile.enabled)
            Toggle("Delete originals after verifying saved copies", isOn: $profile.deleteOriginals)
            Text("All regular files in the selected media folder are imported. Hidden items are skipped. Deletion is permanent and happens only after each saved copy passes a SHA-256 check.")
                .font(.caption).foregroundStyle(.secondary)
            Toggle("Recover and clear this device’s Trash", isOn: $profile.recoverTrash)
            Text("Saves files from your Trash on this device into Recovered Device Trash before removing them. Requires Full Disk Access. Other drives’ Trash is untouched.")
                .font(.caption).foregroundStyle(.secondary)
            Toggle("Eject after successful import", isOn: $profile.autoEject)
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
    }
    private func chooseDestination() {
        NSApp.activate()
        guard let window = NSApp.keyWindow else { return }
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
