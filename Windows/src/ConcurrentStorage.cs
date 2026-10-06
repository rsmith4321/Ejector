using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEject {
    internal sealed class ManualStorageSession {
        internal readonly IConcurrentStorageGuard PrimaryGuard;
        internal readonly string VolumeId, Display;
        readonly EjectPlan plan;
        int phase; // review, confirmed, executing, closed; no replay of an earlier explicit choice
        internal readonly TaskCompletionSource<bool> Terminal = new TaskCompletionSource<bool>();
        internal EjectPlan Plan { get { return new EjectPlan { TargetId = plan.TargetId, TargetDevInst = plan.TargetDevInst, Fingerprint = plan.Fingerprint, Epoch = plan.Epoch, Disks = (int[])plan.Disks.Clone(), Affected = (string[])plan.Affected.Clone() }; } }
        internal ManualStorageSession(IConcurrentStorageGuard guard, Volume volume, EjectPlan plan) { PrimaryGuard = guard; VolumeId = volume.Id; Display = volume.Display; this.plan = plan; }
        internal void Confirm() { if (Interlocked.CompareExchange(ref phase, 1, 0) != 0) throw new SafetyException("This manual review is no longer current."); }
        internal void Start() { if (Interlocked.CompareExchange(ref phase, 2, 1) != 1) throw new SafetyException("A fresh explicit manual eject confirmation is required."); }
        internal void RequireExecuting() { if (Interlocked.CompareExchange(ref phase, 2, 2) != 2) throw new SafetyException("This manual storage action ended. No further removal is authorized."); }
        internal void Close() { Interlocked.Exchange(ref phase, 3); }
    }
    // UI-owned admission/lifecycle. Actual filesystem/native permission still requires fresh worker guards.
    internal sealed class ConcurrentStorageState {
        readonly Operations operations; IConcurrentStorageGuard primary; TrayOperation owner;
        internal ManualStorageSession Active { get; private set; }
        internal bool Busy { get { return Active != null; } }
        internal bool HasPrimary { get { return primary != null && primary.AllowsUnrelatedOperations; } }
        internal Task WaitForManual { get { return Active == null ? Task.FromResult(true) : Active.Terminal.Task; } }
        internal ConcurrentStorageState(Operations operations) { this.operations = operations; }
        internal void Attach(TrayOperation token, IImportGuard guard) { owner = token; primary = token != null && (token.Kind == TrayOperationKind.Import || token.Kind == TrayOperationKind.Resume || token.Kind == TrayOperationKind.Verify || token.Kind == TrayOperationKind.Share) ? guard as IConcurrentStorageGuard : null; }
        internal void ClearPrimary() { owner = null; primary = null; }
        internal string Unavailable(Inventory state, long currentEpoch, string volumeId) {
            if (Busy) return "Another manual storage review or request is still running.";
            if (owner == null || !HasPrimary) return "Wait for the active operation or dialog to finish; continuous storage proof is unavailable.";
            if (state == null || !state.Complete || state.Epoch != currentEpoch) return "Refresh the storage list before reviewing another device.";
            try {
                var plan = EjectService.Plan(state, volumeId);
                if (plan.Disks.Intersect(primary.ProtectedDisks).Any() || !operations.Available(plan.Disks)) return "This device or a related disk is used by an active Easy Eject operation.";
                return null;
            } catch (SafetyException ex) { return ex.Message; }
        }
        internal ManualStorageSession Begin(Inventory state, long currentEpoch, string volumeId) {
            string refusal = Unavailable(state, currentEpoch, volumeId); if (refusal != null) throw new SafetyException(refusal);
            Active = new ManualStorageSession(primary, state.Volumes.Single(v => v.Id == volumeId), EjectService.Plan(state, volumeId)); return Active;
        }
        internal void Finish(ManualStorageSession session) { if (!Object.ReferenceEquals(Active, session)) return; Active = null; session.Close(); session.Terminal.TrySetResult(true); }
    }
    internal sealed class ConcurrentEjectService {
        readonly IDiscovery discovery; readonly Operations operations; readonly Func<Action, IEjectApi> api; readonly Func<string, string> volumeId;
        internal ConcurrentEjectService(IDiscovery discovery, Operations operations, Func<Action, IEjectApi> api, Func<string, string> volumeId) { this.discovery = discovery; this.operations = operations; this.api = api; this.volumeId = volumeId; }
        internal EjectResult Execute(ManualStorageSession session, MetadataCleanupPlan cleanup, CancellationToken cancel, CleanupCounts counts, Action<string> report) {
            cancel.ThrowIfCancellationRequested(); session.Start(); session.PrimaryGuard.Validate();
            var plan = session.Plan; var current = discovery.Scan(); EjectService.Validate(discovery, current, plan);
            if (cleanup != null && (cleanup.TargetId != plan.TargetId || cleanup.Fingerprint != plan.Fingerprint || cleanup.Roots.Any(root => root.VolumeId != session.VolumeId)))
                throw new SafetyException("Metadata cleanup does not belong to this exact reviewed manual storage action. No cleanup or removal was performed.");
            var proof = new StorageConnectionProof(discovery, current, plan.Disks);
            Func<Inventory> validate = () => { cancel.ThrowIfCancellationRequested(); session.RequireExecuting(); session.PrimaryGuard.Validate(); var fresh = discovery.Scan(); proof.Validate(fresh); return fresh; };
            var result = new EjectService(discovery, api(() => { validate(); }), operations).Execute(plan, initial => {
                if (cleanup == null) return; cleanup.Preflight(initial, volumeId);
                try { cleanup.Clean(validate, volumeId, cancel, counts, report); } catch (SafetyException) { if (cancel.IsCancellationRequested) throw new OperationCanceledException("Manual metadata cleanup cancelled; no removal request made.", cancel); throw; }
            }, cancel);
            if (cleanup != null) result.CleanupMessage = counts.Message; return result;
        }
    }
}
