using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static RemovalIntent InterruptedCompletionFixture(Fixture f, bool pending) {
            f.Put(@"DCIM\first.mp4", "first generated verified original");
            if (pending) f.Put(@"DCIM\second.mp4", "second generated verified original");
            string first = Path.Combine(f.Source, @"DCIM\first.mp4");
            using (var stop = new CancellationTokenSource()) {
                Throws<OriginalRemovalFailure>(() => new VerifiedImporter(new Operations(), Native.FreeSpace, line => { if (line.StartsWith("Removed verified original") && line.EndsWith(first)) stop.Cancel(); })
                    .Run(f.Source, f.Source, f.Destination, "fixture-volume", new EvidenceGuard(), stop.Token, _ => {}, RemoveOptions()));
            }
            Check(!File.Exists(first), "Setup did not reach interrupted removal"); NoRemovalCompletion(f); return ReadRemovalIntent(IntentPath(f));
        }
        static void CheckResumedCompletion(Fixture f, RemovalIntent intent, ImportResult result, bool pending, Operations operations) {
            Check(result.RemovedOriginals == (pending ? 1 : 0) && result.PriorAbsentOriginals == 1 && result.ClearedTrashFiles == 0 && result.ClearedTrashRecords == 0 && result.Files == (pending ? 2 : 1), "Completion lost current/prior removal counts");
            Check(File.Exists(result.ReceiptPath) && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 1 && Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0 && result.Receipt.Files.All(x => x.RemovedSource), "Completion lost its exclusive receipt or retained a private partial");
            string receiptHash; var published = ImportHistory.ReadReceipt(result.ReceiptPath, out receiptHash);
            Check(published.Id == intent.ImportId && published.RemovalIntentSha256 == result.Receipt.RemovalIntentSha256 && published.Files.Count(x => x.SourceAbsentAtResume) == 1 && published.Files.All(x => x.RemovedSource) && receiptHash.Length == 64, "Durable receipt lost its exact intent/prior absence evidence");
            Check(!File.ReadAllText(Path.Combine(intent.BatchFolder, result.Receipt.ResumeAuditPath)).Contains("Resume stopped"), "Completed batch was audited as stopped after publication");
            CheckOwnershipSaved(intent); using (operations.Reserve(new[] { 700, 701 })) {}
            Check(Throws<SafetyException>(() => ReviewInterrupted(f)).Message.Contains("completion receipt"), "Completed receipt remained available for removal retry");
            string history = Path.Combine(f.Root, "resume-completion-history"); Directory.CreateDirectory(history); ImportHistory.Add(Batch(f, result), history);
            var restored = ImportHistory.Load(history).Single(); ImportHistory.Verify(restored, HistoryIdentity(), new FakeGuard(), new Operations(), CancellationToken.None, _ => {});
            Check(restored.Id == result.Receipt.Id && restored.Originals.Count == result.Files, "Completion did not retain the exact batch for manual history after source removal");
        }
        static void ResumeCompletionTests() {
            foreach (bool pending in new[] { false, true }) {
                bool hasPending = pending;
                foreach (bool cancel in new[] { false, true }) {
                    bool cancelAfter = cancel;
                    Test("resume " + (hasPending ? "new removal" : "receipt only") + " durable publication survives later " + (cancelAfter ? "cancellation" : "source parent replacement"), () => { using (var f = new Fixture()) using (var stop = new CancellationTokenSource()) {
                        var intent = InterruptedCompletionFixture(f, hasPending); var reviewed = ReviewInterrupted(f); var operations = new Operations(); bool reached = false; string parent = Path.Combine(f.Source, "DCIM");
                        var result = ResumeFixture(f, reviewed, operations: operations, token: stop.Token, report: line => { if (line.StartsWith("Resume complete.")) {
                            Check(Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 1, "Terminal display preceded the receipt"); reached = true;
                            if (cancelAfter) stop.Cancel(); else ReplaceFixtureDirectory(f, parent);
                        } });
                        Check(reached && (cancelAfter ? stop.IsCancellationRequested : File.ReadAllText(Path.Combine(parent, "foreign.keep")) == "retain replacement"), "Post-publication event did not run or foreign data changed");
                        CheckResumedCompletion(f, intent, result, hasPending, operations);
                    } });
                }
                Test("resume " + (hasPending ? "new removal" : "receipt only") + " terminal display exception cannot discard durable completion", () => { using (var f = new Fixture()) {
                    var intent = InterruptedCompletionFixture(f, hasPending); var operations = new Operations(); bool reached = false;
                    var result = ResumeFixture(f, operations: operations, report: line => { if (line.StartsWith("Resume complete.")) { reached = true; throw new OperationCanceledException("Generated late display cancellation"); } });
                    Check(reached, "Terminal display boundary did not run"); CheckResumedCompletion(f, intent, result, hasPending, operations);
                } });
                Test("resume " + (hasPending ? "new removal" : "receipt only") + " cancellation before receipt still refuses completion and permits fresh explicit retry", () => { using (var f = new Fixture()) using (var stop = new CancellationTokenSource()) {
                    var intent = InterruptedCompletionFixture(f, hasPending); var reviewed = ReviewInterrupted(f); var operations = new Operations(); bool reached = false;
                    Action execute = () => ResumeFixture(f, reviewed, operations: operations, token: stop.Token, report: line => { if (line.StartsWith("Publishing verified resumed import receipt")) { reached = true; stop.Cancel(); } });
                    if (hasPending) { var failure = Throws<RemovalResumeFailure>(execute); Check(failure.RemovedOriginals == 1 && File.Exists(failure.AuditPath), "Pre-publication failure lost the new accepted removal"); }
                    else Throws<OperationCanceledException>(execute);
                    Check(reached, "Pre-publication boundary did not run"); NoRemovalCompletion(f); CheckOwnershipSaved(intent); using (operations.Reserve(new[] { 700, 701 })) {}
                    var retry = ResumeFixture(f); Check(retry.RemovedOriginals == 0 && retry.PriorAbsentOriginals == (hasPending ? 2 : 1) && File.Exists(retry.ReceiptPath), "Fresh retry recounted prior removals or failed to publish");
                } });
            }
        }
    }
}
