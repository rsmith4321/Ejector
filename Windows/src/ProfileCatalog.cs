using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace EasyEject {
    internal static class ProfileName {
        internal static string ForDevice(string value) { return String.IsNullOrWhiteSpace(value) ? "Camera device" : new string(value.Where(c => !Char.IsControl(c)).Take(120).ToArray()).Trim(); }
        internal static string Display(ImportProfile profile) { return String.IsNullOrWhiteSpace(profile.Name) ? "Unnamed device (" + profile.VolumeId + ")" : profile.Name; }
    }
    // A catalog entry is a config snapshot, never connection or import authority.
    internal sealed class SavedProfile {
        readonly ImportProfile profile;
        internal readonly string FileName, Error, Hash;
        internal readonly Native.FileInfo Stamp;
        internal SavedProfile(string name, string error) { FileName = name; Error = error; }
        internal SavedProfile(string name, ImportProfile value, string hash, Native.FileInfo stamp) { FileName = name; profile = value.Copy(); Hash = hash; Stamp = stamp; }
        internal bool CanEdit { get { return profile != null && Error == null; } }
        internal ImportProfile Profile { get { if (!CanEdit) throw new SafetyException("This saved profile must be set up again with the original device connected."); return profile.Copy(); } }
        internal string Display { get { return CanEdit ? ProfileName.Display(profile) : FileName + " — needs review"; } }
    }
    public static partial class Profiles {
        internal static ProfileWriter WriterGate(string root) { return new ProfileWriter(root); }
        internal static void ValidateName(string value, bool required) {
            if (value == null && !required) return;
            if (String.IsNullOrWhiteSpace(value) || value.Length > 120 || value.Any(Char.IsControl)) throw new SafetyException("Enter a recognizable device name of 1–120 characters without control characters.");
        }
        internal static void ValidateStoredFolders(ImportProfile value) {
            if (value.RelativeSource == null || value.Destination == null) throw new SafetyException("Saved import folders are missing. Set up this device again.");
            string relative = value.RelativeSource;
            if (relative.Length > 230 || Path.IsPathRooted(relative) || relative.Contains("/") || relative.Split('\\').Any(p => p.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase) || p.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) || p.Equals(".Trashes", StringComparison.OrdinalIgnoreCase))) throw new SafetyException("Saved media folder is not an ordinary relative folder on this device.");
            SafePaths.Normalize(Path.Combine(@"X:\", relative));
            if (value.Destination.Length > 0) SafePaths.Normalize(value.Destination);
        }
        internal static SavedProfile ReadSaved(string root, string name) {
            string path = Path.Combine(root, name);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new SafetyException("Reparse points are not saved profile records.");
            using (var stream = SafePaths.ReadFile(path)) return ReadSaved(stream, name);
        }
        static SavedProfile ReadSaved(FileStream stream, string name) {
            var stamp = Native.Info(stream.SafeFileHandle);
            if (stream.Length > 65536 || stamp.Links != 1 || (stamp.Attributes & 0x1000) != 0) throw new SafetyException("Saved profile is oversized, offline or hard-linked.");
            string hash = VerifiedImporter.Hash(stream, CancellationToken.None); stream.Position = 0;
            var value = (ImportProfile)new DataContractJsonSerializer(typeof(ImportProfile)).ReadObject(stream);
            if (value == null || String.IsNullOrWhiteSpace(value.VolumeId) || value.VolumeSerial == null || String.IsNullOrWhiteSpace(value.DiskInstance) || FileName(value.VolumeId) != name) throw new SafetyException("Saved profile filename or device identity is invalid.");
            ValidateName(value.Name, false); ValidateStoredFolders(value);
            QuickShareProfile.Validate(value);
            if (value.SavedDestination != null) value.SavedDestination.Validate();
            if (value.MacTrashUid != null && !DeviceTrash.ValidMacOwner(value.MacTrashUid)) throw new SafetyException("Saved Mac Trash owner is invalid.");
            return new SavedProfile(name, value, hash, stamp);
        }
        internal static SavedProfile[] Catalog() { return Catalog(Root); }
        internal static SavedProfile[] Catalog(string root, Action<string> checkpoint = null) {
            using (var directories = SafePaths.HoldDirectories(root)) {
                // Keep one root identity for this read. Individual malformed records remain
                // reviewable, but a changed catalog root invalidates the entire snapshot.
                Action<string> check = stage => {
                    directories.Validate();
                    if (checkpoint != null) { checkpoint(stage); directories.Validate(); }
                };
                check("Before catalog enumeration");
                // Other settings/history JSON and temporary files are not profile records.
                var names = Directory.GetFileSystemEntries(root).Select(Path.GetFileName).Where(n => n.Length == 69 && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && n.Take(64).All(c => Uri.IsHexDigit(c))).OrderBy(n => n, StringComparer.Ordinal).Take(1001).ToArray();
                check("After catalog enumeration");
                if (names.Length > 1000) throw new SafetyException("Too many saved profiles to review safely.");
                var entries = new List<SavedProfile>();
                foreach (string name in names) {
                    check("Before profile read");
                    try { entries.Add(ReadSaved(root, name)); }
                    catch (Exception ex) {
                        if (!(ex is IOException || ex is System.ComponentModel.Win32Exception || ex is System.Runtime.Serialization.SerializationException || ex is SafetyException || ex is UnauthorizedAccessException || ex is ArgumentException)) throw;
                        entries.Add(new SavedProfile(name, ex.Message));
                    }
                    // Outside the per-record catch: directory failure is never a bad record.
                    check("After profile read");
                }
                var result = entries.OrderBy(p => p.Display, StringComparer.CurrentCultureIgnoreCase).ToArray();
                check("Before catalog return");
                return result;
            }
        }
        static void CheckSnapshot(SavedProfile expected, SavedProfile current) {
            if (expected == null || !expected.CanEdit || current == null || !current.CanEdit || expected.Hash != current.Hash || !VerifiedImporter.SameStamp(expected.Stamp, current.Stamp)) throw new SafetyException("This profile changed while its editor was open. Refresh saved profiles and review the new choices.");
        }
        internal static void SaveOffline(SavedProfile expected, ImportProfile draft) { SaveOffline(expected, draft, Root); }
        internal static void SaveOffline(SavedProfile expected, ImportProfile draft, string root, DestinationIdentity selectedDestination = null, Action<string> checkpoint = null) {
            if (expected == null || draft == null || !expected.CanEdit) throw new SafetyException("Choose a valid saved profile.");
            var original = expected.Profile;
            if (draft.VolumeId != original.VolumeId || draft.VolumeSerial != original.VolumeSerial || draft.DiskInstance != original.DiskInstance || draft.RelativeSource != original.RelativeSource || draft.MacTrashUid != original.MacTrashUid && draft.MacTrashUid != null) throw new SafetyException("Connect the original device to change its identity, media folder or Mac Trash owner.");
            ValidateName(draft.Name, true); ValidateStoredFolders(draft); SafePaths.Normalize(draft.Destination);
            QuickShareProfile.Validate(draft);
            bool changedDestination = !String.Equals(original.Destination, draft.Destination, StringComparison.OrdinalIgnoreCase) || !ProfileDestination.Same(original.SavedDestination, draft.SavedDestination);
            if (changedDestination || selectedDestination != null) {
                if (selectedDestination == null) throw new SafetyException("Choose and verify the destination again before changing its folder or identity.");
                OfflineProfileDestination.Validate(draft, selectedDestination); draft.SavedDestination = selectedDestination.Copy();
            }
            using (var directories = SafePaths.HoldDirectories(root)) using (var writer = WriterGate(root)) {
                CheckSnapshot(expected, ReadSaved(root, expected.FileName));
                if (checkpoint != null) checkpoint("After profile review");
                directories.Validate();
                // Keep the final compare inside exclusive ownership of the exact old
                // record; a late external edit must not be overwritten by this editor.
                ConfigurationWriter.Write(draft, typeof(ImportProfile), root, expected.FileName, (stage, path) => {
                    directories.Validate(); if (checkpoint != null) checkpoint(stage); directories.Validate();
                }, writer, previous => {
                    if (previous != null) previous.Position = 0;
                    CheckSnapshot(expected, previous == null ? null : ReadSaved(previous, expected.FileName));
                });
            }
        }
        internal static void ForgetOffline(SavedProfile expected) { ForgetOffline(expected, Root); }
        internal static void ForgetOffline(SavedProfile expected, string root) {
            if (expected == null || !expected.CanEdit) throw new SafetyException("Choose a valid saved profile.");
            using (SafePaths.LockDirectories(root)) using (WriterGate(root)) using (var stream = SafePaths.ProtectOriginal(Path.Combine(root, expected.FileName), true)) {
                var stamp = Native.Info(stream.SafeFileHandle); string hash = VerifiedImporter.Hash(stream, CancellationToken.None);
                if (hash != expected.Hash || !VerifiedImporter.SameStamp(expected.Stamp, stamp)) throw new SafetyException("This profile changed. Refresh saved profiles before forgetting it.");
                Native.DeleteOnClose(stream.SafeFileHandle, "Cannot forget this saved profile");
            }
        }
    }
}
