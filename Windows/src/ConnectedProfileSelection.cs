using System;
using System.IO;

namespace EasyEject {
    public static partial class Profiles {
        internal static ImportProfile SaveConnectedSelection(ConnectedProfileReview reviewed, ImportProfile draft, Volume volume, Disk disk, long epoch, string sourceRoot, IDiscovery discovery, bool destinationChosen, Func<string, string> volumeId = null, Action<string> checkpoint = null) {
            if (reviewed == null || draft == null) throw new SafetyException("Review the selected device's profile choices first.");
            var value = draft.Copy(); ValidateStoredFolders(value);
            sourceRoot = SafePaths.Normalize(sourceRoot); string source = SafePaths.Normalize(Path.Combine(sourceRoot, value.RelativeSource)), destination = SafePaths.Normalize(value.Destination);
            if (!SafePaths.Within(source, sourceRoot)) throw new SafetyException("Choose a folder within the selected device volume.");
            using (var directories = SafePaths.HoldDirectories(sourceRoot, source, destination)) {
                directories.Validate(); var guard = new VolumeImportGuard(discovery, source, destination, volume, disk, epoch, volumeId ?? WindowsDiscovery.VolumeId); directories.Validate();
                if (!destinationChosen) ProfileDestination.Require(value, guard.DestinationIdentity, false);
                guard.Validate(); directories.Validate(); value.SavedDestination = guard.DestinationIdentity;
                SaveConnected(reviewed, value, reviewed.Root, stage => {
                    directories.Validate(); guard.Validate(); directories.Validate();
                    if (checkpoint != null) checkpoint(stage);
                    directories.Validate(); guard.Validate(); directories.Validate();
                });
                return value.Copy();
            }
        }
    }
}
