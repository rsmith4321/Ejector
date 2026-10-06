using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        sealed class ScopedDiscovery : IDiscovery, IScopedConnectionDiscovery {
            internal readonly Inventory State; internal readonly ConnectionVersions Versions = new ConnectionVersions();
            internal long CurrentEpoch = 10; internal Action AfterScan; internal string OmitStamp;
            internal ScopedDiscovery(Fixture fixture) {
                State = new Inventory { Epoch = CurrentEpoch };
                State.Disks.Add(new Disk { Number = 700, InstanceId = "source-disk", Serial = "source", TargetId = "source-usb", TargetDevInst = 70, DevInst = 700, Hotplug = true, Ancestors = new List<string> { "source-usb", "source-controller" } });
                State.Disks.Add(new Disk { Number = 701, InstanceId = "destination-disk", Serial = "destination", DevInst = 701, Ancestors = new List<string> { "destination-controller" } });
                State.Disks.Add(new Disk { Number = 702, InstanceId = "other-disk", Serial = "other", TargetId = "other-usb", TargetDevInst = 72, DevInst = 702, Hotplug = true, Ancestors = new List<string> { "other-usb", "other-controller" } });
                State.Volumes.Add(new Volume { Id = "source-volume", Serial = "source-volume-serial", Extents = "source-extents", Disks = new List<int> { 700 }, Mounts = new List<string> { fixture.Source } });
                State.Volumes.Add(new Volume { Id = "destination-volume", Serial = "destination-volume-serial", Extents = "destination-extents", Disks = new List<int> { 701 }, Mounts = new List<string> { fixture.Destination }, System = true });
                State.Volumes.Add(new Volume { Id = "other-volume", Serial = "other-volume-serial", Extents = "other-extents", Disks = new List<int> { 702 }, Mounts = new List<string> { Path.Combine(fixture.Root, "other") } });
                Versions.Begin(); Versions.Enable();
            }
            public long Epoch { get { return CurrentEpoch; } }
            public string Stamp(string[] instances) { return Versions.Stamp(instances); }
            public Inventory Scan() {
                State.Epoch = CurrentEpoch; State.GroupConnections.Clear();
                foreach (string id in State.Disks.Select(d => d.TargetId == "" ? d.InstanceId : d.TargetId).Distinct()) State.GroupConnections[id] = Stamp(WindowsDiscovery.GroupInstances(State, id));
                if (OmitStamp != null) State.GroupConnections.Remove(OmitStamp);
                if (AfterScan != null) AfterScan(); return State;
            }
            internal void Event(string instance) { Versions.Note(instance); CurrentEpoch++; }
            internal void AddSibling(int number, string target, string instance, bool destination = false) {
                State.Disks.Add(new Disk { Number = number, InstanceId = instance, Serial = instance, TargetId = target, Ancestors = new List<string> { target } });
                State.Volumes.Add(new Volume { Id = instance + "-volume", Serial = instance, Extents = instance, Disks = new List<int> { number }, Mounts = new List<string> { @"X:\" }, System = destination });
            }
        }
        static VolumeImportGuard ScopedImportGuard(Fixture f, ScopedDiscovery discovery, Func<string, string> resolver = null) {
            var state = discovery.Scan();
            return new VolumeImportGuard(discovery, f.Source, f.Destination, state.Volumes.Single(v => v.Id == "source-volume"), state.Disks.Single(d => d.Number == 700), state.Epoch,
                resolver ?? (path => path == f.Source ? "source-volume" : "destination-volume"));
        }
        static WindowsDestinationGuard ScopedDestinationGuard(Fixture f, ScopedDiscovery discovery, Func<string, string> resolver = null) {
            return new WindowsDestinationGuard(discovery, f.Destination, DestinationIdentity.Capture(discovery.Scan(), "destination-volume"), resolver ?? (path => "destination-volume"));
        }
        static void StorageConnectionTests() {
            Test("scoped import accepts unrelated device events then clears its fast recheck state", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var guard = ScopedImportGuard(f, d); d.Event("other-disk"); Check(guard.Changed, "Event did not request fresh validation"); guard.Validate(); Check(!guard.Changed, "Unrelated event was not acknowledged after fresh proof");
                d.State.Disks.RemoveAll(x => x.Number == 702); d.State.Volumes.RemoveAll(x => x.Id == "other-volume"); d.Event("other-usb"); guard.Validate(); Check(!guard.Changed && guard.ProtectedDisks.SequenceEqual(new[] { 700, 701 }), "Unrelated disappearance expanded or stopped protection");
            } });
            Test("scoped transfer survives unrelated fake eject while source and destination remain reserved", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", new string('I', 2200000)); var d = new ScopedDiscovery(f); var guard = ScopedImportGuard(f, d); var operations = new Operations(); bool requested = false;
                var result = new VerifiedImporter(operations, path => UInt64.MaxValue, line => {}).Run(f.Source, f.Source, f.Destination, "source-volume", guard, CancellationToken.None, line => {
                    if (!line.StartsWith("Copying", StringComparison.Ordinal)) return;
                    Throws<SafetyException>(() => operations.Reserve(new[] { 700 })); Throws<SafetyException>(() => operations.Reserve(new[] { 701 }));
                    var plan = EjectService.Plan(d.Scan(), "other-volume"); var api = new FakeEject { BeforeRequest = () => d.Event("other-usb") }; var eject = new EjectService(d, api, operations).Execute(plan);
                    requested = eject.Success && api.Calls == 1;
                });
                Check(requested && result.Files == 1 && result.HasSelectedMedia && File.ReadAllText(Saved(result, "clip.mp4")) == new string('I', 2200000) && File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == new string('I', 2200000), "Unrelated fake eject stopped or corrupted transfer");
                using (operations.Reserve(new[] { 700, 701, 702 })) {}
            } });
            foreach (string instance in new[] { "source-disk", "source-usb", "source-controller", "destination-disk", "destination-controller" }) { string changed = instance;
                Test("scoped import refuses same-identity reconnect event on " + changed, delegate { using (var f = new Fixture()) {
                    var d = new ScopedDiscovery(f); var guard = ScopedImportGuard(f, d); d.Event(changed); Check(guard.Changed, "Protected event missed"); Throws<SafetyException>(() => guard.Validate());
                } });
            }
            Test("scoped import detects protected version change even without a global epoch change", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var guard = ScopedImportGuard(f, d); d.Versions.Note("source-disk"); Check(guard.Changed, "Fast proof missed a protected version"); Throws<SafetyException>(() => guard.Validate());
            } });
            Test("scoped import tracking loss refuses rather than reverting to weaker event evidence", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var guard = ScopedImportGuard(f, d); d.Versions.Disable(); Check(guard.Changed, "Tracking loss missed"); Throws<SafetyException>(() => guard.Validate());
            } });
            Test("unavailable scoped tracking retains conservative global event refusal", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); d.Versions.Disable(); var guard = ScopedImportGuard(f, d); guard.Validate(); d.CurrentEpoch++; Throws<SafetyException>(() => guard.Validate());
            } });
            Test("import discovery without live instance proof cannot ignore unrelated epochs", delegate { using (var f = new Fixture()) {
                var scoped = new ScopedDiscovery(f); var state = scoped.Scan(); var d = new FakeDiscovery { State = state }; var guard = new VolumeImportGuard(d, f.Source, f.Destination, state.Volumes[0], state.Disks[0], state.Epoch, path => path == f.Source ? "source-volume" : "destination-volume");
                guard.Validate(); state.Epoch++; Throws<SafetyException>(() => guard.Validate());
            } });
            Test("partially tracked source destination set retains conservative event refusal", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f) { OmitStamp = "destination-disk" }; var guard = ScopedImportGuard(f, d); guard.Validate(); d.Event("other-usb"); Throws<SafetyException>(() => guard.Validate());
            } });
            Test("fallback proof still refuses new unmounted sibling without an epoch event", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); d.Versions.Disable(); var guard = ScopedImportGuard(f, d); d.State.Disks.Add(new Disk { Number = 703, InstanceId = "unmounted-sibling", TargetId = "source-usb", Ancestors = new List<string> { "source-usb" } });
                Throws<SafetyException>(() => guard.Validate());
            } });
            Test("scoped import refuses added source sibling LUN and hidden sibling volume", delegate {
                foreach (bool newDisk in new[] { false, true }) using (var f = new Fixture()) {
                    var d = new ScopedDiscovery(f); var guard = ScopedImportGuard(f, d);
                    if (newDisk) d.AddSibling(703, "source-usb", "new-LUN"); else d.State.Volumes.Add(new Volume { Id = "hidden", Serial = "hidden", Disks = new List<int> { 700 } });
                    d.Event("other-usb"); Throws<SafetyException>(() => guard.Validate());
                }
            });
            Test("scoped import includes existing source sibling LUNs in proof and reservations", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); d.AddSibling(703, "source-usb", "source-sibling"); var guard = ScopedImportGuard(f, d); Check(guard.ProtectedDisks.OrderBy(x => x).SequenceEqual(new[] { 700, 701, 703 }), "Sibling not reserved");
                int[] returned = guard.ProtectedDisks; returned[0] = 702; Check(guard.ProtectedDisks.Contains(700), "Caller altered protection"); d.Event("source-sibling"); Throws<SafetyException>(() => guard.Validate());
            } });
            Test("scoped import refuses protected serial extents devnode mount and disk-permission changes", delegate {
                Action<ScopedDiscovery>[] changes = { d => d.State.Volumes[0].Serial = "replacement", d => d.State.Volumes[1].Extents = "different", d => d.State.Disks[0].DevInst++, d => d.State.Volumes[0].Mounts.Add(@"Y:\"), d => d.State.Disks[0].Hotplug = false, d => d.State.Disks[1].Bus = 7 };
                foreach (var change in changes) using (var f = new Fixture()) { var d = new ScopedDiscovery(f); var guard = ScopedImportGuard(f, d); change(d); d.Event("other-usb"); Throws<SafetyException>(() => guard.Validate()); }
            });
            Test("scoped import refuses source path rebound to another volume", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); bool rebound = false; var guard = ScopedImportGuard(f, d, path => path == f.Source ? (rebound ? "other-volume" : "source-volume") : "destination-volume"); rebound = true; Throws<SafetyException>(() => guard.Validate());
            } });
            Test("scoped import refuses incomplete inventory and event races during scan", delegate {
                foreach (bool race in new[] { false, true }) using (var f = new Fixture()) { var d = new ScopedDiscovery(f); var guard = ScopedImportGuard(f, d); if (race) d.AfterScan = () => d.CurrentEpoch++; else d.State.Complete = false; Throws<SafetyException>(() => guard.Validate()); }
            });
            Test("scoped proof constructor refuses stale live stamps and missing group coverage", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var state = d.Scan(); d.Versions.Note("source-disk"); Throws<SafetyException>(() => new StorageConnectionProof(d, state, new[] { 700, 701 }));
                state = d.Scan(); d.State.Disks[0].Ancestors.Clear(); Throws<SafetyException>(() => new StorageConnectionProof(d, state, new[] { 700, 701 }));
            } });
            Test("scoped destination history guard accepts unrelated source eject and rejects destination reconnect", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var guard = ScopedDestinationGuard(f, d); d.Event("source-usb"); guard.Validate(); Check(!guard.Changed && guard.ProtectedDisks.SequenceEqual(new[] { 701 }), "Offline guard protected absent source or missed recheck"); d.Event("destination-disk"); Throws<SafetyException>(() => guard.Validate());
            } });
            Test("scoped removable destination guard protects all destination sibling LUNs", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); d.State.Disks[1].TargetId = "destination-usb"; d.State.Disks[1].Ancestors.Add("destination-usb"); d.AddSibling(703, "destination-usb", "destination-sibling");
                var guard = ScopedDestinationGuard(f, d); Check(guard.ProtectedDisks.OrderBy(x => x).SequenceEqual(new[] { 701, 703 }), "Destination sibling unreserved"); d.Event("other-usb"); guard.Validate(); d.Event("destination-sibling"); Throws<SafetyException>(() => guard.Validate());
            } });
            Test("destination guard without tracking preserves conservative global event refusal", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); d.Versions.Disable(); var guard = ScopedDestinationGuard(f, d); guard.Validate(); d.CurrentEpoch++; Throws<SafetyException>(() => guard.Validate());
            } });
            Test("scoped destination refuses added sibling volume path rebinding or tracking loss", delegate {
                for (int variant = 0; variant < 3; variant++) using (var f = new Fixture()) {
                    var d = new ScopedDiscovery(f); bool rebound = false; var guard = ScopedDestinationGuard(f, d, path => rebound ? "other-volume" : "destination-volume");
                    if (variant == 0) d.State.Volumes.Add(new Volume { Id = "hidden-destination", Serial = "hidden", Disks = new List<int> { 701 } }); else if (variant == 1) rebound = true; else d.Versions.Disable();
                    Throws<SafetyException>(() => guard.Validate());
                }
            });
            Test("scoped protected reconnect during transfer retains original and publishes no completion", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", new string('I', 2200000)); var d = new ScopedDiscovery(f); var guard = ScopedImportGuard(f, d); var operations = new Operations(); bool eventSent = false;
                Throws<SafetyException>(() => new VerifiedImporter(operations, path => UInt64.MaxValue, line => {}).Run(f.Source, f.Source, f.Destination, "source-volume", guard, CancellationToken.None, line => { if (line.StartsWith("Copying", StringComparison.Ordinal)) { d.Event("source-usb"); eventSent = true; } }));
                Check(eventSent && File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == new string('I', 2200000), "Protected interruption changed source or not exercised"); NoRemovalCompletion(f); using (operations.Reserve(new[] { 700, 701 })) {}
            } });
        }
    }
}
