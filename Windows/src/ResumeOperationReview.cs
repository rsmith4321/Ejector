using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;

namespace EasyEject {
    // A review grants no removal authority until explicitly confirmed once. Keep
    // folder identity across the modal; the engine rechecks all journal evidence.
    internal sealed class ResumeOperationReview : IDisposable {
        readonly ManualImportReview selection;
        readonly Func<VolumeImportGuard> makeGuard;
        readonly ImportProfile profile;
        readonly RemovalResumeEvidence evidence;
        readonly string intent, sid;
        int phase;
        internal readonly string Root, Source, Destination, Description;
        internal readonly MediaOptions Options;
        internal VolumeImportGuard Guard { get; private set; }
        internal ImportProfile Profile { get { return profile.Copy(); } }
        internal bool Current { get { return Volatile.Read(ref phase) == 0 && !Guard.Changed; } }
        ResumeOperationReview(ImportProfile profile, bool askToEject, string root, string source, string destination,
            string intent, string sid, ManualImportReview selection, Func<VolumeImportGuard> makeGuard, RemovalResumeEvidence evidence) {
            this.profile = profile.Copy(); Root = root; Source = source; Destination = destination;
            this.intent = intent; this.sid = sid; this.selection = selection; this.makeGuard = makeGuard; this.evidence = evidence; Guard = selection.Guard;
            var saved = evidence.Options;
            Options = new MediaOptions(saved.VideosOnly, saved.IncludePreviews, saved.CleanLayout, askToEject, saved.DeleteOriginals, saved.RecoverTrash, saved.MacTrashUid);
            Description = RemovalResumeChoice.Explanation(evidence, ProfileName.Display(this.profile), Source, Destination);
        }
        internal static ResumeOperationReview Create(AutomaticConnection connection, ConnectedProfileReview reviewed, ImportDefaults defaults,
            string intentPath, string windowsSid, IDiscovery discovery, Operations operations, CancellationToken cancel, Func<string, string> volumeId = null) {
            cancel.ThrowIfCancellationRequested();
            var profile = reviewed == null ? null : reviewed.Value;
            if (profile == null) throw new SafetyException("Set up this device with its original media folder and destination first. Saving a profile starts no import; then choose Resume interrupted import.");
            if (connection == null || profile.VolumeId != connection.Volume.Id || profile.VolumeSerial != connection.Volume.Serial || profile.DiskInstance != connection.Disk.InstanceId)
                throw new SafetyException("The frozen profile does not match this selected device.");
            Profiles.ValidateStoredFolders(profile);
            string root = SafePaths.Normalize(connection.Root), source = SafePaths.Normalize(Path.Combine(root, profile.RelativeSource)), destination = SafePaths.Normalize(profile.Destination);
            string intent = SafePaths.Normalize(intentPath);
            var v = connection.Volume; var d = connection.Disk; long epoch = connection.Epoch;
            var volume = new Volume { Id = v.Id, Label = v.Label, Serial = v.Serial, Error = v.Error, Extents = v.Extents, System = v.System, Group = v.Group, Classification = v.Classification, Mounts = new List<string>(v.Mounts), Disks = new List<int>(v.Disks) };
            var disk = new Disk { Number = d.Number, DevInst = d.DevInst, InstanceId = d.InstanceId, Model = d.Model, Serial = d.Serial, TargetId = d.TargetId, TargetDevInst = d.TargetDevInst, Bus = d.Bus, Removable = d.Removable, Hotplug = d.Hotplug, Error = d.Error, Ancestors = new List<string>(d.Ancestors) };
            Func<VolumeImportGuard> makeGuard = () => new VolumeImportGuard(discovery, source, destination, volume, disk, epoch, volumeId ?? WindowsDiscovery.VolumeId);
            ManualImportReview selection = null;
            try {
                selection = new ManualImportReview(root, source, destination, (sourcePath, destinationPath) => makeGuard());
                ProfileDestination.Require(profile, selection.Guard.DestinationIdentity, false); selection.Validate();
                var evidence = RemovalResumeEvidence.Review(intent, root, source, destination, windowsSid, selection.Guard, operations, cancel);
                selection.Validate();
                return new ResumeOperationReview(profile, profile.Resolve(defaults).AskToEject, root, source, destination, intent, windowsSid, selection, makeGuard, evidence);
            } catch { if (selection != null) selection.Dispose(); throw; }
        }
        internal void Confirm() {
            if (!Current || Interlocked.CompareExchange(ref phase, 1, 0) != 0) throw new SafetyException("This resume review expired. Refresh and review the interrupted batch again.");
        }
        internal ImportResult Execute(Operations operations, CancellationToken cancel, Action<string> report) {
            if (Interlocked.CompareExchange(ref phase, 2, 1) != 1) throw new SafetyException("A fresh confirmed resume review is required.");
            try {
                cancel.ThrowIfCancellationRequested(); selection.Validate();
                Guard = makeGuard(); selection.Validate(); ProfileDestination.Require(profile, Guard.DestinationIdentity, false);
                cancel.ThrowIfCancellationRequested();
                return RemovalResumeEvidence.Resume(evidence, intent, Root, Source, Destination, sid, Guard, operations, cancel, report);
            } finally { Interlocked.Exchange(ref phase, 3); }
        }
        public void Dispose() {
            int current;
            do { current = Volatile.Read(ref phase); if (current == 2) throw new InvalidOperationException("The resume worker still owns its reviewed folders."); }
            while (Interlocked.CompareExchange(ref phase, 3, current) != current);
            selection.Dispose();
        }
    }
}
