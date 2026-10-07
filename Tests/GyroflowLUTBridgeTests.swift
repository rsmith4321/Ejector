import Foundation

final class LogManager {
    static let shared = LogManager()
    func log(_ message: String) { print(message) }
}

@main struct GyroflowLUTBridgeTests {
    static func main() throws {
        guard CommandLine.arguments.count == 3 else { throw ImportFailure("Pass a disposable fixture folder and original filename.") }
        let folder = URL(fileURLWithPath: CommandLine.arguments[1])
        let original = folder.appendingPathComponent(CommandLine.arguments[2])
        let stabilized = folder.appendingPathComponent(original.deletingPathExtension().lastPathComponent + "_stabilized.mp4")
        // Intentionally absent LUTs prove marked exports do not require a second LUT.
        let tools = EasyShareTools(ffmpeg: URL(fileURLWithPath: "/opt/homebrew/bin/ffmpeg"),
                                   ffprobe: URL(fileURLWithPath: "/opt/homebrew/bin/ffprobe"),
                                   lut: URL(fileURLWithPath: "/nonexistent/luna.cube"), djiLUT: URL(fileURLWithPath: "/nonexistent/dji.cube"))
        func engine(_ color: EasyShareColor) -> EasyShareEngine {
            EasyShareEngine(tools: tools, preset: .hd, color: color, camera: .djiO4Pro,
                            cancellation: ImportCancellation(), validate: {}, report: { _ in }, audit: { print($0) })
        }
        let runner = engine(.djiO4DLogM)
        func hash(_ file: URL) throws -> String {
            try MediaImportEngine.hash(file, cancellation: ImportCancellation(), progress: { _ in })
        }
        let originalSHA = try hash(original); let stabilizedSHA = try hash(stabilized)
        let input = try runner.probe(stabilized)
        precondition(EasyShareEngine.gyroflowAppliedLUT(input))
        for comment in ["", "Gyroflow export LUT applied: ", "Unrelated text Gyroflow export LUT applied: DJI.cube"] {
            let json: [String: Any] = ["streams": [], "format": ["tags": ["comment": comment]]]
            let probe = try JSONDecoder().decode(EasyShareEngine.Probe.self, from: JSONSerialization.data(withJSONObject: json))
            precondition(!EasyShareEngine.gyroflowAppliedLUT(probe))
        }
        let verified = MediaImportEngine.VerifiedMedia(url: original, sha256: originalSHA)
        let result = try runner.run(originals: [verified], folder: folder, stabilized: [stabilized])
        precondition(result.created + result.reused == 1)
        let output = folder.appendingPathComponent("Sharing Copies/" + stabilized.deletingPathExtension().lastPathComponent + "-sharing-1080p.mp4")
        let receipt = try JSONDecoder().decode(EasyShareEngine.Receipt.self, from: Data(contentsOf: EasyShareEngine.receiptURL(output)))
        precondition(receipt.color == .standard && receipt.lutSHA256 == nil)
        let standard = try engine(.standard).run(originals: [verified], folder: folder, stabilized: [stabilized])
        precondition(standard.reused == 1 && standard.created == 0)
        let originalAfter = try hash(original); let stabilizedAfter = try hash(stabilized)
        precondition(originalAfter == originalSHA && stabilizedAfter == stabilizedSHA)
        print("PASS explicit Gyroflow LUT marker; no second LUT; verified sharing output; receipt reuse matches standard color; both inputs unchanged")
    }
}
