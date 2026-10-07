using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEject {
    public static partial class Tests {
        sealed class ModernStorageDiscovery : IDiscovery {
            internal Inventory State = ModernStorageState(); internal long Version = 1; internal int Calls;
            internal Func<Inventory> DuringScan;
            public long Epoch { get { return Interlocked.Read(ref Version); } }
            public Inventory Scan() { Interlocked.Increment(ref Calls); return DuringScan == null ? State : DuringScan(); }
        }
        sealed class ModernRemovalApi : IEjectApi {
            internal int Calls; internal Action Validate, BeforeValidate;
            internal EjectResult Result = new EjectResult { Success = true };
            public EjectResult Request(uint node, string instance) {
                if (BeforeValidate != null) BeforeValidate(); Validate();
                Check(node == 20 && instance == "modern-usb", "Wrong removal node"); Calls++; return Result;
            }
        }
        static Inventory ModernStorageState(string root = @"E:\") {
            var state = new Inventory { Epoch = 1 };
            state.Disks.Add(new Disk { Number = 7, DevInst = 10, InstanceId = "modern-disk", Serial = "modern-serial", TargetId = "modern-usb", TargetDevInst = 20, Ancestors = new List<string> { "modern-disk", "modern-usb" } });
            state.Volumes.Add(new Volume { Id = "modern-volume", Label = "Fixture card", Serial = "ABC", Extents = "7:0:1000", Disks = new List<int> { 7 }, Mounts = new List<string> { root }, Group = StorageGroup.Camera });
            return state;
        }
        static ManualEjectReview ModernReview(ModernStorageDiscovery discovery, ModernRemovalApi api, Operations operations = null, AppSettings settings = null, bool clean = false, Action factory = null) {
            return new ManualEjectReview(discovery.State, new TrayStorageChoice(discovery.State, discovery.State.Volumes[0], discovery.Epoch), settings ?? new AppSettings(), clean,
                discovery, operations ?? new Operations(), validate => { if (factory != null) factory(); api.Validate = validate; return api; }, path => "modern-volume");
        }
        static void StorageControllerTests() {
            Console.WriteLine("Storage fixture context: " + (SynchronizationContext.Current == null ? "none" : SynchronizationContext.Current.GetType().Name));
            Test("modern inventory coalesces active scans and publishes exact fresh state", delegate {
                var discovery = new ModernStorageDiscovery();
                using (var entered = new ManualResetEventSlim()) using (var finish = new ManualResetEventSlim())
                using (var session = new StorageInventorySession(discovery)) {
                    discovery.DuringScan = () => { entered.Set(); Check(finish.Wait(5000), "Fixture scan wait timed out"); return discovery.State; };
                    var first = session.Refresh(); Check(entered.Wait(5000) && session.Scanning && !first.IsCompleted, "No live owned scan");
                    Check(!StorageResult(session.Refresh()) && discovery.Calls == 1, "Second scan started");
                    finish.Set(); Check(StorageResult(first) && session.Current == discovery.State && !session.Pending && !session.Scanning, "Fresh scan failed publication");
                }
            });
            Test("modern notification invalidates a late worker scan even with unchanged metadata", delegate {
                var discovery = new ModernStorageDiscovery();
                using (var entered = new ManualResetEventSlim()) using (var finish = new ManualResetEventSlim())
                using (var session = new StorageInventorySession(discovery)) {
                    discovery.DuringScan = () => { entered.Set(); Check(finish.Wait(5000), "Fixture scan wait timed out"); return discovery.State; };
                    var scan = session.Refresh(); Check(entered.Wait(5000), "Scan not live"); Interlocked.Increment(ref discovery.Version);
                    Check(session.Observe() && !session.Observe() && session.Current == null, "Event was ignored or replayed");
                    finish.Set(); Check(!StorageResult(scan) && session.Pending && session.Current == null, "Late old scan revived choices");
                    discovery.DuringScan = null; discovery.State = ModernStorageState(); discovery.State.Epoch = discovery.Epoch;
                    Check(StorageResult(session.Refresh()) && !session.Pending, "Fresh retry did not recover");
                }
            });
            Test("modern scan fingerprint epoch advance is accepted only as a fresh baseline", delegate {
                var discovery = new ModernStorageDiscovery();
                using (var session = new StorageInventorySession(discovery)) {
                    discovery.DuringScan = () => { discovery.Version = 2; discovery.State.Epoch = 2; return discovery.State; };
                    Check(StorageResult(session.Refresh()) && !session.Observe() && !session.Pending, "Fresh scan-created epoch was repeatedly invalidated");
                }
            });
            Test("modern closed inventory refuses late publication and further scans", delegate {
                var discovery = new ModernStorageDiscovery();
                using (var entered = new ManualResetEventSlim()) using (var finish = new ManualResetEventSlim()) {
                    var session = new StorageInventorySession(discovery);
                    discovery.DuringScan = () => { entered.Set(); Check(finish.Wait(5000), "Fixture scan wait timed out"); return discovery.State; };
                    var scan = session.Refresh(); Check(entered.Wait(5000), "Scan not live"); session.Dispose(); finish.Set();
                    Check(!StorageResult(scan) && session.Current == null && !session.Pending && !session.Observe() && !StorageResult(session.Refresh()) && discovery.Calls == 1, "Closed session revived or scanned");
                }
            });
            Test("modern scan failure avoids an unchanged automatic retry loop", delegate {
                var discovery = new ModernStorageDiscovery();
                using (var session = new StorageInventorySession(discovery)) {
                    discovery.DuringScan = () => { throw new IOException("Owned scan failure"); };
                    Check(!StorageResult(session.Refresh()) && !session.Pending && session.Error == "Owned scan failure" && session.Current == null, "Scan error was hidden or continually retried");
                    discovery.Version++; Check(session.Observe() && session.Pending, "New connection did not request refresh");
                    discovery.DuringScan = null; discovery.State.Epoch = discovery.Epoch;
                    Check(StorageResult(session.Refresh()) && session.Error == null, "New connection could not recover");
                }
            });
            Test("modern incomplete inventory remains visible but cannot authorize removal", delegate {
                var discovery = new ModernStorageDiscovery(); discovery.State.Complete = false;
                using (var session = new StorageInventorySession(discovery)) {
                    Check(StorageResult(session.Refresh()) && session.Current != null, "Partial metadata was hidden");
                    var choice = new TrayStorageChoice(session.Current, session.Current.Volumes[0], discovery.Epoch);
                    Check(!choice.CanEject && choice.Error != null, "Incomplete inventory allowed removal");
                }
            });
            Test("modern eject cancellation and unconfirmed review never create an API", delegate {
                var discovery = new ModernStorageDiscovery(); var api = new ModernRemovalApi(); int factories = 0;
                var review = ModernReview(discovery, api, factory: () => factories++);
                Throws<SafetyException>(() => review.Execute(CancellationToken.None, message => {}));
                review.Cancel(); Throws<SafetyException>(() => review.Confirm());
                Check(factories == 0 && api.Calls == 0 && discovery.Calls == 0, "Cancelled or unconfirmed review reached work");
            });
            Test("modern confirmed eject is exact one-use and releases its reservation", delegate {
                var discovery = new ModernStorageDiscovery(); var api = new ModernRemovalApi(); var operations = new Operations();
                var review = ModernReview(discovery, api, operations); review.Confirm();
                Check(review.Execute(CancellationToken.None, message => {}).Success && api.Calls == 1 && operations.Available(new[] { 7 }), "Confirmed request or release failed");
                Throws<SafetyException>(() => review.Confirm()); Throws<SafetyException>(() => review.Execute(CancellationToken.None, message => {}));
                Check(api.Calls == 1, "Review replayed");
            });
            Test("modern pre-cancelled cleanup does not scan, delete or construct removal API", delegate {
                string root = ModernDefaultsFixture(), metadata = Path.Combine(root, "._kept"); File.WriteAllText(metadata, "owned metadata");
                var discovery = new ModernStorageDiscovery { State = ModernStorageState(root) }; var api = new ModernRemovalApi(); int factories = 0;
                var review = ModernReview(discovery, api, clean: true, factory: () => factories++); review.Confirm();
                using (var cancel = new CancellationTokenSource()) {
                    cancel.Cancel(); Throws<OperationCanceledException>(() => review.Execute(cancel.Token, message => {}));
                    Check(factories == 0 && api.Calls == 0 && discovery.Calls == 0 && File.ReadAllText(metadata) == "owned metadata", "Pre-cancelled review reached work");
                }
            });
            Test("modern same-identity reconnect expires confirmation and worker admission", delegate {
                var discovery = new ModernStorageDiscovery(); var api = new ModernRemovalApi();
                var before = ModernReview(discovery, api); discovery.Version++; discovery.State.Epoch = discovery.Epoch;
                Check(!before.Current, "Reconnect retained current review"); Throws<SafetyException>(() => before.Confirm());
                var after = ModernReview(discovery, api); after.Confirm(); discovery.Version++; discovery.State.Epoch = discovery.Epoch;
                Throws<SafetyException>(() => after.Execute(CancellationToken.None, message => {})); Check(api.Calls == 0, "Reconnected device was removed");
            });
            Test("modern changed topology in API construction is refused with reservation released", delegate {
                var discovery = new ModernStorageDiscovery(); var api = new ModernRemovalApi(); var operations = new Operations();
                var review = ModernReview(discovery, api, operations, factory: () => discovery.State.Volumes[0].Serial = "CHANGED"); review.Confirm();
                Throws<SafetyException>(() => review.Execute(CancellationToken.None, message => {}));
                Check(api.Calls == 0 && operations.Available(new[] { 7 }), "Factory topology change reached removal or leaked reservation");
            });
            Test("modern final native validation refuses changed identity before fake removal", delegate {
                var discovery = new ModernStorageDiscovery(); var api = new ModernRemovalApi { BeforeValidate = () => discovery.State.Disks[0].Serial = "changed" };
                var review = ModernReview(discovery, api); review.Confirm(); Throws<SafetyException>(() => review.Execute(CancellationToken.None, message => {}));
                Check(api.Calls == 0, "Native validation allowed changed disk");
            });
            Test("modern protected sibling and busy physical disk refuse removal", delegate {
                var discovery = new ModernStorageDiscovery(); var api = new ModernRemovalApi(); var operations = new Operations();
                var review = ModernReview(discovery, api, operations); review.Confirm();
                using (operations.Reserve(new[] { 7 })) Throws<SafetyException>(() => review.Execute(CancellationToken.None, message => {}));
                discovery.State.Volumes.Add(new Volume { Id = "protected-sibling", Serial = "DEF", System = true, Disks = new List<int> { 7 } });
                Throws<SafetyException>(() => ModernReview(discovery, api)); Check(api.Calls == 0 && operations.Available(new[] { 7 }), "Protected or reserved storage was removed");
            });
            Test("modern Windows veto is retained as refusal rather than safe unplugging", delegate {
                var discovery = new ModernStorageDiscovery(); var expected = new EjectResult { Success = false, Code = 23, Veto = (int)VetoReason.WindowsApp, Name = "Fixture editor" };
                var api = new ModernRemovalApi { Result = expected }; var review = ModernReview(discovery, api); review.Confirm();
                var result = review.Execute(CancellationToken.None, message => {});
                Check(result == expected && !result.Success && result.Message.Contains("WindowsApp") && result.Message.Contains("Fixture editor"), "Veto result was changed or hidden");
            });
            Test("modern explicit cleanup retains media and Trash and freezes card cleanup choice", delegate {
                string root = ModernDefaultsFixture(); File.WriteAllText(Path.Combine(root, "._clip"), "metadata"); File.WriteAllText(Path.Combine(root, "clip.mp4"), "original");
                string trash = Path.Combine(root, ".Trashes"); Directory.CreateDirectory(trash); File.WriteAllText(Path.Combine(trash, "._kept"), "Trash metadata");
                var discovery = new ModernStorageDiscovery { State = ModernStorageState(root) }; var api = new ModernRemovalApi(); var settings = new AppSettings { CleanCardsOnEject = true };
                var review = ModernReview(discovery, api, settings: settings); settings.CleanCardsOnEject = false;
                Check(review.HasCleanup && review.Description.Contains("Permanently remove macOS metadata") && review.Description.Contains(root), "Frozen cleanup scope was lost");
                review.Confirm(); var result = review.Execute(CancellationToken.None, message => {});
                Check(result.Success && result.Message.Contains("Removed 1") && !File.Exists(Path.Combine(root, "._clip")) && File.ReadAllText(Path.Combine(root, "clip.mp4")) == "original" && File.Exists(Path.Combine(trash, "._kept")) && api.Calls == 1, "Cleanup touched media/Trash or skipped fake request");
            });
            Test("modern cleanup refusal preserves protected metadata and never requests removal", delegate {
                string root = ModernDefaultsFixture(), metadata = Path.Combine(root, "._protected"); File.WriteAllText(metadata, "owned metadata"); File.WriteAllText(Path.Combine(root, "clip.mp4"), "original"); File.SetAttributes(metadata, FileAttributes.ReadOnly);
                var discovery = new ModernStorageDiscovery { State = ModernStorageState(root) }; var api = new ModernRemovalApi(); var operations = new Operations();
                try {
                    var review = ModernReview(discovery, api, operations, clean: true); review.Confirm();
                    Throws<SafetyException>(() => review.Execute(CancellationToken.None, message => {}));
                    Check(api.Calls == 0 && File.ReadAllText(metadata) == "owned metadata" && File.ReadAllText(Path.Combine(root, "clip.mp4")) == "original" && operations.Available(new[] { 7 }), "Refused cleanup altered data or requested removal");
                } finally { File.SetAttributes(metadata, FileAttributes.Normal); }
            });
            Test("modern mid-cleanup cancellation stops removal and reports partial metadata work", delegate {
                string root = ModernDefaultsFixture(); File.WriteAllText(Path.Combine(root, "._a"), "metadata A"); File.WriteAllText(Path.Combine(root, "._z"), "metadata Z"); File.WriteAllText(Path.Combine(root, "clip.mp4"), "original");
                var discovery = new ModernStorageDiscovery { State = ModernStorageState(root) }; var api = new ModernRemovalApi(); var review = ModernReview(discovery, api, clean: true); review.Confirm();
                using (var cancellation = new CancellationTokenSource()) {
                    var stopped = Throws<OperationCanceledException>(() => review.Execute(cancellation.Token, message => cancellation.Cancel()));
                    Check(stopped.Message.Contains("Removed 1") && api.Calls == 0 && Directory.GetFiles(root, "._*").Length == 1 && File.ReadAllText(Path.Combine(root, "clip.mp4")) == "original", "Cleanup cancellation removed media or requested removal");
                }
            });
        }
        static bool StorageResult(Task<bool> task) {
            Check(task.Wait(5000), "Owned inventory worker did not finish independently of its caller's UI context.");
            return task.GetAwaiter().GetResult();
        }
        static int NativeStorageMonitorSmoke() {
            var discovery = new WindowsDiscovery(); long start = discovery.Epoch;
            using (var notifications = new StorageNotificationWindow(discovery, () => false)) {
                notifications.Start(); var state = discovery.Scan();
                Console.WriteLine("Read-only monitor: window/scoped notifications=" + notifications.Available + "; complete=" + state.Complete + "; disks=" + state.Disks.Count + "; volumes=" + state.Volumes.Count);
                Check(notifications.Available && state.Complete && discovery.Epoch > start, "Native monitoring unavailable");
                foreach (var volume in state.Volumes) if (volume.System) Check(!new TrayStorageChoice(state, volume, discovery.Epoch).CanEject, "Protected Windows volume admitted");
            }
            Check(!discovery.NotificationsAvailable && !discovery.Instances.Versions.Available && !discovery.Scan().Complete, "Disposed monitor retained readiness");
            Console.WriteLine("Disposed monitoring refuses operations. No device request or media access."); return 0;
        }
    }
}
