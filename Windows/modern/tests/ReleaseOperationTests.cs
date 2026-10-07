using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EasyEject {
    public static partial class Tests {
        sealed class ReleaseViewFixture : IDisposable {
            internal readonly string Root = ModernDefaultsFixture(), App, Settings;
            internal readonly FakeReleaseViewBackend Backend = new FakeReleaseViewBackend();
            internal readonly List<string> Opened = new List<string>(); internal readonly ReleaseView View;
            internal ReleaseViewFixture() { App = Path.Combine(Root, "app"); Settings = Path.Combine(Root, "settings"); Directory.CreateDirectory(App); Directory.CreateDirectory(Settings); View = new ReleaseView(Backend, new InstallationFolders(App, Settings, Opened.Add)); View.SetAvailable(true); }
            public void Dispose() { View.Dispose(); }
        }
        sealed class FakeReleaseViewBackend : IReleaseViewBackend {
            internal bool Available, Held, Closed, ConfiguredValue = true;
            internal int Runs, Cancels, Presentations; internal long ReviewedId = 42;
            internal WindowsReleaseReviewAction Last; internal string Path; internal long Confirmation;
            internal TaskCompletionSource<ReleaseViewResult> Deferred;
            internal bool Fail, ReturnNull;
            public bool Configured { get { return ConfiguredValue; } } public bool Busy { get { return Held; } }
            bool Ready { get { return ConfiguredValue && Available && !Closed && !Held; } }
            public bool CanCheck { get { return Ready; } } public bool CanArchive { get { return Ready; } } public bool CanExtracted { get { return Ready; } } public bool CanBackup { get { return Ready; } } public bool CanRestore { get { return Ready && ReviewedId != 0; } }
            public long BackupId { get { return ReviewedId; } } public string BackupDescription { get { return ReviewedId == 0 ? null : "Generated exact restrictive history " + ReviewedId; } }
            public void SetAvailable(bool value) { Available = value; }
            public void Cancel() { Cancels++; ReviewedId = 0; }
            internal ReleaseViewResult Result(WindowsReleaseReviewAction action, bool accepted = true) { return new ReleaseViewResult(action, accepted, "Generated review result", "Generated immutable description", () => { Presentations++; if (action == WindowsReleaseReviewAction.Backup) ReviewedId = 84; return true; }); }
            public async Task<ReleaseViewResult> Run(WindowsReleaseReviewAction action, string path, long confirmation) {
                Runs++; Last = action; Path = path; Confirmation = confirmation; Held = true;
                try { if (Fail) throw new IOException("Generated backend failure"); return Deferred == null ? ReturnNull ? null : Result(action) : await Deferred.Task; }
                finally { Held = false; }
            }
            public void Dispose() { Closed = true; }
        }
        static void ReleaseOperationTests() {
            Test("modern release production remains unconfigured and never accesses replay or network", delegate {
                Check(WindowsReleaseChecks.ModernProductionChannel == null, "Modern approval invented"); int calls = 0;
                using (var checks = new WindowsUpdateCoordinator(false, null, () => DateTime.UtcNow, TimeSpan.FromDays(1), true, () => { calls++; throw new Exception("Unrequested store"); }))
                using (var backend = new WindowsReleaseViewBackend(checks, null, ApplicationInformation.Version)) {
                    checks.Start(); backend.SetAvailable(true); Check(checks.PulseAsync().GetAwaiter().GetResult() == null && !backend.CanCheck && !backend.CanArchive && !backend.CanBackup && !backend.CanRestore && calls == 0, "Unavailable channel accessed state");
                    Throws<SafetyException>(() => backend.Run(WindowsReleaseReviewAction.Check, null, 0).GetAwaiter().GetResult());
                }
            });
            Test("modern release guide uses complete modern executable and keeps classic contract", delegate {
                using (var f = new ReleaseViewFixture()) {
                    string text = f.View.Guide(ApplicationInformation.Version, WindowsUpdateSafety.ChannelStatus);
                    Check(text.Contains("EasyEject.Modern.exe") && !text.Contains("EasyEject.Windows.exe") && !text.Contains(".exe.config") && text.Contains("Keep all accompanying files together") && text.Contains("not an approved installable release") && text.Contains(f.App) && text.Contains(f.Settings), "Guide targets wrong package");
                    Check(text.Contains("Manual rollback") && text.Contains("choose Quit") && text.Contains("closing the device window only hides") && f.Backend.Runs == 0 && f.Opened.Count == 0, "Guide starts or omits manual lifecycle");
                }
                string root = ModernDefaultsFixture(); using (var folders = new InstallationFolders(root, root, path => {})) Check(ManualInstallation.Instructions("generated", folders).Contains("EasyEject.Windows.exe. Keep the accompanying .exe.config file."), "Classic guide changed");
            });
            Test("modern release folder actions use captured exact roots with no review work", delegate {
                using (var f = new ReleaseViewFixture()) {
                    File.WriteAllText(Path.Combine(f.App, "original.txt"), "Generated retained bytes"); f.View.OpenFolder(false); f.View.OpenFolder(true);
                    Check(f.Opened.Count == 2 && f.Opened[0] == f.App && f.Opened[1] == f.Settings && f.Backend.Runs == 0 && File.ReadAllText(Path.Combine(f.App, "original.txt")) == "Generated retained bytes", "Folder actions retargeted or wrote");
                    f.View.SetAvailable(false); Throws<SafetyException>(() => f.View.OpenFolder(false));
                }
            });
            Test("modern release moved folder refuses explicit open without launching a replacement", delegate {
                using (var f = new ReleaseViewFixture()) {
                    Directory.Move(f.App, f.App + "-moved"); Directory.CreateDirectory(f.App); File.WriteAllText(Path.Combine(f.App, "foreign.txt"), "Generated replacement");
                    Throws<SafetyException>(() => f.View.OpenFolder(false)); Check(f.Opened.Count == 0 && File.ReadAllText(Path.Combine(f.App, "foreign.txt")) == "Generated replacement", "Replacement folder launched or changed");
                }
            });
            Test("modern release unavailable view refuses all actions and closed views cannot reopen", delegate {
                using (var f = new ReleaseViewFixture()) {
                    f.View.SetAvailable(false); foreach (WindowsReleaseReviewAction action in Enum.GetValues(typeof(WindowsReleaseReviewAction))) Check(!f.View.CanAction(action), "Action available outside owned review");
                    Throws<SafetyException>(() => f.View.Check()); Throws<SafetyException>(() => f.View.BeginChoice(WindowsReleaseReviewAction.Archive));
                    f.View.Dispose(); f.View.SetAvailable(true); Check(!f.View.Open && !f.View.CanClose && f.Backend.Closed, "Closed view revived");
                }
            });
            Test("modern release native selection blocks other actions and cancellation starts no worker", delegate {
                using (var f = new ReleaseViewFixture()) {
                    var choice = f.View.BeginChoice(WindowsReleaseReviewAction.Archive); Check(f.View.Nested && f.View.Busy && !f.View.CanClose && !f.Backend.Available, "Picker did not own review");
                    Throws<InvalidOperationException>(() => f.View.Dispose()); Throws<SafetyException>(() => f.View.Check()); Throws<SafetyException>(() => f.View.BeginChoice(WindowsReleaseReviewAction.Backup));
                    Check(f.View.FinishSelection(choice, null).GetAwaiter().GetResult() == null && f.Backend.Runs == 0 && f.View.CanClose, "Cancel started work");
                    Throws<SafetyException>(() => f.View.FinishSelection(choice, f.App));
                }
            });
            Test("modern release selected file is consumed once with exact immutable action", delegate {
                foreach (var action in new[] { WindowsReleaseReviewAction.Archive, WindowsReleaseReviewAction.Extracted, WindowsReleaseReviewAction.Backup }) using (var f = new ReleaseViewFixture()) {
                    var choice = f.View.BeginChoice(action); var result = f.View.FinishSelection(choice, f.App).GetAwaiter().GetResult();
                    Check(f.Backend.Presentations == 0 && result.TryPresent(), "Worker presented before UI delivery");
                    Check(result.Accepted && f.Backend.Runs == 1 && f.Backend.Last == action && f.Backend.Path == f.App && f.Backend.Presentations == 1 && !result.TryPresent(), "Choice changed or replayed");
                    Throws<SafetyException>(() => f.View.FinishSelection(choice, f.Settings));
                }
            });
            Test("modern release lost activity permanently revokes pending native selection", delegate {
                using (var f = new ReleaseViewFixture()) {
                    var choice = f.View.BeginChoice(WindowsReleaseReviewAction.Archive); f.View.SetAvailable(false); f.View.SetAvailable(true);
                    Throws<SafetyException>(() => f.View.FinishSelection(choice, f.App)); Check(f.Backend.Runs == 0 && f.View.CanClose, "Stale picker started work");
                }
            });
            Test("modern release stop retains picker ownership until its actual completion", delegate {
                using (var f = new ReleaseViewFixture()) {
                    var choice = f.View.BeginChoice(WindowsReleaseReviewAction.Backup); f.View.Cancel(); Check(f.View.Busy && !f.View.CanClose, "Stop released live picker");
                    Throws<InvalidOperationException>(() => f.View.Dispose()); Throws<SafetyException>(() => f.View.FinishSelection(choice, f.Settings)); Check(f.Backend.Runs == 0 && f.View.CanClose, "Stopped picker acted");
                }
            });
            Test("modern release choice cannot cross owners or substitute confirmation for selection", delegate {
                using (var a = new ReleaseViewFixture()) using (var b = new ReleaseViewFixture()) {
                    var choice = a.View.BeginChoice(WindowsReleaseReviewAction.Archive);
                    Throws<SafetyException>(() => b.View.FinishSelection(choice, b.App)); Throws<SafetyException>(() => a.View.FinishConfirmation(choice, true));
                    Check(a.View.FinishSelection(choice, null).GetAwaiter().GetResult() == null && a.Backend.Runs + b.Backend.Runs == 0, "Foreign call consumed legitimate picker or acted");
                }
            });
            Test("modern release recovery requires separate exact confirmation and Keep changes nothing", delegate {
                using (var f = new ReleaseViewFixture()) {
                    var choice = f.View.BeginChoice(WindowsReleaseReviewAction.Restore); Check(choice.Confirmation == 42 && choice.Description.Contains("42") && f.View.Nested && f.Backend.Runs == 0, "Restore started on opening");
                    Check(f.View.FinishConfirmation(choice, false).GetAwaiter().GetResult() == null && f.Backend.Runs == 0 && !f.View.CanAction(WindowsReleaseReviewAction.Restore), "Keep restored history");
                    Throws<SafetyException>(() => f.View.FinishConfirmation(choice, true));
                }
            });
            Test("modern release confirmed restoration uses exact captured backup id once", delegate {
                using (var f = new ReleaseViewFixture()) {
                    var choice = f.View.BeginChoice(WindowsReleaseReviewAction.Restore); f.View.FinishConfirmation(choice, true).GetAwaiter().GetResult();
                    Check(f.Backend.Last == WindowsReleaseReviewAction.Restore && f.Backend.Confirmation == 42 && f.Backend.Runs == 1, "Confirmation retargeted");
                    Throws<SafetyException>(() => f.View.FinishConfirmation(choice, true));
                }
            });
            Test("modern release revoked confirmation never restores after activity returns", delegate {
                using (var f = new ReleaseViewFixture()) {
                    var choice = f.View.BeginChoice(WindowsReleaseReviewAction.Restore); f.View.SetAvailable(false); f.View.SetAvailable(true);
                    Throws<SafetyException>(() => f.View.FinishConfirmation(choice, true)); Check(f.Backend.Runs == 0 && f.View.CanClose, "Stale confirmation restored");
                }
            });
            Test("modern release changed reviewed backup cannot retarget an inline confirmation", delegate {
                using (var f = new ReleaseViewFixture()) {
                    var choice = f.View.BeginChoice(WindowsReleaseReviewAction.Restore); f.Backend.ReviewedId = 99;
                    Throws<SafetyException>(() => f.View.FinishConfirmation(choice, true)); Check(f.Backend.Runs == 0 && f.View.CanClose, "Changed backup restored");
                }
            });
            Test("modern release ignored cancellation retains worker slot then rejects late publication", delegate {
                using (var f = new ReleaseViewFixture()) {
                    f.Backend.Deferred = new TaskCompletionSource<ReleaseViewResult>(); var task = f.View.Check();
                    Check(!task.IsCompleted && f.View.Busy && !f.View.CanClose, "Worker ownership absent"); f.View.Cancel();
                    Throws<InvalidOperationException>(() => f.View.Dispose()); Throws<SafetyException>(() => f.View.Check());
                    Check(f.View.Busy && f.Backend.Runs == 1, "Stop claimed ignored worker terminal"); f.Backend.Deferred.SetResult(f.Backend.Result(WindowsReleaseReviewAction.Check));
                    Check(!task.GetAwaiter().GetResult().Accepted && f.Backend.Presentations == 0 && f.View.CanClose, "Late result presented");
                }
            });
            Test("modern release worker result is revoked by even a brief external activity transition", delegate {
                using (var f = new ReleaseViewFixture()) {
                    f.Backend.Deferred = new TaskCompletionSource<ReleaseViewResult>(); var task = f.View.Check(); f.View.SetAvailable(false); f.View.SetAvailable(true);
                    f.Backend.Deferred.SetResult(f.Backend.Result(WindowsReleaseReviewAction.Check)); Check(!task.GetAwaiter().GetResult().Accepted && f.Backend.Presentations == 0, "Old activity result admitted");
                }
            });
            Test("modern release failed and missing workers finish honestly without automatic retry", delegate {
                foreach (bool missing in new[] { false, true }) using (var f = new ReleaseViewFixture()) {
                    f.Backend.Fail = !missing; f.Backend.ReturnNull = missing; var result = f.View.Check().GetAwaiter().GetResult();
                    Check(!result.Accepted && result.Message.Contains("Review current saved history") && f.Backend.Runs == 1 && f.View.CanClose, "Failure retried or claimed rollback");
                    f.Backend.Fail = f.Backend.ReturnNull = false; Check(f.View.Check().GetAwaiter().GetResult().Accepted && f.Backend.Runs == 2, "Explicit retry unavailable");
                }
            });
            Test("modern release queued result is revoked by activity after worker completion", delegate {
                using (var f = new ReleaseViewFixture()) {
                    var result = f.View.Check().GetAwaiter().GetResult(); Check(f.Backend.Presentations == 0, "Worker presented eagerly");
                    f.View.SetAvailable(false); f.View.SetAvailable(true);
                    Check(!result.TryPresent() && f.Backend.Presentations == 0 && f.Backend.Cancels == 1, "Queued result survived activity or retained pending evidence");
                }
            });
            Test("modern release queued result cannot be presented after a newer request", delegate {
                using (var f = new ReleaseViewFixture()) {
                    var old = f.View.Check().GetAwaiter().GetResult(); var fresh = f.View.Check().GetAwaiter().GetResult();
                    Check(!old.TryPresent() && fresh.TryPresent() && f.Backend.Presentations == 1 && !fresh.TryPresent(), "Queue replay or replaced request admitted");
                }
            });
            Test("modern release close revokes a completed queued result without presenting it", delegate {
                using (var f = new ReleaseViewFixture()) {
                    var result = f.View.Check().GetAwaiter().GetResult(); f.View.Dispose();
                    Check(!result.TryPresent() && f.Backend.Presentations == 0 && f.Backend.Closed, "Closed queued result admitted");
                }
            });
            Test("modern release old queued delivery cannot cancel a newer active worker", delegate {
                using (var f = new ReleaseViewFixture()) {
                    var old = f.View.Check().GetAwaiter().GetResult(); f.Backend.Deferred = new TaskCompletionSource<ReleaseViewResult>(); var fresh = f.View.Check();
                    Check(!old.TryPresent() && f.Backend.Cancels == 0 && f.View.Busy, "Old delivery cancelled new worker");
                    f.Backend.Deferred.SetResult(f.Backend.Result(WindowsReleaseReviewAction.Check));
                    Check(fresh.GetAwaiter().GetResult().TryPresent() && f.Backend.Presentations == 1, "Fresh worker presentation lost");
                }
            });
            Test("modern release unconfigured backend leaves folders available with no remote action", delegate {
                using (var f = new ReleaseViewFixture()) {
                    f.Backend.ConfiguredValue = false; foreach (WindowsReleaseReviewAction action in Enum.GetValues(typeof(WindowsReleaseReviewAction))) Check(!f.View.CanAction(action), "Unapproved action offered");
                    f.View.OpenFolder(false); Check(f.Opened.Count == 1 && f.Backend.Runs == 0, "Guide launched review");
                }
            });
        }
    }
}
