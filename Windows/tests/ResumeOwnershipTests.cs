using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static RemovalIntent InterruptedOwnershipFixture(Fixture f, bool nested = false) {
            string relative = nested ? @"DCIM\clip.mp4" : "clip.mp4"; f.Put(relative, "verified generated original"); string source = Path.Combine(f.Source, relative);
            using (var stop = new CancellationTokenSource()) {
                Throws<OriginalRemovalFailure>(() => new VerifiedImporter(new Operations(), Native.FreeSpace, line => { if (line.StartsWith("Removed verified original") && line.EndsWith(source)) stop.Cancel(); })
                    .Run(f.Source, f.Source, f.Destination, "fixture-volume", new EvidenceGuard(), stop.Token, _ => {}, new MediaOptions(true, true, false, false, true)));
            }
            Check(!File.Exists(source), "Owned original did not reach the intended interruption"); NoRemovalCompletion(f); return ReadRemovalIntent(IntentPath(f));
        }
        static void CheckOwnershipSaved(RemovalIntent intent) {
            foreach (var entry in intent.Files) using (var file = SafePaths.ReadFile(Path.Combine(intent.BatchFolder, entry.SavedRelativePath))) {
                Check(entry.SavedIdentity.Matches(Native.Info(file.SafeFileHandle)) && file.Length == entry.Bytes && VerifiedImporter.Hash(file, CancellationToken.None) == entry.Sha256, "Owned saved dependency lost native identity or bytes");
            }
        }
        static RemovalIntent InterruptedOwnershipRecovery(Fixture f, out string payload) {
            // Sort this ordinary source before $RECYCLE.BIN so only it is removed at setup.
            f.Put(@"!DCIM\ordinary.mp4", "verified generated ordinary"); string ordinary = Path.Combine(f.Source, @"!DCIM\ordinary.mp4"); payload = PutRecycle(f, "BOUND", @"E:\clip.mp4", "verified generated Trash");
            using (var stop = new CancellationTokenSource()) {
                var failure = Throws<OriginalRemovalFailure>(() => new VerifiedImporter(new Operations(), Native.FreeSpace, line => { if (line.StartsWith("Removed verified original") && line.EndsWith(ordinary)) stop.Cancel(); },
                    (root, uid, token, validate) => DeviceTrash.Read(root, FixtureTrashSid, uid, token, validate))
                    .Run(f.Source, f.Source, f.Destination, "fixture-volume", new EvidenceGuard(), stop.Token, _ => {}, RecoverOptions(delete: true)));
                Check(failure.RemovedOriginals == 1 && !File.Exists(ordinary) && File.Exists(payload), "Interruption did not retain the intended pending generated Trash");
            }
            NoRemovalCompletion(f); return ReadRemovalIntent(IntentPath(f));
        }
        static void ResumeOwnershipTests() {
            foreach (int boundary in new[] { 1, 3 }) {
                int selected = boundary;
                Test("resume review " + (selected == 1 ? "initial" : "final") + " device callback cannot replace the empty original source root", () => { using (var f = new Fixture()) {
                    var intent = InterruptedOwnershipFixture(f); var operations = new Operations(); int calls = 0; bool changed = false;
                    var guard = new EvidenceGuard { OnValidate = () => { if (++calls == selected) { changed = true; ReplaceFixtureDirectory(f, f.Source); } } };
                    Throws<SafetyException>(() => ReviewInterrupted(f, guard, operations: operations));
                    Check(changed && File.ReadAllText(Path.Combine(f.Source, "foreign.keep")) == "retain replacement" && Directory.Exists(f.Source + "-moved"), "Source root replacement did not run or was changed"); CheckOwnershipSaved(intent); NoRemovalCompletion(f); using (operations.Reserve(guard.ProtectedDisks)) { }
                } });
            }
            foreach (int boundary in new[] { 1, 2 }) {
                int selected = boundary;
                Test("resume review " + (selected == 1 ? "first" : "per-file") + " device callback cannot replace a saved parent while preserving exact file identity", () => { using (var f = new Fixture()) {
                    var intent = InterruptedOwnershipFixture(f, true); string saved = Path.Combine(intent.BatchFolder, intent.Files.Single().SavedRelativePath), parent = Path.GetDirectoryName(saved); int calls = 0; bool changed = false;
                    var guard = new EvidenceGuard { OnValidate = () => { if (++calls == selected) { changed = true; ReplaceFixtureDirectory(f, parent); File.Move(Path.Combine(parent + "-moved", Path.GetFileName(saved)), saved); } } };
                    Throws<SafetyException>(() => ReviewInterrupted(f, guard));
                    Check(changed && Directory.Exists(parent + "-moved") && File.ReadAllText(Path.Combine(parent, "foreign.keep")) == "retain replacement", "Saved parent replacement did not run or was changed"); CheckOwnershipSaved(intent); NoRemovalCompletion(f);
                } });
            }
            Test("resume review cancellation from final device callback cannot return source-removal evidence", () => { using (var f = new Fixture()) using (var stop = new CancellationTokenSource()) {
                var intent = InterruptedOwnershipFixture(f); int calls = 0; var guard = new EvidenceGuard { OnValidate = () => { if (++calls == 3) stop.Cancel(); } };
                Throws<OperationCanceledException>(() => ReviewInterrupted(f, guard, token: stop.Token)); Check(calls == 3, "Final review callback did not run"); CheckOwnershipSaved(intent); NoRemovalCompletion(f);
            } });
            foreach (string boundary in new[] { "Started explicit resume", "Complete resume batch verified before receipt", "Publishing verified resumed import receipt" }) {
                string selected = boundary;
                Test("resume " + selected + " callback cannot replace the empty source root and publish completion", () => { using (var f = new Fixture()) {
                    var intent = InterruptedOwnershipFixture(f); var confirmed = ReviewInterrupted(f); bool changed = false;
                    Action<string> callback = line => { if (!changed && line.StartsWith(selected)) { changed = true; ReplaceFixtureDirectory(f, f.Source); } };
                    Throws<SafetyException>(() => ResumeFixture(f, confirmed, report: callback, audit: callback));
                    Check(changed && File.ReadAllText(Path.Combine(f.Source, "foreign.keep")) == "retain replacement" && Directory.Exists(f.Source + "-moved"), "Resume callback did not replace its source or changed the replacement"); CheckOwnershipSaved(intent); NoRemovalCompletion(f);
                } });
            }
            Test("resume replaced absent-source parent at disposition report retains every pending Trash payload and record", () => { using (var f = new Fixture()) {
                string payload; var intent = InterruptedOwnershipRecovery(f, out payload); var confirmed = ReviewInterrupted(f); string parent = Path.Combine(f.Source, "!DCIM"), record = Path.Combine(Path.GetDirectoryName(payload), "$IBOUND"); byte[] payloadBytes = File.ReadAllBytes(payload), recordBytes = File.ReadAllBytes(record); bool changed = false;
                Throws<SafetyException>(() => ResumeFixture(f, confirmed, report: line => { if (!changed && line.StartsWith("Resuming verified Trash file")) { changed = true; ReplaceFixtureDirectory(f, parent); } }));
                Check(changed && File.ReadAllBytes(payload).SequenceEqual(payloadBytes) && File.ReadAllBytes(record).SequenceEqual(recordBytes) && File.ReadAllText(Path.Combine(parent, "foreign.keep")) == "retain replacement", "Changed parent crossed disposition or altered remaining generated Trash"); CheckOwnershipSaved(intent); NoRemovalCompletion(f);
            } });
            Test("resume parent replacement after payload removal preserves record reports exact partial count and requires fresh retry", () => { using (var f = new Fixture()) {
                string payload; var intent = InterruptedOwnershipRecovery(f, out payload); var confirmed = ReviewInterrupted(f); string parent = Path.Combine(f.Source, "!DCIM"), record = Path.Combine(Path.GetDirectoryName(payload), "$IBOUND"); byte[] recordBytes = File.ReadAllBytes(record); bool changed = false;
                var failure = Throws<RemovalResumeFailure>(() => ResumeFixture(f, confirmed, audit: line => { if (!changed && line.StartsWith("Removed resumed verified source") && line.EndsWith(payload)) { changed = true; ReplaceFixtureDirectory(f, parent); } }));
                Check(changed && failure.RemovedOriginals == 0 && failure.ClearedTrashFiles == 1 && failure.ClearedTrashRecords == 0 && !File.Exists(payload) && File.ReadAllBytes(record).SequenceEqual(recordBytes) && File.ReadAllText(Path.Combine(parent, "foreign.keep")) == "retain replacement", "Partial counts or remaining record/foreign files were lost"); CheckOwnershipSaved(intent); NoRemovalCompletion(f);
                Throws<SafetyException>(() => ResumeFixture(f, confirmed)); var fresh = ReviewInterrupted(f); var retry = ResumeFixture(f, fresh);
                Check(retry.RemovedOriginals == 0 && retry.ClearedTrashFiles == 0 && retry.ClearedTrashRecords == 1 && retry.PriorAbsentOriginals == 1 && retry.PriorAbsentTrashFiles == 1 && !File.Exists(record) && File.ReadAllText(Path.Combine(parent, "foreign.keep")) == "retain replacement" && File.Exists(retry.ReceiptPath), "Fresh explicit retry changed foreign data or miscounted prior absence/current clearing"); CheckOwnershipSaved(intent);
            } });
        }
    }
}
