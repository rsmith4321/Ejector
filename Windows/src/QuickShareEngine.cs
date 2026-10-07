using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace EasyEject {
    internal sealed class QuickShareTools {
        internal readonly string Ffmpeg, Ffprobe, Lut;
        internal QuickShareTools(string ffmpeg, string ffprobe, string lut = null) {
            Ffmpeg = Executable(ffmpeg); Ffprobe = Executable(ffprobe); Lut = lut == null ? null : SafePaths.Normalize(lut);
        }
        static string Executable(string path) { path = SafePaths.Normalize(path); if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Choose a local FFmpeg/FFprobe executable; scripts are unsupported."); return path; }
        internal IDisposable Hold() { return new HeldTools(this); }
        sealed class HeldTools : IDisposable {
            IDisposable directories; readonly List<FileStream> files = new List<FileStream>();
            internal HeldTools(QuickShareTools tools) { try { directories = SafePaths.LockDirectories(Path.GetDirectoryName(tools.Ffmpeg), Path.GetDirectoryName(tools.Ffprobe)); foreach (string path in new[] { tools.Ffmpeg, tools.Ffprobe }.Distinct(StringComparer.OrdinalIgnoreCase)) files.Add(SafePaths.ReadFile(path)); } catch { Dispose(); throw; } }
            public void Dispose() { foreach (var file in files) file.Dispose(); if (directories != null) directories.Dispose(); }
        }
    }
    [DataContract] internal sealed class QuickShareReceipt {
        [DataMember] internal string Recipe { get; set; }
        [DataMember] internal string SourceSha256 { get; set; }
        [DataMember] internal string LutSha256 { get; set; }
        [DataMember] internal string OutputSha256 { get; set; }
        [DataMember] internal string Preset { get; set; }
        [DataMember] internal string Color { get; set; }
        [DataMember] internal long Bytes { get; set; }
        [DataMember] internal int Width { get; set; }
        [DataMember] internal int Height { get; set; }
        [DataMember] internal double FrameRate { get; set; }
        [DataMember] internal double Seconds { get; set; }
        internal bool Matches(QuickSharePlan plan, string source) {
            return Recipe == QuickSharePlan.Recipe && SourceSha256 == source && LutSha256 == plan.LutSha && Preset == plan.PresetName && Color == plan.Color.ToString() && Bytes > 0
                && Width == plan.Width && Height == plan.Height && Math.Abs(FrameRate - plan.FrameRate) < 0.000000001 && Math.Abs(Seconds - plan.Seconds) < 0.000000001 && ImportHistory.ValidHash(OutputSha256);
        }
    }
    internal sealed class QuickShareResult {
        internal int Created, Reused, Skipped, PublishedCopies, PublishedReceipts;
        internal string Folder, AuditPath;
        internal readonly List<string> Outputs = new List<string>(), CleanupWarnings = new List<string>();
    }
    internal sealed class QuickShareFailure : Exception {
        internal readonly QuickShareResult Partial;
        internal bool Cancelled { get { return InnerException is OperationCanceledException; } }
        internal QuickShareFailure(Exception cause, QuickShareResult partial) : base(cause.Message + "\nQuick Share completed " + partial.Created + " new copies and reused " + partial.Reused + ". Published files/receipts: " + partial.PublishedCopies + "/" + partial.PublishedReceipts + ". Full-quality originals were not modified by Quick Share. Retry is manual.\nSharing folder: " + partial.Folder + (partial.AuditPath == null ? "" : "\nAudit: " + partial.AuditPath) + (partial.CleanupWarnings.Count == 0 ? "" : "\n" + String.Join("\n", partial.CleanupWarnings)), cause) { Partial = partial; }
    }
    // Caller admission must come from an explicit manual choice for an opted-in profile.
    // This transaction has no source connection/eject API and never scans for other originals.
    internal sealed class QuickShareEngine {
        readonly IQuickShareProcess process; readonly Operations operations; readonly Action<string> auditCheck;
        readonly Func<string, ulong> freeSpace; readonly Action<string, string> checkpoint;
        internal QuickShareEngine(IQuickShareProcess process, Operations operations, Action<string> auditCheck = null, Func<string, ulong> freeSpace = null, Action<string, string> checkpoint = null) { this.process = process; this.operations = operations; this.auditCheck = auditCheck ?? (_ => {}); this.freeSpace = freeSpace ?? Native.FreeSpace; this.checkpoint = checkpoint ?? ((stage, path) => {}); }
        internal QuickShareResult Run(CompletedBatch batch, DestinationIdentity current, IImportGuard guard, QuickShareTools tools, QuickSharePreset preset, QuickShareColor color, CancellationToken cancel, Action<string> report) {
            var result = new QuickShareResult(); report = report ?? (_ => {});
            try {
                if (!Enum.IsDefined(typeof(QuickSharePreset), preset) || !Enum.IsDefined(typeof(QuickShareColor), color)) throw new SafetyException("Choose a supported Quick Share size and color.");
                using (var held = new QuickShareBatch(batch, current, guard, operations, cancel, report)) {
                    result.Folder = SafePaths.Normalize(Path.Combine(held.Batch.Folder, "Sharing Copies"));
                    var candidates = held.Originals.Where(x => QuickShareMedia.IsLunaMaster(x.Record.Path, x.Stream)).ToArray();
                    result.Skipped = held.Originals.Count(x => Path.GetExtension(x.Record.Path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)) - candidates.Length;
                    foreach (var original in candidates) if (SafePaths.Within(original.Record.Path, result.Folder)) throw new SafetyException("A Quick Share original is inside Sharing Copies. Choose the exact original import batch.");
                    if (candidates.Length == 0) return result;
                    if (tools == null) throw new SafetyException("Choose FFmpeg and FFprobe before creating sharing copies. Nothing is installed automatically.");
                    if (color == QuickShareColor.LunaILog && tools.Lut == null) throw new SafetyException("Choose the official Luna Rec.709 s33 v2 LUT before applying I-Log conversion.");
                    byte[] lut = color == QuickShareColor.LunaILog ? QuickShareMedia.ReadOfficialLut(tools.Lut, cancel) : null;
                    using (tools.Hold()) {
                        held.Check();
                        using (var sharingDirectories = EnsureDirectory(result.Folder)) {
                            string audits = Path.Combine(result.Folder, ".easy-share-audits"); sharingDirectories.Validate();
                            using (var auditDirectories = EnsureDirectory(audits)) {
                                string attempt = SafePaths.Normalize(Path.Combine(audits, Guid.NewGuid().ToString("N")));
                                sharingDirectories.Validate(); auditDirectories.Validate();
                                if (!Native.CreateDirectory(attempt, IntPtr.Zero)) throw Native.Error("Cannot create an exclusive Quick Share audit attempt");
                                using (var attemptDirectories = SafePaths.HoldDirectories(attempt)) using (var auditFile = SafePaths.NewFile(Path.Combine(attempt, "export-audit.log"))) using (var audit = new StreamWriter(auditFile, new UTF8Encoding(false), 4096, true)) {
                                    Action check = () => { held.Check(); sharingDirectories.Validate(); auditDirectories.Validate(); attemptDirectories.Validate(); };
                                    check();
                                    result.AuditPath = Path.Combine(attempt, "export-audit.log");
                                    Action<string> log = line => { check(); audit.WriteLine(DateTime.UtcNow.ToString("o") + " " + line); audit.Flush(); auditFile.Flush(true); auditCheck(line); };
                                    log("Manual Quick Share | batch " + held.Batch.Id + " | destination " + held.Batch.Destination.VolumeId + " | " + preset + " | " + color);
                                    try {
                                        foreach (var original in candidates) {
                                            check(); report("Reading Luna video " + Path.GetFileName(original.Record.Path));
                                            var plan = new QuickSharePlan(Probe(tools, original.Record.Path, attempt, check, cancel), preset, color);
                                            using (var choice = Choose(result.Folder, original.Record, plan, held, cancel)) {
                                            check();
                                            if (choice.Reused) { held.VerifyOriginal(original); log("Reused verified sharing copy | source SHA256 " + original.Record.Sha256 + " | " + choice.Path); result.Reused++; result.Outputs.Add(choice.Path); continue; }
                                            double required = Math.Ceiling(plan.Seconds * (plan.Bitrate + (plan.HasAudio ? 160000.0 : 0)) / 8) + 1073741824;
                                            if (!QuickShareProbe.Finite(required) || required >= UInt64.MaxValue || freeSpace(result.Folder) <= required) throw new SafetyException("Quick Share needs estimated export space plus 1 GiB free. Saved originals remain intact.");
                                            var work = new PrivateShareWork(result.Folder); FileStream heldLut = null;
                                            try {
                                                Action checkWork = () => { check(); work.Validate(); };
                                                checkWork();
                                                if (lut != null) { using (var target = SafePaths.NewFile(Path.Combine(work.Path, "luna.cube"))) { target.Write(lut, 0, lut.Length); target.Flush(true); } heldLut = SafePaths.ReadFile(Path.Combine(work.Path, "luna.cube")); }
                                                string temporary = Path.Combine(work.Path, "output.mp4");
                                                log("Before encode | source SHA256 " + original.Record.Sha256 + " | " + original.Record.Path + " -> " + choice.Path);
                                                report("Creating " + plan.PresetName + " sharing copy " + Path.GetFileName(original.Record.Path)); checkWork();
                                                process.Run(tools.Ffmpeg, plan.EncodeArguments(original.Record.Path, temporary), work.Path, plan.EncodeTimeout, cancel, checkWork, line => { if (line.StartsWith("out_time_us=", StringComparison.Ordinal)) { double? micros = QuickShareProbe.Number(line.Substring(12)); if (micros.HasValue) report("Creating sharing copy: " + Math.Round(Math.Min(1, Math.Max(0, micros.Value / 1000000 / plan.Seconds)) * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%"); } });
                                                checkWork(); Native.FileInfo stamp; string outputHash; long outputBytes;
                                                using (var output = SafePaths.ReadFile(temporary)) {
                                                    stamp = Native.Info(output.SafeFileHandle); outputBytes = output.Length;
                                                    if (outputBytes <= 0 || stamp.Links != 1) throw new SafetyException("The sharing output is empty or has another file alias.");
                                                    output.Position = 0; outputHash = ImportHistory.Hash(output, cancel);
                                                    report("Checking sharing copy " + Path.GetFileName(original.Record.Path));
                                                    plan.ValidateOutput(Probe(tools, temporary, work.Path, checkWork, cancel));
                                                    process.Run(tools.Ffmpeg, QuickSharePlan.DecodeArguments(temporary), work.Path, plan.DecodeTimeout, cancel, checkWork);
                                                    output.Position = 0; if (!VerifiedImporter.SameStamp(stamp, Native.Info(output.SafeFileHandle)) || ImportHistory.Hash(output, cancel) != outputHash) throw new SafetyException("The sharing output changed during validation.");
                                                }
                                                held.VerifyOriginal(original); checkpoint("Before publication handle", temporary); checkWork();
                                                using (var output = OpenPublication(temporary)) {
                                                    output.Position = 0; if (!VerifiedImporter.SameStamp(stamp, Native.Info(output.SafeFileHandle)) || output.Length != outputBytes || ImportHistory.Hash(output, cancel) != outputHash) throw new SafetyException("The sharing output was replaced after validation.");
                                                    output.Flush(true);
                                                    var receipt = new QuickShareReceipt { Recipe = QuickSharePlan.Recipe, SourceSha256 = original.Record.Sha256, LutSha256 = plan.LutSha, OutputSha256 = outputHash, Preset = plan.PresetName, Color = plan.Color.ToString(), Bytes = outputBytes, Width = plan.Width, Height = plan.Height, FrameRate = plan.FrameRate, Seconds = plan.Seconds };
                                                    using (var record = SafePaths.NewFile(Path.Combine(work.Path, "receipt.partial"))) {
                                                        bool receiptPublished = false;
                                                        try {
                                                            new DataContractJsonSerializer(typeof(QuickShareReceipt)).WriteObject(record, receipt); record.Flush(true);
                                                            log("Before exclusive publication | source SHA256 " + original.Record.Sha256 + " | output SHA256 " + outputHash + " | " + choice.Path);
                                                            checkpoint("Before output publication", choice.Path); checkWork();
                                                            if (Occupied(ReceiptPath(choice.Path))) throw new SafetyException("The sharing receipt filename became occupied. Existing files are kept.");
                                                            Native.Rename(output.SafeFileHandle, choice.Path); result.PublishedCopies++; output.Flush(true);
                                                            checkpoint("Before receipt publication", ReceiptPath(choice.Path)); checkWork();
                                                            Native.Rename(record.SafeFileHandle, ReceiptPath(choice.Path)); receiptPublished = true; result.PublishedReceipts++; record.Flush(true); result.Created++; result.Outputs.Add(choice.Path);
                                                            log("Verified sharing copy | " + QuickSharePlan.Recipe + " | source SHA256 " + original.Record.Sha256 + " | LUT " + (plan.LutSha ?? "none") + " | output SHA256 " + outputHash + " | " + choice.Path);
                                                        } finally { if (!receiptPublished) Native.DeleteOnClose(record.SafeFileHandle); }
                                                    }
                                                }
                                            } finally { if (heldLut != null) heldLut.Dispose(); string warning = work.Clean(); if (warning != null) result.CleanupWarnings.Add(warning); }
                                            }
                                        }
                                        held.VerifyAll(); log("Completed manual Quick Share | new " + result.Created + " | reused " + result.Reused + " | skipped MP4 " + result.Skipped); check();
                                    } catch (Exception ex) { try { log("Stopped Quick Share | new " + result.Created + " | reused " + result.Reused + " | published files/receipts " + result.PublishedCopies + "/" + result.PublishedReceipts + " | " + ex.Message); } catch (Exception) {} throw; }
                                }
                            }
                        }
                    }
                }
                return result;
            } catch (Exception ex) { throw new QuickShareFailure(ex, result); }
        }
        QuickShareProbe Probe(QuickShareTools tools, string path, string directory, Action validate, CancellationToken cancel) { return QuickShareProbe.Parse(process.Run(tools.Ffprobe, QuickSharePlan.ProbeArguments(path), directory, 60, cancel, validate)); }
        sealed class Choice : IDisposable { internal string Path; internal bool Reused; internal FileStream Copy, Receipt; public void Dispose() { if (Copy != null) Copy.Dispose(); if (Receipt != null) Receipt.Dispose(); } }
        Choice Choose(string folder, VerifiedCopy original, QuickSharePlan plan, QuickShareBatch held, CancellationToken cancel) {
            string name = Path.GetFileNameWithoutExtension(original.Path) + "-sharing-" + plan.PresetName;
            for (int i = 0; i < 100; i++) {
                held.Check(); string path = SafePaths.Normalize(Path.Combine(folder, name + (i == 0 ? "" : "-" + original.Sha256.Substring(0, 10) + "-" + i) + ".mp4")); string record = ReceiptPath(path);
                bool outputExists = Occupied(path), receiptExists = Occupied(record);
                if (!outputExists && !receiptExists) return new Choice { Path = path };
                if (outputExists && receiptExists) { var reuse = TryReuse(path, record, original, plan, cancel); if (reuse != null) return reuse; }
            }
            throw new SafetyException("Too many conflicting sharing copies. Existing files are kept; choose another destination.");
        }
        static Choice TryReuse(string path, string record, VerifiedCopy original, QuickSharePlan plan, CancellationToken cancel) {
            var held = new Choice { Path = path }; bool accepted = false;
            try {
                held.Receipt = SafePaths.ReadFile(record); if (held.Receipt.Length >= 32000) return null;
                var value = JsonDocuments.Read<QuickShareReceipt>(held.Receipt, 31999);
                if (value == null || !value.Matches(plan, original.Sha256)) return null;
                held.Copy = SafePaths.ExistingCopy(path); var stamp = Native.Info(held.Copy.SafeFileHandle);
                if (held.Copy.Length != value.Bytes || ImportHistory.Hash(held.Copy, cancel) != value.OutputSha256 || !VerifiedImporter.SameStamp(stamp, Native.Info(held.Copy.SafeFileHandle))) return null;
                held.Copy.Flush(true); held.Reused = true; accepted = true; return held;
            } catch (Exception ex) { if (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception || ex is SerializationException || ex is SafetyException) return null; throw; }
            finally { if (!accepted) held.Dispose(); }
        }
        internal static string ReceiptPath(string output) { return SafePaths.Normalize(Path.Combine(Path.GetDirectoryName(output), "." + Path.GetFileName(output) + ".easy-share.json")); }
        static bool Occupied(string path) {
            try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new SafetyException("A sharing path is redirected. Existing files are kept."); return true; }
            catch (FileNotFoundException) { return false; } catch (DirectoryNotFoundException) { return false; }
        }
        static SafePaths.DirectoryLease EnsureDirectory(string path) {
            path = SafePaths.Normalize(path);
            using (var parent = SafePaths.HoldDirectories(Path.GetDirectoryName(path))) {
                parent.Validate(); Directory.CreateDirectory(path); parent.Validate();
                var result = SafePaths.HoldDirectories(path); try { result.Validate(); return result; } catch { result.Dispose(); throw; }
            }
        }
        static FileStream OpenPublication(string path) {
            var handle = Native.CreateFile(path, 0xC0010000, 0, IntPtr.Zero, 3, 0x80200000, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); throw Native.Error("Cannot exclusively hold validated sharing output"); }
            try { var info = Native.Info(handle); if ((info.Attributes & 0x1015) != 0 || info.Links != 1 || !Native.FinalPath(handle).Equals(path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Sharing output is protected, redirected or has another alias. No protection is changed."); return new FileStream(handle, FileAccess.ReadWrite, 1048576, false); } catch { handle.Dispose(); throw; }
        }
        sealed class PrivateShareWork {
            internal readonly string Path; readonly Native.FileInfo identity; SafePaths.DirectoryLease directories;
            internal PrivateShareWork(string parent) {
                Path = SafePaths.Normalize(System.IO.Path.Combine(parent, ".easy-share-" + Guid.NewGuid().ToString("N")));
                if (!Native.CreateDirectory(Path, IntPtr.Zero)) throw Native.Error("Cannot create an exclusive private sharing folder");
                try {
                    using (var handle = Native.CreateFile(Path, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero)) {
                        if (handle.IsInvalid) throw Native.Error("Cannot identify private sharing folder"); identity = Native.Info(handle);
                        if ((identity.Attributes & 0x10) == 0 || !Native.FinalPath(handle).Equals(Path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Private sharing folder resolves elsewhere.");
                        directories = SafePaths.HoldDirectories(Path); directories.Validate();
                        if (!Native.FinalPath(handle).Equals(Path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Private sharing folder moved while acquiring its directory proof.");
                    }
                } catch (Exception ex) { if (directories != null) directories.Dispose(); throw new SafetyException("The exclusive private sharing folder could not be guarded and was retained at " + Path + ". " + ex.Message); }
            }
            internal void Validate() { if (directories == null) throw new SafetyException("Private sharing work is no longer held."); directories.Validate(); }
            internal string Clean() {
                string error = null;
                try { Validate(); } catch (Exception ex) { if (directories != null) { directories.Dispose(); directories = null; } return "Private sharing work retained at " + Path + ": " + ex.Message; }
                foreach (string name in new[] { "output.mp4", "luna.cube", "receipt.partial" }) {
                    string file = System.IO.Path.Combine(Path, name);
                    try { Validate(); if (!Occupied(file)) continue; using (var held = SafePaths.ProtectOriginal(file, true)) { Validate(); Native.DeleteOnClose(held.SafeFileHandle); } } catch (Exception ex) { error = ex.Message; break; }
                }
                if (directories != null) { directories.Dispose(); directories = null; }
                try {
                    using (var handle = Native.CreateFile(Path, 0x10080, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero)) {
                        if (handle.IsInvalid) throw Native.Error("Cannot remove private sharing directory"); var current = Native.Info(handle);
                        if (current.VolumeSerial != identity.VolumeSerial || current.IndexHigh != identity.IndexHigh || current.IndexLow != identity.IndexLow || !Native.FinalPath(handle).Equals(Path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Private sharing directory identity changed.");
                        Native.DeleteOnClose(handle, "Private sharing directory is not empty or cannot be removed");
                    }
                } catch (Exception ex) { error = ex.Message; }
                return error == null ? null : "Private sharing work retained at " + Path + ": " + error;
            }
        }
    }
}
