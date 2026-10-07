import Foundation
import AppKit

final class LogManager {
    static let shared = LogManager()
    func log(_ message: String) { print(message) }
}

@main struct DJIO4QuickShareTests {
    static func main() async throws {
        guard CommandLine.arguments.count == 4 else { throw ImportFailure("Pass an O4 Pro original, DJI LUT, and disposable test folder.") }
        let original = URL(fileURLWithPath: CommandLine.arguments[1])
        let lut = URL(fileURLWithPath: CommandLine.arguments[2])
        let folder = URL(fileURLWithPath: CommandLine.arguments[3])
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let tools = EasyShareTools(ffmpeg: URL(fileURLWithPath: "/opt/homebrew/bin/ffmpeg"),
                                   ffprobe: URL(fileURLWithPath: "/opt/homebrew/bin/ffprobe"),
                                   lut: URL(fileURLWithPath: "/nonexistent/luna.cube"), djiLUT: lut)
        let engine = EasyShareEngine(tools: tools, preset: .hd, color: .djiO4DLogM, camera: .djiO4Pro,
                                     cancellation: ImportCancellation(), validate: {}, report: { _ in }, audit: { _ in })
        let identified = try engine.isDJIO4ProMaster(original)
        let officialLUT = try EasyShareTools.validateLUT(lut, color: .djiO4DLogM)
        precondition(identified && !officialLUT.isEmpty)
        let prompt = await MainActor.run { QuickSharePrompt.make(deviceName: "O4 Pro", preset: .hd,
                                                               color: .djiO4DLogM, camera: .djiO4Pro) }
        let correctPrompt = await MainActor.run { prompt.applyLUT.title.contains("DJI O4 Pro") && prompt.choice.color == .djiO4DLogM }
        precondition(correctPrompt)
        let eject = await MainActor.run { ImportEjectPrompt.make(hasMedia: true, deviceName: "O4 Pro", quickShare: true, gyroflowFirst: true) }
        let correctEject = await MainActor.run { eject.buttons.last?.title == "Open in Gyroflow" }
        precondition(correctEject)
        let fake = folder.appendingPathComponent("DJI_20250101000000_0001_D_stabilized.mp4")
        try Data("not a real export".utf8).write(to: fake)
        do {
            _ = try engine.run(originals: [], folder: folder, stabilized: [fake])
            throw ImportFailure("Unpaired export was accepted.")
        } catch let error as ImportFailure {
            precondition(error.message.contains("matching a verified DJI"))
        }
        let fixture = folder.appendingPathComponent("DJI_20250101000000_0001_D.MP4")
        if FileManager.default.fileExists(atPath: fixture.path) {
            try engine.run(tools.ffmpeg, ["-nostdin", "-v", "error", "-i", fixture.path,
                                           "-t", "1", "-map", "0:v:0", "-c:v", "libx264", "-preset", "ultrafast",
                                           "-y", fake.path], timeout: 60)
            let hash = try MediaImportEngine.hash(fixture, cancellation: ImportCancellation(), progress: { _ in })
            let result = try engine.run(originals: [.init(url: fixture, sha256: hash)], folder: folder, stabilized: [fake])
            precondition(result.created + result.reused == 1)
            let output = folder.appendingPathComponent("Sharing Copies/DJI_20250101000000_0001_D_stabilized-sharing-1080p.mp4")
            precondition(FileManager.default.fileExists(atPath: output.path))
            let unchanged = try MediaImportEngine.hash(fixture, cancellation: ImportCancellation(), progress: { _ in }) == hash
            precondition(unchanged)
            print("PASS injected stabilized export produces verified LUT sharing copy and leaves source fixture unchanged")
        }
        print("PASS official O4 LUT, real O4P identity, camera-specific prompt, Gyroflow-first eject action, and unpaired-export refusal")
    }
}
