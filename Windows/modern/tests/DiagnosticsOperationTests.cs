using System;
using System.IO;

namespace EasyEject {
    public static partial class Tests {
        static AppIssue ModernIssue(string message = "Generated operation failed") { var issue = new AppIssue(); issue.Record(message); return issue; }
        static void DiagnosticsOperationTests() {
            Test("modern tray deduplicates card removal groups while retaining physical fallback counts", delegate {
                var state = ModernStorageState();
                state.Volumes.Add(new Volume { Disks = new System.Collections.Generic.List<int> { state.Disks[0].Number }, Group = StorageGroup.Camera });
                Check(TrayPresentation.CardCount(null) == 0 && TrayPresentation.CardCount(state) == 1, "Sibling volumes counted as separate cards");
                var sibling = new Disk { Number = 8, TargetId = state.Disks[0].TargetId }; state.Disks.Add(sibling); state.Volumes[1].Disks.Add(8);
                Check(TrayPresentation.CardCount(state) == 1, "Shared removal group counted twice"); sibling.TargetId = "other-generated-device"; Check(TrayPresentation.CardCount(state) == 2, "Distinct removal group hidden");
                state.Disks[0].TargetId = ""; sibling.TargetId = null; Check(TrayPresentation.CardCount(state) == 2, "Physical fallback deduplication changed");
            });
            Test("modern issue opening and cancellation never acknowledge or retry anything", delegate {
                var issue = ModernIssue(); int writes = 0; var review = new IssueReview(issue, value => writes++);
                Check(review.CanDismiss && review.Description.Contains(issue.Message) && writes == 0, "Opening changed issue"); review.Cancel();
                Throws<SafetyException>(() => review.Dismiss()); Check(issue.NeedsAttention && !review.CanDismiss && writes == 0, "Cancel acknowledged issue");
            });
            Test("modern explicit issue acknowledgement persists exact explanation without serializing revision", delegate {
                string root = ModernDefaultsFixture(); var issue = ModernIssue(); string message = issue.Message, observed = issue.ObservedUtc; int writes = 0;
                var review = new IssueReview(issue, value => { writes++; AppIssue.Save(value, root); }); review.Dismiss();
                var saved = AppIssue.Load(root); Check(writes == 1 && issue.Dismissed && saved.Dismissed && saved.Message == message && saved.ObservedUtc == observed, "Exact issue not retained");
                Check(!File.ReadAllText(Path.Combine(root, "issue-state.json")).Contains("revision"), "Private revision escaped schema");
                Throws<SafetyException>(() => review.Dismiss()); Check(writes == 1 && !new IssueReview(saved, value => {}).CanDismiss, "Acknowledged issue remained executable");
            });
            Test("modern changed issue and same-payload later revision refuse an old acknowledgement", delegate {
                for (int mode = 0; mode < 4; mode++) {
                    var issue = ModernIssue(); int writes = 0; var review = new IssueReview(issue, value => writes++); string original = issue.ObservedUtc;
                    if (mode == 0) issue.Message = "Generated different issue";
                    else if (mode == 1) issue.ObservedUtc = "2026-10-07T01:00:00Z";
                    else if (mode == 2) issue.Dismiss();
                    else { issue.Record(issue.Message); issue.ObservedUtc = original; }
                    Check(!review.CanDismiss, "Changed issue still offered"); Throws<SafetyException>(() => review.Dismiss()); Check(writes == 0, "Old review acknowledged later issue");
                }
            });
            Test("modern failed issue save is consumed and retains current attention", delegate {
                var issue = ModernIssue(); int writes = 0; var review = new IssueReview(issue, value => { writes++; throw new IOException("Generated issue save failure"); });
                Throws<IOException>(() => review.Dismiss()); Throws<SafetyException>(() => review.Dismiss()); Check(issue.NeedsAttention && writes == 1, "Failed acknowledgement retried or hid attention");
            });
            Test("modern durable acknowledgement then callback failure requires reread before retry", delegate {
                string root = ModernDefaultsFixture(); var issue = ModernIssue(); var review = new IssueReview(issue, value => { AppIssue.Save(value, root); throw new IOException("Generated failure after durable save"); });
                Throws<IOException>(() => review.Dismiss()); Throws<SafetyException>(() => review.Dismiss()); var saved = AppIssue.Load(root);
                Check(issue.NeedsAttention && saved.Dismissed && !new IssueReview(saved, value => {}).CanDismiss, "Uncertain save claimed rollback or replayed");
            });
            Test("modern newer issue during acknowledgement retains its explanation and attention", delegate {
                var issue = ModernIssue(); var review = new IssueReview(issue, value => issue.Record("Generated later failure"));
                Check(Throws<SafetyException>(() => review.Dismiss()).Message.Contains("newer issue"), "Late outcome hidden");
                Check(issue.Message == "Generated later failure" && issue.NeedsAttention, "Later issue acknowledged by old review");
            });
            Test("modern issue save owns review and saver cannot alias current explanation", delegate {
                var issue = ModernIssue(); string message = issue.Message; IssueReview review = null;
                review = new IssueReview(issue, value => { Throws<InvalidOperationException>(() => review.Cancel()); Throws<SafetyException>(() => review.Dismiss()); value.Message = "Generated saver mutation"; });
                review.Dismiss(); Check(issue.Message == message && issue.Dismissed, "Saver mutated current explanation or worker lost ownership");
                issue.Record("Generated next failure"); Check(issue.NeedsAttention, "Later failure did not restore attention");
            });
            Test("modern bounded issue state roundtrips and acknowledged explanation stays viewable", delegate {
                string root = ModernDefaultsFixture(); var issue = ModernIssue(new string('x', 5000)); Check(issue.Message.Length == 4096, "Issue unbounded");
                issue.Dismiss(); AppIssue.Save(issue, root); var loaded = AppIssue.Load(root); var review = new IssueReview(loaded, value => {});
                Check(loaded.HasIssue && !loaded.NeedsAttention && review.Description.Contains("acknowledged") && !review.CanDismiss, "Acknowledged issue vanished");
            });
            Test("modern log view opening and copy use displayed snapshot without automatic reads or clear", delegate {
                string root = ModernDefaultsFixture(); DebugLog.Append(root, "Generated displayed event"); var snapshot = DebugLog.Load(root); int reads = 0, clears = 0; string copied = null;
                using (var review = new DiagnosticsReview(snapshot, () => { reads++; return DebugLog.Load(root); }, value => { clears++; DebugLog.Clear(value, root); })) {
                    Check(reads + clears == 0 && review.CanCopy && review.CanClear, "Opening performed another action"); DebugLog.Append(root, "Generated unseen event");
                    review.Copy(value => copied = value); Check(copied == snapshot.Text && !copied.Contains("unseen") && reads + clears == 0, "Copy loaded unseen content or cleared log");
                }
            });
            Test("modern missing or closed log views refuse actions without publishing or clipboard use", delegate {
                string root = ModernDefaultsFixture(); using (var review = new DiagnosticsReview(root)) {
                    Check(!review.CanCopy && !review.CanClear, "Missing log enabled actions"); Throws<SafetyException>(() => review.Copy(value => { throw new Exception("No clipboard expected"); })); Throws<SafetyException>(() => review.Clear());
                    DebugLog.Append(root, "Generated new event"); review.Refresh(); Check(review.CanCopy && review.Text.Contains("new event"), "Explicit refresh missing"); review.Dispose();
                    Throws<SafetyException>(() => review.Refresh()); Throws<SafetyException>(() => review.Clear()); Throws<SafetyException>(() => review.Copy(value => {})); Check(DebugLog.Load(root).Exists, "Closed view removed log");
                }
            });
            Test("modern stale log clear is one attempt until explicit fresh review", delegate {
                string root = ModernDefaultsFixture(); DebugLog.Append(root, "Generated original"); int clears = 0;
                using (var review = new DiagnosticsReview(DebugLog.Load(root), () => DebugLog.Load(root), value => { clears++; DebugLog.Clear(value, root); })) {
                    DebugLog.Append(root, "Generated later event"); Throws<SafetyException>(() => review.Clear()); Check(!review.CanClear && clears == 1 && DebugLog.Read(root).Contains("later"), "Stale log was removed");
                    Throws<SafetyException>(() => review.Clear()); Check(clears == 1, "Consumed clear retried"); review.Refresh(); review.Clear(); Check(!DebugLog.Load(root).Exists && clears == 2, "Fresh explicit clear failed");
                }
            });
            Test("modern exact log clear retains every neighboring configuration and media file", delegate {
                string root = ModernDefaultsFixture(); foreach (string name in new[] { "app-settings.json", "issue-state.json", "history.json", "generated.mp4" }) File.WriteAllText(Path.Combine(root, name), "Generated keep bytes");
                DebugLog.Append(root, "Generated private log"); using (var review = new DiagnosticsReview(root)) { review.Clear(); Check(!review.CanClear && !review.CanCopy, "Cleared log actions stayed available"); }
                foreach (string name in new[] { "app-settings.json", "issue-state.json", "history.json", "generated.mp4" }) Check(File.ReadAllText(Path.Combine(root, name)) == "Generated keep bytes", "Neighbor removed: " + name);
                Check(!File.Exists(Path.Combine(root, "debug.log")), "Exact clear did not finish");
            });
            Test("modern identical replacement log with restored time refuses old view", delegate {
                string root = ModernDefaultsFixture(), path = Path.Combine(root, "debug.log"), old = Path.Combine(root, "retained-old.log"); DebugLog.Append(root, "Generated same bytes");
                using (var review = new DiagnosticsReview(root)) {
                    string bytes = File.ReadAllText(path); var time = File.GetLastWriteTimeUtc(path); File.Move(path, old); File.WriteAllText(path, bytes); File.SetLastWriteTimeUtc(path, time);
                    Throws<SafetyException>(() => review.Clear()); Check(File.ReadAllText(path) == bytes && File.ReadAllText(old) == bytes, "Replacement or old log removed");
                }
            });
            Test("modern clear-success refresh-failure reports deletion honestly without retry", delegate {
                string root = ModernDefaultsFixture(); DebugLog.Append(root, "Generated log"); bool refuse = true; int clears = 0;
                using (var review = new DiagnosticsReview(DebugLog.Load(root), () => { if (refuse) throw new IOException("Generated readback failure"); return DebugLog.Load(root); }, value => { clears++; DebugLog.Clear(value, root); })) {
                    Check(Throws<SafetyException>(() => review.Clear()).Message.Contains("was cleared"), "Applied clear reported as rollback");
                    Check(!File.Exists(Path.Combine(root, "debug.log")) && !review.CanClear && clears == 1 && review.Text.Contains("Generated log"), "Readback failure lost displayed evidence");
                    Throws<SafetyException>(() => review.Clear()); refuse = false; review.Refresh(); Check(!review.CanCopy && clears == 1, "Readback automatically repeated clear");
                }
            });
            Test("modern refresh and clipboard failures preserve displayed bytes and remain explicit", delegate {
                string root = ModernDefaultsFixture(); DebugLog.Append(root, "Generated keep"); var snapshot = DebugLog.Load(root); int reads = 0, copies = 0;
                using (var review = new DiagnosticsReview(snapshot, () => { reads++; throw new IOException("Generated load failure"); }, value => { throw new Exception("No clear expected"); })) {
                    Throws<IOException>(() => review.Refresh()); Throws<IOException>(() => review.Copy(value => { copies++; throw new IOException("Generated clipboard busy"); }));
                    Check(reads == 1 && copies == 1 && review.Text == snapshot.Text && DebugLog.Read(root) == snapshot.Text, "Failure rewrote diagnostic content");
                }
            });
            Test("modern debug worker rejects close and other actions during clear callback", delegate {
                string root = ModernDefaultsFixture(); DebugLog.Append(root, "Generated worker"); DiagnosticsReview review = null;
                review = new DiagnosticsReview(DebugLog.Load(root), () => DebugLog.Load(root), value => {
                    Check(review.Busy && !review.CanCopy && !review.CanClear, "Worker not owned"); Throws<InvalidOperationException>(() => review.Dispose()); Throws<SafetyException>(() => review.Refresh()); Throws<SafetyException>(() => review.Copy(text => {})); Throws<SafetyException>(() => review.Clear()); DebugLog.Clear(value, root);
                });
                review.Clear(); Check(!DebugLog.Load(root).Exists && review.Open, "Worker ownership lost"); review.Dispose();
            });
            Test("modern debug review preserves read-only protection when clear refuses", delegate {
                string root = ModernDefaultsFixture(), path = Path.Combine(root, "debug.log"); DebugLog.Append(root, "Generated protected bytes");
                using (var review = new DiagnosticsReview(root)) {
                    File.SetAttributes(path, FileAttributes.ReadOnly);
                    try { Throws<Exception>(() => review.Clear()); Check((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0 && File.ReadAllText(path) == review.Text && !review.CanClear, "Protection or bytes changed"); }
                    finally { File.SetAttributes(path, FileAttributes.Normal); }
                }
            });
            Test("modern information names actual runtime and available tools without claiming release approval", delegate {
                Check(ApplicationInformation.About.Contains(".NET Framework 4.8") && !ApplicationInformation.About.Contains("WinUI 3"), "Classic About changed platform");
                Check(ApplicationInformation.ModernAbout.Contains(".NET 10 / WinUI 3") && !ApplicationInformation.ModernAbout.Contains("Framework 4.8") && ApplicationInformation.ModernAbout.Contains(ApplicationInformation.Version), "Modern About has wrong runtime/version");
                string help = ApplicationInformation.ModernHelp; Check(help.Contains("Quick Share tools lets you select") && help.Contains("Launch at login") && help.Contains("Updates are unavailable") && !help.Contains("Open Windows updates & installation"), "Help advertised unavailable action");
                foreach (string required in new[] { "last 20", "1080p or 4K", "full-quality imported originals", "Export does not automatically eject", "Windows Startup apps" }) Check(help.Contains(required), "Missing manual parity guidance: " + required);
            });
        }
    }
}
