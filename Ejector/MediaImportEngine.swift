import Foundation
import CryptoKit
import Darwin

nonisolated struct ImportFailure: LocalizedError {
    let message: String
    var errorDescription: String? { message }
    init(_ message: String) { self.message = message }
}

nonisolated struct ImportProgress: Sendable {
    var phase: String
    var file: String = ""
    var completed: Int = 0
    var total: Int = 0
    var fraction: Double = 0
}

nonisolated final class ImportCancellation: @unchecked Sendable {
    private let lock = NSLock()
    private var cancelled = false
    func cancel() { lock.lock(); cancelled = true; lock.unlock() }
    func check() throws {
        lock.lock(); let value = cancelled; lock.unlock()
        if value { throw ImportFailure("Import stopped. Unverified originals remain on the device.") }
    }
}

nonisolated struct MediaImportEngine {
    let source: URL
    let mediaFolder: URL
    let destination: URL
    let deleteOriginals: Bool
    let recoverTrash: Bool
    let cancellation: ImportCancellation
    let validate: () throws -> Void
    let report: (ImportProgress) -> Void
    let audit: (String) throws -> Void

    struct Result { let files: Int; let bytes: Int64; let folder: URL }
    struct Stamp: Equatable {
        let device: dev_t
        let inode: ino_t
        let size: off_t
        let seconds: Int
        let nanos: Int
    }

    static func stamp(_ url: URL) throws -> Stamp {
        var s = stat()
        guard lstat(url.path, &s) == 0, (s.st_mode & S_IFMT) == S_IFREG else {
            throw ImportFailure("Cannot read a regular file: \(url.lastPathComponent)")
        }
        return Stamp(device: s.st_dev, inode: s.st_ino, size: s.st_size,
                     seconds: s.st_mtimespec.tv_sec, nanos: s.st_mtimespec.tv_nsec)
    }

    static func checkPath(_ url: URL) throws {
        var p = url.standardizedFileURL
        while p.path != "/" {
            var s = stat()
            if lstat(p.path, &s) == 0 {
                guard (s.st_mode & S_IFMT) != S_IFLNK else {
                    throw ImportFailure("A symbolic link is not allowed in an import path: \(p.path)")
                }
            } else if errno != ENOENT {
                throw ImportFailure("Cannot access \(p.path): \(String(cString: strerror(errno)))")
            }
            p.deleteLastPathComponent()
        }
    }

    static func isWithin(_ child: URL, _ root: URL) -> Bool {
        let p = child.standardizedFileURL.path, r = root.standardizedFileURL.path
        return p == r || p.hasPrefix(r == "/" ? "/" : r + "/")
    }

    static func files(_ root: URL, includeHidden: Bool) throws -> [URL] {
        try checkPath(root)
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: root.path, isDirectory: &isDirectory) else {
            throw ImportFailure("Folder unavailable: \(root.path)")
        }
        guard isDirectory.boolValue else { throw ImportFailure("Not a folder: \(root.path)") }
        var result: [URL] = []
        // Copy every regular file in the selected media folder, including RAW and sidecars.
        // Explicit recursion surfaces permission errors instead of silently returning an empty scan.
        for item in try FileManager.default.contentsOfDirectory(at: root, includingPropertiesForKeys: [.isDirectoryKey, .isSymbolicLinkKey]) {
            let v = try item.resourceValues(forKeys: [.isDirectoryKey, .isSymbolicLinkKey])
            guard v.isSymbolicLink != true else { throw ImportFailure("Symbolic link found on device: \(item.lastPathComponent)") }
            if !includeHidden && item.lastPathComponent.hasPrefix(".") { continue }
            if v.isDirectory == true { result += try files(item, includeHidden: includeHidden) }
            else { _ = try stamp(item); result.append(item) }
        }
        return result.sorted { $0.path < $1.path }
    }

    static func hash(_ url: URL, cancellation: ImportCancellation, progress: (Double) -> Void = { _ in }) throws -> String {
        let total = try stamp(url).size
        let fd = open(url.path, O_RDONLY | O_NOFOLLOW)
        guard fd >= 0 else { throw ImportFailure("Cannot open \(url.lastPathComponent): \(String(cString: strerror(errno)))") }
        let handle = FileHandle(fileDescriptor: fd, closeOnDealloc: true)
        defer { try? handle.close() }
        var hash = SHA256(), done: Int64 = 0
        while true {
            try cancellation.check()
            guard let data = try handle.read(upToCount: 4 * 1024 * 1024), !data.isEmpty else { break }
            hash.update(data: data); done += Int64(data.count)
            progress(total == 0 ? 1 : Double(done) / Double(total))
        }
        return hash.finalize().map { String(format: "%02x", $0) }.joined()
    }

    static func flushSavedCopy(_ url: URL) throws {
        // Reused copies need the same durability gate as a newly written file.
        let fd = open(url.path, O_RDONLY | O_NOFOLLOW)
        guard fd >= 0 else { throw ImportFailure("Cannot open the saved copy for a storage flush. Original kept.") }
        defer { close(fd) }
        guard fcntl(fd, F_FULLFSYNC) == 0 else { throw ImportFailure("Could not flush the saved copy to storage. Original kept.") }
        let parent = open(url.deletingLastPathComponent().path, O_RDONLY)
        guard parent >= 0 else { throw ImportFailure("Cannot open the destination directory. Original kept.") }
        defer { close(parent) }
        guard fsync(parent) == 0 else { throw ImportFailure("Could not flush the destination directory. Original kept.") }
    }

    func run() throws -> Result {
        try validate(); try cancellation.check()
        try Self.checkPath(source); try Self.checkPath(mediaFolder); try Self.checkPath(destination)
        guard Self.isWithin(mediaFolder, source), !Self.isWithin(destination, source),
              !Self.isWithin(source, destination) else { throw ImportFailure("Source and destination must be separate folders on different volumes.") }
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: destination.path, isDirectory: &isDirectory), isDirectory.boolValue else {
            throw ImportFailure("The chosen import destination is unavailable.")
        }
        let formatter = DateFormatter(); formatter.dateFormat = "yyyy-MM-dd"; formatter.locale = Locale(identifier: "en_US_POSIX")
        let dated = destination.appendingPathComponent(formatter.string(from: Date()), isDirectory: true)
        let trash = source.appendingPathComponent(".Trashes/\(getuid())", isDirectory: true)
        report(ImportProgress(phase: "Scanning"))
        let media = try Self.files(mediaFolder, includeHidden: false)
        var trashFiles: [URL] = []
        if recoverTrash {
            // Probe the parent with a throwing directory read. fileExists alone can hide permission denial.
            let parent = source.appendingPathComponent(".Trashes")
            if try FileManager.default.contentsOfDirectory(atPath: source.path).contains(".Trashes") {
                let names = try FileManager.default.contentsOfDirectory(atPath: parent.path)
                if names.contains(String(getuid())) { trashFiles = try Self.files(trash, includeHidden: true) }
            }
        }
        let items = media + trashFiles
        let totalBytes = try items.reduce(Int64(0)) { try $0 + Self.stamp($1).size }
        var completedBytes: Int64 = 0
        var retainedStamps: [URL: Stamp] = [:]
        for (index, file) in items.enumerated() {
            try cancellation.check(); try validate()
            let fromTrash = index >= media.count
            let root = fromTrash ? trash : mediaFolder
            let relative = String(file.path.dropFirst(root.path.count + 1))
            let targetRoot = fromTrash ? dated.appendingPathComponent("Recovered Device Trash") : dated
            let target = targetRoot.appendingPathComponent(relative)
            let before = try Self.stamp(file)
            func progress(_ phase: String, _ fraction: Double) {
                let stage: (Double, Double)
                switch phase {
                case "Checking": stage = (0, 0.20)
                case "Importing": stage = (0.20, 0.45)
                case "Verifying copy": stage = (0.65, 0.15)
                default: stage = (0.80, 0.20)
                }
                let overall = stage.0 + min(1, max(0, fraction)) * stage.1
                report(ImportProgress(phase: phase, file: file.lastPathComponent, completed: index,
                                      total: items.count, fraction: totalBytes == 0 ? 0 : (Double(completedBytes) + Double(before.size) * overall) / Double(totalBytes)))
            }
            progress("Checking", 0)
            let sha = try Self.hash(file, cancellation: cancellation) { progress("Checking", $0) }
            guard try Self.stamp(file) == before else { throw ImportFailure("Source changed while reading. Original kept: \(file.lastPathComponent)") }
            let saved = try save(file, to: target, sha: sha, before: before, progress: progress)
            try validate(); try Self.checkPath(file); try Self.checkPath(saved)
            let savedBefore = try Self.stamp(saved)
            let savedHash = try Self.hash(saved, cancellation: cancellation) { progress("Verifying", $0) }
            guard savedHash == sha, try Self.stamp(saved) == savedBefore,
                  try Self.stamp(file) == before else { throw ImportFailure("Verification failed. Original kept: \(file.lastPathComponent)") }
            try Self.flushSavedCopy(saved)
            try audit("Verified SHA256 \(sha) | \(file.path) -> \(saved.path)")
            try cancellation.check(); try validate()
            if deleteOriginals || fromTrash {
                guard try Self.stamp(file) == before, try Self.stamp(saved) == savedBefore else { throw ImportFailure("A file changed before deletion. Original kept.") }
                try FileManager.default.removeItem(at: file)
                try audit("Removed verified source | \(file.path)")
            }
            if !deleteOriginals && !fromTrash { retainedStamps[file] = before }
            completedBytes += before.size
            report(ImportProgress(phase: "Imported", file: file.lastPathComponent, completed: index + 1,
                                  total: items.count, fraction: totalBytes == 0 ? 1 : Double(completedBytes) / Double(totalBytes)))
        }
        // Newly arrived recordings prevent eject. Copy-only imports retain the original manifest.
        let remaining = try Self.files(mediaFolder, includeHidden: false)
        if deleteOriginals {
            guard remaining.isEmpty else { throw ImportFailure("New recordings arrived. Import again before ejecting.") }
        } else {
            guard Set(remaining.map(\.path)) == Set(media.map(\.path)) else { throw ImportFailure("Device contents changed. Import again before ejecting.") }
            for (file, stamp) in retainedStamps where try Self.stamp(file) != stamp {
                throw ImportFailure("A recording changed after copying. Import again before ejecting.")
            }
        }
        if recoverTrash, FileManager.default.fileExists(atPath: trash.path), !(try Self.files(trash, includeHidden: true)).isEmpty {
            throw ImportFailure("Device Trash changed. Import again before ejecting.")
        }
        try validate(); try cancellation.check()
        return Result(files: items.count, bytes: totalBytes, folder: dated)
    }

    private func save(_ sourceFile: URL, to originalTarget: URL, sha: String, before: Stamp,
                      progress: (String, Double) -> Void) throws -> URL {
        var target = originalTarget
        try Self.checkPath(target)
        if FileManager.default.fileExists(atPath: target.path) {
            if try Self.hash(target, cancellation: cancellation) == sha { return target }
            target = originalTarget.deletingPathExtension().appendingPathExtension(String(sha.prefix(16)) + "." + originalTarget.pathExtension)
            try Self.checkPath(target)
            if FileManager.default.fileExists(atPath: target.path) {
                guard try Self.hash(target, cancellation: cancellation) == sha else { throw ImportFailure("Conflicting destination file. Nothing overwritten.") }
                return target
            }
        }
        let capacity = try FileManager.default.attributesOfFileSystem(forPath: destination.path)[.systemFreeSize] as? NSNumber
        guard let capacity, capacity.int64Value > before.size + 1_073_741_824 else { throw ImportFailure("Not enough free space in the destination. Original kept.") }
        try FileManager.default.createDirectory(at: target.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Self.checkPath(target)
        let temporary = target.deletingLastPathComponent().appendingPathComponent(".easy-eject-\(UUID().uuidString).partial")
        let fd = open(temporary.path, O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW, 0o600)
        guard fd >= 0 else { throw ImportFailure("Cannot create a destination file.") }
        let output = FileHandle(fileDescriptor: fd, closeOnDealloc: true)
        defer { try? output.close(); try? FileManager.default.removeItem(at: temporary) }
        let inputFD = open(sourceFile.path, O_RDONLY | O_NOFOLLOW)
        guard inputFD >= 0 else { throw ImportFailure("Cannot open source for copying.") }
        let input = FileHandle(fileDescriptor: inputFD, closeOnDealloc: true)
        defer { try? input.close() }
        var copied: Int64 = 0
        while true {
            try cancellation.check()
            guard let data = try input.read(upToCount: 4 * 1024 * 1024), !data.isEmpty else { break }
            try output.write(contentsOf: data); copied += Int64(data.count)
            progress("Importing", before.size == 0 ? 1 : Double(copied) / Double(before.size))
        }
        try output.synchronize()
        guard fcntl(fd, F_FULLFSYNC) == 0 else { throw ImportFailure("Could not flush the saved copy to storage. Original kept.") }
        try output.close()
        guard try Self.hash(temporary, cancellation: cancellation, progress: { progress("Verifying copy", $0) }) == sha,
              try Self.stamp(sourceFile) == before else { throw ImportFailure("Copy verification failed. Original kept.") }
        try validate(); try Self.checkPath(target)
        // Atomic exclusive rename also works on destinations without hard-link support.
        guard renamex_np(temporary.path, target.path, UInt32(RENAME_EXCL)) == 0 else { throw ImportFailure("Could not commit the saved copy without overwriting a file. Original kept.") }
        let dirFD = open(target.deletingLastPathComponent().path, O_RDONLY)
        guard dirFD >= 0 else { throw ImportFailure("Cannot verify destination directory durability. Original kept.") }
        defer { close(dirFD) }
        guard fsync(dirFD) == 0 else { throw ImportFailure("Could not flush the destination directory. Original kept.") }
        try FileManager.default.setAttributes([.modificationDate: Date(timeIntervalSince1970: Double(before.seconds))], ofItemAtPath: target.path)
        return target
    }
}
