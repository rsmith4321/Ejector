using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyEject {
    internal enum WindowsReleaseReviewAction { Check, Archive, Extracted, Backup, Restore }
    internal sealed class WindowsReleaseReviewResult {
        internal readonly WindowsReleaseReviewSession Owner; internal readonly long Id, Epoch;
        internal readonly WindowsReleaseReviewAction Action; internal readonly bool Accepted, Cancelled;
        internal readonly string Message, ArchivePath; internal readonly WindowsChannelOffer Offer; internal int Presented;
        internal WindowsReleaseReviewResult(WindowsReleaseReviewSession owner, WindowsUpdateCheckScope scope, WindowsReleaseReviewAction action, bool accepted, bool cancelled, string message, WindowsChannelOffer offer, string archive) {
            Owner = owner; Id = scope.Id; Epoch = scope.ActivityEpoch; Action = action; Accepted = accepted; Cancelled = cancelled; Message = message; Offer = offer; ArchivePath = archive;
        }
    }
    // A review owns read-only operations, not installation permission. No worker reads
    // Form state. A previous file result is always freshly reopened/reverified for the
    // next explicit action; no handles or trust verdict survive as launch authority.
    internal sealed class WindowsReleaseReviewSession : IDisposable {
        readonly object sync = new object(); readonly WindowsUpdateCoordinator checks; readonly ApprovedWindowsChannel channel;
        readonly Func<string, CancellationToken, WindowsArchiveReview> archive;
        readonly Func<string, CancellationToken, WindowsPackageReview> package;
        bool available, disposed; long id, epoch; WindowsUpdateCheckScope active;
        WindowsChannelOffer offer; string archivePath;
        WindowsChannelReplayStore.BackupReview backup; long backupId; bool backupPresented;
        internal WindowsReleaseReviewSession(WindowsUpdateCoordinator checks, ApprovedWindowsChannel channel,
            Func<string, CancellationToken, WindowsArchiveReview> archive = null, Func<string, CancellationToken, WindowsPackageReview> package = null) {
            if (checks == null || checks.Configured != (channel != null)) throw new ArgumentException("Release review requires the same configured channel as its check coordinator.");
            this.checks = checks; this.channel = channel;
            this.archive = archive ?? ((path, cancel) => new WindowsArchiveReview(path, channel.Publisher, new WindowsManifestTrust(), cancel));
            this.package = package ?? ((path, cancel) => new WindowsPackageReview(path, channel.Publisher, new WindowsAuthenticode(), new WindowsManifestTrust(), cancel));
        }
        internal bool Configured { get { return channel != null; } }
        internal bool RecoveryConfigured { get { return Configured && checks.ReplayRecoveryConfigured; } }
        internal bool Busy { get { lock (sync) return active != null; } }
        internal bool CanCheck { get { lock (sync) return !disposed && available && Configured && active == null && !checks.Checking; } }
        internal bool CanArchive { get { lock (sync) return !disposed && available && active == null && offer != null; } }
        internal bool CanExtracted { get { lock (sync) return !disposed && available && active == null && offer != null && archivePath != null; } }
        internal bool CanBackup { get { lock (sync) return !disposed && available && Configured && checks.ReplayRecoveryConfigured && active == null && !checks.Checking; } }
        internal bool CanRestoreBackup { get { lock (sync) return !disposed && available && checks.ReplayRecoveryConfigured && active == null && !checks.Checking && backup != null && backupPresented; } }
        internal long BackupId { get { lock (sync) return backup != null && backupPresented ? backupId : 0; } }
        internal string BackupDescription { get { lock (sync) return backup != null && backupPresented ? backup.Description : null; } }
        internal void PublishAvailable(bool value) {
            WindowsUpdateCheckScope revoked = null;
            lock (sync) { if (disposed || available == value) return; available = value; epoch++; if (!value && active != null) { revoked = active; revoked.Invalidate(); } }
            if (revoked != null) revoked.Cancel();
        }
        internal void Cancel() { WindowsUpdateCheckScope revoked; WindowsChannelReplayStore.BackupReview discarded; lock (sync) { revoked = active; if (revoked != null) revoked.Invalidate(); discarded = backup; backup = null; backupPresented = false; } if (revoked != null) revoked.Cancel(); if (discarded != null) discarded.Dispose(); }
        WindowsUpdateCheckScope Capture(WindowsReleaseReviewAction action, CancellationToken cancel, long confirmation, out WindowsChannelOffer captured, out string capturedArchive, out WindowsChannelReplayStore.BackupReview capturedBackup, out WindowsChannelReplayStore.BackupReview discarded) {
            lock (sync) {
                if (disposed) throw new ObjectDisposedException("WindowsReleaseReviewSession"); cancel.ThrowIfCancellationRequested();
                if (!Configured) throw new SafetyException(WindowsUpdateSafety.ChannelStatus);
                if (!available || active != null) throw new SafetyException("Finish the active review, scan or other dialog before another release action.");
                bool recovery = action == WindowsReleaseReviewAction.Backup || action == WindowsReleaseReviewAction.Restore;
                if (recovery && (!checks.ReplayRecoveryConfigured || checks.Checking)) throw new SafetyException("Finish release checks before reviewing a backup from this configured settings store.");
                if (!recovery && action != WindowsReleaseReviewAction.Check && offer == null || action == WindowsReleaseReviewAction.Extracted && archivePath == null) throw new SafetyException("Check signed release metadata and verify its selected ZIP before reviewing an extracted folder.");
                if (action == WindowsReleaseReviewAction.Restore && (backup == null || !backupPresented || confirmation == 0 || confirmation != backupId)) throw new SafetyException("Review and explicitly confirm this exact update-history backup again.");
                capturedBackup = action == WindowsReleaseReviewAction.Restore ? backup : null; discarded = action == WindowsReleaseReviewAction.Restore ? null : backup;
                backup = null; backupPresented = false;
                captured = offer; capturedArchive = archivePath;
                if (action == WindowsReleaseReviewAction.Check || recovery) { offer = null; archivePath = null; }
                if (action == WindowsReleaseReviewAction.Archive) archivePath = null;
                active = new WindowsUpdateCheckScope(++id, epoch, 0, WindowsUpdateCheckOrigin.Manual, cancel); return active;
            }
        }
        void Guard(WindowsUpdateCheckScope scope) { scope.Token.ThrowIfCancellationRequested(); lock (sync) { if (disposed || !available || active != scope || epoch != scope.ActivityEpoch || !scope.Allowed) throw new OperationCanceledException(scope.Token); } }
        internal Task<WindowsReleaseReviewResult> CheckAsync(CancellationToken cancel) { return Begin(WindowsReleaseReviewAction.Check, null, cancel); }
        internal Task<WindowsReleaseReviewResult> ArchiveAsync(string path, CancellationToken cancel) { return Begin(WindowsReleaseReviewAction.Archive, path, cancel); }
        internal Task<WindowsReleaseReviewResult> ExtractedAsync(string folder, CancellationToken cancel) { return Begin(WindowsReleaseReviewAction.Extracted, folder, cancel); }
        internal Task<WindowsReleaseReviewResult> BackupAsync(string path, CancellationToken cancel) { return Begin(WindowsReleaseReviewAction.Backup, path, cancel); }
        internal Task<WindowsReleaseReviewResult> RestoreBackupAsync(long confirmation, CancellationToken cancel) { return Begin(WindowsReleaseReviewAction.Restore, null, cancel, confirmation); }
        Task<WindowsReleaseReviewResult> Begin(WindowsReleaseReviewAction action, string path, CancellationToken cancel, long confirmation = 0) {
            WindowsChannelOffer captured; string capturedArchive; WindowsChannelReplayStore.BackupReview capturedBackup, discarded;
            var scope = Capture(action, cancel, confirmation, out captured, out capturedArchive, out capturedBackup, out discarded);
            if (discarded != null) discarded.Dispose();
            return Run(scope, action, path, captured, capturedArchive, capturedBackup);
        }
        bool Allowed(WindowsUpdateCheckScope scope) { lock (sync) return !disposed && available && active == scope && epoch == scope.ActivityEpoch && scope.Allowed; }
        async Task<WindowsReleaseReviewResult> Run(WindowsUpdateCheckScope scope, WindowsReleaseReviewAction action, string path, WindowsChannelOffer captured, string capturedArchive, WindowsChannelReplayStore.BackupReview capturedBackup) {
            string message = null, verifiedArchive = null; Exception error = null; WindowsChannelOffer checkedOffer = captured;
            WindowsChannelReplayStore.BackupReview reviewedBackup = null;
            try {
                Guard(scope);
                if (action == WindowsReleaseReviewAction.Check) {
                    var result = await checks.CheckManualAsync(scope.Token).ConfigureAwait(false); Guard(scope);
                    if (!checks.TryPresent(result)) throw new OperationCanceledException(scope.Token);
                    if (result.Outcome != WindowsUpdateCheckOutcome.Accepted) throw new SafetyException(result.Message);
                    checkedOffer = result.Offer;
                    if (!checkedOffer.IsFor(channel)) throw new SafetyException("The release result belongs to a different approved channel policy.");
                    await Task.Run(() => { Guard(scope); checkedOffer.Revalidate(scope.Token); Guard(scope); }).ConfigureAwait(false);
                    message = "Authenticated release metadata. Select a ZIP you downloaded manually to verify its exact signed contents.";
                } else if (action == WindowsReleaseReviewAction.Backup) {
                    reviewedBackup = await Task.Run(() => { Guard(scope); var store = checks.ReplayStore(); Guard(scope); return store.ReviewBackup(channel, path, scope.Token, () => Allowed(scope)); }).ConfigureAwait(false);
                    message = reviewedBackup.Description; checkedOffer = null;
                } else if (action == WindowsReleaseReviewAction.Restore) {
                    bool changed = await Task.Run(() => { Guard(scope); return capturedBackup.Owner.RestoreBackup(capturedBackup, scope.Token, () => Allowed(scope)); }).ConfigureAwait(false);
                    message = changed ? "Update history restored. The backup, other settings and media are retained. Check release metadata again before reviewing a release." : "Current update history already matches this backup. No rewrite occurred. Release checks still require fresh signed metadata."; checkedOffer = null;
                } else {
                    message = await Task.Run(() => {
                        Guard(scope); checkedOffer.Revalidate(scope.Token); Guard(scope);
                        using (var zip = archive(action == WindowsReleaseReviewAction.Archive ? path : capturedArchive, scope.Token)) {
                            Guard(scope); checkedOffer.VerifyArchive(zip, scope.Token); Guard(scope);
                            if (action == WindowsReleaseReviewAction.Archive) { verifiedArchive = zip.Path; return "Selected ZIP matches the fresh signed release and exact archive hash. Extract it manually into a separate versioned folder, then select that folder for executable and package verification."; }
                            using (var extracted = package(path, scope.Token)) { Guard(scope); checkedOffer.VerifyExtracted(zip, extracted, scope.Token); Guard(scope); return "Selected extracted folder matches the freshly verified ZIP, signed release, trusted executable publisher and all six package files.\r\nFolder: " + extracted.Folder + "\r\n\r\nThis is a read-only result for these files at this time. Finish work and Quit before manually switching app folders. No installation or launch permission is retained."; }
                        }
                    }).ConfigureAwait(false);
                }
                Guard(scope);
            } catch (Exception ex) { error = ex; }
            WindowsReleaseReviewResult completed;
            lock (sync) {
                bool current = !disposed && available && active == scope && epoch == scope.ActivityEpoch && scope.Allowed;
                bool cancelled = !current || error is OperationCanceledException; bool accepted = !cancelled && error == null;
                completed = new WindowsReleaseReviewResult(this, scope, action, accepted, cancelled, cancelled ? (action == WindowsReleaseReviewAction.Restore ? "Restore stopped. If publication already completed, its restrictive history is retained. Review current history before another manual attempt." : "Review cancelled. Selected files are retained; retry is manual.") : error == null ? message : "Review refused: " + error.Message, accepted ? checkedOffer : null, accepted ? verifiedArchive : null);
                if (accepted && reviewedBackup != null) { backup = reviewedBackup; backupId = scope.Id; backupPresented = false; reviewedBackup = null; }
            }
            try { if (reviewedBackup != null) reviewedBackup.Dispose(); if (capturedBackup != null) capturedBackup.Dispose(); }
            finally { lock (sync) { if (active == scope) active = null; } }
            scope.Dispose(); return completed;
        }
        internal bool TryPresent(WindowsReleaseReviewResult result) {
            WindowsChannelReplayStore.BackupReview discarded = null; bool presented = false;
            lock (sync) {
                if (result != null && result.Owner == this && !disposed && available && result.Epoch == epoch && result.Id == id && Interlocked.CompareExchange(ref result.Presented, 1, 0) == 0) {
                    if (result.Accepted && result.Action == WindowsReleaseReviewAction.Check) offer = result.Offer;
                    if (result.Accepted && result.Action == WindowsReleaseReviewAction.Archive) archivePath = result.ArchivePath;
                    if (result.Accepted && result.Action == WindowsReleaseReviewAction.Backup && backupId == result.Id) backupPresented = true;
                    presented = true;
                } else if (result != null && result.Owner == this && result.Action == WindowsReleaseReviewAction.Backup && backupId == result.Id && Volatile.Read(ref result.Presented) == 0) { discarded = backup; backup = null; backupPresented = false; }
            }
            if (discarded != null) discarded.Dispose(); return presented;
        }
        internal static string Describe(WindowsChannelOffer offer, string installed) {
            if (offer == null) return "No authenticated release snapshot is selected.";
            string comparison; switch (offer.Release.Compare(installed)) {
                case WindowsReleaseComparison.NewerAvailable: comparison = "A newer stable version is available."; break;
                case WindowsReleaseComparison.SameStableVersion: comparison = "The signed channel has the same stable version. This does not verify the installed files."; break;
                case WindowsReleaseComparison.ChannelOlder: comparison = "The signed channel version is older than this installed stable version."; break;
                default: comparison = "This development or unknown installed version cannot be compared with a stable release."; break;
            }
            return comparison + "\r\nRelease: " + offer.Release.Version + "\r\nSource revision (publisher attestation): " + offer.Release.SourceRevision + "\r\nMetadata expires UTC: " + new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(offer.ExpiresUtc).ToString("u") + "\r\n\r\nManual download URL\r\n" + offer.Release.ArchiveUrl.AbsoluteUri + "\r\nArchive: " + offer.Release.ArchiveName + "\r\nLength: " + offer.Release.ArchiveLength + " bytes\r\nSHA-256: " + offer.Release.ArchiveHash + "\r\n\r\nEach selected-file action rechecks current metadata trust, expiry and exact contents. This snapshot does not prove source/build provenance or authorize installation.";
        }
        public void Dispose() { WindowsUpdateCheckScope revoked; WindowsChannelReplayStore.BackupReview discarded; lock (sync) { if (disposed) return; disposed = true; available = false; epoch++; revoked = active; if (revoked != null) revoked.Invalidate(); offer = null; archivePath = null; discarded = backup; backup = null; backupPresented = false; } if (revoked != null) revoked.Cancel(); if (discarded != null) discarded.Dispose(); }
    }
    internal sealed class WindowsReleaseReviewDialog : Form {
        internal readonly Button CheckRelease, VerifyArchive, VerifyExtracted, StopReview, CloseReview, ReviewBackup, RestoreBackup, StopBackup;
        internal readonly TextBox ReleaseContent, GuideContent, ReviewStatus, BackupContent, BackupStatus;
        readonly WindowsReleaseReviewSession session; readonly InstallationFolders folders; readonly string version; readonly Action activity;
        long presentation;
        internal bool NestedReview { get; private set; }
        internal WindowsReleaseReviewDialog(string version, WindowsReleaseReviewSession session, InstallationFolders folders, Action activity) {
            this.version = version; this.session = session; this.folders = folders; this.activity = activity ?? (() => {});
            Text = "Windows updates and installation"; Font = new Font("Segoe UI", 10); Size = new Size(880, 650); MinimumSize = new Size(720, 440); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var tabs = new TabControl { Dock = DockStyle.Fill }; var release = new TabPage("Release review"); var guide = new TabPage("Installation guide"); var history = new TabPage("Update history"); tabs.TabPages.Add(release); tabs.TabPages.Add(guide); tabs.TabPages.Add(history);
            ReleaseContent = TextArea(session.Configured ? "Choose Check release metadata when idle. Only the approved metadata and signature are requested. Download and extraction are manual; selected-file verification changes no files." : WindowsUpdateSafety.ChannelStatus);
            GuideContent = TextArea(ManualInstallation.Instructions(version, folders, session.Configured ? "Signed Windows metadata checks are configured. Use Release review for a fresh manual check; installation and rollback remain manual." : null));
            release.Controls.Add(ReleaseContent); guide.Controls.Add(GuideContent);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
            CheckRelease = Button("Check release metadata"); VerifyArchive = Button("Verify selected ZIP…"); VerifyExtracted = Button("Verify extracted folder…"); StopReview = Button("Cancel review");
            actions.Controls.AddRange(new Control[] { CheckRelease, VerifyArchive, VerifyExtracted, StopReview }); release.Controls.Add(actions);
            ReviewStatus = TextArea(session.Configured ? "A review starts only from an explicit button." : "Channel unavailable. No check or download occurs."); ReviewStatus.Dock = DockStyle.Bottom; ReviewStatus.Height = 94; release.Controls.Add(ReviewStatus);
            BackupContent = TextArea("Review windows-channel-replay.json from a separate settings backup before choosing Restore.\r\n\r\nOnly update history is restored. The backup, other settings and media stay intact. Older or conflicting history cannot replace the accepted restriction. Corrupt or foreign history is kept for review. Each restore requires an explicit confirmation; no release is downloaded or installed.");
            history.Controls.Add(BackupContent);
            var backupActions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
            ReviewBackup = Button("Review settings backup…"); RestoreBackup = Button("Restore reviewed update history…"); StopBackup = Button("Cancel review");
            backupActions.Controls.AddRange(new Control[] { ReviewBackup, RestoreBackup, StopBackup }); history.Controls.Add(backupActions);
            BackupStatus = TextArea(session.RecoveryConfigured ? "Choose a backup to review. Review alone changes no files." : "Recovery requires an approved Windows channel and its shared settings store."); BackupStatus.Dock = DockStyle.Bottom; BackupStatus.Height = 94; history.Controls.Add(BackupStatus);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10), FlowDirection = FlowDirection.RightToLeft };
            CloseReview = Button("Close"); CloseReview.DialogResult = DialogResult.Cancel;
            var openSettings = Button("Open settings folder"); openSettings.Enabled = folders.Settings.Available; var openApp = Button("Open app folder"); openApp.Enabled = folders.Application.Available;
            footer.Controls.AddRange(new Control[] { CloseReview, openSettings, openApp }); Controls.Add(tabs); Controls.Add(footer); AcceptButton = CloseReview; CancelButton = CloseReview;
            openSettings.Click += delegate { OpenFolder(true); }; openApp.Click += delegate { OpenFolder(false); };
            CheckRelease.Click += async delegate { await RunAction(() => session.CheckAsync(CancellationToken.None)); };
            VerifyArchive.Click += async delegate { await SelectCandidate(false); }; VerifyExtracted.Click += async delegate { await SelectCandidate(true); };
            StopReview.Click += delegate { session.Cancel(); UpdateButtons(); };
            StopBackup.Click += delegate { session.Cancel(); BackupStatus.Text = "Review cancelled. A restore requires another manual review."; UpdateButtons(); };
            ReviewBackup.Click += async delegate { await SelectBackup(); }; RestoreBackup.Click += async delegate { await ConfirmBackup(); };
            Shown += delegate { this.activity(); UpdateButtons(); }; EnabledChanged += delegate { this.activity(); UpdateButtons(); };
            FormClosing += delegate { session.Dispose(); };
            UpdateButtons();
        }
        static Button Button(string text) { return new Button { Text = text, AutoSize = true, MinimumSize = new Size(100, 36) }; }
        static TextBox TextArea(string text) { return new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Text = text }; }
        internal void PublishAvailable(bool value) { session.PublishAvailable(value && Enabled && !NestedReview && !IsDisposed && !Disposing); UpdateButtons(); }
        internal void UpdateButtons() { if (IsDisposed || Disposing) return; CheckRelease.Enabled = session.CanCheck; VerifyArchive.Enabled = session.CanArchive; VerifyExtracted.Enabled = session.CanExtracted; StopReview.Enabled = session.Busy; ReviewBackup.Enabled = session.CanBackup; RestoreBackup.Enabled = session.CanRestoreBackup; StopBackup.Enabled = session.Busy || session.BackupId != 0; }
        internal async Task RunAction(Func<Task<WindowsReleaseReviewResult>> action) {
            if (IsDisposed || Disposing || session.Busy) return;
            long ownedPresentation = ++presentation;
            try {
                var operation = action(); ReviewStatus.Text = "Reviewing signed metadata or selected files…"; ReleaseContent.Text = "Review in progress. Selected-file results appear after current verification completes."; BackupStatus.Text = "An owned review is in progress. Wait for its result."; UpdateButtons(); var result = await operation;
                if (IsDisposed || Disposing || presentation != ownedPresentation) return;
                if (!session.TryPresent(result)) { ReviewStatus.Text = "Review ended after its activity or ownership changed. Another attempt is manual."; ReleaseContent.Text = ReviewStatus.Text; BackupStatus.Text = ReviewStatus.Text; return; }
                if (result.Action == WindowsReleaseReviewAction.Backup || result.Action == WindowsReleaseReviewAction.Restore) { BackupContent.Text = result.Message; BackupStatus.Text = result.Accepted && result.Action == WindowsReleaseReviewAction.Backup ? "Backup reviewed. Restore is a separate confirmed action." : result.Message; ReleaseContent.Text = "Check signed release metadata again before reviewing selected release files."; return; }
                BackupStatus.Text = session.RecoveryConfigured ? "No backup is selected. Choose a backup to review." : "Recovery requires an approved Windows channel and its shared settings store.";
                ReviewStatus.Text = result.Message; if (result.Accepted) ReleaseContent.Text = WindowsReleaseReviewSession.Describe(result.Offer, version) + "\r\n\r\n" + result.Message;
                else ReleaseContent.Text = result.Message + "\r\n\r\nNo download, extraction, execution or installation occurred. Another attempt requires a manual choice.";
            } catch (Exception ex) { if (!IsDisposed && !Disposing && presentation == ownedPresentation) { ReviewStatus.Text = "Review unavailable: " + ex.Message; BackupStatus.Text = ReviewStatus.Text; } }
            finally { if (!IsDisposed && !Disposing && presentation == ownedPresentation) { activity(); UpdateButtons(); } }
        }
        async Task SelectBackup() {
            if (!session.CanBackup) return; string path = null; NestedReview = true; activity();
            try { using (var picker = new OpenFileDialog { Title = "Select update history from your settings backup", FileName = WindowsChannelReplayStore.FileName, Filter = "Update history (windows-channel-replay.json)|windows-channel-replay.json", CheckFileExists = true, Multiselect = false, RestoreDirectory = true }) { if (picker.ShowDialog(this) == DialogResult.OK) path = picker.FileName; } }
            catch (Exception ex) { if (!IsDisposed && !Disposing) BackupStatus.Text = "Selection unavailable: " + ex.Message; }
            finally { NestedReview = false; activity(); UpdateButtons(); }
            if (path != null) await RunAction(() => session.BackupAsync(path, CancellationToken.None));
        }
        internal async Task ConfirmBackup(Func<string, bool> confirm = null) {
            if (!session.CanRestoreBackup || IsDisposed || Disposing) return; long expected = session.BackupId; string description = session.BackupDescription; if (description == null) return; bool confirmed = false; NestedReview = true; activity();
            try { if (confirm != null) confirmed = confirm(description); else using (var dialog = new WindowsReplayBackupConfirmation(description)) confirmed = dialog.ShowDialog(this) == DialogResult.OK; }
            catch (Exception ex) { if (!IsDisposed && !Disposing) BackupStatus.Text = "Confirmation unavailable: " + ex.Message; }
            finally { NestedReview = false; if (!IsDisposed && !Disposing) { activity(); UpdateButtons(); } }
            if (IsDisposed || Disposing) return;
            if (confirmed) await RunAction(() => session.RestoreBackupAsync(expected, CancellationToken.None));
            else { session.Cancel(); BackupStatus.Text = "Restore cancelled. This action changed no update history."; UpdateButtons(); }
        }
        async Task SelectCandidate(bool extracted) {
            if (extracted ? !session.CanExtracted : !session.CanArchive) return;
            string path = null; NestedReview = true; activity();
            try {
                if (extracted) using (var picker = new FolderBrowserDialog { Description = "Select the complete Windows folder you extracted manually", ShowNewFolderButton = false }) { if (picker.ShowDialog(this) == DialogResult.OK) path = picker.SelectedPath; }
                else using (var picker = new OpenFileDialog { Title = "Select the Windows ZIP you downloaded manually", Filter = "Windows release ZIP (*.zip)|*.zip", CheckFileExists = true, Multiselect = false, RestoreDirectory = true }) { if (picker.ShowDialog(this) == DialogResult.OK) path = picker.FileName; }
            } catch (Exception ex) { if (!IsDisposed && !Disposing) ReviewStatus.Text = "Selection unavailable: " + ex.Message; }
            finally { NestedReview = false; activity(); UpdateButtons(); }
            if (path != null) await RunAction(() => extracted ? session.ExtractedAsync(path, CancellationToken.None) : session.ArchiveAsync(path, CancellationToken.None));
        }
        void OpenFolder(bool settings) { if (session.Busy || IsDisposed || Disposing) return; try { folders.Open(settings); } catch (Exception ex) { ReviewStatus.Text = "Folder unavailable: " + ex.Message; } }
        protected override void Dispose(bool disposing) { if (disposing) { session.Dispose(); folders.Dispose(); } base.Dispose(disposing); }
    }
}
