using System;
using System.IO;
using System.Runtime.InteropServices;

namespace EasyEject {
    public static partial class Tests {
        [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateIssueFixtureLink(string newName, string existing, IntPtr security);
        static string IssueFixture(AppIssue issue = null) { string root = ModernDefaultsFixture(); if (issue != null) AppIssue.Save(issue, root); return root; }
        static string IssuePath(string root) { return Path.Combine(root, "issue-state.json"); }
        static IssueReview PersistedIssueOwner(IssuePersistenceState state, IssueRecordReview record) {
            IssueReview owner = null; owner = new IssueReview(state.Live, value => state.SaveReviewed(record, value, owner.ValidateCurrent)); return owner;
        }
        static void IssuePersistenceTests() {
            Test("persisted issue review is read only and cancellation consumes its save", delegate {
                string root = IssueFixture(ModernIssue()), before = File.ReadAllText(IssuePath(root)); var review = new IssueRecordReview(root);
                Check(review.Snapshot.Exists && !review.Snapshot.Value.Dismissed, "Read did not retain issue"); var copy = review.Snapshot.Value; copy.Message = "Generated alias change";
                Check(review.Snapshot.Value.Message != copy.Message, "Snapshot exposed alias"); review.Dispose();
                Throws<SafetyException>(() => review.Save(copy)); Check(File.ReadAllText(IssuePath(root)) == before, "Cancel wrote issue");
            });
            Test("persisted issue acknowledgement publishes once and confirms exact payload", delegate {
                string root = IssueFixture(ModernIssue()); var state = IssuePersistenceState.Load(root);
                using (var record = new IssueRecordReview(root)) {
                    state.Refresh(record); var owner = PersistedIssueOwner(state, record); owner.Dismiss();
                    Check(state.Live.Dismissed && !state.RequiresSave && IssueRecordSnapshot.Same(AppIssue.Load(root), state.Live), "Ack not confirmed");
                    Throws<SafetyException>(() => owner.Dismiss()); Throws<SafetyException>(() => record.Save(ModernIssue()));
                }
                Check(!File.ReadAllText(IssuePath(root)).Contains("revision"), "Revision serialized");
            });
            Test("persisted issue rejects byte edits deletion addition and identical replacement", delegate {
                for (int mode = 0; mode < 4; mode++) {
                    string root = IssueFixture(mode == 2 ? null : ModernIssue());
                    using (var record = new IssueRecordReview(root)) {
                        if (mode == 0) AppIssue.Save(ModernIssue("Generated external issue"), root);
                        else if (mode == 1) File.Delete(IssuePath(root));
                        else if (mode == 2) AppIssue.Save(ModernIssue("Generated added issue"), root);
                        else { string text = File.ReadAllText(IssuePath(root)); File.Move(IssuePath(root), Path.Combine(root, "old-owned-issue.json")); File.WriteAllText(IssuePath(root), text); }
                        string before = File.Exists(IssuePath(root)) ? File.ReadAllText(IssuePath(root)) : null;
                        Throws<SafetyException>(() => record.Save(ModernIssue("Generated forbidden replacement")));
                        Check((File.Exists(IssuePath(root)) ? File.ReadAllText(IssuePath(root)) : null) == before, "Changed record overwritten");
                    }
                }
            });
            Test("persisted issue refuses changed record at final writer gate", delegate {
                string root = IssueFixture(ModernIssue()); bool changed = false;
                using (var record = new IssueRecordReview(root, stage => {
                    if (stage == "Before writer gate") { AppIssue.Save(ModernIssue("Generated gate change"), root); changed = true; }
                })) {
                    Throws<SafetyException>(() => record.Save(ModernIssue("Generated old choice")));
                    Check(changed && AppIssue.Load(root).Message == "Generated gate change", "Final gate overwrote new issue");
                }
            });
            Test("persisted issue holds prior record exclusively through publication", delegate {
                string root = IssueFixture(ModernIssue()); bool blocked = false;
                using (var record = new IssueRecordReview(root, stage => {
                    if (stage == "Before publication") { Throws<IOException>(() => File.WriteAllText(IssuePath(root), "Generated competing write")); blocked = true; }
                })) {
                    record.Save(ModernIssue("Generated reviewed replacement"));
                    Check(blocked && AppIssue.Load(root).Message == "Generated reviewed replacement", "Exclusive hold ineffective");
                }
            });
            Test("persisted issue root replacement is refused without touching either tree", delegate {
                string root = IssueFixture(ModernIssue()), moved = root + "-moved";
                using (var record = new IssueRecordReview(root)) {
                    Directory.Move(root, moved); Directory.CreateDirectory(root); AppIssue.Save(ModernIssue("Generated replacement folder issue"), root);
                    string before = File.ReadAllText(IssuePath(root)), original = File.ReadAllText(IssuePath(moved));
                    Throws<SafetyException>(() => record.Save(ModernIssue()));
                    Check(File.ReadAllText(IssuePath(root)) == before && File.ReadAllText(IssuePath(moved)) == original, "Wrong folder written");
                }
            });
            Test("persisted issue catches empty reviewed root movement during a save callback", delegate {
                string root = IssueFixture(), moved = root + "-moved";
                using (var record = new IssueRecordReview(root, stage => {
                    if (stage == "Before issue save") { Directory.Move(root, moved); Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "foreign.txt"), "Generated foreign tree"); }
                })) {
                    Throws<SafetyException>(() => record.Save(ModernIssue()));
                    Check(!File.Exists(IssuePath(root)) && !File.Exists(IssuePath(moved)) && File.ReadAllText(Path.Combine(root, "foreign.txt")) == "Generated foreign tree", "Moved tree written");
                }
            });
            Test("persisted issue freezes caller payload before callbacks", delegate {
                string root = IssueFixture(), original = "Generated immutable issue"; var value = ModernIssue(original);
                using (var record = new IssueRecordReview(root, stage => { if (stage == "Before issue save") value.Message = "Generated caller alias mutation"; })) {
                    var saved = record.Save(value); Check(saved.Value.Message == original && AppIssue.Load(root).Message == original, "Caller changed frozen save");
                }
            });
            Test("persisted issue save worker rejects close and reentry", delegate {
                string root = IssueFixture(); IssueRecordReview record = null; bool checkedWorker = false;
                record = new IssueRecordReview(root, stage => {
                    if (stage == "Before publication") { Throws<InvalidOperationException>(() => record.Dispose()); Throws<SafetyException>(() => record.Save(ModernIssue())); checkedWorker = true; }
                });
                using (record) record.Save(ModernIssue()); Check(checkedWorker, "Worker ownership not exercised");
            });
            Test("persisted issue failed save is consumed and requires a fresh reread", delegate {
                string root = IssueFixture(ModernIssue()); var state = IssuePersistenceState.Load(root);
                using (var record = new IssueRecordReview(root, stage => { if (stage == "Before publication") throw new IOException("Generated pre-publication failure"); })) {
                    state.Refresh(record); var owner = PersistedIssueOwner(state, record); Throws<IOException>(() => owner.Dismiss());
                    Throws<SafetyException>(() => owner.Dismiss()); Check(state.Live.NeedsAttention && state.RequiresSave && !AppIssue.Load(root).Dismissed, "Failed save hid issue");
                }
                using (var fresh = new IssueRecordReview(root)) { state.Refresh(fresh); Check(!state.RequiresSave && PersistedIssueOwner(state, fresh).CanDismiss, "Fresh known unsaved ack not reconciled"); }
            });
            Test("persisted durable acknowledgement with late failure is reread without replay", delegate {
                string root = IssueFixture(ModernIssue()); var state = IssuePersistenceState.Load(root); int writes = 0;
                using (var record = new IssueRecordReview(root, stage => { if (stage == "After publication") { writes++; throw new IOException("Generated uncertain published acknowledgement"); } })) {
                    state.Refresh(record); var owner = PersistedIssueOwner(state, record); Throws<SafetyException>(() => owner.Dismiss());
                    Check(state.Live.NeedsAttention && state.RequiresSave && AppIssue.Load(root).Dismissed, "Uncertain save misreported");
                }
                using (var fresh = new IssueRecordReview(root)) {
                    state.Refresh(fresh); Check(state.Live.Dismissed && !state.RequiresSave && !PersistedIssueOwner(state, fresh).CanDismiss && writes == 1, "Durable ack not reconciled");
                }
            });
            Test("persisted newer live issue before publication aborts acknowledgement", delegate {
                string root = IssueFixture(ModernIssue()); var state = IssuePersistenceState.Load(root); string before = File.ReadAllText(IssuePath(root));
                using (var record = new IssueRecordReview(root, stage => { if (stage == "Before publication") state.Live.Record("Generated later live failure"); })) {
                    state.Refresh(record); Throws<SafetyException>(() => PersistedIssueOwner(state, record).Dismiss());
                    Check(state.Live.Message == "Generated later live failure" && state.Live.NeedsAttention && File.ReadAllText(IssuePath(root)) == before, "Later live issue hidden");
                }
                using (var fresh = new IssueRecordReview(root)) { Check(state.Refresh(fresh).Contains("Save current explanation") && state.RequiresSave && state.Live.Message == "Generated later live failure", "Later issue lost in reread"); }
            });
            Test("persisted newer live issue after publication survives uncertain reread", delegate {
                string root = IssueFixture(ModernIssue()); var state = IssuePersistenceState.Load(root);
                using (var record = new IssueRecordReview(root, stage => { if (stage == "After publication") state.Live.Record("Generated later post-save issue"); })) {
                    state.Refresh(record); Throws<SafetyException>(() => PersistedIssueOwner(state, record).Dismiss());
                    Check(AppIssue.Load(root).Dismissed && state.Live.NeedsAttention, "Later issue hidden after durable ack");
                }
                using (var fresh = new IssueRecordReview(root)) { state.Refresh(fresh); Check(state.RequiresSave && state.Live.Message == "Generated later post-save issue" && state.Live.NeedsAttention, "Reread dropped later issue"); }
            });
            Test("persisted uncertain acknowledgement and clear cannot consume a later identical-payload revision", delegate {
                for (int mode = 0; mode < 2; mode++) {
                    string root = IssueFixture(ModernIssue()); var state = IssuePersistenceState.Load(root); string observed = state.Live.ObservedUtc;
                    using (var record = new IssueRecordReview(root, stage => {
                        if (stage == "After publication") { state.Live.Record(state.Live.Message); state.Live.ObservedUtc = observed; throw new IOException("Generated later same-payload failure"); }
                    })) {
                        state.Refresh(record);
                        if (mode == 0) Throws<SafetyException>(() => PersistedIssueOwner(state, record).Dismiss());
                        else Throws<SafetyException>(() => state.SaveReviewed(record, new AppIssue(), () => {}));
                    }
                    using (var fresh = new IssueRecordReview(root)) { state.Refresh(fresh); Check(state.RequiresSave && state.Live.NeedsAttention && state.Live.ObservedUtc == observed, "Later same-payload issue consumed"); }
                }
            });
            Test("persisted fresh review adopts external issue when local state is confirmed", delegate {
                string root = IssueFixture(ModernIssue()); var state = IssuePersistenceState.Load(root); AppIssue.Save(ModernIssue("Generated new external warning"), root);
                using (var fresh = new IssueRecordReview(root)) { state.Refresh(fresh); Check(state.Live.Message == "Generated new external warning" && !state.RequiresSave, "External issue not reviewed"); PersistedIssueOwner(state, fresh).Dismiss(); }
                Check(AppIssue.Load(root).Dismissed, "External reviewed issue not acknowledged");
            });
            Test("persisted automatic issue record refuses an external change and requires explicit replacement", delegate {
                string root = IssueFixture(ModernIssue()); var state = IssuePersistenceState.Load(root); AppIssue.Save(ModernIssue("Generated external explanation"), root);
                Throws<SafetyException>(() => state.Record("Generated local unsaved explanation", root)); Check(state.Live.NeedsAttention && state.RequiresSave, "Local failure lost");
                using (var fresh = new IssueRecordReview(root)) {
                    string description = state.Refresh(fresh); Check(description.Contains("Generated external explanation") && state.Live.Message == "Generated local unsaved explanation", "Either explanation lost");
                    var owner = PersistedIssueOwner(state, fresh); owner.SaveCurrent(); Throws<SafetyException>(() => owner.SaveCurrent());
                }
                Check(!state.RequiresSave && state.Live.NeedsAttention && AppIssue.Load(root).Message == state.Live.Message && !AppIssue.Load(root).Dismissed, "Explicit save acknowledged or did not persist");
            });
            Test("persisted successful new issue record and exact clear confirm live status", delegate {
                string root = IssueFixture(); var state = IssuePersistenceState.Load(root); state.Record("Generated new bounded explanation", root);
                Check(!state.RequiresSave && state.Live.NeedsAttention && IssueRecordSnapshot.Same(state.Live, AppIssue.Load(root)), "New issue not saved");
                state.Clear(root); Check(!state.Live.HasIssue && !state.RequiresSave && !AppIssue.Load(root).HasIssue, "Exact issue not cleared");
            });
            Test("persisted automatic clear cannot erase an external newer issue", delegate {
                string root = IssueFixture(ModernIssue()); var state = IssuePersistenceState.Load(root); AppIssue.Save(ModernIssue("Generated later saved failure"), root);
                string before = File.ReadAllText(IssuePath(root)); Throws<SafetyException>(() => state.Clear(root));
                Check(state.Live.NeedsAttention && state.RequiresSave && File.ReadAllText(IssuePath(root)) == before, "Newer saved issue erased");
            });
            Test("persisted uncertain clear reconciles actual cleared state on reopening", delegate {
                string root = IssueFixture(ModernIssue()); var state = IssuePersistenceState.Load(root); var cleared = new AppIssue();
                using (var record = new IssueRecordReview(root, stage => { if (stage == "Before issue readback") throw new IOException("Generated clear readback failure"); })) {
                    state.Refresh(record); Throws<IOException>(() => state.SaveReviewed(record, cleared, () => {}));
                    Check(state.Live.NeedsAttention && state.RequiresSave && !AppIssue.Load(root).HasIssue, "Uncertain clear not retained honestly");
                }
                using (var fresh = new IssueRecordReview(root)) { state.Refresh(fresh); Check(!state.Live.HasIssue && !state.RequiresSave, "Cleared state not reconciled"); }
            });
            Test("persisted unavailable record remains untouched until a fresh valid manual review", delegate {
                string root = IssueFixture(); File.WriteAllText(IssuePath(root), "{invalid generated json}"); string before = File.ReadAllText(IssuePath(root));
                Throws<Exception>(() => IssuePersistenceState.Load(root)); var state = IssuePersistenceState.Unavailable("Generated invalid state");
                Throws<SafetyException>(() => state.Record("Generated local warning", root)); Throws<Exception>(() => new IssueRecordReview(root));
                Check(state.Live.NeedsAttention && File.ReadAllText(IssuePath(root)) == before, "Invalid record overwritten");
                // The fixture explicitly supplies a new valid record; production never repairs it silently.
                File.WriteAllText(IssuePath(root), "{\"Message\":\"\",\"ObservedUtc\":\"\",\"Dismissed\":false}");
                using (var fresh = new IssueRecordReview(root)) { state.Refresh(fresh); Check(state.RequiresSave, "Unsaved warning dropped"); PersistedIssueOwner(state, fresh).SaveCurrent(); }
                Check(AppIssue.Load(root).Message == "Generated local warning", "Explicit recovery not saved");
            });
            Test("persisted inaccessible root retains newly recorded explanation in memory", delegate {
                var state = IssuePersistenceState.Load(IssueFixture());
                Throws<IOException>(() => state.Record("Generated root access failure", () => { throw new IOException("Generated inaccessible root"); }));
                Check(state.Live.Message == "Generated root access failure" && state.Live.NeedsAttention && state.RequiresSave, "Root access dropped new issue");
            });
            Test("persisted automatic save refuses a replaced previously empty state folder", delegate {
                string root = IssueFixture(), moved = root + "-moved"; var state = IssuePersistenceState.Load(root);
                Directory.Move(root, moved); Directory.CreateDirectory(root);
                Throws<SafetyException>(() => state.Record("Generated issue after folder replacement", root));
                Check(!File.Exists(IssuePath(root)) && !File.Exists(IssuePath(moved)) && state.RequiresSave && state.Live.NeedsAttention, "Empty foreign folder accepted silently");
                using (var fresh = new IssueRecordReview(root)) { state.Refresh(fresh); PersistedIssueOwner(state, fresh).SaveCurrent(); }
                Check(AppIssue.Load(root).Message == state.Live.Message && !File.Exists(IssuePath(moved)), "Fresh explicit folder review failed");
            });
            Test("persisted issue state rejects a review from another empty root", delegate {
                string root = IssueFixture(), other = IssueFixture(); var state = IssuePersistenceState.Load(root);
                using (var foreign = new IssueRecordReview(other)) {
                    Throws<SafetyException>(() => state.Refresh(foreign));
                    Throws<SafetyException>(() => state.SaveReviewed(foreign, ModernIssue(), () => {}));
                }
                Check(!File.Exists(IssuePath(root)) && !File.Exists(IssuePath(other)), "Foreign-root issue saved");
            });
            Test("persisted protected and hard-linked issue records stay intact", delegate {
                string root = IssueFixture(ModernIssue()), path = IssuePath(root), before = File.ReadAllText(path);
                using (var record = new IssueRecordReview(root)) {
                    File.SetAttributes(path, FileAttributes.ReadOnly);
                    try { Throws<Exception>(() => record.Save(ModernIssue())); Check(File.ReadAllText(path) == before && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0, "Protection changed"); }
                    finally { File.SetAttributes(path, FileAttributes.Normal); }
                }
                string alias = Path.Combine(root, "generated-issue-alias.json");
                Check(CreateIssueFixtureLink(alias, path, IntPtr.Zero), "Generated hard-link fixture failed");
                Throws<SafetyException>(() => new IssueRecordReview(root)); Check(File.ReadAllText(path) == before && File.ReadAllText(alias) == before, "Aliased issue changed");
            });
            Test("persisted issue rejects invalid decoded model and oversized private record", delegate {
                string root = IssueFixture(), path = IssuePath(root);
                foreach (string text in new[] { "null", "{\"Message\":null}", "{\"Message\":\"Generated invalid date\",\"ObservedUtc\":\"invalid\"}", new string(' ', 16385) }) {
                    File.WriteAllText(path, text); Throws<Exception>(() => new IssueRecordReview(root)); Check(File.ReadAllText(path) == text, "Invalid issue rewritten");
                }
            });
        }
    }
}
