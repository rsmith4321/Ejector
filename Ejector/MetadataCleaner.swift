import Foundation
import Darwin

nonisolated enum MetadataCleaner {
    static func clean(_ root: URL, validate: () throws -> Void) throws -> Int {
        let skipped: Set<String> = [".Spotlight-V100", ".Trashes", ".fseventsd", ".TemporaryItems"]
        var count = 0
        try validate(); try MediaImportEngine.checkPath(root)
        // Every step below is relative to a descriptor opened from the checked root,
        // so swapping a checked folder for a link cannot redirect cleanup off the card.
        let rootFD: Int32
        do { rootFD = try MediaImportEngine.openPinnedDirectory(root) } catch { throw ImportFailure("Cannot access the drive for cleanup.") }
        defer { close(rootFD) }
        var rootStat = stat()
        guard fstat(rootFD, &rootStat) == 0 else { throw ImportFailure("Cannot access the drive for cleanup.") }
        func walk(_ folder: Int32, _ path: String) throws {
            try validate()
            var s = stat()
            guard fstat(folder, &s) == 0, s.st_dev == rootStat.st_dev,
                  (s.st_mode & S_IFMT) == S_IFDIR else { throw ImportFailure("Drive changed during cleanup. Ejection stopped.") }
            let listing = dup(folder)
            guard listing >= 0, let dir = fdopendir(listing) else {
                if listing >= 0 { close(listing) }
                throw ImportFailure("Cannot scan \(path). Authorize this card or check its file permissions.")
            }
            var names: [String] = []
            while true {
                errno = 0
                guard let entry = readdir(dir) else {
                    let failure = errno; closedir(dir)
                    if failure != 0 { throw ImportFailure("Cannot finish scanning \(path). Ejection stopped.") }
                    break
                }
                let name = withUnsafePointer(to: entry.pointee.d_name) { String(cString: UnsafeRawPointer($0).assumingMemoryBound(to: CChar.self)) }
                if name != ".", name != ".." { names.append(name) }
            }
            for name in names where !skipped.contains(name) {
                let item = path + "/" + name
                var entry = stat()
                guard fstatat(folder, name, &entry, AT_SYMLINK_NOFOLLOW) == 0 else { throw ImportFailure("Cannot inspect \(item). Ejection stopped.") }
                if (entry.st_mode & S_IFMT) == S_IFLNK { continue }
                if (entry.st_mode & S_IFMT) == S_IFDIR {
                    let child = openat(folder, name, O_RDONLY | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC)
                    guard child >= 0 else { throw ImportFailure("Cannot scan \(item). Authorize this card or check its file permissions.") }
                    defer { close(child) }
                    var opened = stat()
                    guard fstat(child, &opened) == 0, opened.st_dev == entry.st_dev,
                          opened.st_ino == entry.st_ino else { throw ImportFailure("Drive changed during cleanup. Ejection stopped.") }
                    try walk(child, item)
                    // Never recursively remove arbitrary contents of a __MACOSX directory.
                    if name == "__MACOSX" { _ = unlinkat(folder, name, AT_REMOVEDIR) }
                } else if (entry.st_mode & S_IFMT) == S_IFREG,
                          name.hasPrefix("._") || name == ".DS_Store" || name == ".apdisk" {
                    try validate()
                    #if EASY_EJECT_TEST_SEAMS
                    DeletionRaceHooks.beforeMetadataUnlink?()
                    #endif
                    var current = stat()
                    guard fstatat(folder, name, &current, AT_SYMLINK_NOFOLLOW) == 0, (current.st_mode & S_IFMT) == S_IFREG,
                          current.st_dev == entry.st_dev, current.st_ino == entry.st_ino else {
                        throw ImportFailure("\(name) changed during cleanup. Ejection stopped.")
                    }
                    guard unlinkat(folder, name, 0) == 0 else { throw ImportFailure("Cannot clean \(name). Ejection stopped; check permissions.") }
                    count += 1
                }
            }
        }
        try walk(rootFD, root.standardizedFileURL.path)
        return count
    }
}
