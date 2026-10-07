import Foundation
import CryptoKit
import Darwin

nonisolated enum EasySharePreset: String, Codable, CaseIterable, Sendable {
    case hd = "1080p"
    case uhd = "4K"
    var longEdge: Int { self == .hd ? 1920 : 3840 }
    var shortEdge: Int { self == .hd ? 1080 : 2160 }
    var bitrate: Int { self == .hd ? 8_000_000 : 24_000_000 }
}

nonisolated enum EasyShareColor: String, Codable, CaseIterable, Sendable {
    case lunaILog = "Luna I-Log → standard color"
    case djiO4DLogM = "DJI O4 Pro D-Log M → standard color"
    case standard = "Standard color (no LUT)"
}

nonisolated enum EasyShareCamera: String, Codable, CaseIterable, Sendable {
    case luna = "Insta360 Luna"
    case djiO4Pro = "DJI O4 Pro"
    var logColor: EasyShareColor { self == .luna ? .lunaILog : .djiO4DLogM }
}

// A personal Website-edition experiment. No executable or manufacturer LUT is bundled.
#if !APP_STORE
nonisolated struct EasyShareTools {
    let ffmpeg: URL
    let ffprobe: URL
    let lut: URL
    let djiLUT: URL?
    init(ffmpeg: URL, ffprobe: URL, lut: URL, djiLUT: URL? = nil) {
        self.ffmpeg = ffmpeg; self.ffprobe = ffprobe; self.lut = lut; self.djiLUT = djiLUT
    }
    static let lutName = "Luna_I-Log_to_Rec709_BT1886_s33_v2.cube"
    static let officialLUTSHA = "e3e5c3ab4ca7c166f3ebb740b45ed8cbce649b0c18dca9d5661754c53b9ee17d"
    static let djiLUTName = "DJI O4 Air Unit Series D-Log M to Rec.709 V1.cube"
    static let djiLUTSHA = "b18162854ab47702068410c33afa98a8cb6eef159fc5a04ce0e65fad0fd8947e"
    static var folder: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Easy Eject/Easy Share", isDirectory: true)
    }
    static func installed() throws -> Self {
        func executable(_ name: String) throws -> URL {
            for base in ["/opt/homebrew/bin", "/usr/local/bin"] {
                let url = URL(fileURLWithPath: base).appendingPathComponent(name)
                if FileManager.default.isExecutableFile(atPath: url.path) { return url }
            }
            throw ImportFailure("Easy Share needs FFmpeg installed on this Mac.")
        }
        return try Self(ffmpeg: executable("ffmpeg"), ffprobe: executable("ffprobe"), lut: folder.appendingPathComponent(lutName),
                        djiLUT: folder.appendingPathComponent(djiLUTName))
    }
    func lutURL(for color: EasyShareColor) -> URL { color == .djiO4DLogM ? (djiLUT ?? Self.folder.appendingPathComponent(Self.djiLUTName)) : lut }
    static func lutSHA(for color: EasyShareColor) -> String? {
        switch color { case .lunaILog: officialLUTSHA; case .djiO4DLogM: djiLUTSHA; case .standard: nil }
    }
    static func validateLUT(_ url: URL, color: EasyShareColor = .lunaILog) throws -> Data {
        guard let expected = lutSHA(for: color) else { throw ImportFailure("Standard color does not need a LUT.") }
        try MediaImportEngine.checkPath(url)
        let stamp = try MediaImportEngine.stamp(url)
        guard stamp.size > 0, stamp.size < 2_000_000 else { throw ImportFailure("Choose the official \(color == .lunaILog ? "Luna" : "DJI O4 Air Unit Series") Rec.709 LUT.") }
        let data = try Data(contentsOf: url)
        guard SHA256.hash(data: data).map({ String(format: "%02x", $0) }).joined() == expected else {
            throw ImportFailure("Choose the official \(color == .lunaILog ? "Luna I-Log" : "DJI O4 Air Unit Series D-Log M") to Rec.709 LUT for this camera.")
        }
        return data
    }
    static func installLUT(from url: URL, color: EasyShareColor = .lunaILog) throws {
        let data = try validateLUT(url, color: color)
        try MediaImportEngine.checkPath(folder)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let target = folder.appendingPathComponent(color == .lunaILog ? lutName : djiLUTName)
        try MediaImportEngine.checkPath(target)
        try data.write(to: target, options: .atomic)
    }
}

/// Reads only the exact originals verified by this import, never scans a destination for candidates.
nonisolated struct EasyShareEngine {
    let tools: EasyShareTools
    let preset: EasySharePreset
    let color: EasyShareColor
    let camera: EasyShareCamera
    let cancellation: ImportCancellation
    let validate: () throws -> Void
    let report: (ImportProgress) -> Void
    let audit: (String) throws -> Void
    static let recipe = "luna-share-v1-h264-30fps-aac160"
    var recipe: String { camera == .luna ? Self.recipe : "dji-o4-share-v2-h264-30fps-aac160" }
    init(tools: EasyShareTools, preset: EasySharePreset, color: EasyShareColor, camera: EasyShareCamera = .luna,
         cancellation: ImportCancellation, validate: @escaping () throws -> Void,
         report: @escaping (ImportProgress) -> Void, audit: @escaping (String) throws -> Void) {
        self.tools = tools; self.preset = preset; self.color = color; self.camera = camera
        self.cancellation = cancellation; self.validate = validate; self.report = report; self.audit = audit
    }

    struct Result { var created = 0; var reused = 0; var skipped = 0; var folder: URL }
    struct Receipt: Codable {
        let recipe: String
        let sourceSHA256: String
        let lutSHA256: String?
        let preset: EasySharePreset
        let color: EasyShareColor
        let outputSHA256: String
    }
    struct Probe: Decodable {
        let streams: [Stream]
        let format: Format
        struct Format: Decodable {
            let duration: String?
            let tags: [String: String]?
        }
        struct Stream: Decodable {
            let codec_type: String?
            let codec_name: String?
            let width: Int?
            let height: Int?
            let r_frame_rate: String?
            let duration: String?
            let color_space: String?
            let color_transfer: String?
            let color_primaries: String?
            let side_data_list: [SideData]?
            struct SideData: Decodable { let rotation: Int?; let side_data_type: String? }
        }
        var video: Stream? { streams.first { $0.codec_type == "video" } }
        var audio: Stream? { streams.first { $0.codec_type == "audio" } }
        var seconds: Double { Double(video?.duration ?? format.duration ?? "") ?? 0 }
    }

    /// An explicit marker written by Gyroflow's export LUT option. Never infer log color from video tags.
    static func gyroflowAppliedLUT(_ input: Probe) -> Bool {
        (input.format.tags?["comment"] ?? "").components(separatedBy: .newlines).contains { line in
            let prefix = "Gyroflow export LUT applied: "
            return line.hasPrefix(prefix) && !line.dropFirst(prefix.count).trimmingCharacters(in: .whitespaces).isEmpty
        }
    }

    func check() throws { try cancellation.check(); try validate() }

    func probe(_ url: URL) throws -> Probe {
        let data = try run(tools.ffprobe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", url.path], timeout: 60)
        return try JSONDecoder().decode(Probe.self, from: data)
    }

    /// Video tags cannot distinguish Luna I-Log from Standard. Color is explicitly chosen in the profile.
    /// Restrict this experiment to independent Luna master MP4s with the embedded camera identity.
    static func isLunaMaster(_ url: URL) throws -> Bool {
        guard url.pathExtension.lowercased() == "mp4", url.lastPathComponent.uppercased().hasPrefix("VID_") else { return false }
        let size = try MediaImportEngine.stamp(url).size
        let handle = try FileHandle(forReadingFrom: url); defer { try? handle.close() }
        try handle.seek(toOffset: UInt64(max(0, size - 65_536)))
        let tail = try handle.readToEnd() ?? Data()
        return tail.range(of: Data("Insta360 Luna Ultra".utf8)) != nil || tail.range(of: Data("Insta360 Luna Pro".utf8)) != nil
    }

    func isDJIO4ProMaster(_ url: URL) throws -> Bool {
        guard url.pathExtension.lowercased() == "mp4",
              url.deletingPathExtension().lastPathComponent.range(of: #"^DJI_\d{14}_\d{4}_D$"#, options: .regularExpression) != nil else { return false }
        return try probe(url).format.tags?["encoder"] == "DJI O4P"
    }

    func run(originals: [MediaImportEngine.VerifiedMedia], folder: URL,
             stabilized: [URL] = []) throws -> Result {
        try check()
        let sharing = folder.appendingPathComponent("Sharing Copies", isDirectory: true)
        var result = Result(folder: sharing)
        guard color == .standard || color == camera.logColor else {
            throw ImportFailure("The selected LUT does not match this device’s camera.")
        }
        let candidates: [MediaImportEngine.VerifiedMedia]
        if camera == .luna {
            candidates = try originals.filter { try Self.isLunaMaster($0.url) }
        } else {
            let masters = try originals.filter { try isDJIO4ProMaster($0.url) }
            candidates = try stabilized.map { export in
                guard export.pathExtension.lowercased() == "mp4",
                      export.deletingPathExtension().lastPathComponent.hasSuffix("_stabilized"),
                      masters.contains(where: { export.deletingPathExtension().lastPathComponent ==
                          $0.url.deletingPathExtension().lastPathComponent + "_stabilized" }),
                      MediaImportEngine.isWithin(export, folder),
                      !MediaImportEngine.isWithin(export, sharing) else {
                    throw ImportFailure("Choose a Gyroflow export matching a verified DJI O4 Pro original in this import.")
                }
                try MediaImportEngine.checkPath(export)
                return .init(url: export, sha256: try MediaImportEngine.hash(export, cancellation: cancellation, progress: { _ in }))
            }
            guard !candidates.isEmpty else { throw ImportFailure("Stabilize the O4 Pro originals in Gyroflow, then choose their _stabilized.mp4 exports.") }
        }
        result.skipped = originals.filter { $0.url.pathExtension.lowercased() == "mp4" }.count - candidates.count
        guard !candidates.isEmpty else { return result }
        var cachedLUTData: Data?
        try MediaImportEngine.checkPath(sharing)
        try FileManager.default.createDirectory(at: sharing, withIntermediateDirectories: true)
        for (index, original) in candidates.enumerated() {
            try check()
            try MediaImportEngine.checkPath(original.url)
            guard MediaImportEngine.isWithin(original.url, folder), !MediaImportEngine.isWithin(original.url, sharing) else {
                throw ImportFailure("Easy Share received a file outside this import’s original copies.")
            }
            let before = try MediaImportEngine.stamp(original.url)
            guard try MediaImportEngine.hash(original.url, cancellation: cancellation, progress: { _ in }) == original.sha256 else {
                throw ImportFailure("A saved original changed before Easy Share. Import it again.")
            }
            let input = try probe(original.url)
            if camera == .djiO4Pro {
                let masterName = original.url.deletingPathExtension().lastPathComponent.replacingOccurrences(of: "_stabilized", with: "") + ".MP4"
                guard let master = originals.first(where: { $0.url.lastPathComponent.caseInsensitiveCompare(masterName) == .orderedSame }),
                      try MediaImportEngine.hash(master.url, cancellation: cancellation, progress: { _ in }) == master.sha256,
                      abs(try probe(master.url).seconds - input.seconds) < 0.5 else {
                    throw ImportFailure("The Gyroflow export or its O4 Pro original changed or does not match.")
                }
            }
            guard let video = input.video, let width = video.width, let height = video.height,
                  width > 0, height > 0, input.seconds.isFinite, input.seconds > 0 else {
                throw ImportFailure("Quick Share could not read the camera video’s duration or dimensions.")
            }
            // HDR and Dolby Vision need a different conversion; never run the I-Log LUT on them.
            guard video.color_transfer != "smpte2084", video.color_transfer != "arib-std-b67",
                  !(video.side_data_list?.contains { $0.side_data_type?.contains("DOVI") == true } ?? false) else {
                throw ImportFailure("Quick Share supports D-Log M, I-Log, and standard SDR clips. This clip is HDR.")
            }
            let alreadyApplied = camera == .djiO4Pro && Self.gyroflowAppliedLUT(input)
            let effectiveColor: EasyShareColor = alreadyApplied ? .standard : color
            if alreadyApplied {
                try audit("Quick Share keeps the LUT already applied by Gyroflow | \(original.url.path)")
            }
            let lutSHA = EasyShareTools.lutSHA(for: effectiveColor)
            let lutData: Data?
            if effectiveColor == .standard {
                lutData = nil
            } else {
                if cachedLUTData == nil { cachedLUTData = try EasyShareTools.validateLUT(tools.lutURL(for: effectiveColor), color: effectiveColor) }
                lutData = cachedLUTData
            }
            let rotation = abs(video.side_data_list?.compactMap(\.rotation).first ?? 0) % 180
            let displayWidth = rotation == 90 ? height : width
            let displayHeight = rotation == 90 ? width : height
            let scale = min(1, min(Double(preset.longEdge) / Double(max(displayWidth, displayHeight)),
                                   Double(preset.shortEdge) / Double(min(displayWidth, displayHeight))))
            let outputWidth = max(2, Int(Double(displayWidth) * scale) / 2 * 2)
            let outputHeight = max(2, Int(Double(displayHeight) * scale) / 2 * 2)
            let rateString = video.r_frame_rate ?? "30/1"
            let rateParts = rateString.split(separator: "/").compactMap { Double($0) }
            let rate = rateParts.count == 2 && rateParts[1] > 0 ? rateParts[0] / rateParts[1] : 30
            let fps = rate.isFinite && rate > 0 && rate <= 30 ? rateString : "30"
            let basename = original.url.deletingPathExtension().lastPathComponent + "-sharing-" + preset.rawValue
            let destination = sharing.appendingPathComponent(basename + ".mp4")
            let output = try chooseOutput(destination, original: original, lutSHA: lutSHA, color: effectiveColor)
            if output.reused {
                result.reused += 1
                try audit("Easy Share reused verified \(preset.rawValue) copy | \(output.url.path)")
                continue
            }
            // The private work folder keeps partial exports and the fixed LUT name out of normal media.
            let work = sharing.appendingPathComponent(".easy-share-\(UUID().uuidString)", isDirectory: true)
            try FileManager.default.createDirectory(at: work, withIntermediateDirectories: false)
            defer { try? FileManager.default.removeItem(at: work) }
            let temporary = work.appendingPathComponent("output.mp4")
            if let lutData { try lutData.write(to: work.appendingPathComponent("camera.cube"), options: .withoutOverwriting) }
            var filters = ["fps=\(fps)", "scale=\(outputWidth):\(outputHeight):flags=lanczos"]
            if lutData != nil { filters += ["format=gbrpf32le", "lut3d=file=camera.cube:interp=tetrahedral"] }
            filters += ["scale=in_range=auto:out_range=tv:out_color_matrix=bt709", "format=yuv420p", "setsar=1",
                        "setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709"]
            let args = ["-nostdin", "-hide_banner", "-v", "error", "-xerror", "-nostats", "-stats_period", "0.5", "-progress", "pipe:1",
                        "-i", original.url.path, "-map", "0:v:0", "-map", "0:a:0?", "-map_metadata", "-1", "-map_chapters", "-1",
                        "-vf", filters.joined(separator: ","), "-c:v", "h264_videotoolbox", "-allow_sw", "1", "-b:v", String(preset.bitrate),
                        "-profile:v", "high", "-pix_fmt", "yuv420p", "-color_range", "tv", "-colorspace", "bt709",
                        "-color_primaries", "bt709", "-color_trc", "bt709", "-metadata:s:v:0", "rotate=0",
                        "-c:a", "aac", "-b:a", "160k", "-ac", "2", "-movflags", "+faststart", "-n", temporary.path]
            report(ImportProgress(phase: "Creating sharing copies", file: original.url.lastPathComponent, completed: index,
                                  total: candidates.count, fraction: Double(index) / Double(candidates.count)))
            _ = try run(tools.ffmpeg, args, directory: work, timeout: max(180, input.seconds * 20 + 60)) { line in
                if line.hasPrefix("out_time_us="), let micros = Double(line.dropFirst(12)) {
                    report(ImportProgress(phase: "Creating sharing copies", file: original.url.lastPathComponent, completed: index,
                                          total: candidates.count, fraction: (Double(index) + min(1, max(0, micros / 1_000_000 / input.seconds))) / Double(candidates.count)))
                }
            }
            try check()
            report(ImportProgress(phase: "Checking sharing copy", file: original.url.lastPathComponent, completed: index, total: candidates.count))
            let exported = try probe(temporary)
            guard exported.video?.codec_name == "h264", exported.video?.width == outputWidth, exported.video?.height == outputHeight,
                  abs(exported.seconds - input.seconds) < 0.25,
                  input.audio == nil || exported.audio?.codec_name == "aac",
                  exported.video?.color_primaries == "bt709", exported.video?.color_transfer == "bt709" else {
                throw ImportFailure("The sharing copy failed verification (expected \(outputWidth)×\(outputHeight), \(input.seconds)s; got \(exported.video?.width ?? 0)×\(exported.video?.height ?? 0), \(exported.seconds)s, \(exported.video?.codec_name ?? "unknown"), color \(exported.video?.color_primaries ?? "unknown")/\(exported.video?.color_transfer ?? "unknown"), audio \(exported.audio?.codec_name ?? "none")).")
            }
            if let inputAudio = input.audio, let audioSeconds = Double(inputAudio.duration ?? ""),
               let exportedAudioSeconds = Double(exported.audio?.duration ?? ""), abs(audioSeconds - exportedAudioSeconds) >= 0.25 {
                throw ImportFailure("The sharing copy’s audio duration differs from the original.")
            }
            // Decode the entire output, including audio, before publishing a finished sharing copy.
            _ = try run(tools.ffmpeg, ["-nostdin", "-v", "error", "-xerror", "-i", temporary.path, "-map", "0:v:0", "-map", "0:a:0?", "-f", "null", "-"],
                        timeout: max(180, input.seconds * 10 + 60))
            guard try MediaImportEngine.stamp(original.url) == before,
                  try MediaImportEngine.hash(original.url, cancellation: cancellation, progress: { _ in }) == original.sha256 else {
                throw ImportFailure("The saved original changed while creating its sharing copy.")
            }
            let outputSHA = try MediaImportEngine.hash(temporary, cancellation: cancellation, progress: { _ in })
            try MediaImportEngine.flushSavedCopy(temporary)
            try check(); try MediaImportEngine.checkPath(output.url)
            // Exclusive rename also supports destinations without hard links (such as exFAT).
            guard renamex_np(temporary.path, output.url.path, UInt32(RENAME_EXCL)) == 0 else { throw ImportFailure("The sharing filename is now occupied or cannot be saved. Existing files were kept.") }
            try MediaImportEngine.flushSavedCopy(output.url)
            let receipt = Receipt(recipe: recipe, sourceSHA256: original.sha256, lutSHA256: lutSHA,
                                  preset: preset, color: effectiveColor, outputSHA256: outputSHA)
            let receiptURL = Self.receiptURL(output.url)
            try MediaImportEngine.checkPath(receiptURL)
            try JSONEncoder().encode(receipt).write(to: receiptURL, options: .withoutOverwriting)
            try MediaImportEngine.flushSavedCopy(receiptURL)
            try audit("Easy Share verified \(preset.rawValue), \(effectiveColor.rawValue), original SHA256 \(original.sha256), output SHA256 \(outputSHA) | \(output.url.path)")
            result.created += 1
        }
        return result
    }

    static func receiptURL(_ output: URL) -> URL {
        output.deletingLastPathComponent().appendingPathComponent("." + output.lastPathComponent + ".easy-share.json")
    }
    private func chooseOutput(_ proposed: URL, original: MediaImportEngine.VerifiedMedia, lutSHA: String?, color: EasyShareColor) throws -> (url: URL, reused: Bool) {
        for index in 0..<100 {
            let suffix = index == 0 ? "" : "-\(original.sha256.prefix(10))-\(index)"
            let url = proposed.deletingLastPathComponent().appendingPathComponent(proposed.deletingPathExtension().lastPathComponent + suffix + ".mp4")
            let receiptURL = Self.receiptURL(url)
            try MediaImportEngine.checkPath(url); try MediaImportEngine.checkPath(receiptURL)
            if !FileManager.default.fileExists(atPath: url.path) && !FileManager.default.fileExists(atPath: receiptURL.path) { return (url, false) }
            if let receiptStamp = try? MediaImportEngine.stamp(receiptURL), receiptStamp.size < 32_000,
               let data = try? Data(contentsOf: receiptURL), let receipt = try? JSONDecoder().decode(Receipt.self, from: data),
               receipt.recipe == recipe, receipt.sourceSHA256 == original.sha256, receipt.lutSHA256 == lutSHA,
               receipt.preset == preset, receipt.color == color, (try? MediaImportEngine.stamp(url)) != nil,
               try MediaImportEngine.hash(url, cancellation: cancellation, progress: { _ in }) == receipt.outputSHA256 {
                return (url, true)
            }
        }
        throw ImportFailure("Too many conflicting sharing copies. Choose another destination.")
    }

    /// Files instead of pipes prevent a full stdout/stderr buffer from blocking cancellation.
    /// Process arguments are passed directly; no shell or user-controlled filter paths are evaluated.
    @discardableResult func run(_ executable: URL, _ arguments: [String], directory: URL? = nil,
                               timeout: Double, progress: (String) -> Void = { _ in }) throws -> Data {
        try check()
        let logs = FileManager.default.temporaryDirectory.appendingPathComponent("easy-share-process-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: logs, withIntermediateDirectories: false)
        defer { try? FileManager.default.removeItem(at: logs) }
        let stdout = logs.appendingPathComponent("stdout"), stderr = logs.appendingPathComponent("stderr")
        FileManager.default.createFile(atPath: stdout.path, contents: nil)
        FileManager.default.createFile(atPath: stderr.path, contents: nil)
        let out = try FileHandle(forWritingTo: stdout), err = try FileHandle(forWritingTo: stderr)
        let read = try FileHandle(forReadingFrom: stdout)
        defer { try? out.close(); try? err.close(); try? read.close() }
        let process = Process(); process.executableURL = executable; process.arguments = arguments
        process.currentDirectoryURL = directory; process.standardInput = FileHandle.nullDevice
        process.standardOutput = out; process.standardError = err
        try process.run()
        let started = Date()
        var pending = ""
        do {
            while process.isRunning {
                try check()
                guard Date().timeIntervalSince(started) < timeout else { throw ImportFailure("Easy Share took too long. The verified original is saved.") }
                guard try MediaImportEngine.stamp(stdout).size < 4_000_000, try MediaImportEngine.stamp(stderr).size < 4_000_000 else {
                    throw ImportFailure("Easy Share stopped after excessive encoder output.")
                }
                pending += String(decoding: try read.readToEnd() ?? Data(), as: UTF8.self)
                while let newline = pending.firstIndex(of: "\n") {
                    progress(String(pending[..<newline])); pending.removeSubrange(...newline)
                }
                Thread.sleep(forTimeInterval: 0.1)
            }
            process.waitUntilExit(); try check()
        } catch {
            if process.isRunning {
                process.terminate()
                let deadline = Date().addingTimeInterval(2)
                while process.isRunning && Date() < deadline { Thread.sleep(forTimeInterval: 0.05) }
                if process.isRunning { kill(process.processIdentifier, SIGKILL) }
            }
            process.waitUntilExit(); throw error
        }
        guard process.terminationStatus == 0 else {
            let detail = String(decoding: try Data(contentsOf: stderr).suffix(2_000), as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
            throw ImportFailure("Easy Share could not finish: " + (detail.isEmpty ? "encoder exited with \(process.terminationStatus)." : detail))
        }
        return try Data(contentsOf: stdout)
    }
}
#endif
