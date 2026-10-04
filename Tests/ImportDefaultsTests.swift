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
        precondition(initial.cleanLayout == true && initial.includePreviews == true && initial.videosOnly == true)
        precondition(!initial.enabled && !initial.autoEject && !initial.deleteOriginals && !initial.recoverTrash)
        store.set(true, forKey: ImportDefaults.videosOnlyKey)
        store.set(false, forKey: ImportDefaults.cleanLayoutKey)
        store.set(false, forKey: ImportDefaults.includePreviewsKey)
        store.set(true, forKey: ImportDefaults.automaticImportKey)
        store.set(true, forKey: ImportDefaults.askToEjectKey)
        let customized = profile(ImportDefaults(store: UserDefaults(suiteName: name)!))
        precondition(customized.videosOnly == true && customized.cleanLayout == false && customized.includePreviews == false)
        precondition(customized.enabled && customized.autoEject && !customized.deleteOriginals && !customized.recoverTrash)
        precondition(initial.cleanLayout == true && initial.videosOnly == true) // Saved seed values are unchanged.
        let changedDefaults = ImportDefaults(store: store)
        let inherited = initial.resolved(using: changedDefaults)
        precondition(inherited.videosOnly == true && inherited.cleanLayout == false && inherited.enabled && inherited.autoEject)
        precondition(initial.videosOnly == true && !initial.enabled) // Resolution does not mutate stored data.
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
        precondition(inheritedRoundTrip.sharingPreset == nil && inheritedRoundTrip.sharingColor == nil)
        var sharing = initial
        sharing.sharingPreset = .uhd; sharing.sharingColor = .standard
        let sharingResolved = sharing.resolved(using: changedDefaults)
        precondition(sharingResolved.sharingPreset == .uhd && sharingResolved.sharingColor == .standard)
        let sharingRoundTrip = try JSONDecoder().decode(DroneProfile.self, from: JSONEncoder().encode(sharing))
        precondition(sharingRoundTrip == sharing)
        var legacy = initial
        legacy.usesImportDefaults = nil
        legacy.videosOnly = nil; legacy.cleanLayout = nil; legacy.includePreviews = nil
        legacy.deleteOriginals = true
        let oldData = try JSONEncoder().encode(legacy)
        let decoded = try JSONDecoder().decode(DroneProfile.self, from: oldData)
        precondition(decoded == legacy && decoded.cleanLayout == nil && decoded.deleteOriginals)
        precondition(!decoded.followsImportDefaults && decoded.resolved(using: nextDefaults) == legacy)
        precondition(!profile(ImportDefaults(store: store)).deleteOriginals)
        store.set(true, forKey: ImportDefaults.deleteOriginalsKey)
        store.set(true, forKey: ImportDefaults.recoverTrashKey)
        let removalDefaults = ImportDefaults(store: UserDefaults(suiteName: name)!)
        precondition(removalDefaults.deleteOriginals && removalDefaults.recoverTrash)
        let newCard = profile(removalDefaults)
        precondition(newCard.deleteOriginals && newCard.recoverTrash)
        let existingDefaultCard = initial.resolved(using: removalDefaults)
        precondition(existingDefaultCard.deleteOriginals && existingDefaultCard.recoverTrash)
        precondition(customSnapshot.resolved(using: removalDefaults) == customSnapshot)
        precondition(decoded.resolved(using: removalDefaults) == decoded)
        var switcher = customSnapshot
        switcher.setCustomImportSettings(false, defaults: removalDefaults)
        precondition(switcher.deleteOriginals && switcher.recoverTrash)
        switcher.setCustomImportSettings(true, defaults: removalDefaults)
        precondition(switcher.deleteOriginals && switcher.recoverTrash && !switcher.followsImportDefaults)
        ImportDefaults.setVideosOnly(true, store: store)
        precondition(ImportDefaults(store: store).deleteOriginals && ImportDefaults(store: store).recoverTrash)
        ImportDefaults.setVideosOnly(false, store: store)
        let broadened = ImportDefaults(store: store)
        precondition(!broadened.videosOnly && !broadened.deleteOriginals && !broadened.recoverTrash)
        precondition(existingDefaultCard.deleteOriginals && existingDefaultCard.recoverTrash) // Running import frozen.
        store.removeObject(forKey: ImportDefaults.videosOnlyKey)
        store.set(true, forKey: ImportDefaults.deleteOriginalsKey)
        store.set(true, forKey: ImportDefaults.recoverTrashKey)
        precondition(ImportDefaults(store: store).videosOnly) // Factory Videos only also needs reset on broadening.
        ImportDefaults.setVideosOnly(false, store: store)
        precondition(!ImportDefaults(store: store).deleteOriginals && !ImportDefaults(store: store).recoverTrash)
        print("PASS: factory defaults, saved preferences, per-card overrides, live inheritance, frozen operations, custom isolation, legacy profiles, Videos only factory default, removal-default inheritance and photo-broadening reset")
    }
}
