import Foundation

nonisolated struct DroneProfile: Codable, Identifiable, Equatable, Sendable {
    var id: String
    var name: String
    var mediaPath: String
    var destinationBookmark: Data
    var destinationVolumeID: String
    var destinationLabel: String
    var sourceBookmark: Data? = nil
    var enabled = false
    var deleteOriginals = false
    var recoverTrash = false
    // Legacy persisted key; true now asks for confirmation and never ejects automatically.
    var autoEject = false
    var videosOnly: Bool? = nil
    var cleanLayout: Bool? = nil
    var includePreviews: Bool? = nil
    // Missing in older profiles: preserve their saved choices as custom settings.
    var usesImportDefaults: Bool? = nil
    // Per-device Quick Share availability and preferred size. Never starts an export automatically.
    // Legacy sharing settings remain compatible; older profiles and new cards start off.
    var sharingPreset: EasySharePreset? = nil
    var sharingColor: EasyShareColor? = nil
    var sharingCamera: EasyShareCamera? = nil

    var followsImportDefaults: Bool { usesImportDefaults == true }
}
