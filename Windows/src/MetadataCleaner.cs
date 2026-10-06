using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace EasyEject {
    internal sealed class CleanupCounts {
        internal int Files, Folders, Skipped;
        internal string Message { get { return "Removed " + Files + " macOS metadata files and " + Folders + " empty __MACOSX folders. Photos, recordings and device Trash were kept."; } }
    }
    internal static class MetadataCleaner {
        static readonly HashSet<string> protectedNames = new HashSet<string>(new[] { ".Spotlight-V100", ".Trashes", ".fseventsd", ".TemporaryItems", "$RECYCLE.BIN", "System Volume Information", ".Trash" }, StringComparer.OrdinalIgnoreCase);
        internal static bool Protected(string name) { return protectedNames.Contains(name) || name.StartsWith(".Trash-", StringComparison.OrdinalIgnoreCase); }
        internal static bool Metadata(string name) { return name.StartsWith("._", StringComparison.Ordinal) || name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase) || name.Equals(".apdisk", StringComparison.OrdinalIgnoreCase); }
        internal static void Preflight(IEnumerable<string> roots, Action validate) {
            validate();
            foreach (string input in roots) {
                string root = SafePaths.Normalize(input); if (root.Substring(3).Split('\\').Any(Protected)) throw new SafetyException("Device Trash and system folders cannot be cleanup roots.");
                using (SafePaths.LockDirectories(root)) { validate(); }
            }
        }
        internal static void Clean(string input, Action validate, CancellationToken cancellation, CleanupCounts counts, Action<string> report) {
            string root = SafePaths.Normalize(input);
            Preflight(new[] { root }, validate);
            using (SafePaths.LockDirectories(root)) {
                using (var h = Open(root, false)) {
                    var rootInfo = Native.Info(h); CheckPath(h, root, rootInfo.VolumeSerial, true);
                    Walk(root, rootInfo.VolumeSerial, validate, cancellation, counts, report);
                }
            }
            cancellation.ThrowIfCancellationRequested(); validate();
        }
        static SafeFileHandle Open(string path, bool remove) {
            var h = Native.CreateFile(path, remove ? 0x10080U : 0x80U, remove ? 0U : 1U, IntPtr.Zero, 3, 0x02300000, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); throw Native.Error("Cannot open cleanup item without following links: " + path); } return h;
        }
        static Native.FileInfo CheckPath(SafeFileHandle handle, string path, uint serial, bool directory) {
            var info = Native.Info(handle);
            if (((info.Attributes & 0x10) != 0) != directory || info.VolumeSerial != serial || !Native.FinalPath(handle).Equals(path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Cleanup path, volume or item type changed. Removal stopped.");
            if ((info.Attributes & 0x1004) != 0) throw new SafetyException("System or offline items cannot be removed by metadata cleanup.");
            return info;
        }
        static Native.FileInfo Walk(string folder, uint serial, Action validate, CancellationToken cancellation, CleanupCounts counts, Action<string> report) {
            cancellation.ThrowIfCancellationRequested(); validate();
            Native.FileInfo identity;
            using (SafePaths.LockDirectories(folder)) using (var h = Open(folder, false)) {
                identity = CheckPath(h, folder, serial, true);
                string[] entries = Directory.GetFileSystemEntries(folder).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (string input in entries) {
                    cancellation.ThrowIfCancellationRequested(); validate(); string path = SafePaths.Normalize(input); string name = Path.GetFileName(path);
                    if (!SafePaths.Within(path, folder) || path == folder) throw new SafetyException("Cleanup entry escaped its folder.");
                    if (Protected(name)) { counts.Skipped++; continue; }
                    var attributes = File.GetAttributes(path);
                    if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Offline)) != 0) { counts.Skipped++; continue; }
                    if ((attributes & FileAttributes.Directory) != 0) {
                        var childIdentity = Walk(path, serial, validate, cancellation, counts, report);
                        // Parent remains pinned; reopen this folder itself for an atomic empty-only deletion.
                        if (name.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)) RemoveEmpty(path, serial, childIdentity, validate, cancellation, counts);
                    } else if (Metadata(name)) {
                        using (var file = Open(path, true)) {
                            var info = CheckPath(file, path, serial, false);
                            if (info.Links != 1) { counts.Skipped++; continue; }
                            cancellation.ThrowIfCancellationRequested(); validate(); CheckPath(file, path, serial, false);
                            Native.DeleteOnClose(file, "Cannot remove selected metadata: " + name);
                        }
                        counts.Files++; report("Removed metadata: " + name);
                    }
                }
            }
            return identity;
        }
        static void RemoveEmpty(string path, uint serial, Native.FileInfo expected, Action validate, CancellationToken cancellation, CleanupCounts counts) {
            using (SafePaths.LockDirectories(path)) {
                if (Directory.GetFileSystemEntries(path).Length != 0) return;
            }
            using (var folder = Open(path, true)) {
                var current = CheckPath(folder, path, serial, true);
                if (current.IndexHigh != expected.IndexHigh || current.IndexLow != expected.IndexLow) throw new SafetyException("The empty __MACOSX folder changed before cleanup.");
                cancellation.ThrowIfCancellationRequested(); validate(); CheckPath(folder, path, serial, true);
                // Windows refuses a nonempty directory atomically if an item arrives after enumeration.
                try { Native.DeleteOnClose(folder, "Cannot remove empty __MACOSX folder"); }
                catch (Win32Exception ex) { if (ex.NativeErrorCode == 145) return; throw; }
            }
            counts.Folders++;
        }
    }
    internal sealed class CleanupRoot {
        internal readonly string Path, VolumeId, Fingerprint;
        internal CleanupRoot(string path, Volume volume) { Path = SafePaths.Normalize(path); VolumeId = volume.Id; Fingerprint = volume.Fingerprint; }
    }
    internal sealed class MetadataCleanupPlan {
        internal readonly string TargetId, Fingerprint; internal readonly CleanupRoot[] Roots;
        MetadataCleanupPlan(Inventory state, string target, string fingerprint, int[] disks, IEnumerable<string> volumeIds) {
            if (!state.Complete || state.GroupFingerprint(target) != fingerprint) throw new SafetyException("Cleanup device identity is unavailable.");
            TargetId = target; Fingerprint = fingerprint;
            var roots = new List<CleanupRoot>();
            foreach (var id in volumeIds.Distinct(StringComparer.OrdinalIgnoreCase)) {
                var volume = state.Volumes.SingleOrDefault(v => v.Id == id);
                if (volume == null || volume.System || volume.Error != "" || volume.Serial == "" || volume.Disks.Count != 1 || !disks.Contains(volume.Disks[0])) throw new SafetyException("Cleanup cannot affect protected, unknown or unrelated storage.");
                string root = volume.Mounts.Where(p => p.Length >= 3 && Char.IsLetter(p[0]) && p[1] == ':').OrderBy(p => p.Length).FirstOrDefault();
                if (root == null) throw new SafetyException("A cleanup volume has no accessible local mount. Review it individually without cleanup.");
                roots.Add(new CleanupRoot(root, volume));
            }
            if (roots.Count == 0) throw new SafetyException("No volume was selected for metadata cleanup."); Roots = roots.ToArray();
        }
        internal static MetadataCleanupPlan Single(Inventory state, EjectPlan plan, string volumeId) { return new MetadataCleanupPlan(state, plan.TargetId, plan.Fingerprint, plan.Disks, new[] { volumeId }); }
        internal static MetadataCleanupPlan Bulk(Inventory state, BulkTarget target) { return new MetadataCleanupPlan(state, target.Id, target.Fingerprint, target.Disks, target.Volumes); }
        internal string Description { get { return "Permanently remove macOS metadata (._*, .DS_Store, .apdisk and empty __MACOSX) from:\r\n" + String.Join("\r\n", Roots.Select(r => r.Path)) + "\r\nPhotos, recordings, other files and device Trash are kept. Cleanup failure stops ejection."; } }
        internal void Validate(Inventory state, Func<string, string> resolveVolume) {
            if (!state.Complete || state.GroupFingerprint(TargetId) != Fingerprint) throw new SafetyException("Cleanup device connection or topology changed.");
            foreach (var root in Roots) {
                var volume = state.Volumes.SingleOrDefault(v => v.Id == root.VolumeId);
                if (volume == null || volume.Fingerprint != root.Fingerprint || resolveVolume(root.Path) != root.VolumeId) throw new SafetyException("Cleanup volume root or identity changed.");
            }
        }
        internal void Preflight(Inventory state, Func<string, string> resolveVolume) { Validate(state, resolveVolume); MetadataCleaner.Preflight(Roots.Select(r => r.Path), () => Validate(state, resolveVolume)); }
        internal void Clean(Func<Inventory> validate, Func<string, string> resolveVolume, CancellationToken cancellation, CleanupCounts counts, Action<string> report) {
            Action guard = () => Validate(validate(), resolveVolume);
            try { foreach (var root in Roots) MetadataCleaner.Clean(root.Path, guard, cancellation, counts, report); }
            catch (Exception ex) { throw new SafetyException("Metadata cleanup stopped; the current device was not requested for removal. " + counts.Message + " " + ex.Message); }
        }
    }
}
