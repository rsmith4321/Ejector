import Foundation
import CryptoKit
import Darwin

@main struct EasyShareTests {
    static func main() throws {
        setbuf(stdout, nil)
        guard CommandLine.arguments.count == 4 else { throw ImportFailure("Pass a Luna sample MP4, the official s33 LUT, and an evidence folder.") }
        let sample = URL(fileURLWithPath: CommandLine.arguments[1])
        let lut = URL(fileURLWithPath: CommandLine.arguments[2])
        let root = URL(fileURLWithPath: CommandLine.arguments[3])
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let tools = EasyShareTools(ffmpeg: URL(fileURLWithPath: "/opt/homebrew/bin/ffmpeg"),
                                   ffprobe: URL(fileURLWithPath: "/opt/homebrew/bin/ffprobe"), lut: lut)
        var count = 0
        func check(_ condition: Bool, _ message: String) throws { guard condition else { throw ImportFailure("TEST FAILED: " + message) }; count += 1; print("PASS " + message) }
        func fails(_ message: String, _ body: () throws -> Void) throws {
            var failed = false
            do { try body() } catch { failed = true }
            try check(failed, message)
        }
        func engine(_ preset: EasySharePreset = .hd, color: EasyShareColor = .lunaILog, token: ImportCancellation = ImportCancellation(),
                    report: @escaping (ImportProgress) -> Void = { _ in }) -> EasyShareEngine {
            EasyShareEngine(tools: tools, preset: preset, color: color, cancellation: token, validate: {}, report: report, audit: { print($0) })
        }
        func verified(_ url: URL) throws -> MediaImportEngine.VerifiedMedia {
            .init(url: url, sha256: try MediaImportEngine.hash(url, cancellation: ImportCancellation(), progress: { _ in }))
        }
        let runner = engine()
        let fixture = root.appendingPathComponent("VID_fixture.mp4")
        // A short remuxed section retains original encoded frames/audio. Append camera identity
        // because FFmpeg's remux drops Insta360's proprietary trailer. This is an injected fixture.
        try runner.run(tools.ffmpeg, ["-nostdin", "-v", "error", "-i", sample.path, "-t", "5", "-map", "0:v:0", "-map", "0:a:0?", "-c", "copy", "-y", fixture.path], timeout: 60)
        let handle = try FileHandle(forWritingTo: fixture); try handle.seekToEnd(); try handle.write(contentsOf: Data("Insta360 Luna Ultra".utf8)); try handle.close()
        let original = try verified(fixture)
        try check(try EasyShareEngine.isLunaMaster(sample), "actual Luna original identified from embedded camera identity")
        let unrelated = root.appendingPathComponent("other.mp4"); try FileManager.default.copyItem(at: fixture, to: unrelated)
        let unrelatedOriginal = try verified(unrelated)
        let results1080 = try runner.run(originals: [original, unrelatedOriginal], folder: root)
        try check(results1080.created == 1 && results1080.skipped == 1, "1080p export reads only selected Luna master files")
        let output1080 = root.appendingPathComponent("Sharing Copies/VID_fixture-sharing-1080p.mp4")
        let probe1080 = try runner.probe(output1080)
        try check(probe1080.video?.width == 1920 && probe1080.video?.height == 1080 && probe1080.audio?.codec_name == "aac", "1080p H.264 export preserves audio and passes full decode")
        try check(try verified(fixture).sha256 == original.sha256, "source fixture remains byte-for-byte unchanged after LUT export")
        let results4k = try engine(.uhd).run(originals: [original], folder: root)
        let output4k = root.appendingPathComponent("Sharing Copies/VID_fixture-sharing-4K.mp4")
        let probe4k = try runner.probe(output4k)
        try check(results4k.created == 1 && probe4k.video?.width == 3840 && probe4k.video?.height == 2160, "4K option exports separate verified sharing copy")
        try check(try MediaImportEngine.stamp(output1080).size < MediaImportEngine.stamp(fixture).size && MediaImportEngine.stamp(output4k).size < MediaImportEngine.stamp(fixture).size, "both compressed copies are smaller than this source")
        let reused = try runner.run(originals: [original], folder: root)
        try check(reused.reused == 1 && reused.created == 0, "repeat import reuses matching hash-verified receipt")
        let formerHash = try verified(output1080).sha256
        let standard = try engine(color: .standard).run(originals: [original], folder: root)
        try check(standard.created == 1 && verified(output1080).sha256 == formerHash, "changing color recipe preserves prior sharing copy and chooses new name")
        let badHash = MediaImportEngine.VerifiedMedia(url: fixture, sha256: String(repeating: "0", count: 64))
        try fails("changed saved original rejected before processing") { _ = try runner.run(originals: [badHash], folder: root) }
        let outside = MediaImportEngine.VerifiedMedia(url: sample, sha256: String(repeating: "0", count: 64))
        try fails("original outside this batch’s destination rejected") { _ = try runner.run(originals: [outside], folder: root) }
        let cancelRoot = root.appendingPathComponent("cancel"); try FileManager.default.createDirectory(at: cancelRoot, withIntermediateDirectories: true)
        let cancelFile = cancelRoot.appendingPathComponent("VID_cancel.mp4"); try FileManager.default.copyItem(at: fixture, to: cancelFile)
        let cancelOriginal = try verified(cancelFile)
        let token = ImportCancellation()
        try fails("cancellation during encoder startup stops safely") {
            _ = try engine(token: token, report: { if $0.phase == "Creating sharing copies" && $0.fraction > 0 { token.cancel() } }).run(originals: [cancelOriginal], folder: cancelRoot)
        }
        let cancelFiles = try MediaImportEngine.files(cancelRoot, includeHidden: true)
        try check(cancelFiles == [cancelFile] && verified(cancelFile).sha256 == cancelOriginal.sha256, "cancelled export clears partial work and retains original")
        // A silent portrait fixture verifies 60→30 fps conversion, orientation and no enlargement.
        let portrait = root.appendingPathComponent("VID_portrait.mp4")
        try runner.run(tools.ffmpeg, ["-nostdin", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=720x1280:rate=60", "-t", "1", "-c:v", "libx264", "-preset", "ultrafast", "-y", portrait.path], timeout: 60)
        let portraitHandle = try FileHandle(forWritingTo: portrait); try portraitHandle.seekToEnd(); try portraitHandle.write(contentsOf: Data("Insta360 Luna Ultra".utf8)); try portraitHandle.close()
        _ = try engine(color: .standard).run(originals: [try verified(portrait)], folder: root)
        let portraitProbe = try runner.probe(root.appendingPathComponent("Sharing Copies/VID_portrait-sharing-1080p.mp4"))
        try check(portraitProbe.video?.width == 720 && portraitProbe.video?.height == 1280 && portraitProbe.video?.r_frame_rate == "30/1" && portraitProbe.audio == nil, "portrait silent clip preserves framing without upscaling and caps 60 fps at 30")
        let badLUT = root.appendingPathComponent("wrong.cube"); try Data("LUT_3D_SIZE 2".utf8).write(to: badLUT)
        try fails("unrecognized LUT refused") { _ = try EasyShareTools.validateLUT(badLUT) }
        let symlinkRoot = root.appendingPathComponent("symlink"); try FileManager.default.createDirectory(at: symlinkRoot, withIntermediateDirectories: true)
        try FileManager.default.createSymbolicLink(at: symlinkRoot.appendingPathComponent("Sharing Copies"), withDestinationURL: results1080.folder)
        let linkInput = symlinkRoot.appendingPathComponent("VID_link.mp4"); try FileManager.default.copyItem(at: fixture, to: linkInput)
        try fails("sharing destination symbolic link rejected") { _ = try runner.run(originals: [try verified(linkInput)], folder: symlinkRoot) }
        let receipt = EasyShareEngine.receiptURL(output1080)
        try Data("damaged receipt".utf8).write(to: receipt)
        _ = try runner.run(originals: [original], folder: root)
        try check(try verified(output1080).sha256 == formerHash, "unverifiable existing export retained instead of overwritten")
        print("\(count) Easy Share checks passed")
    }
}
