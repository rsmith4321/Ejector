using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        static Inventory CardState() {
            var state = State(); state.Volumes[0].Group = StorageGroup.Camera; state.Volumes[0].Classification = "Removable SD / MMC candidate"; state.Disks[0].Removable = true;
            AddCard(state, 8, "b", StorageGroup.Emulator); state.Volumes[1].Classification = "Emulator card candidate (SD hardware + folder evidence)"; state.GroupConnections["usb-a"] = "stamp-a"; state.GroupConnections["usb-b"] = "stamp-b"; return state;
        }
        static void AddCard(Inventory state, int number, string suffix, StorageGroup group) {
            state.Disks.Add(new Disk { Number = number, DevInst = (uint)(number + 10), InstanceId = "disk-" + suffix, Serial = "serial-" + suffix, TargetId = "usb-" + suffix, TargetDevInst = (uint)(number + 20), Removable = true, Ancestors = new List<string> { "disk-" + suffix, "usb-" + suffix } });
            state.Volumes.Add(new Volume { Id = "volume-" + suffix, Serial = "serial-volume-" + suffix, Extents = number + ":0:1000", Disks = new List<int> { number }, Mounts = new List<string> { ((char)('E' + number - 7)) + @":\" }, Group = group });
        }
        sealed class CardDiscovery : IDiscovery, IBulkConnections {
            internal Inventory State = CardState(); internal readonly ConnectionVersions Versions = new ConnectionVersions(); internal Action<int> BeforeScan; int scans;
            internal CardDiscovery() { Versions.Begin(); Versions.Enable(); RefreshStamps(); }
            void RefreshStamps() { foreach (var id in State.Disks.Where(d => d.TargetId != "").Select(d => d.TargetId).Distinct().ToArray()) State.GroupConnections[id] = Stamp(WindowsDiscovery.GroupInstances(State, id)); }
            public Inventory Scan() { scans++; if (BeforeScan != null) BeforeScan(scans); RefreshStamps(); return State; }
            public long Epoch { get { return State.Epoch; } }
            public string Stamp(string[] ids) { return Versions.Stamp(ids); }
            internal void Changed(string id) { Versions.Note(id); State.Epoch++; }
            internal void Remove(string id) {
                var numbers = State.Disks.Where(d => d.TargetId == id).Select(d => d.Number).ToArray();
                State.Disks.RemoveAll(d => numbers.Contains(d.Number)); State.Volumes.RemoveAll(v => v.Disks.Any(numbers.Contains)); Changed(id);
            }
        }
        sealed class CardApi : IEjectApi {
            internal readonly List<string> Calls = new List<string>(); internal Action Validate, JustBeforeValidation; internal Func<string, EjectResult> OnRequest;
            public EjectResult Request(uint node, string id) { if (JustBeforeValidation != null) JustBeforeValidation(); Validate(); Calls.Add(id); return OnRequest(id); }
        }
        static BulkEjectResult ExecuteCards(CardDiscovery discovery, BulkEjectPlan plan, CardApi api, Operations operations) {
            return new BulkEjectService(discovery, discovery, operations, validate => { api.Validate = validate; return api; }, () => {}, 2).Execute(plan);
        }
        sealed class HotkeyApi : IHotkeyApi {
            internal int Registers, Unregisters; internal bool RegisterResult = true, UnregisterResult = true; internal IntPtr Window; internal int Id; internal uint Key, Modifiers;
            public bool Register(IntPtr window, int id, uint modifiers, uint key) { Registers++; Window = window; Id = id; Modifiers = modifiers; Key = key; return RegisterResult; }
            public bool Unregister(IntPtr window, int id) { Unregisters++; Check(window == Window && id == Id, "Unregistered a different owner's shortcut"); return UnregisterResult; }
        }
        static IntPtr HotkeyPayload(char letter) { return new IntPtr((letter << 16) | 7); }
        static void BulkTests() {
            Test("all eleven camera roots count with conservative directory evidence", delegate { using (var fixture = new Fixture()) {
                foreach (var name in new[] { "DCIM", "PRIVATE", "AVCHD", "MP_ROOT", "XDROOT", "BPAV", "NIKON", "CANONMSC", "FUJI", "GOPRO", "SONY" }) { string path = Path.Combine(fixture.Source, name.ToLowerInvariant()); Directory.CreateDirectory(path); Check(FolderEvidence.Read(fixture.Source).Camera, "Missed root " + name); Directory.Delete(path); }
                fixture.Put("DCIM", "File is not camera-folder evidence"); Check(!FolderEvidence.Read(fixture.Source).Camera, "Filename classified as a directory");
            } });
            Test("emulator folders require two distinct hits and SD hardware", delegate { using (var fixture = new Fixture()) {
                Directory.CreateDirectory(Path.Combine(fixture.Source, "ROMS")); Check(FolderEvidence.Read(fixture.Source).EmulatorHits == 1, "Wrong hit count");
                Directory.CreateDirectory(Path.Combine(fixture.Source, ".EmulationStation")); var evidence = FolderEvidence.Read(fixture.Source); Check(evidence.EmulatorHits == 2, "Case-insensitive emulator roots missed");
                var volume = new Volume(); Classifier.Apply(volume, new Disk { Bus = 12, Removable = true }, true, evidence.EmulatorHits); Check(volume.Group == StorageGroup.Emulator, "Camera folder overrode emulator");
                Classifier.Apply(volume, new Disk { Bus = 17, Hotplug = true }, false, 4); Check(volume.Group == StorageGroup.External, "SSD mistaken for emulator card");
                Classifier.Apply(volume, new Disk { Model = "USB SDXC reader", Hotplug = true }, false, 1); Check(volume.Group == StorageGroup.Camera, "SD descriptor ignored");
                Classifier.Apply(volume, new Disk { Model = "SSD drive", Hotplug = true }, false, 4); Check(volume.Group == StorageGroup.External, "SD matched inside SSD");
            } });
            Test("camera evidence ignores links and never promotes fixed or system storage", delegate { using (var fixture = new Fixture()) {
                string link = Path.Combine(fixture.Source, "DCIM"); CreateJunction(fixture, link, fixture.Destination); Check(!FolderEvidence.Read(fixture.Source).Camera, "Reparse evidence followed");
                var volume = new Volume(); Classifier.Apply(volume, new Disk { Model = "SDXC" }, true, 4); Check(volume.Group == StorageGroup.Internal, "Fixed storage promoted");
                volume.System = true; Classifier.Apply(volume, new Disk { Bus = 12, Removable = true }, true, 4); Check(volume.Group == StorageGroup.Protected && !volume.IsCard, "System promoted");
            } });
            Test("bulk policy plans only camera and emulator cards and deduplicates partitions", delegate {
                var state = CardState(); state.Volumes.Add(new Volume { Id = "partition-a2", Serial = "part2", Disks = new List<int> { 7 }, Group = StorageGroup.Camera }); AddCard(state, 9, "c", StorageGroup.External);
                var plan = BulkEjectPolicy.Plan(state); Check(plan.Targets.Length == 2 && plan.Targets[0].Affected.Length == 2 && !plan.Description.Contains("volume-c"), "Wrong grouping");
                state.Volumes.Clear(); Check(BulkEjectPolicy.Plan(state).Targets.Length == 0, "Empty cards not empty");
            });
            Test("bulk policy refuses mixed partitions and unclassified sibling LUNs", delegate {
                var state = CardState(); state.Volumes.Add(new Volume { Id = "ssd-partition", Disks = new List<int> { 7 }, Group = StorageGroup.External }); Throws<SafetyException>(() => BulkEjectPolicy.Plan(state));
                state = CardState(); state.Disks.Add(new Disk { Number = 9, InstanceId = "hidden-lun", Ancestors = new List<string> { "usb-a" } }); Throws<SafetyException>(() => BulkEjectPolicy.Plan(state));
            });
            Test("bulk groups sibling card LUNs under one removal request and one count", delegate {
                var state = CardState(); state.Disks[1].TargetId = "usb-a"; state.Disks[1].TargetDevInst = state.Disks[0].TargetDevInst; state.Disks[1].Ancestors.Add("usb-a");
                state.Volumes.Add(new Volume { Id = "third-card-partition", Disks = new List<int> { 8 }, Group = StorageGroup.Emulator });
                var plan = BulkEjectPolicy.Plan(state); Check(plan.Targets.Length == 1 && plan.Targets[0].Disks.Length == 2 && plan.Targets[0].Affected.Length == 3, "Card LUN omitted or requested twice");
                using (var window = new DeviceWindow(true)) { window.PresentInventory(state); int count = (int)typeof(DeviceWindow).GetField("cardCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(window); Check(count == 1, "Partitions/LUNs counted as separate devices"); }
            });
            Test("bulk policy refuses protected, unknown, spanning and untracked cards", delegate {
                var state = CardState(); state.Volumes[0].Disks.Clear(); Throws<SafetyException>(() => BulkEjectPolicy.Plan(state));
                state = CardState(); state.Volumes[0].System = true; Throws<SafetyException>(() => BulkEjectPolicy.Plan(state));
                state = CardState(); state.Volumes[0].Disks.Add(8); Throws<SafetyException>(() => BulkEjectPolicy.Plan(state));
                state = CardState(); state.Volumes[1].Error = "Fixture locked"; Throws<SafetyException>(() => BulkEjectPolicy.Plan(state));
                state = CardState(); state.GroupConnections.Clear(); Throws<SafetyException>(() => BulkEjectPolicy.Plan(state));
            });
            Test("bulk removal reserves every confirmed card before any API and releases all", delegate {
                var d = new CardDiscovery(); var ops = new Operations(); var api = new CardApi { OnRequest = id => { throw new Exception("Must never request while a later card is busy"); } }; var plan = BulkEjectPolicy.Plan(d.State);
                using (ops.Reserve(new[] { 8 })) { var result = ExecuteCards(d, plan, api, ops); Check(!result.Success && result.Confirmed == 0 && api.Calls.Count == 0, "Partial removal before busy refusal"); using (ops.Reserve(new[] { 7 })) {} }
                using (ops.Reserve(new[] { 7, 8 })) {}
            });
            Test("bulk removal survives its own device events and verifies each absent", delegate {
                var d = new CardDiscovery(); var ops = new Operations(); var api = new CardApi { OnRequest = id => { Throws<SafetyException>(() => ops.Reserve(new[] { 7 })); Throws<SafetyException>(() => ops.Reserve(new[] { 8 })); d.Remove(id); return new EjectResult { Success = true }; } };
                var result = ExecuteCards(d, BulkEjectPolicy.Plan(d.State), api, ops); Check(result.Success && result.Confirmed == 2 && api.Calls.SequenceEqual(new[] { "usb-a", "usb-b" }) && result.SafeDevices.Count == 2, "Expected removal invalidated unrelated card"); using (ops.Reserve(new[] { 7, 8 })) {}
            });
            Test("bulk removal stops on first veto and never retries", delegate {
                var d = new CardDiscovery(); var api = new CardApi { OnRequest = id => new EjectResult { Code = 0x17, Veto = 5, Name = "Fixture editor" } };
                var result = ExecuteCards(d, BulkEjectPolicy.Plan(d.State), api, new Operations()); Check(!result.Success && result.Confirmed == 0 && api.Calls.Count == 1 && result.Message.Contains("Fixture editor") && result.Message.Contains("OutstandingOpen"), "Veto omitted or retried");
            });
            Test("bulk later veto retains earlier verified safe-removal names", delegate {
                var d = new CardDiscovery(); var api = new CardApi { OnRequest = id => { if (id == "usb-a") { d.Remove(id); return new EjectResult { Success = true }; } return new EjectResult { Code = 23, Veto = 5, Name = "Fixture veto" }; } };
                var result = ExecuteCards(d, BulkEjectPolicy.Plan(d.State), api, new Operations()); Check(!result.Success && result.Confirmed == 1 && result.SafeDevices.Count == 1 && result.SafeDevices[0].Contains(@"E:\") && result.Message.Contains("1 of 2"), "Earlier safe removal concealed");
            });
            Test("bulk accepted request without disappearance never reports safe or continues", delegate {
                var d = new CardDiscovery(); var api = new CardApi { OnRequest = id => new EjectResult { Success = true } };
                var result = ExecuteCards(d, BulkEjectPolicy.Plan(d.State), api, new Operations()); Check(!result.Success && result.Confirmed == 0 && api.Calls.Count == 1 && result.SafeDevices.Count == 0, "Accepted request claimed absent");
            });
            Test("bulk waits through transient notification inventory before verifying disappearance", delegate {
                var d = new CardDiscovery(); var api = new CardApi { OnRequest = id => { if (id == "usb-b") d.Remove(id); return new EjectResult { Success = true }; } };
                d.BeforeScan = number => { if (number == 4) d.State.Complete = false; if (number == 5) { d.State.Complete = true; d.Remove("usb-a"); } };
                var result = ExecuteCards(d, BulkEjectPolicy.Plan(d.State), api, new Operations()); Check(result.Success && result.Confirmed == 2 && api.Calls.Count == 2, "Transient callback scan prevented verified sequence");
            });
            Test("bulk stops on identical reconnect of a later card", delegate {
                var d = new CardDiscovery(); var api = new CardApi { OnRequest = id => { d.Remove(id); d.Changed("disk-b"); d.Changed("disk-b"); return new EjectResult { Success = true }; } };
                var result = ExecuteCards(d, BulkEjectPolicy.Plan(d.State), api, new Operations()); Check(!result.Success && result.Confirmed == 1 && api.Calls.Count == 1, "Reconnected identical card ejected");
            });
            Test("bulk stops when a later card identity, classification or sibling changes", delegate {
                foreach (int variant in new[] { 0, 1, 2 }) {
                    var d = new CardDiscovery(); var api = new CardApi { OnRequest = id => { d.Remove(id); if (variant == 0) d.State.Volumes[0].Serial = "changed"; else if (variant == 1) d.State.Volumes[0].Group = StorageGroup.External; else d.State.Volumes.Add(new Volume { Id = "new-partition", Disks = new List<int> { 8 }, Group = StorageGroup.Emulator }); return new EjectResult { Success = true }; } };
                    var result = ExecuteCards(d, BulkEjectPolicy.Plan(d.State), api, new Operations()); Check(!result.Success && result.Confirmed == 1 && api.Calls.Count == 1, "Changed later card removed");
                }
            });
            Test("bulk never adds a newly connected unconfirmed card", delegate {
                var d = new CardDiscovery(); var api = new CardApi { OnRequest = id => { d.Remove(id); if (id == "usb-a") AddCard(d.State, 9, "c", StorageGroup.Camera); return new EjectResult { Success = true }; } };
                var result = ExecuteCards(d, BulkEjectPolicy.Plan(d.State), api, new Operations()); Check(result.Success && result.Confirmed == 2 && d.State.Volumes.Single().Id == "volume-c" && !api.Calls.Contains("usb-c"), "New card added to old confirmation");
            });
            Test("bulk preflight rejects a changed global generation before first request", delegate {
                var d = new CardDiscovery(); var plan = BulkEjectPolicy.Plan(d.State); d.State.Epoch++; var api = new CardApi { OnRequest = id => new EjectResult { Success = true } };
                var result = ExecuteCards(d, plan, api, new Operations()); Check(!result.Success && api.Calls.Count == 0, "Stale confirmation used");
            });
            Test("bulk live callback closes the scan-to-request connection gap", delegate {
                var d = new CardDiscovery(); var api = new CardApi { JustBeforeValidation = () => d.Changed("usb-b"), OnRequest = id => new EjectResult { Success = true } };
                var result = ExecuteCards(d, BulkEjectPolicy.Plan(d.State), api, new Operations()); Check(!result.Success && api.Calls.Count == 0, "Changed confirmed peer missed before API");
            });
            Test("bulk loses authority when notification tracking faults", delegate {
                var d = new CardDiscovery(); var api = new CardApi { OnRequest = id => { d.Remove(id); d.Versions.Disable(); return new EjectResult { Success = true }; } };
                var result = ExecuteCards(d, BulkEjectPolicy.Plan(d.State), api, new Operations()); Check(!result.Success && result.Confirmed == 1 && api.Calls.Count == 1, "Unavailable tracking allowed later removal");
            });
            Test("bulk incomplete disappearance verification stops without a safe claim", delegate {
                var d = new CardDiscovery(); var api = new CardApi { OnRequest = id => { d.Remove(id); d.State.Complete = false; return new EjectResult { Success = true }; } };
                var result = ExecuteCards(d, BulkEjectPolicy.Plan(d.State), api, new Operations()); Check(!result.Success && result.Confirmed == 0 && api.Calls.Count == 1, "Incomplete inventory proved absence");
            });
            Test("bulk confirmation captures immutable names and defaults to keep connected", delegate {
                var state = CardState(); var plan = BulkEjectPolicy.Plan(state); string description = plan.Description; state.Volumes[0].Mounts.Clear(); state.Disks[0].TargetId = "changed";
                Check(plan.Description == description, "Confirmation changed with inventory"); using (var dialog = new BulkEjectDialog(plan)) Check(((Button)dialog.AcceptButton).DialogResult == DialogResult.Cancel && dialog.CancelButton == dialog.AcceptButton, "Enter requested eject");
            });
            Test("per-instance versions isolate unrelated events and include shared ancestors", delegate {
                var versions = new ConnectionVersions(); versions.Begin(); versions.Enable(); string[] a = { "disk-a", "hub" }, b = { "disk-b", "hub" }; string first = versions.Stamp(a), second = versions.Stamp(b);
                versions.Note("disk-a"); Check(versions.Stamp(a) != first && versions.Stamp(b) == second, "Unrelated group invalidated"); versions.Note("hub"); Check(versions.Stamp(b) != second, "Shared ancestor missed");
                string stable = versions.Stamp(b); Check(stable == versions.Stamp(new[] { "HUB", "DISK-B", "hub" }), "Case/order changed stamp"); versions.Disable(); versions.Enable(); Check(!versions.Available && versions.Stamp(b) == "", "Fault recovered without new registration"); versions.Begin(); versions.Enable(); Check(versions.Stamp(b) != stable, "Restart restored old authority");
            });
            Test("malformed device-instance event faults proof rather than guessing identity", delegate {
                byte[] data = new byte[416]; Array.Copy(BitConverter.GetBytes(2), 0, data, 0, 4); byte[] id = System.Text.Encoding.Unicode.GetBytes("USB\\FIXTURE\0"); Array.Copy(id, 0, data, 8, id.Length); IntPtr ptr = Marshal.AllocHGlobal(data.Length);
                try { Marshal.Copy(data, 0, ptr, data.Length); Check(InstanceNotifications.Decode(9, ptr, (uint)(8 + id.Length)) == "USB\\FIXTURE", "Valid notification missed"); Check(InstanceNotifications.Decode(9, ptr, 9) == null && InstanceNotifications.Decode(6, ptr, 416) == null && InstanceNotifications.Decode(7, ptr, 8 + (uint)id.Length - 2) == null, "Malformed data accepted"); } finally { Marshal.FreeHGlobal(ptr); }
                var versions = new ConnectionVersions(); versions.Begin(); versions.Note(""); versions.Enable(); Check(!versions.Available, "Registration race revived faulted tracking");
            });
            Test("shortcut is off by default and uses only selected supported letter", delegate {
                var settings = new AppSettings(); var api = new HotkeyApi(); using (var shortcut = new GlobalHotkey(api)) {
                    Check(!settings.ShortcutEnabled && settings.ShortcutKey == "E" && shortcut.Configure(new IntPtr(123), settings.ShortcutEnabled, settings.ShortcutKey) && api.Registers == 0, "Default registered a key");
                    Check(shortcut.Configure(new IntPtr(123), true, "X") && api.Registers == 1 && api.Key == 'X' && api.Modifiers == 0x4007, "Wrong registration");
                    Check(shortcut.Matches(new IntPtr(GlobalHotkey.Id), HotkeyPayload('X'), false) && !shortcut.Matches(new IntPtr(GlobalHotkey.Id), HotkeyPayload('X'), true) && !shortcut.Matches(new IntPtr(GlobalHotkey.Id + 1), HotkeyPayload('X'), false) && !shortcut.Matches(new IntPtr(GlobalHotkey.Id), HotkeyPayload('E'), false), "Foreign or busy key triggered");
                    Check(shortcut.Configure(new IntPtr(123), true, "D") && api.Unregisters == 1 && api.Registers == 2 && !shortcut.Matches(new IntPtr(GlobalHotkey.Id), HotkeyPayload('X'), false), "Old key survived reconfiguration");
                } Check(api.Unregisters == 2, "Dispose retained shortcut");
            });
            Test("shortcut collisions and unsupported choices never register fallback keys", delegate {
                var api = new HotkeyApi { RegisterResult = false }; using (var shortcut = new GlobalHotkey(api)) {
                    Check(!shortcut.Configure(new IntPtr(123), true, "E") && api.Registers == 1 && !shortcut.Registered && shortcut.Status.Contains("unavailable"), "Collision ignored");
                    Check(!shortcut.Configure(new IntPtr(123), true, "Z") && api.Registers == 1 && !shortcut.Matches(new IntPtr(GlobalHotkey.Id), HotkeyPayload('E'), false), "Fallback registered");
                }
            });
            Test("shortcut release failure suppresses action and prevents another registration", delegate {
                var api = new HotkeyApi(); using (var shortcut = new GlobalHotkey(api)) {
                    shortcut.Configure(new IntPtr(123), true, "E"); api.UnregisterResult = false; Check(!shortcut.Configure(new IntPtr(123), false, "E") && !shortcut.Matches(new IntPtr(GlobalHotkey.Id), HotkeyPayload('E'), false), "Failed disable still triggers");
                    Check(!shortcut.Configure(new IntPtr(123), true, "X") && api.Registers == 1, "Second registration after failed release"); shortcut.WindowDestroyed(); Check(!shortcut.Registered, "Destroyed window retained authority");
                }
            });
            Test("shortcut persistence migration and settings draft keep enabling explicit", delegate { using (var fixture = new Fixture()) {
                AppSettings.Save(new AppSettings { ShowNotifications = false }, fixture.Destination); var legacy = AppSettings.Load(fixture.Destination); Check(!legacy.ShortcutEnabled && legacy.ShortcutKey == "E", "Legacy opted in");
                using (var dialog = new AppSettingsDialog(legacy)) { dialog.Shortcut.Checked = true; dialog.ShortcutLetter.SelectedItem = "R"; Check(!legacy.ShortcutEnabled && dialog.Value.ShortcutEnabled && dialog.Value.ShortcutKey == "R", "Draft changed saved choices"); AppSettings.Save(dialog.Value, fixture.Destination); }
                var saved = AppSettings.Load(fixture.Destination); Check(saved.ShortcutEnabled && saved.ShortcutKey == "R" && !saved.NotificationsEnabled, "Persistence lost choices"); Throws<SafetyException>(() => AppSettings.Save(new AppSettings { ShortcutLetter = "Z" }, fixture.Destination)); Check(AppSettings.Load(fixture.Destination).ShortcutKey == "R", "Rejected choice replaced saved state");
            } });
        }
    }
}
