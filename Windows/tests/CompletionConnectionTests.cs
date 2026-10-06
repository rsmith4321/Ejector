using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static ImportCompletion ScopedCompletion(Fixture f, ScopedDiscovery discovery, string volumeId = "source-volume") {
            var state = discovery.Scan(); var volume = state.Volumes.Single(v => v.Id == volumeId);
            return new ImportCompletion(new ImportCompletionOrigin(state, volumeId, volume.Mounts[0], discovery), volumeId, f.Destination, f.Destination, DestinationIdentity.Capture(state, "destination-volume"), true, "Fixture import complete");
        }
        sealed class CompletionRequestApi : IEjectApi {
            internal Action BeforeValidation; internal int Requests; readonly Action validate;
            internal CompletionRequestApi(Action validate) { this.validate = validate; }
            public EjectResult Request(uint node, string instance) { if (BeforeValidation != null) BeforeValidation(); validate(); Requests++; return new EjectResult { Success = true }; }
        }
        static void CompletionConnectionTests() {
            Test("scoped completion survives unrelated notification and fresh inventory epoch", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var entry = ScopedCompletion(f, d); var registry = new ImportCompletionRegistry(); registry.Insert(entry);
                d.Event("other-usb"); Check(registry.InvalidateNotifications().Length == 0 && registry.Reconcile(d.Scan(), path => "source-volume").Length == 0 && registry.Matches(entry.Id, d.Scan(), "source-volume", f.Source), "Unrelated event removed process source choice");
            } });
            Test("scoped completion choice expires permanently on source reconnect with unchanged identities", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var entry = ScopedCompletion(f, d); var registry = new ImportCompletionRegistry(); registry.Insert(entry); d.Event("source-usb");
                Check(registry.InvalidateNotifications().SequenceEqual(new[] { entry.Id }) && registry.Get(entry.Id) == null, "Protected reconnect kept choice"); registry.Reconcile(d.Scan(), path => "source-volume"); Check(registry.Entries.Length == 0, "Scan restored expired authority");
            } });
            Test("scoped completion missing source reconciles away and cannot return after same-volume reappearance", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var entry = ScopedCompletion(f, d); var registry = new ImportCompletionRegistry(); registry.Insert(entry); var removed = d.State.Volumes[0]; d.State.Volumes.Remove(removed);
                Check(registry.Reconcile(d.Scan(), path => "source-volume").SequenceEqual(new[] { entry.Id }), "Missing source kept choice"); d.State.Volumes.Add(removed); registry.Reconcile(d.Scan(), path => "source-volume");
                var api = new FakeEject(); Throws<SafetyException>(() => new CompletedEjectService(registry, d, api, new Operations(), path => "source-volume").Execute(entry.Id)); Check(api.Calls == 0, "Reappearing source restored authority");
            } });
            Test("scoped completion tracking loss expires choice without weaker fallback", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var entry = ScopedCompletion(f, d); var registry = new ImportCompletionRegistry(); registry.Insert(entry); d.Versions.Disable();
                Check(registry.InvalidateNotifications().SequenceEqual(new[] { entry.Id }), "Tracking loss kept token"); d.Versions.Begin(); d.Versions.Enable(); Check(!entry.Origin.SourceCurrent(d.Scan(), "source-volume", f.Source) && registry.Get(entry.Id) == null, "Fresh tracker restored old connection");
            } });
            Test("completion without scoped proof still expires on any notification", delegate { using (var f = new Fixture()) {
                var state = State(); var entry = Completion(state, f, true); var registry = new ImportCompletionRegistry(); registry.Insert(entry); Check(registry.InvalidateNotifications().SequenceEqual(new[] { entry.Id }), "Legacy/global choice ignored unknown notification");
            } });
            Test("scoped completion registry invalidates only confirmed source removal disks", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var registry = new ImportCompletionRegistry(); var source = ScopedCompletion(f, d); var other = ScopedCompletion(f, d, "other-volume"); registry.Insert(source); registry.Insert(other);
                Check(registry.InvalidateDisks(new[] { 701 }).Length == 0 && registry.Entries.Length == 2, "Destination-only action invalidated source authority");
                Check(registry.InvalidateDisks(new[] { 702 }).SequenceEqual(new[] { other.Id }) && registry.Get(source.Id) != null, "Unrelated confirmed removal invalidated source");
                Check(registry.InvalidateDisks(new[] { 700 }).SequenceEqual(new[] { source.Id }), "Source action did not expire choice");
            } });
            Test("scoped completion registry treats related sibling LUN removal as source removal", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); d.AddSibling(703, "source-usb", "source-sibling"); var entry = ScopedCompletion(f, d); var registry = new ImportCompletionRegistry(); registry.Insert(entry);
                Check(registry.InvalidateDisks(new[] { 703 }).SequenceEqual(new[] { entry.Id }), "Sibling source action left token");
            } });
            Test("scoped completion reconciliation refuses unreadable root and incomplete topology", delegate {
                foreach (bool unreadable in new[] { false, true }) using (var f = new Fixture()) {
                    var d = new ScopedDiscovery(f); var entry = ScopedCompletion(f, d); var registry = new ImportCompletionRegistry(); registry.Insert(entry); if (!unreadable) d.State.Complete = false;
                    Check(registry.Reconcile(d.Scan(), path => { if (unreadable) throw new IOException("Fixture unreadable root"); return "source-volume"; }).SequenceEqual(new[] { entry.Id }), "Unavailable identity kept permission");
                }
            });
            Test("scoped completion refuses same-ID changed mount serial and hidden sibling", delegate {
                Action<ScopedDiscovery>[] changes = { d => d.State.Volumes[0].Serial = "replacement", d => d.State.Volumes[0].Mounts.Add(@"Y:\"), d => d.State.Volumes.Add(new Volume { Id = "hidden", Serial = "hidden", Disks = new System.Collections.Generic.List<int> { 700 } }) };
                foreach (var change in changes) using (var f = new Fixture()) { var d = new ScopedDiscovery(f); var entry = ScopedCompletion(f, d); change(d); d.Event("other-usb"); Check(!entry.Origin.SourceCurrent(d.Scan(), "source-volume", f.Source), "Changed source used unrelated epoch exemption"); }
            });
            Test("scoped blocked-removal completion retains connection and folder without eject authority", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); d.State.Disks[0].TargetId = ""; var entry = ScopedCompletion(f, d); var registry = new ImportCompletionRegistry(); registry.Insert(entry); d.Event("other-usb");
                Check(entry.Origin.Plan == null && entry.Origin.SourceCurrent(d.Scan(), "source-volume", f.Source) && !entry.Origin.Matches(d.Scan(), "source-volume", f.Source) && registry.InvalidateNotifications().Length == 0 && registry.Reconcile(d.Scan(), path => "source-volume").Length == 0, "Blocked removal lost folder or gained permission");
                var api = new FakeEject(); Throws<SafetyException>(() => new CompletedEjectService(registry, d, api, new Operations(), path => "source-volume").Execute(entry.Id)); Check(api.Calls == 0, "Blocked source requested eject");
            } });
            Test("scoped post-import explicit eject obtains fresh epoch after unrelated event and consumes once", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var entry = ScopedCompletion(f, d); var registry = new ImportCompletionRegistry(); registry.Insert(entry); d.Event("other-usb"); var api = new FakeEject();
                var service = new CompletedEjectService(registry, d, api, new Operations(), path => "source-volume"); Check(service.Execute(entry.Id).Success && api.Calls == 1 && api.LastId == "source-usb" && registry.Get(entry.Id) == null, "Fresh action used old epoch or wrong device");
                Throws<SafetyException>(() => service.Execute(entry.Id)); Check(api.Calls == 1, "Consumed token repeated");
            } });
            Test("scoped post-import busy reservation preserves explicit choice for later retry", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var entry = ScopedCompletion(f, d); var registry = new ImportCompletionRegistry(); registry.Insert(entry); d.Event("other-usb"); var api = new FakeEject(); var operations = new Operations(); var service = new CompletedEjectService(registry, d, api, operations, path => "source-volume");
                using (operations.Reserve(new[] { 700 })) Throws<SafetyException>(() => service.Execute(entry.Id)); Check(api.Calls == 0 && registry.Get(entry.Id) != null, "Busy path consumed choice"); Check(service.Execute(entry.Id).Success && api.Calls == 1, "Busy release did not allow manual retry");
            } });
            Test("scoped completion final native callback refuses reconnect after earlier request checks", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var entry = ScopedCompletion(f, d); var registry = new ImportCompletionRegistry(); registry.Insert(entry); CompletionRequestApi api = null;
                var service = new CompletedEjectService(registry, d, validate => api = new CompletionRequestApi(validate) { BeforeValidation = () => d.Event("source-usb") }, new Operations(), path => "source-volume");
                Throws<SafetyException>(() => service.Execute(entry.Id)); Check(api != null && api.Requests == 0 && registry.Get(entry.Id) == null, "Final reconnect reached removal or replay token survived");
            } });
            Test("scoped completion final native callback accepts proved unrelated event", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var entry = ScopedCompletion(f, d); var registry = new ImportCompletionRegistry(); registry.Insert(entry); CompletionRequestApi api = null;
                var service = new CompletedEjectService(registry, d, validate => api = new CompletionRequestApi(validate) { BeforeValidation = () => d.Event("other-usb") }, new Operations(), path => "source-volume");
                Check(service.Execute(entry.Id).Success && api.Requests == 1 && registry.Get(entry.Id) == null, "Unrelated native-boundary event blocked proven source");
            } });
            Test("scoped completion reviewed cleanup uses fresh source proof after unrelated earlier event", delegate { using (var f = new Fixture()) {
                f.Put(".DS_Store", "owned disposable metadata"); f.Put("clip.mp4", "retained media"); var d = new ScopedDiscovery(f); d.State.Volumes[0].Group = StorageGroup.Camera; var entry = ScopedCompletion(f, d); var registry = new ImportCompletionRegistry(); registry.Insert(entry); d.Event("other-usb"); var api = new FakeEject(); var counts = new CleanupCounts();
                var cleanup = MetadataCleanupPlan.Single(d.Scan(), entry.Origin.Plan, "source-volume");
                var result = new CompletedEjectService(registry, d, api, new Operations(), path => "source-volume").ExecuteReviewed(entry.Id, (initial, validate) => { cleanup.Preflight(initial, path => "source-volume"); cleanup.Clean(validate, path => "source-volume", CancellationToken.None, counts, line => {}); });
                Check(result.Success && api.Calls == 1 && !File.Exists(Path.Combine(f.Source, ".DS_Store")) && File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == "retained media", "Cleanup used stale import epoch or changed user fixture media");
            } });
            Test("scoped completion cancellation before consume preserves manual choice and requests nothing", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var entry = ScopedCompletion(f, d); var registry = new ImportCompletionRegistry(); registry.Insert(entry); var api = new FakeEject(); using (var cancel = new CancellationTokenSource()) {
                    cancel.Cancel(); Throws<OperationCanceledException>(() => new CompletedEjectService(registry, d, api, new Operations(), path => "source-volume").Execute(entry.Id, state => {}, cancel.Token)); Check(api.Calls == 0 && registry.Get(entry.Id) != null, "Early cancellation consumed choice");
                }
            } });
            Test("scoped native completion notification binding preserves unrelated dialog and disables changed source", delegate { using (var f = new Fixture()) using (var window = new DeviceWindow(true)) {
                var d = new ScopedDiscovery(f); var entry = ScopedCompletion(f, d); var registry = (ImportCompletionRegistry)typeof(DeviceWindow).GetField("completions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window); registry.Insert(entry);
                using (var dialog = new ImportCompletionDialog(entry, true)) {
                    typeof(DeviceWindow).GetField("activeCompletion", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, dialog); var notified = typeof(DeviceWindow).GetMethod("ExpireNotifiedCompletions", BindingFlags.NonPublic | BindingFlags.Instance);
                    d.Event("other-usb"); notified.Invoke(window, null); Check(dialog.Eject.Enabled && registry.Get(entry.Id) != null, "Unrelated notification disabled native choice"); d.Event("source-usb"); notified.Invoke(window, null); Check(!dialog.Eject.Enabled && registry.Get(entry.Id) == null, "Changed source retained native action");
                    typeof(DeviceWindow).GetField("activeCompletion", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, null);
                }
            } });
        }
    }
}
