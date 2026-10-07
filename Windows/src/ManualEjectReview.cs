using System;
using System.Threading;

namespace EasyEject {
    // The confirmed review is one-use and has its own immutable plan/cleanup scope.
    // The shared eject/cleanup engines still recheck before each filesystem/native action.
    internal sealed class ManualEjectReview {
        readonly IDiscovery discovery; readonly Operations operations; readonly Func<Action, IEjectApi> api;
        readonly Func<string, string> volumeId; readonly EjectPlan plan; readonly MetadataCleanupPlan cleanup;
        int phase;
        internal readonly string Description, Display;
        internal long Epoch { get { return plan.Epoch; } }
        internal bool HasCleanup { get { return cleanup != null; } }
        internal ManualEjectReview(Inventory state, TrayStorageChoice choice, AppSettings settings, bool forceClean,
            IDiscovery discovery, Operations operations, Func<Action, IEjectApi> api, Func<string, string> volumeId) {
            this.discovery = discovery; this.operations = operations; this.api = api; this.volumeId = volumeId;
            var volume = choice.Resolve(state, discovery.Epoch); var selected = EjectService.Plan(state, volume.Id);
            plan = new EjectPlan { TargetId = selected.TargetId, TargetDevInst = selected.TargetDevInst, Fingerprint = selected.Fingerprint,
                Epoch = selected.Epoch, Disks = (int[])selected.Disks.Clone(), Affected = (string[])selected.Affected.Clone() };
            Display = volume.Display;
            bool clean = forceClean || volume.IsCard && settings.CleanupCardsEnabled;
            cleanup = clean ? MetadataCleanupPlan.Single(state, plan, volume.Id) : null;
            Description = "Windows will remove this device and all affected volumes:\n\n" + String.Join("\n", plan.Affected)
                + (!volume.IsCard && settings.WarnOtherEnabled ? "\n\nThis storage is not recognized as a camera or emulator card. Review other data and every affected partition before ejecting." : "")
                + (cleanup == null ? "" : "\n\n" + cleanup.Description) + "\n\nFinish all transfers first. Request safe removal?";
        }
        internal bool Current { get { return Volatile.Read(ref phase) == 0 && discovery.Epoch == plan.Epoch; } }
        internal void Confirm() {
            if (discovery.Epoch != plan.Epoch || Interlocked.CompareExchange(ref phase, 1, 0) != 0) throw new SafetyException("This eject review expired. Refresh and review the device again.");
        }
        internal void Cancel() { Interlocked.CompareExchange(ref phase, 3, 0); }
        internal EjectResult Execute(CancellationToken cancellation, Action<string> report) {
            if (Interlocked.CompareExchange(ref phase, 2, 1) != 1) throw new SafetyException("A fresh explicit eject confirmation is required.");
            var counts = new CleanupCounts();
            try {
                cancellation.ThrowIfCancellationRequested(); EjectService.Validate(discovery, discovery.Scan(), plan);
                Func<Inventory> fresh = () => { cancellation.ThrowIfCancellationRequested(); var current = discovery.Scan(); EjectService.Validate(discovery, current, plan); return current; };
                var result = new EjectService(discovery, api(() => { fresh(); }), operations).Execute(plan, initial => {
                    if (cleanup == null) return;
                    cleanup.Preflight(initial, volumeId);
                    cleanup.Clean(fresh, volumeId, cancellation, counts, report);
                }, cancellation);
                if (cleanup != null) result.CleanupMessage = counts.Message; return result;
            } catch (SafetyException) {
                if (cancellation.IsCancellationRequested) throw new OperationCanceledException("Metadata cleanup cancelled before removal. " + counts.Message, cancellation);
                throw;
            } finally { Interlocked.Exchange(ref phase, 3); }
        }
    }
}
