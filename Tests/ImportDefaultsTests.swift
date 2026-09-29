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
        precondition(initial.cleanLayout == true && initial.videosOnly == false) // Saved seed values are unchanged.
        let changedDefaults = ImportDefaults(store: store)
        let inherited = initial.resolved(using: changedDefaults)
        precondition(inherited.videosOnly == true && inherited.cleanLayout == false && inherited.enabled && inherited.autoEject)
        precondition(initial.videosOnly == false && !initial.enabled) // Resolution does not mutate stored data.
        var override = customized
        override.setCustomImportSettings(true, defaults: changedDefaults)
        override.cleanLayout = true
        precondition(ImportDefaults(store: store).cleanLayout == false) // Per-device edits stay local.
        let customSnapshot = override
        store.set(false, forKey: ImportDefaults.videosOnlyKey)
        store.set(false, forKey: ImportDefaults.automaticImportKey)
        let nextDefaults = ImportDefaults(store: store)
        precondition(override.resolved(using: nextDefaults) == customSnapshot)
        precondition(inherited.videosOnly == true && inherited.enabled) // In-flight options stay frozen.
        override.deleteOriginals = true; override.recoverTrash = true
        override.setCustomImportSettings(false, defaults: nextDefaults)
        precondition(override.followsImportDefaults && override.videosOnly == false && !override.enabled)
        precondition(!override.deleteOriginals && !override.recoverTrash)
        var malformed = override
        malformed.deleteOriginals = true; malformed.recoverTrash = true
        precondition(!malformed.resolved(using: nextDefaults).deleteOriginals && !malformed.resolved(using: nextDefaults).recoverTrash)
        override.setCustomImportSettings(true, defaults: changedDefaults)
        precondition(!override.followsImportDefaults && override.videosOnly == true && override.enabled)
        let roundTrip = try JSONDecoder().decode(DroneProfile.self, from: JSONEncoder().encode(override))
        precondition(roundTrip == override && roundTrip.usesImportDefaults == false)
        let inheritedRoundTrip = try JSONDecoder().decode(DroneProfile.self, from: JSONEncoder().encode(initial))
        precondition(inheritedRoundTrip.followsImportDefaults)
        var legacy = initial
        legacy.usesImportDefaults = nil
        legacy.videosOnly = nil; legacy.cleanLayout = nil; legacy.includePreviews = nil
        legacy.deleteOriginals = true
        let oldData = try JSONEncoder().encode(legacy)
        let decoded = try JSONDecoder().decode(DroneProfile.self, from: oldData)
        precondition(decoded == legacy && decoded.cleanLayout == nil && decoded.deleteOriginals)
        precondition(!decoded.followsImportDefaults && decoded.resolved(using: nextDefaults) == legacy)
        precondition(!profile(ImportDefaults(store: store)).deleteOriginals)
        print("PASS: factory defaults, saved preferences, per-card overrides, live inheritance, frozen operations, custom isolation, legacy profiles and original retention")
    }
}
