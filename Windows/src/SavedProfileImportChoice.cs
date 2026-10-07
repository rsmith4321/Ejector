using System;
using System.Linq;
using System.Threading;

namespace EasyEject {
    // One explicit catalog action. Saved records and names alone never authorize
    // work: the selected record, current connection and held folders are reviewed.
    internal sealed class SavedProfileImportChoice {
        readonly SavedProfile entry; int consumed;
        internal readonly AutomaticConnection Connection;
        SavedProfileImportChoice(SavedProfile entry, AutomaticConnection connection) { this.entry = entry; Connection = connection; }
        internal static SavedProfileImportChoice Capture(SavedProfile entry, Inventory state, long observedEpoch) {
            if (entry == null || !entry.CanEdit) throw new SafetyException("Choose a valid saved profile. Records needing setup cannot start an import.");
            if (state == null || !state.Complete || state.Epoch != observedEpoch) throw new SafetyException("Refresh connected storage before importing from this saved profile.");
            var profile = entry.Profile; var volumes = state.Volumes.Where(v => v.Id == profile.VolumeId).ToArray();
            var connection = volumes.Length == 1 ? AutomaticConnection.Capture(state, volumes[0]) : null;
            if (connection == null || connection.Volume.Serial != profile.VolumeSerial || connection.Disk.InstanceId != profile.DiskInstance)
                throw new SafetyException("Connect the original device for " + ProfileName.Display(profile) + ". Formatting or changed device identity requires setup again. No import started.");
            return new SavedProfileImportChoice(entry, connection);
        }
        void RequireCurrent(IDiscovery discovery) {
            var state = discovery.Scan();
            if (state.Epoch != Connection.Epoch || state.Epoch != discovery.Epoch) throw new SafetyException("Storage changed after selecting this saved profile. Refresh and choose Import now again.");
            Connection.RequireCurrent(state);
        }
        ConnectedProfileReview ReviewRecord(string configurationRoot, Action<string> checkpoint) {
            var reviewed = Profiles.ReviewConnected(Connection.Volume, Connection.Disk, configurationRoot, checkpoint);
            if (reviewed.Value == null || reviewed.FileName != entry.FileName || reviewed.Hash != entry.Hash || !VerifiedImporter.SameStamp(entry.Stamp, reviewed.Stamp))
                throw new SafetyException("This saved profile changed. Refresh Saved profiles and review its current choices before importing.");
            return reviewed;
        }
        internal ImportOperationReview Prepare(IDiscovery discovery, ImportDefaults defaults, string configurationRoot,
            Func<string, string> volumeId = null, Action<string> checkpoint = null) {
            if (Interlocked.CompareExchange(ref consumed, 1, 0) != 0) throw new SafetyException("This saved-profile import choice was already used. Choose Import now again for a fresh review.");
            RequireCurrent(discovery); ImportOperationReview result = null;
            using (var directories = SafePaths.HoldDirectories(configurationRoot)) {
                try {
                    var reviewed = ReviewRecord(configurationRoot, checkpoint);
                    directories.Validate(); RequireCurrent(discovery);
                    result = ImportOperationReview.Manual(Connection, reviewed, defaults, discovery, volumeId);
                    if (checkpoint != null) checkpoint("Before saved-profile import handoff");
                    directories.Validate(); ReviewRecord(configurationRoot, checkpoint); directories.Validate(); RequireCurrent(discovery); result.ValidateForPresentation();
                    return result;
                } catch { if (result != null) result.Dispose(); throw; }
            }
        }
    }
}
