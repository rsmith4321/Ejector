using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEject {
    public static partial class Tests {
        sealed class ModernPrimaryStorageGuard : IConcurrentStorageGuard {
            internal bool Valid = true, ThrowAvailability; internal int Checks;
            public int[] ProtectedDisks { get { return new[] { 7, 8 }; } }
            public bool AllowsUnrelatedOperations { get { if (ThrowAvailability) throw new SafetyException("Fixture proof unavailable"); return Valid; } }
            public void Validate() { Checks++; if (!Valid) throw new SafetyException("Fixture primary connection changed"); }
        }
        sealed class ModernSecondaryStorageApi : IEjectApi {
            internal Action Validate, Before; internal int Requests; internal bool Success = true;
            public EjectResult Request(uint node, string instance) {
                Check(node == 30 && instance == "modern-other-usb", "Secondary requested a protected or wrong device");
                if (Before != null) Before(); Validate(); Requests++;
                return new EjectResult { Success = Success, Code = Success ? 0U : 23U, Veto = (int)VetoReason.WindowsApp, Name = "Fixture veto" };
            }
        }
        sealed class ModernConcurrentFixture {
            internal readonly ModernImportFixture Import = new ModernImportFixture();
            internal readonly ModernPrimaryStorageGuard Guard = new ModernPrimaryStorageGuard();
            internal readonly ConcurrentStorageWork Work;
            internal readonly TaskCompletionSource<int> Primary = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TrayOperation Token = new TrayOperation(TrayOperationKind.Import, "Owned primary fixture");
            internal readonly ModernSecondaryStorageApi Api = new ModernSecondaryStorageApi();
            internal readonly string Other; internal int Factories;
            internal ModernConcurrentFixture() {
                Other = Path.Combine(Import.Root, "other-device"); Directory.CreateDirectory(Other);
                var state = Import.Discovery.State;
                state.Disks.Add(new Disk { Number = 9, DevInst = 12, InstanceId = "modern-other", TargetId = "modern-other-usb", TargetDevInst = 30, Ancestors = new List<string> { "modern-other", "modern-other-usb" } });
                state.Volumes.Add(new Volume { Id = "modern-other-volume", Serial = "GHI", Extents = "9:0:1000", Disks = new List<int> { 9 }, Mounts = new List<string> { Other }, Group = StorageGroup.External });
                state.GroupConnections["modern-other-usb"] = "other-fixture-connection-1";
                Work = new ConcurrentStorageWork(Import.Operations); Work.Start(Token, () => Primary.Task, Guard);
            }
            internal ConcurrentStorageReview Review(bool clean = false) {
                var state = Import.Discovery.State;
                return new ConcurrentStorageReview(Work.State, state, new TrayStorageChoice(state, state.Volumes[2], Import.Discovery.Epoch), new AppSettings(), clean,
                    Import.Discovery, Import.Operations, validate => { Factories++; Api.Validate = validate; return Api; }, path => "modern-other-volume");
            }
            internal void Finish() { Primary.TrySetResult(1); Check(Work.Drain(Token).IsCompleted, "Fixture secondary review still owns terminal lifecycle"); }
        }
        static void ConcurrentOperationTests() {
            Test("modern primary worker supports ready scopes for import resume verify and share only", delegate {
                foreach (var kind in new[] { TrayOperationKind.Import, TrayOperationKind.Resume, TrayOperationKind.Verify, TrayOperationKind.Share, TrayOperationKind.Eject }) {
                    var work = new ConcurrentStorageWork(new Operations()); var token = new TrayOperation(kind, "Owned scope"); var pending = new TaskCompletionSource<int>();
                    var actual = work.Start(token, () => pending.Task, new ModernPrimaryStorageGuard());
                    Check(Object.ReferenceEquals(actual, pending.Task) && work.Live && work.Available == (kind != TrayOperationKind.Eject), "Worker kind/handle authority changed: " + kind);
                    Throws<InvalidOperationException>(() => work.Drain(token)); pending.SetResult(1); Check(work.Drain(token).IsCompleted && !work.Available, "Terminal scope not released");
                }
            });
            Test("modern stale ready and drain callbacks cannot release a newer worker", delegate {
                var work = new ConcurrentStorageWork(new Operations()); TrayOperation first = new TrayOperation(TrayOperationKind.Import, "First"), next = new TrayOperation(TrayOperationKind.Share, "Next");
                work.Start(first, () => Task.FromResult(1)); Check(!work.Attach(first, new ModernPrimaryStorageGuard()) && work.Drain(first).IsCompleted, "Finished worker granted authority");
                var pending = new TaskCompletionSource<int>(); work.Start(next, () => pending.Task, new ModernPrimaryStorageGuard());
                Check(!work.Attach(first, new ModernPrimaryStorageGuard()) && work.Drain(first).IsCompleted && work.Available, "Old callback affected newer work"); pending.SetResult(1); work.Drain(next);
            });
            Test("modern worker factory failure and unavailable optional proof retain honest ownership", delegate {
                var work = new ConcurrentStorageWork(new Operations()); var token = new TrayOperation(TrayOperationKind.Import, "Fixture");
                Throws<SafetyException>(() => work.Start<int>(token, () => { throw new SafetyException("Fixture worker creation refused"); })); Check(!work.Live, "Failed factory fabricated a worker");
                var pending = new TaskCompletionSource<int>(); var guard = new ModernPrimaryStorageGuard { ThrowAvailability = true };
                var actual = work.Start(token, () => pending.Task, guard);
                Check(Object.ReferenceEquals(actual, pending.Task) && work.Live && !work.Available, "Optional proof failure abandoned live worker or admitted other storage");
                Throws<InvalidOperationException>(() => work.Drain(token)); guard.ThrowAvailability = false; Check(work.Attach(token, guard) && work.Available, "Fresh proof could not attach to same live work"); pending.SetResult(1); work.Drain(token);
            });
            Test("modern other-device admission blocks primary source destination siblings and reserved targets", delegate {
                var f = new ModernConcurrentFixture(); var state = f.Import.Discovery.State;
                Check(f.Work.State.Unavailable(state, 1, "modern-volume") != null && f.Work.State.Unavailable(state, 1, "modern-target-volume") != null && f.Work.State.Unavailable(state, 1, "modern-other-volume") == null, "Protected/other admission incorrect");
                using (f.Import.Operations.Reserve(new[] { 9 })) Check(f.Work.State.Unavailable(state, 1, "modern-other-volume") != null, "Reserved target admitted");
                state.Disks[1].TargetId = "modern-other-usb"; state.Disks[1].Ancestors.Add("modern-other-usb");
                Check(f.Work.State.Unavailable(state, 1, "modern-other-volume") != null, "Related protected destination admitted"); f.Finish();
            });
            Test("modern other-device review defaults to no action and closes without helper or media change", delegate {
                var f = new ModernConcurrentFixture(); File.WriteAllText(Path.Combine(f.Other, "ordinary.mp4"), "owned fixture");
                using (var review = f.Review()) Check(review.Current && review.Description.Contains("own Stop control") && f.Work.State.Busy, "Review omitted independent lifetime/confirmation");
                Check(f.Factories == 0 && f.Api.Requests == 0 && File.ReadAllText(Path.Combine(f.Other, "ordinary.mp4")) == "owned fixture", "Unconfirmed review touched device data"); f.Finish();
            });
            Test("modern other-device changed review refuses before worker construction", delegate {
                var f = new ModernConcurrentFixture(); using (var review = f.Review()) {
                    f.Import.Discovery.Version = f.Import.Discovery.State.Epoch = 2;
                    Throws<SafetyException>(() => review.Execute(message => {})); Check(f.Factories == 0 && !review.Busy && review.TryClose(), "Expired review started worker");
                } f.Finish();
            });
            Test("modern secondary success is retained once and leaves primary reservations untouched", delegate {
                var f = new ModernConcurrentFixture(); using (f.Import.Operations.Reserve(new[] { 7, 8 })) using (var review = f.Review()) {
                    var result = review.Execute(message => {}).GetAwaiter().GetResult();
                    Check(result.Success && !result.Cancelled && Object.ReferenceEquals(review.Outcome, result) && f.Api.Requests == 1 && !f.Import.Operations.Available(new[] { 7, 8 }) && f.Import.Operations.Available(new[] { 9 }), "Secondary result or reservations lost");
                    Throws<SafetyException>(() => review.Execute(message => {})); review.RequestStop(); Check(review.Outcome.Success, "Late Stop erased accepted removal");
                } f.Finish();
            });
            Test("modern primary terminal waits for exact secondary review and rejects new primary admission", delegate {
                var f = new ModernConcurrentFixture(); var review = f.Review(); f.Primary.SetResult(1); var wait = f.Work.Drain(f.Token);
                Check(!wait.IsCompleted && !f.Work.Live && !f.Work.Available && Object.ReferenceEquals(wait, f.Work.Drain(f.Token)), "Primary did not retain exact secondary wait");
                Throws<InvalidOperationException>(() => f.Work.Start(new TrayOperation(TrayOperationKind.Verify, "New"), () => Task.FromResult(1)));
                review.Dispose(); Check(wait.IsCompleted, "Closing exact secondary failed to release completion"); f.Finish();
            });
            Test("modern active secondary refuses disposal and primary completion until worker terminal and view close", delegate {
                var f = new ModernConcurrentFixture(); using (var entered = new ManualResetEventSlim()) using (var finish = new ManualResetEventSlim()) using (var review = f.Review()) {
                    f.Api.Before = () => { entered.Set(); Check(finish.Wait(5000), "Owned secondary fixture wait expired"); };
                    var worker = review.Execute(message => {}); Check(entered.Wait(5000), "No confirmed secondary worker");
                    Check(review.Busy && !review.TryClose(), "Active view released worker"); Throws<InvalidOperationException>(() => review.Dispose());
                    f.Primary.SetResult(1); var wait = f.Work.Drain(f.Token); Check(!wait.IsCompleted, "Main completion raced secondary worker");
                    finish.Set(); Check(worker.GetAwaiter().GetResult().Success && !wait.IsCompleted, "Worker terminal incorrectly released still-open review");
                    Check(review.TryClose() && wait.IsCompleted, "Terminal view close did not release lifecycle");
                } f.Finish();
            });
            Test("modern secondary Stop is independent and cancels before native request", delegate {
                var f = new ModernConcurrentFixture(); using (var entered = new ManualResetEventSlim()) using (var finish = new ManualResetEventSlim()) using (var review = f.Review()) {
                    f.Api.Before = () => { entered.Set(); Check(finish.Wait(5000), "Owned cancel fixture wait expired"); };
                    var worker = review.Execute(message => {}); Check(entered.Wait(5000), "No secondary cancel worker"); review.RequestStop();
                    Check(!review.TryClose() && !f.Primary.Task.IsCompleted && f.Guard.Valid, "Secondary Stop ended main worker or released native work"); finish.Set();
                    var result = worker.GetAwaiter().GetResult(); Check(result.Cancelled && !result.Success && f.Api.Requests == 0 && f.Import.Operations.Available(new[] { 9 }), "Cancelled action requested removal or leaked target reservation");
                } f.Finish();
            });
            Test("modern lost primary proof refuses secondary request and preserves existing ordinary data", delegate {
                var f = new ModernConcurrentFixture(); File.WriteAllText(Path.Combine(f.Other, "ordinary.mp4"), "retained");
                using (var review = f.Review()) {
                    f.Api.Before = () => f.Guard.Valid = false;
                    var result = review.Execute(message => {}).GetAwaiter().GetResult();
                    Check(!result.Success && !result.Cancelled && result.Message.Contains("No automatic retry") && f.Api.Requests == 0 && File.ReadAllText(Path.Combine(f.Other, "ordinary.mp4")) == "retained", "Lost primary proof reached request or changed media");
                } f.Finish();
            });
            Test("modern secondary veto remains visible after worker and primary completion", delegate {
                var f = new ModernConcurrentFixture(); f.Api.Success = false;
                using (var review = f.Review()) { var result = review.Execute(message => {}).GetAwaiter().GetResult(); f.Primary.SetResult(1); Check(!result.Success && result.Message.Contains("veto") && review.Outcome == result && !f.Work.Drain(f.Token).IsCompleted, "Veto explanation or exact UI lifetime lost"); }
                Check(f.Work.Drain(f.Token).IsCompleted && f.Api.Requests == 1, "Veto reissued request or held view after close");
            });
            Test("modern explicit secondary cleanup changes only generated metadata on reviewed other device", delegate {
                var f = new ModernConcurrentFixture(); File.WriteAllText(Path.Combine(f.Other, ".DS_Store"), "owned metadata"); File.WriteAllText(Path.Combine(f.Other, "ordinary.mp4"), "ordinary retained");
                f.Import.Put("clip.mp4");
                using (var review = f.Review(true)) {
                    Check(review.Description.Contains("metadata"), "Explicit cleanup was hidden from review"); var result = review.Execute(message => {}).GetAwaiter().GetResult();
                    Check(result.Success && !File.Exists(Path.Combine(f.Other, ".DS_Store")) && File.Exists(Path.Combine(f.Other, "ordinary.mp4")) && File.Exists(Path.Combine(f.Import.Source, "clip.mp4")), "Cleanup exceeded generated selected metadata scope");
                } f.Finish();
            });
        }
    }
}
