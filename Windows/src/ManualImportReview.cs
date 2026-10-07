using System;
using System.IO;
using System.Threading;

namespace EasyEject {
    // Retain selected folders from device review through confirmation, optional
    // legacy binding and copying. Release them before completion/eject choices.
    internal sealed class ManualImportReview : IDisposable {
        readonly string root, source, destination;
        SafePaths.DirectoryLease directories;
        ImportMediaFolder mediaFolder;
        internal readonly VolumeImportGuard Guard;
        internal ManualImportReview(string root, string source, string destination, Func<string, string, VolumeImportGuard> makeGuard) {
            this.root = SafePaths.Normalize(root); this.source = SafePaths.Normalize(source); this.destination = SafePaths.Normalize(destination);
            if (!SafePaths.Within(this.source, this.root)) throw new SafetyException("Choose a folder within the selected device volume.");
            try {
                directories = SafePaths.HoldDirectories(this.root, this.destination);
                mediaFolder = new ImportMediaFolder(this.root, this.source);
                ValidatePaths(); Guard = makeGuard(this.source, this.destination); ValidatePaths();
                if (Guard == null) throw new SafetyException("The selected import devices could not be reviewed.");
            } catch { Dispose(); throw; }
        }
        void ValidatePaths() { directories.Validate(); mediaFolder.ValidateSelection(); }
        internal void Validate() { ValidatePaths(); Guard.Validate(); ValidatePaths(); }
        internal ImportProfile BindLegacy(ConnectedProfileReview reviewed, Action<string> checkpoint = null) {
            var profile = reviewed == null ? null : reviewed.Value;
            if (profile == null || !SafePaths.Normalize(Path.Combine(root, profile.RelativeSource)).Equals(source, StringComparison.OrdinalIgnoreCase) || !SafePaths.Normalize(profile.Destination).Equals(destination, StringComparison.OrdinalIgnoreCase))
                throw new SafetyException("The reviewed profile folders do not match this manual import choice.");
            Validate(); return Profiles.BindConnectedDestination(reviewed, Guard.DestinationIdentity, reviewed.Root, stage => {
                Validate(); if (checkpoint != null) checkpoint(stage); Validate();
            });
        }
        internal ImportResult Run(VerifiedImporter importer, string volumeId, MediaOptions options, CancellationToken cancel, Action<string> report, Action<ImportWorkProgress> progress = null) {
            cancel.ThrowIfCancellationRequested(); Validate();
            // A durable receipt is completed evidence, independent of a later
            // source/cancellation event in its terminal display callback.
            return importer.Run(root, source, destination, volumeId, Guard, cancel, report, options, progress);
        }
        public void Dispose() { if (mediaFolder != null) mediaFolder.Dispose(); if (directories != null) directories.Dispose(); mediaFolder = null; directories = null; }
    }
}
