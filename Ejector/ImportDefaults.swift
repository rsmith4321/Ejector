import Foundation

/// A snapshot for newly enrolled devices, never a fallback for saved profiles.
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

    func newProfile(id: String, name: String, mediaPath: String, destinationBookmark: Data,
                    destinationVolumeID: String, destinationLabel: String, sourceBookmark: Data) -> DroneProfile {
        DroneProfile(id: id, name: name, mediaPath: mediaPath, destinationBookmark: destinationBookmark,
                     destinationVolumeID: destinationVolumeID, destinationLabel: destinationLabel,
                     sourceBookmark: sourceBookmark, enabled: automaticImport, autoEject: askToEject,
                     videosOnly: videosOnly, cleanLayout: cleanLayout, includePreviews: includePreviews)
    }
}
