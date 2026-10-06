using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace EasyEject {
    public static class Tests {
        static int passed, failed;
        sealed class FakeDiscovery : IDiscovery { public Inventory State; public Inventory Scan() { return State; } public long Epoch { get { return State.Epoch; } } }
        sealed class FakeEject : IEjectApi { public int Calls; public EjectResult Result = new EjectResult { Success = true }; public EjectResult Request(uint node, string id) { Calls++; return Result; } }
        sealed class FakeGuard : IImportGuard { public int Calls, FailAt = Int32.MaxValue; public int[] ProtectedDisks { get { return new[] { 700, 701 }; } } public void Validate() { if (++Calls >= FailAt) throw new SafetyException("Fixture disconnected"); } }
        static void CreateJunction(Fixture fixture, string link, string target) {
            Check(SafePaths.Within(link, fixture.Root) && SafePaths.Within(target, fixture.Root), "Junction fixture must stay isolated");
            Directory.CreateDirectory(link);
            byte[] substitute = System.Text.Encoding.Unicode.GetBytes(@"\??\" + target), print = System.Text.Encoding.Unicode.GetBytes(target);
            byte[] data = new byte[16 + substitute.Length + 2 + print.Length + 2];
            Array.Copy(BitConverter.GetBytes(0xA0000003U), 0, data, 0, 4);
            Array.Copy(BitConverter.GetBytes((ushort)(data.Length - 8)), 0, data, 4, 2);
            Array.Copy(BitConverter.GetBytes((ushort)substitute.Length), 0, data, 10, 2);
            Array.Copy(BitConverter.GetBytes((ushort)(substitute.Length + 2)), 0, data, 12, 2);
            Array.Copy(BitConverter.GetBytes((ushort)print.Length), 0, data, 14, 2);
            Array.Copy(substitute, 0, data, 16, substitute.Length); Array.Copy(print, 0, data, 18 + substitute.Length, print.Length);
            using (var h = Native.CreateFile(link, 0x40000000, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero)) {
                if (h.IsInvalid) throw Native.Error("Cannot open isolated junction fixture");
                int returned;
                if (!Native.DeviceIoControl(h, 0x900A4, data, data.Length, null, 0, out returned, IntPtr.Zero)) throw Native.Error("Cannot create isolated junction fixture");
            }
        }
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static T Throws<T>(Action action) where T : Exception { try { action(); } catch (T ex) { return ex; } throw new Exception("Expected " + typeof(T).Name); }
        static void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); } }
        static Inventory State() {
            var state = new Inventory { Epoch = 1 };
            state.Disks.Add(new Disk { Number = 7, DevInst = 10, InstanceId = "disk-a", Serial = "serial-a", TargetId = "usb-a", TargetDevInst = 20, Ancestors = new List<string> { "disk-a", "usb-a" } });
            state.Volumes.Add(new Volume { Id = "volume-a", Serial = "ABC", Extents = "7:0:1000", Disks = new List<int> { 7 }, Mounts = new List<string> { @"E:\" } }); return state;
        }
        sealed class Fixture : IDisposable {
            public readonly string Root, Source, Destination;
            public Fixture() {
                Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectFixtures", "run-" + Guid.NewGuid().ToString("N").Substring(0, 16));
                Source = Path.Combine(Root, "source"); Destination = Path.Combine(Root, "destination"); Directory.CreateDirectory(Source); Directory.CreateDirectory(Destination);
            }
            public void Put(string name, string text) { string path = Path.Combine(Source, name); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text); }
            public ImportResult Run(Action<string> report) { return new VerifiedImporter(new Operations()).Run(Source, Source, Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, report); }
            public void Dispose() {
                string allowed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectFixtures");
                if (!SafePaths.Within(Path.GetFullPath(Root), Path.GetFullPath(allowed)) || Root == allowed) throw new Exception("Unsafe fixture cleanup path");
                Clean(Root);
            }
            static void Clean(string path) {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(path, false); return; }
                foreach (var file in Directory.GetFiles(path)) File.Delete(file);
                foreach (var folder in Directory.GetDirectories(path)) Clean(folder);
                Directory.Delete(path, false);
            }
        }
        [STAThread] public static int Main(string[] args) {
            if (args.Length == 1 && args[0] == "--ui-smoke") {
                using (var window = new DeviceWindow()) {
                    IntPtr handle = window.Handle;
                    var field = typeof(DeviceWindow).GetField("discovery", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var discovery = (WindowsDiscovery)field.GetValue(window); var state = discovery.Scan();
                    Check(handle != IntPtr.Zero && !window.Visible && discovery.NotificationsAvailable, "Hidden window / notifications unavailable");
                    Check(state.Complete && state.Volumes.Any(v => v.System), "Read-only UI inventory incomplete");
                    foreach (var v in state.Volumes.Where(v => v.System)) Throws<SafetyException>(() => EjectService.Plan(state, v.Id));
                    Console.WriteLine("PASS hidden device window initializes, disk notifications register, read-only inventory maps " + state.Volumes.Count + " volumes, and system eject is blocked.");
                    return 0;
                }
            }
            if (args.Length == 1 && args[0] == "--inventory") {
                var inventory = new WindowsDiscovery().Scan(); // Read-only mode: no notification window, hence no actions permitted.
                Console.WriteLine("READ ONLY: " + inventory.Disks.Count + " disks, " + inventory.Volumes.Count + " volumes");
                foreach (var v in inventory.Volumes) Console.WriteLine(v.Display + " | disks " + String.Join(",", v.Disks) + " | " + v.Classification + " | " + v.Error);
                foreach (var error in inventory.Errors) Console.WriteLine(error); return 0;
            }
            Test("plan includes all partitions", delegate { var s = State(); s.Volumes.Add(new Volume { Id = "v2", Serial = "DEF", Disks = new List<int> { 7 } }); var p = EjectService.Plan(s, "volume-a"); Check(p.Affected.Length == 2 && p.Disks.Length == 1, "Missing sibling"); });
            Test("plan includes sibling LUN under same removal target", delegate { var s = State(); s.Disks.Add(new Disk { Number = 8, InstanceId = "disk-b", Ancestors = new List<string> { "usb-a" } }); s.Volumes.Add(new Volume { Id = "v2", Disks = new List<int> { 8 } }); Check(EjectService.Plan(s, "volume-a").Disks.Length == 2, "Missing LUN"); });
            Test("unknown mapping blocks eject", delegate { var s = State(); s.Volumes[0].Disks.Clear(); Throws<SafetyException>(() => EjectService.Plan(s, "volume-a")); });
            Test("spanning volume blocks eject", delegate { var s = State(); s.Volumes[0].Disks.Add(8); Throws<SafetyException>(() => EjectService.Plan(s, "volume-a")); });
            Test("protected sibling blocks eject", delegate { var s = State(); s.Volumes.Add(new Volume { Id = "OS", System = true, Disks = new List<int> { 7 } }); Throws<SafetyException>(() => EjectService.Plan(s, "volume-a")); });
            Test("incomplete inventory blocks eject", delegate { var s = State(); s.Complete = false; Throws<SafetyException>(() => EjectService.Plan(s, "volume-a")); });
            Test("missing removable node blocks eject", delegate { var s = State(); s.Disks[0].TargetId = ""; Throws<SafetyException>(() => EjectService.Plan(s, "volume-a")); });
            Test("unknown sibling identity blocks eject", delegate { var s = State(); s.Volumes.Add(new Volume { Id = "v2", Disks = new List<int> { 7 }, Error = "locked" }); Throws<SafetyException>(() => EjectService.Plan(s, "volume-a")); });
            Test("unchanged plan calls fake eject once", delegate { var d = new FakeDiscovery { State = State() }; var a = new FakeEject(); var p = EjectService.Plan(d.State, "volume-a"); Check(new EjectService(d, a, new Operations()).Execute(p).Success && a.Calls == 1, "Wrong result"); });
            Test("disconnect/reconnect invalidates identical identities", delegate { var d = new FakeDiscovery { State = State() }; var a = new FakeEject(); var p = EjectService.Plan(d.State, "volume-a"); d.State.Epoch++; Throws<SafetyException>(() => new EjectService(d, a, new Operations()).Execute(p)); Check(a.Calls == 0, "Ejected stale connection"); });
            Test("new sibling invalidates old confirmation", delegate { var d = new FakeDiscovery { State = State() }; var a = new FakeEject(); var p = EjectService.Plan(d.State, "volume-a"); d.State.Volumes.Add(new Volume { Id = "v2", Disks = new List<int> { 7 } }); Throws<SafetyException>(() => new EjectService(d, a, new Operations()).Execute(p)); Check(a.Calls == 0, "Ejected unconfirmed sibling"); });
            Test("changed serial blocks old confirmation", delegate { var d = new FakeDiscovery { State = State() }; var a = new FakeEject(); var p = EjectService.Plan(d.State, "volume-a"); d.State.Volumes[0].Serial = "new"; Throws<SafetyException>(() => new EjectService(d, a, new Operations()).Execute(p)); Check(a.Calls == 0, "Wrong card ejected"); });
            Test("changed extent blocks old confirmation", delegate { var d = new FakeDiscovery { State = State() }; var p = EjectService.Plan(d.State, "volume-a"); d.State.Volumes[0].Extents = "7:1:1000"; Throws<SafetyException>(() => new EjectService(d, new FakeEject(), new Operations()).Execute(p)); });
            Test("veto reason and name reported", delegate { var d = new FakeDiscovery { State = State() }; var a = new FakeEject { Result = new EjectResult { Code = 0x17, Veto = 5, Name = "Fixture app" } }; var r = new EjectService(d, a, new Operations()).Execute(EjectService.Plan(d.State, "volume-a")); Check(!r.Success && r.Message.Contains("OutstandingOpen") && r.Message.Contains("Fixture app"), "Veto lost"); });
            Test("reservations prevent import/eject overlap and release", delegate { var ops = new Operations(); using (ops.Reserve(new[] { 7, 8 })) Throws<SafetyException>(() => ops.Reserve(new[] { 8 })); using (ops.Reserve(new[] { 8 })) {} });
            Test("different volumes on same physical disk cannot import", delegate { var s = State(); s.Volumes.Add(new Volume { Id = "destination", Serial = "other-partition", Disks = new List<int> { 7 } }); Throws<SafetyException>(() => VolumeImportGuard.PlanProtection(s, "volume-a", "destination")); });
            Test("separate LUN on same removable device cannot be destination", delegate { var s = State(); s.Disks.Add(new Disk { Number = 8, InstanceId = "disk-b", TargetId = "usb-a", Ancestors = new List<string> { "usb-a" } }); s.Volumes.Add(new Volume { Id = "destination", Serial = "other-LUN", Disks = new List<int> { 8 } }); Throws<SafetyException>(() => VolumeImportGuard.PlanProtection(s, "volume-a", "destination")); });
            Test("import reserves source sibling disks and destination", delegate { var s = State(); s.Disks.Add(new Disk { Number = 8, InstanceId = "disk-b", Ancestors = new List<string> { "usb-a" } }); s.Disks.Add(new Disk { Number = 0, InstanceId = "internal" }); s.Volumes.Add(new Volume { Id = "destination", Serial = "OS", System = true, Disks = new List<int> { 0 } }); Check(VolumeImportGuard.PlanProtection(s, "volume-a", "destination").OrderBy(x => x).SequenceEqual(new[] { 0, 7, 8 }), "Missing protected disk"); });
            Test("generic NVMe SSD never confirmed CFexpress", delegate { var d = new Disk { Model = "Generic NVMe SSD", Bus = 17, Hotplug = true }; Check(!Classifier.Classify(d, true).Contains("CFexpress") && Classifier.Classify(d, true).Contains("unconfirmed"), "Overconfident classifier"); });
            Test("internal disk with DCIM remains internal", delegate { Check(Classifier.Classify(new Disk { Model = "SSD" }, true) == "Internal / fixed storage", "Internal misclassified"); });
            Test("hardware SD is only a candidate", delegate { Check(Classifier.Classify(new Disk { Bus = 12, Removable = true }, false).Contains("candidate"), "No SD evidence"); });
            Test("path prefix boundary enforced", delegate { Check(!SafePaths.Within(@"C:\card-other\file", @"C:\card"), "Prefix escaped"); });
            Test("ambiguous and alternate stream paths rejected", delegate { Throws<SafetyException>(() => SafePaths.Normalize(@"C:\card\..\outside")); Throws<SafetyException>(() => SafePaths.Normalize(@"C:\card\file:stream")); Throws<SafetyException>(() => SafePaths.Normalize(@"C:\card\folder.\file")); Throws<SafetyException>(() => SafePaths.Normalize(@"\\server\card")); });
            Test("copy preserves relative paths, unknown files, originals and receipts", delegate {
                using (var f = new Fixture()) { f.Put(@"DCIM\100\clip.mov", "video fixture"); f.Put(@"DCIM\100\clip.xml", "sidecar"); f.Put("unknown.data", "unknown"); var r = f.Run(x => {});
                    Check(r.Files == 3 && File.ReadAllText(Path.Combine(r.Folder, @"Camera originals\DCIM\100\clip.mov")) == "video fixture", "Bad copy"); Check(File.Exists(Path.Combine(r.Folder, "verified-import.json")) && File.Exists(Path.Combine(f.Source, "unknown.data")), "Receipt/original missing"); }
            });
            Test("repeat import never overwrites earlier files", delegate { using (var f = new Fixture()) { f.Put("clip.mov", "first"); var a = f.Run(x => {}); f.Put("clip.mov", "second"); var b = f.Run(x => {}); Check(a.Folder != b.Folder && File.ReadAllText(Path.Combine(a.Folder, @"Camera originals\clip.mov")) == "first", "Overwritten"); } });
            Test("source/destination nesting rejected", delegate { using (var f = new Fixture()) { Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, Path.Combine(f.Source, "output"), "fixture", new FakeGuard(), CancellationToken.None, x => {})); } });
            Test("partial package selection rejected", delegate { using (var f = new Fixture()) { string clip = Path.Combine(f.Source, "CLIP"); Directory.CreateDirectory(clip); Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, clip, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {})); } });
            Test("empty source creates no batch or completion receipt", delegate { using (var f = new Fixture()) { Check(f.Run(x => {}).Files == 0 && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Empty misleading batch"); } });
            Test("cancellation leaves originals and no completion receipt", delegate { using (var f = new Fixture()) { f.Put("clip.mov", new string('x', 2000000)); var token = new CancellationTokenSource(); Throws<OperationCanceledException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), token.Token, x => token.Cancel())); Check(File.Exists(Path.Combine(f.Source, "clip.mov")) && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0, "Cancelled state unsafe"); } });
            Test("identity loss leaves originals and incomplete batch", delegate { using (var f = new Fixture()) { f.Put("clip.mov", "media"); var guard = new FakeGuard { FailAt = 3 }; var error = Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", guard, CancellationToken.None, x => {})); Check(guard.Calls == 3 && error.Message == "Fixture disconnected" && File.Exists(Path.Combine(f.Source, "clip.mov")) && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Identity-loss state unsafe"); } });
            Test("destination conflict never overwritten", delegate { using (var f = new Fixture()) { f.Put("clip.mov", "new-media"); Throws<System.ComponentModel.Win32Exception>(() => f.Run(x => { if (x.StartsWith("Copying")) { string batch = Directory.GetDirectories(f.Destination).Single(); File.WriteAllText(Path.Combine(batch, @"Camera originals\clip.mov"), "existing"); } })); string saved = Directory.GetFiles(f.Destination, "clip.mov", SearchOption.AllDirectories).Single(); Check(File.ReadAllText(saved) == "existing", "Conflict overwritten"); } });
            Test("source cannot be modified while copied", delegate { using (var f = new Fixture()) { f.Put("clip.mov", "original"); bool denied = false; var r = f.Run(x => { if (x.StartsWith("Copying")) { try { File.WriteAllText(Path.Combine(f.Source, "clip.mov"), "changed"); } catch (IOException) { denied = true; } } }); Check(denied && r.Files == 1, "Source not pinned"); } });
            Test("earlier saved copy tampering invalidates completion", delegate { using (var f = new Fixture()) { f.Put("a.mov", "first"); f.Put("b.mov", "second"); bool changed = false; var error = Throws<SafetyException>(() => f.Run(x => { if (x == "Copying b.mov") { string batch = Directory.GetDirectories(f.Destination).Single(); File.WriteAllText(Path.Combine(batch, @"Camera originals\a.mov"), "tampered"); changed = true; } })); Check(changed && error.Message.Contains("earlier saved copy changed") && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Tampering marked complete or test never reached tampering"); } });
            Test("earlier source changes invalidate completion", delegate { using (var f = new Fixture()) { f.Put("a.mov", "first"); f.Put("b.mov", "second"); bool changed = false; var error = Throws<SafetyException>(() => f.Run(x => { if (x == "Copying b.mov") { File.WriteAllText(Path.Combine(f.Source, "a.mov"), "changed"); changed = true; } })); Check(changed && error.Message.Contains("source recording changed") && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Changed source marked complete or test never reached source change"); } });
            Test("directory rename is denied while source path is pinned", delegate { using (var f = new Fixture()) { f.Put(@"DCIM\clip.mov", "media"); bool denied = false; f.Run(x => { if (x.StartsWith("Copying")) { try { Directory.Move(Path.Combine(f.Source, "DCIM"), Path.Combine(f.Source, "renamed")); } catch (IOException) { denied = true; } } }); Check(denied, "Source directory not pinned"); } });
            Test("source junction escape rejected", delegate { using (var f = new Fixture()) {
                string outside = Path.Combine(f.Root, "outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "private.mov"), "outside"); string link = Path.Combine(f.Source, "link");
                CreateJunction(f, link, outside);
                var error = Throws<SafetyException>(() => f.Run(x => {})); Check(error.Message.Contains("reparse points") && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Link imported or wrong rejection cause");
            } });
            Test("destination ancestor junction rejected before copying", delegate { using (var f = new Fixture()) { f.Put("clip.mov", "media"); string outside = Path.Combine(f.Root, "outside"); Directory.CreateDirectory(outside); string link = Path.Combine(f.Destination, "link"); CreateJunction(f, link, outside); var error = Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, link, "fixture", new FakeGuard(), CancellationToken.None, x => {})); Check(error.Message.Contains("Reparse points") && Directory.GetFileSystemEntries(outside).Length == 0, "Destination escaped or wrong rejection cause"); } });
            Test("profile survives replacement and rejects a reformatted card", delegate { using (var f = new Fixture()) { var s = State(); string root = Path.Combine(f.Root, "profiles"); var p = new ImportProfile { VolumeId = s.Volumes[0].Id, VolumeSerial = s.Volumes[0].Serial, DiskInstance = s.Disks[0].InstanceId, RelativeSource = "DCIM", Destination = f.Destination }; Profiles.Save(p, root); Check(Profiles.Load(s.Volumes[0], s.Disks[0], root).RelativeSource == "DCIM", "Profile roundtrip failed"); p.RelativeSource = "PRIVATE"; Profiles.Save(p, root); Check(Profiles.Load(s.Volumes[0], s.Disks[0], root).RelativeSource == "PRIVATE", "Profile replacement failed"); s.Volumes[0].Serial = "formatted"; Throws<SafetyException>(() => Profiles.Load(s.Volumes[0], s.Disks[0], root)); } });
            Test("binary and empty media copies have complete verified receipts", delegate { using (var f = new Fixture()) {
                byte[] bytes = new byte[2097275]; for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 31 + 7);
                File.WriteAllBytes(Path.Combine(f.Source, "large.mov"), bytes); File.WriteAllBytes(Path.Combine(f.Source, "empty.xmp"), new byte[0]); var result = f.Run(x => {});
                Check(result.Files == 2 && result.Bytes == bytes.Length, "Incorrect large/empty byte counts");
                Check(File.ReadAllBytes(Path.Combine(result.Folder, @"Camera originals\large.mov")).SequenceEqual(bytes), "Large copy differs or lost its tail");
                ImportReceipt receipt; using (var file = File.OpenRead(Path.Combine(result.Folder, "verified-import.json"))) receipt = (ImportReceipt)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(ImportReceipt)).ReadObject(file);
                string expected; using (var hash = System.Security.Cryptography.SHA256.Create()) expected = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                Check(receipt.OriginalsKept && receipt.SourceVolume == "fixture-volume" && receipt.Files.Count == 2 && receipt.Files.Single(x => x.Bytes == bytes.Length).Sha256 == expected && receipt.Files.Single(x => x.Bytes == 0).Sha256 == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "Receipt is incomplete or hashes are incorrect");
            } });
            Test("production configuration storage supports package redirection", delegate {
                var state = State(); state.Volumes[0].Id = "fixture-only-" + Guid.NewGuid().ToString("N"); string root = Profiles.Root;
                string name; using (var hash = System.Security.Cryptography.SHA256.Create()) name = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(state.Volumes[0].Id))).Replace("-", "") + ".json";
                string path = Path.Combine(root, name); Check(!File.Exists(path), "Disposable profile already exists");
                using (SafePaths.LockDirectories(root)) {
                    try { Profiles.Save(new ImportProfile { VolumeId = state.Volumes[0].Id, VolumeSerial = state.Volumes[0].Serial, DiskInstance = state.Disks[0].InstanceId, RelativeSource = "DCIM", Destination = @"Z:\fixture-only" }); Check(Profiles.Load(state.Volumes[0], state.Disks[0]).RelativeSource == "DCIM", "Production configuration roundtrip failed"); }
                    finally { if (File.Exists(path)) { Check((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 && SafePaths.Within(path, root), "Unsafe disposable profile cleanup"); File.Delete(path); } }
                }
            });
            Console.WriteLine(passed + " passed; " + failed + " failed. No real device eject API was used."); return failed == 0 ? 0 : 1;
        }
    }
}
