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
    public sealed class VolumeImportGuard : IImportGuard, IConnectionGuard {
        readonly IDiscovery discovery; readonly string sourceId, destinationId, fingerprint; readonly long epoch;
        readonly string source, destination; readonly int[] disks;
        public int[] ProtectedDisks { get { return disks; } }
        public bool Changed { get { return discovery.Epoch != epoch; } }
        public VolumeImportGuard(IDiscovery discovery, string source, string destination, Volume expectedVolume, Disk expectedDisk, long selectionEpoch) {
            this.discovery = discovery; this.source = source; this.destination = destination;
            sourceId = WindowsDiscovery.VolumeId(source); destinationId = WindowsDiscovery.VolumeId(destination);
            if (sourceId != expectedVolume.Id) throw new SafetyException("The selected source belongs to a different volume.");
            var state = discovery.Scan(); epoch = state.Epoch;
            var currentSource = Resolve(state, sourceId);
            if (epoch != selectionEpoch || currentSource.Fingerprint != expectedVolume.Fingerprint || state.Disks.Single(d => d.Number == currentSource.Disks[0]).Fingerprint != expectedDisk.Fingerprint)
                throw new SafetyException("The selected card connection changed while choosing folders. Refresh and choose the device again.");
            disks = PlanProtection(state, sourceId, destinationId); fingerprint = Fingerprint(state);
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
        string Fingerprint(Inventory state) {
            return Resolve(state, sourceId).Fingerprint + "\n" + Resolve(state, destinationId).Fingerprint + "\n" + String.Join("\n", state.Disks.Where(d => disks.Contains(d.Number)).Select(d => d.Fingerprint).OrderBy(x => x));
        }
        public void Validate() {
            if (discovery.Epoch != epoch) throw new SafetyException("Device connection changed during import. Originals are kept.");
            var state = discovery.Scan();
            if (state.Epoch != epoch || WindowsDiscovery.VolumeId(source) != sourceId || WindowsDiscovery.VolumeId(destination) != destinationId || Fingerprint(state) != fingerprint) throw new SafetyException("Import device identities changed. Originals are kept.");
        }
    }
    [DataContract] public sealed class ReceiptEntry {
        [DataMember] public string RelativePath; [DataMember] public string Sha256; [DataMember] public long Bytes;
    }
    [DataContract] public sealed class ImportReceipt {
        [DataMember] public string StartedUtc; [DataMember] public string SourceVolume; [DataMember] public bool OriginalsKept = true;
        [DataMember] public bool HasSelectedMedia;
        [DataMember] public List<ReceiptEntry> Files = new List<ReceiptEntry>();
    }
    public sealed class ImportResult { public string Folder; public int Files; public long Bytes; public bool HasSelectedMedia; }
    public sealed class VerifiedImporter {
        readonly Operations operations;
        public VerifiedImporter(Operations operations) { this.operations = operations; }
        public ImportResult Run(string sourceRoot, string selectedFolder, string destination, string volumeId, IImportGuard guard, CancellationToken cancel, Action<string> report) {
            return Run(sourceRoot, selectedFolder, destination, volumeId, guard, cancel, report, new MediaOptions(false, true));
        }
        public ImportResult Run(string sourceRoot, string selectedFolder, string destination, string volumeId, IImportGuard guard, CancellationToken cancel, Action<string> report, MediaOptions options) {
            sourceRoot = SafePaths.Normalize(sourceRoot); selectedFolder = SafePaths.Normalize(selectedFolder); destination = SafePaths.Normalize(destination);
            if (!SafePaths.Within(selectedFolder, sourceRoot) || SafePaths.Within(destination, sourceRoot) || SafePaths.Within(sourceRoot, destination)) throw new SafetyException("Source and destination folders overlap or the selection is outside the source volume.");
            if (new[] { "STREAM", "CLIP" }.Contains(Path.GetFileName(selectedFolder).ToUpperInvariant())) throw new SafetyException("Select the complete camera package or card, including audio and metadata, rather than a partial STREAM/CLIP folder.");
            using (operations.Reserve(guard.ProtectedDisks))
            using (SafePaths.LockDirectories(selectedFolder, destination)) {
                cancel.ThrowIfCancellationRequested(); guard.Validate();
                var plan = MediaSelection.Plan(Scan(selectedFolder, cancel), sourceRoot, selectedFolder, options);
                var manifest = plan.Files;
                if (manifest.Count == 0) return new ImportResult { Folder = destination, Files = 0 };
                var stamps = new Dictionary<string, Native.FileInfo>();
                foreach (string file in manifest) {
                    using (SafePaths.LockDirectories(Path.GetDirectoryName(file))) using (var input = SafePaths.ReadFile(file)) stamps[file] = Native.Info(input.SafeFileHandle);
                }
                string batch = Path.Combine(destination, DateTime.Now.ToString("yyyy-MM-dd") + " Easy Eject " + Guid.NewGuid().ToString("N"));
                // A fresh batch avoids collisions; finalization is also exclusive for every file.
                if (!Native.CreateDirectory(batch, IntPtr.Zero)) throw Native.Error("Cannot create a new, exclusive import batch");
                using (SafePaths.LockDirectories(batch)) {
                    var receipt = new ImportReceipt { StartedUtc = DateTime.UtcNow.ToString("o"), SourceVolume = volumeId, HasSelectedMedia = plan.Media.Count != 0 };
                    var originals = new Dictionary<string, string>();
                    long bytes = 0;
                    foreach (string file in manifest) {
                        cancel.ThrowIfCancellationRequested(); guard.Validate();
                        string relative = SafePaths.Relative(file, selectedFolder), target = Path.Combine(batch, MediaSelection.IsDeviceIndex(file) ? ".easy-eject-metadata" : "Camera originals", relative);
                        originals[SafePaths.Relative(target, batch)] = file;
                        SafePaths.Normalize(target); if (!SafePaths.Within(target, batch)) throw new SafetyException("Destination path escaped batch folder.");
                        EnsureDirectories(Path.GetDirectoryName(target), batch);
                        using (SafePaths.LockDirectories(Path.GetDirectoryName(file), Path.GetDirectoryName(target)))
                        using (var input = SafePaths.ReadFile(file)) {
                            if (!SameStamp(Native.Info(input.SafeFileHandle), stamps[file])) throw new SafetyException("A source file changed after scanning. Originals are kept.");
                            string pending = Path.Combine(Path.GetDirectoryName(target), ".easyeject-" + Guid.NewGuid().ToString("N") + ".partial");
                            bool published = false;
                            using (var output = SafePaths.NewFile(pending)) {
                                try {
                                    report("Copying " + relative); var buffer = new byte[1048576]; long count = 0; int n;
                                    using (var sourceHash = SHA256.Create()) {
                                        while ((n = input.Read(buffer, 0, buffer.Length)) != 0) {
                                            cancel.ThrowIfCancellationRequested(); if (discoveryChanged(guard)) guard.Validate();
                                            output.Write(buffer, 0, n); sourceHash.TransformBlock(buffer, 0, n, null, 0); count += n;
                                        }
                                        sourceHash.TransformFinalBlock(new byte[0], 0, 0);
                                        output.Flush(true); output.Position = 0; report("Verifying " + relative);
                                        string copyHash = Hash(output, cancel), originalHash = Hex(sourceHash.Hash);
                                        // Reread the held, write/delete-denying source handle, not a path that can be swapped.
                                        input.Position = 0;
                                        if (copyHash != originalHash || Hash(input, cancel) != originalHash || input.Length != count) throw new SafetyException("SHA-256 verification failed. Originals are kept.");
                                        cancel.ThrowIfCancellationRequested(); guard.Validate();
                                        Native.Rename(output.SafeFileHandle, target); published = true; output.Flush(true);
                                        output.Position = 0;
                                        string publishedPath = Native.FinalPath(output.SafeFileHandle), publishedHash = Hash(output, cancel);
                                        if (!publishedPath.Equals(target, StringComparison.OrdinalIgnoreCase) || publishedHash != originalHash) throw new SafetyException("Published copy verification failed. Originals are kept. Expected path: " + target + "; resolved path: " + publishedPath + "; hash match: " + (publishedHash == originalHash));
                                        receipt.Files.Add(new ReceiptEntry { RelativePath = SafePaths.Relative(target, batch), Sha256 = originalHash, Bytes = count }); bytes += count;
                                    }
                                } finally {
                                    // Cleanup addresses only the held private file; a path substitution cannot delete another file.
                                    if (!published) Native.DeleteOnClose(output.SafeFileHandle);
                                }
                            }
                        }
                    }
                    guard.Validate(); cancel.ThrowIfCancellationRequested();
                    if (!manifest.SequenceEqual(MediaSelection.Plan(Scan(selectedFolder, cancel), sourceRoot, selectedFolder, options).Files)) throw new SafetyException("Source file list changed during import. Completed copies and all originals remain available.");
                    // Recheck every final copy before marking this batch complete; fail closed on an edited earlier copy.
                    foreach (var entry in receipt.Files) {
                        string path = Path.Combine(batch, entry.RelativePath);
                        using (SafePaths.LockDirectories(Path.GetDirectoryName(path))) using (var saved = SafePaths.ReadFile(path)) {
                            if (saved.Length != entry.Bytes || Hash(saved, cancel) != entry.Sha256) throw new SafetyException("An earlier saved copy changed. Batch remains incomplete; originals are kept.");
                        }
                        string original = originals[entry.RelativePath];
                        using (SafePaths.LockDirectories(Path.GetDirectoryName(original))) using (var input = SafePaths.ReadFile(original)) {
                            if (!SameStamp(Native.Info(input.SafeFileHandle), stamps[original]) || Hash(input, cancel) != entry.Sha256) throw new SafetyException("A source recording changed after copying. Batch remains incomplete; originals are kept.");
                        }
                    }
                    guard.Validate();
                    string receiptPath = Path.Combine(batch, "verified-import.json");
                    using (var stream = SafePaths.NewFile(Path.Combine(batch, ".receipt-" + Guid.NewGuid().ToString("N") + ".partial"))) {
                        bool complete = false;
                        try { new DataContractJsonSerializer(typeof(ImportReceipt)).WriteObject(stream, receipt); stream.Flush(true); cancel.ThrowIfCancellationRequested(); guard.Validate(); Native.Rename(stream.SafeFileHandle, receiptPath); stream.Flush(true); complete = true; }
                        finally { if (!complete) Native.DeleteOnClose(stream.SafeFileHandle); }
                    }
                    report("Verified " + receipt.Files.Count + " files. Originals kept. Device remains connected.");
                    return new ImportResult { Folder = batch, Files = receipt.Files.Count, Bytes = bytes, HasSelectedMedia = receipt.HasSelectedMedia };
                }
            }
        }
        // Production guard exposes an inexpensive connection-generation check for each copy chunk.
        static bool discoveryChanged(IImportGuard guard) { var fast = guard as IConnectionGuard; return fast != null && fast.Changed; }
        static bool SameStamp(Native.FileInfo a, Native.FileInfo b) {
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
        static string Hash(Stream stream, CancellationToken cancel) {
            using (var sha = SHA256.Create()) { byte[] buffer = new byte[1048576]; int n; while ((n = stream.Read(buffer, 0, buffer.Length)) != 0) { cancel.ThrowIfCancellationRequested(); sha.TransformBlock(buffer, 0, n, null, 0); } sha.TransformFinalBlock(new byte[0], 0, 0); return Hex(sha.Hash); }
        }
        static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    }
    public interface IConnectionGuard { bool Changed { get; } }
    [DataContract] public sealed class ImportProfile {
        [DataMember] public string VolumeId; [DataMember] public string VolumeSerial; [DataMember] public string DiskInstance;
        [DataMember] public string RelativeSource; [DataMember] public string Destination;
        [DataMember] public bool? UsesImportDefaults; [DataMember] public bool? VideosOnly; [DataMember] public bool? IncludePreviews;
        public bool FollowsDefaults { get { return UsesImportDefaults == true; } }
        public MediaOptions Resolve(ImportDefaults defaults) {
            if (defaults == null) throw new ArgumentNullException("defaults");
            // Prototype profiles without option fields used all-media with previews. Preserve that.
            return FollowsDefaults ? defaults.Snapshot() : new MediaOptions(VideosOnly ?? false, IncludePreviews ?? true);
        }
        public void SetCustom(bool custom, ImportDefaults defaults) {
            MediaOptions options = FollowsDefaults || !custom ? defaults.Snapshot() : Resolve(defaults);
            VideosOnly = options.VideosOnly; IncludePreviews = options.IncludePreviews; UsesImportDefaults = !custom;
        }
        public ImportProfile Copy() { return (ImportProfile)MemberwiseClone(); }
    }
    public static class Profiles {
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
        static string FileName(string volumeId) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(volumeId))).Replace("-", "") + ".json"; }
        public static void Save(ImportProfile profile) {
            Save(profile, Root);
        }
        internal static void Save(ImportProfile profile, string root) {
            if (profile == null || String.IsNullOrWhiteSpace(profile.VolumeId)) throw new SafetyException("Import profile has no volume identity.");
            WriteJson(profile, typeof(ImportProfile), root, FileName(profile.VolumeId));
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
                return p;
            }
        }
        public static void Remove(Volume volume, Disk disk) { Remove(volume, disk, Root); }
        internal static void Remove(Volume volume, Disk disk, string root) {
            using (SafePaths.LockDirectories(root)) {
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
