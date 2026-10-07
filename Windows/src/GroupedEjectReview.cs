using System;
using System.Linq;
using System.Threading;

namespace EasyEject {
    internal sealed class GroupedEjectReview {
        readonly BulkEjectPlan plan; readonly MetadataCleanupPlan[] cleanup;
        readonly IDiscovery discovery; readonly Operations operations; readonly IBulkConnections connections;
        readonly Func<Action, IEjectApi> api; readonly Func<string, string> volumeId;
        int phase;
        internal readonly string Description;
        internal int Count { get { return plan.Targets.Length; } }
        internal int[] Disks { get { return plan.Targets.SelectMany(t => t.Disks).ToArray(); } }
        internal bool Current { get { return Volatile.Read(ref phase) == 0 && discovery.Epoch == plan.Epoch; } }
        internal bool HasCleanup { get { return cleanup.Length != 0; } }
        internal GroupedEjectReview(Inventory state, AppSettings settings, IDiscovery discovery, Operations operations, IBulkConnections connections,
            Func<Action, IEjectApi> api, Func<string, string> volumeId) {
            this.discovery = discovery; this.operations = operations; this.connections = connections; this.api = api; this.volumeId = volumeId;
            plan = BulkEjectPolicy.Plan(state);
            if (state.Epoch != discovery.Epoch) throw new SafetyException("Connections changed. Refresh and review the cards.");
            cleanup = settings.CleanupCardsEnabled ? plan.Targets.Select(t => MetadataCleanupPlan.Bulk(state, t)).ToArray() : new MetadataCleanupPlan[0];
            Description = "Request Windows safe removal for these " + Count + " camera/emulator devices and all listed volumes?\n\n" + plan.Description
                + (HasCleanup ? "\n\n" + String.Join("\n\n", cleanup.Select(p => p.Description)) : "")
                + "\n\nFinish all transfers first. Requests run one device at a time and stop on a veto or identity change. Wait for verified removal before unplugging.";
        }
        internal void Confirm() { if (!Current || Interlocked.CompareExchange(ref phase, 1, 0) != 0) throw new SafetyException("This grouped removal review expired. Refresh and review remaining cards."); }
        internal void Cancel() {
            int current;
            do { current = Volatile.Read(ref phase); if (current == 2) throw new InvalidOperationException("Grouped removal is still running."); }
            while (Interlocked.CompareExchange(ref phase, 3, current) != current);
        }
        internal BulkEjectResult Execute(CancellationToken cancel, CleanupCounts counts, Action<string> report, Action wait = null, int checks = 30) {
            if (Interlocked.CompareExchange(ref phase, 2, 1) != 1) throw new SafetyException("A fresh confirmed grouped removal review is required.");
            try {
                var service = wait == null ? new BulkEjectService(discovery, connections, operations, api) : new BulkEjectService(discovery, connections, operations, api, wait, checks);
                var result = service.Execute(plan, state => { foreach (var p in cleanup) p.Preflight(state, volumeId); }, (target, validate) => {
                    report("Reviewing safe removal: " + String.Join("; ", target.Affected));
                    if (HasCleanup) cleanup.Single(p => p.TargetId == target.Id).Clean(validate, volumeId, cancel, counts, report);
                }, cancel);
                if (HasCleanup) result.CleanupMessage = counts.Message;
                return result;
            } finally { Interlocked.Exchange(ref phase, 3); }
        }
    }
}
