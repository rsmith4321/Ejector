using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    // Process-only authority, captured before copying. Never serialized or restored from history.
    internal sealed class ImportCompletionOrigin {
        internal readonly string VolumeId, SourceRoot, VolumeFingerprint, DiskFingerprint, BlockedReason;
        internal readonly long Epoch;
        internal readonly bool SourceIsCard;
        readonly EjectPlan plan;
        internal EjectPlan Plan { get { return CopyPlan(plan); } }
        internal ImportCompletionOrigin(Inventory state, string volumeId, string sourceRoot) {
            var volume = state.Volumes.SingleOrDefault(x => x.Id == volumeId);
            if (!state.Complete || volume == null || volume.Error != "" || volume.System || volume.Disks.Count != 1 || String.IsNullOrWhiteSpace(volume.Serial)) throw new SafetyException("Import source identity is unavailable for completion actions.");
            var disk = state.Disks.SingleOrDefault(x => x.Number == volume.Disks[0]);
            if (disk == null || disk.Error != "" || disk.InstanceId == "") throw new SafetyException("Import source physical identity is unavailable.");
            SourceRoot = SafePaths.Normalize(sourceRoot);
            if (!volume.Mounts.Any(x => SafePaths.Normalize(x).Equals(SourceRoot, StringComparison.OrdinalIgnoreCase))) throw new SafetyException("Completion source is not the selected volume root.");
            Epoch = state.Epoch; SourceIsCard = volume.IsCard; VolumeId = volumeId; VolumeFingerprint = volume.Fingerprint; DiskFingerprint = disk.Fingerprint;
            try { plan = CopyPlan(EjectService.Plan(state, volumeId)); BlockedReason = ""; }
            catch (SafetyException ex) { BlockedReason = ex.Message; }
        }
        internal bool Matches(Inventory state, string currentVolumeId, string currentRoot) {
            if (!state.Complete || state.Epoch != Epoch || currentVolumeId != VolumeId || currentRoot == null || !SourceRoot.Equals(currentRoot, StringComparison.OrdinalIgnoreCase)) return false;
            var volume = state.Volumes.SingleOrDefault(x => x.Id == VolumeId);
            if (volume == null || volume.Disks.Count != 1 || volume.Fingerprint != VolumeFingerprint) return false;
            var disk = state.Disks.SingleOrDefault(x => x.Number == volume.Disks[0]);
            return disk != null && disk.Fingerprint == DiskFingerprint && plan != null && state.GroupFingerprint(plan.TargetId) == plan.Fingerprint;
        }
        static EjectPlan CopyPlan(EjectPlan value) {
            return value == null ? null : new EjectPlan { TargetId = value.TargetId, TargetDevInst = value.TargetDevInst, Epoch = value.Epoch, Fingerprint = value.Fingerprint, Disks = (int[])value.Disks.Clone(), Affected = (string[])value.Affected.Clone() };
        }
    }
    internal sealed class ImportCompletion {
        internal readonly string Id, DeviceName, Folder, DestinationRoot, Message;
        internal readonly bool HasMedia;
        internal readonly ImportCompletionOrigin Origin;
        readonly DestinationIdentity destination;
        readonly CompletedBatch batch;
        internal CompletedBatch Batch { get { return batch == null ? null : ImportHistory.Clone(batch); } }
        internal DestinationIdentity Destination { get { return destination.Copy(); } }
        internal ImportCompletion(ImportCompletionOrigin origin, string deviceName, string folder, string destinationRoot, DestinationIdentity destination, bool hasMedia, string message, CompletedBatch completedBatch = null) {
            Id = Guid.NewGuid().ToString("N"); Origin = origin; DeviceName = deviceName; Folder = SafePaths.Normalize(folder); DestinationRoot = SafePaths.Normalize(destinationRoot);
            if (!SafePaths.Within(Folder, DestinationRoot)) throw new SafetyException("Completion folder escaped its destination.");
            destination.Validate(); this.destination = destination.Copy(); HasMedia = hasMedia; Message = message;
            if (completedBatch != null) {
                ImportHistory.Validate(completedBatch);
                if (!hasMedia || completedBatch.ProfileId != origin.VolumeId || !completedBatch.Folder.Equals(Folder, StringComparison.OrdinalIgnoreCase) || !completedBatch.DestinationRoot.Equals(DestinationRoot, StringComparison.OrdinalIgnoreCase) || !completedBatch.Destination.Matches(destination)) throw new SafetyException("The sharing batch does not belong to this exact import completion.");
                batch = ImportHistory.Clone(completedBatch);
            }
        }
    }
    internal sealed class ImportCompletionRegistry {
        readonly object gate = new object(); readonly Dictionary<string, ImportCompletion> entries = new Dictionary<string, ImportCompletion>();
        internal ImportCompletion[] Entries { get { lock (gate) return entries.Values.OrderBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase).ToArray(); } }
        internal string[] Insert(ImportCompletion value) { lock (gate) { var removed = InvalidateSource(value.Origin.SourceRoot); entries.Add(value.Id, value); return removed; } }
        internal ImportCompletion Get(string id) { lock (gate) { ImportCompletion entry; return id != null && entries.TryGetValue(id, out entry) ? entry : null; } }
        internal string[] InvalidateSource(string root) { lock (gate) { var ids = entries.Values.Where(x => x.Origin.SourceRoot.Equals(root, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToArray(); foreach (string id in ids) entries.Remove(id); return ids; } }
        internal string[] InvalidateAll() { lock (gate) { var ids = entries.Keys.ToArray(); entries.Clear(); return ids; } }
        internal bool Matches(string id, Inventory state, string currentVolumeId, string currentRoot) { lock (gate) { var entry = Get(id); return entry != null && entry.Origin.Matches(state, currentVolumeId, currentRoot); } }
        internal void Consume(string id, Inventory state, string currentVolumeId, string currentRoot) {
            lock (gate) { if (!Matches(id, state, currentVolumeId, currentRoot)) throw new SafetyException("This import eject choice expired. Refresh and review the connected device."); entries.Remove(id); }
        }
    }
    internal sealed class CompletedEjectService {
        readonly ImportCompletionRegistry registry; readonly IDiscovery discovery; readonly IEjectApi api; readonly Operations operations; readonly Func<string, string> resolveVolume;
        internal CompletedEjectService(ImportCompletionRegistry registry, IDiscovery discovery, IEjectApi api, Operations operations, Func<string, string> resolveVolume) {
            this.registry = registry; this.discovery = discovery; this.api = api; this.operations = operations; this.resolveVolume = resolveVolume;
        }
        internal EjectResult Execute(string id) {
            return Execute(id, state => {});
        }
        internal EjectResult Execute(string id, Action<Inventory> beforeRequest, CancellationToken cancellation = default(CancellationToken)) {
            var entry = registry.Get(id);
            if (entry == null || entry.Origin.Plan == null) throw new SafetyException("No current import eject choice exists. Review the connected device.");
            return new EjectService(discovery, api, operations).Execute(entry.Origin.Plan, state => { registry.Consume(id, state, resolveVolume(entry.Origin.SourceRoot), entry.Origin.SourceRoot); beforeRequest(state); }, cancellation);
        }
    }
    internal static class CompletionFolder {
        internal static void Open(ImportCompletion entry, DestinationIdentity current, IImportGuard guard, Operations operations, Action<string> open) {
            if (!entry.Destination.Matches(current)) throw new SafetyException("The import destination changed. The folder was not opened.");
            using (operations.Reserve(guard.ProtectedDisks)) using (SafePaths.LockDirectories(entry.DestinationRoot, entry.Folder)) { guard.Validate(); open(entry.Folder); }
        }
    }
}
