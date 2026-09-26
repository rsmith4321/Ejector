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
    var videosOnly = false
    var cleanLayout = false
    var sourceIdentifier = ""
    var includePreviews = true

    struct Result { let files: Int; let bytes: Int64; let folder: URL; var note: String = ""; var shouldAutoEject = true }
    struct Stamp: Equatable {
        let device: dev_t
        let inode: ino_t
        let size: off_t
        let seconds: Int
        let nanos: Int
        let changeSeconds: Int
        let changeNanos: Int
    }

    static func stamp(_ url: URL) throws -> Stamp {
        var s = stat()
        guard lstat(url.path, &s) == 0, (s.st_mode & S_IFMT) == S_IFREG else {
            throw ImportFailure("Cannot read a regular file: \(url.lastPathComponent)")
        }
        return Stamp(device: s.st_dev, inode: s.st_ino, size: s.st_size,
                     seconds: s.st_mtimespec.tv_sec, nanos: s.st_mtimespec.tv_nsec,
                     changeSeconds: s.st_ctimespec.tv_sec, changeNanos: s.st_ctimespec.tv_nsec)
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

    // Unknown files are preserved in a camera-folder backup, never discarded or
    // made eligible for source deletion by an extension we do not understand.
    static let videoExtensions: Set<String> = ["mp4", "mov", "m4v", "insv", "lrv", "avi", "mkv", "mts", "m2ts", "m2t", "mpg", "mpeg", "mxf", "360", "braw", "r3d", "crm"]
    static let photoExtensions: Set<String> = ["jpg", "jpeg", "gif", "bmp", "webp", "avif", "jxl", "gpr", "heic", "heif", "hif", "png", "tif", "tiff", "dng", "insp", "arw", "srf", "sr2", "cr2", "cr3", "crw", "nef", "nrw", "orf", "rw2", "raw", "raf", "pef", "srw", "x3f", "3fr", "fff", "iiq", "rwl", "mos", "mrw", "kdc", "dcr", "erf", "mef", "mdc", "mpo"]
    static let videoSidecarExtensions: Set<String> = ["srt", "thm", "lrf", "xmp", "xml", "rmd", "bim", "sidecar", "gyro", "bbl", "bfl", "gcsv"]
    static let structuredFolders: Set<String> = ["AVCHD", "BDMV", "BPAV", "XDROOT", "M4ROOT", "PXROOT", "CINEROOT", "CRM", "XFVC", "CONTENTS", "VIDEO_TS", "AUDIO_TS", "PROXY", "PROXIES"]
    static let structuredExtensions: Set<String> = ["mts", "m2ts", "m2t", "mxf", "r3d", "crm", "ari", "arx", "cif", "sif", "mpl", "cpi"]

    static func isDeviceIndex(_ file: URL) -> Bool { file.lastPathComponent.lowercased() == "fileinfo_list.list" }
    static func normalized(_ text: String) -> String { text.precomposedStringWithCanonicalMapping.lowercased() }
    static func stem(_ file: URL) -> String { normalized(file.deletingPathExtension().path) }

    static func selectedFiles(_ files: [URL], videosOnly: Bool) -> [URL] {
        let candidates = files.filter { !isDeviceIndex($0) }
        guard videosOnly else { return candidates }
        let photoStems = Set(candidates.filter { photoExtensions.contains($0.pathExtension.lowercased()) }.map(stem))
        // A same-name still/MOV pair may be a Live Photo. Preserve both for the photo app.
        let masters = candidates.filter { videoExtensions.contains($0.pathExtension.lowercased()) && $0.pathExtension.lowercased() != "lrv" && !photoStems.contains(stem($0)) }
        let masterKeys = Set(masters.map { normalized($0.deletingLastPathComponent().path) + "/" + association($0) })
        let videos = masters + candidates.filter {
            $0.pathExtension.lowercased() == "lrv" && masterKeys.contains(normalized($0.deletingLastPathComponent().path) + "/" + association($0))
        }
        let videoStems = Set(videos.map(stem))
        let videoPaths = Set(videos.map { normalized($0.path) })
        let otherStems = Set(candidates.filter {
            !videoExtensions.contains($0.pathExtension.lowercased()) && !videoSidecarExtensions.contains($0.pathExtension.lowercased())
        }.map(stem))
        let videoSet = Set(videos)
        return candidates.filter { file in
            if videoSet.contains(file) { return true }
            guard videoSidecarExtensions.contains(file.pathExtension.lowercased()) else { return false }
            let key = stem(file)
            return videoPaths.contains(key) || (videoStems.contains(key) && !otherStems.contains(key))
        }
    }

    static func knownMedia(_ file: URL) -> Bool {
        let ext = file.pathExtension.lowercased()
        return videoExtensions.contains(ext) || photoExtensions.contains(ext) || ["wav", "mp3", "aac", "m4a"].contains(ext)
    }

    static func recognizedFiles(_ files: [URL]) -> Set<URL> {
        let masters = files.filter { knownMedia($0) && $0.pathExtension.lowercased() != "lrv" }
        let masterKeys = Set(masters.filter { videoExtensions.contains($0.pathExtension.lowercased()) }.map { normalized($0.deletingLastPathComponent().path) + "/" + association($0) })
        let media = masters + files.filter { $0.pathExtension.lowercased() == "lrv" && masterKeys.contains(normalized($0.deletingLastPathComponent().path) + "/" + association($0)) }
        let mediaSet = Set(media)
        let keys = Set(media.map(stem)).union(media.map { normalized($0.path) })
        return Set(files.filter { mediaSet.contains($0) || (videoSidecarExtensions.contains($0.pathExtension.lowercased()) && keys.contains(stem($0))) })
    }

    static func isStructuredComponent(_ name: String) -> Bool {
        structuredFolders.contains(name.uppercased()) || ["rdc", "rdm"].contains(URL(fileURLWithPath: name).pathExtension.lowercased())
    }

    static func isOptionalPreview(_ file: URL, root: URL) -> Bool {
        let directories = [root.lastPathComponent] + Array(file.pathComponents.dropFirst(root.pathComponents.count).dropLast())
        // Metadata/proxies inside a required recording package are a unit, not optional clutter.
        let required = directories.contains { isStructuredComponent($0) && !["PROXY", "PROXIES"].contains($0.uppercased()) }
        guard !required else { return false }
        return ["lrv", "lrf", "thm"].contains(file.pathExtension.lowercased()) || directories.contains { ["PROXY", "PROXIES"].contains($0.uppercased()) }
    }

    // Include the complete support tree for a recognized recording package; these
    // may contain thumbnail JPGs/audio/XML that are not standalone photo imports.
    // Only actual selected video/companions are eligible for deletion in video-only.
    func selectedMedia(_ files: [URL], root: URL) -> [URL] {
        let candidates = includePreviews ? files : files.filter { !Self.isOptionalPreview($0, root: root) }
        let ordinary = Set(Self.selectedFiles(candidates, videosOnly: videosOnly))
        guard videosOnly else { return candidates.filter { !Self.isDeviceIndex($0) } }
        let rootPackage = Self.isStructuredComponent(root.lastPathComponent) || root.lastPathComponent.uppercased() == "PRIVATE"
        let packageFiles = candidates.filter { file in
            rootPackage || file.pathComponents.dropFirst(root.pathComponents.count).dropLast().contains(where: Self.isStructuredComponent)
        }
        let hasP2 = packageFiles.contains { $0.pathComponents.contains(where: { $0.uppercased() == "CONTENTS" }) }
        let included = ordinary.union(packageFiles)
        return candidates.filter { !Self.isDeviceIndex($0) && (included.contains($0) || (hasP2 && $0.lastPathComponent.uppercased() == "LASTCLIP.TXT")) }
    }

    func completePackageSelected(_ files: [URL]) -> Bool {
        // Optional GoPro Proxy folders can be preserved from an ordinary DCIM selection.
        let hardMarker: (String) -> Bool = { Self.isStructuredComponent($0) && !["PROXY", "PROXIES"].contains($0.uppercased()) }
        let required = mediaFolder.pathComponents.dropFirst(source.pathComponents.count).contains(where: hardMarker) || files.contains { file in
            file.pathComponents.dropFirst(mediaFolder.pathComponents.count).contains(where: hardMarker) || Self.structuredExtensions.contains(file.pathExtension.lowercased()) || ["pro.prj", "lastclip.txt", "index.bdm"].contains(file.lastPathComponent.lowercased())
        }
        if !required { return true }
        if mediaFolder.standardizedFileURL == source.standardizedFileURL { return true }
        // P2's sibling LASTCLIP.TXT and RED reel parents require the full card.
        return ["PRIVATE", "AVCHD", "BPAV", "M4ROOT", "XDROOT", "PXROOT", "CINEROOT", "CRM", "XFVC"].contains(mediaFolder.lastPathComponent.uppercased())
    }

    func supportsVideoPackages(_ files: [URL], root: URL) -> Bool {
        let rootPackage = Self.isStructuredComponent(root.lastPathComponent) || root.lastPathComponent.uppercased() == "PRIVATE"
        return !files.contains { file in
            if file.lastPathComponent.lowercased() == "pro.prj" { return true }
            guard Self.structuredExtensions.contains(file.pathExtension.lowercased()) else { return false }
            return !rootPackage && !file.pathComponents.dropFirst(root.pathComponents.count).dropLast().contains(where: Self.isStructuredComponent)
        }
    }

    func needsCameraStructure(_ files: [URL]) -> Bool {
        if mediaFolder.pathComponents.dropFirst(source.pathComponents.count).contains(where: Self.isStructuredComponent) { return true }
        return files.contains { file in
            let relative = file.pathComponents.dropFirst(mediaFolder.pathComponents.count)
            return relative.contains(where: Self.isStructuredComponent) || Self.structuredExtensions.contains(file.pathExtension.lowercased()) ||
                ["pro.prj", "lastclip.txt", "index.bdm"].contains(file.lastPathComponent.lowercased())
        }
    }

    // A semantic stem collision also matters: clip.MOV from one folder must not
    // acquire another folder's clip.XMP merely because the full filenames differ.
    static func association(_ file: URL) -> String {
        var key = normalized(file.deletingPathExtension().lastPathComponent)
        if videoSidecarExtensions.contains(file.pathExtension.lowercased()), videoExtensions.contains(URL(fileURLWithPath: key).pathExtension) {
            key = URL(fileURLWithPath: key).deletingPathExtension().lastPathComponent
        }
        if let range = key.range(of: "^(?:pro_vid|vid|lrv|img)_([0-9]{8}_[0-9]{6})_(?:[0-9]{2}_)?([0-9]+)$", options: .regularExpression) {
            let value = String(key[range]).split(separator: "_")
            let date = value.firstIndex(where: { $0.count == 8 && $0.allSatisfy(\.isNumber) })!
            return "insta-" + value[date] + "-" + value[date + 1] + "-" + value.last!
        }
        if key.range(of: "^g[hxl][0-9]{6}$", options: .regularExpression) != nil { return "gopro-" + key.dropFirst(2) }
        return key
    }

    private func fingerprint(_ files: [URL], root: URL) throws -> String {
        var hash = SHA256()
        hash.update(data: Data((sourceIdentifier.isEmpty ? source.path : sourceIdentifier).utf8))
        for file in files.sorted(by: { $0.path < $1.path }) {
            try validate(); try cancellation.check()
            let relative = String(file.path.dropFirst(root.path.count + 1))
            hash.update(data: Data(("\n" + relative + ":" + (try Self.hash(file, cancellation: cancellation))).utf8))
        }
        return hash.finalize().map { String(format: "%02x", $0) }.joined().prefix(24).description
    }

    private func targetConflicts(_ file: URL, _ target: URL) throws -> Bool {
        try Self.checkPath(target)
        guard FileManager.default.fileExists(atPath: target.path) else { return false }
        return try Self.hash(target, cancellation: cancellation) != Self.hash(file, cancellation: cancellation)
    }

    private func preservedTargets(_ files: [URL], root: URL, folder: URL, snapshot: Bool = false) throws -> [URL: URL] {
        var targetRoot = folder
        let keys = files.map { Self.normalized(String($0.path.dropFirst(root.path.count + 1))) }
        guard Set(keys).count == keys.count else { throw ImportFailure("Names differ only by case or Unicode form. Keep originals and use a case-sensitive destination with a camera-specific transfer tool.") }
        var conflict = snapshot
        if !conflict {
            for file in files where try targetConflicts(file, folder.appendingPathComponent(String(file.path.dropFirst(root.path.count + 1)))) { conflict = true }
        }
        if conflict { targetRoot = folder.appendingPathComponent("Additional media").appendingPathComponent(try fingerprint(files, root: root)) }
        return Dictionary(uniqueKeysWithValues: files.map { ($0, targetRoot.appendingPathComponent(String($0.path.dropFirst(root.path.count + 1)))) })
    }

    // Camera filenames remain unchanged in every layout, including collision and
    // Trash paths. A conflicting group gets its own folder, never individual suffixes.
    private func cleanTargets(_ files: [URL], dated: URL) throws -> [URL: URL] {
        let groups = Dictionary(grouping: files, by: { $0.deletingLastPathComponent() })
        let associations = Dictionary(grouping: files, by: Self.association)
        let existing = FileManager.default.fileExists(atPath: dated.path) ? try FileManager.default.contentsOfDirectory(at: dated, includingPropertiesForKeys: nil).filter { (try? $0.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true } : []
        var result: [URL: URL] = [:]
        for parent in groups.keys.sorted(by: { $0.path < $1.path }) {
            let group = groups[parent]!.sorted { $0.path < $1.path }
            let names = Set(group.map { Self.normalized($0.lastPathComponent) })
            guard names.count == group.count else { throw ImportFailure("Names differ only by case or Unicode form. Originals kept; use a camera-specific transfer tool.") }
            let keys = Set(group.map(Self.association))
            var conflict = group.contains { Set((associations[Self.association($0)] ?? []).map { $0.deletingLastPathComponent() }).count > 1 }
            if existing.contains(where: { keys.contains(Self.association($0)) && !names.contains(Self.normalized($0.lastPathComponent)) }) { conflict = true }
            for file in group where try targetConflicts(file, dated.appendingPathComponent(file.lastPathComponent)) { conflict = true }
            var folder = dated
            if conflict { folder = dated.appendingPathComponent("Additional media").appendingPathComponent(try fingerprint(group, root: mediaFolder)) }
            for file in group { result[file] = folder.appendingPathComponent(file.lastPathComponent) }
        }
        return result
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
        let scanned = try Self.files(mediaFolder, includeHidden: false)
        let structured = needsCameraStructure(includePreviews ? scanned : scanned.filter { !Self.isOptionalPreview($0, root: mediaFolder) })
        if structured && (!completePackageSelected(scanned) || (videosOnly && !supportsVideoPackages(scanned, root: mediaFolder))) {
            throw ImportFailure("This selection is part of a camera recording package. Select the complete camera folder or whole card so audio, metadata and clips stay together. For unrecognized or multi-camera packages, use All media and sidecars. No files were removed.")
        }
        let media = selectedMedia(scanned, root: mediaFolder)
        let indexes = scanned.filter(Self.isDeviceIndex)
        let recognized = Self.recognizedFiles(media)
        let preserveStructure = structured || (!videosOnly && media.contains { !recognized.contains($0) })
        var trashFiles: [URL] = []
        if recoverTrash {
            // Probe the parent with a throwing directory read. fileExists alone can hide permission denial.
            let parent = source.appendingPathComponent(".Trashes")
            if try FileManager.default.contentsOfDirectory(atPath: source.path).contains(".Trashes") {
                let names = try FileManager.default.contentsOfDirectory(atPath: parent.path)
                if names.contains(String(getuid())) {
                    let scannedTrash = try Self.files(trash, includeHidden: true)
                    if videosOnly && needsCameraStructure(scannedTrash) && !supportsVideoPackages(scannedTrash, root: trash) { throw ImportFailure("Device Trash contains a structured camera recording. Turn off Trash recovery or use All media and sidecars; no files were removed.") }
                    trashFiles = selectedMedia(scannedTrash, root: trash)
                }
            }
        }
        report(ImportProgress(phase: "Preparing import"))
        try Self.checkPath(dated)
        let mediaTargets: [URL: URL]
        if preserveStructure {
            let snapshot = dated.appendingPathComponent("Camera originals").appendingPathComponent(try fingerprint(media, root: mediaFolder))
            mediaTargets = try preservedTargets(media, root: mediaFolder, folder: mediaFolder == source ? snapshot : snapshot.appendingPathComponent(mediaFolder.lastPathComponent))
        } else {
            mediaTargets = cleanLayout ? try cleanTargets(media, dated: dated) : try preservedTargets(media, root: mediaFolder, folder: dated)
        }
        var targets = mediaTargets
        targets.merge(try preservedTargets(trashFiles, root: trash, folder: dated.appendingPathComponent("Recovered Device Trash"))) { current, _ in current }
        for file in indexes {
            let relative = String(file.path.dropFirst(mediaFolder.path.count + 1))
            targets[file] = dated.appendingPathComponent(".easy-eject-metadata").appendingPathComponent(try fingerprint([file], root: mediaFolder)).appendingPathComponent(relative)
        }
        let items = media + trashFiles + indexes
        let deletable = videosOnly ? Set(Self.selectedFiles(media + trashFiles, videosOnly: true)) : recognized.union(Self.recognizedFiles(trashFiles))
        let indexSet = Set(indexes)
        let totalBytes = try items.reduce(Int64(0)) { try $0 + Self.stamp($1).size }
        var completedBytes: Int64 = 0
        var retainedStamps: [URL: Stamp] = [:]
        var savedStamps: [URL: Stamp] = [:]
        var newSelectedCopy = false
        let initialStamps = try Dictionary(uniqueKeysWithValues: (media + trashFiles).map { ($0, try Self.stamp($0)) })
        var removals: [(source: URL, saved: URL, before: Stamp, savedBefore: Stamp)] = []
        for (index, file) in items.enumerated() {
            try cancellation.check(); try validate()
            let fromTrash = index >= media.count && index < media.count + trashFiles.count
            guard let target = targets[file] else { throw ImportFailure("Import plan is incomplete. Originals kept.") }
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
            let copy = try save(file, to: target, sha: sha, before: before, progress: progress)
            let saved = copy.url
            if !copy.reused && deletable.contains(file) { newSelectedCopy = true }
            try validate(); try Self.checkPath(file); try Self.checkPath(saved)
            let savedBefore = try Self.stamp(saved)
            let savedHash = try Self.hash(saved, cancellation: cancellation) { progress("Verifying", $0) }
            guard savedHash == sha, try Self.stamp(saved) == savedBefore,
                  try Self.stamp(file) == before else { throw ImportFailure("Verification failed. Original kept: \(file.lastPathComponent)") }
            try Self.flushSavedCopy(saved)
            savedStamps[saved] = savedBefore
            try audit("Verified SHA256 \(sha) | \(file.path) -> \(saved.path)")
            try cancellation.check(); try validate()
            if (deleteOriginals || fromTrash) && deletable.contains(file) && !indexSet.contains(file) {
                guard try Self.stamp(file) == before, try Self.stamp(saved) == savedBefore else { throw ImportFailure("A file changed before deletion. Original kept.") }
                removals.append((file, saved, before, savedBefore))
            }
            if !indexSet.contains(file) { retainedStamps[file] = before }
            completedBytes += before.size
            report(ImportProgress(phase: "Imported", file: file.lastPathComponent, completed: index + 1,
                                  total: items.count, fraction: totalBytes == 0 ? 1 : Double(completedBytes) / Double(totalBytes)))
        }
        // Recheck the selected manifest before deletion as well as before eject.
        // In video-only mode, unrelated photos and their edits do not block completion.
        func checkManifest(_ root: URL, expected: [URL], hidden: Bool) throws {
            let current = selectedMedia(try Self.files(root, includeHidden: hidden), root: root)
            guard Set(current) == Set(expected) else { throw ImportFailure("Device contents changed. Import again before deleting or ejecting; originals kept.") }
            for file in expected {
                guard try Self.stamp(file) == initialStamps[file] else { throw ImportFailure("A recording changed during import. Originals kept.") }
            }
        }
        try checkManifest(mediaFolder, expected: media, hidden: false)
        if recoverTrash && FileManager.default.fileExists(atPath: trash.path) { try checkManifest(trash, expected: trashFiles, hidden: true) }
        // Every saved dependency matters, including metadata whose source is retained.
        // Check all saved files before deleting even the first original.
        for (saved, expected) in savedStamps {
            try cancellation.check(); try validate(); try Self.checkPath(saved)
            guard try Self.stamp(saved) == expected else { throw ImportFailure("A saved file changed before deletion. Originals kept; import again.") }
        }
        // Verify the complete selected batch before removing any originals, so a
        // failed companion copy leaves all source files available for another attempt.
        for entry in removals {
            try cancellation.check(); try validate()
            try Self.checkPath(entry.source); try Self.checkPath(entry.saved)
            guard try Self.stamp(entry.source) == entry.before, try Self.stamp(entry.saved) == entry.savedBefore else {
                throw ImportFailure("A file changed before deletion. Remaining originals kept.")
            }
        }
        for entry in removals {
            try cancellation.check(); try validate()
            try Self.checkPath(entry.source); try Self.checkPath(entry.saved)
            guard try Self.stamp(entry.source) == entry.before, try Self.stamp(entry.saved) == entry.savedBefore else {
                throw ImportFailure("A file changed before deletion. Remaining originals kept.")
            }
            try FileManager.default.removeItem(at: entry.source)
            try audit("Removed verified source | \(entry.source.path)")
        }
        let removed = Set(removals.map(\.source))
        let remaining = selectedMedia(try Self.files(mediaFolder, includeHidden: false), root: mediaFolder)
        guard Set(remaining) == Set(media).subtracting(removed) else { throw ImportFailure("Device contents changed. Import again before ejecting.") }
        for (file, stamp) in retainedStamps where !removed.contains(file) {
            guard try Self.stamp(file) == stamp else { throw ImportFailure("A recording changed after copying. Import again before ejecting.") }
        }
        if recoverTrash, FileManager.default.fileExists(atPath: trash.path) {
            let remainingTrash = selectedMedia(try Self.files(trash, includeHidden: true), root: trash)
            guard Set(remainingTrash) == Set(trashFiles).subtracting(removed) else { throw ImportFailure("Device Trash changed. Import again before ejecting.") }
        }
        try validate(); try cancellation.check()
        var notes: [String] = []
        if preserveStructure { notes.append("Camera folders preserved for compatibility.") }
        if !indexes.isEmpty { notes.append("Device index backed up in .easy-eject-metadata and retained on device.") }
        if !includePreviews { notes.append("Optional previews left on device; required package files preserved.") }
        if videosOnly { notes.append("Photos and unrecognized files left on device.") }
        else if deleteOriginals && media.contains(where: { !deletable.contains($0) }) { notes.append("Unrecognized files kept on device.") }
        return Result(files: items.count, bytes: totalBytes, folder: dated, note: notes.joined(separator: " "), shouldAutoEject: newSelectedCopy || !removals.isEmpty)
    }

    private struct SavedCopy { let url: URL; let reused: Bool }

    private func save(_ sourceFile: URL, to originalTarget: URL, sha: String, before: Stamp,
                      progress: (String, Double) -> Void) throws -> SavedCopy {
        let target = originalTarget
        try Self.checkPath(target)
        if FileManager.default.fileExists(atPath: target.path) {
            if try Self.hash(target, cancellation: cancellation) == sha { return SavedCopy(url: target, reused: true) }
            throw ImportFailure("Destination changed during import. Original filenames and remaining sources kept; import again.")
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
        return SavedCopy(url: target, reused: false)
    }
}
