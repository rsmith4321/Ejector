#if APP_STORE
import AppKit
import Combine

/// Only an explicit Open panel selection creates a volume-wide sandbox grant.
@MainActor final class CardAccess: ObservableObject {
    struct Grant: Codable, Identifiable {
        let id: String
        var name: String
        var bookmark: Data
    }
    static let shared = CardAccess()
    @Published private(set) var grants: [Grant] = []
    @Published var error: String?
    private let key = "authorizedCardVolumes.v1"
    init() {
        if let data = UserDefaults.standard.data(forKey: key) {
            do { grants = try JSONDecoder().decode([Grant].self, from: data) }
            catch { self.error = "Saved card permissions could not be read. Authorize your card again." }
        }
    }
    private func persist(_ updated: [Grant]) throws {
        let data = try JSONEncoder().encode(updated)
        UserDefaults.standard.set(data, forKey: key)
        grants = updated
    }
    func open(_ volume: URL) throws -> ScopedFolder {
        let id = try ImportVolumes.identity(volume)
        guard let index = grants.firstIndex(where: { $0.id == id }) else {
            throw ImportFailure("Authorize \(volume.lastPathComponent) to inspect folders or clean metadata.")
        }
        let access = try ScopedFolder(grants[index].bookmark)
        do {
            try MediaImportEngine.checkPath(access.url)
            guard access.url.standardizedFileURL == volume.standardizedFileURL,
                  try ImportVolumes.root(access.url).standardizedFileURL == volume.standardizedFileURL,
                  try ImportVolumes.identity(access.url) == id else {
                throw ImportFailure("The authorized card changed. Authorize this volume again.")
            }
            if let bookmark = access.refreshedBookmark {
                var updated = grants; updated[index].bookmark = bookmark
                try persist(updated)
            }
            return access
        } catch { access.close(); throw error }
    }
    /// Selection grants access only. It never enables imports, cleanup, or deletion.
    @discardableResult func authorize(_ expected: URL? = nil) -> Bool {
        do {
            let expectedID = try expected.map { try ImportVolumes.identity($0) }
            let panel = NSOpenPanel()
            panel.canChooseFiles = false; panel.canChooseDirectories = true
            panel.allowsMultipleSelection = false
            panel.title = "Authorize a card or external volume"
            panel.message = "Select the card itself to recognize camera/emulator folders and use Clean & Eject. This remembers access to this volume; it does not start an import or delete files."
            panel.prompt = "Authorize"
            panel.directoryURL = expected ?? URL(fileURLWithPath: "/Volumes")
            NSApp.activate()
            guard panel.runModal() == .OK, let selected = panel.url else { return false }
            try MediaImportEngine.checkPath(selected)
            let root = try ImportVolumes.root(selected).standardizedFileURL
            let id = try ImportVolumes.identity(selected)
            guard selected.standardizedFileURL == root, root.path.hasPrefix("/Volumes/"),
                  expectedID == nil || expectedID == id,
                  expected == nil || expected?.standardizedFileURL == root else {
                throw ImportFailure("Select the original card itself under /Volumes, not a folder or another disk.")
            }
            let bookmark = try selected.bookmarkData(options: [.withSecurityScope], includingResourceValuesForKeys: nil, relativeTo: nil)
            var updated = grants.filter { $0.id != id }
            updated.append(Grant(id: id, name: selected.lastPathComponent, bookmark: bookmark))
            try persist(updated)
            error = nil
            NotificationCenter.default.post(name: .init("CardAccessChanged"), object: nil)
            return true
        } catch {
            self.error = error.localizedDescription
            let alert = NSAlert(); alert.messageText = "Card permission"
            alert.informativeText = error.localizedDescription; alert.addButton(withTitle: "OK")
            NSApp.activate(); alert.runModal(); return false
        }
    }
    func require(_ volume: URL) throws -> ScopedFolder {
        if let access = try? open(volume) { return access }
        guard authorize(volume) else { throw ImportFailure("Card access was not granted. Nothing was cleaned or ejected.") }
        return try open(volume)
    }
    func forget(_ id: String) {
        do {
            try persist(grants.filter { $0.id != id })
            NotificationCenter.default.post(name: .init("CardAccessChanged"), object: nil)
        } catch { self.error = error.localizedDescription }
    }
}
#endif
