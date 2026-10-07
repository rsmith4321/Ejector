using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEject {
    public static partial class Tests {
        sealed class ModernHotkeyApi : IHotkeyApi {
            internal int Registers, Unregisters; internal bool Accept = true, Release = true;
            public bool Register(IntPtr window, int id, uint modifiers, uint key) { Registers++; Check(window == new IntPtr(42) && id == GlobalHotkey.Id && modifiers == GlobalHotkey.Modifiers, "Wrong hotkey registration"); return Accept; }
            public bool Unregister(IntPtr window, int id) { Unregisters++; return Release; }
        }
        static IntPtr ModernKey(string letter = "E", int modifiers = 7) { return new IntPtr((letter[0] << 16) | modifiers); }
        static void SettingsOperationTests() {
            Test("modern shortcut delivery is single use and failed queue cannot later trigger", delegate {
                var queue = new List<Action>(); int reviews = 0; bool accept = true;
                using (var dispatch = new HotkeyDispatch(new ModernHotkeyApi(), () => false, a => { queue.Add(a); return accept; }, () => reviews++)) {
                    dispatch.Configure(new IntPtr(42), true, "E"); dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey()); queue[0](); queue[0]();
                    Check(reviews == 1, "Duplicate queued delivery started another review"); accept = false;
                    Check(!dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey()), "Failed queue admitted"); queue[1](); Check(reviews == 1, "Failed queue retained authority");
                }
            });
            Test("modern destroyed shortcut window and uncertain busy state refuse queued review", delegate {
                Action delivery = null; int reviews = 0; bool uncertain = false;
                using (var dispatch = new HotkeyDispatch(new ModernHotkeyApi(), () => { if (uncertain) throw new IOException("Generated unavailable UI state"); return false; }, a => { delivery = a; return true; }, () => reviews++)) {
                    dispatch.Configure(new IntPtr(42), true, "E"); uncertain = true; Check(!dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey()), "Uncertain ownership admitted shortcut"); uncertain = false;
                    dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey()); dispatch.WindowDestroyed(); delivery(); Check(reviews == 0 && !dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey()), "Destroyed window retained review authority");
                }
            });
            Test("modern settings cancelled drafts and foreign cleanup reviews write nothing", delegate {
                int writes = 0; var before = new AppSettings(); var edit = new AppSettingsEdit(before, p => writes++); var other = new AppSettingsEdit(before, p => writes++);
                var review = edit.ReviewCleanup(); Check(!other.AcceptCleanup(review, true), "Foreign draft accepted cleanup");
                edit.Set(AppSettingChoice.Notifications, false); edit.Set(AppSettingChoice.Debug, true); Check(!edit.AcceptCleanup(review, true), "Changed draft accepted old cleanup review"); edit.Cancel();
                Throws<SafetyException>(() => edit.Save()); Check(!before.DebugEnabled && before.NotificationsEnabled && writes == 0, "Cancelled draft published settings");
            });
            Test("modern cleanup enable requires fresh confirmation and cancellation cannot replay", delegate {
                var edit = new AppSettingsEdit(new AppSettings(), p => {}); var first = edit.ReviewCleanup();
                Check(!edit.AcceptCleanup(first, false) && !edit.AcceptCleanup(first, true) && !edit.Snapshot().CleanupCardsEnabled, "Cancelled review enabled cleanup");
                var next = edit.ReviewCleanup(); Check(edit.AcceptCleanup(next, true) && !edit.AcceptCleanup(next, true), "Fresh cleanup confirmation failed or replayed");
                edit.DisableCleanup(); var closed = edit.ReviewCleanup(); edit.Cancel(); Check(!edit.AcceptCleanup(closed, true), "Closed draft accepted cleanup");
            });
            Test("modern settings snapshots and persistence callback cannot rewrite frozen result", delegate {
                var before = new AppSettings { ShortcutLetter = "R" }; AppSettings captured = null;
                var edit = new AppSettingsEdit(before, p => { captured = AppSettingsEdit.Copy(p); p.ShortcutLetter = "X"; p.ShowNotifications = true; });
                var snapshot = edit.Snapshot(); snapshot.ShortcutLetter = "BAD"; before.ShortcutLetter = "D";
                edit.Set(AppSettingChoice.Notifications, false); edit.Set(AppSettingChoice.Shortcut, true); edit.SetLetter("T"); var result = edit.Save();
                Check(result.ShortcutKey == "T" && !result.NotificationsEnabled && captured.ShortcutKey == "T" && !captured.NotificationsEnabled, "Caller or saver changed frozen settings");
                Throws<SafetyException>(() => edit.Save()); Throws<SafetyException>(() => edit.SetLetter("E"));
            });
            Test("modern settings persist all choices only in owned fixture configuration", delegate {
                string root = ModernDefaultsFixture(); var edit = new AppSettingsEdit(new AppSettings(), p => AppSettings.Save(p, root));
                edit.Set(AppSettingChoice.Notifications, false); edit.Set(AppSettingChoice.Debug, true); edit.Set(AppSettingChoice.Shortcut, true); edit.SetLetter("K");
                edit.Set(AppSettingChoice.WarnOther, false); edit.Set(AppSettingChoice.AutomaticUpdates, false); Check(edit.AcceptCleanup(edit.ReviewCleanup(), true), "Cleanup review failed"); edit.Save();
                var actual = AppSettings.Load(root); Check(!actual.NotificationsEnabled && actual.DebugEnabled && actual.ShortcutEnabled && actual.ShortcutKey == "K" && actual.CleanupCardsEnabled && !actual.WarnOtherEnabled && !actual.AutomaticUpdatesEnabled, "Saved choices differ");
            });
            Test("modern failed save is consumed even if durable settings were already written", delegate {
                string root = ModernDefaultsFixture(); int writes = 0; var edit = new AppSettingsEdit(new AppSettings(), p => { writes++; AppSettings.Save(p, root); throw new IOException("Generated terminal save failure"); });
                edit.Set(AppSettingChoice.Debug, true); Throws<IOException>(() => edit.Save()); Throws<SafetyException>(() => edit.Save());
                Check(writes == 1 && AppSettings.Load(root).DebugEnabled && !edit.Open, "Failure retried publication or hid durable saved state");
            });
            Test("modern settings active save rejects cancel edits and a second worker", delegate {
                using (var entered = new ManualResetEventSlim()) using (var finish = new ManualResetEventSlim()) {
                    int writes = 0; var edit = new AppSettingsEdit(new AppSettings(), p => { Interlocked.Increment(ref writes); entered.Set(); Check(finish.Wait(5000), "Settings fixture worker timed out"); });
                    var worker = Task.Run(() => edit.Save()); Check(entered.Wait(5000), "Settings worker never entered");
                    try { Throws<InvalidOperationException>(() => edit.Cancel()); Throws<SafetyException>(() => edit.SetLetter("E")); Throws<SafetyException>(() => edit.Save()); Check(edit.Saving, "Worker ownership lost"); }
                    finally { finish.Set(); }
                    Check(worker.GetAwaiter().GetResult().ShortcutKey == "E" && writes == 1 && !edit.Open, "Multiple saves or wrong terminal settings");
                }
            });
            Test("modern shortcut admits only exact enabled modifiers ID and key", delegate {
                var api = new ModernHotkeyApi(); var queue = new List<Action>(); int reviews = 0;
                using (var dispatch = new HotkeyDispatch(api, () => false, a => { queue.Add(a); return true; }, () => reviews++)) {
                    dispatch.Configure(new IntPtr(42), false, "E"); Check(!dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey()) && api.Registers == 0, "Disabled shortcut registered or triggered");
                    dispatch.Configure(new IntPtr(42), true, "E");
                    Check(!dispatch.Receive(IntPtr.Zero, ModernKey()) && !dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey(modifiers: 3)) && !dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey("R")), "Wrong shortcut admitted");
                    Check(dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey()) && !dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey()) && reviews == 0 && queue.Count == 1, "Shortcut executed directly or duplicated"); queue[0](); Check(reviews == 1, "Queued review not delivered");
                }
            });
            Test("modern shortcut queued before busy state never starts a removal review", delegate {
                var queue = new List<Action>(); bool busy = false; int reviews = 0;
                using (var dispatch = new HotkeyDispatch(new ModernHotkeyApi(), () => busy, a => { queue.Add(a); return true; }, () => reviews++)) {
                    dispatch.Configure(new IntPtr(42), true, "E"); dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey()); busy = true; queue[0]();
                    Check(reviews == 0 && !dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey()), "Busy shortcut started a review"); busy = false;
                    Check(dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey()), "Fresh idle shortcut failed"); queue[1](); Check(reviews == 1, "Fresh review not delivered");
                }
            });
            Test("modern shortcut reconfiguration and shutdown revoke previously queued actions", delegate {
                var queue = new List<Action>(); int reviews = 0; var api = new ModernHotkeyApi();
                var dispatch = new HotkeyDispatch(api, () => false, a => { queue.Add(a); return true; }, () => reviews++);
                dispatch.Configure(new IntPtr(42), true, "E"); dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey());
                dispatch.Configure(new IntPtr(42), true, "E"); queue[0](); Check(reviews == 0, "Same-letter reconfiguration retained old request");
                dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey()); dispatch.Dispose(); queue[1](); Check(reviews == 0 && api.Unregisters == 2, "Shutdown delivered queued review or failed release");
            });
            Test("modern shortcut registration conflicts release failures and failed queues stay inactive", delegate {
                var api = new ModernHotkeyApi { Accept = false }; int reviews = 0;
                using (var dispatch = new HotkeyDispatch(api, () => false, a => false, () => reviews++)) {
                    Check(!dispatch.Configure(new IntPtr(42), true, "E") && !dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey()), "Conflict retained authority");
                    api.Accept = true; Check(dispatch.Configure(new IntPtr(42), true, "E"), "Fresh registration failed"); Check(!dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey()) && !dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey()), "Failed queue remained admitted");
                    api.Release = false; Check(!dispatch.Configure(new IntPtr(42), true, "R") && api.Registers == 2 && !dispatch.Receive(new IntPtr(GlobalHotkey.Id), ModernKey()), "Release failure registered another shortcut");
                    Check(reviews == 0, "Refused shortcut invoked review");
                }
            });
        }
        sealed class ModernBulkConnections : IBulkConnections {
            internal readonly ModernStorageDiscovery Discovery;
            internal ModernBulkConnections(ModernStorageDiscovery value) { Discovery = value; }
            public string Stamp(string[] instances) { return Discovery.State.GroupConnections.Where(p => instances.Contains(p.Key)).Select(p => p.Value).Single(); }
        }
        sealed class ModernBulkApi : IEjectApi {
            internal readonly ModernStorageDiscovery Discovery; internal readonly Action Validate;
            internal int Calls; internal bool Veto, KeepPresent;
            internal Action Before;
            internal ModernBulkApi(ModernStorageDiscovery value, Action validate) { Discovery = value; Validate = validate; }
            public EjectResult Request(uint node, string instance) {
                if (Before != null) Before(); Validate(); Calls++;
                if (Veto) return new EjectResult { Success = false, Veto = 5, Name = "Generated fixture veto" };
                if (!KeepPresent) {
                    int[] disks = Discovery.State.Disks.Where(d => d.TargetId == instance).Select(d => d.Number).ToArray();
                    Discovery.State.Volumes.RemoveAll(v => v.Disks.Any(disks.Contains)); Discovery.State.Disks.RemoveAll(d => disks.Contains(d.Number)); Discovery.State.Epoch++; Discovery.Version++;
                }
                return new EjectResult { Success = true };
            }
        }
        static void GroupedOperationTests() {
            Test("modern grouped cancellation at request boundary reaches no fake disposition", delegate {
                var discovery = new ModernStorageDiscovery(); discovery.State.GroupConnections["modern-usb"] = "fixture-generation-1"; ModernBulkApi api = null;
                using (var cancel = new CancellationTokenSource()) {
                    var review = new GroupedEjectReview(discovery.State, new AppSettings(), discovery, new Operations(), new ModernBulkConnections(discovery), validate => api = new ModernBulkApi(discovery, validate) { Before = () => cancel.Cancel() }, path => "modern-volume");
                    review.Confirm(); var result = review.Execute(cancel.Token, new CleanupCounts(), text => {}, () => {}, 1);
                    Check(!result.Success && result.Confirmed == 0 && api.Calls == 0 && discovery.State.Volumes.Count == 1, "Cancellation crossed final request guard");
                }
            });
            Test("modern grouped partial veto retains exact final absence count and remaining card", delegate {
                var discovery = new ModernStorageDiscovery(); discovery.State.GroupConnections["modern-usb"] = "fixture-generation-1";
                discovery.State.Disks.Add(new Disk { Number = 8, DevInst = 11, InstanceId = "second-disk", Serial = "second-serial", TargetId = "second-usb", TargetDevInst = 21, Ancestors = new List<string> { "second-disk", "second-usb" } });
                discovery.State.Volumes.Add(new Volume { Id = "second-volume", Serial = "DEF", Extents = "8:0:1000", Disks = new List<int> { 8 }, Mounts = new List<string> { @"F:\" }, Group = StorageGroup.Emulator }); discovery.State.GroupConnections["second-usb"] = "fixture-generation-2";
                int factories = 0; var operations = new Operations();
                var review = new GroupedEjectReview(discovery.State, new AppSettings(), discovery, operations, new ModernBulkConnections(discovery), validate => new ModernBulkApi(discovery, validate) { Veto = ++factories == 2 }, path => "unused-fake-volume");
                review.Confirm(); var result = review.Execute(CancellationToken.None, new CleanupCounts(), text => {}, () => {}, 1);
                Check(!result.Success && result.Requested == 2 && result.Confirmed == 1 && result.SafeDevices.Count == 1 && discovery.State.Volumes.Single().Id == "second-volume" && operations.Available(new[] { 7, 8 }), "Partial veto lost final count or remaining card");
            });
            Test("modern grouped review requires explicit confirmation and cancellation cannot replay", delegate {
                var discovery = new ModernStorageDiscovery(); discovery.State.GroupConnections["modern-usb"] = "fixture-generation-1"; int factories = 0;
                var review = new GroupedEjectReview(discovery.State, new AppSettings(), discovery, new Operations(), new ModernBulkConnections(discovery), validate => { factories++; return new ModernBulkApi(discovery, validate); }, path => "modern-volume");
                Check(review.Count == 1 && !review.HasCleanup && review.Description.Contains("all listed volumes"), "Grouping/cleanup/default warning lost");
                Throws<SafetyException>(() => review.Execute(CancellationToken.None, new CleanupCounts(), text => {})); review.Cancel(); Throws<SafetyException>(() => review.Confirm());
                Check(factories == 0 && discovery.State.Volumes.Count == 1, "Cancelled review used an eject API");
            });
            Test("modern grouped successful fake removal requires final absence and consumes confirmation", delegate {
                var discovery = new ModernStorageDiscovery(); discovery.State.GroupConnections["modern-usb"] = "fixture-generation-1"; var operations = new Operations(); ModernBulkApi api = null;
                var review = new GroupedEjectReview(discovery.State, new AppSettings(), discovery, operations, new ModernBulkConnections(discovery), validate => api = new ModernBulkApi(discovery, validate), path => "modern-volume");
                var caller = review.Disks; caller[0] = 900; review.Confirm(); var result = review.Execute(CancellationToken.None, new CleanupCounts(), text => {}, () => {}, 1);
                Check(api.Calls == 1 && result.Success && result.Confirmed == 1 && result.SafeDevices.Count == 1 && operations.Available(new[] { 7 }), "Wrong API count/absence/reservations");
                Throws<SafetyException>(() => review.Execute(CancellationToken.None, new CleanupCounts(), text => {}));
            });
            Test("modern grouped reconnection after confirmation makes no API request", delegate {
                var discovery = new ModernStorageDiscovery(); discovery.State.GroupConnections["modern-usb"] = "fixture-generation-1"; int calls = 0;
                var review = new GroupedEjectReview(discovery.State, new AppSettings(), discovery, new Operations(), new ModernBulkConnections(discovery), validate => { calls++; return new ModernBulkApi(discovery, validate); }, path => "modern-volume");
                review.Confirm(); discovery.State.Epoch++; discovery.Version++; var result = review.Execute(CancellationToken.None, new CleanupCounts(), text => {}, () => {}, 1);
                Check(!result.Success && result.Confirmed == 0 && calls == 0 && discovery.State.Volumes.Count == 1, "Changed connection requested removal");
            });
            Test("modern grouped accepted request with present device is never reported safe", delegate {
                var discovery = new ModernStorageDiscovery(); discovery.State.GroupConnections["modern-usb"] = "fixture-generation-1";
                var review = new GroupedEjectReview(discovery.State, new AppSettings(), discovery, new Operations(), new ModernBulkConnections(discovery), validate => new ModernBulkApi(discovery, validate) { KeepPresent = true }, path => "modern-volume");
                review.Confirm(); var result = review.Execute(CancellationToken.None, new CleanupCounts(), text => {}, () => {}, 1);
                Check(!result.Success && result.Confirmed == 0 && result.SafeDevices.Count == 0 && discovery.State.Volumes.Count == 1, "Accepted but present device reported safe");
            });
            Test("modern grouped veto and pre-cancellation preserve devices and release disks", delegate {
                var discovery = new ModernStorageDiscovery(); discovery.State.GroupConnections["modern-usb"] = "fixture-generation-1"; var operations = new Operations(); int factories = 0;
                Func<GroupedEjectReview> fresh = () => new GroupedEjectReview(discovery.State, new AppSettings(), discovery, operations, new ModernBulkConnections(discovery), validate => { factories++; return new ModernBulkApi(discovery, validate) { Veto = true }; }, path => "modern-volume");
                var review = fresh(); review.Confirm(); var result = review.Execute(CancellationToken.None, new CleanupCounts(), text => {}, () => {}, 1); Check(!result.Success && result.Message.Contains("veto") && result.SafeDevices.Count == 0, "Veto lost");
                var cancelled = fresh(); cancelled.Confirm(); using (var stop = new CancellationTokenSource()) { stop.Cancel(); result = cancelled.Execute(stop.Token, new CleanupCounts(), text => {}, () => {}, 1); }
                Check(!result.Success && factories == 1 && discovery.State.Volumes.Count == 1 && operations.Available(new[] { 7 }), "Cancelled action requested removal or retained disks");
            });
            Test("modern grouped review excludes other storage and refuses mixed partitions", delegate {
                var discovery = new ModernStorageDiscovery(); discovery.State.GroupConnections["modern-usb"] = "fixture-generation-1";
                discovery.State.Volumes.Add(new Volume { Id = "foreign-partition", Serial = "foreign", Group = StorageGroup.External, Disks = new List<int> { 7 } });
                Throws<SafetyException>(() => new GroupedEjectReview(discovery.State, new AppSettings(), discovery, new Operations(), new ModernBulkConnections(discovery), validate => { throw new Exception("No API expected"); }, path => "modern-volume"));
                discovery.State.Volumes.RemoveAt(1); discovery.State.Volumes[0].Group = StorageGroup.External;
                var empty = new GroupedEjectReview(discovery.State, new AppSettings(), discovery, new Operations(), new ModernBulkConnections(discovery), validate => { throw new Exception("No API expected"); }, path => "modern-volume"); Check(empty.Count == 0, "Other storage entered grouped removal");
            });
            Test("modern grouped cleanup uses frozen settings and only generated metadata", delegate {
                string root = ModernDefaultsFixture(); File.WriteAllText(Path.Combine(root, ".DS_Store"), "Generated metadata"); File.WriteAllText(Path.Combine(root, "clip.mp4"), "Generated retained media");
                var discovery = new ModernStorageDiscovery { State = ModernStorageState(root) }; discovery.State.GroupConnections["modern-usb"] = "fixture-generation-1"; var settings = new AppSettings { CleanCardsOnEject = true };
                var review = new GroupedEjectReview(discovery.State, settings, discovery, new Operations(), new ModernBulkConnections(discovery), validate => new ModernBulkApi(discovery, validate), path => "modern-volume");
                settings.CleanCardsOnEject = false; Check(review.HasCleanup && review.Description.Contains("metadata"), "Settings changed frozen cleanup"); review.Confirm(); var counts = new CleanupCounts(); var result = review.Execute(CancellationToken.None, counts, text => {}, () => {}, 1);
                Check(result.Success && !File.Exists(Path.Combine(root, ".DS_Store")) && File.ReadAllText(Path.Combine(root, "clip.mp4")) == "Generated retained media" && result.CleanupMessage == counts.Message, "Cleanup exceeded generated metadata or lost counts");
            });
        }
    }
}
