using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyEject {
    internal enum WindowsReleaseReviewAction { Check, Archive, Extracted }
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
        internal WindowsReleaseReviewSession(WindowsUpdateCoordinator checks, ApprovedWindowsChannel channel,
            Func<string, CancellationToken, WindowsArchiveReview> archive = null, Func<string, CancellationToken, WindowsPackageReview> package = null) {
            if (checks == null || checks.Configured != (channel != null)) throw new ArgumentException("Release review requires the same configured channel as its check coordinator.");
            this.checks = checks; this.channel = channel;
            this.archive = archive ?? ((path, cancel) => new WindowsArchiveReview(path, channel.Publisher, new WindowsManifestTrust(), cancel));
            this.package = package ?? ((path, cancel) => new WindowsPackageReview(path, channel.Publisher, new WindowsAuthenticode(), new WindowsManifestTrust(), cancel));
        }
        internal bool Configured { get { return channel != null; } }
        internal bool Busy { get { lock (sync) return active != null; } }
        internal bool CanCheck { get { lock (sync) return !disposed && available && Configured && active == null && !checks.Checking; } }
        internal bool CanArchive { get { lock (sync) return !disposed && available && active == null && offer != null; } }
        internal bool CanExtracted { get { lock (sync) return !disposed && available && active == null && offer != null && archivePath != null; } }
        internal void PublishAvailable(bool value) {
            WindowsUpdateCheckScope revoked = null;
            lock (sync) { if (disposed || available == value) return; available = value; epoch++; if (!value && active != null) { revoked = active; revoked.Invalidate(); } }
            if (revoked != null) revoked.Cancel();
        }
        internal void Cancel() { WindowsUpdateCheckScope revoked; lock (sync) { revoked = active; if (revoked != null) revoked.Invalidate(); } if (revoked != null) revoked.Cancel(); }
        WindowsUpdateCheckScope Capture(WindowsReleaseReviewAction action, CancellationToken cancel, out WindowsChannelOffer captured, out string capturedArchive) {
            lock (sync) {
                if (disposed) throw new ObjectDisposedException("WindowsReleaseReviewSession"); cancel.ThrowIfCancellationRequested();
                if (!Configured) throw new SafetyException(WindowsUpdateSafety.ChannelStatus);
                if (!available || active != null) throw new SafetyException("Finish the active review, scan or other dialog before another release action.");
                if (action != WindowsReleaseReviewAction.Check && offer == null || action == WindowsReleaseReviewAction.Extracted && archivePath == null) throw new SafetyException("Check signed release metadata and verify its selected ZIP before reviewing an extracted folder.");
                captured = offer; capturedArchive = archivePath;
                if (action == WindowsReleaseReviewAction.Check) { offer = null; archivePath = null; }
                if (action == WindowsReleaseReviewAction.Archive) archivePath = null;
                active = new WindowsUpdateCheckScope(++id, epoch, 0, WindowsUpdateCheckOrigin.Manual, cancel); return active;
            }
        }
        void Guard(WindowsUpdateCheckScope scope) { scope.Token.ThrowIfCancellationRequested(); lock (sync) { if (disposed || !available || active != scope || epoch != scope.ActivityEpoch || !scope.Allowed) throw new OperationCanceledException(scope.Token); } }
        internal Task<WindowsReleaseReviewResult> CheckAsync(CancellationToken cancel) { return Begin(WindowsReleaseReviewAction.Check, null, cancel); }
        internal Task<WindowsReleaseReviewResult> ArchiveAsync(string path, CancellationToken cancel) { return Begin(WindowsReleaseReviewAction.Archive, path, cancel); }
        internal Task<WindowsReleaseReviewResult> ExtractedAsync(string folder, CancellationToken cancel) { return Begin(WindowsReleaseReviewAction.Extracted, folder, cancel); }
        Task<WindowsReleaseReviewResult> Begin(WindowsReleaseReviewAction action, string path, CancellationToken cancel) {
            WindowsChannelOffer captured; string capturedArchive; var scope = Capture(action, cancel, out captured, out capturedArchive);
            return Run(scope, action, path, captured, capturedArchive);
        }
        async Task<WindowsReleaseReviewResult> Run(WindowsUpdateCheckScope scope, WindowsReleaseReviewAction action, string path, WindowsChannelOffer captured, string capturedArchive) {
            string message = null, verifiedArchive = null; Exception error = null; WindowsChannelOffer checkedOffer = captured;
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
                completed = new WindowsReleaseReviewResult(this, scope, action, accepted, cancelled, cancelled ? "Review cancelled. Selected files are retained; retry is manual." : error == null ? message : "Review refused: " + error.Message, accepted ? checkedOffer : null, accepted ? verifiedArchive : null);
                if (active == scope) active = null;
            }
            scope.Dispose(); return completed;
        }
        internal bool TryPresent(WindowsReleaseReviewResult result) {
            lock (sync) {
                if (result == null || result.Owner != this || disposed || !available || result.Epoch != epoch || result.Id != id || Interlocked.CompareExchange(ref result.Presented, 1, 0) != 0) return false;
                if (result.Accepted && result.Action == WindowsReleaseReviewAction.Check) offer = result.Offer;
                if (result.Accepted && result.Action == WindowsReleaseReviewAction.Archive) archivePath = result.ArchivePath;
                return true;
            }
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
        public void Dispose() { WindowsUpdateCheckScope revoked; lock (sync) { if (disposed) return; disposed = true; available = false; epoch++; revoked = active; if (revoked != null) revoked.Invalidate(); offer = null; archivePath = null; } if (revoked != null) revoked.Cancel(); }
    }
    internal sealed class WindowsReleaseReviewDialog : Form {
        internal readonly Button CheckRelease, VerifyArchive, VerifyExtracted, StopReview, CloseReview;
        internal readonly TextBox ReleaseContent, GuideContent, ReviewStatus;
        readonly WindowsReleaseReviewSession session; readonly InstallationFolders folders; readonly string version; readonly Action activity;
        long presentation;
        internal bool NestedReview { get; private set; }
        internal WindowsReleaseReviewDialog(string version, WindowsReleaseReviewSession session, InstallationFolders folders, Action activity) {
            this.version = version; this.session = session; this.folders = folders; this.activity = activity ?? (() => {});
            Text = "Windows updates and installation"; Font = new Font("Segoe UI", 10); Size = new Size(880, 650); MinimumSize = new Size(720, 440); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var tabs = new TabControl { Dock = DockStyle.Fill }; var release = new TabPage("Release review"); var guide = new TabPage("Installation guide"); tabs.TabPages.Add(release); tabs.TabPages.Add(guide);
            ReleaseContent = TextArea(session.Configured ? "Choose Check release metadata when idle. Only the approved metadata and signature are requested. Download and extraction are manual; selected-file verification changes no files." : WindowsUpdateSafety.ChannelStatus);
            GuideContent = TextArea(ManualInstallation.Instructions(version, folders, session.Configured ? "Signed Windows metadata checks are configured. Use Release review for a fresh manual check; installation and rollback remain manual." : null));
            release.Controls.Add(ReleaseContent); guide.Controls.Add(GuideContent);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
            CheckRelease = Button("Check release metadata"); VerifyArchive = Button("Verify selected ZIP…"); VerifyExtracted = Button("Verify extracted folder…"); StopReview = Button("Cancel review");
            actions.Controls.AddRange(new Control[] { CheckRelease, VerifyArchive, VerifyExtracted, StopReview }); release.Controls.Add(actions);
            ReviewStatus = TextArea(session.Configured ? "A review starts only from an explicit button." : "Channel unavailable. No check or download occurs."); ReviewStatus.Dock = DockStyle.Bottom; ReviewStatus.Height = 94; release.Controls.Add(ReviewStatus);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10), FlowDirection = FlowDirection.RightToLeft };
            CloseReview = Button("Close"); CloseReview.DialogResult = DialogResult.Cancel;
            var openSettings = Button("Open settings folder"); openSettings.Enabled = folders.Settings.Available; var openApp = Button("Open app folder"); openApp.Enabled = folders.Application.Available;
            footer.Controls.AddRange(new Control[] { CloseReview, openSettings, openApp }); Controls.Add(tabs); Controls.Add(footer); AcceptButton = CloseReview; CancelButton = CloseReview;
            openSettings.Click += delegate { OpenFolder(true); }; openApp.Click += delegate { OpenFolder(false); };
            CheckRelease.Click += async delegate { await RunAction(() => session.CheckAsync(CancellationToken.None)); };
            VerifyArchive.Click += async delegate { await SelectCandidate(false); }; VerifyExtracted.Click += async delegate { await SelectCandidate(true); };
            StopReview.Click += delegate { session.Cancel(); UpdateButtons(); };
            Shown += delegate { this.activity(); UpdateButtons(); }; EnabledChanged += delegate { this.activity(); UpdateButtons(); };
            FormClosing += delegate { session.Dispose(); };
            UpdateButtons();
        }
        static Button Button(string text) { return new Button { Text = text, AutoSize = true, MinimumSize = new Size(100, 36) }; }
        static TextBox TextArea(string text) { return new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Text = text }; }
        internal void PublishAvailable(bool value) { session.PublishAvailable(value && Enabled && !NestedReview && !IsDisposed && !Disposing); UpdateButtons(); }
        internal void UpdateButtons() { if (IsDisposed || Disposing) return; CheckRelease.Enabled = session.CanCheck; VerifyArchive.Enabled = session.CanArchive; VerifyExtracted.Enabled = session.CanExtracted; StopReview.Enabled = session.Busy; }
        internal async Task RunAction(Func<Task<WindowsReleaseReviewResult>> action) {
            if (IsDisposed || Disposing || session.Busy) return;
            long ownedPresentation = ++presentation;
            try {
                var operation = action(); ReviewStatus.Text = "Reviewing signed metadata or selected files…"; ReleaseContent.Text = "Review in progress. Selected-file results appear after current verification completes."; UpdateButtons(); var result = await operation;
                if (IsDisposed || Disposing || presentation != ownedPresentation) return;
                if (!session.TryPresent(result)) { ReviewStatus.Text = "Review ended after its activity or ownership changed. Another attempt is manual."; ReleaseContent.Text = ReviewStatus.Text; return; }
                ReviewStatus.Text = result.Message; if (result.Accepted) ReleaseContent.Text = WindowsReleaseReviewSession.Describe(result.Offer, version) + "\r\n\r\n" + result.Message;
                else ReleaseContent.Text = result.Message + "\r\n\r\nNo download, extraction, execution or installation occurred. Another attempt requires a manual choice.";
            } catch (Exception ex) { if (!IsDisposed && !Disposing && presentation == ownedPresentation) ReviewStatus.Text = "Review unavailable: " + ex.Message; }
            finally { if (!IsDisposed && !Disposing && presentation == ownedPresentation) { activity(); UpdateButtons(); } }
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
