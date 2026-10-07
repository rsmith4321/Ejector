using System;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEject {
    internal sealed class ReleaseViewResult {
        internal readonly WindowsReleaseReviewAction Action;
        internal readonly bool Accepted;
        internal readonly string Message, Description;
        readonly Func<bool> present; int consumed;
        internal ReleaseViewResult(WindowsReleaseReviewAction action, bool accepted, string message, string description, Func<bool> present) { Action = action; Accepted = accepted; Message = message; Description = description; this.present = present; }
        internal bool TryPresent() { return Interlocked.CompareExchange(ref consumed, 1, 0) == 0 && present != null && present(); }
    }
    internal interface IReleaseViewBackend : IDisposable {
        bool Configured { get; } bool Busy { get; }
        bool CanCheck { get; } bool CanArchive { get; } bool CanExtracted { get; } bool CanBackup { get; } bool CanRestore { get; }
        long BackupId { get; } string BackupDescription { get; }
        void SetAvailable(bool value); void Cancel();
        Task<ReleaseViewResult> Run(WindowsReleaseReviewAction action, string path, long confirmation);
    }
    internal sealed class WindowsReleaseViewBackend : IReleaseViewBackend {
        readonly WindowsUpdateCoordinator checks; readonly WindowsReleaseReviewSession session; readonly string version;
        internal WindowsReleaseViewBackend(WindowsUpdateCoordinator checks, ApprovedWindowsChannel channel, string version) { this.checks = checks; session = new WindowsReleaseReviewSession(checks, channel); this.version = version; }
        public bool Configured { get { return session.Configured; } } public bool Busy { get { return session.Busy; } }
        public bool CanCheck { get { return session.CanCheck; } } public bool CanArchive { get { return session.CanArchive; } } public bool CanExtracted { get { return session.CanExtracted; } }
        public bool CanBackup { get { return session.CanBackup; } } public bool CanRestore { get { return session.CanRestoreBackup; } }
        public long BackupId { get { return session.BackupId; } } public string BackupDescription { get { return session.BackupDescription; } }
        public void SetAvailable(bool value) { checks.SetReviewOpen(true); checks.PublishIdle(value); session.PublishAvailable(value); }
        public void Cancel() { session.Cancel(); }
        public async Task<ReleaseViewResult> Run(WindowsReleaseReviewAction action, string path, long confirmation) {
            Task<WindowsReleaseReviewResult> operation;
            switch (action) {
                case WindowsReleaseReviewAction.Check: operation = session.CheckAsync(CancellationToken.None); break;
                case WindowsReleaseReviewAction.Archive: operation = session.ArchiveAsync(path, CancellationToken.None); break;
                case WindowsReleaseReviewAction.Extracted: operation = session.ExtractedAsync(path, CancellationToken.None); break;
                case WindowsReleaseReviewAction.Backup: operation = session.BackupAsync(path, CancellationToken.None); break;
                case WindowsReleaseReviewAction.Restore: operation = session.RestoreBackupAsync(confirmation, CancellationToken.None); break;
                default: throw new SafetyException("Choose a supported release review action.");
            }
            var result = await operation.ConfigureAwait(false);
            return new ReleaseViewResult(result.Action, result.Accepted, result.Message, result.Action == WindowsReleaseReviewAction.Backup || result.Action == WindowsReleaseReviewAction.Restore ? result.Message : WindowsReleaseReviewSession.Describe(result.Offer, version) + "\n\n" + result.Message, () => session.TryPresent(result));
        }
        public void Dispose() { session.Dispose(); }
    }
    internal sealed class ReleaseViewChoice {
        internal readonly ReleaseView Owner; internal readonly long Epoch, Confirmation;
        internal readonly WindowsReleaseReviewAction Action; internal readonly string Description;
        internal ReleaseViewChoice(ReleaseView owner, long epoch, WindowsReleaseReviewAction action, long confirmation, string description) { Owner = owner; Epoch = epoch; Action = action; Confirmation = confirmation; Description = description; }
    }
    // Keeps native pickers, inline confirmation and an exact worker distinct. A stop
    // revokes delivery immediately, but does not release their owned lifetime early.
    internal sealed class ReleaseView : IDisposable {
        readonly object sync = new object(); readonly IReleaseViewBackend backend; readonly InstallationFolders folders;
        bool available; int phase; long epoch; ReleaseViewChoice choice;
        internal ReleaseView(IReleaseViewBackend backend, InstallationFolders folders) { if (backend == null || folders == null) throw new ArgumentNullException("release view"); this.backend = backend; this.folders = folders; }
        internal bool Open { get { lock (sync) return phase != 4; } }
        internal bool Busy { get { lock (sync) return phase == 1 || phase == 3 || backend.Busy; } }
        internal bool Nested { get { lock (sync) return phase == 1; } }
        internal bool CanClose { get { lock (sync) return phase == 0 && !backend.Busy; } }
        bool Ready { get { return phase == 0 && available && !backend.Busy; } }
        bool Can(WindowsReleaseReviewAction action) {
            if (!Ready) return false;
            switch (action) { case WindowsReleaseReviewAction.Check: return backend.CanCheck; case WindowsReleaseReviewAction.Archive: return backend.CanArchive;
                case WindowsReleaseReviewAction.Extracted: return backend.CanExtracted; case WindowsReleaseReviewAction.Backup: return backend.CanBackup; case WindowsReleaseReviewAction.Restore: return backend.CanRestore; default: return false; }
        }
        internal bool CanAction(WindowsReleaseReviewAction action) { lock (sync) return Can(action); }
        internal bool CanOpenFolder(bool settings) { lock (sync) return Ready && (settings ? folders.Settings : folders.Application).Available; }
        internal string Guide(string version, string status) { return ManualInstallation.ModernInstructions(version, folders, status); }
        void Publish() { backend.SetAvailable(available && (phase == 0 || phase == 3)); }
        internal void SetAvailable(bool value) { lock (sync) { if (phase == 4) return; if (available != value) { available = value; epoch++; } Publish(); } }
        internal void OpenFolder(bool settings) { lock (sync) { if (!CanOpenFolder(settings)) throw new SafetyException("Finish the current release action before opening a reviewed folder."); folders.Open(settings); } }
        internal ReleaseViewChoice BeginChoice(WindowsReleaseReviewAction action) {
            lock (sync) {
                if (action == WindowsReleaseReviewAction.Check || !Can(action)) throw new SafetyException("This release choice is unavailable. Review current status first.");
                long confirmation = action == WindowsReleaseReviewAction.Restore ? backend.BackupId : 0;
                string description = action == WindowsReleaseReviewAction.Restore ? backend.BackupDescription : null;
                if (action == WindowsReleaseReviewAction.Restore && (confirmation == 0 || description == null)) throw new SafetyException("Review this exact update-history backup first.");
                choice = new ReleaseViewChoice(this, epoch, action, confirmation, description); phase = 1; Publish(); return choice;
            }
        }
        ReleaseViewChoice FinishChoice(ReleaseViewChoice expected, bool restore) {
            lock (sync) {
                if (expected == null || expected.Owner != this || choice != expected || phase != 1 || (expected.Action == WindowsReleaseReviewAction.Restore) != restore) throw new SafetyException("This choice closed or belongs to another release view.");
                choice = null; phase = 0; Publish();
                if (!available || expected.Epoch != epoch) { backend.Cancel(); throw new SafetyException("Release activity changed during this choice. Open a fresh manual review."); }
                return expected;
            }
        }
        internal Task<ReleaseViewResult> FinishSelection(ReleaseViewChoice expected, string path) {
            var selected = FinishChoice(expected, false);
            if (path == null) { backend.Cancel(); return Task.FromResult<ReleaseViewResult>(null); }
            return Start(selected.Action, SafePaths.Normalize(path), 0);
        }
        internal Task<ReleaseViewResult> FinishConfirmation(ReleaseViewChoice expected, bool confirmed) {
            var selected = FinishChoice(expected, true);
            if (!confirmed) { backend.Cancel(); return Task.FromResult<ReleaseViewResult>(null); }
            if (backend.BackupId != selected.Confirmation || backend.BackupDescription != selected.Description) { backend.Cancel(); throw new SafetyException("The reviewed backup changed. Review it again before restoring history."); }
            return Start(WindowsReleaseReviewAction.Restore, null, selected.Confirmation);
        }
        internal Task<ReleaseViewResult> Check() { return Start(WindowsReleaseReviewAction.Check, null, 0); }
        Task<ReleaseViewResult> Start(WindowsReleaseReviewAction action, string path, long confirmation) {
            long captured;
            lock (sync) { if (!Can(action)) throw new SafetyException("This release action is unavailable or another action still owns the view."); captured = epoch; phase = 3; Publish(); }
            return Run(action, path, confirmation, captured);
        }
        bool Current(long captured) { lock (sync) return available && phase == 3 && epoch == captured; }
        async Task<ReleaseViewResult> Run(WindowsReleaseReviewAction action, string path, long confirmation, long captured) {
            try {
                if (!Current(captured)) throw new OperationCanceledException(); var operation = backend.Run(action, path, confirmation);
                if (operation == null) throw new SafetyException("The release backend returned no owned worker.");
                var result = await operation.ConfigureAwait(false);
                if (result == null) throw new SafetyException("The release backend returned no review result.");
                if (!Current(captured) || !result.TryPresent()) { backend.Cancel(); return new ReleaseViewResult(action, false, "Review ended after its activity changed. Any already published restrictive history is retained; reread before another manual action.", "", null); }
                return result;
            } catch (Exception ex) { backend.Cancel(); return new ReleaseViewResult(action, false, "Release action did not finish. Review current saved history before another manual action. " + ex.Message, "", null); }
            finally { lock (sync) { phase = 0; Publish(); } }
        }
        internal void Cancel() { lock (sync) { if (phase == 4) return; epoch++; backend.Cancel(); } }
        public void Dispose() { lock (sync) { if (phase == 4) return; if (!CanClose) throw new InvalidOperationException("A picker, confirmation or release worker still owns this view."); phase = 4; available = false; epoch++; backend.SetAvailable(false); backend.Dispose(); folders.Dispose(); } }
    }
}
