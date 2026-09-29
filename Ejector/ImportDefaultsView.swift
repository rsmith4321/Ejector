import SwiftUI

struct ImportDefaultsView: View {
    @AppStorage(ImportDefaults.videosOnlyKey) private var videosOnly = true
    @AppStorage(ImportDefaults.includePreviewsKey) private var includePreviews = true
    @AppStorage(ImportDefaults.cleanLayoutKey) private var cleanLayout = true
    @AppStorage(ImportDefaults.automaticImportKey) private var automaticImport = false
    @AppStorage(ImportDefaults.askToEjectKey) private var askToEject = false
    @AppStorage(ImportDefaults.deleteOriginalsKey) private var deleteOriginals = false
    @AppStorage(ImportDefaults.recoverTrashKey) private var recoverTrash = false

    @State private var confirmingDeletion = false
    @State private var confirmingTrashRecovery = false

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Import defaults").font(.headline)
            Text("Cards using defaults follow these settings on their next import. Choose Use custom settings in a card’s profile to give it different settings.")
                .font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            Picker("Import", selection: Binding(get: { videosOnly }, set: { ImportDefaults.setVideosOnly($0) })) {
                Text("All media and sidecars").tag(false)
                Text("Videos only").tag(true)
            }
            Toggle("Include camera previews", isOn: $includePreviews)
            Text("Include optional LRV, LRF, THM and camera proxy files.")
                .font(.caption).foregroundStyle(.secondary)
            Toggle("Clean folder layout", isOn: $cleanLayout)
            Text("Save ordinary clips and companions directly in dated folders. Required camera-package structure is preserved.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            Toggle("Import automatically when connected", isOn: $automaticImport)
            Toggle("Ask to eject after import", isOn: $askToEject)
            Divider()
            Text("Originals on devices using defaults").font(.headline)
            Toggle(videosOnly ? "Permanently delete imported videos" : "Permanently delete imported originals", isOn: Binding(
                get: { deleteOriginals },
                set: { enabled in
                    if enabled { confirmingDeletion = true } else { deleteOriginals = false }
                }
            ))
            Text(deleteOriginals
                ? "Removes recognized imported originals after the selected batch passes saved-copy verification. Skips Trash."
                : "Keep originals is on (recommended). Verified copies are saved and originals stay on the device.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            Toggle("Recover and clear device Trash", isOn: Binding(
                get: { recoverTrash },
                set: { enabled in
                    if enabled { confirmingTrashRecovery = true } else { recoverTrash = false }
                }
            ))
            Text("Copies and verifies files matching your import selection before permanently removing them from each device’s Trash. Videos only leaves photos and unrecognized files in Trash.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            #if APP_STORE
            Text("Trash recovery also requires whole-card authorization for each device. Without that permission, importing stops and keeps originals.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            #endif
            Text("These choices apply to every card using defaults, including new cards. Custom profiles keep their own choices. Switching from Videos only to All media turns both removal options off for review.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
        .alert("Enable permanent deletion in defaults?", isPresented: $confirmingDeletion) {
            Button("Cancel", role: .cancel) { }.keyboardShortcut(.defaultAction)
            Button("Enable permanent deletion", role: .destructive) { deleteOriginals = true }
        } message: {
            Text("This applies on the next import to every card using defaults, including cards added later. Custom profiles are unchanged. After the selected batch passes saved-copy verification, recognized imported originals are permanently removed from the device. They skip Trash and cannot be restored from it. Videos only leaves photos and unrecognized files untouched; skipped previews and device indexes are retained.\n\nUse only for unimportant or replaceable media. Keep originals for client work and important photos or videos, and keep independent backups. Deleting outside Sony and some other cameras can leave their media database inconsistent. Use the camera to erase or format after backup if unsure.\n\nThe developer is not responsible for lost files.")
        }
        .alert("Enable Trash recovery in defaults?", isPresented: $confirmingTrashRecovery) {
            Button("Cancel", role: .cancel) { }.keyboardShortcut(.defaultAction)
            Button("Enable verified Trash recovery", role: .destructive) { recoverTrash = true }
        } message: {
            Text("This applies on the next import to every card using defaults, including cards added later. Files in each device’s Trash matching your import selection are copied and verified before being permanently removed from Trash. Videos only leaves photos and unrecognized files untouched. Keep independent backups. Custom profiles are unchanged. Enabling this setting does not start an import.")
        }
    }
}

/// Let every view show current defaults without copying them into saved profiles.
@propertyWrapper struct StoredImportDefaults: DynamicProperty {
    @AppStorage(ImportDefaults.videosOnlyKey) private var videosOnly = true
    @AppStorage(ImportDefaults.includePreviewsKey) private var includePreviews = true
    @AppStorage(ImportDefaults.cleanLayoutKey) private var cleanLayout = true
    @AppStorage(ImportDefaults.automaticImportKey) private var automaticImport = false
    @AppStorage(ImportDefaults.askToEjectKey) private var askToEject = false
    @AppStorage(ImportDefaults.deleteOriginalsKey) private var deleteOriginals = false
    @AppStorage(ImportDefaults.recoverTrashKey) private var recoverTrash = false

    var wrappedValue: ImportDefaults {
        ImportDefaults(videosOnly: videosOnly, includePreviews: includePreviews,
                       cleanLayout: cleanLayout, automaticImport: automaticImport, askToEject: askToEject,
                       deleteOriginals: deleteOriginals, recoverTrash: recoverTrash)
    }
}

struct ImportDefaultsSheet: View {
    @Environment(\.dismiss) private var dismiss
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                ImportDefaultsView()
                HStack { Spacer(); Button("Done") { dismiss() }.keyboardShortcut(.defaultAction) }
            }.padding(24)
        }.frame(width: 520, height: 680)
    }
}
