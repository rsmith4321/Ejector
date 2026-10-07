using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        const string ModernResumeSid = "S-1-5-21-700-701-702-1001";
        static string ModernInterrupted(ModernImportFixture f, bool trash = false, int count = 2) {
            f.Profile.DeleteOriginals = !trash; f.Profile.RecoverTrash = trash; f.Profile.MacTrashUid = trash ? "501" : null;
            Profiles.Save(f.Profile, f.Config); f.Put("photo.jpg"); f.Put("fileinfo_list.list");
            string folder = trash ? Path.Combine(f.Source, ".Trashes", "501") : f.Source; Directory.CreateDirectory(folder);
            for (int i = 0; i < count; i++) File.WriteAllText(Path.Combine(folder, "clip" + i + ".mp4"), "Owned generated resume original " + i);
            using (var cancel = new CancellationTokenSource()) using (var review = f.Manual()) {
                review.Confirm();
                var engine = new VerifiedImporter(f.Operations, path => UInt64.MaxValue,
                    line => { if (line.StartsWith("Removed verified original", StringComparison.Ordinal)) cancel.Cancel(); },
                    (root, uid, token, validate) => DeviceTrash.Read(root, ModernResumeSid, uid, token, validate));
                var failure = Throws<OriginalRemovalFailure>(() => review.Execute(engine, cancel.Token, message => {}));
                Check(failure.Removed == 1, "Generated interruption did not remove exactly one acknowledged original");
            }
            Check(f.Operations.Available(new[] { 7, 8 }) && Directory.GetFiles(f.Target, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Interruption issued completion or retained a reservation");
            return Directory.GetFiles(f.Target, "removal-intent.json", SearchOption.AllDirectories).Single();
        }
        static ResumeOperationReview ModernResume(ModernImportFixture f, string intent, CancellationToken token = default(CancellationToken), string sid = ModernResumeSid) {
            return ResumeOperationReview.Create(f.Connection, f.Reviewed, new ImportDefaults(), intent, sid, f.Discovery, f.Operations, token, f.Resolve);
        }
        static void ResumeOperationTests() {
            Test("modern resume freezes caller connection and profile before the fresh guard", delegate {
                var f = new ModernImportFixture(); string intent = ModernInterrupted(f); var connection = f.Connection;
                using (var review = ResumeOperationReview.Create(connection, f.Reviewed, new ImportDefaults(), intent, ModernResumeSid, f.Discovery, f.Operations, CancellationToken.None, f.Resolve)) {
                    connection.Volume.Serial = "caller-only-serial"; connection.Volume.Mounts.Clear(); connection.Disk.Serial = "caller-only-disk";
                    f.Profile.Destination = Path.Combine(f.Root, "unrelated"); Profiles.Save(f.Profile, f.Config);
                    review.Confirm(); var result = review.Execute(f.Operations, CancellationToken.None, text => {});
                    Check(result.RemovedOriginals == 1 && File.Exists(result.ReceiptPath) && review.Destination == f.Target && review.Profile.Destination == f.Target, "Caller rewrote frozen selection or guard");
                }
            });
            Test("modern edited remaining original refuses after confirmation despite restored timestamp", delegate {
                var f = new ModernImportFixture(); string intent = ModernInterrupted(f); using (var review = ModernResume(f, intent)) {
                    review.Confirm(); string remaining = Path.Combine(f.Source, "clip1.mp4"); var stamp = File.GetLastWriteTimeUtc(remaining);
                    File.WriteAllText(remaining, "Changed generated remaining original"); File.SetLastWriteTimeUtc(remaining, stamp);
                    Throws<SafetyException>(() => review.Execute(f.Operations, CancellationToken.None, text => {}));
                    Check(File.ReadAllText(remaining) == "Changed generated remaining original" && Directory.GetFiles(f.Target, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Edited original was removed or completed");
                }
            });
            Test("modern resume requires original profile scope and exact journal filename", delegate {
                var f = new ModernImportFixture(); string intent = ModernInterrupted(f);
                string wrong = Path.Combine(Path.GetDirectoryName(intent), "foreign.json"); File.Copy(intent, wrong);
                Throws<SafetyException>(() => ModernResume(f, wrong));
                f.Profile.RelativeSource = "different-source"; Directory.CreateDirectory(Path.Combine(f.Source, "different-source")); Profiles.Save(f.Profile, f.Config);
                Throws<SafetyException>(() => ModernResume(f, intent));
                Check(File.Exists(Path.Combine(f.Source, "clip1.mp4")) && f.Operations.Available(new[] { 7, 8 }), "Wrong scope retained authority or disks");
            });
            Test("modern moved or replaced review folders refuse before any new removal", delegate {
                foreach (bool target in new[] { false, true }) {
                    var f = new ModernImportFixture(); string intent = ModernInterrupted(f); string original = target ? f.Target : f.Source;
                    string moved = Path.Combine(f.Root, "moved-review-folder");
                    using (var review = ModernResume(f, intent)) {
                        // Windows can rename a directory even with attribute handles open.
                        // The captured identity must refuse the moved/replacement tree.
                        Directory.Move(original, moved); Directory.CreateDirectory(original); File.WriteAllText(Path.Combine(original, "foreign.txt"), "Generated replacement tree");
                        review.Confirm(); Throws<SafetyException>(() => review.Execute(f.Operations, CancellationToken.None, text => {}));
                        Check(File.ReadAllText(Path.Combine(original, "foreign.txt")) == "Generated replacement tree" && File.Exists(Path.Combine(target ? f.Source : moved, "clip1.mp4")), "Moved original or replacement was removed");
                    }
                    Check(f.Operations.Available(new[] { 7, 8 }), "Moved-folder refusal retained disks");
                }
            });
            Test("modern interrupted review cancellation unconfirmed and replayed execution preserve remaining originals", delegate {
                var f = new ModernImportFixture(); string intent = ModernInterrupted(f); string remaining = Directory.GetFiles(f.Source, "*.mp4").Single();
                var review = ModernResume(f, intent);
                Check(review.Current && review.Description.Contains("PERMANENTLY REMOVE") && review.Description.Contains("1 ordinary originals"), "Exact remaining scope missing");
                Throws<SafetyException>(() => review.Execute(f.Operations, CancellationToken.None, message => {})); review.Dispose();
                Throws<SafetyException>(() => review.Confirm()); Throws<SafetyException>(() => review.Execute(f.Operations, CancellationToken.None, message => {}));
                Check(File.Exists(remaining) && Directory.GetFiles(f.Target, "verified-import.json", SearchOption.AllDirectories).Length == 0 && f.Operations.Available(new[] { 7, 8 }), "Cancelled review removed data or completed");
            });
            Test("modern resume uses journal choices despite changed profile and saves exact restart history", delegate {
                var f = new ModernImportFixture(); string intent = ModernInterrupted(f);
                f.Profile.DeleteOriginals = false; f.Profile.VideosOnly = false; f.Profile.IncludePreviews = true; f.Profile.CleanLayout = false; f.Profile.AskToEject = true; Profiles.Save(f.Profile, f.Config);
                ImportResult result; ImportProfile profile; VolumeImportGuard guard;
                using (var review = ModernResume(f, intent)) {
                    Check(review.Options.DeleteOriginals && review.Options.VideosOnly && !review.Options.IncludePreviews && review.Options.CleanLayout && review.Options.AskToEject, "Current choices rewrote interrupted scope");
                    var caller = review.Profile; caller.Destination = Path.Combine(f.Root, "wrong"); caller.AskToEject = false;
                    review.Confirm(); result = review.Execute(f.Operations, CancellationToken.None, message => {}); profile = review.Profile; guard = review.Guard;
                    Throws<SafetyException>(() => review.Execute(f.Operations, CancellationToken.None, message => {}));
                }
                var outcome = new ImportedOutcome(result, profile, guard, f.Source, f.Discovery, batch => ImportHistory.Add(batch, f.Config), f.Resolve);
                var saved = ImportHistory.Load(f.Config).Single();
                Check(result.RemovedOriginals == 1 && result.PriorAbsentOriginals == 1 && File.Exists(result.ReceiptPath) && saved.Originals.Count == 2 && outcome.SourceCurrent && outcome.Message.Contains("Already absent before this action: 1") && File.Exists(Path.Combine(f.Source, "photo.jpg")) && File.Exists(Path.Combine(f.Source, "fileinfo_list.list")), "Resume lost exact history/counts or expanded removal");
                Check(Directory.GetFiles(f.Source, "*.mp4").Length == 0 && f.Operations.Available(new[] { 7, 8 }), "Remaining generated originals or reservation incorrect");
            });
            Test("modern resume cancellation before disposition consumes confirmation and releases for manual retry", delegate {
                var f = new ModernImportFixture(); string intent = ModernInterrupted(f); using (var cancel = new CancellationTokenSource()) using (var review = ModernResume(f, intent)) {
                    review.Confirm(); Throws<OperationCanceledException>(() => review.Execute(f.Operations, cancel.Token, text => { if (text.StartsWith("Resuming verified original removal", StringComparison.Ordinal)) cancel.Cancel(); }));
                    Throws<SafetyException>(() => review.Execute(f.Operations, CancellationToken.None, message => {}));
                }
                Check(Directory.GetFiles(f.Source, "*.mp4").Length == 1 && f.Operations.Available(new[] { 7, 8 }), "Cancelled resume cleared remaining source or held disks");
                using (var fresh = ModernResume(f, intent)) { fresh.Confirm(); Check(fresh.Execute(f.Operations, CancellationToken.None, text => {}).RemovedOriginals == 1, "Fresh explicit retry failed"); }
            });
            Test("modern changed saved dependency after confirmation refuses removal", delegate {
                var f = new ModernImportFixture(); string intent = ModernInterrupted(f); using (var review = ModernResume(f, intent)) {
                    review.Confirm(); string copy = Directory.GetFiles(f.Target, "clip1.mp4", SearchOption.AllDirectories).Single(); var stamp = File.GetLastWriteTimeUtc(copy);
                    File.WriteAllText(copy, "Changed generated saved original"); File.SetLastWriteTimeUtc(copy, stamp);
                    Throws<SafetyException>(() => review.Execute(f.Operations, CancellationToken.None, message => {}));
                    Check(File.Exists(Path.Combine(f.Source, "clip1.mp4")), "Changed copy enabled source removal");
                }
                Check(f.Operations.Available(new[] { 7, 8 }), "Refusal retained disks");
            });
            Test("modern changed journal and selected source after review refuse old confirmation", delegate {
                var f = new ModernImportFixture(); string intent = ModernInterrupted(f); using (var review = ModernResume(f, intent)) {
                    review.Confirm(); File.AppendAllText(intent, " "); Throws<SafetyException>(() => review.Execute(f.Operations, CancellationToken.None, message => {}));
                }
                var other = new ModernImportFixture(); string otherIntent = ModernInterrupted(other); using (var review = ModernResume(other, otherIntent)) {
                    review.Confirm(); other.Put("new.mp4"); Throws<SafetyException>(() => review.Execute(other.Operations, CancellationToken.None, message => {}));
                    Check(Directory.GetFiles(other.Source, "*.mp4").Length == 2, "New recording broadened authority");
                }
                Check(File.Exists(Path.Combine(f.Source, "clip1.mp4")), "Changed journal cleared sources");
            });
            Test("modern protected reconnect invalidates modal review and execution", delegate {
                var f = new ModernImportFixture(); string intent = ModernInterrupted(f); using (var review = ModernResume(f, intent)) {
                    f.Discovery.State.Epoch++; f.Discovery.Version++; f.Discovery.State.GroupConnections["modern-usb"] = "reconnected-fixture";
                    Check(!review.Current, "Reconnect kept review current"); Throws<SafetyException>(() => review.Confirm());
                    Check(File.Exists(Path.Combine(f.Source, "clip1.mp4")), "Reconnect removed source");
                }
            });
            Test("modern changed destination refuses review without migrating legacy or altered profile", delegate {
                var f = new ModernImportFixture(); string intent = ModernInterrupted(f); f.Discovery.State.Disks[1].Serial = "replacement-destination";
                Throws<SafetyException>(() => ModernResume(f, intent)); Check(File.Exists(Path.Combine(f.Source, "clip1.mp4")), "Changed destination cleared original");
                var legacy = new ModernImportFixture(true); string legacyIntent = ModernInterrupted(legacy); Profiles.Save(legacy.Profile, legacy.Config); byte[] before = File.ReadAllBytes(Path.Combine(legacy.Config, Profiles.FileName(legacy.Profile.VolumeId)));
                using (var review = ModernResume(legacy, legacyIntent)) { review.Confirm(); review.Execute(legacy.Operations, CancellationToken.None, text => {}); }
                Check(before.SequenceEqual(File.ReadAllBytes(Path.Combine(legacy.Config, Profiles.FileName(legacy.Profile.VolumeId)))) && legacy.Reviewed.Value.SavedDestination == null, "Resume silently migrated legacy profile");
            });
            Test("modern resume folders remain held through confirmation and worker callbacks", delegate {
                var f = new ModernImportFixture(); string intent = ModernInterrupted(f); using (var review = ModernResume(f, intent)) {
                    Check(review.Current, "Held review unexpectedly expired");
                    review.Confirm(); int checks = 0;
                    var result = review.Execute(f.Operations, CancellationToken.None, text => {
                        if (!text.StartsWith("Resuming verified original removal", StringComparison.Ordinal)) return;
                        Throws<InvalidOperationException>(() => review.Dispose()); Throws<IOException>(() => File.WriteAllText(Path.Combine(f.Source, "clip1.mp4"), "Forbidden generated replacement")); checks++;
                    }); Check(checks == 1 && File.Exists(result.ReceiptPath), "Worker lost folder ownership");
                }
                string moved = Path.Combine(f.Root, "released-source"); Directory.Move(f.Source, moved); Check(Directory.Exists(moved), "Terminal review retained folders");
            });
            Test("modern pre-cancelled resume touches no discovery and confirmed cancellation cannot replay", delegate {
                var f = new ModernImportFixture(); string intent = ModernInterrupted(f); using (var cancel = new CancellationTokenSource()) {
                    cancel.Cancel(); int calls = f.Discovery.Calls; Throws<OperationCanceledException>(() => ModernResume(f, intent, cancel.Token)); Check(f.Discovery.Calls == calls, "Precancel accessed device callbacks");
                    using (var review = ModernResume(f, intent)) { review.Confirm(); Throws<OperationCanceledException>(() => review.Execute(f.Operations, cancel.Token, text => {})); Throws<SafetyException>(() => review.Execute(f.Operations, CancellationToken.None, text => {})); }
                }
                Check(File.Exists(Path.Combine(f.Source, "clip1.mp4")) && f.Operations.Available(new[] { 7, 8 }), "Precancel removed source or held disks");
            });
            Test("modern completed resume survives late cancellation and presentation failure", delegate {
                var f = new ModernImportFixture(); string intent = ModernInterrupted(f); ImportResult result;
                using (var cancel = new CancellationTokenSource()) using (var review = ModernResume(f, intent)) {
                    review.Confirm(); result = review.Execute(f.Operations, cancel.Token, text => { if (text == "Resume complete. Verified receipt saved.") { cancel.Cancel(); throw new IOException("Generated display failure after durable receipt"); } });
                    Check(cancel.IsCancellationRequested && File.Exists(result.ReceiptPath) && result.RemovedOriginals == 1 && result.PriorAbsentOriginals == 1, "Late event retracted durable completion");
                }
                Throws<SafetyException>(() => ModernResume(f, intent)); Check(f.Operations.Available(new[] { 7, 8 }), "Completed journal replay or held disks");
            });
            Test("modern resume partial failure reports only new removal and fresh review counts previous absences", delegate {
                var f = new ModernImportFixture(); string intent = ModernInterrupted(f, count: 3);
                using (var cancel = new CancellationTokenSource()) using (var review = ModernResume(f, intent)) {
                    review.Confirm(); int seen = 0;
                    var failure = Throws<RemovalResumeFailure>(() => review.Execute(f.Operations, cancel.Token, text => { if (text.StartsWith("Resuming verified original removal", StringComparison.Ordinal) && ++seen == 2) cancel.Cancel(); }));
                    Check(failure.RemovedOriginals == 1 && Directory.GetFiles(f.Source, "*.mp4").Length == 1 && Directory.GetFiles(f.Target, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Partial failure misreported source/removal/completion");
                }
                using (var fresh = ModernResume(f, intent)) { fresh.Confirm(); var result = fresh.Execute(f.Operations, CancellationToken.None, text => {}); Check(result.PriorAbsentOriginals == 2 && result.RemovedOriginals == 1, "Fresh retry confused prior/new removal"); }
            });
            Test("modern journal-bound device Trash resume preserves owners and ordinary originals", delegate {
                var f = new ModernImportFixture(); string intent = ModernInterrupted(f, trash: true); f.Put("ordinary.mp4");
                // Newly selected ordinary data requires a fresh import, not expansion of this journal.
                Throws<SafetyException>(() => ModernResume(f, intent)); File.Delete(Path.Combine(f.Source, "ordinary.mp4"));
                Throws<SafetyException>(() => ModernResume(f, intent, sid: "S-1-5-21-800-801-802-1002"));
                string foreign = Path.Combine(f.Source, ".Trashes", "502"); Directory.CreateDirectory(foreign); File.WriteAllText(Path.Combine(foreign, "foreign.mp4"), "Generated unrelated owner's original");
                using (var review = ModernResume(f, intent)) {
                    Check(review.Options.RecoverTrash && !review.Options.DeleteOriginals && review.Options.MacTrashUid == "501", "Journal owner/options changed");
                    review.Confirm(); var result = review.Execute(f.Operations, CancellationToken.None, text => {});
                    Check(result.ClearedTrashFiles == 1 && result.PriorAbsentTrashFiles == 1 && File.Exists(Path.Combine(foreign, "foreign.mp4")) && File.Exists(Path.Combine(f.Source, "photo.jpg")), "Trash resume exceeded frozen owner/scope");
                }
            });
        }
    }
}
