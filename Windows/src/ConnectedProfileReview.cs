using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace EasyEject {
    // Configuration evidence only; live source/destination guards remain separate.
    internal sealed class ConnectedProfileReview {
        internal readonly string Root, VolumeId, VolumeSerial, DiskInstance, FileName, Hash, Error;
        internal readonly Native.FileInfo Stamp;
        readonly ImportProfile value;
        internal bool Exists { get { return Hash != null; } }
        internal ImportProfile Value { get { return value == null ? null : value.Copy(); } }
        internal ConnectedProfileReview(string root, Volume volume, Disk disk, string hash = null, Native.FileInfo stamp = default(Native.FileInfo), ImportProfile profile = null, string error = null) {
            if (volume == null || disk == null || String.IsNullOrWhiteSpace(volume.Id) || volume.Serial == null || String.IsNullOrWhiteSpace(disk.InstanceId)) throw new SafetyException("The selected device identity cannot be used for a saved profile.");
            Root = SafePaths.Normalize(root); VolumeId = volume.Id; VolumeSerial = volume.Serial; DiskInstance = disk.InstanceId; FileName = Profiles.FileName(VolumeId);
            Hash = hash; Stamp = stamp; value = profile == null ? null : profile.Copy(); Error = error;
        }
    }
    public static partial class Profiles {
        internal static ConnectedProfileReview ReviewConnected(Volume volume, Disk disk) { return ReviewConnected(volume, disk, Root); }
        internal static ConnectedProfileReview ReviewConnected(Volume volume, Disk disk, string root, Action<string> checkpoint = null) {
            var empty = new ConnectedProfileReview(root, volume, disk); root = empty.Root;
            volume = new Volume { Id = empty.VolumeId, Serial = empty.VolumeSerial };
            disk = new Disk { InstanceId = empty.DiskInstance };
            try { if ((File.GetAttributes(root) & FileAttributes.Directory) == 0) throw new SafetyException("Saved profile storage is not a directory."); }
            catch (FileNotFoundException) { return empty; }
            catch (DirectoryNotFoundException) { return empty; }
            using (var directories = SafePaths.HoldDirectories(root)) {
                if (checkpoint != null) checkpoint("Before connected profile review"); directories.Validate();
                string path = Path.Combine(root, empty.FileName); bool exists = true;
                try { if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) throw new SafetyException("Saved profile is a directory. Existing data is retained."); }
                catch (FileNotFoundException) { exists = false; }
                var result = empty;
                if (exists) using (var stream = SafePaths.ReadFile(path)) {
                    Configuration.CheckRecord(stream, 65536); var stamp = Native.Info(stream.SafeFileHandle);
                    string hash = VerifiedImporter.Hash(stream, CancellationToken.None); stream.Position = 0;
                    ImportProfile profile = null; string error = null;
                    try {
                        profile = JsonDocuments.Read<ImportProfile>(stream, 65536);
                        ValidateProfileRecord(profile, empty.FileName); RequireConnectedIdentity(profile, volume, disk);
                    } catch (Exception ex) {
                        if (!(ex is SerializationException || ex is SafetyException || ex is ArgumentException)) throw;
                        error = ex.Message; profile = null;
                    }
                    if (checkpoint != null) checkpoint("After connected profile decode"); Configuration.CheckRecord(stream, 65536);
                    if (!VerifiedImporter.SameStamp(stamp, Native.Info(stream.SafeFileHandle))) throw new SafetyException("Saved profile changed during review. Close and review it again.");
                    result = new ConnectedProfileReview(root, volume, disk, hash, stamp, profile, error);
                }
                if (checkpoint != null) checkpoint("After connected profile review"); directories.Validate(); return result;
            }
        }
        static void RequireReviewRoot(ConnectedProfileReview expected, string root) {
            if (expected == null || !SafePaths.Normalize(root).Equals(expected.Root, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Review this device's saved profile in its current configuration folder.");
        }
        static void RequireExactReview(ConnectedProfileReview expected, FileStream previous) {
            if (expected.Exists != (previous != null)) throw new SafetyException("Saved profile appeared or disappeared while this choice was open. Close and review its current record.");
            if (previous == null) return;
            Configuration.CheckRecord(previous, 65536); previous.Position = 0;
            if (!VerifiedImporter.SameStamp(expected.Stamp, Native.Info(previous.SafeFileHandle)) || VerifiedImporter.Hash(previous, CancellationToken.None) != expected.Hash) throw new SafetyException("Saved profile changed while this choice was open. Close and review its current record.");
        }
        internal static void SaveConnected(ConnectedProfileReview expected, ImportProfile draft) { if (expected == null) throw new SafetyException("Review this device's saved profile first."); SaveConnected(expected, draft, expected.Root); }
        internal static void SaveConnected(ConnectedProfileReview expected, ImportProfile draft, string root, Action<string> checkpoint = null) {
            RequireReviewRoot(expected, root);
            if (draft == null || draft.VolumeId != expected.VolumeId || draft.VolumeSerial != expected.VolumeSerial || draft.DiskInstance != expected.DiskInstance) throw new SafetyException("New profile choices do not match the reviewed selected device.");
            ValidateProfileRecord(draft, expected.FileName); var value = draft.Copy();
            using (var directories = ConfigurationWriter.CreateRoot(expected.Root)) using (var writer = WriterGate(expected.Root)) {
                if (checkpoint != null) checkpoint("Before connected publication"); directories.Validate();
                ConfigurationWriter.Write(value, typeof(ImportProfile), expected.Root, expected.FileName, (stage, path) => {
                    directories.Validate(); if (checkpoint != null) checkpoint(stage); directories.Validate();
                }, writer, previous => RequireExactReview(expected, previous));
            }
        }
        internal static void ForgetConnected(ConnectedProfileReview expected) { if (expected == null) throw new SafetyException("Review this device's saved profile first."); ForgetConnected(expected, expected.Root); }
        internal static void ForgetConnected(ConnectedProfileReview expected, string root, Action<string> checkpoint = null) {
            RequireReviewRoot(expected, root); if (expected.Value == null) throw new SafetyException("Choose a valid reviewed saved profile before forgetting it.");
            using (var directories = SafePaths.HoldDirectories(expected.Root)) using (WriterGate(expected.Root)) {
                if (checkpoint != null) checkpoint("Before connected disposition"); directories.Validate();
                string path = Path.Combine(expected.Root, expected.FileName);
                using (var previous = ConfigurationWriter.OpenPrevious(path)) {
                    RequireExactReview(expected, previous);
                    if (checkpoint != null) checkpoint("Before reviewed profile disposition"); directories.Validate();
                    var stamp = Native.Info(previous.SafeFileHandle);
                    if ((stamp.Attributes & 0x1015) != 0 || stamp.Links != 1 || !VerifiedImporter.SameStamp(expected.Stamp, stamp) || !Native.FinalPath(previous.SafeFileHandle).Equals(path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Reviewed profile changed or became protected. Existing data is retained.");
                    Native.DeleteOnClose(previous.SafeFileHandle, "Cannot forget the reviewed saved profile");
                }
            }
        }
        internal static ImportProfile BindConnectedDestination(ConnectedProfileReview expected, DestinationIdentity identity) { if (expected == null) throw new SafetyException("Review this device's saved profile first."); return BindConnectedDestination(expected, identity, expected.Root); }
        internal static ImportProfile BindConnectedDestination(ConnectedProfileReview expected, DestinationIdentity identity, string root) {
            RequireReviewRoot(expected, root); var value = expected.Value;
            if (value == null || value.SavedDestination != null || identity == null) throw new SafetyException("Review an unbound legacy profile and its destination first.");
            identity.Validate(); ProfileDestination.Require(value, identity, false); value.SavedDestination = identity.Copy(); SaveConnected(expected, value, root); return value;
        }
    }
}
