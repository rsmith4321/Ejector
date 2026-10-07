using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace EasyEject {
    // Persistent identity excludes transient disk numbers, devnodes and connection epochs.
    // Each later operation obtains a fresh, connection-bound guard; history cannot eject.
    [DataContract] public sealed class DestinationIdentity {
        [DataMember] public string VolumeId, VolumeSerial, DiskInstance, DiskSerial;
        public DestinationIdentity Copy() { return (DestinationIdentity)MemberwiseClone(); }
        internal void Validate() {
            if (String.IsNullOrWhiteSpace(VolumeId) || String.IsNullOrWhiteSpace(VolumeSerial) || String.IsNullOrWhiteSpace(DiskInstance) || DiskSerial == null)
                throw new SafetyException("Saved destination identity is incomplete.");
        }
        internal bool Matches(DestinationIdentity other) {
            Validate(); if (other == null) return false; other.Validate();
            return String.Equals(VolumeId, other.VolumeId, StringComparison.OrdinalIgnoreCase) && String.Equals(VolumeSerial, other.VolumeSerial, StringComparison.OrdinalIgnoreCase)
                && String.Equals(DiskInstance, other.DiskInstance, StringComparison.OrdinalIgnoreCase) && String.Equals(DiskSerial, other.DiskSerial, StringComparison.OrdinalIgnoreCase);
        }
        internal static DestinationIdentity Capture(Inventory state, string id) {
            var volume = state.Volumes.SingleOrDefault(x => x.Id == id);
            if (!state.Complete || volume == null || volume.Error != "" || volume.Disks.Count != 1) throw new SafetyException("Saved destination is missing or has an unknown physical mapping.");
            var disk = state.Disks.SingleOrDefault(x => x.Number == volume.Disks[0]);
            if (disk == null || disk.Error != "") throw new SafetyException("Saved destination disk is unavailable.");
            var result = new DestinationIdentity { VolumeId = volume.Id, VolumeSerial = volume.Serial, DiskInstance = disk.InstanceId, DiskSerial = disk.Serial };
            result.Validate(); return result;
        }
    }
    internal sealed class WindowsDestinationGuard : IImportGuard, IConnectionGuard, IConcurrentStorageGuard {
        readonly IDiscovery discovery; readonly string root; readonly DestinationIdentity expected; readonly int[] disks;
        readonly Func<string, string> volumeId; readonly StorageConnectionProof connection;
        public int[] ProtectedDisks { get { return (int[])disks.Clone(); } }
        public bool Changed { get { return connection.Changed; } }
        public bool AllowsUnrelatedOperations { get { return connection.Scoped && !connection.ProtectedConnectionChanged; } }
        internal WindowsDestinationGuard(IDiscovery discovery, string root, DestinationIdentity expected, Func<string, string> volumeId = null) {
            this.discovery = discovery; this.root = SafePaths.Normalize(root); this.expected = expected.Copy(); this.volumeId = volumeId ?? WindowsDiscovery.VolumeId;
            var state = discovery.Scan(); CheckIdentity(state);
            var volume = state.Volumes.Single(x => x.Id == expected.VolumeId); var disk = state.Disks.Single(x => x.Number == volume.Disks.Single());
            disks = state.Disks.Where(x => x.Number == disk.Number || (disk.TargetId != "" && (x.InstanceId == disk.TargetId || x.Ancestors.Contains(disk.TargetId)))).Select(x => x.Number).ToArray();
            connection = new StorageConnectionProof(discovery, state, disks);
        }
        void CheckIdentity(Inventory state) {
            if (volumeId(root) != expected.VolumeId || !expected.Matches(DestinationIdentity.Capture(state, expected.VolumeId)))
                throw new SafetyException("The saved destination changed. No saved original or export was changed.");
        }
        public void Validate() {
            var state = discovery.Scan(); CheckIdentity(state);
            connection.Validate(state);
        }
    }
    [DataContract] public sealed class VerifiedCopy {
        [DataMember] public string Path, Sha256; [DataMember] public long Bytes;
    }
    [DataContract] public sealed class CompletedBatch {
        [DataMember] public string Id, ProfileId, DeviceName, CompletedUtc, Folder, DestinationRoot, ReceiptPath, ReceiptSha256;
        [DataMember] public DestinationIdentity Destination;
        [DataMember] public List<VerifiedCopy> Originals = new List<VerifiedCopy>();
    }
    internal static class ImportHistory {
        const int Limit = 20; const long MaximumJson = 16777216;
        static readonly object gate = new object();
        sealed class HistorySnapshot {
            internal List<CompletedBatch> Values = new List<CompletedBatch>();
            internal bool Exists; internal string Hash; internal Native.FileInfo Stamp;
        }
        internal static CompletedBatch Create(ImportResult result, ImportProfile profile, string deviceName, DestinationIdentity destination) {
            if (!result.HasSelectedMedia) return null;
            destination.Validate();
            ImportReceipt receipt; string hash;
            using (SafePaths.LockDirectories(result.Folder, Path.GetDirectoryName(result.ReceiptPath))) {
                receipt = ReadReceipt(result.ReceiptPath, out hash);
            }
            string expectedHash;
            using (var expected = new MemoryStream()) { new DataContractJsonSerializer(typeof(ImportReceipt)).WriteObject(expected, result.Receipt); expected.Position = 0; expectedHash = Hash(expected, CancellationToken.None); }
            if (hash != expectedHash || receipt.Id != result.Receipt.Id || receipt.SourceVolume != profile.VolumeId || !receipt.HasSelectedMedia) throw new SafetyException("The completed import receipt does not match this operation.");
            var value = new CompletedBatch { Id = receipt.Id, ProfileId = profile.VolumeId, DeviceName = deviceName, CompletedUtc = DateTime.UtcNow.ToString("o"), Folder = result.Folder,
                DestinationRoot = profile.Destination, ReceiptPath = result.ReceiptPath, ReceiptSha256 = hash, Destination = destination.Copy(),
                Originals = receipt.Files.Where(x => x.IsMedia).Select(x => new VerifiedCopy { Path = System.IO.Path.Combine(result.Folder, x.RelativePath), Sha256 = x.Sha256, Bytes = x.Bytes }).ToList() };
            Validate(value); return value;
        }
        internal static List<CompletedBatch> Load() { return Load(Profiles.Root); }
        internal static List<CompletedBatch> Load(string root) {
            lock (gate) using (var directories = SafePaths.HoldDirectories(root)) {
                var snapshot = ReadHistory(root); directories.Validate(); return snapshot.Values;
            }
        }
        static HistorySnapshot ReadHistory(string root) {
            string path = Path.Combine(root, "completed-imports.json");
            try { if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) throw new SafetyException("Saved import history is a directory, not an ordinary record."); }
            catch (FileNotFoundException) { return new HistorySnapshot(); }
            using (var stream = SafePaths.ReadFile(path)) {
                if (stream.Length > MaximumJson) throw new SafetyException("Saved import history is too large to read.");
                var stamp = Native.Info(stream.SafeFileHandle);
                var values = JsonDocuments.Read<List<CompletedBatch>>(stream, (int)MaximumJson);
                if (values == null) throw new SafetyException("Saved import history is invalid.");
                // Validate even older excess records before retaining the last 20.
                foreach (var value in values) Validate(value);
                if (values.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count) throw new SafetyException("Saved history contains duplicate batch identities.");
                stream.Position = 0; string hash = Hash(stream, CancellationToken.None);
                if (!VerifiedImporter.SameStamp(stamp, Native.Info(stream.SafeFileHandle))) throw new SafetyException("Saved import history changed during its held read.");
                return new HistorySnapshot { Values = values.Take(Limit).ToList(), Exists = true, Stamp = stamp, Hash = hash };
            }
        }
        static void RequirePrevious(HistorySnapshot snapshot, FileStream previous) {
            if (snapshot.Exists != (previous != null)) throw new SafetyException("Saved import history appeared or disappeared before publication. Refresh history before retrying.");
            if (previous != null) previous.Position = 0;
            if (previous != null && (!VerifiedImporter.SameStamp(snapshot.Stamp, Native.Info(previous.SafeFileHandle)) || previous.Length > MaximumJson || Hash(previous, CancellationToken.None) != snapshot.Hash))
                throw new SafetyException("Saved import history changed before its exclusive replacement. Existing history is retained; refresh before retrying.");
        }
        internal static void Add(CompletedBatch value) { Add(value, Profiles.Root); }
        internal static void Add(CompletedBatch value, string root, Action<string> checkpoint = null) {
            if (value == null) return;
            // Validate a deep copy before creating any storage; callers cannot change
            // this append after the shared transaction has been admitted.
            value = Clone(value); Validate(value); root = SafePaths.Normalize(root);
            lock (gate) using (var directories = ConfigurationWriter.CreateRoot(root)) using (var writer = Profiles.WriterGate(root)) {
                directories.Validate(); writer.Validate(root); var snapshot = ReadHistory(root); directories.Validate();
                if (checkpoint != null) checkpoint("After history read"); directories.Validate(); writer.Validate(root);
                var values = snapshot.Values;
                if (values.Any(x => String.Equals(x.Id, value.Id, StringComparison.OrdinalIgnoreCase))) throw new SafetyException("This import batch is already in history.");
                values.Insert(0, value); values = values.Take(Limit).ToList();
                using (var size = new MemoryStream()) { new DataContractJsonSerializer(typeof(List<CompletedBatch>)).WriteObject(size, values); if (size.Length > MaximumJson) throw new SafetyException("Completed history exceeds its safe storage limit. Verified copies remain available."); }
                ConfigurationWriter.Write(values, typeof(List<CompletedBatch>), root, "completed-imports.json", null, writer, previous => RequirePrevious(snapshot, previous));
            }
        }
        internal static CompletedBatch Clone(CompletedBatch value) {
            using (var stream = new MemoryStream()) { var json = new DataContractJsonSerializer(typeof(CompletedBatch)); json.WriteObject(stream, value); stream.Position = 0; return (CompletedBatch)json.ReadObject(stream); }
        }
        internal static void Validate(CompletedBatch value) {
            DateTime date; Guid id;
            if (value == null || !Guid.TryParseExact(value.Id, "N", out id) || String.IsNullOrWhiteSpace(value.ProfileId) || String.IsNullOrWhiteSpace(value.DeviceName)
                || !DateTime.TryParse(value.CompletedUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out date) || date.Kind != DateTimeKind.Utc
                || value.Destination == null || value.Originals == null || value.Originals.Count == 0 || !ValidHash(value.ReceiptSha256)) throw new SafetyException("Saved import batch is incomplete.");
            value.Destination.Validate();
            foreach (string path in new[] { value.DestinationRoot, value.Folder, value.ReceiptPath }) Canonical(path);
            string receipt = Path.Combine(value.Folder, ".easy-eject-receipts", value.Id, "verified-import.json");
            if (!SafePaths.Within(value.Folder, value.DestinationRoot) || value.Folder.Equals(value.DestinationRoot, StringComparison.OrdinalIgnoreCase) || !receipt.Equals(value.ReceiptPath, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Saved batch paths no longer match the exact import record.");
            foreach (var original in value.Originals) {
                if (original == null || !ValidHash(original.Sha256) || original.Bytes < 0) throw new SafetyException("Saved batch file verification is incomplete.");
                Canonical(original.Path);
                if (!SafePaths.Within(original.Path, value.Folder) || original.Path.Equals(value.Folder, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Saved original escaped its batch folder.");
                string relative = SafePaths.Relative(original.Path, value.Folder);
                string first = relative.Split('\\')[0];
                if (first.Equals(".easy-eject-receipts", StringComparison.OrdinalIgnoreCase) || first.Equals(".easy-eject-metadata", StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Import records/indexes cannot become Quick Share originals.");
            }
            if (value.Originals.Select(x => x.Path.Normalize(NormalizationForm.FormC)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Originals.Count) throw new SafetyException("Saved batch contains duplicate file paths.");
        }
        static void Canonical(string path) { if (path == null || !String.Equals(path, SafePaths.Normalize(path), StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Saved batch path is ambiguous."); }
        internal static bool ValidHash(string hash) { return hash != null && hash.Length == 64 && hash.All(x => (x >= '0' && x <= '9') || (x >= 'a' && x <= 'f')); }
        internal static ImportReceipt ReadReceipt(string path, out string hash) {
            using (var stream = SafePaths.ReadFile(path)) return ReadReceipt(stream, CancellationToken.None, out hash);
        }
        internal static ImportReceipt ReadReceipt(FileStream stream, CancellationToken cancel, out string hash) {
            stream.Position = 0;
            if (stream.Length > MaximumJson) throw new SafetyException("Completed receipt is too large to read.");
            hash = Hash(stream, cancel); stream.Position = 0;
            var receipt = JsonDocuments.Read<ImportReceipt>(stream, (int)MaximumJson);
            if (receipt == null || receipt.Files == null || receipt.Files.Any(x => x == null || x.RelativePath == null || !ValidHash(x.Sha256) || x.Bytes < 0)) throw new SafetyException("Completed receipt is invalid.");
            return receipt;
        }
        internal static void RequireExactReceipt(CompletedBatch batch, ImportReceipt receipt, string hash) {
            if (hash != batch.ReceiptSha256 || receipt.Id != batch.Id || receipt.SourceVolume != batch.ProfileId || !receipt.HasSelectedMedia) throw new SafetyException("The completed receipt changed. Saved originals were not changed.");
            var recorded = receipt.Files.Where(x => x.IsMedia).Select(x => new VerifiedCopy { Path = Path.Combine(batch.Folder, x.RelativePath), Sha256 = x.Sha256, Bytes = x.Bytes }).ToArray();
            if (recorded.Length != batch.Originals.Count || recorded.Where((x, i) => x.Path != batch.Originals[i].Path || x.Sha256 != batch.Originals[i].Sha256 || x.Bytes != batch.Originals[i].Bytes).Any()) throw new SafetyException("Saved batch no longer matches its exact receipt.");
        }
        // Operates only on exact recorded paths. Never scans the date folder for candidates.
        internal static void Verify(CompletedBatch batch, DestinationIdentity current, IImportGuard guard, Operations operations, CancellationToken cancel, Action<string> report) {
            batch = Clone(batch); Validate(batch);
            if (!batch.Destination.Matches(current)) throw new SafetyException("The saved destination changed. Saved originals were not changed.");
            using (operations.Reserve(guard.ProtectedDisks)) using (var directories = SafePaths.HoldDirectories(batch.DestinationRoot, batch.Folder, Path.GetDirectoryName(batch.ReceiptPath))) {
                var parents = new Dictionary<string, SafePaths.DirectoryLease>(StringComparer.OrdinalIgnoreCase);
                try {
                // Capture every exact recorded parent before invoking device callbacks.
                // Validate only the current parent per file, and all parents at the
                // boundaries, avoiding a quadratic walk for large package histories.
                foreach (string parent in batch.Originals.Select(original => Path.GetDirectoryName(original.Path)).Distinct(StringComparer.OrdinalIgnoreCase)) { cancel.ThrowIfCancellationRequested(); parents.Add(parent, SafePaths.HoldDirectories(parent)); }
                Action validateParents = () => { foreach (var lease in parents.Values) lease.Validate(); };
                cancel.ThrowIfCancellationRequested(); directories.Validate(); guard.Validate(); directories.Validate(); validateParents(); string hash; var receipt = ReadReceipt(batch.ReceiptPath, out hash); directories.Validate();
                RequireExactReceipt(batch, receipt, hash);
                foreach (var original in batch.Originals) {
                    var originalDirectories = parents[Path.GetDirectoryName(original.Path)];
                    {
                        cancel.ThrowIfCancellationRequested(); directories.Validate(); originalDirectories.Validate(); guard.Validate(); directories.Validate(); originalDirectories.Validate();
                        report("Verifying saved " + SafePaths.Relative(original.Path, batch.Folder)); directories.Validate(); originalDirectories.Validate();
                        using (var stream = SafePaths.ReadFile(original.Path)) {
                            if (stream.Length != original.Bytes || Hash(stream, cancel) != original.Sha256) throw new SafetyException("A saved original changed. Quick Share is refused; files were not changed.");
                            directories.Validate(); originalDirectories.Validate();
                        }
                    }
                }
                directories.Validate(); guard.Validate(); directories.Validate(); validateParents(); cancel.ThrowIfCancellationRequested();
                } finally { foreach (var lease in parents.Values.Reverse()) lease.Dispose(); }
            }
        }
        internal static string Hash(Stream stream, CancellationToken cancel) {
            using (var sha = SHA256.Create()) { byte[] buffer = new byte[1048576]; int count; while ((count = stream.Read(buffer, 0, buffer.Length)) != 0) { cancel.ThrowIfCancellationRequested(); sha.TransformBlock(buffer, 0, count, null, 0); } sha.TransformFinalBlock(new byte[0], 0, 0); return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant(); }
        }
    }
}
