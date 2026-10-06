using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;

namespace EasyEject {
    public interface IImportGuard { int[] ProtectedDisks { get; } void Validate(); }
    internal interface IImportDeviceEvidence { DestinationIdentity SourceDevice { get; } DestinationIdentity DestinationDevice { get; } }
    public sealed class VolumeImportGuard : IImportGuard, IConnectionGuard, IImportDeviceEvidence, IConcurrentStorageGuard {
        readonly IDiscovery discovery; readonly string sourceId, destinationId; readonly Func<string, string> volumeId;
        readonly StorageConnectionProof connection;
        readonly string source, destination; readonly int[] disks;
        readonly DestinationIdentity destinationIdentity;
        readonly DestinationIdentity sourceIdentity;
        internal readonly ImportCompletionOrigin CompletionOrigin;
        public DestinationIdentity DestinationIdentity { get { return destinationIdentity.Copy(); } }
        DestinationIdentity IImportDeviceEvidence.SourceDevice { get { return sourceIdentity.Copy(); } }
        DestinationIdentity IImportDeviceEvidence.DestinationDevice { get { return destinationIdentity.Copy(); } }
        public int[] ProtectedDisks { get { return (int[])disks.Clone(); } }
        public bool Changed { get { return connection.Changed; } }
        bool IConcurrentStorageGuard.AllowsUnrelatedOperations { get { return connection.Scoped && !connection.ProtectedConnectionChanged; } }
        public VolumeImportGuard(IDiscovery discovery, string source, string destination, Volume expectedVolume, Disk expectedDisk, long selectionEpoch)
            : this(discovery, source, destination, expectedVolume, expectedDisk, selectionEpoch, WindowsDiscovery.VolumeId) {}
        internal VolumeImportGuard(IDiscovery discovery, string source, string destination, Volume expectedVolume, Disk expectedDisk, long selectionEpoch, Func<string, string> volumeId, Func<Inventory, bool> selectionCurrent = null) {
            this.discovery = discovery; this.source = source; this.destination = destination; this.volumeId = volumeId;
            sourceId = volumeId(source); destinationId = volumeId(destination);
            if (sourceId != expectedVolume.Id) throw new SafetyException("The selected source belongs to a different volume.");
            var state = discovery.Scan(); long epoch = state.Epoch;
            var currentSource = Resolve(state, sourceId);
            if (!(selectionCurrent == null ? epoch == selectionEpoch : selectionCurrent(state)) || currentSource.Fingerprint != expectedVolume.Fingerprint || state.Disks.Single(d => d.Number == currentSource.Disks[0]).Fingerprint != expectedDisk.Fingerprint)
                throw new SafetyException("The selected card connection changed while choosing folders. Refresh and choose the device again.");
            disks = PlanProtection(state, sourceId, destinationId); connection = new StorageConnectionProof(discovery, state, disks); destinationIdentity = DestinationIdentity.Capture(state, destinationId); sourceIdentity = DestinationIdentity.Capture(state, sourceId);
            string root = currentSource.Mounts.Where(x => SafePaths.Within(source, SafePaths.Normalize(x))).OrderByDescending(x => x.Length).FirstOrDefault();
            if (root == null) throw new SafetyException("The selected import source root is unavailable.");
            CompletionOrigin = new ImportCompletionOrigin(state, sourceId, root, discovery);
        }
        public static int[] PlanProtection(Inventory state, string sourceId, string destinationId) {
            var s = Resolve(state, sourceId); var d = Resolve(state, destinationId);
            if (s.System) throw new SafetyException("The Windows system volume cannot be an import source.");
            if (s.Disks.Intersect(d.Disks).Any()) throw new SafetyException("Source and destination must be on different physical disks.");
            var sd = state.Disks.Single(x => x.Number == s.Disks[0]); var dd = state.Disks.Single(x => x.Number == d.Disks[0]);
            if (!(sd.Removable || sd.Hotplug || sd.TargetId != "")) throw new SafetyException("This source is not identified as external or removable storage.");
            if (sd.TargetId != "" && sd.TargetId == dd.TargetId) throw new SafetyException("Source and destination share a removable device.");
            return Expand(state, s.Disks.Concat(d.Disks));
        }
        static Volume Resolve(Inventory state, string id) {
            var v = state.Volumes.SingleOrDefault(x => x.Id == id);
            if (!state.Complete || v == null || v.Error != "" || v.Disks.Count != 1 || v.Serial == "" || state.Disks.Single(x => x.Number == v.Disks[0]).Error != "") throw new SafetyException("A stable source/destination physical disk identity is unavailable.");
            return v;
        }
        static int[] Expand(Inventory state, IEnumerable<int> numbers) {
            var chosen = state.Disks.Where(x => numbers.Contains(x.Number)).ToArray();
            return state.Disks.Where(x => chosen.Any(d => d.Number == x.Number || (d.TargetId != "" && x.Ancestors.Contains(d.TargetId)))).Select(x => x.Number).Distinct().ToArray();
        }
        public void Validate() {
            var state = discovery.Scan();
            Resolve(state, sourceId); Resolve(state, destinationId);
            if (volumeId(source) != sourceId || volumeId(destination) != destinationId) throw new SafetyException("Import device identities changed. Remaining originals are kept.");
            connection.Validate(state);
        }
    }
    [DataContract] public sealed class ReceiptEntry {
        [DataMember] public string RelativePath; [DataMember] public string Sha256; [DataMember] public long Bytes;
        [DataMember] public string SourceRelativePath; [DataMember] public bool IsMedia, Reused;
        [DataMember] public bool RemovedSource;
        [DataMember] public bool FromTrash, IsTrashRecord;
        [DataMember] public bool SourceAbsentAtResume;
    }
    [DataContract] public sealed class ImportReceipt {
        [DataMember] public string StartedUtc; [DataMember] public string SourceVolume; [DataMember] public bool OriginalsKept = true;
        [DataMember] public bool HasSelectedMedia;
        [DataMember] public string Id; [DataMember] public bool VideosOnly, IncludePreviews, CleanLayout;
        [DataMember] public bool OriginalRemovalRequested;
        [DataMember] public bool TrashRecoveryRequested;
        [DataMember] public string ResumedUtc, RemovalIntentSha256, ResumeAuditPath;
        [DataMember] public List<ReceiptEntry> Files = new List<ReceiptEntry>();
    }
    public sealed class ImportResult { public string Folder, ReceiptPath; public int Files, Reused, RemovedOriginals, RecoveredTrashFiles, ClearedTrashFiles, ClearedTrashRecords, PriorAbsentOriginals, PriorAbsentTrashFiles, PriorAbsentTrashRecords; public long Bytes; public bool HasSelectedMedia; public ImportReceipt Receipt; }
    public sealed class VerifiedImporter {
        readonly Operations operations; readonly Func<string, ulong> capacity; readonly Action<string> auditCheck;
        readonly Func<string, string, CancellationToken, Action, DeviceTrashSnapshot> trashCatalog;
        public VerifiedImporter(Operations operations) : this(operations, Native.FreeSpace, x => {}) {}
        internal VerifiedImporter(Operations operations, Func<string, ulong> capacity, Action<string> auditCheck, Func<string, string, CancellationToken, Action, DeviceTrashSnapshot> trashCatalog = null) { this.operations = operations; this.capacity = capacity; this.auditCheck = auditCheck; this.trashCatalog = trashCatalog ?? DeviceTrash.Read; }
        public ImportResult Run(string sourceRoot, string selectedFolder, string destination, string volumeId, IImportGuard guard, CancellationToken cancel, Action<string> report) {
            return Run(sourceRoot, selectedFolder, destination, volumeId, guard, cancel, report, new MediaOptions(false, true));
        }
        public ImportResult Run(string sourceRoot, string selectedFolder, string destination, string volumeId, IImportGuard guard, CancellationToken cancel, Action<string> report, MediaOptions options, Action<ImportWorkProgress> progress = null) {
            sourceRoot = SafePaths.Normalize(sourceRoot); selectedFolder = SafePaths.Normalize(selectedFolder); destination = SafePaths.Normalize(destination);
            if (!SafePaths.Within(selectedFolder, sourceRoot) || SafePaths.Within(destination, sourceRoot) || SafePaths.Within(sourceRoot, destination)) throw new SafetyException("Source and destination folders overlap or the selection is outside the source volume.");
            if (new[] { "STREAM", "CLIP" }.Contains(Path.GetFileName(selectedFolder).ToUpperInvariant())) throw new SafetyException("Select the complete camera package or card, including audio and metadata, rather than a partial STREAM/CLIP folder.");
            using (operations.Reserve(guard.ProtectedDisks))
            using (SafePaths.LockDirectories(sourceRoot, destination))
            using (var mediaFolder = new ImportMediaFolder(sourceRoot, selectedFolder)) {
                cancel.ThrowIfCancellationRequested(); guard.Validate();
                var work = new ImportWorkReporter(progress); work.Emit("Scanning", 0);
                var plan = new ImportSourcePlan(sourceRoot, selectedFolder, options, cancel, guard, trashCatalog, mediaFolder);
                var manifest = plan.Files;
                if (manifest.Count == 0) { cancel.ThrowIfCancellationRequested(); guard.Validate(); plan.CheckManifest(file => false, cancel, guard.Validate); work.Emit("No matching media", 1); return new ImportResult { Folder = destination, Files = 0 }; }
                var stamps = new Dictionary<string, Native.FileInfo>(); var hashes = new Dictionary<string, string>();
                int checkedFiles = 0;
                foreach (string file in manifest) {
                    cancel.ThrowIfCancellationRequested(); guard.Validate(); report("Checking " + plan.Label(file));
                    using (SafePaths.LockDirectories(Path.GetDirectoryName(file))) using (var input = SafePaths.ReadFile(file)) {
                        stamps[file] = Native.Info(input.SafeFileHandle); hashes[file] = Hash(input, cancel, fraction => work.Emit("Checking originals", 0.20 * (checkedFiles + fraction) / manifest.Count));
                        if (!SameStamp(stamps[file], Native.Info(input.SafeFileHandle))) throw new SafetyException("A source changed while planning import. Originals are kept.");
                    }
                    checkedFiles++;
                }
                plan.CheckCapturedSources(stamps, hashes);
                double totalBytes = stamps.Values.Sum(info => (double)info.SizeHigh * 4294967296D + info.SizeLow);
                work.Emit("Preparing import", 0.20);
                string batch = Path.Combine(destination, DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                if (Directory.Exists(batch)) { using (SafePaths.LockDirectories(batch)) {} }
                var targets = plan.Targets(batch, volumeId, hashes, (file, target) => {
                    using (SafePaths.LockDirectories(ExistingParent(target, destination))) {
                        if (Directory.Exists(target)) throw new SafetyException("A destination filename is occupied by a directory. Originals are kept.");
                        if (!File.Exists(target)) return false;
                        using (var saved = SafePaths.ReadFile(target)) return Hash(saved, cancel) != hashes[file];
                    }
                });
                EnsureDirectories(batch, destination);
                using (SafePaths.LockDirectories(batch)) {
                    var receipt = new ImportReceipt { Id = Guid.NewGuid().ToString("N"), StartedUtc = DateTime.UtcNow.ToString("o"), SourceVolume = volumeId, HasSelectedMedia = plan.HasMedia, VideosOnly = options.VideosOnly, IncludePreviews = options.IncludePreviews, CleanLayout = options.CleanLayout, OriginalRemovalRequested = options.DeleteOriginals, TrashRecoveryRequested = options.RecoverTrash };
                    var originals = new Dictionary<string, string>();
                    long bytes = 0; int reused = 0;
                    string receipts = Path.Combine(batch, ".easy-eject-receipts"); EnsureDirectories(receipts, batch);
                    using (SafePaths.LockDirectories(receipts)) {
                    string record = Path.Combine(receipts, receipt.Id);
                    if (!Native.CreateDirectory(record, IntPtr.Zero)) throw Native.Error("Cannot create an exclusive import record");
                    using (SafePaths.LockDirectories(record))
                    using (var auditStream = SafePaths.NewFile(Path.Combine(record, "import-audit.log")))
                    using (var audit = new StreamWriter(auditStream, new System.Text.UTF8Encoding(false), 4096, true)) {
                    Action<string> log = line => { audit.WriteLine(DateTime.UtcNow.ToString("o") + " " + line); audit.Flush(); auditStream.Flush(true); auditCheck(line); };
                    log("Started import | source " + volumeId + " | " + selectedFolder + " -> " + batch);
                    try {
                    foreach (string file in manifest) {
                        cancel.ThrowIfCancellationRequested(); guard.Validate();
                        string relative = plan.Label(file), target = targets[file];
                        originals[SafePaths.Relative(target, batch)] = file;
                        SafePaths.Normalize(target); if (!SafePaths.Within(target, batch)) throw new SafetyException("Destination path escaped batch folder.");
                        EnsureDirectories(Path.GetDirectoryName(target), batch);
                        using (SafePaths.LockDirectories(Path.GetDirectoryName(file), Path.GetDirectoryName(target)))
                        using (var input = SafePaths.ReadFile(file)) {
                            Action<string, double> measured = (phase, fraction) => work.Emit(phase, 0.20 + 0.79 * (totalBytes == 0 ? (receipt.Files.Count + fraction) / manifest.Count : (bytes + input.Length * fraction) / totalBytes));
                            if (!SameStamp(Native.Info(input.SafeFileHandle), stamps[file])) throw new SafetyException("A source file changed after scanning. Originals are kept.");
                            if (File.Exists(target)) {
                                report("Reusing verified " + relative);
                                using (var saved = SafePaths.ExistingCopy(target)) {
                                    if (saved.Length != input.Length || Hash(saved, cancel, fraction => measured("Checking saved copy", fraction * 0.50)) != hashes[file] || Hash(input, cancel, fraction => measured("Checking originals", 0.50 + fraction * 0.50)) != hashes[file]) throw new SafetyException("Destination changed during import. Existing copies and originals are kept; import again.");
                                    saved.Flush(true);
                                    receipt.Files.Add(plan.Entry(file, SafePaths.Relative(target, batch), hashes[file], input.Length, true)); bytes += input.Length; reused++;
                                }
                                measured("Verified copies", 0); log("Reused verified SHA256 " + hashes[file] + " | " + file + " -> " + target); continue;
                            }
                            if (capacity(destination) <= (ulong)input.Length + 1073741824UL) throw new SafetyException("Not enough free space in the destination (file plus 1 GiB reserve required). Originals are kept.");
                            string pending = Path.Combine(Path.GetDirectoryName(target), ".easyeject-" + Guid.NewGuid().ToString("N") + ".partial");
                            bool published = false;
                            using (var output = SafePaths.NewFile(pending)) {
                                try {
                                    report("Copying " + relative); var buffer = new byte[1048576]; long count = 0; int n;
                                    using (var sourceHash = SHA256.Create()) {
                                        while ((n = input.Read(buffer, 0, buffer.Length)) != 0) {
                                            cancel.ThrowIfCancellationRequested(); if (discoveryChanged(guard)) guard.Validate();
                                            output.Write(buffer, 0, n); sourceHash.TransformBlock(buffer, 0, n, null, 0); count += n; measured("Copying", input.Length == 0 ? 0.50 : 0.50 * count / input.Length);
                                        }
                                        sourceHash.TransformFinalBlock(new byte[0], 0, 0);
                                        output.Flush(true); output.Position = 0; report("Verifying " + relative);
                                        string copyHash = Hash(output, cancel, fraction => measured("Verifying copy", 0.50 + 0.15 * fraction)), originalHash = Hex(sourceHash.Hash);
                                        // Reread the held, write/delete-denying source handle, not a path that can be swapped.
                                        input.Position = 0;
                                        if (copyHash != originalHash || originalHash != hashes[file] || Hash(input, cancel, fraction => measured("Checking originals", 0.65 + 0.20 * fraction)) != originalHash || input.Length != count) throw new SafetyException("SHA-256 verification failed. Originals are kept.");
                                        cancel.ThrowIfCancellationRequested(); guard.Validate();
                                        Native.Rename(output.SafeFileHandle, target); published = true; Native.PreserveModified(output.SafeFileHandle, stamps[file]); output.Flush(true);
                                        output.Position = 0;
                                        string publishedPath = Native.FinalPath(output.SafeFileHandle), publishedHash = Hash(output, cancel, fraction => measured("Checking published copy", 0.85 + 0.15 * fraction));
                                        if (!publishedPath.Equals(target, StringComparison.OrdinalIgnoreCase) || publishedHash != originalHash) throw new SafetyException("Published copy verification failed. Originals are kept. Expected path: " + target + "; resolved path: " + publishedPath + "; hash match: " + (publishedHash == originalHash));
                                        receipt.Files.Add(plan.Entry(file, SafePaths.Relative(target, batch), originalHash, count, false)); bytes += count;
                                    }
                                } finally {
                                    // Cleanup addresses only the held private file; a path substitution cannot delete another file.
                                    if (!published) Native.DeleteOnClose(output.SafeFileHandle);
                                }
                            }
                            measured("Verified copies", 0); log("Copied verified SHA256 " + hashes[file] + " | " + file + " -> " + target);
                        }
                    }
                    work.Emit("Final batch verification", 0.99); guard.Validate(); cancel.ThrowIfCancellationRequested();
                    plan.CheckManifest(x => false, cancel, guard.Validate);
                    // Recheck and hold every source and saved dependency until receipt publication.
                    using (var removal = new OriginalRemovalSession(batch, receipt, originals, stamps, plan.Deletable)) {
                    removal.VerifyAll(cancel, guard.Validate);
                    guard.Validate();
                    log("Batch verification complete | " + receipt.Files.Count + " files, " + bytes + " bytes, " + reused + " reused; " + (plan.Deletable.Count > 0 ? "removal/Trash clearing requested; source files still held" : "originals kept"));
                    Action checkManifest = () => plan.CheckManifest(removal.WasRemoved, cancel, guard.Validate);
                    using (var removalIntent = plan.Deletable.Count > 0 ? removal.CaptureIntent(plan, receipt, destination, batch, cancel, guard).Publish(record, cancel, guard.Validate, log) : null) {
                    if (plan.Deletable.Count > 0) removal.Remove(cancel, guard.Validate, checkManifest, log, report);
                    int removedCount = receipt.Files.Count(e => e.RemovedSource && !e.FromTrash), clearedTrash = receipt.Files.Count(e => e.RemovedSource && e.FromTrash && !e.IsTrashRecord), clearedRecords = receipt.Files.Count(e => e.RemovedSource && e.IsTrashRecord);
                    receipt.OriginalsKept = removedCount == 0;
                    work.Emit("Publishing receipt", 0.99); report("Publishing verified import receipt. " + OriginalRemovalChoice.Summary(removedCount) + " Cleared " + clearedTrash + " verified Trash files and " + clearedRecords + " records.");
                    cancel.ThrowIfCancellationRequested(); guard.Validate(); checkManifest(); removal.VerifyAll(cancel, guard.Validate);
                    string receiptPath = Path.Combine(record, "verified-import.json");
                    using (var stream = SafePaths.NewFile(Path.Combine(record, ".receipt-" + Guid.NewGuid().ToString("N") + ".partial"))) {
                        bool complete = false;
                        try { new DataContractJsonSerializer(typeof(ImportReceipt)).WriteObject(stream, receipt); stream.Flush(true); cancel.ThrowIfCancellationRequested(); guard.Validate(); Native.Rename(stream.SafeFileHandle, receiptPath); stream.Flush(true); complete = true; }
                        finally { if (!complete) Native.DeleteOnClose(stream.SafeFileHandle); }
                    }
                    work.Complete();
                    return new ImportResult { Folder = batch, ReceiptPath = receiptPath, Receipt = receipt, Files = receipt.Files.Count, Reused = reused, RemovedOriginals = removedCount, RecoveredTrashFiles = receipt.Files.Count(e => e.FromTrash && !e.IsTrashRecord), ClearedTrashFiles = clearedTrash, ClearedTrashRecords = clearedRecords, Bytes = bytes, HasSelectedMedia = receipt.HasSelectedMedia };
                    }
                    }
                    } catch (Exception ex) {
                        int removedCount = receipt.Files.Count(e => e.RemovedSource && !e.FromTrash), clearedTrash = receipt.Files.Count(e => e.RemovedSource && e.FromTrash && !e.IsTrashRecord), clearedRecords = receipt.Files.Count(e => e.RemovedSource && e.IsTrashRecord);
                        try { log((removedCount == 0 ? "Stopped; originals kept" : "Stopped; " + removedCount + " verified originals removed; remaining originals kept") + "; " + clearedTrash + " verified Trash files and " + clearedRecords + " records cleared | " + ex.Message); } catch {}
                        if (removedCount + clearedTrash + clearedRecords > 0) throw new OriginalRemovalFailure(removedCount, ex, batch, Path.Combine(record, "import-audit.log"), clearedTrash, clearedRecords); throw;
                    }
                    }
                    }
                }
            }
        }
        static string ExistingParent(string path, string root) {
            string parent = Path.GetDirectoryName(path);
            while (!Directory.Exists(parent)) { if (!SafePaths.Within(parent, root) || parent.Equals(root, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Destination parent is unavailable."); parent = Path.GetDirectoryName(parent); }
            if (!SafePaths.Within(parent, root)) throw new SafetyException("Destination parent escaped its root.");
            return parent;
        }
        // Production guard exposes an inexpensive connection-generation check for each copy chunk.
        static bool discoveryChanged(IImportGuard guard) { var fast = guard as IConnectionGuard; return fast != null && fast.Changed; }
        internal static bool SameStamp(Native.FileInfo a, Native.FileInfo b) {
            return a.VolumeSerial == b.VolumeSerial && a.IndexHigh == b.IndexHigh && a.IndexLow == b.IndexLow && a.SizeHigh == b.SizeHigh && a.SizeLow == b.SizeLow && a.Modified.dwHighDateTime == b.Modified.dwHighDateTime && a.Modified.dwLowDateTime == b.Modified.dwLowDateTime;
        }
        public static List<string> Scan(string root, CancellationToken cancel) {
            var files = new List<string>(); Walk(root, root, files, cancel); files.Sort(StringComparer.OrdinalIgnoreCase); return files;
        }
        static void Walk(string current, string root, List<string> files, CancellationToken cancel) {
            using (SafePaths.LockDirectories(current)) {
                foreach (string item in Directory.GetFileSystemEntries(current).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) {
                    cancel.ThrowIfCancellationRequested(); SafePaths.Normalize(item); SafePaths.Relative(item, root);
                    FileAttributes attrs = File.GetAttributes(item);
                    if ((attrs & FileAttributes.ReparsePoint) != 0) throw new SafetyException("Symbolic links, junctions, cloud placeholders, and other reparse points are not imported.");
                    if (Path.GetFileName(item).StartsWith(".", StringComparison.Ordinal)) continue;
                    if ((attrs & FileAttributes.Directory) != 0) {
                        if (new[] { "System Volume Information", "$RECYCLE.BIN" }.Contains(Path.GetFileName(item), StringComparer.OrdinalIgnoreCase)) continue;
                        Walk(item, root, files, cancel);
                    } else files.Add(item);
                }
            }
        }
        static void EnsureDirectories(string directory, string root) {
            if (!SafePaths.Within(directory, root)) throw new SafetyException("Directory escaped destination.");
            if (Directory.Exists(directory)) { using (SafePaths.LockDirectories(directory)) {} return; }
            EnsureDirectories(Path.GetDirectoryName(directory), root);
            using (SafePaths.LockDirectories(Path.GetDirectoryName(directory))) { Directory.CreateDirectory(directory); using (SafePaths.LockDirectories(directory)) {} }
        }
        internal static string Hash(Stream stream, CancellationToken cancel, Action<double> progress = null) {
            long length = progress == null ? 0 : stream.Length - stream.Position, read = 0;
            using (var sha = SHA256.Create()) { byte[] buffer = new byte[1048576]; int n; while ((n = stream.Read(buffer, 0, buffer.Length)) != 0) { cancel.ThrowIfCancellationRequested(); sha.TransformBlock(buffer, 0, n, null, 0); read += n; if (progress != null) progress(length == 0 ? 1 : (double)read / length); } sha.TransformFinalBlock(new byte[0], 0, 0); if (progress != null) progress(1); return Hex(sha.Hash); }
        }
        static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    }
    public interface IConnectionGuard { bool Changed { get; } }
    [DataContract] public sealed class ImportProfile {
        [DataMember] public string Name;
        [DataMember] public string VolumeId; [DataMember] public string VolumeSerial; [DataMember] public string DiskInstance;
        [DataMember] public string RelativeSource; [DataMember] public string Destination;
        [DataMember] public DestinationIdentity SavedDestination;
        [DataMember] public bool? AutomaticImport;
        [DataMember] public bool? UsesImportDefaults; [DataMember] public bool? VideosOnly; [DataMember] public bool? IncludePreviews; [DataMember] public bool? CleanLayout;
        [DataMember] public bool? AskToEject;
        [DataMember] public bool? DeleteOriginals;
        [DataMember] public bool? RecoverTrash; [DataMember] public string MacTrashUid;
        // Per-device manual offer. Missing fields preserve legacy off/default-Luna behavior.
        [DataMember] public string SharingPreset; [DataMember] public string SharingColor;
        public bool FollowsDefaults { get { return UsesImportDefaults == true; } }
        public bool AutomaticEnabled(ImportDefaults defaults) { if (defaults == null) throw new ArgumentNullException("defaults"); return FollowsDefaults ? defaults.AutomaticEnabled : AutomaticImport ?? false; }
        public MediaOptions Resolve(ImportDefaults defaults) {
            if (defaults == null) throw new ArgumentNullException("defaults");
            // Prototype profiles without option fields used all-media with previews. Preserve that.
            var choices = FollowsDefaults ? defaults.Snapshot() : new MediaOptions(VideosOnly ?? false, IncludePreviews ?? true, CleanLayout ?? false, AskToEject ?? false, DeleteOriginals ?? false, RecoverTrash ?? false);
            if (MacTrashUid != null && !DeviceTrash.ValidMacOwner(MacTrashUid)) throw new SafetyException("Saved Mac Trash owner selection is invalid. Review this device's profile.");
            return new MediaOptions(choices.VideosOnly, choices.IncludePreviews, choices.CleanLayout, choices.AskToEject, choices.DeleteOriginals, choices.RecoverTrash, MacTrashUid);
        }
        public void SetCustom(bool custom, ImportDefaults defaults) {
            bool automatic = FollowsDefaults || !custom ? defaults.AutomaticEnabled : AutomaticImport ?? false;
            MediaOptions options = FollowsDefaults || !custom ? defaults.Snapshot() : Resolve(defaults);
            VideosOnly = options.VideosOnly; IncludePreviews = options.IncludePreviews; CleanLayout = options.CleanLayout; AskToEject = options.AskToEject; DeleteOriginals = options.DeleteOriginals; RecoverTrash = options.RecoverTrash; AutomaticImport = automatic; UsesImportDefaults = !custom;
        }
        public void SetVideosOnly(bool value) { if (!value && (VideosOnly ?? false)) { DeleteOriginals = false; RecoverTrash = false; } VideosOnly = value; }
        public ImportProfile Copy() { var value = (ImportProfile)MemberwiseClone(); if (SavedDestination != null) value.SavedDestination = SavedDestination.Copy(); return value; }
    }
    public static partial class Profiles {
        internal static string Root {
            get {
                string appData = SafePaths.Normalize(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                // Codex's packaged process redirects newly created app-data folders. Resolve only
                // our own configuration folder inside app-data; media import paths stay strict.
                using (SafePaths.LockDirectories(appData)) {
                    string app = Path.Combine(appData, "EasyEject"); Directory.CreateDirectory(app);
                    using (var handle = Native.CreateFile(app, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero)) {
                        if (handle.IsInvalid) throw Native.Error("Cannot open Easy Eject configuration folder");
                        Native.Info(handle); string physical = Native.FinalPath(handle);
                        if (!SafePaths.Within(physical, appData)) throw new SafetyException("Configuration folder resolves outside local app-data.");
                        using (SafePaths.LockDirectories(physical)) {
                            string folder = Path.Combine(physical, "Profiles"); Directory.CreateDirectory(folder);
                            using (SafePaths.LockDirectories(folder)) {} return folder;
                        }
                    }
                }
            }
        }
        internal static string FileName(string volumeId) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(volumeId))).Replace("-", "") + ".json"; }
        public static void Save(ImportProfile profile) {
            Save(profile, Root);
        }
        internal static void Save(ImportProfile profile, string root) {
            if (profile == null || String.IsNullOrWhiteSpace(profile.VolumeId)) throw new SafetyException("Import profile has no volume identity.");
            if (profile.MacTrashUid != null && !DeviceTrash.ValidMacOwner(profile.MacTrashUid)) throw new SafetyException("Saved Mac Trash owner selection is invalid. Review this device's profile.");
            ValidateName(profile.Name, false);
            QuickShareProfile.Validate(profile);
            if (profile.SavedDestination != null) profile.SavedDestination.Validate();
            Directory.CreateDirectory(root);
            using (SafePaths.LockDirectories(root)) using (WriterGate(root)) WriteJson(profile, typeof(ImportProfile), root, FileName(profile.VolumeId));
        }
        internal static void WriteJson(object value, Type type, string root, string name) {
            Directory.CreateDirectory(root);
            using (SafePaths.LockDirectories(root)) {
                string temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp"), final = Path.Combine(root, name);
                SafePaths.Normalize(final); if (!SafePaths.Within(final, root) || Path.GetFileName(final) != name) throw new SafetyException("Configuration filename escaped its folder.");
                try {
                    using (var stream = SafePaths.NewFile(temporary)) { new DataContractJsonSerializer(type).WriteObject(stream, value); stream.Flush(true); }
                    if (File.Exists(final)) { using (var existing = SafePaths.ReadFile(final)) {} }
                    if (File.Exists(final)) File.Replace(temporary, final, null); else File.Move(temporary, final);
                } finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        public static ImportProfile Load(Volume volume, Disk disk) {
            return Load(volume, disk, Root);
        }
        internal static ImportProfile Load(Volume volume, Disk disk, string root) {
            string path = Path.Combine(root, FileName(volume.Id)); if (!File.Exists(path)) return null;
            using (SafePaths.LockDirectories(root)) using (var stream = SafePaths.ReadFile(path)) {
                if (stream.Length > 65536) throw new SafetyException("Saved import profile is too large to read.");
                var p = (ImportProfile)new DataContractJsonSerializer(typeof(ImportProfile)).ReadObject(stream);
                if (p == null || p.VolumeId != volume.Id || p.VolumeSerial != volume.Serial || p.DiskInstance != disk.InstanceId) throw new SafetyException("Saved import profile no longer matches this card. Choose the folders again and save a new profile.");
                if (p.RelativeSource == null || p.Destination == null) throw new SafetyException("Saved import folders are invalid. Choose the folders again.");
                ValidateName(p.Name, false);
                QuickShareProfile.Validate(p);
                if (p.SavedDestination != null) p.SavedDestination.Validate();
                if (p.MacTrashUid != null && !DeviceTrash.ValidMacOwner(p.MacTrashUid)) throw new SafetyException("Saved Mac Trash owner selection is invalid. Review this device's profile.");
                return p;
            }
        }
        public static void Remove(Volume volume, Disk disk) { Remove(volume, disk, Root); }
        internal static void Remove(Volume volume, Disk disk, string root) {
            using (SafePaths.LockDirectories(root)) using (WriterGate(root)) {
                if (Load(volume, disk, root) == null) return;
                string path = Path.Combine(root, FileName(volume.Id));
                using (var handle = Native.CreateFile(path, 0x10080, 0, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero)) {
                    if (handle.IsInvalid) throw Native.Error("Cannot forget the saved import profile");
                    Native.Info(handle);
                    if (!Native.FinalPath(handle).Equals(path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Saved profile resolves to another path.");
                    Native.DeleteOnClose(handle);
                }
            }
        }
    }
}
