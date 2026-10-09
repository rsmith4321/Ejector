using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using EasyEject;

// Deliberate opt-in foreground fixture. This program never constructs an eject
// API or a real source guard. Native storage use is notification registration and
// read-only discovery/verification of generated files on the internal destination.
internal static class NativeHistoryFixture {
    sealed class NativeMonitor : IDisposable {
        readonly IDisposable monitor;
        internal NativeMonitor(WindowsDiscovery discovery) {
            var type = typeof(VerifiedImporter).Assembly.GetType("EasyEject.StorageNotificationWindow", true);
            monitor = (IDisposable)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { discovery, new Func<bool>(() => false) }, null);
            try {
                type.GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(monitor, null);
                if (!discovery.NotificationsAvailable) throw new IOException("Native connection notifications are unavailable.");
            } catch { monitor.Dispose(); throw; }
        }
        public void Dispose() { monitor.Dispose(); }
    }
    static string[] Verify(CompletedBatch batch, DestinationIdentity current, WindowsDiscovery discovery, CancellationToken cancel,
        Action<string> checkpoint = null, Action<IImportGuard> ready = null, Operations operations = null) {
        var backend = typeof(VerifiedImporter).Assembly;
        var guardType = backend.GetType("EasyEject.WindowsDestinationGuard", true);
        Func<CompletedBatch, IImportGuard> makeGuard = saved => (IImportGuard)Activator.CreateInstance(guardType,
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { discovery, saved.DestinationRoot, saved.Destination, null }, null);
        var method = backend.GetType("EasyEject.ImportHistory", true).GetMethod("Verify", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(CompletedBatch), typeof(DestinationIdentity), typeof(Func<CompletedBatch, IImportGuard>),
                typeof(Operations), typeof(CancellationToken), typeof(Action<string>), typeof(Action<IImportGuard>) }, null);
        var messages = new List<string>();
        method.Invoke(null, new object[] { batch, current, makeGuard, operations ?? new Operations(), cancel,
            new Action<string>(line => { messages.Add(line); checkpoint?.Invoke(line); }), ready });
        return messages.ToArray();
    }
    static object[] VerifyPowerBoundaries(CompletedBatch batch, DestinationIdentity current, WindowsDiscovery discovery) {
        var power = typeof(WindowsDiscovery).GetMethod("HandlePowerEvent", BindingFlags.Instance | BindingFlags.NonPublic);
        if (power == null) throw new IOException("The exact backend has no owned power-boundary handler.");
        Action<int> boundary = value => { if (!(bool)power.Invoke(discovery, new object[] { (long)value })) throw new IOException("Owned power boundary was not recognized."); };
        var results = new List<object>();
        foreach (int value in new[] { 4, 18, 7 }) {
            if (!discovery.Scan().Complete) throw new IOException("Fresh pre-boundary native inventory is incomplete.");
            var originals = batch.Originals.ToDictionary(x => x.Path, x => Hash(x.Path)); string receipt = Hash(batch.ReceiptPath);
            long before = discovery.Epoch; int reports = 0; bool injected = false; IImportGuard oldGuard = null;
            var operations = new Operations(); string stopped;
            try {
                Verify(batch, current, discovery, CancellationToken.None, line => {
                    if (++reports == 2) { injected = true; boundary(value); }
                }, guard => oldGuard = guard, operations);
                throw new IOException("Saved verification accepted a retired destination proof.");
            } catch (TargetInvocationException ex) when (ex.InnerException is SafetyException) { stopped = ex.InnerException.Message; }
            if (!injected || oldGuard == null || reports != 2 || discovery.Epoch <= before) throw new IOException("Verification did not reach the owned power boundary after its first recorded file.");
            long retired = discovery.Epoch; bool suspended = value == 4;
            if (suspended && discovery.Scan().Complete) throw new IOException("Suspended native inventory still permits admission.");
            if (suspended) boundary(18);
            string oldRefusal;
            try { oldGuard.Validate(); throw new IOException("The old guard healed after resume."); }
            catch (SafetyException ex) { oldRefusal = ex.Message; }
            using (operations.Reserve(oldGuard.ProtectedDisks)) {}
            IImportGuard freshGuard = null;
            var retry = Verify(batch, current, discovery, CancellationToken.None, ready: guard => freshGuard = guard, operations: operations);
            if (retry.Length != 2 || retry.Any(line => line.Contains("unrelated.mov")) || freshGuard == null) throw new IOException("Fresh exact-history retry failed.");
            long freshEpoch = discovery.Epoch;
            if (value != 7) {
                boundary(7);
                if (discovery.Epoch != freshEpoch) throw new IOException("Paired user resume retired fresh history proof twice.");
                freshGuard.Validate();
            }
            using (operations.Reserve(freshGuard.ProtectedDisks)) {}
            if (Hash(batch.ReceiptPath) != receipt || originals.Any(x => Hash(x.Key) != x.Value)) throw new IOException("Owned power check changed saved originals or receipt.");
            results.Add(new { PowerEvent = value, EpochBefore = before, EpochRetired = retired, FreshEpoch = freshEpoch,
                InterruptedAfterFirstRecordedFile = true, ReportsBeforeRefusal = reports, SuspendedInventoryRejected = suspended,
                Refusal = stopped, OldGuardAfterResumeRefusal = oldRefusal, FreshRetryVerifiedFiles = retry.Length,
                PairedResumePreservedFreshProof = value != 7, SavedHashesUnchanged = true, ReservationsReleased = true });
        }
        return results.ToArray();
    }
    sealed class OwnedGuard : IImportGuard {
        readonly string source, destination;
        internal OwnedGuard(string source, string destination) { this.source = source; this.destination = destination; }
        public int[] ProtectedDisks { get { return new[] { 2147483001, 2147483002 }; } }
        public void Validate() { Ordinary(source); Ordinary(destination); }
    }
    static void Ordinary(string path) {
        FileSystemInfo item = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        while (item != null) {
            item.Refresh();
            if (!item.Exists || (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Missing or redirected fixture path: " + item.FullName);
            item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent;
        }
    }
    static string Hash(string path) { using (var stream = File.OpenRead(path)) return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    static void Create(string path, byte[] bytes) {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
    }
    static byte[] Json<T>(T value) { using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); return stream.ToArray(); } }
    static object Identity(string path) {
        var info = new FileInfo(path);
        return new { Path = info.FullName, Bytes = info.Length, CreationUtc = info.CreationTimeUtc.ToString("o"), Sha256 = Hash(path) };
    }
    [STAThread] static int Main(string[] args) {
        try {
            bool video = args.Length == 8 && args[0] == "--prepare-owned-native-video-history";
            if (!video && (args.Length != 4 || args[0] != "--seed-owned-native-history" && args[0] != "--prepare-owned-native-history" && args[0] != "--prepare-owned-native-history-power")) throw new ArgumentException("Usage: --prepare-owned-native-history (or explicit --prepare-owned-native-history-power / --seed-owned-native-history) NEW_FIXTURE_ROOT EXACT_CONFIG_ROOT EXPECTED_BACKEND_SHA256. Video preparation: --prepare-owned-native-video-history plus FFMPEG_EXE FFMPEG_SHA256 FFPROBE_EXE FFPROBE_SHA256; no video seed mode.");
            bool seed = args[0] == "--seed-owned-native-history";
            bool powerCheck = args[0] == "--prepare-owned-native-history-power";
            string backend = typeof(VerifiedImporter).Assembly.Location;
            if (!String.Equals(Hash(backend), args[3], StringComparison.OrdinalIgnoreCase)) throw new IOException("Loaded backend does not match the reviewed hash.");
            string root = Path.GetFullPath(args[1]);
            string allowed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectBuildTools") + Path.DirectorySeparatorChar;
            if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(root) || File.Exists(root)) throw new IOException("Fixture must be a new child of the owned EasyEjectBuildTools directory.");
            Ordinary(Path.GetDirectoryName(root));
            using var videoTools = video ? new NativeHistoryVideo(args[4], args[5], args[6], args[7]) : null;
            string config = Path.GetFullPath(args[2]);
            string ordinaryConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyEject", "Profiles");
            if (!String.Equals(config, Path.GetFullPath(ordinaryConfig), StringComparison.OrdinalIgnoreCase)) throw new IOException("Fixture supports only the explicit ordinary local Easy Eject configuration root.");
            // Use this exact backend's existing own-folder resolver for packaged
            // app-data redirection. Media paths retain the normal strict guards.
            var resolver = typeof(Profiles).GetProperty("Root", BindingFlags.Static | BindingFlags.NonPublic);
            if (resolver == null) throw new IOException("Reviewed backend configuration resolver is unavailable.");
            config = SafePaths.Normalize((string)resolver.GetValue(null));
            Ordinary(config);
            using var configScope = SafePaths.LockDirectories(config, Path.GetDirectoryName(root));
            var beforePaths = Directory.GetFiles(config).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            if (beforePaths.Any(x => Path.GetFileName(x) != "quick-share-tools.json" && Path.GetFileName(x) != "issue-state.json")) throw new IOException("Use only the reviewed empty-profile/history configuration. Existing preferences are retained.");
            foreach (var path in beforePaths) Ordinary(path);
            var beforeHashes = beforePaths.ToDictionary(x => x, Hash, StringComparer.OrdinalIgnoreCase);
            string id = "EasyEject-native-history-fixture-" + Guid.NewGuid().ToString("N");
            string profileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".json";
            string historyPath = Path.Combine(config, "completed-imports.json"), profilePath = Path.Combine(config, profileName);
            if (File.Exists(historyPath) || Directory.Exists(historyPath) || File.Exists(profilePath) || Directory.Exists(profilePath)) throw new IOException("Fixture configuration target already exists.");
            Directory.CreateDirectory(root);
            using var fixtureScope = SafePaths.LockDirectories(root);
            string source = Path.Combine(root, "source"), destination = Path.Combine(root, "destination");
            Directory.CreateDirectory(source); Directory.CreateDirectory(destination);
            foreach (var path in beforePaths) Create(Path.Combine(root, "before-" + Path.GetFileName(path)), File.ReadAllBytes(path));
            if (video) videoTools.Generate(root, source);
            else {
                Create(Path.Combine(source, "first.mov"), Encoding.UTF8.GetBytes("Easy Eject owned native-history fixture one\n"));
                Create(Path.Combine(source, "second.mov"), Encoding.UTF8.GetBytes("Easy Eject owned native-history fixture two\n"));
            }
            string volumeId = WindowsDiscovery.VolumeId(destination);
            var discovery = new WindowsDiscovery();
            using var monitor = new NativeMonitor(discovery);
            var state = discovery.Scan();
            var volume = state.Volumes.SingleOrDefault(x => x.Id == volumeId);
            if (!state.Complete || volume == null || !volume.System || volume.Error != "" || volume.Disks.Count != 1) throw new IOException("Fixture destination must map to the known protected internal Windows volume. " + String.Join("; ", state.Errors));
            var disk = state.Disks.Single(x => x.Number == volume.Disks[0]);
            if (disk.Error != "" || String.IsNullOrWhiteSpace(volume.Serial) || String.IsNullOrWhiteSpace(disk.InstanceId)) throw new IOException("Read-only destination identity is incomplete.");
            var identity = new DestinationIdentity { VolumeId = volume.Id, VolumeSerial = volume.Serial, DiskInstance = disk.InstanceId, DiskSerial = disk.Serial };
            var result = new VerifiedImporter(new Operations()).Run(source, source, destination, id, new OwnedGuard(source, destination), CancellationToken.None, Console.WriteLine, new MediaOptions(true, true, true, false, false, false));
            if (!result.HasSelectedMedia || result.Files != 2 || result.RemovedOriginals != 0 || result.Receipt.Files.Any(x => x.RemovedSource || x.FromTrash)) throw new IOException("Unexpected fixture import result.");
            var batch = new CompletedBatch { Id = result.Receipt.Id, ProfileId = id, DeviceName = video ? "Easy Eject generated video fixture" : "Easy Eject disposable history fixture", CompletedUtc = DateTime.UtcNow.ToString("o"),
                Folder = result.Folder, DestinationRoot = destination, ReceiptPath = result.ReceiptPath, ReceiptSha256 = Hash(result.ReceiptPath), Destination = identity,
                Originals = result.Receipt.Files.Where(x => x.IsMedia).Select(x => new VerifiedCopy { Path = Path.Combine(result.Folder, x.RelativePath), Sha256 = x.Sha256, Bytes = x.Bytes }).ToList() };
            if (batch.Originals.Count != 2 || batch.Originals.Any(x => Hash(x.Path) != x.Sha256 || new FileInfo(x.Path).Length != x.Bytes)) throw new IOException("Fixture receipt/copy verification failed.");
            var profile = new ImportProfile { Name = batch.DeviceName, VolumeId = id, VolumeSerial = "FIXTURE-" + id, DiskInstance = "fixture-only-device-" + id,
                RelativeSource = "", Destination = destination, SavedDestination = identity.Copy(), UsesImportDefaults = false, AutomaticImport = false,
                VideosOnly = true, IncludePreviews = true, CleanLayout = true, AskToEject = false, DeleteOriginals = false, RecoverTrash = false, SharingPreset = null, SharingColor = "standard" };
            Create(Path.Combine(result.Folder, "unrelated.mov"), Encoding.UTF8.GetBytes("Unrecorded fixture; saved history must never scan this file.\n"));
            // Make the original fixture folder unavailable without deleting originals.
            string retainedSource = Path.Combine(root, "retained-source"); Directory.Move(source, retainedSource);
            var verification = Verify(batch, identity, discovery, CancellationToken.None);
            if (Directory.Exists(source) || verification.Length != 2 || verification.Any(x => x.Contains("unrelated.mov"))) throw new IOException("Exact saved-batch verification did not match the source-absent fixture.");
            var changedDestination = identity.Copy(); changedDestination.DiskSerial += "-changed-fixture";
            string destinationRefusal;
            try { Verify(batch, changedDestination, discovery, CancellationToken.None); throw new IOException("Changed destination was accepted."); }
            catch (TargetInvocationException ex) when (ex.InnerException is SafetyException) { destinationRefusal = ex.InnerException.Message; }
            string savedOriginal = batch.Originals[0].Path;
            string retainedOriginal = Path.Combine(root, "retained-saved-original.mov");
            string rejectedOriginal = Path.Combine(root, "rejected-changed-original.mov");
            string originalRefusal;
            File.Move(savedOriginal, retainedOriginal);
            try {
                Create(savedOriginal, Encoding.UTF8.GetBytes("Changed generated fixture; verification must refuse.\n"));
                try { Verify(batch, identity, discovery, CancellationToken.None); throw new IOException("Changed original was accepted."); }
                catch (TargetInvocationException ex) when (ex.InnerException is SafetyException) { originalRefusal = ex.InnerException.Message; }
            } finally {
                // Retain both versions and restore only this exclusively owned copy.
                if (File.Exists(savedOriginal)) File.Move(savedOriginal, rejectedOriginal);
                File.Move(retainedOriginal, savedOriginal);
            }
            string cancellationRefusal;
            using (var cancelled = new CancellationTokenSource()) {
                cancelled.Cancel();
                try { Verify(batch, identity, discovery, cancelled.Token); throw new IOException("Pre-cancelled verification was accepted."); }
                catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException) { cancellationRefusal = ex.InnerException.Message; }
            }
            if (Verify(batch, identity, discovery, CancellationToken.None).Length != 2) throw new IOException("Fresh verification retry failed.");
            var powerVerification = powerCheck ? VerifyPowerBoundaries(batch, identity, discovery) : Array.Empty<object>();
            foreach (var path in beforePaths) if (Hash(path) != beforeHashes[path]) throw new IOException("Existing configuration changed; do not seed.");
            if (Directory.GetFiles(config).Length != beforePaths.Length) throw new IOException("Configuration gained unrelated files; do not seed.");
            byte[] profileBytes = Json(profile), historyBytes = Json(new List<CompletedBatch> { batch });
            // Retain provenance before either exclusive configuration write, including
            // a partial refusal if another process creates the second target first.
            Create(Path.Combine(root, "fixture-prepublication.json"), JsonSerializer.SerializeToUtf8Bytes(new {
                ProfilePath = profilePath, ProfileSha256 = Convert.ToHexString(SHA256.HashData(profileBytes)).ToLowerInvariant(),
                HistoryPath = historyPath, HistorySha256 = Convert.ToHexString(SHA256.HashData(historyBytes)).ToLowerInvariant(),
                TargetsInitiallyAbsent = true, BeforeFiles = beforePaths.Select(x => Identity(x)).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true }));
            string preparedProfile = Path.Combine(root, "prepared-profile.json"), preparedHistory = Path.Combine(root, "prepared-history.json");
            Create(preparedProfile, profileBytes); Create(preparedHistory, historyBytes);
            if (seed) { Create(profilePath, profileBytes); Create(historyPath, historyBytes); }
            var evidence = new { ObservedUtc = DateTime.UtcNow.ToString("o"), Backend = backend, BackendSha256 = Hash(backend), FixtureRoot = root, RequestedConfigRoot = args[2], ConfigRoot = config,
                Seeded = seed, PreparedProfile = Identity(preparedProfile), PreparedHistory = Identity(preparedHistory),
                Profile = seed ? Identity(profilePath) : null, History = seed ? Identity(historyPath) : null, Receipt = Identity(batch.ReceiptPath), OriginalCopies = batch.Originals.Select(x => Identity(x.Path)).ToArray(),
                RetainedSource = retainedSource, UnrecordedFile = Identity(Path.Combine(result.Folder, "unrelated.mov")), Destination = identity,
                NotificationsAvailable = discovery.NotificationsAvailable, InventoryComplete = state.Complete,
                Verification = verification, ChangedDestinationRefusal = destinationRefusal, ChangedOriginalRefusal = originalRefusal,
                RejectedChangedOriginal = Identity(rejectedOriginal), CancellationRefusal = cancellationRefusal, FreshRetryVerifiedFiles = 2,
                PowerVerification = powerVerification, PowerScope = powerCheck ? "Managed calls to this helper's owned discovery handler only; no OS broadcast, real sleep or existing-preview input" : null,
                BeforeFiles = beforePaths.Select(x => Identity(x)).ToArray(), VideoFixture = video,
                SuppliedTools = video ? new[] { Identity(Path.GetFullPath(args[4])), Identity(Path.GetFullPath(args[6])) } : null,
                Scope = video ? "Generated one-second 640x360 silent30fps clips, supplied hash-pinned tools/probe/full decode and injected Luna identity; fake source guard/reservations; read-only current internal destination; no live publication, source eject authority, official LUT or real camera/color claim" : "Generated text fixtures; fake source guard/reservations; read-only current internal destination identity; no source eject authority or real camera/video claim" };
            byte[] evidenceBytes = JsonSerializer.SerializeToUtf8Bytes(evidence, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
            Create(Path.Combine(root, "fixture-receipt.json"), evidenceBytes);
            Console.WriteLine(seed ? "Owned fixture seeded." : "Owned fixture prepared. Live profile/history configuration was not changed.");
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
