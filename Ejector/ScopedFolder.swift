#if APP_STORE
import Foundation

/// Owns exactly one access claim, including across the worker queue and eject callback.
nonisolated final class ScopedFolder: @unchecked Sendable {
    let url: URL
    let refreshedBookmark: Data?
    private let lock = NSLock()
    private var active = true

    init(_ bookmark: Data) throws {
        var stale = false
        do {
            url = try URL(resolvingBookmarkData: bookmark,
                          options: [.withSecurityScope, .withoutUI, .withoutMounting],
                          bookmarkDataIsStale: &stale)
        } catch {
            throw ImportFailure("Folder permission is unavailable. Connect the drive or authorize the folder again. \(error.localizedDescription)")
        }
        guard url.startAccessingSecurityScopedResource() else {
            throw ImportFailure("Folder permission could not be opened. Authorize the folder again.")
        }
        do {
            refreshedBookmark = stale ? try url.bookmarkData(options: [.withSecurityScope], includingResourceValuesForKeys: nil, relativeTo: nil) : nil
        } catch {
            url.stopAccessingSecurityScopedResource()
            throw ImportFailure("Folder permission could not be refreshed. Authorize the folder again.")
        }
    }

    func close() {
        lock.lock(); defer { lock.unlock() }
        if active { active = false; url.stopAccessingSecurityScopedResource() }
    }
    deinit { close() }
}
#endif
