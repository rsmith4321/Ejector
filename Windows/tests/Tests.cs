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
        sealed class InterruptedGuard : IImportGuard, IConnectionGuard {
            readonly Fixture fixture; readonly CancellationTokenSource cancellation; int checks; public long WrittenBeforeStop;
            public InterruptedGuard(Fixture fixture, CancellationTokenSource cancellation) { this.fixture = fixture; this.cancellation = cancellation; }
            public int[] ProtectedDisks { get { return new[] { 700, 701 }; } }
            public bool Changed { get {
                if (++checks != 2) return false;
                string pending = Directory.GetFiles(fixture.Destination, "*.partial", SearchOption.AllDirectories).Single();
                WrittenBeforeStop = new FileInfo(pending).Length;
                if (cancellation != null) cancellation.Cancel();
                return true;
            } }
            public void Validate() {
                if (WrittenBeforeStop == 0) return;
                if (cancellation != null) cancellation.Token.ThrowIfCancellationRequested();
                throw new SafetyException("Fixture disconnected during transfer");
            }
        }
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
        static string Saved(ImportResult result, string sourceRelative) { return Path.Combine(result.Folder, result.Receipt.Files.Single(x => x.SourceRelativePath == sourceRelative).RelativePath); }
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
            if (args.Length == 1 && args[0] == "--ui-render") {
                string output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectWindowsPreview", "run-" + Guid.NewGuid().ToString("N").Substring(0, 16));
                Directory.CreateDirectory(output); var state = State(); var defaults = new ImportDefaults(); var profile = defaults.NewProfile(state.Volumes[0], state.Disks[0]); profile.RelativeSource = "DCIM"; profile.Destination = @"D:\Travel backups";
                using (var dialog = new ImportProfileDialog(profile, @"E:\", "Fixture Insta360 card · E:\\", defaults, true)) {
                    dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "profile-defaults.png"));
                    dialog.UseCustom.Checked = true; dialog.Selection.Previews.Checked = false; Render(dialog, Path.Combine(output, "profile-custom.png"));
                }
                using (var dialog = new ImportDefaultsDialog(defaults)) { dialog.Size = dialog.MinimumSize; Render(dialog, Path.Combine(output, "shared-defaults.png")); }
                using (var window = new DeviceWindow()) { window.Size = window.MinimumSize; Render(window, Path.Combine(output, "device-window.png")); }
                Console.WriteLine("Hidden forms rendered at minimum size. No device operations: " + output); return 0;
            }
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
                    Check(r.Files == 3 && File.ReadAllText(Saved(r, @"DCIM\100\clip.mov")) == "video fixture" && r.Receipt.Files.All(x => x.RelativePath.StartsWith("Camera originals")) && Saved(r, @"DCIM\100\clip.mov").EndsWith(@"DCIM\100\clip.mov"), "Bad copy or unfamiliar structure flattened"); Check(File.Exists(r.ReceiptPath) && File.Exists(Path.Combine(f.Source, "unknown.data")), "Receipt/original missing"); }
            });
            Test("repeat import never overwrites earlier files", delegate { using (var f = new Fixture()) { f.Put("clip.mov", "first"); var a = f.Run(x => {}); f.Put("clip.mov", "second"); var b = f.Run(x => {}); Check(a.Folder == b.Folder && Saved(a, "clip.mov") != Saved(b, "clip.mov") && File.ReadAllText(Saved(a, "clip.mov")) == "first" && File.ReadAllText(Saved(b, "clip.mov")) == "second" && Saved(b, "clip.mov").Contains("Additional media"), "Overwritten or conflict group lost"); } });
            Test("source/destination nesting rejected", delegate { using (var f = new Fixture()) { Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, Path.Combine(f.Source, "output"), "fixture", new FakeGuard(), CancellationToken.None, x => {})); } });
            Test("partial package selection rejected", delegate { using (var f = new Fixture()) { string clip = Path.Combine(f.Source, "CLIP"); Directory.CreateDirectory(clip); Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, clip, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {})); } });
            Test("empty source creates no batch or completion receipt", delegate { using (var f = new Fixture()) { Check(f.Run(x => {}).Files == 0 && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Empty misleading batch"); } });
            Test("cancellation leaves originals and no completion receipt", delegate { using (var f = new Fixture()) { f.Put("clip.mov", new string('x', 2000000)); var token = new CancellationTokenSource(); Throws<OperationCanceledException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), token.Token, x => token.Cancel())); Check(File.Exists(Path.Combine(f.Source, "clip.mov")) && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0, "Cancelled state unsafe"); } });
            foreach (bool cancelled in new[] { false, true }) {
                Test(cancelled ? "mid-transfer cancellation cleans partials and releases disks" : "mid-transfer disconnection cleans partials and releases disks", delegate { using (var f = new Fixture()) using (var token = new CancellationTokenSource()) {
                    byte[] bytes = new byte[3145851]; for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 13 + 3);
                    string original = Path.Combine(f.Source, "clip.mov"); File.WriteAllBytes(original, bytes);
                    var operations = new Operations(); var guard = new InterruptedGuard(f, cancelled ? token : null);
                    Action run = () => new VerifiedImporter(operations).Run(f.Source, f.Source, f.Destination, "fixture", guard, token.Token, x => {});
                    if (cancelled) Throws<OperationCanceledException>(run);
                    else Check(Throws<SafetyException>(run).Message == "Fixture disconnected during transfer", "Wrong interruption cause");
                    Check(guard.WrittenBeforeStop == 1048576 && File.ReadAllBytes(original).SequenceEqual(bytes), "Test did not interrupt after a copied chunk or source changed");
                    Check(Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Destination, "clip.mov", SearchOption.AllDirectories).Length == 0, "Interrupted batch was published or partial retained");
                    var retry = new VerifiedImporter(operations).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {});
                    Check(retry.Files == 1 && retry.Bytes == bytes.Length && File.ReadAllBytes(Saved(retry, "clip.mov")).SequenceEqual(bytes), "Disk reservation was not released or retry was incorrect");
                } });
            }
            Test("identity loss leaves originals and incomplete batch", delegate { using (var f = new Fixture()) { f.Put("clip.mov", "media"); var guard = new FakeGuard { FailAt = 3 }; var error = Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", guard, CancellationToken.None, x => {})); Check(guard.Calls == 3 && error.Message == "Fixture disconnected" && File.Exists(Path.Combine(f.Source, "clip.mov")) && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Identity-loss state unsafe"); } });
            Test("destination conflict never overwritten", delegate { using (var f = new Fixture()) { f.Put("clip.mov", "new-media"); Throws<System.ComponentModel.Win32Exception>(() => f.Run(x => { if (x.StartsWith("Copying")) { string batch = Directory.GetDirectories(f.Destination).Single(); File.WriteAllText(Path.Combine(batch, "clip.mov"), "existing"); } })); string saved = Directory.GetFiles(f.Destination, "clip.mov", SearchOption.AllDirectories).Single(); Check(File.ReadAllText(saved) == "existing", "Conflict overwritten"); } });
            Test("source cannot be modified while copied", delegate { using (var f = new Fixture()) { f.Put("clip.mov", "original"); bool denied = false; var r = f.Run(x => { if (x.StartsWith("Copying")) { try { File.WriteAllText(Path.Combine(f.Source, "clip.mov"), "changed"); } catch (IOException) { denied = true; } } }); Check(denied && r.Files == 1, "Source not pinned"); } });
            Test("earlier saved copy tampering invalidates completion", delegate { using (var f = new Fixture()) { f.Put("a.mov", "first"); f.Put("b.mov", "second"); bool changed = false; var error = Throws<SafetyException>(() => f.Run(x => { if (x == "Copying b.mov") { string batch = Directory.GetDirectories(f.Destination).Single(); File.WriteAllText(Path.Combine(batch, "a.mov"), "tampered"); changed = true; } })); Check(changed && error.Message.Contains("earlier saved copy changed") && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Tampering marked complete or test never reached tampering"); } });
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
                Check(File.ReadAllBytes(Saved(result, "large.mov")).SequenceEqual(bytes), "Large copy differs or lost its tail");
                ImportReceipt receipt; using (var file = File.OpenRead(result.ReceiptPath)) receipt = (ImportReceipt)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(ImportReceipt)).ReadObject(file);
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
            Test("video selection preserves still pairs and ambiguous companions", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { "photo.JPG", "photo.MOV", "clip.mp4", "clip.mp4.xmp", "clip.xmp", "clip.bin", "orphan.LRV", "unknown.raw" }) f.Put(name, name);
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, true));
                var saved = result.Receipt.Files.Select(x => Path.GetFileName(Path.Combine(result.Folder, x.RelativePath))).OrderBy(x => x).ToArray();
                Check(result.HasSelectedMedia && saved.SequenceEqual(new[] { "clip.mp4", "clip.mp4.xmp" }), "Live Photo, orphan preview or ambiguous companion selected");
                Check(Directory.GetFiles(f.Source).Length == 8, "Selection deleted originals");
            } });
            Test("Insta360 lens pairs remain intact with previews disabled", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { "VID_20261006_000001_00_001.insv", "VID_20261006_000001_10_001.insv", "LRV_20261006_000001_01_001.lrv", "VID_20261006_000001_00_001.thm" }) f.Put(name, name);
                var withPreviews = MediaSelection.Plan(VerifiedImporter.Scan(f.Source, CancellationToken.None), f.Source, f.Source, new MediaOptions(true, true));
                Check(withPreviews.Media.Count == 4, "Associated lens preview or thumbnail omitted by default");
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, false));
                Check(result.Files == 2 && Directory.GetFiles(result.Folder, "*.insv", SearchOption.AllDirectories).Length == 2 && Directory.GetFiles(result.Folder, "*.lrv", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Source).Length == 4, "Lens names changed, preview copied or originals removed");
            } });
            Test("Sony package keeps audio metadata and required previews", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { @"PRIVATE\M4ROOT\CLIP\clip.MP4", @"PRIVATE\M4ROOT\CLIP\clip.XML", @"PRIVATE\M4ROOT\THMBNL\clip.JPG", @"PRIVATE\M4ROOT\AUDIO\clip.WAV", @"DCIM\photo.JPG", @"DCIM\photo.MOV" }) f.Put(name, name);
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, false));
                Check(result.Files == 4 && File.Exists(Saved(result, @"PRIVATE\M4ROOT\THMBNL\clip.JPG")) && result.Receipt.Files.All(x => x.RelativePath.StartsWith("Camera originals") && !x.SourceRelativePath.StartsWith("DCIM")), "Package dependency dropped or photo workflow included");
            } });
            Test("P2 package keeps LASTCLIP and audio outside the selected video", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { @"CONTENTS\VIDEO\clip.MXF", @"CONTENTS\AUDIO\clip.MXF", @"CONTENTS\ICON\clip.BMP", "LASTCLIP.TXT", @"DCIM\photo.JPG" }) f.Put(name, name);
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, false));
                Check(result.Files == 4 && File.Exists(Saved(result, "LASTCLIP.TXT")) && result.Receipt.Files.All(x => x.RelativePath.StartsWith("Camera originals")), "P2 support file omitted");
            } });
            Test("partial RED reel and unfamiliar video package stop before copying", delegate { using (var f = new Fixture()) {
                f.Put(@"reel.RDC\clip.R3D", "recording");
                Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, Path.Combine(f.Source, "reel.RDC"), f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(false, true)));
                f.Put("PRO.PRJ", "unknown package");
                Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, true)));
                Check(Directory.GetFileSystemEntries(f.Destination).Length == 0, "Unsupported package wrote a partial batch");
                Check(f.Run(x => {}).Files == 2, "Whole-card all-media backup rejected");
            } });
            Test("device index backup is separate from a selected-media result", delegate { using (var f = new Fixture()) {
                f.Put("fileinfo_list.list", "index"); f.Put("photo.JPG", "photo");
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, true));
                Check(result.Files == 1 && !result.HasSelectedMedia && File.Exists(Saved(result, "fileinfo_list.list")) && File.ReadAllText(Path.Combine(f.Source, "fileinfo_list.list")) == "index", "Index marked as media or original removed");
                ImportReceipt receipt; using (var file = File.OpenRead(result.ReceiptPath)) receipt = (ImportReceipt)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(ImportReceipt)).ReadObject(file);
                Check(!receipt.HasSelectedMedia && receipt.Files.Single().RelativePath.StartsWith(".easy-eject-metadata"), "Receipt cannot distinguish metadata-only import");
            } });
            Test("preview-only video selection creates no misleading completion batch", delegate { using (var f = new Fixture()) {
                f.Put("orphan.lrv", "preview"); f.Put("photo.jpg", "photo");
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, false));
                Check(!result.HasSelectedMedia && result.Files == 0 && Directory.GetFileSystemEntries(f.Destination).Length == 0, "No matching video was marked complete");
            } });
            Test("shared defaults preserve custom choices and freeze an in-flight selection", delegate {
                var state = State(); var defaults = new ImportDefaults(); var profile = defaults.NewProfile(state.Volumes[0], state.Disks[0]);
                Check(profile.FollowsDefaults && profile.Resolve(defaults).VideosOnly && profile.Resolve(defaults).IncludePreviews && profile.Resolve(defaults).CleanLayout, "Factory selection or inheritance incorrect");
                var frozen = profile.Resolve(defaults); defaults.VideosOnly = false; defaults.IncludePreviews = false; defaults.CleanLayout = false;
                var current = profile.Resolve(defaults);
                Check(!current.VideosOnly && !current.IncludePreviews && !current.CleanLayout && frozen.VideosOnly && frozen.IncludePreviews && frozen.CleanLayout && profile.VideosOnly == true && profile.IncludePreviews == true && profile.CleanLayout == true, "Resolution changed stored profile or frozen options");
                profile.SetCustom(true, defaults); profile.VideosOnly = true;
                defaults.VideosOnly = false; defaults.IncludePreviews = true; defaults.CleanLayout = true;
                Check(!profile.FollowsDefaults && profile.Resolve(defaults).VideosOnly && !profile.Resolve(defaults).IncludePreviews && !profile.Resolve(defaults).CleanLayout, "Custom settings followed shared edits");
                profile.SetCustom(false, defaults);
                Check(profile.FollowsDefaults && !profile.Resolve(defaults).VideosOnly && profile.Resolve(defaults).IncludePreviews && profile.Resolve(defaults).CleanLayout, "Switch to defaults failed");
            });
            Test("legacy profiles remain custom all-media after settings roundtrip", delegate { using (var f = new Fixture()) {
                var state = State(); string root = Path.Combine(f.Root, "profiles");
                var profile = new ImportProfile { VolumeId = state.Volumes[0].Id, VolumeSerial = state.Volumes[0].Serial, DiskInstance = state.Disks[0].InstanceId, RelativeSource = "DCIM", Destination = f.Destination };
                Profiles.Save(profile, root);
                // Remove new option keys entirely to represent an actual pre-options JSON file.
                string path = Directory.GetFiles(root).Single();
                string legacy = File.ReadAllText(path); legacy = System.Text.RegularExpressions.Regex.Replace(legacy, @",?""(?:UsesImportDefaults|VideosOnly|IncludePreviews|CleanLayout)"":null", "").Replace("{,", "{"); File.WriteAllText(path, legacy);
                var loaded = Profiles.Load(state.Volumes[0], state.Disks[0], root); var options = loaded.Resolve(new ImportDefaults { VideosOnly = true, IncludePreviews = false });
                Check(!loaded.FollowsDefaults && !options.VideosOnly && options.IncludePreviews && !options.CleanLayout, "Legacy import semantics changed");
                loaded.SetCustom(false, new ImportDefaults()); Profiles.Save(loaded, root); var restored = Profiles.Load(state.Volumes[0], state.Disks[0], root);
                Check(restored.FollowsDefaults && restored.Resolve(new ImportDefaults()).VideosOnly, "Explicit migration was not retained");
            } });
            Test("defaults persistence does not overwrite device profiles", delegate { using (var f = new Fixture()) {
                var state = State(); string root = Path.Combine(f.Root, "profiles"); Directory.CreateDirectory(root);
                var empty = ImportDefaults.Load(root); Check(empty.Snapshot().VideosOnly && empty.Snapshot().IncludePreviews, "Absent settings not factory defaults");
                var profile = empty.NewProfile(state.Volumes[0], state.Disks[0]); profile.Destination = f.Destination; Profiles.Save(profile, root);
                ImportDefaults.Save(new ImportDefaults { VideosOnly = false, IncludePreviews = false }, root); ImportDefaults.Save(new ImportDefaults { VideosOnly = true, IncludePreviews = false }, root);
                Check(Directory.GetFiles(root).Length == 2 && ImportDefaults.Load(root).Snapshot().VideosOnly && !ImportDefaults.Load(root).Snapshot().IncludePreviews && Profiles.Load(state.Volumes[0], state.Disks[0], root).Destination == f.Destination, "Settings replacement damaged another profile");
                File.WriteAllText(Path.Combine(root, "import-defaults.json"), "{}"); Check(ImportDefaults.Load(root).Snapshot().VideosOnly && ImportDefaults.Load(root).Snapshot().IncludePreviews, "Missing defaults fields changed factory behavior");
                File.WriteAllText(Path.Combine(root, "import-defaults.json"), "null"); Throws<SafetyException>(() => ImportDefaults.Load(root));
                File.WriteAllText(Path.Combine(root, "import-defaults.json"), "invalid-json"); Throws<System.Runtime.Serialization.SerializationException>(() => ImportDefaults.Load(root));
                File.WriteAllText(Path.Combine(root, "import-defaults.json"), "{}" + new string(' ', 20000)); Throws<SafetyException>(() => ImportDefaults.Load(root));
            } });
            Test("forget profile leaves defaults and all fixture media intact", delegate { using (var f = new Fixture()) {
                var state = State(); string root = Path.Combine(f.Root, "profiles"); var defaults = new ImportDefaults(); var profile = defaults.NewProfile(state.Volumes[0], state.Disks[0]); profile.Destination = f.Destination;
                f.Put("clip.mov", "source-original"); Profiles.Save(profile, root); ImportDefaults.Save(defaults, root);
                string serial = state.Volumes[0].Serial; state.Volumes[0].Serial = "changed"; Throws<SafetyException>(() => Profiles.Remove(state.Volumes[0], state.Disks[0], root)); state.Volumes[0].Serial = serial;
                Check(Profiles.Load(state.Volumes[0], state.Disks[0], root) != null, "Stale device identity removed the profile");
                Profiles.Remove(state.Volumes[0], state.Disks[0], root);
                Check(Profiles.Load(state.Volumes[0], state.Disks[0], root) == null && Directory.GetFiles(root).Length == 1 && ImportDefaults.Load(root).Snapshot().VideosOnly && File.ReadAllText(Path.Combine(f.Source, "clip.mov")) == "source-original", "Forget affected media or defaults");
            } });
            Test("profile editor mode changes stay isolated until saving", delegate { using (var f = new Fixture()) {
                var state = State(); var defaults = new ImportDefaults(); var original = defaults.NewProfile(state.Volumes[0], state.Disks[0]); original.Destination = f.Destination;
                using (var dialog = new ImportProfileDialog(original, f.Source, "Fixture card", defaults, true)) {
                    Check(dialog.UseDefaults.Checked && !dialog.Selection.Enabled, "Default values are editable as custom");
                    dialog.UseCustom.Checked = true; Check(!dialog.Draft.FollowsDefaults && dialog.Selection.Enabled, "Custom radio did not enable fields");
                    dialog.Selection.Videos.Checked = false; dialog.Selection.Previews.Checked = false; dialog.Selection.Clean.Checked = false; dialog.SaveCustomSelection();
                    Check(!dialog.Draft.Resolve(defaults).VideosOnly && !dialog.Draft.Resolve(defaults).IncludePreviews && !dialog.Draft.Resolve(defaults).CleanLayout && original.FollowsDefaults && original.VideosOnly == true && original.CleanLayout == true, "Draft or original isolation broken");
                    dialog.UseDefaults.Checked = true; Check(dialog.Draft.FollowsDefaults && !dialog.Selection.Enabled && dialog.Selection.Videos.Checked && dialog.Selection.Previews.Checked, "Switching back did not inherit defaults");
                    Check(dialog.AcceptButton != null && dialog.CancelButton != null, "Save/Cancel keyboard controls unavailable");
                }
                Check(original.FollowsDefaults && original.Resolve(defaults).VideosOnly, "Closing an unsaved editor mutated its profile");
            } });
            Test("editing defaults during a copy affects only the next import", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "video"); f.Put("photo.jpg", "photo"); var state = State(); var defaults = new ImportDefaults(); var profile = defaults.NewProfile(state.Volumes[0], state.Disks[0]); var frozen = profile.Resolve(defaults);
                var importer = new VerifiedImporter(new Operations());
                var first = importer.Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => { if (x.StartsWith("Copying")) defaults.VideosOnly = false; }, frozen);
                var second = importer.Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, profile.Resolve(defaults));
                Check(first.Files == 1 && first.Receipt.Files.All(x => !x.SourceRelativePath.EndsWith(".jpg")) && second.Files == 2 && second.Receipt.Files.Count(x => x.SourceRelativePath.EndsWith(".jpg")) == 1 && Directory.GetFiles(f.Source).Length == 2, "Running selection changed or future defaults were ignored");
            } });
            Test("identical repeated import reuses verified files and retains camera dates", delegate { using (var f = new Fixture()) {
                f.Put(@"DCIM\clip.mp4", "master"); f.Put(@"DCIM\clip.srt", "subtitle");
                DateTime recorded = new DateTime(2025, 4, 3, 12, 13, 14, DateTimeKind.Utc); File.SetLastWriteTimeUtc(Path.Combine(f.Source, @"DCIM\clip.mp4"), recorded);
                var importer = new VerifiedImporter(new Operations()); var options = new MediaOptions(true, true, true);
                var a = importer.Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, options);
                var b = importer.Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, options);
                Check(a.Files == 2 && a.Reused == 0 && b.Files == 2 && b.Reused == 2 && b.HasSelectedMedia && Saved(a, @"DCIM\clip.mp4") == Saved(b, @"DCIM\clip.mp4") && Path.GetDirectoryName(Saved(a, @"DCIM\clip.mp4")) == a.Folder, "Repeat duplicated media or failed clean layout");
                Check(File.GetLastWriteTimeUtc(Saved(a, @"DCIM\clip.mp4")) == recorded && Directory.GetFiles(a.Folder, "*.mp4", SearchOption.AllDirectories).Length == 1 && a.ReceiptPath != b.ReceiptPath && File.Exists(a.ReceiptPath) && File.Exists(b.ReceiptPath), "Camera time or immutable receipts lost");
                Check(b.Receipt.Files.All(x => x.Reused && File.Exists(Path.Combine(b.Folder, x.RelativePath))) && File.ReadAllText(Path.Combine(Path.GetDirectoryName(b.ReceiptPath), "import-audit.log")).Contains("Reused verified SHA256"), "Reuse omitted exact manifest or durable audit");
            } });
            foreach (bool clean in new[] { false, true }) Test("lens-pair conflicts preserve the whole group with clean layout=" + clean, delegate { using (var f = new Fixture()) {
                string[] names = { "VID_20261006_000001_00_001.insv", "VID_20261006_000001_10_001.insv", "VID_20261006_000001_00_001.insv.gyro" };
                foreach (string name in names) f.Put(name, "new-" + name);
                string date = Path.Combine(f.Destination, DateTime.Now.ToString("yyyy-MM-dd")); Directory.CreateDirectory(date); File.WriteAllText(Path.Combine(date, names[0]), "previous recording");
                var importer = new VerifiedImporter(new Operations()); var options = new MediaOptions(true, true, clean);
                var result = importer.Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, options);
                string folder = Path.GetDirectoryName(Saved(result, names[0]));
                Check(result.Files == 3 && folder.Contains("Additional media") && result.Receipt.Files.All(x => Path.GetDirectoryName(Path.Combine(result.Folder, x.RelativePath)) == folder) && names.All(x => File.ReadAllText(Path.Combine(folder, x)) == "new-" + x), "Conflict split or renamed a recording group");
                Check(File.ReadAllText(Path.Combine(date, names[0])) == "previous recording" && importer.Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, options).Reused == 3, "Prior media changed or stable conflict group not reused");
            } });
            Test("clean layout keeps semantic groups from different cameras separate", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { @"camera-a\clip.MOV", @"camera-a\clip.XMP", @"camera-b\clip.MP4", @"camera-b\clip.SRT" }) f.Put(name, name);
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, true, true));
                string a = Path.GetDirectoryName(Saved(result, @"camera-a\clip.MOV")), b = Path.GetDirectoryName(Saved(result, @"camera-b\clip.MP4"));
                Check(a != b && a.Contains("Additional media") && b.Contains("Additional media") && Path.GetDirectoryName(Saved(result, @"camera-a\clip.XMP")) == a && Path.GetDirectoryName(Saved(result, @"camera-b\clip.SRT")) == b, "Different camera companions merged");
            } });
            Test("unfamiliar media preserves its selected folder and reuses the snapshot", delegate { using (var f = new Fixture()) {
                f.Put(@"DCIM\clip.mp4", "master"); f.Put(@"DCIM\unfamiliar.data", "unknown"); var importer = new VerifiedImporter(new Operations()); var options = new MediaOptions(false, true, true);
                var a = importer.Run(f.Source, Path.Combine(f.Source, "DCIM"), f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, options);
                var b = importer.Run(f.Source, Path.Combine(f.Source, "DCIM"), f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, options);
                Check(a.Receipt.Files.All(x => x.RelativePath.StartsWith("Camera originals") && Path.GetDirectoryName(Path.Combine(a.Folder, x.RelativePath)).EndsWith("DCIM")) && b.Reused == 2 && Saved(a, "unfamiliar.data") == Saved(b, "unfamiliar.data"), "Unknown media flattened or duplicate snapshot created");
            } });
            Test("proxy and master with the same filename retain distinct camera paths", delegate { using (var f = new Fixture()) {
                f.Put(@"DCIM\clip.mp4", "master"); f.Put(@"DCIM\Proxy\clip.mp4", "preview");
                var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, true, true));
                Check(result.Files == 2 && Saved(result, @"DCIM\clip.mp4") != Saved(result, @"DCIM\Proxy\clip.mp4") && File.ReadAllText(Saved(result, @"DCIM\clip.mp4")) == "master" && File.ReadAllText(Saved(result, @"DCIM\Proxy\clip.mp4")) == "preview" && Saved(result, @"DCIM\Proxy\clip.mp4").EndsWith(@"DCIM\Proxy\clip.mp4"), "Proxy overwrote or became associated with master");
            } });
            Test("saved-copy change during reuse stops a new completion receipt", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); var first = f.Run(x => {}); string saved = Saved(first, "clip.mov"); bool changed = false;
                var error = Throws<SafetyException>(() => f.Run(x => { if (x.StartsWith("Reusing")) { File.WriteAllText(saved, "tampered"); changed = true; } }));
                Check(changed && error.Message.Contains("Destination changed") && File.ReadAllText(saved) == "tampered" && File.ReadAllText(Path.Combine(f.Source, "clip.mov")) == "original" && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 1, "Changed reuse was restored, originals lost or failed operation marked complete");
            } });
            Test("late audit failure preserves originals and supports a verified retry", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); var operations = new Operations();
                var importer = new VerifiedImporter(operations, Native.FreeSpace, x => { if (x.StartsWith("Batch verification complete")) throw new IOException("Fixture audit write failed"); });
                Check(Throws<IOException>(() => importer.Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {})).Message == "Fixture audit write failed", "Wrong audit failure");
                Check(File.ReadAllText(Path.Combine(f.Source, "clip.mov")) == "original" && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Destination, "import-audit.log", SearchOption.AllDirectories).Any(x => File.ReadAllText(x).Contains("Stopped; originals kept")), "Failed audit lost originals or implied completion");
                var retry = new VerifiedImporter(operations).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}); Check(retry.Reused == 1 && File.ReadAllText(Saved(retry, "clip.mov")) == "original", "Audit failure retained reservations or damaged copies");
            } });
            Test("free-space refusal leaves no copied media or completion receipt", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); var importer = new VerifiedImporter(new Operations(), x => 0UL, x => {});
                Check(Throws<SafetyException>(() => importer.Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {})).Message.Contains("free space"), "Wrong capacity failure");
                Check(File.ReadAllText(Path.Combine(f.Source, "clip.mov")) == "original" && Directory.GetFiles(f.Destination, "*.mov", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Capacity refusal left misleading completed media");
            } });
            Test("same-size source mutation after planning cannot pass restored timestamps", delegate { using (var f = new Fixture()) {
                f.Put("a.mov", "first"); f.Put("b.mov", "second"); string file = Path.Combine(f.Source, "a.mov"); DateTime modified = File.GetLastWriteTimeUtc(file); bool changed = false;
                var error = Throws<SafetyException>(() => f.Run(x => { if (x == "Checking b.mov") { File.WriteAllText(file, "FIRST"); File.SetLastWriteTimeUtc(file, modified); changed = true; } }));
                Check(changed && error.Message.Contains("SHA-256") && File.ReadAllText(file) == "FIRST" && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0, "Cached plan accepted changed bytes or test failed earlier");
            } });
            Test("Unicode-equivalent source names fail before destination creation", delegate { using (var f = new Fixture()) {
                f.Put("\u00e9.mov", "composed"); f.Put("e\u0301.mov", "decomposed");
                Check(Throws<SafetyException>(() => f.Run(x => {})).Message.Contains("Unicode"), "Wrong name collision failure");
                Check(Directory.GetFiles(f.Source).Length == 2 && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Ambiguous names mutated destination or source");
            } });
            Test("verified reuse needs no space reservation for another media copy", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); var first = f.Run(x => {}); int checks = 0;
                var second = new VerifiedImporter(new Operations(), x => { checks++; return 0UL; }, x => {}).Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, x => {});
                Check(second.Reused == 1 && checks == 0 && Saved(first, "clip.mov") == Saved(second, "clip.mov") && File.ReadAllText(Saved(second, "clip.mov")) == "original", "Reuse made a duplicate space reservation or changed the copy");
            } });
            Test("read-only saved copy cannot be claimed as flushed reuse", delegate { using (var f = new Fixture()) {
                f.Put("clip.mov", "original"); var first = f.Run(x => {}); string saved = Saved(first, "clip.mov"); FileAttributes attributes = File.GetAttributes(saved);
                try {
                    File.SetAttributes(saved, attributes | FileAttributes.ReadOnly); Throws<System.ComponentModel.Win32Exception>(() => f.Run(x => {}));
                    Check(File.ReadAllText(saved) == "original" && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 1 && File.ReadAllText(Path.Combine(f.Source, "clip.mov")) == "original", "Unflushed reuse marked complete or data changed");
                } finally { File.SetAttributes(saved, attributes); }
            } });
            Console.WriteLine(passed + " passed; " + failed + " failed. No real device eject API was used."); return failed == 0 ? 0 : 1;
        }
        static void Render(System.Windows.Forms.Form form, string path) {
            IntPtr handle = form.Handle; CreateRenderHandles(form); form.PerformLayout();
            Console.WriteLine(Path.GetFileName(path) + " size=" + form.Size + " minimum=" + form.MinimumSize);
            Check(handle != IntPtr.Zero && !form.Visible, "Rendering must use a hidden fixture window");
            using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
        }
        static void CreateRenderHandles(System.Windows.Forms.Control control) {
            IntPtr handle = control.Handle; control.PerformLayout();
            if (control is System.Windows.Forms.TextBox) Console.WriteLine("Text field bounds=" + control.Bounds + " characters=" + control.Text.Length);
            foreach (System.Windows.Forms.Control child in control.Controls) CreateRenderHandles(child);
        }
    }
}
