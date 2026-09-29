import Foundation

/// Shared preferences, resolved once per import for profiles that explicitly follow defaults.
nonisolated struct ImportDefaults: Equatable {
    static let videosOnlyKey = "importDefaults.videosOnly"
    static let includePreviewsKey = "importDefaults.includePreviews"
    static let cleanLayoutKey = "importDefaults.cleanLayout"
    static let automaticImportKey = "importDefaults.automaticImport"
    static let askToEjectKey = "importDefaults.askToEject"
    static let deleteOriginalsKey = "importDefaults.deleteOriginals"
    static let recoverTrashKey = "importDefaults.recoverTrash"

    var videosOnly: Bool
    var includePreviews: Bool
    var cleanLayout: Bool
    var automaticImport: Bool
    var askToEject: Bool
    var deleteOriginals: Bool
    var recoverTrash: Bool

    init(store: UserDefaults = .standard) {
        videosOnly = store.object(forKey: Self.videosOnlyKey) as? Bool ?? true
        includePreviews = store.object(forKey: Self.includePreviewsKey) as? Bool ?? true
        cleanLayout = store.object(forKey: Self.cleanLayoutKey) as? Bool ?? true
        automaticImport = store.object(forKey: Self.automaticImportKey) as? Bool ?? false
        askToEject = store.object(forKey: Self.askToEjectKey) as? Bool ?? false
        deleteOriginals = store.object(forKey: Self.deleteOriginalsKey) as? Bool ?? false
        recoverTrash = store.object(forKey: Self.recoverTrashKey) as? Bool ?? false
    }

    init(videosOnly: Bool, includePreviews: Bool, cleanLayout: Bool, automaticImport: Bool, askToEject: Bool, deleteOriginals: Bool = false, recoverTrash: Bool = false) {
        self.videosOnly = videosOnly
        self.includePreviews = includePreviews
        self.cleanLayout = cleanLayout
        self.automaticImport = automaticImport
        self.askToEject = askToEject
        self.deleteOriginals = deleteOriginals
        self.recoverTrash = recoverTrash
    }

    /// Changing from videos to all media must not silently extend removal to photos.
    static func setVideosOnly(_ value: Bool, store: UserDefaults = .standard) {
        if !value && ImportDefaults(store: store).videosOnly {
            store.set(false, forKey: deleteOriginalsKey)
            store.set(false, forKey: recoverTrashKey)
        }
        store.set(value, forKey: videosOnlyKey)
    }

    func newProfile(id: String, name: String, mediaPath: String, destinationBookmark: Data,
                    destinationVolumeID: String, destinationLabel: String, sourceBookmark: Data) -> DroneProfile {
        DroneProfile(id: id, name: name, mediaPath: mediaPath, destinationBookmark: destinationBookmark,
                     destinationVolumeID: destinationVolumeID, destinationLabel: destinationLabel,
                     sourceBookmark: sourceBookmark, enabled: automaticImport,
                     deleteOriginals: deleteOriginals, recoverTrash: recoverTrash, autoEject: askToEject,
                     videosOnly: videosOnly, cleanLayout: cleanLayout, includePreviews: includePreviews, usesImportDefaults: true)
    }
}

extension DroneProfile {
    /// Return a stable set of options for this operation; never rewrite saved profiles on load.
    func resolved(using defaults: ImportDefaults) -> DroneProfile {
        guard followsImportDefaults else { return self }
        var result = self
        result.videosOnly = defaults.videosOnly
        result.includePreviews = defaults.includePreviews
        result.cleanLayout = defaults.cleanLayout
        result.enabled = defaults.automaticImport
        result.autoEject = defaults.askToEject
        result.deleteOriginals = defaults.deleteOriginals
        result.recoverTrash = defaults.recoverTrash
        return result
    }

    mutating func setCustomImportSettings(_ custom: Bool, defaults: ImportDefaults) {
        if followsImportDefaults { self = resolved(using: defaults) }
        usesImportDefaults = !custom
        if !custom { self = resolved(using: defaults) }
    }
}
