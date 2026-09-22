import Foundation
import Darwin

@main struct ImportEngineTests {
    static func main() throws {
        var passed = 0
        func test(_ name: String, _ body: (URL, URL, URL) throws -> Void) throws {
            let base = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("easy-eject-test-\(UUID().uuidString)")
            let source = base.appendingPathComponent("source"), media = source.appendingPathComponent("DCIM"), dest = base.appendingPathComponent("destination")
            try FileManager.default.createDirectory(at: media, withIntermediateDirectories: true)
            try FileManager.default.createDirectory(at: dest, withIntermediateDirectories: true)
            defer { try? FileManager.default.removeItem(at: base) }
            try body(source, media, dest); passed += 1; print("PASS \(name)")
        }
        func check(_ condition: Bool, _ message: String) throws { if !condition { throw ImportFailure("TEST FAILED: \(message)") } }
        func fails(_ body: () throws -> Void) throws {
            var failed = false
            do { try body() } catch { failed = true }
            try check(failed, "expected a failure")
        }
        func engine(_ source: URL, _ media: URL, _ dest: URL, delete: Bool = true, trash: Bool = false,
                    token: ImportCancellation = ImportCancellation(), validate: @escaping () throws -> Void = {},
                    report: @escaping (ImportProgress) -> Void = { _ in }, audit: @escaping (String) throws -> Void = { _ in }) -> MediaImportEngine {
            MediaImportEngine(source: source, mediaFolder: media, destination: dest, deleteOriginals: delete,
                              recoverTrash: trash, cancellation: token, validate: validate, report: report, audit: audit)
        }
        let content = Data(repeating: 73, count: 200_000)
        try test("copy, verify, delete, preserve nested folders") { source, media, dest in
            let sub = media.appendingPathComponent("DJI_001"); try FileManager.default.createDirectory(at: sub, withIntermediateDirectories: true)
            let file = sub.appendingPathComponent("clip.MP4"); try content.write(to: file)
            let result = try engine(source, media, dest).run()
            try check(result.files == 1 && result.bytes == content.count, "count and byte totals")
            try check(!FileManager.default.fileExists(atPath: file.path), "source removed")
            try check(try Data(contentsOf: result.folder.appendingPathComponent("DJI_001/clip.MP4")) == content, "copy identical")
        }
        try test("copy-only and repeat import reuse saved file") { source, media, dest in
            let file = media.appendingPathComponent("clip.mp4"); try content.write(to: file)
            _ = try engine(source, media, dest, delete: false).run()
            let r = try engine(source, media, dest, delete: false).run()
            try check(try Data(contentsOf: file) == content, "original retained")
            try check(try MediaImportEngine.files(r.folder, includeHidden: true).count == 1, "no duplicate")
        }
        try test("name collision never overwrites") { source, media, dest in
            let file = media.appendingPathComponent("clip.mp4"); try content.write(to: file)
            let r = try engine(source, media, dest, delete: false).run()
            try Data("new recording".utf8).write(to: file)
            _ = try engine(source, media, dest).run()
            try check(try Data(contentsOf: r.folder.appendingPathComponent("clip.mp4")) == content, "first copy kept")
            try check(try MediaImportEngine.files(r.folder, includeHidden: true).count == 2, "collision preserved separately")
        }
        try test("RAW and unfamiliar sidecars are imported; hidden metadata remains") { source, media, dest in
            for name in ["photo.CR3", "motion.futureformat", "INDEX", ".DS_Store"] {
                try content.write(to: media.appendingPathComponent(name))
            }
            let result = try engine(source, media, dest).run()
            try check(result.files == 3, "all visible files counted")
            for name in ["photo.CR3", "motion.futureformat", "INDEX"] {
                try check(try Data(contentsOf: result.folder.appendingPathComponent(name)) == content, "format saved intact")
                try check(!FileManager.default.fileExists(atPath: media.appendingPathComponent(name).path), "verified source removed")
            }
            try check(FileManager.default.fileExists(atPath: media.appendingPathComponent(".DS_Store").path), "hidden metadata retained")
        }
        try test("missing destination preserves original") { source, media, dest in
            let file = media.appendingPathComponent("clip.mp4"); try content.write(to: file)
            try FileManager.default.removeItem(at: dest)
            try fails { _ = try engine(source, media, dest).run() }
            try check(FileManager.default.fileExists(atPath: file.path), "original kept")
        }
        try test("cancel during copy preserves source and clears partial") { source, media, dest in
            let file = media.appendingPathComponent("clip.mp4"); try content.write(to: file)
            let token = ImportCancellation()
            try fails { _ = try engine(source, media, dest, token: token, report: { if $0.phase == "Importing" { token.cancel() } }).run() }
            try check(FileManager.default.fileExists(atPath: file.path), "cancel kept original")
            try check(try MediaImportEngine.files(dest, includeHidden: true).isEmpty, "no partial left")
        }
        try test("volume validation failure preserves original") { source, media, dest in
            let file = media.appendingPathComponent("clip.mp4"); try content.write(to: file)
            var invalid = false
            try fails { _ = try engine(source, media, dest, validate: { if invalid { throw ImportFailure("disconnected") } }, report: { if $0.phase == "Verifying" { invalid = true } }).run() }
            try check(FileManager.default.fileExists(atPath: file.path), "disconnect kept original")
        }
        try test("audit write failure prevents deletion") { source, media, dest in
            let file = media.appendingPathComponent("clip.mp4"); try content.write(to: file)
            try fails { _ = try engine(source, media, dest, audit: { _ in throw ImportFailure("log full") }).run() }
            try check(FileManager.default.fileExists(atPath: file.path), "audit failure kept original")
        }
        try test("source symlink rejected") { source, media, dest in
            try FileManager.default.createSymbolicLink(at: media.appendingPathComponent("clip.mp4"), withDestinationURL: dest)
            try fails { _ = try engine(source, media, dest).run() }
        }
        try test("destination symlink rejected") { source, media, dest in
            let file = media.appendingPathComponent("clip.mp4"); try content.write(to: file)
            let link = dest.appendingPathComponent("link"); try FileManager.default.createSymbolicLink(at: link, withDestinationURL: source)
            try fails { _ = try engine(source, media, link).run() }
            try check(FileManager.default.fileExists(atPath: file.path), "symlink kept original")
        }
        try test("trash recovered before removal") { source, media, dest in
            let trash = source.appendingPathComponent(".Trashes/\(getuid())"); try FileManager.default.createDirectory(at: trash, withIntermediateDirectories: true)
            let file = trash.appendingPathComponent("deleted.mp4"); try content.write(to: file)
            let r = try engine(source, media, dest, trash: true).run()
            try check(try Data(contentsOf: r.folder.appendingPathComponent("Recovered Device Trash/deleted.mp4")) == content, "trash recovered")
            try check(!FileManager.default.fileExists(atPath: file.path), "trash cleared")
        }
        try test("new recording blocks completion") { source, media, dest in
            try content.write(to: media.appendingPathComponent("clip.mp4"))
            try fails { _ = try engine(source, media, dest, report: { if $0.phase == "Imported" { try! content.write(to: media.appendingPathComponent("new.mp4")) } }).run() }
            try check(FileManager.default.fileExists(atPath: media.appendingPathComponent("new.mp4").path), "new footage kept")
        }
        try test("copy-only mutation blocks completion") { source, media, dest in
            let file = media.appendingPathComponent("clip.mp4"); try content.write(to: file)
            try fails { _ = try engine(source, media, dest, delete: false, report: { if $0.phase == "Imported" { try! Data("changed".utf8).write(to: file) } }).run() }
        }
        try test("cleanup preserves trash, links, and user files inside __MACOSX") { source, media, dest in
            let mac = source.appendingPathComponent("__MACOSX"); try FileManager.default.createDirectory(at: mac, withIntermediateDirectories: true)
            try content.write(to: mac.appendingPathComponent("keep.mp4"))
            try content.write(to: mac.appendingPathComponent("._junk"))
            try content.write(to: source.appendingPathComponent(".DS_Store"))
            let trash = source.appendingPathComponent(".Trashes"); try FileManager.default.createDirectory(at: trash, withIntermediateDirectories: true)
            try content.write(to: trash.appendingPathComponent("._keep"))
            try content.write(to: dest.appendingPathComponent("._outside"))
            try FileManager.default.createSymbolicLink(at: source.appendingPathComponent("link"), withDestinationURL: dest)
            let count = try MetadataCleaner.clean(source, validate: {})
            try check(count == 2, "only metadata removed")
            try check(FileManager.default.fileExists(atPath: mac.appendingPathComponent("keep.mp4").path), "user files kept")
            try check(FileManager.default.fileExists(atPath: trash.appendingPathComponent("._keep").path), "trash kept")
            try check(FileManager.default.fileExists(atPath: dest.appendingPathComponent("._outside").path), "link target kept")
        }
        print("\(passed) tests passed")
    }
}
