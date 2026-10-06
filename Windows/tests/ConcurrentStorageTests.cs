using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        static ConcurrentStorageState ConcurrentState(Fixture f, ScopedDiscovery discovery, Operations operations, TrayOperationKind kind = TrayOperationKind.Import) {
            var state = new ConcurrentStorageState(operations); state.Attach(new TrayOperation(kind, "Fixture transfer"), ScopedImportGuard(f, discovery)); return state;
        }
        static ConcurrentEjectService ConcurrentService(ScopedDiscovery d, Operations operations, Func<Action, IEjectApi> api) { return new ConcurrentEjectService(d, operations, api, path => "other-volume"); }
        static void ConcurrentStorageTests() {
            Test("concurrent storage admission excludes source destination siblings and opaque work", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); d.AddSibling(703, "source-usb", "source-sibling"); var operations = new Operations(); var state = ConcurrentState(f, d, operations);
                Check(state.Unavailable(d.Scan(), d.Epoch, "other-volume") == null && state.Unavailable(d.Scan(), d.Epoch, "source-volume") != null && state.Unavailable(d.Scan(), d.Epoch, "destination-volume") != null && state.Unavailable(d.Scan(), d.Epoch, "source-sibling-volume") != null, "Protected physical group review admitted");
                state.Attach(new TrayOperation(TrayOperationKind.Import, "Opaque fixture"), new FakeGuard()); Check(!state.HasPrimary && state.Unavailable(d.Scan(), d.Epoch, "other-volume") != null, "Opaque work admitted unrelated request");
            } });
            Test("concurrent admission requires fresh complete snapshot live tracking and unreserved target", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var operations = new Operations(); var state = ConcurrentState(f, d, operations); var snapshot = d.Scan();
                Check(state.Unavailable(snapshot, d.Epoch + 1, "other-volume") != null, "Stale list admitted"); snapshot.Complete = false; Check(state.Unavailable(snapshot, d.Epoch, "other-volume") != null, "Incomplete list admitted"); snapshot.Complete = true;
                using (operations.Reserve(new[] { 702 })) Check(state.Unavailable(d.Scan(), d.Epoch, "other-volume") != null, "Busy target admitted"); d.Versions.Disable(); Check(!state.HasPrimary && state.Unavailable(d.Scan(), d.Epoch, "other-volume") != null, "Lost tracking admitted");
            } });
            Test("concurrent storage attaches import resume verify share but excludes eject and missing owners", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); foreach (var kind in new[] { TrayOperationKind.Import, TrayOperationKind.Resume, TrayOperationKind.Verify, TrayOperationKind.Share }) { var state = ConcurrentState(f, d, new Operations(), kind); Check(state.HasPrimary, "Safe primary kind unavailable: " + kind); }
                var blocked = ConcurrentState(f, d, new Operations(), TrayOperationKind.Eject); Check(!blocked.HasPrimary, "Nested eject admitted"); blocked.Attach(null, ScopedImportGuard(f, d)); Check(blocked.Unavailable(d.Scan(), d.Epoch, "other-volume") != null, "Missing owner admitted");
            } });
            Test("concurrent manual review serializes second reviews and finishes only its own token", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var state = ConcurrentState(f, d, new Operations()); var session = state.Begin(d.Scan(), d.Epoch, "other-volume"); var wait = state.WaitForManual;
                Check(state.Busy && !wait.IsCompleted, "Review did not hold lifecycle"); Throws<SafetyException>(() => state.Begin(d.Scan(), d.Epoch, "other-volume"));
                var foreign = new ManualStorageSession(session.PrimaryGuard, d.State.Volumes[2], session.Plan); state.Finish(foreign); Check(state.Busy && !wait.IsCompleted, "Foreign callback ended active review");
                state.ClearPrimary(); Check(state.Busy && !state.HasPrimary, "Primary completion cleared secondary work"); state.Finish(session); Check(!state.Busy && wait.IsCompleted && state.WaitForManual.IsCompleted, "Secondary terminal did not release wait");
            } });
            Test("concurrent session keeps immutable confirmed removal plan", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var state = ConcurrentState(f, d, new Operations()); var session = state.Begin(d.Scan(), d.Epoch, "other-volume"); var changed = session.Plan; changed.Disks[0] = 700; changed.TargetId = "source-usb"; changed.Affected[0] = "changed";
                Check(session.Plan.Disks.Single() == 702 && session.Plan.TargetId == "other-usb" && session.Plan.Affected[0] != "changed", "Review plan mutated"); state.Finish(session);
            } });
            Test("concurrent service requires explicit confirmation and rejects closed review", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var operations = new Operations(); var state = ConcurrentState(f, d, operations); var session = state.Begin(d.Scan(), d.Epoch, "other-volume"); int factories = 0;
                var service = ConcurrentService(d, operations, validate => { factories++; return new CompletionRequestApi(validate); }); Throws<SafetyException>(() => service.Execute(session, null, CancellationToken.None, new CleanupCounts(), line => {}));
                state.Finish(session); Throws<SafetyException>(() => session.Confirm()); Throws<SafetyException>(() => service.Execute(session, null, CancellationToken.None, new CleanupCounts(), line => {})); Check(factories == 0, "Unconfirmed/closed review constructed request");
            } });
            Test("concurrent fake eject succeeds once while primary source and destination reservations stay busy", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var operations = new Operations(); var state = ConcurrentState(f, d, operations); var session = state.Begin(d.Scan(), d.Epoch, "other-volume"); session.Confirm(); CompletionRequestApi api = null;
                using (operations.Reserve(new[] { 700, 701 })) {
                    var service = ConcurrentService(d, operations, validate => api = new CompletionRequestApi(validate) { BeforeValidation = () => { Throws<SafetyException>(() => operations.Reserve(new[] { 702 })); d.Event("other-usb-unrelated-child"); } });
                    Check(service.Execute(session, null, CancellationToken.None, new CleanupCounts(), line => {}).Success && api.Requests == 1, "Unrelated request blocked or wrong reservation");
                    Throws<SafetyException>(() => service.Execute(session, null, CancellationToken.None, new CleanupCounts(), line => {})); Check(api.Requests == 1, "Confirmation replayed");
                    Throws<SafetyException>(() => operations.Reserve(new[] { 700, 701 }));
                }
                state.Finish(session); using (operations.Reserve(new[] { 700, 701, 702 })) {}
            } });
            Test("concurrent service refuses primary reconnect before any helper request", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var operations = new Operations(); var state = ConcurrentState(f, d, operations); var session = state.Begin(d.Scan(), d.Epoch, "other-volume"); session.Confirm(); d.Event("source-usb"); int factories = 0;
                Throws<SafetyException>(() => ConcurrentService(d, operations, validate => { factories++; return new CompletionRequestApi(validate); }).Execute(session, null, CancellationToken.None, new CleanupCounts(), line => {})); Check(factories == 0, "Changed primary reached helper request"); state.Finish(session);
            } });
            Test("concurrent native boundary refuses changed target primary and expired session", delegate {
                for (int variant = 0; variant < 3; variant++) using (var f = new Fixture()) {
                    var d = new ScopedDiscovery(f); var operations = new Operations(); var state = ConcurrentState(f, d, operations); var session = state.Begin(d.Scan(), d.Epoch, "other-volume"); session.Confirm(); CompletionRequestApi api = null; int which = variant;
                    var service = ConcurrentService(d, operations, validate => api = new CompletionRequestApi(validate) { BeforeValidation = () => { if (which == 0) d.Event("other-disk"); else if (which == 1) d.Event("destination-disk"); else state.Finish(session); } });
                    Throws<SafetyException>(() => service.Execute(session, null, CancellationToken.None, new CleanupCounts(), line => {})); Check(api.Requests == 0, "Final boundary accepted changed authority"); state.Finish(session); using (operations.Reserve(new[] { 702 })) {}
                }
            });
            Test("concurrent target cancellation before request leaves primary token untouched", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var operations = new Operations(); var state = ConcurrentState(f, d, operations); var session = state.Begin(d.Scan(), d.Epoch, "other-volume"); session.Confirm(); using (var primaryCancel = new CancellationTokenSource()) using (var manualCancel = new CancellationTokenSource()) {
                    manualCancel.Cancel(); int factories = 0; Throws<OperationCanceledException>(() => ConcurrentService(d, operations, validate => { factories++; return new CompletionRequestApi(validate); }).Execute(session, null, manualCancel.Token, new CleanupCounts(), line => {}));
                    Check(!primaryCancel.IsCancellationRequested && factories == 0, "Manual cancellation changed primary or reached API");
                }
                state.Finish(session);
            } });
            Test("concurrent explicit cleanup changes only owned other-device metadata before fake eject", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); string other = d.State.Volumes[2].Mounts[0]; Directory.CreateDirectory(other); File.WriteAllText(Path.Combine(other, ".DS_Store"), "metadata fixture"); File.WriteAllText(Path.Combine(other, "photo.jpg"), "other media"); f.Put("clip.mp4", "primary original");
                var operations = new Operations(); var state = ConcurrentState(f, d, operations); var session = state.Begin(d.Scan(), d.Epoch, "other-volume"); var cleanup = MetadataCleanupPlan.Single(d.State, session.Plan, "other-volume"); session.Confirm(); CompletionRequestApi api = null; var counts = new CleanupCounts();
                using (operations.Reserve(new[] { 700, 701 })) Check(ConcurrentService(d, operations, validate => api = new CompletionRequestApi(validate)).Execute(session, cleanup, CancellationToken.None, counts, line => {}).Success, "Cleanup prevented correct fake request");
                Check(api.Requests == 1 && !File.Exists(Path.Combine(other, ".DS_Store")) && File.ReadAllText(Path.Combine(other, "photo.jpg")) == "other media" && File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == "primary original", "Cleanup escaped its selected device fixture"); state.Finish(session);
            } });
            Test("concurrent service refuses cleanup from a different device before changing any metadata", delegate { using (var f = new Fixture()) {
                f.Put(".DS_Store", "primary metadata retained"); var d = new ScopedDiscovery(f); var operations = new Operations(); var state = ConcurrentState(f, d, operations); var session = state.Begin(d.Scan(), d.Epoch, "other-volume"); session.Confirm();
                var wrong = MetadataCleanupPlan.Single(d.Scan(), EjectService.Plan(d.State, "source-volume"), "source-volume"); int factories = 0;
                Throws<SafetyException>(() => ConcurrentService(d, operations, validate => { factories++; return new CompletionRequestApi(validate); }).Execute(session, wrong, CancellationToken.None, new CleanupCounts(), line => {}));
                Check(factories == 0 && File.ReadAllText(Path.Combine(f.Source, ".DS_Store")) == "primary metadata retained", "Mismatched cleanup reached primary metadata or native request"); state.Finish(session);
            } });
            Test("concurrent cancellation at final request boundary leaves primary cancellation independent", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var operations = new Operations(); var state = ConcurrentState(f, d, operations); var session = state.Begin(d.Scan(), d.Epoch, "other-volume"); session.Confirm(); CompletionRequestApi api = null;
                using (var primary = new CancellationTokenSource()) using (var manual = new CancellationTokenSource()) {
                    var service = ConcurrentService(d, operations, validate => api = new CompletionRequestApi(validate) { BeforeValidation = () => manual.Cancel() });
                    Throws<OperationCanceledException>(() => service.Execute(session, null, manual.Token, new CleanupCounts(), line => {})); Check(api.Requests == 0 && !primary.IsCancellationRequested, "Late cancel reached request or changed primary");
                }
                state.Finish(session); using (operations.Reserve(new[] { 702 })) {}
            } });
            Test("concurrent cleanup cancellation stops fake eject and releases its own reservation", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); string other = d.State.Volumes[2].Mounts[0]; Directory.CreateDirectory(other); File.WriteAllText(Path.Combine(other, ".DS_Store"), "metadata fixture"); File.WriteAllText(Path.Combine(other, "._clip"), "metadata fixture");
                var operations = new Operations(); var state = ConcurrentState(f, d, operations); var session = state.Begin(d.Scan(), d.Epoch, "other-volume"); var cleanup = MetadataCleanupPlan.Single(d.State, session.Plan, "other-volume"); session.Confirm(); CompletionRequestApi api = null;
                using (var cancel = new CancellationTokenSource()) Throws<OperationCanceledException>(() => ConcurrentService(d, operations, validate => api = new CompletionRequestApi(validate)).Execute(session, cleanup, cancel.Token, new CleanupCounts(), line => cancel.Cancel()));
                Check(api != null && api.Requests == 0, "Cancelled cleanup requested eject"); state.Finish(session); using (operations.Reserve(new[] { 702 })) {}
            } });
            Test("concurrent tray policy enables other device while disabling protected source review", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var state = ConcurrentState(f, d, new Operations()); string reviewed = null;
                var menu = TrayStorageMenu.Build(d.Scan(), d.Epoch, false, false, false, (choice, clean) => reviewed = choice.VolumeId, choice => state.Unavailable(d.State, d.Epoch, choice.VolumeId));
                try { var actions = TrayActions(menu).ToArray(); var enabled = actions.Where(item => item.Enabled).ToArray(); Check(enabled.Length == 2 && actions.Any(item => !item.Enabled), "Protected source menu still enabled or other device absent"); enabled[0].PerformClick(); Check(reviewed == "other-volume", "Wrong enabled target"); } finally { foreach (var item in menu) item.Dispose(); }
            } });
            Test("manual storage native dialog has independent cancel and refuses close while worker runs", delegate {
                using (var primary = new CancellationTokenSource()) using (var manual = new CancellationTokenSource()) using (var dialog = new ManualStorageDialog("Fixture other device", manual)) {
                    IntPtr handle = dialog.Handle; dialog.Close(); Check(!dialog.IsDisposed && manual.IsCancellationRequested && !primary.IsCancellationRequested && !dialog.Stop.Enabled, "Close did not safely stop own worker");
                    dialog.Complete("Fixture request ended"); Check(dialog.Done.Enabled && dialog.Status.Text == "Fixture request ended", "Terminal dialog did not release close"); dialog.Close(); Check(dialog.IsDisposed, "Terminal dialog stayed locked");
                }
            });
            Test("manual storage late queued progress cannot overwrite terminal result", delegate {
                using (var cancel = new CancellationTokenSource()) using (var dialog = new ManualStorageDialog("Fixture", cancel)) {
                    IntPtr handle = dialog.Handle; dialog.PostProgress("old cleanup progress"); dialog.Complete("exact final result"); Application.DoEvents(); Check(dialog.Status.Text == "exact final result" && dialog.Done.Enabled && !dialog.Stop.Enabled, "Old callback overwrote terminal state");
                }
            });
            Test("native concurrent issue binding retains primary progress cancel and earlier attention", delegate { using (var window = new DeviceWindow(true)) using (var cancel = new CancellationTokenSource()) {
                Type type = typeof(DeviceWindow); var flags = BindingFlags.Instance | BindingFlags.NonPublic; type.GetField("working", flags).SetValue(window, true); type.GetField("cancellation", flags).SetValue(window, cancel);
                var button = (Button)type.GetField("cancel", flags).GetValue(window); button.Enabled = true; var text = (TextBox)type.GetField("status", flags).GetValue(window); text.Text = "Primary import 43%";
                var activity = (TrayActivity)type.GetField("trayActivity", flags).GetValue(window); var token = activity.Begin(TrayOperationKind.Import, "Primary", "Copying"); var issue = new AppIssue(); issue.Record("Earlier primary issue"); type.GetField("appIssue", flags).SetValue(window, issue);
                window.RecordConcurrentIssue("Other device veto"); type.GetMethod("ClearIssue", flags).Invoke(window, null);
                Check(text.Text == "Primary import 43%" && activity.Current == token && button.Enabled && !cancel.IsCancellationRequested && issue.NeedsAttention && issue.Message.Contains("Earlier primary issue") && issue.Message.Contains("Other device veto"), "Secondary failure changed primary lifecycle or lost an issue"); type.GetField("cancellation", flags).SetValue(window, null);
            } });
            Test("native secondary lifetime keeps settings blocked after primary worker finishes", delegate { using (var f = new Fixture()) using (var window = new DeviceWindow(true)) using (var manualCancel = new CancellationTokenSource()) {
                Type type = typeof(DeviceWindow); var flags = BindingFlags.Instance | BindingFlags.NonPublic; type.GetField("working", flags).SetValue(window, true); var tray = (TrayActivity)type.GetField("trayActivity", flags).GetValue(window); var owner = tray.Begin(TrayOperationKind.Import, "Primary", "Copying"); var d = new ScopedDiscovery(f); window.AttachStorageWork(owner, ScopedImportGuard(f, d));
                var state = (ConcurrentStorageState)type.GetField("concurrentStorage", flags).GetValue(window); var session = state.Begin(d.Scan(), d.Epoch, "other-volume"); type.GetMethod("FinishOperation", flags).Invoke(window, null);
                Check(state.Busy && !(bool)type.GetField("working", flags).GetValue(window) && !((Button)type.GetField("settings", flags).GetValue(window)).Enabled && !manualCancel.IsCancellationRequested, "Primary finish released secondary gate or cancelled own worker");
                state.Finish(session); type.GetMethod("UpdateActions", flags).Invoke(window, null); Check(((Button)type.GetField("settings", flags).GetValue(window)).Enabled, "Secondary terminal left settings blocked");
            } });
            Test("native storage attachment refuses a stale primary progress owner", delegate { using (var f = new Fixture()) using (var window = new DeviceWindow(true)) {
                Type type = typeof(DeviceWindow); var flags = BindingFlags.Instance | BindingFlags.NonPublic; type.GetField("working", flags).SetValue(window, true); var tray = (TrayActivity)type.GetField("trayActivity", flags).GetValue(window); var old = tray.Begin(TrayOperationKind.Import, "old", "Copying"); tray.Begin(TrayOperationKind.Share, "new", "Checking originals");
                window.AttachStorageWork(old, ScopedImportGuard(f, new ScopedDiscovery(f))); var state = (ConcurrentStorageState)type.GetField("concurrentStorage", flags).GetValue(window); Check(!state.HasPrimary, "Stale worker attached new operation protection");
            } });
        }
    }
}
