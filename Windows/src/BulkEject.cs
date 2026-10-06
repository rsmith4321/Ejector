using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace EasyEject {
    internal interface IBulkConnections { string Stamp(string[] instances); }
    internal sealed class WindowsBulkConnections : IBulkConnections {
        readonly WindowsDiscovery discovery;
        internal WindowsBulkConnections(WindowsDiscovery discovery) { this.discovery = discovery; }
        public string Stamp(string[] instances) { return discovery.Instances.Versions.Stamp(instances); }
    }
    internal sealed class BulkTarget {
        internal readonly string Id, Fingerprint, Connection; internal readonly uint Node;
        internal readonly int[] Disks; internal readonly string[] Affected, Volumes, Instances;
        internal BulkTarget(Inventory inventory, EjectPlan plan) {
            Id = plan.TargetId; Node = plan.TargetDevInst; Fingerprint = plan.Fingerprint; Connection = inventory.ConnectionStamp(Id);
            Disks = (int[])plan.Disks.Clone(); Affected = (string[])plan.Affected.Clone();
            Volumes = inventory.Volumes.Where(v => v.Disks.Any(Disks.Contains)).Select(v => v.Id).ToArray();
            Instances = WindowsDiscovery.GroupInstances(inventory, Id);
        }
    }
    internal sealed class BulkEjectPlan {
        internal readonly long Epoch; internal readonly BulkTarget[] Targets;
        internal BulkEjectPlan(long epoch, IEnumerable<BulkTarget> targets) { Epoch = epoch; Targets = targets.ToArray(); }
        internal string Description { get { return String.Join("\r\n\r\n", Targets.Select((t, index) => "Device " + (index + 1) + ":\r\n" + String.Join("\r\n", t.Affected))); } }
    }
    internal static class BulkEjectPolicy {
        internal static BulkEjectPlan Plan(Inventory state) {
            if (state == null || !state.Complete) throw new SafetyException("Device inventory is incomplete. Refresh before ejecting cards.");
            var targets = new List<BulkTarget>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in state.Volumes.Where(v => v.IsCard).OrderBy(v => v.Id, StringComparer.OrdinalIgnoreCase)) {
                var plan = EjectService.Plan(state, card.Id);
                if (!seen.Add(plan.TargetId)) continue;
                var siblings = state.Volumes.Where(v => v.Disks.Any(plan.Disks.Contains)).ToArray();
                if (siblings.Any(v => !v.IsCard) || plan.Disks.Any(n => !siblings.Any(v => v.Disks.Contains(n))))
                    throw new SafetyException("A card shares its removal device with another or unclassified volume. Review all partitions and eject that device individually.");
                if (String.IsNullOrEmpty(state.ConnectionStamp(plan.TargetId))) throw new SafetyException("Per-device connection tracking is unavailable. Eject each device individually after reviewing it.");
                targets.Add(new BulkTarget(state, plan));
            }
            // Overlapping ancestor removal targets must never be requested twice.
            if (targets.SelectMany(t => t.Disks).Distinct().Count() != targets.Sum(t => t.Disks.Length)) throw new SafetyException("Card removal groups overlap. Review and eject each device individually.");
            return new BulkEjectPlan(state.Epoch, targets);
        }
        internal static void Validate(Inventory state, IEnumerable<BulkTarget> targets) {
            if (!state.Complete) throw new SafetyException("Device inventory changed or is incomplete. Refresh and review remaining cards.");
            foreach (var t in targets) {
                if (state.GroupFingerprint(t.Id) != t.Fingerprint || state.ConnectionStamp(t.Id) != t.Connection ||
                    state.Volumes.Where(v => v.Disks.Any(t.Disks.Contains)).Any(v => !v.IsCard))
                    throw new SafetyException("A confirmed card connection, identity, classification or removal group changed. Refresh and review remaining cards.");
            }
        }
    }
    internal sealed class BulkEjectResult {
        internal int Requested, Confirmed; internal bool Success; internal string Detail = "";
        internal readonly List<string> SafeDevices = new List<string>();
        internal string Summary { get { return (Success ? "Finished. " : "Stopped; remaining devices were not reported safe. ") + Confirmed + " of " + Requested + " confirmed devices safely removed."; } }
        internal string Message { get {
            if (Requested == 0 && Success) return "No camera or emulator cards are connected.";
            string text = Summary;
            if (SafeDevices.Count > 0) text += "\r\n\r\nSafe to unplug:\r\n" + String.Join("\r\n", SafeDevices);
            return text + "\r\n\r\n" + (Success ? "All confirmed card volumes are absent." : "Reason: " + Detail);
        } }
    }
    internal sealed class BulkEjectService {
        readonly IDiscovery discovery; readonly IBulkConnections connections; readonly Operations operations;
        readonly Func<Action, IEjectApi> api; readonly Action wait; readonly int checks;
        internal BulkEjectService(IDiscovery discovery, IBulkConnections connections, Operations operations, Func<Action, IEjectApi> api)
            : this(discovery, connections, operations, api, () => Thread.Sleep(100), 30) {}
        internal BulkEjectService(IDiscovery discovery, IBulkConnections connections, Operations operations, Func<Action, IEjectApi> api, Action wait, int checks) {
            this.discovery = discovery; this.connections = connections; this.operations = operations; this.api = api; this.wait = wait; this.checks = checks;
        }
        void ValidateConnections(IEnumerable<BulkTarget> remaining) {
            foreach (var target in remaining) if (connections.Stamp(target.Instances) != target.Connection)
                throw new SafetyException("A confirmed device disconnected, restarted or lost connection tracking. Review remaining cards before another request.");
        }
        internal BulkEjectResult Execute(BulkEjectPlan plan) {
            var result = new BulkEjectResult { Requested = plan.Targets.Length };
            try {
                using (operations.Reserve(plan.Targets.SelectMany(t => t.Disks))) {
                    var initial = discovery.Scan();
                    if (initial.Epoch != plan.Epoch || discovery.Epoch != plan.Epoch) throw new SafetyException("Connections changed after confirmation. Refresh and review the cards.");
                    BulkEjectPolicy.Validate(initial, plan.Targets); ValidateConnections(plan.Targets);
                    for (int index = 0; index < plan.Targets.Length; index++) {
                        var remaining = plan.Targets.Skip(index).ToArray(); var current = discovery.Scan();
                        BulkEjectPolicy.Validate(current, remaining); ValidateConnections(remaining);
                        var target = plan.Targets[index];
                        var accepted = api(() => ValidateConnections(remaining)).Request(target.Node, target.Id);
                        if (!accepted.Success) throw new SafetyException(accepted.Message);
                        bool absent = false;
                        for (int attempt = 0; attempt < checks; attempt++) {
                            var after = discovery.Scan();
                            if (after.Complete && after.GroupFingerprint(target.Id) == "" && !after.Volumes.Any(v => target.Volumes.Contains(v.Id))) { absent = true; break; }
                            wait();
                        }
                        if (!absent) throw new SafetyException("Windows accepted removal, but the confirmed volumes could not be verified absent. Check Windows Safely Remove Hardware before unplugging that device.");
                        result.Confirmed++; result.SafeDevices.Add(String.Join("; ", target.Affected));
                        // A later card failure cannot conceal earlier confirmed removals.
                    }
                    result.Success = true;
                }
            } catch (Exception ex) { result.Detail = ex.Message; }
            return result;
        }
    }
}
