import Foundation
import Darwin

nonisolated enum MetadataCleaner {
    static func clean(_ root: URL, validate: () throws -> Void) throws -> Int {
        let skipped: Set<String> = [".Spotlight-V100", ".Trashes", ".fseventsd", ".TemporaryItems"]
        var count = 0
        try validate(); try MediaImportEngine.checkPath(root)
        var rootStat = stat()
        guard lstat(root.path, &rootStat) == 0 else { throw ImportFailure("Cannot access the drive for cleanup.") }
        func walk(_ folder: URL) throws {
            try validate(); try MediaImportEngine.checkPath(folder)
            var s = stat()
            guard lstat(folder.path, &s) == 0, s.st_dev == rootStat.st_dev,
                  (s.st_mode & S_IFMT) == S_IFDIR else { throw ImportFailure("Drive changed during cleanup. Ejection stopped.") }
            guard let dir = opendir(folder.path) else { throw ImportFailure("Cannot scan \(folder.path). Authorize this card or check its file permissions.") }
            var names: [String] = []
            while true {
                errno = 0
                guard let entry = readdir(dir) else {
                    let failure = errno; closedir(dir)
                    if failure != 0 { throw ImportFailure("Cannot finish scanning \(folder.path). Ejection stopped.") }
                    break
                }
                let name = withUnsafePointer(to: entry.pointee.d_name) { String(cString: UnsafeRawPointer($0).assumingMemoryBound(to: CChar.self)) }
                if name != ".", name != ".." { names.append(name) }
            }
            for name in names where !skipped.contains(name) {
                let item = folder.appendingPathComponent(name)
                var entry = stat()
                guard lstat(item.path, &entry) == 0 else { throw ImportFailure("Cannot inspect \(item.path). Ejection stopped.") }
                if (entry.st_mode & S_IFMT) == S_IFLNK { continue }
                if (entry.st_mode & S_IFMT) == S_IFDIR {
                    try walk(item)
                    // Never recursively remove arbitrary contents of a __MACOSX directory.
                    if name == "__MACOSX" { _ = rmdir(item.path) }
                } else if (entry.st_mode & S_IFMT) == S_IFREG,
                          name.hasPrefix("._") || name == ".DS_Store" || name == ".apdisk" {
                    try validate(); try MediaImportEngine.checkPath(item)
                    guard unlink(item.path) == 0 else { throw ImportFailure("Cannot clean \(name). Ejection stopped; check permissions.") }
                    count += 1
                }
            }
        }
        try walk(root)
        return count
    }
}
