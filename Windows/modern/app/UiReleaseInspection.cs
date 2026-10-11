#if UI_TESTS
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace EasyEject.Modern {
    public sealed partial class MainWindow {
        ReviewReleaseBackend reviewReleaseBackend;
        bool useReviewReleaseBackend;
        bool? lastReviewReleaseAvailable;
        bool reviewReleaseDeliveryPaused;
        async Task PauseReviewReleaseDelivery(ReleaseView owner, ReleaseViewResult result) {
            if (!UiTestHost.Review || reviewReleaseBackend == null || reviewReleaseDeliveryPaused ||
                !File.Exists(Path.Combine(UiTestHost.Root, "release-delivery-pause.txt"))) return;
            reviewReleaseDeliveryPaused = true;
            // The real presenter worker is terminal. Hold only this UI handler's
            // consumption, leaving dispatch/pulses/Close available for inspection.
            File.WriteAllText(Path.Combine(UiTestHost.Root, "release-delivery-wait.json"),
                System.Text.Json.JsonSerializer.Serialize(new { utc = DateTime.UtcNow,
                    action = result?.Action.ToString(), accepted = result?.Accepted,
                    open = owner.Open, busy = owner.Busy, canClose = owner.CanClose }));
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(UiTestHost.Root, "release-delivery-release.txt"))) {
                if (timer.Elapsed > TimeSpan.FromMinutes(5)) throw new TimeoutException("Owned generated UI delivery pause timed out.");
                await Task.Delay(50);
            }
            File.WriteAllText(Path.Combine(UiTestHost.Root, "release-delivery-resumed.json"),
                System.Text.Json.JsonSerializer.Serialize(new { utc = DateTime.UtcNow,
                    open = owner.Open, busy = owner.Busy, canClose = owner.CanClose }));
        }
        bool ReviewReleaseActivityAvailable() {
            if (!UiTestHost.Review || reviewReleaseBackend == null) return true;
            bool value = !File.Exists(Path.Combine(UiTestHost.Root, "release-activity-busy.txt"));
            if (lastReviewReleaseAvailable != value) {
                lastReviewReleaseAvailable = value;
                File.AppendAllText(Path.Combine(UiTestHost.Root, "release-activity.jsonl"),
                    System.Text.Json.JsonSerializer.Serialize(new { utc = DateTime.UtcNow, available = value,
                        nested = releaseView.Nested, busy = releaseView.Busy, canClose = releaseView.CanClose }) + "\r\n");
            }
            return value;
        }
        void InitializeReleaseReviewProbe(StackPanel footer) {
            var button = new Button { Content = "Test release workflows" };
            button.Click += (_, args) => {
                if (StorageBusy || confirmation != null) return;
                if (File.Exists(Path.Combine(UiTestHost.Root, "release-backend.jsonl"))) {
                    Summary.Text = "Choose a fresh owned fixture for another release inspection."; return;
                }
                useReviewReleaseBackend = true; ViewModernReleases();
            };
            footer.Children.Add(button);
        }
        IReleaseViewBackend CreateReviewReleaseBackend() {
            if (!UiTestHost.Review || !useReviewReleaseBackend) {
                reviewReleaseBackend = null;
                return WindowsReleaseViewBackend.ForModern(releaseChecks, WindowsReleaseChecks.ModernProductionChannel, ApplicationInformation.Version, components: WindowsReleaseChecks.ModernProductionComponents);
            }
            useReviewReleaseBackend = false;
            return reviewReleaseBackend = new ReviewReleaseBackend(UiTestHost.Root);
        }
    }
    // Read-only, clearly generated backend for the real foreground presenter.
    // Never authenticates a release, accesses replay state, restores or installs.
    internal sealed class ReviewReleaseBackend : IReleaseViewBackend {
        readonly string root, history; readonly object traceLock = new object();
        volatile bool available, held, closed;
        long backup; int runs, presentations;
        internal ReviewReleaseBackend(string root) { this.root = root; history = Path.Combine(root, "generated-history.json"); Record("Created"); }
        void Record(string kind, WindowsReleaseReviewAction? action = null, string path = null) {
            lock (traceLock) File.AppendAllText(Path.Combine(root, "release-backend.jsonl"),
                System.Text.Json.JsonSerializer.Serialize(new { utc = DateTime.UtcNow, kind, action = action?.ToString(), path, held, runs, presentations, backup }) + "\r\n");
        }
        public bool Configured => true;
        public bool Busy => held;
        bool Ready => available && !held && !closed;
        public bool CanCheck => Ready;
        public bool CanArchive => false;
        public bool CanExtracted => false;
        public bool CanBackup => Ready;
        public bool CanRestore => Ready && backup != 0;
        public long BackupId => backup;
        public string BackupDescription => backup == 0 ? null : "Generated reviewed history 9001; no real restore capability.";
        public void SetAvailable(bool value) { if (available == value) return; available = value; Record("Available"); }
        public void Cancel() { backup = 0; Record("Cancelled"); }
        public async Task<ReleaseViewResult> Run(WindowsReleaseReviewAction action, string path, long confirmation) {
            held = true; runs++; Record("Started", action, path);
            try {
                bool secondHold = runs == 2 && File.Exists(Path.Combine(root, "release-second-worker-hold.txt"));
                if (action == WindowsReleaseReviewAction.Check && (runs == 1 || secondHold)) {
                    string release = Path.Combine(root, secondHold ? "release-second-worker-release.txt" : "release-worker-release.txt");
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    while (!File.Exists(release)) {
                        if (timer.Elapsed > TimeSpan.FromMinutes(3)) throw new TimeoutException("Owned generated release worker timed out.");
                        await Task.Delay(50).ConfigureAwait(false);
                    }
                }
                if (action == WindowsReleaseReviewAction.Backup) {
                    if (!String.Equals(Path.GetFullPath(path), history, StringComparison.OrdinalIgnoreCase))
                        throw new SafetyException("Only the owned generated history fixture can be read.");
                    if (File.ReadAllText(history) != "{\"generated\":true,\"authenticUpdateHistory\":false}")
                        throw new SafetyException("Generated history fixture changed.");
                } else if (action != WindowsReleaseReviewAction.Check)
                    throw new SafetyException("No restore, archive, installation or executable capability exists in this engineering backend.");
                Record("ResultCreated", action, path);
                return new ReleaseViewResult(action, true, "Generated foreground result; no update was authenticated or applied.",
                    action == WindowsReleaseReviewAction.Backup ? "Generated reviewed history 9001; no real restore capability." : "Generated metadata result; production channel remains unconfigured.", () => {
                        presentations++; if (action == WindowsReleaseReviewAction.Backup) backup = 9001;
                        Record("Presented", action, path); return true;
                    });
            } finally { held = false; Record("Terminal", action, path); }
        }
        public void Dispose() { if (held) throw new InvalidOperationException("Generated worker still owns backend."); closed = true; Record("Disposed"); }
    }
}
#endif
