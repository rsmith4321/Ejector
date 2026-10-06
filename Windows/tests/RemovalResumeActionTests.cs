using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static ImportResult ResumeFixture(Fixture f, RemovalResumeEvidence confirmed = null, Action<string> report = null, Action<string> audit = null, CancellationToken token = default(CancellationToken), EvidenceGuard guard = null, Operations operations = null) {
            return RemovalResumeEvidence.Resume(confirmed ?? ReviewInterrupted(f), IntentPath(f), f.Source, f.Source, f.Destination, FixtureTrashSid, guard ?? new EvidenceGuard(), operations ?? new Operations(), token, report ?? (x => {}), audit);
        }
        static void InterruptConfiguredRecovery(Fixture f, MediaOptions options, string afterSource) {
            using (var cancellation = new CancellationTokenSource()) {
                Throws<OriginalRemovalFailure>(() => new VerifiedImporter(new Operations(), Native.FreeSpace,
                    line => { if (line.StartsWith("Removed verified original") && line.EndsWith(afterSource)) cancellation.Cancel(); },
                    (root, uid, token, validate) => DeviceTrash.Read(root, FixtureTrashSid, uid, token, validate))
                    .Run(f.Source, f.Source, f.Destination, "fixture-volume", new EvidenceGuard(), cancellation.Token, x => {}, options));
            }
        }
        static void RemovalResumeActionTests() {
            Test("manual resume clears the exact orphan and exclusively completes the original verified batch", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); var result = ResumeFixture(f);
                Check(!File.Exists(record) && File.ReadAllText(Path.Combine(f.Source, "ordinary.mp4")) == "Fixture ordinary original" && File.Exists(Path.Combine(f.Source, "fileinfo_list.list")), "Resume changed retained originals/indexes or failed to clear the orphan");
                Check(result.RemovedOriginals == 0 && result.ClearedTrashFiles == 0 && result.ClearedTrashRecords == 1 && result.PriorAbsentTrashFiles == 1 && result.PriorAbsentOriginals == 0 && result.Reused == 4 && result.Receipt.OriginalsKept && result.Receipt.HasSelectedMedia && result.Receipt.RemovalIntentSha256 == ReviewHashForFixture(f) && File.Exists(result.ReceiptPath), "Prior missing data was counted as removal by this action or complete batch was lost");
                Check(result.Receipt.Files.Single(x => x.IsTrashRecord).RemovedSource && !result.Receipt.Files.Single(x => x.IsTrashRecord).SourceAbsentAtResume && result.Receipt.Files.Single(x => x.FromTrash && !x.IsTrashRecord).SourceAbsentAtResume && result.Receipt.Files.Count(x => x.IsMedia) == 2 && File.Exists(Path.Combine(result.Folder, result.Receipt.ResumeAuditPath)), "Receipt lacks distinct prior absence, raw-record exclusion or durable resume audit");
                Check(Throws<SafetyException>(() => ReviewInterrupted(f)).Message.Contains("completion receipt"), "Completed resume could run again");
            } });
            Test("resumed receipt restores exact last-20 history after source absence and restart", delegate { using (var f = new Fixture()) {
                InterruptedRecovery(f); var result = ResumeFixture(f); string historyRoot = Path.Combine(f.Root, "history"); Directory.CreateDirectory(historyRoot);
                ImportHistory.Add(Batch(f, result), historyRoot); var restored = ImportHistory.Load(historyRoot).Single();
                string disconnected = Path.Combine(f.Root, "source-unavailable"); Check(SafePaths.Within(f.Source, f.Root) && SafePaths.Within(disconnected, f.Root), "Fixture move escaped its root"); Directory.Move(f.Source, disconnected); Directory.CreateDirectory(f.Source);
                int reads = 0; ImportHistory.Verify(restored, HistoryIdentity(), new FakeGuard(), new Operations(), CancellationToken.None, x => reads++);
                Check(reads == 2 && restored.Originals.All(x => !x.Path.Contains("Recovered Trash Records")) && restored.Originals.Any(x => x.Path.EndsWith("clip.mp4")), "Resume history omitted exact recovered originals, included raw records or depended on source presence");
            } });
            Test("resume finishes remaining ordinary removal only when frozen journal choice requested it", delegate { using (var f = new Fixture()) {
                f.Put("ordinary.mp4", "Fixture ordinary original"); f.Put("fileinfo_list.list", "Fixture retained index"); string payload = PutRecycle(f, "ORDER", @"E:\clip.mp4", "Fixture Trash original");
                InterruptConfiguredRecovery(f, RecoverOptions(delete: true), payload); var review = ReviewInterrupted(f);
                Check(review.Options.DeleteOriginals && review.RemainingOriginals == 1 && review.RemainingTrashRecords == 1, "Frozen pending ordinary removal omitted from review");
                var result = ResumeFixture(f, review); Check(result.RemovedOriginals == 1 && result.PriorAbsentOriginals == 0 && result.PriorAbsentTrashFiles == 1 && result.ClearedTrashRecords == 1 && !result.Receipt.OriginalsKept && !File.Exists(Path.Combine(f.Source, "ordinary.mp4")) && File.Exists(Path.Combine(f.Source, "fileinfo_list.list")), "Frozen deletion/index preservation or action counts changed");
            } });
            Test("partial resumed payload cancellation keeps both restoration records and allows fresh manual retry", delegate { using (var f = new Fixture()) using (var cancellation = new CancellationTokenSource()) {
                f.Put("ordinary.mp4", "Fixture original"); string first = PutRecycle(f, "FIRST", @"E:\first.mp4", "Fixture first"), second = PutRecycle(f, "SECOND", @"E:\second.mp4", "Fixture second");
                InterruptConfiguredRecovery(f, RecoverOptions(), first); var review = ReviewInterrupted(f); var operations = new Operations();
                var failure = Throws<RemovalResumeFailure>(() => ResumeFixture(f, review, audit: line => { if (line.StartsWith("Removed resumed verified source") && line.EndsWith(second)) cancellation.Cancel(); }, token: cancellation.Token, operations: operations));
                Check(failure.ClearedTrashFiles == 1 && failure.ClearedTrashRecords == 0 && failure.RemovedOriginals == 0 && !File.Exists(second) && Directory.GetFiles(Path.GetDirectoryName(first), "$I*").Length == 2 && File.Exists(failure.AuditPath), "Partial resume cleared records early or lost exact accepted-disposition counts");
                NoRemovalCompletion(f); using (operations.Reserve(new[] { 700, 701 })) {}
                var retried = ResumeFixture(f); Check(retried.ClearedTrashFiles == 0 && retried.ClearedTrashRecords == 2 && retried.PriorAbsentTrashFiles == 2 && retried.PriorAbsentTrashRecords == 0, "Fresh retry recounted prior missing payloads or failed to finish their records");
            } });
            Test("resume can publish after all clearing finished but the earlier receipt was cancelled", delegate { using (var f = new Fixture()) using (var cancellation = new CancellationTokenSource()) {
                string record = InterruptedRecovery(f);
                var failure = Throws<RemovalResumeFailure>(() => ResumeFixture(f, audit: line => { if (line.StartsWith("Removed resumed verified source") && line.EndsWith(record)) cancellation.Cancel(); }, token: cancellation.Token));
                Check(failure.ClearedTrashRecords == 1 && !File.Exists(record), "Last record clearing was not reported"); NoRemovalCompletion(f);
                var review = ReviewInterrupted(f); Check(review.RemainingOriginals + review.RemainingTrashFiles + review.RemainingTrashRecords == 0 && review.PendingWindowsRecords.Count == 0, "All-cleared incomplete batch was unavailable for completion review");
                var result = ResumeFixture(f, review); Check(result.ClearedTrashRecords == 0 && result.PriorAbsentTrashRecords == 1 && result.PriorAbsentTrashFiles == 1 && File.Exists(result.ReceiptPath), "Receipt-only retry performed or claimed new removals");
            } });
            Test("failed resume audit before disposition leaves all remaining source data intact", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); Throws<IOException>(() => ResumeFixture(f, audit: line => { if (line.StartsWith("Before resumed permanent removal")) throw new IOException("Fixture pre-disposition audit failure"); }));
                Check(File.Exists(record) && File.Exists(Path.Combine(f.Source, "ordinary.mp4")), "Failed audit allowed permanent removal"); NoRemovalCompletion(f);
                Check(ResumeFixture(f).ClearedTrashRecords == 1, "Audit refusal left locks or prevented fresh retry");
            } });
            Test("failed audit after accepted resume disposition reports exact count and preserves retry copies", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); var failure = Throws<RemovalResumeFailure>(() => ResumeFixture(f, audit: line => { if (line.StartsWith("Removed resumed verified source")) throw new IOException("Fixture post-disposition audit failure"); }));
                Check(failure.ClearedTrashRecords == 1 && !File.Exists(record) && File.ReadAllText(Directory.GetFiles(f.Destination, "clip.mp4", SearchOption.AllDirectories).Single()) == "Fixture Trash original", "Post-disposition audit failure lost count or copies"); NoRemovalCompletion(f);
                Check(ResumeFixture(f).PriorAbsentTrashRecords == 1, "After-disposition retry lost prior absence evidence");
            } });
            Test("resume holds journal original audit every copy and remaining sources through record disposition", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); int protectedChecks = 0;
                var result = ResumeFixture(f, report: text => { if (text.StartsWith("Resuming verified Trash record")) {
                    foreach (string path in new[] { record, IntentPath(f), Path.Combine(f.Source, "ordinary.mp4"), Path.Combine(Path.GetDirectoryName(IntentPath(f)), "import-audit.log"), Directory.GetFiles(f.Destination, "clip.mp4", SearchOption.AllDirectories).Single(), Directory.GetFiles(f.Destination, "$IRESUME", SearchOption.AllDirectories).Single() }) { Throws<IOException>(() => File.WriteAllText(path, "Fixture forbidden mutation")); protectedChecks++; }
                    Throws<IOException>(() => Directory.Move(Path.GetDirectoryName(record), Path.Combine(f.Root, "moved-owner"))); protectedChecks++;
                } });
                Check(protectedChecks == 7 && result.ClearedTrashRecords == 1, "Complete batch or owner ancestors were unprotected during removal");
            } });
            Test("new source absence after confirmation refuses execution until a fresh review", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); var confirmed = ReviewInterrupted(f); File.Delete(record);
                Check(Throws<SafetyException>(() => ResumeFixture(f, confirmed)).Message.Contains("presence changed after confirmation"), "Old confirmation accepted a different absent-source set"); NoRemovalCompletion(f);
                var result = ResumeFixture(f); Check(result.ClearedTrashRecords == 0 && result.PriorAbsentTrashRecords == 1, "Fresh receipt-only review claimed removal performed elsewhere");
            } });
            Test("journal modification after confirmation refuses execution before any clearing", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); var confirmed = ReviewInterrupted(f); File.AppendAllText(IntentPath(f), " ");
                Check(Throws<SafetyException>(() => ResumeFixture(f, confirmed)).Message.Contains("differs from the reviewed batch") && File.Exists(record), "Old confirmation trusted changed journal bytes"); NoRemovalCompletion(f);
            } });
            Test("new selected recording after confirmation invalidates execution and retains the orphan", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); var confirmed = ReviewInterrupted(f); f.Put("new.mp4", "Fixture newly recorded original");
                Throws<SafetyException>(() => ResumeFixture(f, confirmed)); Check(File.Exists(record) && File.Exists(Path.Combine(f.Source, "new.mp4")), "Resume ignored a changed selected manifest"); NoRemovalCompletion(f);
            } });
            Test("unselected photos arriving after confirmation remain and do not broaden resume", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); var confirmed = ReviewInterrupted(f); f.Put("photo.JPG", "Fixture unselected ordinary photo"); string photo = PutRecycle(f, "PHOTO", @"E:\photo.JPG", "Fixture unselected Trash photo");
                var result = ResumeFixture(f, confirmed); Check(result.ClearedTrashRecords == 1 && !File.Exists(record) && File.Exists(photo) && File.Exists(Path.Combine(f.Source, "photo.JPG")), "Unselected photos blocked or expanded clearing");
            } });
            Test("guard loss at the final per-source boundary stops before native resume disposition", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); var confirmed = ReviewInterrupted(f); var guard = new EvidenceGuard(); bool lost = false; guard.OnValidate = () => { if (lost) throw new SafetyException("Fixture connection changed at resume boundary"); };
                Check(Throws<SafetyException>(() => ResumeFixture(f, confirmed, report: text => { if (text.StartsWith("Resuming verified Trash record")) lost = true; }, guard: guard)).Message.Contains("connection changed at resume boundary") && File.Exists(record), "Resume crossed a failed final source guard"); NoRemovalCompletion(f);
            } });
            Test("cancellation before resumed native disposition retains the current source and complete copies", delegate { using (var f = new Fixture()) using (var cancellation = new CancellationTokenSource()) {
                string record = InterruptedRecovery(f); var confirmed = ReviewInterrupted(f); var operations = new Operations();
                Throws<OperationCanceledException>(() => ResumeFixture(f, confirmed, report: text => { if (text.StartsWith("Resuming verified Trash record")) cancellation.Cancel(); }, token: cancellation.Token, operations: operations));
                Check(File.Exists(record) && File.ReadAllText(Directory.GetFiles(f.Destination, "clip.mp4", SearchOption.AllDirectories).Single()) == "Fixture Trash original", "Pre-disposition cancellation cleared source or changed copies"); NoRemovalCompletion(f); using (operations.Reserve(new[] { 700, 701 })) {}
            } });
            Test("receipt publication conflict retains existing data and reports the already cleared record", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); string receipt = Path.Combine(Path.GetDirectoryName(IntentPath(f)), "verified-import.json");
                var failure = Throws<RemovalResumeFailure>(() => ResumeFixture(f, report: text => { if (text.StartsWith("Publishing verified resumed")) File.WriteAllText(receipt, "Existing fixture receipt"); }));
                Check(failure.ClearedTrashRecords == 1 && !File.Exists(record) && File.ReadAllText(receipt) == "Existing fixture receipt" && Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0, "Resume overwrote a conflicting receipt, lost partial count or retained private work");
            } });
            Test("copies remain held through final resumed receipt publication", delegate { using (var f = new Fixture()) {
                InterruptedRecovery(f); int checkedCopies = 0; var intent = ReadRemovalIntent(IntentPath(f));
                var result = ResumeFixture(f, report: text => { if (text.StartsWith("Publishing verified resumed")) {
                    foreach (var entry in intent.Files) { Throws<IOException>(() => File.WriteAllText(Path.Combine(intent.BatchFolder, entry.SavedRelativePath), "Fixture forbidden replacement")); checkedCopies++; }
                } });
                Check(checkedCopies == 4 && File.Exists(result.ReceiptPath), "Some dependencies were released before the receipt became durable");
            } });
            Test("read-only restoration record refuses the whole execution without changing its protection", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); var confirmed = ReviewInterrupted(f); FileAttributes attributes = File.GetAttributes(record); File.SetAttributes(record, attributes | FileAttributes.ReadOnly);
                try { Throws<SafetyException>(() => ResumeFixture(f, confirmed)); Check(File.Exists(record) && (File.GetAttributes(record) & FileAttributes.ReadOnly) != 0, "Resume removed or unlocked a protected record"); NoRemovalCompletion(f); }
                finally { File.SetAttributes(record, attributes); }
            } });
            Test("a durable intent cancelled before the first removal can resume the full frozen batch", delegate { using (var f = new Fixture()) using (var cancellation = new CancellationTokenSource()) {
                string payload = PutRecycle(f, "UNSTARTED", @"E:\clip.mp4", "Fixture untouched Trash original");
                Throws<OperationCanceledException>(() => new VerifiedImporter(new Operations(), Native.FreeSpace,
                    line => { if (line.StartsWith("Durable removal intent")) cancellation.Cancel(); }, (root, uid, token, validate) => DeviceTrash.Read(root, FixtureTrashSid, uid, token, validate))
                    .Run(f.Source, f.Source, f.Destination, "fixture-volume", new EvidenceGuard(), cancellation.Token, x => {}, RecoverOptions()));
                var confirmed = ReviewInterrupted(f); Check(confirmed.AbsentSources.Count == 0 && confirmed.RemainingTrashFiles == 1 && confirmed.RemainingTrashRecords == 1 && confirmed.PendingWindowsRecords.Count == 0, "Unstarted clearing was not available for explicit resume review");
                var result = ResumeFixture(f, confirmed); Check(result.ClearedTrashFiles == 1 && result.ClearedTrashRecords == 1 && result.PriorAbsentTrashFiles == 0 && !File.Exists(payload), "Unstarted batch was confused with prior source absence");
            } });
            Test("resuming explicit Mac owner preserves companions unknowns and every other owner", delegate { using (var f = new Fixture()) {
                string payload = PutRecycle(f, "FIRST", @"E:\first.mp4", "Fixture Windows Trash original");
                f.Put(@".Trashes\501\clip.MOV", "Fixture Mac original"); f.Put(@".Trashes\501\clip.SRT", "Fixture Mac companion"); f.Put(@".Trashes\501\unknown.data", "Fixture Mac unknown"); f.Put(@".Trashes\502\other.mp4", "Fixture other owner");
                InterruptConfiguredRecovery(f, RecoverOptions(macUid: "501"), payload); var result = ResumeFixture(f);
                Check(result.ClearedTrashFiles == 2 && result.ClearedTrashRecords == 1 && result.PriorAbsentTrashFiles == 1 && !File.Exists(Path.Combine(f.Source, @".Trashes\501\clip.MOV")) && !File.Exists(Path.Combine(f.Source, @".Trashes\501\clip.SRT")) && File.Exists(Path.Combine(f.Source, @".Trashes\501\unknown.data")) && File.Exists(Path.Combine(f.Source, @".Trashes\502\other.mp4")), "Mac association/owner scope expanded or ordinary deletion authority changed");
            } });
            Test("resume confirmation defaults to Cancel and discloses frozen choices counts owners and permanence", delegate { using (var f = new Fixture()) {
                InterruptedRecovery(f); var confirmed = ReviewInterrupted(f); string text = RemovalResumeChoice.Explanation(confirmed, "Fixture camera", f.Source, f.Destination);
                using (var dialog = new RemovalResumeConfirmationDialog(text)) Check(dialog.AcceptButton == dialog.Cancel && dialog.CancelButton == dialog.Cancel && dialog.Resume.DialogResult == System.Windows.Forms.DialogResult.Yes && text.Contains("PERMANENTLY REMOVE") && text.Contains("0 ordinary originals, 0 Trash files, and 1 Windows restoration records") && text.Contains("current Windows user") && text.Contains("frozen") && text.Contains("No export or automatic eject"), "Resume acknowledgment omitted scope or had an unsafe default");
                var result = ResumeFixture(f, confirmed); string summary = RemovalResumeChoice.Summary(result); Check(summary.Contains("0 verified ordinary originals") && summary.Contains("1 restoration records") && summary.Contains("Already absent before this action") && summary.Contains("1 Trash files"), "Completion presentation conflated prior missing files and new removal");
            } });
            Test("resumed deleted camera package keeps required support and its folder restoration record", delegate { using (var f = new Fixture()) {
                string owner = Path.Combine(f.Source, "$RECYCLE.BIN", FixtureTrashSid), payload = Path.Combine(owner, "$RPACKAGE"); Directory.CreateDirectory(payload);
                foreach (string name in new[] { @"M4ROOT\CLIP\clip.MXF", @"M4ROOT\CLIP\clip.XML", @"M4ROOT\AUDIO\audio.WAV", @"M4ROOT\THMBNL\clip.JPG" }) { string path = Path.Combine(payload, name); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "Fixture " + name); }
                string record = Path.Combine(owner, "$IPACKAGE"); File.WriteAllBytes(record, RecycleRecord(@"E:\PRIVATE", 0));
                InterruptConfiguredRecovery(f, RecoverOptions(previews: false), Path.Combine(payload, @"M4ROOT\CLIP\clip.MXF"));
                var result = ResumeFixture(f); Check(result.ClearedTrashFiles == 1 && result.PriorAbsentTrashFiles == 1 && result.ClearedTrashRecords == 0 && File.Exists(record) && File.Exists(Path.Combine(payload, @"M4ROOT\AUDIO\audio.WAV")) && File.Exists(Path.Combine(payload, @"M4ROOT\THMBNL\clip.JPG")) && result.Receipt.Files.Count(x => x.IsMedia) == 4, "Resume broke package association, cleared support/folder record, or lost exact saved media history");
            } });
            Test("resume never treats a protected Trash tree as an ordinary selected media folder", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); string selected = Path.Combine(f.Source, "$RECYCLE.BIN", FixtureTrashSid);
                Check(Throws<SafetyException>(() => RemovalResumeEvidence.Review(IntentPath(f), f.Source, selected, f.Destination, FixtureTrashSid, new EvidenceGuard(), new Operations(), CancellationToken.None)).Message.Contains("protected Trash/system tree") && File.Exists(record), "Owner-aware scope bypassed through ordinary folder selection");
            } });
            Test("original-removal-only resume preserves malformed unselected Trash without enumerating recovery", delegate { using (var f = new Fixture()) using (var cancellation = new CancellationTokenSource()) {
                f.Put("first.mp4", "Fixture first original"); f.Put("second.mp4", "Fixture second original"); f.Put("fileinfo_list.list", "Fixture retained index");
                string malformed = Path.Combine("$RECYCLE.BIN", FixtureTrashSid, "$IBROKEN"); f.Put(malformed, "Fixture malformed unrelated Trash"); f.Put(@".Trashes\501\other.mp4", "Fixture unselected Mac owner");
                string first = Path.Combine(f.Source, "first.mp4");
                Throws<OriginalRemovalFailure>(() => new VerifiedImporter(new Operations(), Native.FreeSpace,
                    line => { if (line.StartsWith("Removed verified original") && line.EndsWith(first)) cancellation.Cancel(); },
                    (root, uid, token, validate) => { throw new Exception("Recovery-off must not enumerate Trash"); })
                    .Run(f.Source, f.Source, f.Destination, "fixture-volume", new EvidenceGuard(), cancellation.Token, x => {}, new MediaOptions(true, true, true, false, true, false, "501")));
                var confirmed = ReviewInterrupted(f); Check(!confirmed.Options.RecoverTrash && confirmed.Options.DeleteOriginals && confirmed.RemainingOriginals == 1 && RemovalResumeChoice.Explanation(confirmed, "Fixture camera", f.Source, f.Destination).Contains("recovery is off"), "Original-only review implied recovery or lost frozen scope");
                var result = ResumeFixture(f, confirmed); Check(result.RemovedOriginals == 1 && result.PriorAbsentOriginals == 1 && result.ClearedTrashFiles == 0 && result.ClearedTrashRecords == 0 && !File.Exists(Path.Combine(f.Source, "second.mp4")) && File.Exists(Path.Combine(f.Source, "fileinfo_list.list")) && File.ReadAllText(Path.Combine(f.Source, malformed)) == "Fixture malformed unrelated Trash" && File.Exists(Path.Combine(f.Source, @".Trashes\501\other.mp4")), "Original-only resume swept Trash or lost action/prior counts");
            } });
            Test("original-only receipt retry records prior absence without claiming a new deletion", delegate { using (var f = new Fixture()) using (var cancellation = new CancellationTokenSource()) {
                f.Put("clip.mp4", "Fixture saved original"); string source = Path.Combine(f.Source, "clip.mp4");
                Throws<OriginalRemovalFailure>(() => new VerifiedImporter(new Operations(), Native.FreeSpace, line => { if (line.StartsWith("Removed verified original") && line.EndsWith(source)) cancellation.Cancel(); })
                    .Run(f.Source, f.Source, f.Destination, "fixture-volume", new EvidenceGuard(), cancellation.Token, x => {}, new MediaOptions(true, true, true, false, true)));
                var confirmed = ReviewInterrupted(f); Check(confirmed.RemainingOriginals == 0 && RemovalResumeChoice.Explanation(confirmed, "Fixture camera", f.Source, f.Destination).Contains("re-verifies and records"), "Receipt-only review described new permanent removal");
                var result = ResumeFixture(f, confirmed); Check(result.RemovedOriginals == 0 && result.PriorAbsentOriginals == 1 && !result.Receipt.OriginalsKept && result.Receipt.Files.Single().SourceAbsentAtResume && File.Exists(result.ReceiptPath), "Prior missing original was counted as newly deleted or omitted from the final batch");
            } });
        }
        static string ReviewHashForFixture(Fixture f) { using (var input = SafePaths.ReadFile(IntentPath(f))) return VerifiedImporter.Hash(input, CancellationToken.None); }
    }
}
