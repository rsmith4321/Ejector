import Foundation
import DiskArbitration

@main struct MountedVolumeImportTest {
    static func main() throws {
        let source = URL(fileURLWithPath: "/Volumes/Easy Eject Test")
        let destination = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Easy Eject Test Imports")
        let sourceID = try ImportVolumes.identity(source)
        let destinationID = try ImportVolumes.identity(destination)
        guard sourceID != destinationID, source.lastPathComponent == "Easy Eject Test",
              FileManager.default.fileExists(atPath: source.appendingPathComponent("DCIM/DJI_001/TEST_IMPORT.MP4").path) else {
            throw ImportFailure("Disposable test volume not available")
        }
        let validate: () throws -> Void = {
            guard try ImportVolumes.identity(source) == sourceID,
                  try ImportVolumes.identity(destination) == destinationID,
                  try ImportVolumes.root(source) == source else { throw ImportFailure("Test volume identity changed") }
        }
        var phases = Set<String>()
        let engine = MediaImportEngine(source: source, mediaFolder: source.appendingPathComponent("DCIM"), destination: destination,
            deleteOriginals: true, recoverTrash: false, cancellation: ImportCancellation(), validate: validate,
            report: { phases.insert($0.phase) }, audit: { print($0) })
        let result = try engine.run()
        guard result.files == 1, phases.isSuperset(of: ["Scanning", "Checking", "Importing", "Verifying", "Imported"]),
              try MediaImportEngine.files(source.appendingPathComponent("DCIM"), includeHidden: false).isEmpty else {
            throw ImportFailure("Test did not complete all expected phases")
        }
        try validate()
        // Same native API as the app; no force and no fallback that hides a busy-disk failure.
        var finished = false
        var ejectError: Error?
        FileManager.default.unmountVolume(at: source, options: [.allPartitionsAndEjectDisk, .withoutUI]) { error in
            ejectError = error; finished = true
        }
        let deadline = Date().addingTimeInterval(30)
        while !finished && Date() < deadline { RunLoop.current.run(until: Date().addingTimeInterval(0.1)) }
        if let ejectError { throw ejectError }
        guard finished, !FileManager.default.fileExists(atPath: source.path) else { throw ImportFailure("Eject not confirmed") }
        print("PASS mounted-volume import: \(result.files) file, \(result.bytes) bytes; all phases observed; source removed after verification; native eject confirmed")
    }
}
