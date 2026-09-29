import Foundation

@main struct ImportDefaultsTests {
    static func main() throws {
        let name = "EasyEject.ImportDefaultsTests.\(UUID())"
        let store = UserDefaults(suiteName: name)!
        defer { store.removePersistentDomain(forName: name) }
        func profile(_ defaults: ImportDefaults) -> DroneProfile {
            defaults.newProfile(id: "fixture", name: "Test card", mediaPath: "DCIM",
                                destinationBookmark: Data(), destinationVolumeID: "destination",
                                destinationLabel: "/test", sourceBookmark: Data())
        }
        let initial = profile(ImportDefaults(store: store))
        precondition(initial.cleanLayout == true && initial.includePreviews == true && initial.videosOnly == false)
        precondition(!initial.enabled && !initial.autoEject && !initial.deleteOriginals && !initial.recoverTrash)
        store.set(true, forKey: ImportDefaults.videosOnlyKey)
        store.set(false, forKey: ImportDefaults.cleanLayoutKey)
        store.set(false, forKey: ImportDefaults.includePreviewsKey)
        store.set(true, forKey: ImportDefaults.automaticImportKey)
        store.set(true, forKey: ImportDefaults.askToEjectKey)
        let customized = profile(ImportDefaults(store: UserDefaults(suiteName: name)!))
        precondition(customized.videosOnly == true && customized.cleanLayout == false && customized.includePreviews == false)
        precondition(customized.enabled && customized.autoEject && !customized.deleteOriginals && !customized.recoverTrash)
        precondition(initial.cleanLayout == true && initial.videosOnly == false) // No live inheritance.
        var override = customized
        override.cleanLayout = true
        precondition(ImportDefaults(store: store).cleanLayout == false) // Per-device edits stay local.
        var legacy = initial
        legacy.videosOnly = nil; legacy.cleanLayout = nil; legacy.includePreviews = nil
        legacy.deleteOriginals = true
        let oldData = try JSONEncoder().encode(legacy)
        let decoded = try JSONDecoder().decode(DroneProfile.self, from: oldData)
        precondition(decoded == legacy && decoded.cleanLayout == nil && decoded.deleteOriginals)
        precondition(!profile(ImportDefaults(store: store)).deleteOriginals)
        print("PASS: factory defaults, saved preferences, per-card overrides, snapshot isolation, legacy profiles and original retention")
    }
}
