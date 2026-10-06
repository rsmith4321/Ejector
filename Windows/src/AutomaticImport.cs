using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyEject {
    internal static class ProfileDestination {
        internal static bool Same(DestinationIdentity a, DestinationIdentity b) { return a == null ? b == null : a.Matches(b); }
        internal static void Require(ImportProfile profile, DestinationIdentity current, bool automatic) {
            if (profile.SavedDestination == null) {
                if (automatic) throw new SafetyException("Automatic import needs a verified saved destination. Connect the original device and save its profile, or explicitly choose its destination again in Saved profiles. No import started.");
            } else if (!profile.SavedDestination.Matches(current)) throw new SafetyException("The profile's selected destination disk is unavailable or changed. Choose the destination again in its profile. No import started.");
            OfflineProfileDestination.Validate(profile, current);
        }
    }
    internal sealed class AutomaticConnection {
        internal readonly string VolumeId, Token, Root;
        internal readonly Volume Volume; internal readonly Disk Disk;
        internal readonly long Epoch;
        AutomaticConnection(Inventory state, Volume volume, Disk disk, string root, string token) {
            VolumeId = volume.Id; Root = root; Token = token; Epoch = state.Epoch;
            Volume = new Volume { Id = volume.Id, Label = volume.Label, Serial = volume.Serial, Error = volume.Error, Extents = volume.Extents, System = volume.System, Group = volume.Group, Classification = volume.Classification, Mounts = new List<string>(volume.Mounts), Disks = new List<int>(volume.Disks) };
            Disk = new Disk { Number = disk.Number, DevInst = disk.DevInst, InstanceId = disk.InstanceId, Model = disk.Model, Serial = disk.Serial, TargetId = disk.TargetId, TargetDevInst = disk.TargetDevInst, Bus = disk.Bus, Removable = disk.Removable, Hotplug = disk.Hotplug, Error = disk.Error, Ancestors = new List<string>(disk.Ancestors) };
        }
        internal static AutomaticConnection Capture(Inventory state, Volume volume) {
            if (state == null || !state.Complete || volume.System || volume.Error != "" || volume.Disks.Count != 1 || String.IsNullOrWhiteSpace(volume.Id) || String.IsNullOrWhiteSpace(volume.Serial)) return null;
            var disks = state.Disks.Where(x => x.Number == volume.Disks[0]).ToArray();
            if (disks.Length != 1 || disks[0].Error != "" || !Classifier.External(disks[0]) || String.IsNullOrWhiteSpace(disks[0].InstanceId)) return null;
            var disk = disks[0]; string target = disk.TargetId == "" ? disk.InstanceId : disk.TargetId;
            string stamp = state.ConnectionStamp(target);
            if (String.IsNullOrEmpty(stamp)) return null;
            string root = volume.Mounts.Where(x => x.Length >= 3 && Char.IsLetter(x[0]) && x[1] == ':' && x[2] == '\\').OrderBy(x => x.Length).ThenBy(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (root == null) return null; root = SafePaths.Normalize(root);
            return new AutomaticConnection(state, volume, disk, root, volume.Fingerprint + "\n" + disk.Fingerprint + "|" + disk.Bus + "|" + disk.Removable + "|" + disk.Hotplug + "\n" + state.GroupFingerprint(target) + "\n" + stamp);
        }
        internal AutomaticConnection RequireCurrent(Inventory state) {
            var volumes = state.Volumes.Where(x => x.Id == VolumeId).ToArray(); var current = volumes.Length != 1 ? null : Capture(state, volumes[0]);
            if (current == null || current.Token != Token) throw new SafetyException("Automatic import source connection or related storage changed before work started. Reconnect the source or choose a fresh manual import.");
            return current;
        }
    }
    internal sealed class AutomaticImportRequest {
        readonly ImportProfile profile;
        internal ImportProfile Profile { get { return profile.Copy(); } }
        internal readonly AutomaticConnection Connection; internal readonly MediaOptions Options;
        internal AutomaticImportRequest(AutomaticConnection connection, ImportProfile profile, ImportDefaults defaults) { Connection = connection; this.profile = profile.Copy(); Options = profile.Resolve(defaults); }
        internal void ValidateAdmission(Inventory current, DestinationIdentity destination) {
            Connection.RequireCurrent(current);
            if (profile.VolumeId != Connection.Volume.Id || profile.VolumeSerial != Connection.Volume.Serial || profile.DiskInstance != Connection.Disk.InstanceId) throw new SafetyException("The automatic profile no longer matches this card. Connect the original card or enroll the formatted device again. No import started.");
            Profiles.ValidateStoredFolders(profile);
            ProfileDestination.Require(profile, destination, true);
        }
    }
    // UI-thread scheduler. An admitted attempt is consumed before preflight, so failure/cancel
    // cannot loop into another copy/removal. Only preflight waits may retry after a storage event.
    internal sealed class AutomaticImportScheduler {
        sealed class Attempt { internal string Token; internal long? WaitingEpoch; }
        readonly Dictionary<string, Attempt> attempts = new Dictionary<string, Attempt>(StringComparer.OrdinalIgnoreCase);
        bool suppressNext;
        internal void SettingsSaved() { suppressNext = true; }
        internal void Observe(Inventory state) {
            if (state == null || !state.Complete) return;
            var ids = new HashSet<string>(state.Volumes.Select(v => v.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var id in attempts.Keys.Where(id => !ids.Contains(id)).ToArray()) attempts.Remove(id);
            if (!suppressNext) return;
            foreach (var v in state.Volumes) { var c = AutomaticConnection.Capture(state, v); if (c != null) attempts[v.Id] = new Attempt { Token = c.Token }; }
            suppressNext = false;
        }
        internal void MarkManual(Inventory state, Volume volume) {
            var c = AutomaticConnection.Capture(state, volume); if (c != null) attempts[volume.Id] = new Attempt { Token = c.Token };
        }
        internal AutomaticImportRequest Take(Inventory state, IEnumerable<ImportProfile> profiles, ImportDefaults defaults, bool busy) {
            if (busy || state == null || !state.Complete) return null;
            Observe(state);
            foreach (var profile in profiles.OrderBy(p => ProfileName.Display(p), StringComparer.CurrentCultureIgnoreCase)) {
                if (!profile.AutomaticEnabled(defaults)) continue;
                var volumes = state.Volumes.Where(x => x.Id == profile.VolumeId).ToArray(); if (volumes.Length != 1) continue;
                var c = AutomaticConnection.Capture(state, volumes[0]); if (c == null) continue;
                Attempt prior;
                if (attempts.TryGetValue(profile.VolumeId, out prior) && prior.Token == c.Token && (!prior.WaitingEpoch.HasValue || prior.WaitingEpoch.Value == state.Epoch)) continue;
                attempts[profile.VolumeId] = new Attempt { Token = c.Token };
                return new AutomaticImportRequest(c, profile, defaults);
            }
            return null;
        }
        internal void PreflightWaiting(AutomaticImportRequest request, long? observedEpoch = null) {
            Attempt current; if (attempts.TryGetValue(request.Connection.VolumeId, out current) && current.Token == request.Connection.Token) current.WaitingEpoch = observedEpoch ?? request.Connection.Epoch;
        }
    }
    internal static class AutomaticImportAdmission {
        internal static VolumeImportGuard Bind(AutomaticImportRequest request, IDiscovery discovery, string source, string destination, Action<long> observed = null, Func<string, string> volumeId = null) {
            var profile = request.Profile; var connection = request.Connection; Profiles.ValidateStoredFolders(profile);
            string expectedSource = SafePaths.Normalize(Path.Combine(connection.Root, profile.RelativeSource));
            if (!SafePaths.Within(expectedSource, connection.Root) || !SafePaths.Normalize(source).Equals(expectedSource, StringComparison.OrdinalIgnoreCase) || !SafePaths.Normalize(destination).Equals(SafePaths.Normalize(profile.Destination), StringComparison.OrdinalIgnoreCase))
                throw new SafetyException("Automatic import folders do not match the frozen device profile. No import started.");
            if (observed != null) observed(discovery.Epoch);
            var guard = new VolumeImportGuard(discovery, source, destination, connection.Volume, connection.Disk, connection.Epoch, volumeId ?? WindowsDiscovery.VolumeId, state => {
                if (observed != null) observed(state.Epoch); connection.RequireCurrent(state);
                if (state.Epoch != connection.Epoch) {
                    string target = connection.Disk.TargetId;
                    var numbers = state.Disks.Where(d => d.Number == connection.Disk.Number || target != "" && (d.InstanceId == target || d.Ancestors.Contains(target))).Select(d => d.Number);
                    if (!new StorageConnectionProof(discovery, state, numbers).Scoped) throw new SafetyException("Continuous source connection proof is unavailable. Review a fresh import choice.");
                }
                return true;
            });
            var fresh = discovery.Scan(); if (observed != null) observed(fresh.Epoch);
            request.ValidateAdmission(fresh, guard.DestinationIdentity); guard.Validate(); return guard;
        }
    }
}
