using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace EasyEject {
    [DataContract] internal sealed class QuickShareToolFile {
        [DataMember] internal string Path, Sha256;
        [DataMember] internal long Bytes;
        internal QuickShareToolFile Copy() { return (QuickShareToolFile)MemberwiseClone(); }
        internal void Validate(bool lut, string root) {
            if (Path == null || SafePaths.Normalize(Path) != Path || !ImportHistory.ValidHash(Sha256) || Bytes <= 0 || Bytes > QuickShareConfiguration.MaximumToolBytes)
                throw new SafetyException("Saved Quick Share file selection is invalid. Choose the files again.");
            if (lut) {
                if (Path != QuickShareConfiguration.LutPath(root) || Sha256 != QuickShareMedia.OfficialLutSha || Bytes >= 2000000) throw new SafetyException("Saved Quick Share LUT is not the official local copy.");
            } else if (!System.IO.Path.GetExtension(Path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Select a local FFmpeg/FFprobe executable, not a script.");
        }
    }
    [DataContract] internal sealed class QuickShareToolSettings {
        [DataMember] internal int Version = 1;
        [DataMember] internal QuickShareToolFile Ffmpeg, Ffprobe, Lut;
        internal QuickShareToolSettings Copy() { return new QuickShareToolSettings { Version = Version, Ffmpeg = Ffmpeg == null ? null : Ffmpeg.Copy(), Ffprobe = Ffprobe == null ? null : Ffprobe.Copy(), Lut = Lut == null ? null : Lut.Copy() }; }
        internal void Validate(string root) {
            if (Version != 1) throw new SafetyException("Saved Quick Share setup version is unsupported. Review the configuration.");
            if (Ffmpeg != null) Ffmpeg.Validate(false, root); if (Ffprobe != null) Ffprobe.Validate(false, root); if (Lut != null) Lut.Validate(true, root);
        }
    }
    internal sealed class QuickShareConfigurationSnapshot {
        readonly QuickShareToolSettings value;
        internal readonly bool Exists;
        internal readonly string Hash;
        internal readonly Native.FileInfo Stamp;
        internal QuickShareToolSettings Value { get { return value.Copy(); } }
        internal QuickShareConfigurationSnapshot() { value = new QuickShareToolSettings(); }
        internal QuickShareConfigurationSnapshot(QuickShareToolSettings settings, string hash, Native.FileInfo stamp) { value = settings.Copy(); Hash = hash; Stamp = stamp; Exists = true; }
    }
    internal static class QuickShareConfiguration {
        internal const string FileName = "quick-share-tools.json";
        internal const long MaximumToolBytes = 1073741824;
        internal static string LutPath(string root) { return System.IO.Path.Combine(SafePaths.Normalize(root), "luna-lut-" + QuickShareMedia.OfficialLutSha + ".cube"); }
        internal static QuickShareConfigurationSnapshot Load() { return Load(Profiles.Root); }
        internal static QuickShareConfigurationSnapshot Load(string root) {
            using (SafePaths.LockDirectories(root)) {
                string path = System.IO.Path.Combine(root, FileName);
                if (Directory.Exists(path)) throw new SafetyException("Quick Share setup is a directory instead of a configuration file.");
                if (!File.Exists(path)) return new QuickShareConfigurationSnapshot();
                using (var stream = SafePaths.ReadFile(path)) return Read(stream, root);
            }
        }
        static QuickShareConfigurationSnapshot Read(FileStream stream, string root) {
            var stamp = Native.Info(stream.SafeFileHandle);
            if (stream.Length > 16384 || stamp.Links != 1 || (stamp.Attributes & 0x1000) != 0) throw new SafetyException("Quick Share setup is oversized, offline or hard-linked.");
            string hash = ImportHistory.Hash(stream, CancellationToken.None); stream.Position = 0;
            var value = (QuickShareToolSettings)new DataContractJsonSerializer(typeof(QuickShareToolSettings)).ReadObject(stream);
            if (value == null) throw new SafetyException("Quick Share setup is invalid."); value.Validate(root); return new QuickShareConfigurationSnapshot(value, hash, stamp);
        }
        static void CheckSnapshot(QuickShareConfigurationSnapshot expected, QuickShareConfigurationSnapshot current) {
            if (expected == null || expected.Exists != current.Exists || expected.Exists && (expected.Hash != current.Hash || !VerifiedImporter.SameStamp(expected.Stamp, current.Stamp)))
                throw new SafetyException("Quick Share setup changed while this choice was open. Close it and review the current setup.");
        }
        static QuickShareToolFile Capture(string path, CancellationToken cancel) {
            if (String.IsNullOrWhiteSpace(path)) return null;
            path = SafePaths.Normalize(path);
            if (!System.IO.Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Choose a local FFmpeg/FFprobe executable, not a script.");
            using (SafePaths.LockDirectories(System.IO.Path.GetDirectoryName(path))) using (var stream = SafePaths.ReadFile(path)) {
                if (stream.Length <= 0 || stream.Length > MaximumToolBytes || (Native.Info(stream.SafeFileHandle).Attributes & 0x1000) != 0) throw new SafetyException("Quick Share executable is empty, offline or exceeds 1 GiB.");
                return new QuickShareToolFile { Path = path, Bytes = stream.Length, Sha256 = ImportHistory.Hash(stream, cancel) };
            }
        }
        internal static QuickShareConfigurationSnapshot Save(QuickShareConfigurationSnapshot expected, string ffmpeg, string ffprobe, string lutSource, string root, CancellationToken cancel) {
            root = SafePaths.Normalize(root);
            using (SafePaths.LockDirectories(root)) using (Profiles.WriterGate(root)) {
                cancel.ThrowIfCancellationRequested(); CheckSnapshot(expected, Load(root));
                var value = new QuickShareToolSettings { Ffmpeg = Capture(ffmpeg, cancel), Ffprobe = Capture(ffprobe, cancel) };
                if (!String.IsNullOrWhiteSpace(lutSource)) {
                    byte[] bytes = QuickShareMedia.ReadOfficialLut(SafePaths.Normalize(lutSource), cancel); string target = LutPath(root);
                    // Selecting and saving explicitly authorizes this one official local copy.
                    // Clearing preferences leaves the copy intact; it never deletes the supplied asset.
                    if (File.Exists(target)) QuickShareMedia.ReadOfficialLut(target, cancel);
                    else {
                        using (var output = SafePaths.NewFile(System.IO.Path.Combine(root, ".lut-" + Guid.NewGuid().ToString("N") + ".partial"))) {
                            bool published = false;
                            try { output.Write(bytes, 0, bytes.Length); output.Flush(true); cancel.ThrowIfCancellationRequested(); Native.Rename(output.SafeFileHandle, target); published = true; output.Flush(true); }
                            finally { if (!published) Native.DeleteOnClose(output.SafeFileHandle); }
                        }
                    }
                    value.Lut = new QuickShareToolFile { Path = target, Bytes = bytes.Length, Sha256 = QuickShareMedia.OfficialLutSha };
                }
                value.Validate(root);
                using (var held = new VerifiedQuickShareTools(value, cancel, false, QuickShareColor.Standard)) {
                    cancel.ThrowIfCancellationRequested();
                    Profiles.WriteJson(value, typeof(QuickShareToolSettings), root, FileName);
                    // A late Cancel cannot pretend an already committed preference write did not happen.
                    return Load(root);
                }
            }
        }
        internal static VerifiedQuickShareTools Acquire(QuickShareConfigurationSnapshot expected, string root, QuickShareColor color, CancellationToken cancel) {
            var held = new VerifiedQuickShareTools();
            try {
                held.AddDirectories(root);
                string path = System.IO.Path.Combine(root, FileName);
                if (!File.Exists(path)) throw new SafetyException("Choose FFmpeg and FFprobe in Quick Share tools before exporting.");
                var stream = held.AddFile(path); var current = Read(stream, root); CheckSnapshot(expected, current);
                held.Initialize(current.Value, cancel, true, color); return held;
            } catch { held.Dispose(); throw; }
        }
    }
    // Keeps the chosen config, executable contents and official LUT pinned through manual export.
    internal sealed class VerifiedQuickShareTools : IDisposable {
        readonly List<IDisposable> directories = new List<IDisposable>(); readonly List<FileStream> files = new List<FileStream>();
        internal QuickShareTools Tools { get; private set; }
        internal VerifiedQuickShareTools() {}
        internal VerifiedQuickShareTools(QuickShareToolSettings settings, CancellationToken cancel, bool requireTools, QuickShareColor color) { try { Initialize(settings, cancel, requireTools, color); } catch { Dispose(); throw; } }
        internal void AddDirectories(params string[] paths) { directories.Add(SafePaths.LockDirectories(paths)); }
        internal FileStream AddFile(string path) { var stream = SafePaths.ReadFile(path); files.Add(stream); return stream; }
        internal void Initialize(QuickShareToolSettings settings, CancellationToken cancel, bool requireTools, QuickShareColor color) {
            if (!Enum.IsDefined(typeof(QuickShareColor), color)) throw new SafetyException("Choose a supported Quick Share color.");
            if (requireTools && (settings.Ffmpeg == null || settings.Ffprobe == null)) throw new SafetyException("Choose both FFmpeg and FFprobe in Quick Share tools before exporting.");
            if (requireTools && color == QuickShareColor.LunaILog && settings.Lut == null) throw new SafetyException("Choose the official Luna I-Log LUT in Quick Share tools or leave the LUT off for this export.");
            var records = new[] { settings.Ffmpeg, settings.Ffprobe, color == QuickShareColor.LunaILog || !requireTools ? settings.Lut : null }.Where(x => x != null).ToArray();
            if (records.Length > 0) AddDirectories(records.Select(x => System.IO.Path.GetDirectoryName(x.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            foreach (var record in records) {
                cancel.ThrowIfCancellationRequested(); var stream = AddFile(record.Path);
                if (stream.Length != record.Bytes || stream.Length > QuickShareConfiguration.MaximumToolBytes || (Native.Info(stream.SafeFileHandle).Attributes & 0x1000) != 0 || ImportHistory.Hash(stream, cancel) != record.Sha256)
                    throw new SafetyException("A selected Quick Share executable or LUT changed or is unavailable. Choose it again in Quick Share tools before retrying.");
            }
            if (settings.Ffmpeg != null && settings.Ffprobe != null) Tools = new QuickShareTools(settings.Ffmpeg.Path, settings.Ffprobe.Path, settings.Lut == null ? null : settings.Lut.Path);
        }
        public void Dispose() { foreach (var file in files) file.Dispose(); files.Clear(); foreach (var directory in directories.AsEnumerable().Reverse()) directory.Dispose(); directories.Clear(); }
    }
}
