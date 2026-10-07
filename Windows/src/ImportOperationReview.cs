using System;
using System.IO;
using System.Threading;

namespace EasyEject {
    // Holds reviewed directories through confirmation and copying. Profile/default
    // changes cannot alter this operation, and cancelled reviews cannot be replayed.
    internal sealed class ImportOperationReview : IDisposable {
        readonly ManualImportReview selection; readonly ConnectedProfileReview reviewed;
        ImportProfile profile; int phase;
        internal readonly string Root, Source, Destination, Description;
        internal readonly MediaOptions Options; internal readonly bool Automatic;
        internal bool EngineStarted { get; private set; }
        internal VolumeImportGuard Guard { get { return selection.Guard; } }
        internal ImportProfile Profile { get { return profile.Copy(); } }
        internal bool Current { get { return Volatile.Read(ref phase) == 0 && !Guard.Changed; } }
        ImportOperationReview(ImportProfile profile, MediaOptions options, string root, ManualImportReview selection, ConnectedProfileReview reviewed, bool automatic) {
            this.profile = profile.Copy(); Options = options; Root = SafePaths.Normalize(root);
            Source = SafePaths.Normalize(Path.Combine(Root, profile.RelativeSource)); Destination = SafePaths.Normalize(profile.Destination);
            this.selection = selection; this.reviewed = reviewed; Automatic = automatic; phase = automatic ? 1 : 0;
            ProfileDestination.Require(profile, Guard.DestinationIdentity, automatic);
            Description = (options.VideosOnly ? "Copy selected videos and required companions from:\n" : "Copy all media and sidecars from:\n") + Source + "\n\nInto dated folders under:\n" + Destination
                + "\n\n" + (options.IncludePreviews ? "Camera previews included." : "Optional previews stay on the device; required package files are included.")
                + "\n" + (options.CleanLayout ? "Ordinary media uses clean layout; camera packages and unfamiliar media retain their tree." : "Media folder paths are preserved.")
                + "\nOriginal filenames are retained. New and reused copies are verified with SHA-256.\n\n" + OriginalRemovalChoice.BeforeImport(options)
                + "\n\n" + DeviceTrashChoice.BeforeImport(options) + "\n\nEject and sharing require a separate manual choice. Start this import?";
        }
        internal static ImportOperationReview Manual(AutomaticConnection connection, ConnectedProfileReview reviewed, ImportDefaults defaults, IDiscovery discovery, Func<string, string> volumeId = null) {
            var profile = reviewed == null ? null : reviewed.Value;
            if (profile == null) throw new SafetyException("Set up this device's import profile first. Saving starts no import; then choose Verified import and review the folders.");
            RequireProfile(connection, profile); var options = profile.Resolve(defaults);
            string source = SafePaths.Normalize(Path.Combine(connection.Root, profile.RelativeSource)), destination = SafePaths.Normalize(profile.Destination);
            ManualImportReview selection = null;
            try {
                selection = new ManualImportReview(connection.Root, source, destination, (s, d) => new VolumeImportGuard(discovery, s, d, connection.Volume, connection.Disk, connection.Epoch, volumeId ?? WindowsDiscovery.VolumeId));
                return new ImportOperationReview(profile, options, connection.Root, selection, reviewed, false);
            } catch { if (selection != null) selection.Dispose(); throw; }
        }
        internal static ImportOperationReview AutomaticRequest(AutomaticImportRequest request, IDiscovery discovery, Action<long> observed = null, Func<string, string> volumeId = null) {
            if (request == null || !request.Enabled) throw new SafetyException("Automatic import was not enabled in this frozen request.");
            var profile = request.Profile; RequireProfile(request.Connection, profile);
            string source = SafePaths.Normalize(Path.Combine(request.Connection.Root, profile.RelativeSource)), destination = SafePaths.Normalize(profile.Destination);
            ManualImportReview selection = null;
            try {
                selection = new ManualImportReview(request.Connection.Root, source, destination, (s, d) => AutomaticImportAdmission.Bind(request, discovery, s, d, observed, volumeId));
                return new ImportOperationReview(profile, request.Options, request.Connection.Root, selection, null, true);
            } catch { if (selection != null) selection.Dispose(); throw; }
        }
        static void RequireProfile(AutomaticConnection connection, ImportProfile profile) {
            if (connection == null || profile.VolumeId != connection.Volume.Id || profile.VolumeSerial != connection.Volume.Serial || profile.DiskInstance != connection.Disk.InstanceId) throw new SafetyException("The frozen profile does not match this selected device.");
            Profiles.ValidateStoredFolders(profile);
        }
        internal void Confirm() { if (!Current || Interlocked.CompareExchange(ref phase, 1, 0) != 0) throw new SafetyException("This import review expired. Refresh and review the device again."); }
        internal ImportResult Execute(VerifiedImporter importer, CancellationToken cancel, Action<string> report, Action<ImportWorkProgress> progress = null) {
            if (Interlocked.CompareExchange(ref phase, 2, 1) != 1) throw new SafetyException("A fresh confirmed manual import or enabled automatic request is required.");
            try {
                cancel.ThrowIfCancellationRequested(); selection.Validate();
                if (!Automatic && profile.SavedDestination == null) profile = selection.BindLegacy(reviewed);
                cancel.ThrowIfCancellationRequested(); EngineStarted = true;
                return selection.Run(importer, profile.VolumeId, Options, cancel, report, progress);
            } finally { Interlocked.Exchange(ref phase, 3); }
        }
        public void Dispose() {
            int current;
            do { current = Volatile.Read(ref phase); if (current == 2) throw new InvalidOperationException("The import worker still owns its reviewed folders."); }
            while (Interlocked.CompareExchange(ref phase, 3, current) != current);
            selection.Dispose();
        }
    }
    internal sealed class ImportedOutcome {
        internal readonly ImportResult Result; internal readonly string Message, HistoryWarning;
        internal readonly bool SourceCurrent; readonly CompletedBatch batch; readonly ImportProfile profile;
        internal CompletedBatch Batch { get { return batch == null ? null : ImportHistory.Clone(batch); } }
        internal ImportProfile Profile { get { return profile.Copy(); } }
        internal ImportedOutcome(ImportResult result, ImportProfile profile, VolumeImportGuard guard, string root, IDiscovery discovery,
            Action<CompletedBatch> saveHistory, Func<string, string> volumeId = null) {
            Result = result; this.profile = profile.Copy(); volumeId = volumeId ?? WindowsDiscovery.VolumeId;
            if (result.HasSelectedMedia) {
                try { var saved = ImportHistory.Create(result, this.profile, ProfileName.Display(this.profile), guard.DestinationIdentity); saveHistory(ImportHistory.Clone(saved)); batch = ImportHistory.Clone(saved); }
                catch (Exception ex) { HistoryWarning = CompletionPresentation.HistoryFailure(ex); }
            }
            try { var state = discovery.Scan(); SourceCurrent = state.Epoch == discovery.Epoch && guard.CompletionOrigin.SourceCurrent(state, volumeId(root), root); }
            catch (Exception) { SourceCurrent = false; }
            string removal = result.Receipt != null && !String.IsNullOrEmpty(result.Receipt.ResumedUtc) ? RemovalResumeChoice.Summary(result)
                : OriginalRemovalChoice.Summary(result.RemovedOriginals) + " " + DeviceTrashChoice.Summary(result.RecoveredTrashFiles, result.ClearedTrashFiles, result.ClearedTrashRecords);
            Message = result.Files == 0 ? "No matching media or device indexes were found. No batch was created."
                : (result.HasSelectedMedia ? "Import complete. " : "No matching media. Device index backup only. ") + result.Files + " files verified (" + result.Bytes + " bytes; " + result.Reused + " reused).\n" + result.Folder;
            Message += result.HasSelectedMedia ? HistoryWarning == null ? "\nExact verified files saved in Import history." : "\n" + HistoryWarning : "";
            Message += "\n\n" + removal + " " + (SourceCurrent ? "Device remains connected." : "The source connection changed after copying. Refresh and review the device.");
        }
    }
}
