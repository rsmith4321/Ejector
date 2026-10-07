using System;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEject {
    internal sealed class ConcurrentStorageOutcome {
        internal readonly bool Success, Cancelled; internal readonly string Message;
        internal ConcurrentStorageOutcome(bool success, bool cancelled, string message) { Success = success; Cancelled = cancelled; Message = message; }
    }
    // Own one secondary confirmation/worker independently of the main transfer.
    // Terminal native results are retained before presentation, and the UI must
    // await the exact worker before finishing the session or starting completion.
    internal sealed class ConcurrentStorageReview : IDisposable {
        readonly object gate = new object(); readonly ConcurrentStorageState state; readonly ManualStorageSession session;
        readonly IDiscovery discovery; readonly ConcurrentEjectService service; readonly MetadataCleanupPlan cleanup;
        readonly CleanupCounts counts = new CleanupCounts(); readonly CancellationTokenSource cancel = new CancellationTokenSource();
        int phase; Task<ConcurrentStorageOutcome> worker; ConcurrentStorageOutcome outcome;
        internal readonly string Description, Display;
        internal ConcurrentStorageReview(ConcurrentStorageState state, Inventory inventory, TrayStorageChoice choice, AppSettings settings, bool forceClean,
            IDiscovery discovery, Operations operations, Func<Action, IEjectApi> api, Func<string, string> volumeId) {
            this.state = state; this.discovery = discovery;
            try {
                var volume = choice.Resolve(inventory, discovery.Epoch); session = state.Begin(inventory, discovery.Epoch, volume.Id); Display = session.Display;
                bool clean = forceClean || volume.IsCard && settings.CleanupCardsEnabled;
                cleanup = clean ? MetadataCleanupPlan.Single(inventory, session.Plan, volume.Id) : null;
                Description = "Another transfer continues on separate disks. Windows will remove this device and all affected storage:\n\n" + String.Join("\n", session.Plan.Affected)
                    + (!volume.IsCard && settings.WarnOtherEnabled ? "\n\nThis storage is not recognized as a camera or emulator card. Review every affected partition and other data." : "")
                    + (cleanup == null ? "" : "\n\n" + cleanup.Description)
                    + "\n\nFinish other programs' transfers on this device first. This action has its own Stop control; stopping the main transfer does not stop this action. Request safe removal?";
                service = new ConcurrentEjectService(discovery, operations, api, volumeId);
            } catch { if (session != null) state.Finish(session); cancel.Dispose(); throw; }
        }
        internal bool Busy { get { lock (gate) return phase == 1; } }
        internal bool Current { get { lock (gate) return phase == 0 && discovery.Epoch == session.Plan.Epoch && session.PrimaryGuard.AllowsUnrelatedOperations; } }
        internal ConcurrentStorageOutcome Outcome { get { lock (gate) return outcome; } }
        internal int[] Disks { get { return session.Plan.Disks; } }
        internal Task<ConcurrentStorageOutcome> Execute(Action<string> report) {
            lock (gate) {
                if (!Current) throw new SafetyException("This other-device review expired. Cancel and review fresh storage before retrying.");
                session.Confirm(); phase = 1;
                worker = Task.Run(() => {
                    ConcurrentStorageOutcome terminal;
                    try {
                        var result = service.Execute(session, cleanup, cancel.Token, counts, report);
                        terminal = new ConcurrentStorageOutcome(result.Success, false, result.Message);
                    } catch (OperationCanceledException) {
                        terminal = new ConcurrentStorageOutcome(false, true, "Other-device action cancelled before its removal request. The main transfer keeps its own cancellation state. " + counts.Message);
                    } catch (Exception ex) {
                        terminal = new ConcurrentStorageOutcome(false, false, "Other-device action stopped. No automatic retry. " + ex.Message + "\n" + counts.Message);
                    }
                    lock (gate) { outcome = terminal; phase = 2; } return terminal;
                });
                return worker;
            }
        }
        internal void RequestStop() { lock (gate) { if (phase == 1) cancel.Cancel(); } }
        internal bool TryClose() {
            lock (gate) {
                if (phase == 3) return true;
                if (phase == 1 || worker != null && !worker.IsCompleted) return false;
                phase = 3; state.Finish(session); cancel.Dispose(); return true;
            }
        }
        public void Dispose() { if (!TryClose()) throw new InvalidOperationException("The other-device worker still owns its reviewed session. Await it before closing."); }
    }
}
