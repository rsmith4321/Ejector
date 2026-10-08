using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        sealed class FakePowerNotifications : IPowerNotificationApi {
            internal int Registers, Unregisters; internal IntPtr Window, LastRegistration;
            internal bool Missing, RegisterThrows, UnregisterFails, UnregisterThrows;
            public IntPtr Register(IntPtr window) { Registers++; Window = window; if (RegisterThrows) throw new Exception("Fake register failure"); return Missing ? IntPtr.Zero : new IntPtr(100 + Registers); }
            public bool Unregister(IntPtr registration) { Unregisters++; LastRegistration = registration; if (UnregisterThrows) throw new Exception("Fake unregister failure"); return !UnregisterFails; }
        }
        static void FakePower(ScopedDiscovery discovery, long powerEvent) {
            if (discovery.Versions.PowerEvent(powerEvent)) discovery.CurrentEpoch++;
            discovery.State.Complete = !discovery.Versions.Suspended;
        }
        static void PowerConnectionTests() {
            Test("power epoch publication holds the stamp gate until retirement is complete", delegate {
                var versions = new ConnectionVersions(); versions.Begin(); versions.Enable(); string prior = versions.Stamp(new[] { "disk" });
                using (var inBoundary = new ManualResetEvent(false)) using (var releaseBoundary = new ManualResetEvent(false)) using (var readerStarted = new ManualResetEvent(false)) using (var readerFinished = new ManualResetEvent(false)) {
                    string observed = null; long epoch = 0; Exception workerFailure = null;
                    var writer = new Thread(() => { try { versions.PowerEvent(18, () => { inBoundary.Set(); if (!releaseBoundary.WaitOne(5000)) throw new Exception("Fixture boundary release timed out"); Interlocked.Increment(ref epoch); }); } catch (Exception ex) { workerFailure = ex; } });
                    var reader = new Thread(() => { readerStarted.Set(); observed = versions.Stamp(new[] { "disk" }); readerFinished.Set(); });
                    writer.Start();
                    try { Check(inBoundary.WaitOne(5000), "Power fixture did not reach retirement"); reader.Start(); Check(readerStarted.WaitOne(5000) && !readerFinished.WaitOne(100), "Stamp escaped before epoch publication"); }
                    finally { releaseBoundary.Set(); Check(writer.Join(5000), "Owned boundary worker did not finish"); if (reader.ThreadState != ThreadState.Unstarted) Check(reader.Join(5000), "Owned reader did not finish"); }
                    Check(workerFailure == null && epoch == 1 && observed != null && observed != prior, "Reader missed the complete retirement");
                }
            });
            Test("power subscription owns registration and releases it once", delegate {
                var api = new FakePowerNotifications(); var subscription = new PowerNotifications(api);
                Check(subscription.Start(new IntPtr(17)) && subscription.Available && api.Window == new IntPtr(17), "Owned window not subscribed");
                subscription.Dispose(); subscription.Dispose(); Check(!subscription.Available && api.Registers == 1 && api.Unregisters == 1 && api.LastRegistration == new IntPtr(101), "Registration ownership leaked or repeated");
                Throws<ObjectDisposedException>(() => subscription.Start(new IntPtr(18)));
            });
            Test("power subscription does not register a missing window", delegate {
                var api = new FakePowerNotifications(); using (var subscription = new PowerNotifications(api)) {
                    Check(!subscription.Start(IntPtr.Zero) && !subscription.Available && api.Registers == 0, "Missing HWND registered");
                }
            });
            foreach (bool throws in new[] { false, true }) { bool exception = throws;
                Test("power registration failure closes admission " + exception, delegate {
                    var api = new FakePowerNotifications { Missing = !exception, RegisterThrows = exception };
                    using (var subscription = new PowerNotifications(api)) {
                        Check(!subscription.Start(new IntPtr(17)) && !subscription.Available, "Failed subscription appeared available");
                        api.Missing = api.RegisterThrows = false; Check(subscription.Start(new IntPtr(18)) && api.Registers == 2, "Fresh registration could not recover");
                    }
                });
            }
            foreach (bool throws in new[] { false, true }) { bool exception = throws;
                Test("power unregister failure retains ownership and blocks replacement " + exception, delegate {
                    var api = new FakePowerNotifications(); using (var subscription = new PowerNotifications(api)) {
                        Check(subscription.Start(new IntPtr(17)), "Initial registration failed");
                        api.UnregisterFails = !exception; api.UnregisterThrows = exception;
                        Check(!subscription.Stop() && !subscription.Available && !subscription.Start(new IntPtr(18)) && api.Registers == 1, "Failed release permitted a second owner");
                        api.UnregisterFails = api.UnregisterThrows = false;
                        Check(subscription.Stop() && subscription.Start(new IntPtr(19)) && api.Registers == 2, "Explicit successful release did not allow retry");
                    }
                });
            }
            Test("power window recreation retires prior subscription before registering", delegate {
                var api = new FakePowerNotifications(); using (var subscription = new PowerNotifications(api)) {
                    Check(subscription.Start(new IntPtr(17)) && subscription.Start(new IntPtr(18)), "Recreated subscription failed");
                    Check(api.Registers == 2 && api.Unregisters == 1 && api.LastRegistration == new IntPtr(101), "Prior registration not retired first");
                }
            });
            Test("suspend blocks tracking across enable and monitor restart until resume", delegate {
                var versions = new ConnectionVersions(); versions.Begin(); versions.Enable(); string old = versions.Stamp(new[] { "disk" });
                Check(versions.PowerEvent(4) && versions.Suspended && !versions.Available && versions.Stamp(new[] { "disk" }) == "", "Suspend kept proof");
                versions.Enable(); Check(!versions.Available, "Enable cleared suspend"); versions.Begin(); versions.Enable(); Check(!versions.Available && versions.Suspended, "Monitor restart cleared suspend");
                Check(versions.PowerEvent(18) && versions.Available && !versions.Suspended && versions.Stamp(new[] { "disk" }) != old, "Resume reused the old era");
            });
            Test("automatic and user resume share one proof boundary", delegate {
                var versions = new ConnectionVersions(); versions.Begin(); versions.Enable(); versions.PowerEvent(4); versions.PowerEvent(18);
                string fresh = versions.Stamp(new[] { "disk" }); Check(!versions.PowerEvent(7) && versions.Stamp(new[] { "disk" }) == fresh && versions.Available, "User activity retired the same resume twice");
                Check(versions.PowerEvent(7) && versions.Stamp(new[] { "disk" }) != fresh, "Standalone resume had no boundary");
            });
            Test("power resume does not heal tracking fault or invalid event data", delegate {
                foreach (bool invalid in new[] { false, true }) {
                    var versions = new ConnectionVersions(); versions.Begin(); versions.Enable(); if (invalid) versions.Note(""); else versions.Disable();
                    versions.PowerEvent(4); versions.PowerEvent(18); versions.PowerEvent(7); versions.Enable();
                    Check(!versions.Available && versions.Stamp(new[] { "disk" }) == "", "Power cycle healed a fault");
                }
            });
            Test("power status setting and unknown events preserve proof and epoch", delegate {
                var discovery = new WindowsDiscovery(); discovery.Instances.Versions.Begin(); discovery.Instances.Versions.Enable();
                string stamp = discovery.Instances.Versions.Stamp(new[] { "disk" });
                foreach (long powerEvent in new long[] { 10, 32787, 0, -1, Int64.MaxValue }) Check(!discovery.HandlePowerEvent(powerEvent), "Ordinary event handled as a boundary");
                Check(discovery.Epoch == 0 && discovery.Instances.Versions.Stamp(new[] { "disk" }) == stamp, "Ordinary power event retired proof");
                Check(discovery.HandlePowerEvent(18) && discovery.Epoch == 1 && discovery.HandlePowerEvent(7) && discovery.Epoch == 1, "Resume pair handling/epoch incorrect");
            });
            foreach (int powerEvent in new[] { 4, 18, 7 }) { int boundary = powerEvent;
                Test("power boundary refuses captured import and permits fresh manual retry " + boundary, delegate { using (var f = new Fixture(true)) {
                    var d = new ScopedDiscovery(f); var old = ScopedImportGuard(f, d); FakePower(d, boundary);
                    Check(old.Changed, "Old import not marked changed"); Throws<SafetyException>(() => old.Validate());
                    if (boundary == 4) { Throws<SafetyException>(() => ScopedImportGuard(f, d)); FakePower(d, 18); }
                    Throws<SafetyException>(() => old.Validate()); var fresh = ScopedImportGuard(f, d); fresh.Validate(); d.Event("other-disk"); fresh.Validate();
                    Check(!fresh.Changed, "Fresh unrelated-event validation failed");
                } });
                Test("power boundary interrupts generated copy and releases reservations " + boundary, delegate { using (var f = new Fixture(true)) {
                    f.Put("first.mp4", "first retained original"); f.Put("second.mp4", new string('P', 2200000));
                    var d = new ScopedDiscovery(f); var guard = ScopedImportGuard(f, d); var operations = new Operations(); bool signaled = false;
                    Throws<SafetyException>(() => new VerifiedImporter(operations, path => UInt64.MaxValue, line => {}).Run(f.Source, f.Source, f.Destination, "source-volume", guard, CancellationToken.None, line => {
                        if (!line.StartsWith("Copying", StringComparison.Ordinal) || signaled) return; signaled = true; FakePower(d, boundary);
                    }));
                    Check(signaled && File.ReadAllText(Path.Combine(f.Source, "first.mp4")) == "first retained original" && File.ReadAllText(Path.Combine(f.Source, "second.mp4")) == new string('P', 2200000), "Power interruption changed originals");
                    NoRemovalCompletion(f); using (operations.Reserve(new[] { 700, 701 })) {}
                    if (boundary == 4) FakePower(d, 18);
                    var retry = new VerifiedImporter(operations, path => UInt64.MaxValue, line => {}).Run(f.Source, f.Source, f.Destination, "source-volume", ScopedImportGuard(f, d), CancellationToken.None, line => {});
                    Check(retry.Files == 2 && retry.RemovedOriginals == 0 && File.ReadAllText(Saved(retry, "first.mp4")) == "first retained original", "Fresh retry failed");
                } });
                Test("power boundary retires completion and blocks fake removal " + boundary, delegate { using (var f = new Fixture(true)) {
                    var d = new ScopedDiscovery(f); var registry = new ImportCompletionRegistry(); var old = ScopedCompletion(f, d); registry.Insert(old);
                    FakePower(d, boundary); Check(registry.InvalidateNotifications().SequenceEqual(new[] { old.Id }), "Completion survived boundary");
                    if (boundary == 4) FakePower(d, 18); var api = new FakeEject();
                    Throws<SafetyException>(() => new CompletedEjectService(registry, d, api, new Operations(), path => "source-volume").Execute(old.Id));
                    Check(api.Calls == 0 && registry.Get(old.Id) == null, "Old completion reached fake removal or returned after resume");
                    var fresh = ScopedCompletion(f, d); registry.Insert(fresh); Check(registry.Matches(fresh.Id, d.Scan(), "source-volume", f.Source), "Fresh completion unavailable");
                } });
            }
            Test("resume after a published generated copy retains exact saved bytes and receipt", delegate { using (var f = new Fixture(true)) {
                f.Put("clip.mp4", "full-quality original"); var d = new ScopedDiscovery(f); var operations = new Operations();
                var result = new VerifiedImporter(operations, path => UInt64.MaxValue, line => {}).Run(f.Source, f.Source, f.Destination, "source-volume", ScopedImportGuard(f, d), CancellationToken.None, line => {});
                string original = File.ReadAllText(Saved(result, "clip.mp4")), receipt = File.ReadAllText(result.ReceiptPath); var guard = ScopedDestinationGuard(f, d);
                FakePower(d, 4); FakePower(d, 18); Throws<SafetyException>(() => guard.Validate()); ScopedDestinationGuard(f, d).Validate();
                Check(File.ReadAllText(Saved(result, "clip.mp4")) == original && File.ReadAllText(result.ReceiptPath) == receipt && File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == original, "Power boundary changed a published result or original");
            } });
            Test("resume refuses frozen automatic request without repeating interrupted work", delegate { using (var f = new Fixture(true)) {
                var d = new ScopedDiscovery(f); d.State.Volumes[0].Group = StorageGroup.Camera; var state = d.Scan();
                var profile = AutomaticProfile(state.Volumes[0], state.Disks[0]); profile.Destination = f.Destination; profile.SavedDestination = DestinationIdentity.Capture(state, "destination-volume");
                var scheduler = new AutomaticImportScheduler(); var request = Admit(scheduler, state, profile); Check(request != null, "Initial opt-in did not admit");
                profile.VideosOnly = false; FakePower(d, 4); scheduler.PowerBoundary();
                Check(Admit(scheduler, d.Scan(), profile) == null, "Suspended import started"); Throws<SafetyException>(() => request.Connection.RequireCurrent(d.Scan()));
                FakePower(d, 18); state = d.Scan(); scheduler.Observe(state);
                Throws<SafetyException>(() => request.Connection.RequireCurrent(state));
                Check(Admit(scheduler, state, profile) == null && request.Options.VideosOnly, "Resume repeated work or changed its frozen options");
                d.Event("source-usb"); Check(Admit(scheduler, d.Scan(), profile) != null, "Later genuinely new fake connection could not follow its opt-in");
            } });
        }
    }
}
