import Foundation

#if !APP_STORE
/// Destination copies survive source ejection. Eject permissions remain in the separate,
/// connection-bound ImportCompletionRegistry and are never restored from this history.
nonisolated struct QuickShareImport: Codable, Identifiable, Equatable, Sendable {
    let id: String
    let profileID: String
    let deviceName: String
    let date: Date
    let folder: URL
    let destinationBookmark: Data
    let destinationVolumeID: String
    let originals: [MediaImportEngine.VerifiedMedia]

    static func load(from url: URL) throws -> [Self] {
        try MediaImportEngine.checkPath(url)
        guard FileManager.default.fileExists(atPath: url.path) else { return [] }
        return Array(try JSONDecoder().decode([Self].self, from: Data(contentsOf: url)).prefix(20))
    }

    static func save(_ imports: [Self], to url: URL) throws {
        try MediaImportEngine.checkPath(url)
        try JSONEncoder().encode(Array(imports.prefix(20))).write(to: url, options: .atomic)
    }
}
#endif
