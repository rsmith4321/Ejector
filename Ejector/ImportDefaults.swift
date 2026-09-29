import Foundation

/// Shared preferences, resolved once per import for profiles that explicitly follow defaults.
nonisolated struct ImportDefaults: Equatable {
    static let videosOnlyKey = "importDefaults.videosOnly"
    static let includePreviewsKey = "importDefaults.includePreviews"
    static let cleanLayoutKey = "importDefaults.cleanLayout"
    static let automaticImportKey = "importDefaults.automaticImport"
    static let askToEjectKey = "importDefaults.askToEject"

    var videosOnly: Bool
    var includePreviews: Bool
    var cleanLayout: Bool
    var automaticImport: Bool
    var askToEject: Bool

    init(store: UserDefaults = .standard) {
        videosOnly = store.object(forKey: Self.videosOnlyKey) as? Bool ?? false
        includePreviews = store.object(forKey: Self.includePreviewsKey) as? Bool ?? true
        cleanLayout = store.object(forKey: Self.cleanLayoutKey) as? Bool ?? true
        automaticImport = store.object(forKey: Self.automaticImportKey) as? Bool ?? false
        askToEject = store.object(forKey: Self.askToEjectKey) as? Bool ?? false
    }

    init(videosOnly: Bool, includePreviews: Bool, cleanLayout: Bool, automaticImport: Bool, askToEject: Bool) {
        self.videosOnly = videosOnly
        self.includePreviews = includePreviews
        self.cleanLayout = cleanLayout
        self.automaticImport = automaticImport
        self.askToEject = askToEject
    }

    func newProfile(id: String, name: String, mediaPath: String, destinationBookmark: Data,
                    destinationVolumeID: String, destinationLabel: String, sourceBookmark: Data) -> DroneProfile {
        DroneProfile(id: id, name: name, mediaPath: mediaPath, destinationBookmark: destinationBookmark,
                     destinationVolumeID: destinationVolumeID, destinationLabel: destinationLabel,
                     sourceBookmark: sourceBookmark, enabled: automaticImport, autoEject: askToEject,
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
        // Destructive options require custom settings so later default changes cannot broaden deletion.
        result.deleteOriginals = false
        result.recoverTrash = false
        return result
    }

    mutating func setCustomImportSettings(_ custom: Bool, defaults: ImportDefaults) {
        if followsImportDefaults { self = resolved(using: defaults) }
        usesImportDefaults = !custom
        if !custom { self = resolved(using: defaults) }
    }
}
