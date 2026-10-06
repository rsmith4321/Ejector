using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static AutomaticImportRequest ScopedAutomaticRequest(Fixture f, ScopedDiscovery d, AutomaticImportScheduler scheduler = null) {
            var state = d.Scan(); var profile = AutomaticProfile(state.Volumes[0], state.Disks[0]); profile.Destination = f.Destination; profile.SavedDestination = DestinationIdentity.Capture(state, "destination-volume");
            return Admit(scheduler ?? new AutomaticImportScheduler(), state, profile);
        }
        static VolumeImportGuard BindAutomatic(Fixture f, AutomaticImportRequest request, IDiscovery d, Action<long> observed = null, Func<string, string> resolver = null) {
            return AutomaticImportAdmission.Bind(request, d, f.Source, f.Destination, observed, resolver ?? (path => path == f.Source ? "source-volume" : "destination-volume"));
        }
        static void AutomaticAdmissionTests() {
            Test("automatic source snapshot accepts newer unrelated epoch with unchanged source token", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var request = ScopedAutomaticRequest(f, d); long original = request.Connection.Epoch; d.Event("other-usb"); var fresh = request.Connection.RequireCurrent(d.Scan());
                Check(fresh.Epoch > original && fresh.Token == request.Connection.Token && fresh.Root == f.Source && request.Connection.Epoch == original, "Refresh changed original proof or rejected unrelated epoch");
            } });
            Test("automatic bound guard admits unrelated event and imports disposable data once", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "synthetic automatic recording"); var d = new ScopedDiscovery(f); var scheduler = new AutomaticImportScheduler(); var request = ScopedAutomaticRequest(f, d, scheduler); d.Event("other-usb"); long observed = -1;
                var guard = BindAutomatic(f, request, d, epoch => observed = epoch); var result = ParityImport(f, f.Source, guard, options: request.Options);
                Check(observed == d.Epoch && result.Files == 1 && File.ReadAllText(Saved(result, "clip.mp4")) == "synthetic automatic recording" && File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == "synthetic automatic recording", "Automatic guard failed or source changed");
                Check(ScopedAutomaticRequest(f, d, scheduler) == null, "Admission refreshed into a second automatic import");
            } });
            Test("automatic guard construction accepts unrelated event after path resolution begins", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var request = ScopedAutomaticRequest(f, d); bool sent = false;
                var guard = BindAutomatic(f, request, d, resolver: path => { if (!sent) { d.Event("other-usb"); sent = true; } return path == f.Source ? "source-volume" : "destination-volume"; });
                guard.Validate(); Check(sent && !guard.Changed, "Constructor still pinned unrelated global epoch");
            } });
            Test("automatic manual-selection constructor still refuses changed global selection epoch", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var request = ScopedAutomaticRequest(f, d); d.Event("other-usb");
                Throws<SafetyException>(() => new VolumeImportGuard(d, f.Source, f.Destination, request.Connection.Volume, request.Connection.Disk, request.Connection.Epoch, path => path == f.Source ? "source-volume" : "destination-volume"));
            } });
            Test("automatic guard refuses protected reconnect during its construction", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var request = ScopedAutomaticRequest(f, d); bool sent = false;
                Throws<SafetyException>(() => BindAutomatic(f, request, d, resolver: path => { if (!sent) { d.Event("source-usb"); sent = true; } return path == f.Source ? "source-volume" : "destination-volume"; })); Check(sent, "Protected event not injected");
            } });
            Test("automatic source token refuses added sibling disk or hidden partition", delegate {
                foreach (bool disk in new[] { false, true }) using (var f = new Fixture()) {
                    var d = new ScopedDiscovery(f); var request = ScopedAutomaticRequest(f, d); if (disk) d.AddSibling(703, "source-usb", "new-sibling"); else d.State.Volumes.Add(new Volume { Id = "hidden", Serial = "hidden", Disks = new System.Collections.Generic.List<int> { 700 } });
                    Throws<SafetyException>(() => request.Connection.RequireCurrent(d.Scan())); Throws<SafetyException>(() => BindAutomatic(f, request, d));
                }
            });
            Test("automatic source token refuses sibling extents and source permission changes", delegate {
                Action<ScopedDiscovery>[] changes = { d => d.State.Volumes.Single(v => v.Id == "sibling-volume").Extents = "changed", d => d.State.Disks[0].Bus = 12, d => d.State.Disks[0].Removable = true, d => d.State.Disks[0].Hotplug = false };
                foreach (var change in changes) using (var f = new Fixture()) { var d = new ScopedDiscovery(f); d.AddSibling(703, "source-usb", "sibling"); var request = ScopedAutomaticRequest(f, d); change(d); Throws<SafetyException>(() => BindAutomatic(f, request, d)); }
            });
            Test("automatic admission refuses lost source tracking and cannot bridge cached proof without live provider", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var request = ScopedAutomaticRequest(f, d); var state = d.Scan(); var cached = new FakeDiscovery { State = state }; state.Epoch++; Throws<SafetyException>(() => BindAutomatic(f, request, cached));
                d.Versions.Disable(); Throws<SafetyException>(() => BindAutomatic(f, request, d));
            } });
            Test("automatic admission retains global fallback when source epoch is unchanged", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var request = ScopedAutomaticRequest(f, d); var cached = new FakeDiscovery { State = d.Scan() }; var guard = BindAutomatic(f, request, cached); guard.Validate(); cached.State.Epoch++; Throws<SafetyException>(() => guard.Validate());
            } });
            Test("automatic admission refuses retargeted frozen source or destination without writes", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var request = ScopedAutomaticRequest(f, d);
                Throws<SafetyException>(() => AutomaticImportAdmission.Bind(request, d, Path.Combine(f.Source, "elsewhere"), f.Destination, volumeId: path => "source-volume"));
                Throws<SafetyException>(() => AutomaticImportAdmission.Bind(request, d, f.Source, Path.Combine(f.Destination, "elsewhere"), volumeId: path => "source-volume"));
                Check(Directory.GetFileSystemEntries(f.Source).Length == 0 && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Admission wrote or created folders");
            } });
            Test("automatic admission refuses changed saved destination identity at a newer unrelated epoch", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var request = ScopedAutomaticRequest(f, d); d.Event("other-usb"); d.State.Volumes[1].Serial = "replacement destination"; Throws<SafetyException>(() => BindAutomatic(f, request, d)); Check(Directory.GetFileSystemEntries(f.Destination).Length == 0, "Destination refusal wrote data");
            } });
            Test("automatic admission refuses incomplete inventory and source proof races", delegate {
                foreach (bool race in new[] { false, true }) using (var f = new Fixture()) {
                    var d = new ScopedDiscovery(f); var request = ScopedAutomaticRequest(f, d); if (race) d.AfterScan = () => d.Event("source-usb"); else d.State.Complete = false; Throws<SafetyException>(() => BindAutomatic(f, request, d));
                }
            });
            Test("automatic source selection refuses ambiguous duplicate volume identities", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var request = ScopedAutomaticRequest(f, d); d.State.Volumes.Add(d.State.Volumes[0]); Throws<SafetyException>(() => request.Connection.RequireCurrent(d.Scan()));
                Check(Directory.GetFileSystemEntries(f.Source).Length == 0 && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Ambiguous selection wrote data");
            } });
            Test("automatic preflight wait records actual observed newer epoch rather than retrying immediately", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var scheduler = new AutomaticImportScheduler(); var request = ScopedAutomaticRequest(f, d, scheduler); d.Event("other-usb"); long observed = -1;
                Throws<IOException>(() => BindAutomatic(f, request, d, epoch => observed = epoch, path => { throw new IOException("fixture unavailable destination path"); }));
                scheduler.PreflightWaiting(request, observed); Check(observed == d.Epoch && ScopedAutomaticRequest(f, d, scheduler) == null, "Old wait epoch caused an immediate retry loop");
                d.Event("other-disk"); Check(ScopedAutomaticRequest(f, d, scheduler) != null && ScopedAutomaticRequest(f, d, scheduler) == null, "Later event lost single preflight retry semantics");
            } });
            Test("automatic changed source token cannot rewrite a newer connection attempt's waiting state", delegate { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); var scheduler = new AutomaticImportScheduler(); var old = ScopedAutomaticRequest(f, d, scheduler); d.Event("source-usb"); var current = ScopedAutomaticRequest(f, d, scheduler); Check(current != null, "Reconnect not admitted");
                scheduler.PreflightWaiting(old, d.Epoch); d.Event("other-usb"); Check(ScopedAutomaticRequest(f, d, scheduler) == null, "Old request gave a started newer connection retry authority");
            } });
        }
    }
}
