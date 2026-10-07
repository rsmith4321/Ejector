using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        sealed class ReplayRecoveryFixture : IDisposable {
            internal readonly ReplayFixture Files = new ReplayFixture();
            internal readonly WindowsChannelReplayStore Store;
            internal readonly WindowsUpdateCoordinator Checks;
            internal readonly WindowsReleaseReviewSession Review;
            internal readonly string Backup; internal int Providers;
            internal ReplayRecoveryFixture(Action<string, string> checkpoint = null) {
                Files.Accept(Files.Store(), Files.Offer(issued: Files.Channel.Seconds - 50)); Store = Files.Store(checkpoint); Backup = ReplayBackupPath(Files, Files.Offer("0.2.0.0", Files.Channel.Seconds - 20));
                Checks = new WindowsUpdateCoordinator(true, _ => Task.FromResult(Files.Offer()), () => Files.Channel.Now, TimeSpan.FromDays(1), true, () => { Providers++; return Store; });
                Checks.PublishIdle(true); Checks.SetReviewOpen(true); Checks.Start(); Review = new WindowsReleaseReviewSession(Checks, Files.Channel.Channel); Review.PublishAvailable(true);
            }
            internal WindowsReleaseReviewResult Inspect(bool present = true) { var result = ChannelAwait(() => Review.BackupAsync(Backup, CancellationToken.None)); if (present) Check(Review.TryPresent(result), "Backup result not presented"); return result; }
            internal WindowsReleaseReviewResult Restore(long id) { var result = ChannelAwait(() => Review.RestoreBackupAsync(id, CancellationToken.None)); Check(Review.TryPresent(result), "Restore result not presented"); return result; }
            public void Dispose() { Review.Dispose(); Checks.Dispose(); Files.Dispose(); }
        }
        static void CheckBackupReleased(string path) { using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) Check(file.Length > 0, "Backup handle retained"); }
        static void WindowsReplayRecoveryTests() {
            Test("recovery session requires presented exact review and a separate one-use restore", () => {
                using (var f = new ReplayRecoveryFixture()) { string hash = ReplayHash(f.Files); Check(f.Providers == 0 && f.Review.CanBackup && !f.Review.CanRestoreBackup, "Provider eagerly accessed or restore initially offered");
                    var result = f.Inspect(false); Check(result.Accepted && !f.Review.CanRestoreBackup && ReplayHash(f.Files) == hash, "Read performed restoration"); Throws<SafetyException>(() => f.Review.RestoreBackupAsync(result.Id, CancellationToken.None));
                    Check(f.Review.TryPresent(result) && !f.Review.TryPresent(result) && f.Review.CanRestoreBackup, "Presentation not one use"); Throws<SafetyException>(() => f.Review.RestoreBackupAsync(result.Id + 1, CancellationToken.None));
                    Check(f.Restore(result.Id).Accepted && ReplaySavedFloor(f.Files).Version == "0.2.0.0" && !f.Review.CanRestoreBackup && !f.Review.CanArchive && !f.Review.CanExtracted, "Restore omitted floor or retained release authority");
                    Throws<SafetyException>(() => f.Review.RestoreBackupAsync(result.Id, CancellationToken.None)); CheckBackupReleased(f.Backup); }
            });
            Test("recovery idle cancellation releases backup without changing target", () => {
                using (var f = new ReplayRecoveryFixture()) { string hash = ReplayHash(f.Files); f.Inspect(); Throws<IOException>(() => File.Delete(f.Backup)); f.Review.Cancel(); Check(!f.Review.CanRestoreBackup && ReplayHash(f.Files) == hash, "Cancelled review changed state or kept confirmation"); CheckBackupReleased(f.Backup); }
            });
            Test("closed recovery session releases pending evidence and refuses all further actions", () => {
                using (var f = new ReplayRecoveryFixture()) { var result = f.Inspect(); f.Review.Dispose(); Throws<ObjectDisposedException>(() => f.Review.RestoreBackupAsync(result.Id, CancellationToken.None)); Check(!f.Review.CanBackup && !f.Review.CanRestoreBackup, "Closed recovery offered action"); CheckBackupReleased(f.Backup); }
            });
            Test("late recovery result after activity loss is refused and releases held backup", () => {
                using (var f = new ReplayRecoveryFixture()) { var result = f.Inspect(false); f.Review.PublishAvailable(false); f.Review.PublishAvailable(true); Check(!f.Review.TryPresent(result) && !f.Review.CanRestoreBackup, "Old activity result admitted"); CheckBackupReleased(f.Backup); }
            });
            Test("new release check discards backup review and old confirmation without queuing restore", () => {
                using (var f = new ReplayRecoveryFixture()) { var backup = f.Inspect(); string hash = ReplayHash(f.Files); var check = ChannelAwait(() => f.Review.CheckAsync(CancellationToken.None)); Check(f.Review.TryPresent(check) && check.Accepted && !f.Review.CanRestoreBackup && ReplayHash(f.Files) == hash, "Metadata check restored backup or kept stale choice"); Throws<SafetyException>(() => f.Review.RestoreBackupAsync(backup.Id, CancellationToken.None)); CheckBackupReleased(f.Backup); }
            });
            Test("failed backup refresh clears previous release and backup authority", () => {
                using (var f = new ReplayRecoveryFixture()) { var check = ChannelAwait(() => f.Review.CheckAsync(CancellationToken.None)); Check(f.Review.TryPresent(check) && f.Review.CanArchive, "Initial check failed"); f.Inspect(); f.Review.Cancel(); File.WriteAllText(f.Backup, "corrupt generated record"); var refused = f.Inspect(); Check(!refused.Accepted && !f.Review.CanRestoreBackup && !f.Review.CanArchive && !f.Review.CanExtracted, "Failed refresh retained authority"); }
            });
            Test("backup worker cancellation retains honest slot until native read returns", () => {
                using (var f = new ReplayRecoveryFixture()) using (var entered = new ManualResetEventSlim()) using (var release = new ManualResetEventSlim()) {
                    f.Files.Protection.BeforeUnprotect = () => { entered.Set(); Check(release.Wait(4000), "Recovery read release timeout"); }; string hash = ReplayHash(f.Files); var task = f.Review.BackupAsync(f.Backup, CancellationToken.None);
                    try { Check(entered.Wait(4000), "Recovery read not reached"); f.Review.Cancel(); Check(f.Review.Busy && !f.Review.CanBackup, "Cancelled token-ignoring read released slot early"); Throws<SafetyException>(() => f.Review.BackupAsync(f.Backup, CancellationToken.None)); } finally { release.Set(); }
                    var result = ChannelAwait(() => task); Check(result.Cancelled && !f.Review.Busy && !f.Review.CanRestoreBackup && ReplayHash(f.Files) == hash, "Cancelled read changed state or retained slot"); CheckBackupReleased(f.Backup); }
            });
            Test("closed backup worker drops late evidence after native read returns", () => {
                using (var f = new ReplayRecoveryFixture()) using (var entered = new ManualResetEventSlim()) using (var release = new ManualResetEventSlim()) {
                    f.Files.Protection.BeforeUnprotect = () => { entered.Set(); Check(release.Wait(4000), "Closed read release timeout"); }; var task = f.Review.BackupAsync(f.Backup, CancellationToken.None);
                    try { Check(entered.Wait(4000), "Read not reached"); f.Review.Dispose(); Check(f.Review.Busy, "Closing claimed worker terminal"); } finally { release.Set(); }
                    var result = ChannelAwait(() => task); Check(result.Cancelled && !f.Review.TryPresent(result) && !f.Review.Busy, "Closed late evidence presented"); CheckBackupReleased(f.Backup); }
            });
            Test("restore cancellation keeps its owned file and slot until publication guard returns", () => {
                using (var entered = new ManualResetEventSlim()) using (var release = new ManualResetEventSlim()) using (var f = new ReplayRecoveryFixture((stage, _) => { if (stage == "Before publication") { entered.Set(); Check(release.Wait(4000), "Restore release timeout"); } })) {
                    var result = f.Inspect(); string hash = ReplayHash(f.Files); var task = f.Review.RestoreBackupAsync(result.Id, CancellationToken.None);
                    try { Check(entered.Wait(4000), "Restore not reached"); f.Review.Cancel(); Check(f.Review.Busy && !f.Review.CanBackup, "Restore slot released early"); } finally { release.Set(); }
                    var cancelled = ChannelAwait(() => task); Check(cancelled.Cancelled && ReplayHash(f.Files) == hash && !f.Review.Busy && Directory.GetFiles(f.Files.Root).Length == 1, "Cancelled restore wrote/leaked work"); CheckBackupReleased(f.Backup); }
            });
            Test("postpublication recovery scope loss retains advanced history and reports possible publication", () => {
                WindowsReleaseReviewSession owner = null;
                using (var f = new ReplayRecoveryFixture((stage, _) => { if (stage == "After publication" && owner != null) { owner.PublishAvailable(false); owner.PublishAvailable(true); } })) {
                    owner = f.Review; var backup = f.Inspect(); var result = ChannelAwait(() => f.Review.RestoreBackupAsync(backup.Id, CancellationToken.None));
                    Check(result.Cancelled && result.Message.Contains("publication already completed") && ReplaySavedFloor(f.Files).Version == "0.2.0.0" && !f.Review.CanRestoreBackup, "Stopped restore reported no durable effect or retained permission"); CheckBackupReleased(f.Backup); }
            });
            Test("configured checks without persistent store expose no recovery action", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var checks = f.Create()) using (var review = new WindowsReleaseReviewSession(checks, f.Channel.Channel)) {
                    checks.PublishIdle(true); review.PublishAvailable(true); Check(!review.RecoveryConfigured && !review.CanBackup && !review.CanRestoreBackup, "Missing shared store exposed recovery"); Throws<SafetyException>(() => review.BackupAsync(@"C:\unrequested\windows-channel-replay.json", CancellationToken.None)); }
            });
            Test("coordinator close disables pending recovery before any further provider access", () => {
                using (var f = new ReplayRecoveryFixture()) { var result = f.Inspect(); int providers = f.Providers; f.Checks.Dispose(); Check(!f.Review.CanBackup && !f.Review.CanRestoreBackup, "Closed coordinator offered restore"); Throws<SafetyException>(() => f.Review.RestoreBackupAsync(result.Id, CancellationToken.None)); Check(f.Providers == providers, "Closed recovery touched provider"); f.Review.Cancel(); CheckBackupReleased(f.Backup); }
            });
            Test("unconfigured production recovery performs no root access or backup work", () => {
                using (var f = new WindowsChannelFixture()) { int roots = 0; using (var checks = WindowsReleaseChecks.Create(null, () => { roots++; throw new Exception("Unrequested root"); }, ChannelTransport(f), new WindowsManifestTrust(f.Archive.Package.CertificateTrust), () => f.Now, true)) using (var review = new WindowsReleaseReviewSession(checks, null)) {
                        checks.PublishIdle(true); review.PublishAvailable(true); Check(!review.CanBackup && !review.CanRestoreBackup && !checks.ReplayRecoveryConfigured, "Unconfigured recovery enabled"); Throws<SafetyException>(() => review.BackupAsync(@"C:\unrequested\windows-channel-replay.json", CancellationToken.None)); Check(roots == 0, "Unconfigured recovery accessed state"); } }
            });
            Test("factory recovery shares retained live floor with normal native-protected checks", () => {
                using (var f = new ReplayFixture()) { var channel = f.Channel; int roots = 0; var transport = ChannelTransport(channel, channel.Text(issued: channel.Seconds - 50));
                    using (var checks = WindowsReleaseChecks.Create(channel.Channel, () => { roots++; return f.Root; }, transport, new WindowsManifestTrust(channel.Archive.Package.CertificateTrust), () => channel.Now, false)) {
                        checks.PublishIdle(true); Check(UpdateManual(checks).Outcome == WindowsUpdateCheckOutcome.Accepted, "Initial native check failed"); string old = ReplayBackupPath(f);
                        transport.Fetch = ChannelTransport(channel, channel.Text("0.2.0.0", channel.Seconds - 20)).Fetch; Check(UpdateManual(checks).Outcome == WindowsUpdateCheckOutcome.Accepted, "Advanced native check failed"); File.Delete(f.PathValue); checks.SetReviewOpen(true);
                        using (var review = new WindowsReleaseReviewSession(checks, channel.Channel)) { review.PublishAvailable(true); var result = ChannelAwait(() => review.BackupAsync(old, CancellationToken.None)); Check(review.TryPresent(result) && !result.Accepted && result.Message.Contains("rolled back") && !File.Exists(f.PathValue), "Recovery used fresh store and forgot observed high water"); }
                        Check(roots == 1 && Object.ReferenceEquals(checks.ReplayStore(), checks.ReplayStore()), "Factory created independent stores");
                    } }
            });
            Test("hidden recovery cancel default preserves exact snapshot and releases manual review", () => {
                using (var f = new ReplayRecoveryFixture()) using (var folders = new InstallationFolders(f.Files.Root, f.Files.Root, _ => { throw new Exception("Unrequested open"); })) {
                    WindowsReleaseReviewDialog dialog = null; using (dialog = new WindowsReleaseReviewDialog(ApplicationInformation.Version, f.Review, folders, () => { if (dialog != null) dialog.PublishAvailable(true); })) {
                        string hash = ReplayHash(f.Files); PumpShareSetup(dialog.RunAction(() => f.Review.BackupAsync(f.Backup, CancellationToken.None)));
                        bool ownedText = false; PumpShareSetup(dialog.ConfirmBackup(text => { ownedText = dialog.NestedReview && !f.Review.CanCheck && text.Contains("0.2.0.0") && text.Contains("UTC") && !text.Contains("epoch seconds"); return false; }));
                        Check(ownedText && ReplayHash(f.Files) == hash && !dialog.RestoreBackup.Enabled && dialog.BackupStatus.Text.Contains("changed no update history"), "Cancel wrote settings, left restore live or omitted exact owned review"); CheckBackupReleased(f.Backup); }
                }
            });
            Test("hidden recovery explicit confirmation restores only history and requires fresh metadata", () => {
                using (var f = new ReplayRecoveryFixture()) using (var folders = new InstallationFolders(f.Files.Root, f.Files.Root, _ => {})) {
                    WindowsReleaseReviewDialog dialog = null; using (dialog = new WindowsReleaseReviewDialog(ApplicationInformation.Version, f.Review, folders, () => { if (dialog != null) dialog.PublishAvailable(true); })) {
                        PumpShareSetup(dialog.RunAction(() => f.Review.BackupAsync(f.Backup, CancellationToken.None))); PumpShareSetup(dialog.ConfirmBackup(_ => true));
                        Check(ReplaySavedFloor(f.Files).Version == "0.2.0.0" && dialog.BackupStatus.Text.Contains("Update history restored") && !dialog.RestoreBackup.Enabled && !dialog.VerifyArchive.Enabled && dialog.ReleaseContent.Text.Contains("again"), "Confirmed recovery omitted effect or kept release authority"); CheckBackupReleased(f.Backup); }
                }
            });
            Test("hidden confirmation refuses current-file replacement while dialog is open", () => {
                using (var f = new ReplayRecoveryFixture()) using (var folders = new InstallationFolders(f.Files.Root, f.Files.Root, _ => {})) {
                    WindowsReleaseReviewDialog dialog = null; using (dialog = new WindowsReleaseReviewDialog(ApplicationInformation.Version, f.Review, folders, () => { if (dialog != null) dialog.PublishAvailable(true); })) {
                        PumpShareSetup(dialog.RunAction(() => f.Review.BackupAsync(f.Backup, CancellationToken.None))); byte[] old = File.ReadAllBytes(f.Files.PathValue);
                        PumpShareSetup(dialog.ConfirmBackup(_ => { File.Move(f.Files.PathValue, f.Files.PathValue + ".moved"); File.WriteAllBytes(f.Files.PathValue, old); return true; }));
                        Check(dialog.BackupStatus.Text.Contains("changed after review") && ReplaySavedFloor(f.Files).Version == "0.1.0.0" && !dialog.RestoreBackup.Enabled, "Confirmation accepted replacement target"); }
                }
            });
            Test("hidden confirmation uses immutable reviewed evidence rather than replaced display text", () => {
                using (var f = new ReplayRecoveryFixture()) using (var folders = new InstallationFolders(f.Files.Root, f.Files.Root, _ => {})) {
                    WindowsReleaseReviewDialog dialog = null; using (dialog = new WindowsReleaseReviewDialog(ApplicationInformation.Version, f.Review, folders, () => { if (dialog != null) dialog.PublishAvailable(true); })) {
                        PumpShareSetup(dialog.RunAction(() => f.Review.BackupAsync(f.Backup, CancellationToken.None))); dialog.BackupContent.Text = "replaced display text"; bool exact = false;
                        PumpShareSetup(dialog.ConfirmBackup(text => { exact = text.Contains(f.Backup) && text.Contains(f.Files.PathValue) && text.Contains("Current update history: 0.1.0.0") && !text.Contains("replaced display text"); return false; })); Check(exact && !dialog.RestoreBackup.Enabled, "Confirmation trusted mutable display or retained cancelled review"); }
                }
            });
            Test("closed hidden recovery view rejects late worker output and releases backup", () => {
                using (var f = new ReplayRecoveryFixture()) using (var folders = new InstallationFolders(f.Files.Root, f.Files.Root, _ => {})) using (var entered = new ManualResetEventSlim()) using (var release = new ManualResetEventSlim()) {
                    var dialog = new WindowsReleaseReviewDialog(ApplicationInformation.Version, f.Review, folders, () => {}); f.Files.Protection.BeforeUnprotect = () => { entered.Set(); Check(release.Wait(4000), "Closed UI read timeout"); };
                    var task = dialog.RunAction(() => f.Review.BackupAsync(f.Backup, CancellationToken.None)); try { Check(entered.Wait(4000), "UI read not reached"); dialog.Dispose(); Check(f.Review.Busy, "Disposed UI claimed worker completion"); } finally { release.Set(); }
                    PumpShareSetup(task); Check(dialog.IsDisposed && !f.Review.Busy && !f.Review.CanRestoreBackup, "Closed UI retained authority or worker slot"); CheckBackupReleased(f.Backup); }
            });
            Test("hidden recovery and confirmation keep bounded read-only content and Cancel defaults", () => {
                using (var f = new ReplayRecoveryFixture()) using (var folders = new InstallationFolders(f.Files.Root, f.Files.Root, _ => {})) using (var dialog = new WindowsReleaseReviewDialog(ApplicationInformation.Version, f.Review, folders, () => {})) using (var confirmation = new WindowsReplayBackupConfirmation(new string('x', 8000))) {
                    dialog.Size = dialog.MinimumSize; dialog.CreateControl(); dialog.PerformLayout(); confirmation.Size = confirmation.MinimumSize; confirmation.CreateControl(); confirmation.PerformLayout();
                    Check(dialog.AcceptButton == dialog.CloseReview && dialog.CancelButton == dialog.CloseReview && dialog.BackupContent.ReadOnly && dialog.BackupStatus.Height == 94 && dialog.BackupStatus.ScrollBars == ScrollBars.Vertical && dialog.ReviewBackup.Enabled && !dialog.RestoreBackup.Enabled, "Recovery default/content unsafe");
                    Check(confirmation.AcceptButton == confirmation.Keep && confirmation.CancelButton == confirmation.Keep && confirmation.Content.ReadOnly && confirmation.Content.ScrollBars == ScrollBars.Vertical && confirmation.Keep.Bottom <= confirmation.Keep.Parent.ClientSize.Height && confirmation.Restore.Right <= confirmation.Restore.Parent.ClientSize.Width, "Confirmation defaults or footer clipped"); }
            });
        }
        static void WindowsReplayRecoveryRender(string output) {
            string allowed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectWindowsPreview"); output = SafePaths.Normalize(output);
            Check(SafePaths.Within(output, allowed) && !output.Equals(allowed, StringComparison.OrdinalIgnoreCase), "Recovery render output escaped preview folder"); Directory.CreateDirectory(output);
            using (var f = new ReplayRecoveryFixture()) using (var folders = new InstallationFolders(f.Files.Root, f.Files.Root, _ => {})) using (var dialog = new WindowsReleaseReviewDialog(ApplicationInformation.Version, f.Review, folders, () => {})) {
                dialog.Size = dialog.MinimumSize; var page = (TabPage)dialog.BackupContent.Parent; ((TabControl)page.Parent).SelectedTab = page;
                PumpShareSetup(dialog.RunAction(() => f.Review.BackupAsync(f.Backup, CancellationToken.None))); Render(dialog, Path.Combine(output, "update-history-review.png"));
                using (var confirm = new WindowsReplayBackupConfirmation(dialog.BackupContent.Text)) {
                    confirm.Size = confirm.MinimumSize; Render(confirm, Path.Combine(output, "update-history-confirm.png"));
                    string complete = confirm.Content.Text; confirm.Content.Text = complete.Substring(complete.IndexOf("Restore to:", StringComparison.Ordinal));
                    Render(confirm, Path.Combine(output, "update-history-confirm-end.png")); confirm.Content.Text = complete;
                }
            }
            Console.WriteLine("Hidden recovery renders use generated configuration only; foreground picker/key delivery remains pending.");
        }
    }
}
