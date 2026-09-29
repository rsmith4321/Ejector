import SwiftUI

struct ImportDefaultsView: View {
    @AppStorage(ImportDefaults.videosOnlyKey) private var videosOnly = false
    @AppStorage(ImportDefaults.includePreviewsKey) private var includePreviews = true
    @AppStorage(ImportDefaults.cleanLayoutKey) private var cleanLayout = true
    @AppStorage(ImportDefaults.automaticImportKey) private var automaticImport = false
    @AppStorage(ImportDefaults.askToEjectKey) private var askToEject = false

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Defaults for new import devices").font(.headline)
            Text("New cards and cameras start with these settings. Review or change them during setup. Saved profiles keep their own settings.")
                .font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            Picker("Import", selection: $videosOnly) {
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
            Text("Originals are always kept when setting up a new device. Permanent deletion and Trash recovery must be enabled separately in each profile.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
    }
}
