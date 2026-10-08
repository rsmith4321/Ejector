using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static CompletedBatch PowerShareBatch(Fixture f, ScopedDiscovery discovery, bool two = false) {
            f.Put("VID_first.mp4", "generated first original with injected Insta360 Luna Ultra identity");
            if (two) f.Put("VID_second.mp4", "generated second original with injected Insta360 Luna Ultra identity");
            var guard = ScopedImportGuard(f, discovery);
            var result = new VerifiedImporter(new Operations(), _ => UInt64.MaxValue, _ => {}).Run(f.Source, f.Source, f.Destination, "source-volume", guard, CancellationToken.None, _ => {});
            var batch = ImportHistory.Create(result, new ImportProfile { VolumeId = "source-volume", Destination = f.Destination }, "Generated Luna camera", guard.DestinationIdentity);
            Check(batch.Originals.Count == (two ? 2 : 1), "Generated sharing batch lost an exact original");
            foreach (var original in batch.Originals) using (var stream = SafePaths.ReadFile(original.Path)) Check(QuickShareMedia.IsLunaMaster(original.Path, stream), "Generated sharing original lacks injected Luna eligibility");
            return batch;
        }
        static void ResumeFakePower(ScopedDiscovery discovery, int boundary) { if (boundary == 4) FakePower(discovery, 18); }
        static void CheckPowerShareKept(Fixture f, CompletedBatch batch, string config, byte[] history, byte[] receipt) {
            VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
            Check(File.ReadAllBytes(Path.Combine(config, "completed-imports.json")).SequenceEqual(history) && File.ReadAllBytes(batch.ReceiptPath).SequenceEqual(receipt), "Power interruption changed saved import history or receipt");
            foreach (var original in batch.Originals) Check(File.ReadAllText(Path.Combine(f.Source, Path.GetFileName(original.Path))) == File.ReadAllText(original.Path), "Power interruption changed a source original");
        }
        static RemovalIntent PowerInterruptedRemoval(Fixture f, ScopedDiscovery discovery, Operations operations) {
            foreach (string name in new[] { "first", "second", "third" }) f.Put(name + ".mp4", "generated retained " + name + " original");
            f.Put("unknown.keep", "retain unrelated source bytes");
            using (var stop = new CancellationTokenSource()) {
                var failure = Throws<OriginalRemovalFailure>(() => new VerifiedImporter(operations, _ => UInt64.MaxValue,
                    line => { if (line.StartsWith("Removed verified original", StringComparison.Ordinal) && line.EndsWith(Path.Combine(f.Source, "first.mp4"), StringComparison.Ordinal)) stop.Cancel(); })
                    .Run(f.Source, f.Source, f.Destination, "source-volume", ScopedImportGuard(f, discovery), stop.Token, _ => {}, RemoveOptions()));
                Check(failure.InnerException is OperationCanceledException && !File.Exists(Path.Combine(f.Source, "first.mp4")), "Generated setup did not stop after the first verified removal");
            }
            NoRemovalCompletion(f); return ReadRemovalIntent(IntentPath(f));
        }
        static ResumeOperationReview PowerResumeReview(Fixture f, ScopedDiscovery discovery, Operations operations) {
            string config = Path.Combine(f.Root, "resume-profile"); Directory.CreateDirectory(config);
            var state = discovery.Scan(); var volume = state.Volumes.Single(v => v.Id == "source-volume"); var disk = state.Disks.Single(d => d.Number == 700);
            if (!File.Exists(Path.Combine(config, Profiles.FileName(volume.Id)))) Profiles.Save(new ImportProfile {
                Name = "Generated recovery camera", VolumeId = volume.Id, VolumeSerial = volume.Serial, DiskInstance = disk.InstanceId, RelativeSource = "", Destination = f.Destination,
                SavedDestination = DestinationIdentity.Capture(state, "destination-volume"), VideosOnly = true, IncludePreviews = true, CleanLayout = true, DeleteOriginals = true
            }, config);
            return ResumeOperationReview.Create(AutomaticConnection.Capture(state, volume), Profiles.ReviewConnected(volume, disk, config), new ImportDefaults(), IntentPath(f), FixtureTrashSid, discovery, operations, CancellationToken.None,
                path => path == f.Source ? "source-volume" : "destination-volume");
        }
        static void CheckPowerResumeSaved(Fixture f, RemovalIntent intent, byte[] journal) {
            Check(File.ReadAllBytes(IntentPath(f)).SequenceEqual(journal) && File.ReadAllText(Path.Combine(f.Source, "unknown.keep")) == "retain unrelated source bytes", "Power refusal changed journal or unrelated bytes");
            CheckOwnershipSaved(intent);
        }
        static void PowerOperationTests() {
            foreach (int powerEvent in new[] { 4, 18, 7 }) { int boundary = powerEvent;
                foreach (string injectedStage in new[] { "Before encode", "After encode", "Decode" }) { string stage = injectedStage;
                    Test("power interrupts manual sharing at " + stage + " " + boundary, () => { using (var f = new Fixture(true)) {
                        var discovery = new ScopedDiscovery(f); var batch = PowerShareBatch(f, discovery); string config = ManualShareSetup(f, batch);
                        byte[] history = File.ReadAllBytes(Path.Combine(config, "completed-imports.json")), receipt = File.ReadAllBytes(batch.ReceiptPath);
                        var operations = new Operations(); var process = new FakeShareProcess(); bool signaled = false;
                        process.OnStage = (current, path) => { if (current == stage && !signaled) { signaled = true; FakePower(discovery, boundary); } };
                        var service = ManualShares(config, process, operations, _ => ScopedDestinationGuard(f, discovery));
                        var failure = Throws<QuickShareFailure>(() => service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {}));
                        Check(signaled && failure.InnerException is SafetyException && !failure.Cancelled && failure.Partial.Created == 0 && failure.Partial.PublishedCopies == 0 && failure.Partial.PublishedReceipts == 0 && failure.Partial.Outputs.Count == 0, "Interrupted sharing published output or lost its refusal reason");
                        Check(!File.Exists(ShareFile(batch, "VID_first")) && !File.Exists(QuickShareEngine.ReceiptPath(ShareFile(batch, "VID_first"))), "Interrupted sharing published a copy or receipt");
                        CheckPowerShareKept(f, batch, config, history, receipt); using (operations.Reserve(new[] { 701 })) {}
                        int encodes = process.Encodes; ResumeFakePower(discovery, boundary); Check(service.CanOffer(batch) && process.Encodes == encodes, "Resume implicitly restarted manual sharing");
                        var retry = service.Run(service.Prepare(batch).Choose(QuickSharePreset.Uhd, QuickShareColor.Standard), CancellationToken.None, _ => {});
                        Check(retry.Created == 1 && retry.Reused == 0 && retry.Outputs.Single() == ShareFile(batch, "VID_first", "4K"), "Fresh manual size choice/retry failed");
                        CheckPowerShareKept(f, batch, config, history, receipt); using (operations.Reserve(new[] { 701 })) {}
                    } });
                }
                Test("power preserves an earlier published sharing copy and exact receipt " + boundary, () => { using (var f = new Fixture(true)) {
                    var discovery = new ScopedDiscovery(f); var batch = PowerShareBatch(f, discovery, true); string config = ManualShareSetup(f, batch);
                    byte[] history = File.ReadAllBytes(Path.Combine(config, "completed-imports.json")), receipt = File.ReadAllBytes(batch.ReceiptPath);
                    var operations = new Operations(); var process = new FakeShareProcess(); var service = ManualShares(config, process, operations, _ => ScopedDestinationGuard(f, discovery)); bool signaled = false;
                    var failure = Throws<QuickShareFailure>(() => service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, line => {
                        if (!signaled && line == "Reading Luna video VID_second.mp4") { signaled = true; FakePower(discovery, boundary); }
                    }));
                    string output = ShareFile(batch, "VID_first"), record = QuickShareEngine.ReceiptPath(output); byte[] outputBytes = File.ReadAllBytes(output), recordBytes = File.ReadAllBytes(record);
                    Check(signaled && failure.InnerException is SafetyException && failure.Partial.Created == 1 && failure.Partial.PublishedCopies == 1 && failure.Partial.PublishedReceipts == 1 && failure.Partial.Outputs.Single() == output && process.Encodes == 1, "Earlier successful publication/counts were lost");
                    CheckPowerShareKept(f, batch, config, history, receipt); using (operations.Reserve(new[] { 701 })) {} ResumeFakePower(discovery, boundary);
                    var retry = service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {});
                    Check(retry.Created == 1 && retry.Reused == 1 && process.Encodes == 2 && File.ReadAllBytes(output).SequenceEqual(outputBytes) && File.ReadAllBytes(record).SequenceEqual(recordBytes), "Fresh manual retry replaced verified publication instead of reusing it");
                    CheckPowerShareKept(f, batch, config, history, receipt);
                } });
                Test("power between sharing output and receipt keeps the unreceipted copy for manual retry " + boundary, () => { using (var f = new Fixture(true)) {
                    var discovery = new ScopedDiscovery(f); var batch = PowerShareBatch(f, discovery); var operations = new Operations(); var process = new FakeShareProcess(); bool signaled = false;
                    var failure = Throws<QuickShareFailure>(() => new QuickShareEngine(process, operations, freeSpace: _ => UInt64.MaxValue, checkpoint: (stage, path) => {
                        if (stage == "Before receipt publication" && !signaled) { signaled = true; FakePower(discovery, boundary); }
                    }).Run(batch, batch.Destination, ScopedDestinationGuard(f, discovery), FixtureShareTools(f), QuickSharePreset.Hd, QuickShareColor.Standard, CancellationToken.None, _ => {}));
                    string output = ShareFile(batch, "VID_first"); byte[] retained = File.ReadAllBytes(output);
                    Check(signaled && failure.InnerException is SafetyException && failure.Partial.Created == 0 && failure.Partial.PublishedCopies == 1 && failure.Partial.PublishedReceipts == 0 && failure.Partial.Outputs.Count == 0 && QuickSharePresentation.CancelNeedsAttention(failure), "Unreceipted publication was reported as complete");
                    Check(!File.Exists(QuickShareEngine.ReceiptPath(output)), "Invalid receipt published after power retirement"); CheckSharePrivateGone(batch); VerifyShareOriginals(batch); using (operations.Reserve(new[] { 701 })) {}
                    ResumeFakePower(discovery, boundary);
                    var retry = new QuickShareEngine(process, operations, freeSpace: _ => UInt64.MaxValue).Run(batch, batch.Destination, ScopedDestinationGuard(f, discovery), FixtureShareTools(f), QuickSharePreset.Hd, QuickShareColor.Standard, CancellationToken.None, _ => {});
                    Check(retry.Created == 1 && retry.Reused == 0 && retry.Outputs.Single() != output && File.ReadAllBytes(output).SequenceEqual(retained) && !File.Exists(QuickShareEngine.ReceiptPath(output)), "Manual retry overwrote or trusted the unreceipted copy");
                    VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
                } });
                Test("power during metadata cleanup stops fake eject with precise partial counts " + boundary, () => { using (var f = new Fixture(true)) {
                    foreach (string name in new[] { ".DS_Store", ".apdisk", "._clip.mp4", "clip.mp4", @".Trashes\501\retained.mp4" }) f.Put(name, "retained generated " + name);
                    var discovery = new ScopedDiscovery(f); var state = discovery.Scan(); var plan = EjectService.Plan(state, "source-volume"); var clean = MetadataCleanupPlan.Single(state, plan, "source-volume");
                    Func<string, string> resolve = path => path == f.Source ? "source-volume" : "destination-volume";
                    var operations = new Operations(); var api = new FakeEject(); var eject = new EjectService(discovery, api, operations); var counts = new CleanupCounts(); bool signaled = false;
                    Throws<SafetyException>(() => eject.Execute(plan, initial => {
                        clean.Preflight(initial, resolve); clean.Clean(() => { var fresh = discovery.Scan(); EjectService.Validate(discovery, fresh, plan); return fresh; }, resolve, CancellationToken.None, counts, line => { if (!signaled) { signaled = true; FakePower(discovery, boundary); } });
                    }));
                    Check(signaled && counts.Files == 1 && counts.Folders == 0 && api.Calls == 0 && new[] { ".DS_Store", ".apdisk", "._clip.mp4" }.Count(name => File.Exists(Path.Combine(f.Source, name))) == 2, "Cleanup continued or reached eject after power retirement");
                    Check(File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == "retained generated clip.mp4" && File.ReadAllText(Path.Combine(f.Source, @".Trashes\501\retained.mp4")) == @"retained generated .Trashes\501\retained.mp4", "Cleanup changed recording/Trash");
                    using (operations.Reserve(new[] { 700 })) {} ResumeFakePower(discovery, boundary); Throws<SafetyException>(() => eject.Execute(plan)); Check(api.Calls == 0, "Old confirmation survived resume");
                    state = discovery.Scan(); var freshPlan = EjectService.Plan(state, "source-volume"); var freshClean = MetadataCleanupPlan.Single(state, freshPlan, "source-volume"); var retried = new CleanupCounts();
                    Check(eject.Execute(freshPlan, initial => { freshClean.Preflight(initial, resolve); freshClean.Clean(() => { var current = discovery.Scan(); EjectService.Validate(discovery, current, freshPlan); return current; }, resolve, CancellationToken.None, retried, _ => {}); }).Success && retried.Files == 2 && api.Calls == 1, "Fresh reviewed retry recounted removals or failed fake eject");
                } });
                foreach (bool confirmedFirst in new[] { false, true }) { bool confirmed = confirmedFirst;
                    Test("power expires " + (confirmed ? "confirmed" : "open") + " interrupted-removal review " + boundary, () => { using (var f = new Fixture(true)) {
                        var discovery = new ScopedDiscovery(f); var operations = new Operations(); var intent = PowerInterruptedRemoval(f, discovery, operations); byte[] journal = File.ReadAllBytes(IntentPath(f));
                        using (var review = PowerResumeReview(f, discovery, operations)) {
                            Check(review.Current, "Initial review unavailable"); if (confirmed) review.Confirm(); FakePower(discovery, boundary);
                            Check(!review.Current, "Power retirement left review current"); if (confirmed) Throws<SafetyException>(() => review.Execute(operations, CancellationToken.None, _ => {})); else Throws<SafetyException>(() => review.Confirm());
                        }
                        Check(File.Exists(Path.Combine(f.Source, "second.mp4")) && File.Exists(Path.Combine(f.Source, "third.mp4")), "Expired review removed remaining originals"); NoRemovalCompletion(f); CheckPowerResumeSaved(f, intent, journal); using (operations.Reserve(new[] { 700, 701 })) {}
                        ResumeFakePower(discovery, boundary); using (var retry = PowerResumeReview(f, discovery, operations)) { retry.Confirm(); var result = retry.Execute(operations, CancellationToken.None, _ => {});
                            Check(result.RemovedOriginals == 2 && result.PriorAbsentOriginals == 1 && result.Reused == 3 && File.Exists(result.ReceiptPath), "Fresh explicit recovery lost prior/current counts or saved receipt"); }
                        CheckPowerResumeSaved(f, intent, journal); using (operations.Reserve(new[] { 700, 701 })) {}
                    } });
                }
                Test("power stops resumed removal after one generated source and preserves exact retry evidence " + boundary, () => { using (var f = new Fixture(true)) {
                    var discovery = new ScopedDiscovery(f); var operations = new Operations(); var intent = PowerInterruptedRemoval(f, discovery, operations); byte[] journal = File.ReadAllBytes(IntentPath(f)); bool signaled = false;
                    var guard = ScopedImportGuard(f, discovery); var evidence = ReviewInterrupted(f, guard, operations: operations);
                    var failure = Throws<RemovalResumeFailure>(() => RemovalResumeEvidence.Resume(evidence, IntentPath(f), f.Source, f.Source, f.Destination, FixtureTrashSid, guard, operations, CancellationToken.None, _ => {}, line => {
                        if (line.StartsWith("Removed resumed verified source", StringComparison.Ordinal) && !signaled) { signaled = true; FakePower(discovery, boundary); }
                    }));
                    Check(signaled && failure.InnerException is SafetyException && failure.RemovedOriginals == 1 && failure.ClearedTrashFiles == 0 && failure.ClearedTrashRecords == 0 && File.Exists(failure.AuditPath) && !File.Exists(Path.Combine(f.Source, "second.mp4")) && File.ReadAllText(Path.Combine(f.Source, "third.mp4")) == "generated retained third original", "Power interruption lost precise removal/retention counts");
                    NoRemovalCompletion(f); CheckPowerResumeSaved(f, intent, journal); using (operations.Reserve(new[] { 700, 701 })) {} ResumeFakePower(discovery, boundary);
                    using (var retry = PowerResumeReview(f, discovery, operations)) { retry.Confirm(); var result = retry.Execute(operations, CancellationToken.None, _ => {});
                        Check(result.RemovedOriginals == 1 && result.PriorAbsentOriginals == 2 && result.Reused == 3 && result.Receipt.Files.Count(x => x.SourceAbsentAtResume) == 2 && File.Exists(result.ReceiptPath), "Fresh review lost prior removal evidence or replayed an old removal"); }
                    CheckPowerResumeSaved(f, intent, journal); using (operations.Reserve(new[] { 700, 701 })) {}
                } });
            }
        }
    }
}
