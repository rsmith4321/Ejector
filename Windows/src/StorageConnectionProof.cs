using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace EasyEject {
    // A live instance stamp is required before a global event may be treated as unrelated.
    internal interface IScopedConnectionDiscovery { string Stamp(string[] instances); }
    internal sealed class StorageConnectionProof {
        sealed class Group {
            internal string Id, Stamp, Fingerprint; internal string[] Instances;
        }
        readonly IDiscovery discovery; readonly IScopedConnectionDiscovery connections;
        readonly long originalEpoch; long checkedEpoch;
        readonly int[] disks; readonly string fingerprint; readonly Group[] groups; readonly bool scoped;
        internal bool Scoped { get { return scoped; } }
        internal bool ProtectedConnectionChanged { get { return Scoped && groups.Any(group => connections.Stamp(group.Instances) != group.Stamp); } }
        internal bool Changed {
            get { return discovery.Epoch != Interlocked.Read(ref checkedEpoch) || ProtectedConnectionChanged; }
        }
        internal StorageConnectionProof(IDiscovery discovery, Inventory state, IEnumerable<int> protectedDisks) {
            this.discovery = discovery; connections = discovery as IScopedConnectionDiscovery;
            originalEpoch = checkedEpoch = state.Epoch; disks = protectedDisks.Distinct().OrderBy(x => x).ToArray();
            if (!state.Complete || disks.Length == 0 || state.Disks.Count(d => disks.Contains(d.Number)) != disks.Length || state.Epoch != discovery.Epoch)
                throw ChangedConnection();
            fingerprint = Fingerprint(state);
            var captured = new List<Group>(); bool allTracked = connections != null;
            foreach (string id in state.Disks.Where(d => disks.Contains(d.Number)).Select(d => d.TargetId == "" ? d.InstanceId : d.TargetId).Distinct(StringComparer.OrdinalIgnoreCase)) {
                string stamp = state.ConnectionStamp(id);
                if (String.IsNullOrEmpty(id)) throw ChangedConnection();
                if (String.IsNullOrEmpty(stamp)) allTracked = false;
                string[] instances = WindowsDiscovery.GroupInstances(state, id);
                if (instances.Length == 0 || state.Disks.Where(d => disks.Contains(d.Number) && (d.TargetId == "" ? d.InstanceId : d.TargetId).Equals(id, StringComparison.OrdinalIgnoreCase)).Any(d => !instances.Contains(d.InstanceId, StringComparer.OrdinalIgnoreCase))) throw ChangedConnection();
                captured.Add(new Group { Id = id, Stamp = stamp, Fingerprint = state.GroupFingerprint(id), Instances = instances });
            }
            groups = captured.ToArray(); scoped = allTracked && groups.Length != 0;
            Validate(state);
        }
        string Fingerprint(Inventory state) {
            return String.Join("\n", state.Disks.Where(d => disks.Contains(d.Number)).Select(d => "D:" + d.Fingerprint + "|" + d.Bus + "|" + d.Removable + "|" + d.Hotplug)
                .Concat(state.Volumes.Where(v => v.Disks.Any(disks.Contains)).Select(v => "V:" + v.Fingerprint)).OrderBy(x => x, StringComparer.Ordinal));
        }
        internal void Validate(Inventory state) {
            if (!state.Complete || state.Epoch != discovery.Epoch || state.Disks.Count(d => disks.Contains(d.Number)) != disks.Length || Fingerprint(state) != fingerprint)
                throw ChangedConnection();
            foreach (var group in groups) {
                if (state.GroupFingerprint(group.Id) != group.Fingerprint ||
                    !WindowsDiscovery.GroupInstances(state, group.Id).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).SequenceEqual(group.Instances.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)) throw ChangedConnection();
                if (Scoped && (state.ConnectionStamp(group.Id) != group.Stamp || connections.Stamp(group.Instances) != group.Stamp)) throw ChangedConnection();
            }
            if (!Scoped && state.Epoch != originalEpoch) throw ChangedConnection();
            // Events during this check are not acknowledged. The next fast check must validate them.
            if (state.Epoch != discovery.Epoch) throw ChangedConnection();
            Interlocked.Exchange(ref checkedEpoch, state.Epoch);
        }
        static SafetyException ChangedConnection() { return new SafetyException("A protected storage connection, sibling volume or identity changed, or connection proof is unavailable. Remaining originals and saved copies are kept."); }
    }
}
