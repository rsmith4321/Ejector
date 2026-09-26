import Foundation
import Darwin

@main struct ImportEngineTests {
    static func main() throws {
        #if APP_STORE
        guard CommandLine.arguments.count == 2 else { throw ImportFailure("Pass the runner-created external sentinel path.") }
        #endif
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
                    videosOnly: Bool = false, cleanLayout: Bool = false, includePreviews: Bool = true, token: ImportCancellation = ImportCancellation(), validate: @escaping () throws -> Void = {},
                    report: @escaping (ImportProgress) -> Void = { _ in }, audit: @escaping (String) throws -> Void = { _ in }) -> MediaImportEngine {
            MediaImportEngine(source: source, mediaFolder: media, destination: dest, deleteOriginals: delete,
                              recoverTrash: trash, cancellation: token, validate: validate, report: report, audit: audit,
                              videosOnly: videosOnly, cleanLayout: cleanLayout, includePreviews: includePreviews)
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
            let first = try engine(source, media, dest, delete: false).run()
            let r = try engine(source, media, dest, delete: false).run()
            try check(first.shouldAutoEject, "first new media copy may finish with automatic eject")
            try check(!r.shouldAutoEject, "reconnected no-op copy-only device stays available to Lightroom")
            try check(try Data(contentsOf: file) == content, "original retained")
            try check(try MediaImportEngine.files(r.folder, includeHidden: true).count == 1, "no duplicate")
        }
        try test("reused saved copy with opted-in deletion may eject after verified removal") { source, media, dest in
            let file = media.appendingPathComponent("clip.mp4")
            try content.write(to: file)
            _ = try engine(source, media, dest, delete: false).run()
            let result = try engine(source, media, dest).run()
            try check(!FileManager.default.fileExists(atPath: file.path), "reused verified copy permits explicitly enabled source removal")
            try check(result.shouldAutoEject, "verified source deletion is meaningful work even when no new copy was needed")
        }
        try test("device-index-only import stays connected for the next photo workflow") { source, media, dest in
            let index = media.appendingPathComponent("fileinfo_list.list")
            try content.write(to: index)
            let first = try engine(source, media, dest, videosOnly: true, cleanLayout: true).run()
            let repeated = try engine(source, media, dest, videosOnly: true, cleanLayout: true).run()
            try check(!first.shouldAutoEject && !repeated.shouldAutoEject, "index backup alone never triggers automatic eject")
            try check(try Data(contentsOf: index) == content, "device index remains on source")
            try check(try MediaImportEngine.files(dest, includeHidden: true).count == 1, "repeated index backup reuses saved metadata")
        }
        try test("name collision never overwrites") { source, media, dest in
            let file = media.appendingPathComponent("clip.mp4"); try content.write(to: file)
            let r = try engine(source, media, dest, delete: false).run()
            try Data("new recording".utf8).write(to: file)
            _ = try engine(source, media, dest).run()
            try check(try Data(contentsOf: r.folder.appendingPathComponent("clip.mp4")) == content, "first copy kept")
            try check(try MediaImportEngine.files(r.folder, includeHidden: true).count == 2, "collision preserved separately")
        }
        try test("RAW and unfamiliar files are saved; unrecognized originals and hidden metadata remain") { source, media, dest in
            for name in ["photo.CR3", "motion.futureformat", "INDEX", ".DS_Store"] {
                try content.write(to: media.appendingPathComponent(name))
            }
            let result = try engine(source, media, dest).run()
            try check(result.files == 3, "all visible files counted")
            let saved = try MediaImportEngine.files(result.folder, includeHidden: true)
            for name in ["photo.CR3", "motion.futureformat", "INDEX"] {
                let copies = saved.filter { $0.lastPathComponent == name }
                try check(copies.count == 1 && (try Data(contentsOf: copies[0])) == content, "format saved intact")
                try check(FileManager.default.fileExists(atPath: media.appendingPathComponent(name).path) == (name != "photo.CR3"), "only recognized media original removed")
            }
            try check(FileManager.default.fileExists(atPath: media.appendingPathComponent(".DS_Store").path), "hidden metadata retained")
        }
        try test("videos only preserves Live Photos, RAW, unfamiliar files, and ambiguous sidecars") { source, media, dest in
            let selected = ["video.MP4", "video.SRT", "video.MP4.SRT"]
            let retained = ["photo.CR3", "photo.XMP", "live.HEIC", "live.MOV", "shared.JPG", "shared.MP4", "shared.XMP", "shared.JPG.XMP", "raw.GPR", "raw.MP4", "jpegxl.JXL", "jpegxl.MOV", "animation.GIF", "animation.MOV", "bitmap.BMP", "bitmap.MOV", "web.WEBP", "web.MOV", "modern.AVIF", "modern.MOV", "unknown.futureformat"]
            for name in selected + retained { try content.write(to: media.appendingPathComponent(name)) }
            let result = try engine(source, media, dest, videosOnly: true, cleanLayout: true).run()
            let saved = try MediaImportEngine.files(result.folder, includeHidden: true)
            try check(Set(saved.map { $0.lastPathComponent }) == Set(selected), "only independent video and its recognized companions copied")
            for name in selected { try check(!FileManager.default.fileExists(atPath: media.appendingPathComponent(name).path), "selected video original removed") }
            for name in retained { try check(try Data(contentsOf: media.appendingPathComponent(name)) == content, "photo or ambiguous original retained byte-for-byte") }
        }
        try test("videos-only Trash recovery preserves photo groups and unknown files") { source, media, dest in
            let trash = source.appendingPathComponent(".Trashes/" + String(getuid()))
            try FileManager.default.createDirectory(at: trash, withIntermediateDirectories: true)
            let selected = ["video.MOV", "video.SRT"]
            let retained = ["photo.DNG", "photo.XMP", "live.JPG", "live.MOV", "live.XMP", "unknown.data"]
            for name in selected + retained { try content.write(to: trash.appendingPathComponent(name)) }
            let result = try engine(source, media, dest, delete: false, trash: true, videosOnly: true, cleanLayout: true).run()
            let saved = try MediaImportEngine.files(result.folder, includeHidden: true)
            try check(Set(saved.map { $0.lastPathComponent }) == Set(selected), "Trash uses the same conservative video selection")
            for name in selected { try check(!FileManager.default.fileExists(atPath: trash.appendingPathComponent(name).path), "recovered video removed from Trash") }
            for name in retained { try check(try Data(contentsOf: trash.appendingPathComponent(name)) == content, "photo or unknown Trash original retained") }
        }
        try test("clean layout retains original Insta360 lens names and saves device index separately") { source, media, dest in
            let camera = media.appendingPathComponent("Camera01")
            try FileManager.default.createDirectory(at: camera, withIntermediateDirectories: true)
            let names = ["VID_20260926_103000_00_001.insv", "VID_20260926_103000_10_001.insv", "LRV_20260926_103000_11_001.lrv"]
            for name in names { try content.write(to: camera.appendingPathComponent(name)) }
            let index = media.appendingPathComponent("fileinfo_list.list")
            let indexBytes = Data("device calibration and recording index fixture".utf8)
            try indexBytes.write(to: index)
            let result = try engine(source, media, dest, cleanLayout: true).run()
            for name in names {
                try check(try Data(contentsOf: result.folder.appendingPathComponent(name)) == content, "ordinary camera media is directly accessible with exact filename")
                try check(!FileManager.default.fileExists(atPath: camera.appendingPathComponent(name).path), "verified media original removed")
            }
            let saved = try MediaImportEngine.files(result.folder, includeHidden: true)
            let indexes = saved.filter { $0.lastPathComponent == "fileinfo_list.list" }
            try check(indexes.count == 1 && indexes[0].pathComponents.contains(".easy-eject-metadata"), "device index preserved outside the clean visible media list")
            try check(try Data(contentsOf: indexes[0]) == indexBytes, "saved device metadata is intact")
            try check(try Data(contentsOf: index) == indexBytes, "device index is never deleted")
        }
        for clean in [false, true] {
            try test("filename conflict preserves a complete lens pair with layout \(clean ? "clean" : "camera")") { source, media, dest in
                let camera = media.appendingPathComponent("Camera01")
                try FileManager.default.createDirectory(at: camera, withIntermediateDirectories: true)
                let firstName = "VID_20260926_103000_00_001.insv"
                let secondName = "VID_20260926_103000_10_001.insv"
                let first = camera.appendingPathComponent(firstName), second = camera.appendingPathComponent(secondName)
                try content.write(to: first); try content.write(to: second)
                let original = try engine(source, media, dest, delete: false, cleanLayout: clean).run()
                let changed = Data("different recording with a recycled camera filename".utf8)
                try changed.write(to: first)
                _ = try engine(source, media, dest, cleanLayout: clean).run()
                let saved = try MediaImportEngine.files(original.folder, includeHidden: true)
                let newFirst = try saved.filter { file in
                    guard file.lastPathComponent == firstName else { return false }
                    return try Data(contentsOf: file) == changed
                }
                try check(newFirst.count == 1, "conflicting recording saved with its exact original filename")
                try check(try Data(contentsOf: newFirst[0].deletingLastPathComponent().appendingPathComponent(secondName)) == content, "both lens files stay together")
                try check(try saved.contains { file in
                    guard file.lastPathComponent == firstName else { return false }
                    return try Data(contentsOf: file) == content
                }, "previous recording remains intact")
                try check(saved.allSatisfy { [firstName, secondName].contains($0.lastPathComponent) }, "no per-file filename suffixes break camera associations")
            }
        }
        try test("clean layout does not merge different camera groups sharing a clip stem") { source, media, dest in
            for path in ["CameraA/clip.MP4", "CameraA/clip.XMP", "CameraB/clip.MOV", "CameraB/clip.SRT"] {
                let file = media.appendingPathComponent(path)
                try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
                try Data(path.utf8).write(to: file)
            }
            let result = try engine(source, media, dest, cleanLayout: true).run()
            let saved = try MediaImportEngine.files(result.folder, includeHidden: true)
            func savedFile(_ name: String) throws -> URL {
                guard let file = saved.first(where: { $0.lastPathComponent == name }) else { throw ImportFailure("Missing fixture \(name)") }
                return file
            }
            let a = try savedFile("clip.MP4").deletingLastPathComponent()
            let b = try savedFile("clip.MOV").deletingLastPathComponent()
            try check(a != b, "different cameras retain distinct clip identities despite different file extensions")
            try check(try savedFile("clip.XMP").deletingLastPathComponent() == a, "first camera companion stays with its video")
            try check(try savedFile("clip.SRT").deletingLastPathComponent() == b, "second camera companion stays with its video")
        }
        try test("Trash collision preserves video and sidecar names as one group") { source, media, dest in
            let trash = source.appendingPathComponent(".Trashes/\(getuid())")
            try FileManager.default.createDirectory(at: trash, withIntermediateDirectories: true)
            let names = ["recording.mp4", "recording.srt"]
            for name in names { try content.write(to: trash.appendingPathComponent(name)) }
            let original = try engine(source, media, dest, delete: false, trash: true).run()
            let changed = Data("recovered second recording".utf8)
            for name in names { try changed.write(to: trash.appendingPathComponent(name)) }
            _ = try engine(source, media, dest, delete: false, trash: true).run()
            let saved = try MediaImportEngine.files(original.folder, includeHidden: true)
            let videos = try saved.filter { file in
                guard file.lastPathComponent == "recording.mp4" else { return false }
                return try Data(contentsOf: file) == changed
            }
            try check(videos.count == 1, "recovered collision retains video filename")
            try check(try Data(contentsOf: videos[0].deletingLastPathComponent().appendingPathComponent("recording.srt")) == changed, "recovered companion remains next to its video with its original name")
            try check(saved.allSatisfy { names.contains($0.lastPathComponent) }, "recovery never adds per-file hash suffixes")
        }
        try test("partial structured camera folder refuses videos-only before copying or deleting") { source, _, dest in
            let clip = source.appendingPathComponent("M4ROOT/CLIP/C0001.MP4")
            try FileManager.default.createDirectory(at: clip.deletingLastPathComponent(), withIntermediateDirectories: true)
            try content.write(to: clip)
            try content.write(to: source.appendingPathComponent("M4ROOT/MEDIAPRO.XML"))
            try fails { _ = try engine(source, clip.deletingLastPathComponent(), dest, videosOnly: true, cleanLayout: true).run() }
            try check(try Data(contentsOf: clip) == content, "structured recording retained")
            try check(try MediaImportEngine.files(dest, includeHidden: true).isEmpty, "incompatible selection fails before destination mutation")
        }
        try test("structured camera package preserves saved hierarchy and limits opted-in deletion to recognized media") { source, _, dest in
            let paths = ["M4ROOT/CLIP/C0001.MP4", "M4ROOT/CLIP/C0001M01.XML", "M4ROOT/MEDIAPRO.XML"]
            for path in paths {
                let file = source.appendingPathComponent(path)
                try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
                try Data(path.utf8).write(to: file)
            }
            let result = try engine(source, source, dest, cleanLayout: true).run()
            let saved = try MediaImportEngine.files(result.folder, includeHidden: true)
            for path in paths {
                let copies = saved.filter { $0.path.hasSuffix("/" + path) }
                try check(copies.count == 1 && copies[0].pathComponents.contains("Camera originals"), "structured package hierarchy preserved")
                try check(try Data(contentsOf: copies[0]) == Data(path.utf8), "structured file saved intact")
                if path.hasSuffix(".MP4") {
                    try check(!FileManager.default.fileExists(atPath: source.appendingPathComponent(path).path), "recognized structured recording removed after opted-in verification")
                } else {
                    try check(try Data(contentsOf: source.appendingPathComponent(path)) == Data(path.utf8), "unassociated structured metadata retained on device")
                }
            }
        }
        for selectPackageFolder in [false, true] {
            try test("Sony videos-only copies full package and preserves photo workflow with selection " + (selectPackageFolder ? "M4ROOT" : "whole card")) { source, media, dest in
                let packagePaths = ["M4ROOT/CLIP/C0001.MP4", "M4ROOT/CLIP/C0001M01.XML", "M4ROOT/MEDIAPRO.XML", "M4ROOT/THMB/C0001.JPG"]
                for path in packagePaths {
                    let file = source.appendingPathComponent(path)
                    try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
                    try Data(path.utf8).write(to: file)
                }
                let photos = ["photo.ARW", "photo.JPG", "photo.XMP", "live.HEIC", "live.MOV"]
                for name in photos { try content.write(to: media.appendingPathComponent(name)) }
                let selectedRoot = selectPackageFolder ? source.appendingPathComponent("M4ROOT") : source
                let result = try engine(source, selectedRoot, dest, videosOnly: true, cleanLayout: true).run()
                let saved = try MediaImportEngine.files(result.folder, includeHidden: true)
                try check(saved.count == packagePaths.count, "standalone DCIM photos and Live Photo movie excluded from video package import")
                for path in packagePaths {
                    let copies = saved.filter { $0.path.hasSuffix("/" + path) }
                    try check(copies.count == 1, "complete package wrapper and support tree preserved")
                    try check(try Data(contentsOf: copies[0]) == Data(path.utf8), "package support copy intact")
                    if path.hasSuffix(".MP4") {
                        try check(!FileManager.default.fileExists(atPath: source.appendingPathComponent(path).path), "opted-in verified Sony video removed")
                    } else {
                        try check(try Data(contentsOf: source.appendingPathComponent(path)) == Data(path.utf8), "package thumbnail and unassociated metadata remain on source")
                    }
                }
                for name in photos { try check(try Data(contentsOf: media.appendingPathComponent(name)) == content, "photo workflow input unchanged") }
            }
        }
        try test("GoPro proxy and master retain their distinct hierarchy with identical names") { source, media, dest in
            let master = media.appendingPathComponent("100GOPRO/GX010001.MP4")
            let proxy = media.appendingPathComponent("100GOPRO/Proxy/GX010001.MP4")
            for file in [master, proxy] { try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true) }
            try Data("full resolution recording".utf8).write(to: master)
            try Data("smaller proxy recording".utf8).write(to: proxy)
            let result = try engine(source, source, dest, videosOnly: true, cleanLayout: true).run()
            let saved = try MediaImportEngine.files(result.folder, includeHidden: true)
            let masters = saved.filter { $0.path.hasSuffix("/DCIM/100GOPRO/GX010001.MP4") }
            try check(masters.count == 1 && saved.count == 2, "master copied without flattening proxy into it")
            try check(try Data(contentsOf: masters[0]) == Data("full resolution recording".utf8), "master bytes unchanged")
            try check(try Data(contentsOf: masters[0].deletingLastPathComponent().appendingPathComponent("Proxy/GX010001.MP4")) == Data("smaller proxy recording".utf8), "proxy retains its camera-relative location")
        }
        try test("P2 videos-only copies CONTENTS audio and LASTCLIP while preserving unrelated photos") { source, media, dest in
            let paths = ["CONTENTS/VIDEO/0001.MXF", "CONTENTS/AUDIO/0001.WAV", "CONTENTS/CLIP/0001.XML", "CONTENTS/ICON/0001.BMP", "LASTCLIP.TXT"]
            for path in paths {
                let file = source.appendingPathComponent(path)
                try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
                try Data(path.utf8).write(to: file)
            }
            let photo = media.appendingPathComponent("photo.RW2")
            try content.write(to: photo)
            let result = try engine(source, source, dest, videosOnly: true, cleanLayout: true).run()
            let saved = try MediaImportEngine.files(result.folder, includeHidden: true)
            try check(saved.count == paths.count, "P2 package selected without unrelated photo")
            let videos = saved.filter { $0.path.hasSuffix("/CONTENTS/VIDEO/0001.MXF") }
            try check(videos.count == 1, "P2 video copied in correct hierarchy")
            let packageRoot = videos[0].deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            for path in paths { try check(try Data(contentsOf: packageRoot.appendingPathComponent(path)) == Data(path.utf8), "P2 sibling dependency saved together") }
            try check(!FileManager.default.fileExists(atPath: source.appendingPathComponent("CONTENTS/VIDEO/0001.MXF").path), "opted-in recognized video removed")
            for path in paths where !path.hasSuffix(".MXF") { try check(try Data(contentsOf: source.appendingPathComponent(path)) == Data(path.utf8), "P2 package support remains on source") }
            try check(try Data(contentsOf: photo) == content, "unrelated photo remains for Lightroom")
        }
        try test("orphan LRV is excluded from video import and retained after all-media backup") { source, media, dest in
            let proxy = media.appendingPathComponent("GL010001.LRV")
            try content.write(to: proxy)
            let videos = try engine(source, media, dest, videosOnly: true, cleanLayout: true).run()
            try check(videos.files == 0 && (try MediaImportEngine.files(dest, includeHidden: true)).isEmpty, "orphan proxy is not presented as a successfully imported recording")
            try check(try Data(contentsOf: proxy) == content, "video-only leaves orphan proxy on source")
            let all = try engine(source, media, dest, cleanLayout: true).run()
            let saved = try MediaImportEngine.files(all.folder, includeHidden: true)
            let copies = saved.filter { $0.lastPathComponent == proxy.lastPathComponent }
            try check(copies.count == 1 && (try Data(contentsOf: copies[0])) == content, "all-media preserves unfamiliar proxy without changing its name")
            try check(try Data(contentsOf: proxy) == content, "orphan proxy is retained despite original-deletion preference")
        }
        try test("GoPro LRV is imported and deleted only with its same-folder master") { source, media, dest in
            let names = ["GH010001.MP4", "GL010001.LRV"]
            for name in names { try content.write(to: media.appendingPathComponent(name)) }
            let result = try engine(source, media, dest, videosOnly: true, cleanLayout: true).run()
            let saved = try MediaImportEngine.files(result.folder, includeHidden: true)
            try check(Set(saved.map { $0.lastPathComponent }) == Set(names), "GoPro naming convention associates proxy with master")
            for name in names { try check(!FileManager.default.fileExists(atPath: media.appendingPathComponent(name).path), "verified master/proxy group removed after explicit deletion opt-in") }
        }
        try test("optional previews remain included by default") { source, media, dest in
            let paths = ["clip.MP4", "clip.SRT", "clip.LRV", "clip.LRF", "clip.THM", "Proxy/clip.MP4", "Proxies/extra.MP4"]
            for path in paths {
                let file = media.appendingPathComponent(path)
                try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
                try Data(path.utf8).write(to: file)
            }
            let result = try engine(source, source, dest, delete: false, videosOnly: true, cleanLayout: true).run()
            let saved = try MediaImportEngine.files(result.folder, includeHidden: true)
            try check(saved.count == paths.count, "default retains ordinary previews and proxy folders")
            for path in paths {
                let copies = saved.filter { $0.path.hasSuffix("/DCIM/" + path) }
                try check(copies.count == 1 && (try Data(contentsOf: copies[0])) == Data(path.utf8), "default preserves preview identity and camera-relative location")
            }
        }
        for videosOnly in [false, true] {
            try test("disabled optional previews remain untouched in " + (videosOnly ? "videos-only" : "all-media") + " mode") { source, media, dest in
                let masters = ["clip.MP4", "clip.SRT"]
                let skipped = ["clip.LRV", "clip.LRF", "clip.THM", "Proxy/clip.MP4", "Proxies/extra.MP4"]
                for path in masters + skipped {
                    let file = media.appendingPathComponent(path)
                    try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
                    try Data(path.utf8).write(to: file)
                }
                let result = try engine(source, source, dest, videosOnly: videosOnly, cleanLayout: true, includePreviews: false).run()
                let saved = try MediaImportEngine.files(result.folder, includeHidden: true)
                try check(Set(saved.map { $0.lastPathComponent }) == Set(masters) && saved.count == masters.count, "master and required SRT imported without optional previews")
                for path in masters { try check(!FileManager.default.fileExists(atPath: media.appendingPathComponent(path).path), "opted-in imported master and SRT removed") }
                for path in skipped { try check(try Data(contentsOf: media.appendingPathComponent(path)) == Data(path.utf8), "skipped preview remains intact on device") }
            }
        }
        try test("disabled optional previews remain in device Trash after video recovery") { source, media, dest in
            let trash = source.appendingPathComponent(".Trashes/" + String(getuid()))
            let masters = ["clip.MP4", "clip.SRT"]
            let skipped = ["clip.LRV", "clip.LRF", "clip.THM", "Proxy/clip.MP4", "Proxies/extra.MP4"]
            for path in masters + skipped {
                let file = trash.appendingPathComponent(path)
                try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
                try Data(path.utf8).write(to: file)
            }
            let result = try engine(source, media, dest, delete: false, trash: true, videosOnly: true, cleanLayout: true, includePreviews: false).run()
            let saved = try MediaImportEngine.files(result.folder, includeHidden: true)
            try check(Set(saved.map { $0.lastPathComponent }) == Set(masters) && saved.count == masters.count, "Trash recovery respects preview setting")
            for path in masters { try check(!FileManager.default.fileExists(atPath: trash.appendingPathComponent(path).path), "verified selected Trash recording removed") }
            for path in skipped { try check(try Data(contentsOf: trash.appendingPathComponent(path)) == Data(path.utf8), "skipped Trash preview is never removed") }
        }
        try test("required structured camera dependencies remain included when optional previews are disabled") { source, media, dest in
            let paths = ["M4ROOT/CLIP/C0001.MP4", "M4ROOT/CLIP/C0001M01.XML", "M4ROOT/CLIP/C0001.LRF", "M4ROOT/THMB/C0001.THM", "M4ROOT/Proxy/C0001.MP4"]
            for path in paths {
                let file = source.appendingPathComponent(path)
                try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
                try Data(path.utf8).write(to: file)
            }
            try content.write(to: media.appendingPathComponent("photo.ARW"))
            let result = try engine(source, source, dest, delete: false, videosOnly: true, cleanLayout: true, includePreviews: false).run()
            let saved = try MediaImportEngine.files(result.folder, includeHidden: true)
            try check(saved.count == paths.count, "hard camera package dependencies override optional-preview exclusion")
            for path in paths {
                let copies = saved.filter { $0.path.hasSuffix("/" + path) }
                try check(copies.count == 1 && (try Data(contentsOf: copies[0])) == Data(path.utf8), "structured dependency retains its relative location and contents")
            }
        }
        try test("metadata-only reconnect with skipped previews stays connected") { source, media, dest in
            let paths = ["fileinfo_list.list", "preview.LRV", "preview.LRF", "preview.THM", "Proxy/preview.MP4"]
            for path in paths {
                let file = media.appendingPathComponent(path)
                try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
                try Data(path.utf8).write(to: file)
            }
            let first = try engine(source, media, dest, videosOnly: true, cleanLayout: true, includePreviews: false).run()
            let second = try engine(source, media, dest, videosOnly: true, cleanLayout: true, includePreviews: false).run()
            try check(!first.shouldAutoEject && !second.shouldAutoEject, "remaining skipped previews do not turn metadata-only reconnect into an automatic eject")
            let saved = try MediaImportEngine.files(dest, includeHidden: true)
            try check(saved.count == 1 && saved[0].lastPathComponent == "fileinfo_list.list", "only device metadata is backed up")
            for path in paths { try check(try Data(contentsOf: media.appendingPathComponent(path)) == Data(path.utf8), "skipped previews and device index retained") }
        }
        try test("lost saved package companion prevents all opted-in video deletion") { source, _, dest in
            let paths = ["M4ROOT/CLIP/C0001.MP4", "M4ROOT/CLIP/C0001M01.XML", "M4ROOT/MEDIAPRO.XML"]
            for path in paths {
                let file = source.appendingPathComponent(path)
                try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
                try Data(path.utf8).write(to: file)
            }
            var removedSavedCompanion = false
            try fails { _ = try engine(source, source, dest, videosOnly: true, cleanLayout: true, report: { progress in
                if progress.phase == "Imported", progress.completed == progress.total, !removedSavedCompanion {
                    let saved = try! MediaImportEngine.files(dest, includeHidden: true)
                    let companion = saved.first { $0.lastPathComponent == "C0001M01.XML" }!
                    try! FileManager.default.removeItem(at: companion)
                    removedSavedCompanion = true
                }
            }).run() }
            try check(removedSavedCompanion, "fixture removed already-verified package metadata from destination")
            for path in paths { try check(try Data(contentsOf: source.appendingPathComponent(path)) == Data(path.utf8), "incomplete saved package preserves every original") }
        }
        try test("late batch audit failure preserves every original") { source, media, dest in
            for name in ["a.mp4", "z.mp4"] { try content.write(to: media.appendingPathComponent(name)) }
            var verified = 0
            try fails { _ = try engine(source, media, dest, cleanLayout: true, audit: { line in
                if line.hasPrefix("Verified SHA256") {
                    verified += 1
                    if verified == 2 { throw ImportFailure("generated late log failure") }
                }
            }).run() }
            try check(verified == 2, "fixture reached the later file after first copy verification")
            for name in ["a.mp4", "z.mp4"] { try check(try Data(contentsOf: media.appendingPathComponent(name)) == content, "no original deleted before whole-batch verification and audit") }
        }
        try test("photo companion arriving during video import prevents deletion") { source, media, dest in
            let video = media.appendingPathComponent("live.MOV"), photo = media.appendingPathComponent("live.HEIC")
            try content.write(to: video)
            try fails { _ = try engine(source, media, dest, videosOnly: true, cleanLayout: true, report: { progress in
                if progress.phase == "Imported" { try! content.write(to: photo) }
            }).run() }
            try check(try Data(contentsOf: video) == content, "new photo association protects copied movie original")
            try check(try Data(contentsOf: photo) == content, "new photo remains intact")
        }
        for mutateSavedCopy in [false, true] {
            try test("same-size \(mutateSavedCopy ? "saved-copy" : "source") mutation with restored mtime prevents deletion") { source, media, dest in
                for name in ["a.mp4", "z.mp4"] { try content.write(to: media.appendingPathComponent(name)) }
                var mutated = false
                var mutationError: Error?
                try fails { _ = try engine(source, media, dest, cleanLayout: true, report: { progress in
                    guard progress.phase == "Imported", progress.completed == progress.total, !mutated else { return }
                    do {
                        let target: URL
                        if mutateSavedCopy {
                            guard let file = try MediaImportEngine.files(dest, includeHidden: true).first(where: { $0.lastPathComponent == "a.mp4" }) else { throw ImportFailure("Generated saved copy missing") }
                            target = file
                        } else { target = media.appendingPathComponent("a.mp4") }
                        var before = stat()
                        guard lstat(target.path, &before) == 0 else { throw ImportFailure("Cannot stat generated mutation fixture") }
                        let handle = try FileHandle(forWritingTo: target)
                        try handle.write(contentsOf: Data(repeating: 42, count: content.count)); try handle.close()
                        let times = [before.st_atimespec, before.st_mtimespec]
                        let restored = times.withUnsafeBufferPointer { utimensat(AT_FDCWD, target.path, $0.baseAddress, 0) }
                        guard restored == 0 else { throw ImportFailure("Cannot restore generated fixture mtime") }
                        var after = stat()
                        guard lstat(target.path, &after) == 0, after.st_size == before.st_size,
                              after.st_ino == before.st_ino, after.st_mtimespec.tv_sec == before.st_mtimespec.tv_sec,
                              after.st_mtimespec.tv_nsec == before.st_mtimespec.tv_nsec else { throw ImportFailure("Mutation fixture did not preserve size, inode, and mtime") }
                        mutated = true
                    } catch { mutationError = error }
                }).run() }
                try check(mutated && mutationError == nil, "fixture changed bytes while preserving inode, size, and exact mtime")
                for name in ["a.mp4", "z.mp4"] { try check(FileManager.default.fileExists(atPath: media.appendingPathComponent(name).path), "mtime-preserving mutation prevents all source deletion") }
            }
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
            try check(FileManager.default.fileExists(atPath: media.appendingPathComponent("clip.mp4").path), "batch change prevents every original deletion")
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
        #if APP_STORE
        try test("sandbox denies unselected external fixture") { _, _, _ in
            // The runner creates this exact disposable sentinel outside the sandbox first.
            try fails { _ = try Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1])) }
        }
        try test("invalid bookmark fails closed") { _, _, _ in
            try fails { _ = try ScopedFolder(Data("invalid".utf8)) }
        }
        #endif
        print("\(passed) tests passed")
    }
}
