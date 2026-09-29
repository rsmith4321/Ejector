import SwiftUI

struct ImportDefaultsView: View {
    @AppStorage(ImportDefaults.videosOnlyKey) private var videosOnly = false
    @AppStorage(ImportDefaults.includePreviewsKey) private var includePreviews = true
    @AppStorage(ImportDefaults.cleanLayoutKey) private var cleanLayout = true
    @AppStorage(ImportDefaults.automaticImportKey) private var automaticImport = false
    @AppStorage(ImportDefaults.askToEjectKey) private var askToEject = false

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Import defaults").font(.headline)
            Text("Cards using defaults follow these settings on their next import. Choose Customize for this device in a card’s profile to give it different settings.")
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
            Text("Cards using defaults keep originals. To enable permanent deletion or Trash recovery, customize that device’s profile.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// Let every view show current defaults without copying them into saved profiles.
@propertyWrapper struct StoredImportDefaults: DynamicProperty {
    @AppStorage(ImportDefaults.videosOnlyKey) private var videosOnly = false
    @AppStorage(ImportDefaults.includePreviewsKey) private var includePreviews = true
    @AppStorage(ImportDefaults.cleanLayoutKey) private var cleanLayout = true
    @AppStorage(ImportDefaults.automaticImportKey) private var automaticImport = false
    @AppStorage(ImportDefaults.askToEjectKey) private var askToEject = false

    var wrappedValue: ImportDefaults {
        ImportDefaults(videosOnly: videosOnly, includePreviews: includePreviews,
                       cleanLayout: cleanLayout, automaticImport: automaticImport, askToEject: askToEject)
    }
}

struct ImportDefaultsSheet: View {
    @Environment(\.dismiss) private var dismiss
    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            ImportDefaultsView()
            HStack { Spacer(); Button("Done") { dismiss() }.keyboardShortcut(.defaultAction) }
        }.padding(24).frame(width: 470)
    }
}
