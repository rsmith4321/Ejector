using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;
using EasyEject;

// Opt-in source fixture, excluded from the app and default contracts. It invokes
// only its own handlers. No OS power requests, broadcasts, media or eject APIs.
internal static class PowerConnectionProbe {
    const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Assembly Backend = typeof(WindowsDiscovery).Assembly;
    static readonly List<object> Observations = new List<object>();
    static int passed, failed;

    sealed class OwnedHandler : IDisposable {
        readonly object owner;
        readonly MethodInfo wndProc;
        readonly IDisposable lifetime;
        internal readonly WindowsDiscovery Discovery;
        internal readonly IntPtr Handle;
        internal OwnedHandler(string kind) {
            Type type = Backend.GetType(kind == "modern" ? "EasyEject.StorageNotificationWindow" : "EasyEject.DeviceWindow", true);
            if (kind == "modern") {
                Discovery = new WindowsDiscovery();
                owner = Activator.CreateInstance(type, HiddenInstance, null, new object[] { Discovery, new Func<bool>(() => false) }, null);
                lifetime = (IDisposable)owner;
                try {
                    Invoke(type.GetMethod("Start", HiddenInstance), owner);
                    Handle = ((NativeWindow)owner).Handle;
                } catch { lifetime.Dispose(); throw; }
            } else {
                owner = Activator.CreateInstance(type, HiddenInstance, null, new object[] { true }, null);
                lifetime = (IDisposable)owner;
                try {
                    // The read-only classic constructor owns a tray icon. Hide
                    // that fixture icon immediately; never show or run the form.
                    ((NotifyIcon)type.GetField("tray", HiddenInstance).GetValue(owner)).Visible = false;
                    Discovery = (WindowsDiscovery)type.GetField("discovery", HiddenInstance).GetValue(owner);
                    Handle = ((Form)owner).Handle;
                } catch { lifetime.Dispose(); throw; }
            }
            wndProc = type.GetMethod("WndProc", HiddenInstance);
            if (Handle == IntPtr.Zero || wndProc == null || !Discovery.NotificationsAvailable || !TrackingAvailable(Discovery)) {
                lifetime.Dispose(); throw new IOException("Owned native notification registration is unavailable.");
            }
        }
        internal void Power(int powerEvent) {
            // Direct managed invocation, never SendMessage/PostMessage or HWND_BROADCAST.
            object[] arguments = { Message.Create(Handle, 0x218, new IntPtr(powerEvent), IntPtr.Zero) };
            Invoke(wndProc, owner, arguments);
        }
        public void Dispose() { lifetime.Dispose(); }
    }

    static object Invoke(MethodInfo method, object target, params object[] arguments) {
        if (method == null) throw new MissingMethodException("The exact backend fixture contract is missing.");
        try { return method.Invoke(target, arguments); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw;
        }
    }
    static object Versions(WindowsDiscovery discovery) {
        object instances = typeof(WindowsDiscovery).GetField("Instances", HiddenInstance).GetValue(discovery);
        return instances.GetType().GetField("Versions", HiddenInstance).GetValue(instances);
    }
    static bool TrackingAvailable(WindowsDiscovery discovery) {
        object versions = Versions(discovery);
        return (bool)versions.GetType().GetProperty("Available", HiddenInstance).GetValue(versions);
    }
    static void FaultTracking(WindowsDiscovery discovery) {
        object versions = Versions(discovery);
        Invoke(versions.GetType().GetMethod("Disable", HiddenInstance), versions);
    }
    static object Capture(WindowsDiscovery discovery, Inventory state, int disk) {
        return Activator.CreateInstance(Backend.GetType("EasyEject.StorageConnectionProof", true), HiddenInstance,
            null, new object[] { discovery, state, new[] { disk } }, null);
    }
    static bool Validate(object proof, Inventory state) {
        try { Invoke(proof.GetType().GetMethod("Validate", HiddenInstance), proof, state); return true; }
        catch (SafetyException) { return false; }
    }
    static bool Scoped(object proof) { return (bool)proof.GetType().GetProperty("Scoped", HiddenInstance).GetValue(proof); }
    static Disk InternalDisk(Inventory state) {
        string windowsVolume = WindowsDiscovery.VolumeId(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        var volume = state.Volumes.SingleOrDefault(v => v.Id.Equals(windowsVolume, StringComparison.OrdinalIgnoreCase));
        if (!state.Complete || volume == null || !volume.System || volume.Error != "" || volume.Disks.Count != 1)
            throw new IOException("Read-only Windows system-volume mapping is incomplete: " + String.Join("; ", state.Errors));
        var disk = state.Disks.Single(d => d.Number == volume.Disks[0]);
        if (disk.Error != "" || String.IsNullOrWhiteSpace(disk.InstanceId)) throw new IOException("Internal disk identity is unavailable.");
        return disk;
    }
    static string Group(Disk disk) { return disk.TargetId == "" ? disk.InstanceId : disk.TargetId; }
    static void Case(string name, Func<object> action) {
        try { object observation = action(); passed++; Observations.Add(new { Name = name, Passed = true, Observation = observation }); }
        catch (ProbeFailure ex) { failed++; Observations.Add(new { Name = name, Passed = false, Observation = ex.Observation, Error = ex.Message }); }
        catch (Exception ex) { failed++; Observations.Add(new { Name = name, Passed = false, Error = ex.ToString(), AdmissionOrExecutionFailure = true }); }
    }
    sealed class ProbeFailure : Exception {
        internal readonly object Observation;
        internal ProbeFailure(string message, object observation) : base(message) { Observation = observation; }
    }
    static object Check(bool success, string message, object observation) {
        if (!success) throw new ProbeFailure(message, observation); return observation;
    }
    static void Boundary(string kind, int powerEvent) {
        Case(kind + " power boundary " + powerEvent, () => {
            using var handler = new OwnedHandler(kind);
            var before = handler.Discovery.Scan(); var disk = InternalDisk(before);
            var proof = Capture(handler.Discovery, before, disk.Number);
            if (!Scoped(proof) || !Validate(proof, before)) throw new IOException("Initial native scoped proof is unavailable.");
            string stampBefore = before.ConnectionStamp(Group(disk)); long epochBefore = before.Epoch;
            handler.Power(powerEvent);
            var after = handler.Discovery.Scan(); bool oldAccepted = Validate(proof, after);
            bool suspendedAdmissionBlocked = powerEvent != 4 || !after.Complete;
            string stampAfter = after.ConnectionStamp(Group(disk));
            if (powerEvent == 4) handler.Power(18);
            var resumed = handler.Discovery.Scan();
            bool oldAcceptedAfterResume = Validate(proof, resumed);
            var fresh = Capture(handler.Discovery, resumed, disk.Number);
            bool freshAccepted = Scoped(fresh) && Validate(fresh, resumed);
            var observation = new {
                Event = powerEvent, EpochBefore = epochBefore, EpochAfter = after.Epoch, EpochResumed = resumed.Epoch,
                StampBefore = stampBefore, StampAfter = stampAfter, StampResumed = resumed.ConnectionStamp(Group(disk)),
                CompleteAfter = after.Complete, OldProofAccepted = oldAccepted, OldProofAcceptedAfterResume = oldAcceptedAfterResume,
                SuspendedAdmissionBlocked = suspendedAdmissionBlocked, FreshPostResumeProofAccepted = freshAccepted,
                AfterErrors = after.Errors.ToArray(), ResumeErrors = resumed.Errors.ToArray()
            };
            return Check(after.Epoch > epochBefore && !oldAccepted && !oldAcceptedAfterResume && suspendedAdmissionBlocked && freshAccepted,
                "Power boundary did not retire the old proof, block suspended admission and permit a fresh post-resume proof.", observation);
        });
    }
    static void Controls(string kind) {
        Case(kind + " ordinary charger status keeps current proof", () => {
            using var handler = new OwnedHandler(kind);
            var before = handler.Discovery.Scan(); var disk = InternalDisk(before); var proof = Capture(handler.Discovery, before, disk.Number);
            if (!Scoped(proof)) throw new IOException("Initial native scoped proof is unavailable.");
            handler.Power(10); var after = handler.Discovery.Scan();
            bool accepted = Validate(proof, after);
            var observation = new { EpochBefore = before.Epoch, EpochAfter = after.Epoch, OldProofAccepted = accepted,
                StampBefore = before.ConnectionStamp(Group(disk)), StampAfter = after.ConnectionStamp(Group(disk)) };
            return Check(after.Epoch == before.Epoch && accepted, "Ordinary power status invalidated a current proof.", observation);
        });
        Case(kind + " global epoch alone still permits scoped revalidation", () => {
            using var handler = new OwnedHandler(kind);
            var before = handler.Discovery.Scan(); var disk = InternalDisk(before); var proof = Capture(handler.Discovery, before, disk.Number);
            if (!Scoped(proof)) throw new IOException("Initial native scoped proof is unavailable.");
            handler.Discovery.ConnectionChanged(); var after = handler.Discovery.Scan(); bool accepted = Validate(proof, after);
            return Check(after.Epoch > before.Epoch && accepted, "Unrelated epoch control failed.", new {
                EpochBefore = before.Epoch, EpochAfter = after.Epoch, OldProofAccepted = accepted,
                StampBefore = before.ConnectionStamp(Group(disk)), StampAfter = after.ConnectionStamp(Group(disk))
            });
        });
        Case(kind + " resume preserves a tracking fault", () => {
            using var handler = new OwnedHandler(kind);
            var before = handler.Discovery.Scan(); var disk = InternalDisk(before); var proof = Capture(handler.Discovery, before, disk.Number);
            if (!Scoped(proof)) throw new IOException("Initial native scoped proof is unavailable.");
            FaultTracking(handler.Discovery); handler.Power(18); var after = handler.Discovery.Scan();
            bool available = TrackingAvailable(handler.Discovery), accepted = Validate(proof, after);
            return Check(!available && !accepted, "Resume restored faulted tracking or accepted its old proof.", new {
                TrackingAvailable = available, OldProofAccepted = accepted, InventoryComplete = after.Complete
            });
        });
        Case(kind + " fixture disposal closes notification admission", () => {
            var handler = new OwnedHandler(kind); var discovery = handler.Discovery;
            try { InternalDisk(discovery.Scan()); } finally { handler.Dispose(); }
            var after = discovery.Scan();
            return Check(!discovery.NotificationsAvailable && !TrackingAvailable(discovery) && !after.Complete,
                "Disposed fixture retained notification admission.", new {
                    NotificationsAvailable = discovery.NotificationsAvailable, TrackingAvailable = TrackingAvailable(discovery), InventoryComplete = after.Complete
                });
        });
    }
    static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    static void Ordinary(string path) {
        FileSystemInfo item = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        while (item != null) {
            item.Refresh();
            if (!item.Exists || (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Configuration must be an existing ordinary path: " + item.FullName);
            item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent;
        }
    }
    static Dictionary<string, string> ConfigurationSnapshot(string root) {
        Ordinary(root);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) {
            Ordinary(path);
            if (Directory.Exists(path)) throw new IOException("Use the reviewed configuration containing only ordinary direct records.");
            result.Add(Path.GetFileName(path), Hash(path));
        }
        return result;
    }
    [STAThread] static int Main(string[] args) {
        try {
            if (args.Length != 3 || args[0] != "--verify-owned-power-boundaries")
                throw new IOException("Explicit opt-in, an existing canonical configuration root and the exact backend SHA-256 are required.");
            string backendPath = Backend.Location, backendHash = Hash(backendPath);
            if (args[2].Length != 64 || !backendHash.Equals(args[2], StringComparison.OrdinalIgnoreCase))
                throw new IOException("Loaded backend does not match the reviewed hash.");
            string config = Path.GetFullPath(args[1]); var before = ConfigurationSnapshot(config);
            // Require an existing ordinary configuration before resolving the
            // backend's own packaged app-data path. Never create fixture records.
            string ordinaryConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyEject", "Profiles");
            if (!Directory.Exists(ordinaryConfig)) throw new IOException("The normal configuration is absent; do not create it for this probe.");
            string resolved = (string)typeof(Profiles).GetProperty("Root", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            if (!Path.GetFullPath(resolved).Equals(config, StringComparison.OrdinalIgnoreCase)) throw new IOException("Configuration root does not match this exact backend's existing resolver.");
            try {
                foreach (string kind in new[] { "modern", "classic" }) {
                    Controls(kind);
                    foreach (int powerEvent in new[] { 4, 18, 7 }) Boundary(kind, powerEvent);
                }
            } finally {
                var after = ConfigurationSnapshot(config);
                bool unchanged = before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out string hash) && hash == pair.Value);
                Case("existing configuration records unchanged", () => Check(unchanged, "Existing configuration changed; preserve all evidence and inspect it.",
                    new { Before = before, After = after }));
            }
            Console.WriteLine(JsonSerializer.Serialize(new {
                ObservedUtc = DateTime.UtcNow.ToString("o"), BackendPath = backendPath, BackendSha256 = backendHash,
                ConfigurationRoot = config, Passed = passed, Failed = failed, Cases = Observations,
                Scope = "Owned synthetic managed power-message handlers and read-only native inventory; no real sleep, device removal, media or existing app control"
            }, new JsonSerializerOptions { WriteIndented = true }));
            return failed == 0 ? 0 : 1;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
