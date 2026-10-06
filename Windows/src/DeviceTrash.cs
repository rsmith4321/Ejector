using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace EasyEject {
    // Read-only catalog. Recovery must use the verified import/removal engine; no Shell restore/empty API.
    internal sealed class WindowsTrashRecordInfo {
        internal readonly string OriginalPath; internal readonly long Bytes; internal readonly DateTime DeletedUtc;
        WindowsTrashRecordInfo(string path, long bytes, DateTime deleted) { OriginalPath = path; Bytes = bytes; DeletedUtc = deleted; }
        internal static WindowsTrashRecordInfo Parse(byte[] data) {
            if (data == null || data.Length < 26 || data.Length > 4096) throw new SafetyException("Device Recycle Bin metadata is truncated or too large. Nothing was cleared.");
            ulong version = BitConverter.ToUInt64(data, 0), bytes = BitConverter.ToUInt64(data, 8); long time = BitConverter.ToInt64(data, 16);
            if (bytes > Int64.MaxValue || time <= 0) throw new SafetyException("Device Recycle Bin metadata has invalid size or deletion time.");
            int offset, length;
            if (version == 1) { offset = 24; length = data.Length - offset; if (data.Length > 544 || length % 2 != 0) throw new SafetyException("Unsupported version-1 Recycle Bin metadata layout."); }
            else if (version == 2 && data.Length >= 30) {
                uint characters = BitConverter.ToUInt32(data, 24); offset = 28;
                if (characters < 4 || characters > 241 || data.Length != offset + characters * 2) throw new SafetyException("Device Recycle Bin metadata path length is invalid.");
                length = (int)characters * 2;
            } else throw new SafetyException("Unsupported device Recycle Bin metadata version. Nothing was cleared.");
            string text;
            try { text = new UnicodeEncoding(false, false, true).GetString(data, offset, length); }
            catch (DecoderFallbackException) { throw new SafetyException("Device Recycle Bin metadata has invalid Unicode."); }
            int end = text.IndexOf('\0');
            if (end < 3 || text.Skip(end).Any(c => c != '\0') || (version == 2 && end != text.Length - 1)) throw new SafetyException("Device Recycle Bin metadata path is not terminated correctly.");
            string path = text.Substring(0, end);
            try {
                if (path.Contains('/') || !SafePaths.Normalize(path).Equals(path, StringComparison.OrdinalIgnoreCase) || path.Length <= 3) throw new SafetyException("Device Recycle Bin original path is ambiguous or unsupported.");
                return new WindowsTrashRecordInfo(path, (long)bytes, DateTime.FromFileTimeUtc(time));
            } catch (ArgumentException) { throw new SafetyException("Device Recycle Bin original path or deletion time is invalid."); }
        }
    }
    internal sealed class TrashSourceFile {
        internal readonly string ActualPath, LogicalPath, RecordPath; internal readonly Native.FileInfo Stamp;
        internal TrashSourceFile(string actual, string logical, string record, Native.FileInfo stamp) { ActualPath = actual; LogicalPath = logical; RecordPath = record; Stamp = stamp; }
    }
    internal sealed class TrashRecord {
        internal readonly string MetadataPath, MetadataSha256, PayloadRoot; internal readonly Native.FileInfo MetadataStamp;
        internal readonly WindowsTrashRecordInfo Original; internal readonly bool Directory;
        internal readonly ReadOnlyCollection<TrashSourceFile> Files;
        internal TrashRecord(string metadata, string hash, Native.FileInfo stamp, string payload, WindowsTrashRecordInfo original, bool directory, List<TrashSourceFile> files) {
            MetadataPath = metadata; MetadataSha256 = hash; MetadataStamp = stamp; PayloadRoot = payload; Original = original; Directory = directory; Files = files.AsReadOnly();
        }
    }
    internal sealed class DeviceTrashSelection {
        internal readonly ReadOnlyCollection<TrashSourceFile> Files;
        internal readonly ReadOnlyCollection<TrashRecord> Records;
        internal readonly ReadOnlyCollection<string> RemovablePayloads, RemovableRecords;
        internal readonly bool Structured;
        internal DeviceTrashSelection(DeviceTrashSnapshot snapshot, MediaOptions options) {
            var plan = MediaSelection.Plan(snapshot.Files.Select(x => x.LogicalPath).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(), snapshot.LogicalRoot, snapshot.LogicalRoot, options);
            var selected = new HashSet<string>(plan.Media, StringComparer.OrdinalIgnoreCase);
            Files = snapshot.Files.Where(x => selected.Contains(x.LogicalPath)).ToList().AsReadOnly(); Structured = plan.Structured;
            var deletable = MediaSelection.Deletable(plan, options);
            RemovablePayloads = Files.Where(x => deletable.Contains(x.LogicalPath)).Select(x => x.ActualPath).ToList().AsReadOnly();
            var records = new HashSet<string>(Files.Where(x => x.RecordPath != null).Select(x => x.RecordPath), StringComparer.OrdinalIgnoreCase);
            Records = snapshot.Records.Where(x => records.Contains(x.MetadataPath)).ToList().AsReadOnly();
            // A folder's record remains while its directory/preserved contents remain, like retained Mac Trash folders.
            RemovableRecords = Records.Where(x => !x.Directory && x.Files.Count == 1 && RemovablePayloads.Contains(x.Files[0].ActualPath)).Select(x => x.MetadataPath).ToList().AsReadOnly();
        }
    }
    internal sealed class DeviceTrashSnapshot {
        internal readonly string SourceRoot, LogicalRoot, WindowsOwnerRoot, MacOwnerRoot, WindowsSid, MacUid;
        internal readonly ReadOnlyCollection<TrashSourceFile> Files; internal readonly ReadOnlyCollection<TrashRecord> Records;
        internal DeviceTrashSnapshot(string root, string logical, string windowsOwner, string macOwner, List<TrashSourceFile> files, List<TrashRecord> records, string sid, string macUid) {
            SourceRoot = root; LogicalRoot = logical; WindowsOwnerRoot = windowsOwner; MacOwnerRoot = macOwner; WindowsSid = sid; MacUid = macUid; Files = files.AsReadOnly(); Records = records.AsReadOnly();
            if (files.Select(x => x.LogicalPath.Normalize(NormalizationForm.FormC)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count
                || files.Select(x => x.ActualPath.Normalize(NormalizationForm.FormC)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count) throw new SafetyException("Device Trash contains ambiguous duplicate original names. Restore those items manually before recovery; nothing was cleared.");
        }
        internal DeviceTrashSelection Select(MediaOptions options) { return new DeviceTrashSelection(this, options); }
        internal void CheckManifest(MediaOptions options, Func<string, bool> removed, CancellationToken cancellation, Action validate) {
            var expected = Select(options);
            var current = DeviceTrash.ReadCurrent(this, expected, removed, cancellation, validate);
            var virtualFiles = current.Files.Concat(Files.Where(x => removed(x.ActualPath))).ToList();
            var records = current.Records.Concat(Records.Where(x => removed(x.MetadataPath))).ToList();
            var virtualSnapshot = new DeviceTrashSnapshot(SourceRoot, LogicalRoot, current.WindowsOwnerRoot, current.MacOwnerRoot, virtualFiles, records, WindowsSid, MacUid);
            var selected = virtualSnapshot.Select(options);
            if (!selected.Files.Select(x => x.ActualPath).Where(x => !removed(x)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).SequenceEqual(expected.Files.Select(x => x.ActualPath).Where(x => !removed(x)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                || !selected.Records.Select(x => x.MetadataPath).Where(x => !removed(x)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).SequenceEqual(expected.Records.Select(x => x.MetadataPath).Where(x => !removed(x)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))) throw new SafetyException("The selected device Trash changed during recovery. Remaining Trash files are kept.");
        }
    }
    internal static class DeviceTrash {
        sealed class ReadContext {
            internal Dictionary<string, TrashSourceFile> ProtectedFiles; internal Dictionary<string, TrashRecord> ProtectedRecords, AllRecords; internal Func<string, bool> Removed;
        }
        internal static DeviceTrashSnapshot ReadCurrent(DeviceTrashSnapshot frozen, DeviceTrashSelection selected, Func<string, bool> removed, CancellationToken cancellation, Action validate) {
            var context = new ReadContext { ProtectedFiles = selected.Files.ToDictionary(x => x.ActualPath, StringComparer.OrdinalIgnoreCase), ProtectedRecords = selected.Records.ToDictionary(x => x.MetadataPath, StringComparer.OrdinalIgnoreCase), AllRecords = frozen.Records.ToDictionary(x => x.MetadataPath, StringComparer.OrdinalIgnoreCase), Removed = removed };
            return ReadCore(frozen.SourceRoot, frozen.WindowsSid, frozen.MacUid, cancellation, validate, context);
        }
        internal static DeviceTrashSnapshot Read(string sourceRoot, string selectedMacUid, CancellationToken cancellation, Action validate) {
            using (var identity = WindowsIdentity.GetCurrent()) {
                if (identity.User == null) throw new SafetyException("The current Windows Trash owner is unavailable.");
                return Read(sourceRoot, identity.User.Value, selectedMacUid, cancellation, validate);
            }
        }
        internal static bool ValidMacOwner(string uid) {
            uint parsed; return uid != null && UInt32.TryParse(uid, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out parsed) && parsed.ToString(System.Globalization.CultureInfo.InvariantCulture) == uid;
        }
        static void Check(CancellationToken cancellation, Action validate) { cancellation.ThrowIfCancellationRequested(); validate(); }
        internal static DeviceTrashSnapshot Read(string sourceRoot, string windowsSid, string selectedMacUid, CancellationToken cancellation, Action validate) {
            return ReadCore(sourceRoot, windowsSid, selectedMacUid, cancellation, validate, null);
        }
        static DeviceTrashSnapshot ReadCore(string sourceRoot, string windowsSid, string selectedMacUid, CancellationToken cancellation, Action validate, ReadContext context) {
            sourceRoot = SafePaths.Normalize(sourceRoot);
            try { if (new SecurityIdentifier(windowsSid).Value != windowsSid) throw new SafetyException("Device Trash owner SID is not canonical."); }
            catch (ArgumentException) { throw new SafetyException("Device Trash owner SID is invalid."); }
            if (selectedMacUid != null && !ValidMacOwner(selectedMacUid)) throw new SafetyException("Choose an explicit numeric Mac Trash owner folder; a Mac UID cannot be inferred from Windows.");
            string logical = SafePaths.Normalize(Path.Combine(sourceRoot, ".easy-eject-trash-names"));
            var files = new List<TrashSourceFile>(); var records = new List<TrashRecord>(); string windowsOwner = null, macOwner = null;
            using (SafePaths.LockDirectories(sourceRoot)) {
                Check(cancellation, validate); string bin = Child(sourceRoot, "$RECYCLE.BIN", cancellation, validate);
                if (bin != null) {
                    using (SafePaths.LockDirectories(bin)) { windowsOwner = Child(bin, windowsSid, cancellation, validate); }
                    if (windowsOwner != null) ReadWindows(windowsOwner, logical, files, records, cancellation, validate, context);
                }
                // No traversal of any Mac owner unless the profile explicitly selects that UID.
                if (selectedMacUid != null) {
                    string parent = Child(sourceRoot, ".Trashes", cancellation, validate);
                    if (parent != null) {
                        using (SafePaths.LockDirectories(parent)) { macOwner = Child(parent, selectedMacUid, cancellation, validate); }
                        if (macOwner != null) Walk(macOwner, Path.Combine(logical, "Mac Trash"), null, files, cancellation, validate, context);
                    }
                }
                Check(cancellation, validate);
                return new DeviceTrashSnapshot(sourceRoot, logical, windowsOwner, macOwner, files.OrderBy(x => x.LogicalPath, StringComparer.OrdinalIgnoreCase).ToList(), records, windowsSid, selectedMacUid);
            }
        }
        // Throwing enumeration distinguishes absence from denied/inaccessible Trash; no File.Exists probe.
        static string Child(string parent, string name, CancellationToken cancellation, Action validate) {
            Check(cancellation, validate);
            string[] found = Directory.GetFileSystemEntries(parent).Where(x => Path.GetFileName(x).Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (found.Length == 0) return null;
            if (found.Length != 1 || (Attributes(found[0]) & FileAttributes.Directory) == 0) throw new SafetyException("The selected device Trash location is ambiguous or is not a directory.");
            using (SafePaths.LockDirectories(found[0])) {} return found[0];
        }
        static FileAttributes Attributes(string path) {
            SafePaths.Normalize(path); var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new SafetyException("Device Trash contains a link or cloud placeholder. Recovery stops without following it.");
            if ((attributes & FileAttributes.Offline) != 0) throw new SafetyException("Device Trash contains offline data. Recovery requires ordinary local files.");
            return attributes;
        }
        static TrashSourceFile FileItem(string path, string logical, string record, CancellationToken cancellation, Action validate, ReadContext context) {
            Check(cancellation, validate); SafePaths.Normalize(logical);
            if (context != null) {
                if (context.Removed(path)) throw new SafetyException("A replacement appeared at a cleared Trash payload path. The replacement is kept.");
                TrashSourceFile known;
                if (context.ProtectedFiles.TryGetValue(path, out known)) {
                    if (known.LogicalPath != logical || known.RecordPath != record) throw new SafetyException("A protected Trash payload changed original-path mapping.");
                    // The import session checks identity/hash on its held source; do not reopen an exclusive DELETE handle.
                    return known;
                }
            }
            using (SafePaths.LockDirectories(Path.GetDirectoryName(path))) using (var handle = Native.CreateFile(path, 0x80, 7, IntPtr.Zero, 3, 0x00300000, IntPtr.Zero)) {
                if (handle.IsInvalid) throw Native.Error("Cannot inspect a device Trash payload without following links");
                var stamp = Native.Info(handle);
                if ((stamp.Attributes & 0x10) != 0 || !Native.FinalPath(handle).Equals(path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("A device Trash payload changed type or path while reading.");
                return new TrashSourceFile(path, logical, record, stamp);
            }
        }
        static void Walk(string directory, string logical, string record, List<TrashSourceFile> files, CancellationToken cancellation, Action validate, ReadContext context) {
            using (SafePaths.LockDirectories(directory)) {
                Check(cancellation, validate);
                foreach (string path in Directory.GetFileSystemEntries(directory).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) {
                    Check(cancellation, validate); var attributes = Attributes(path); string name = SafePaths.Normalize(Path.Combine(logical, Path.GetFileName(path)));
                    if ((attributes & FileAttributes.Directory) != 0) Walk(path, name, record, files, cancellation, validate, context);
                    else files.Add(FileItem(path, name, record, cancellation, validate, context));
                }
            }
        }
        static void ReadWindows(string owner, string logical, List<TrashSourceFile> files, List<TrashRecord> records, CancellationToken cancellation, Action validate, ReadContext context) {
            using (SafePaths.LockDirectories(owner)) {
                Check(cancellation, validate); string[] items = Directory.GetFileSystemEntries(owner);
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string metadata in items.Where(x => Path.GetFileName(x).StartsWith("$I", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) {
                    Check(cancellation, validate); string name = Path.GetFileName(metadata);
                    if (context != null && context.Removed(metadata)) throw new SafetyException("A replacement appeared at a cleared Trash record path. The replacement is kept.");
                    if (name.Length <= 2 || (Attributes(metadata) & FileAttributes.Directory) != 0) throw new SafetyException("Invalid device Recycle Bin metadata item.");
                    string[] matching = items.Where(x => Path.GetFileName(x).Equals("$R" + name.Substring(2), StringComparison.OrdinalIgnoreCase)).ToArray();
                    TrashRecord known = null;
                    if (context != null) context.AllRecords.TryGetValue(metadata, out known);
                    bool clearedPayload = matching.Length == 0 && known != null && !known.Directory && context.Removed(known.PayloadRoot);
                    if (matching.Length != 1 && !clearedPayload) throw new SafetyException("A device Recycle Bin record has no unique payload. Restore or review the item manually; remaining Trash data is kept.");
                    WindowsTrashRecordInfo info; string hash; Native.FileInfo stamp;
                    TrashRecord protectedRecord;
                    if (context != null && context.ProtectedRecords.TryGetValue(metadata, out protectedRecord)) { info = protectedRecord.Original; hash = protectedRecord.MetadataSha256; stamp = protectedRecord.MetadataStamp; }
                    else {
                    using (var input = SafePaths.ReadFile(metadata, 4096)) {
                        stamp = Native.Info(input.SafeFileHandle);
                        if (input.Length > 4096) throw new SafetyException("Device Recycle Bin metadata is too large.");
                        byte[] data = new byte[(int)input.Length]; int offset = 0, count;
                        while (offset < data.Length && (count = input.Read(data, offset, data.Length - offset)) > 0) { Check(cancellation, validate); offset += count; }
                        if (offset != data.Length) throw new SafetyException("Device Recycle Bin metadata was truncated while reading.");
                        info = WindowsTrashRecordInfo.Parse(data); input.Position = 0; hash = VerifiedImporter.Hash(input, cancellation);
                        if (!VerifiedImporter.SameStamp(stamp, Native.Info(input.SafeFileHandle))) throw new SafetyException("Device Recycle Bin metadata changed while reading.");
                    }
                    }
                    string payload = clearedPayload ? known.PayloadRoot : matching[0]; bool directory = !clearedPayload && (Attributes(payload) & FileAttributes.Directory) != 0;
                    // Preserve original names and folder ancestry, independent of the randomized $R payload name/old drive letter.
                    string targetName = SafePaths.Normalize(Path.Combine(logical, "Windows Trash", info.OriginalPath.Substring(3)));
                    var members = new List<TrashSourceFile>();
                    if (directory) Walk(payload, targetName, metadata, members, cancellation, validate, context);
                    else if (!clearedPayload) {
                        var item = FileItem(payload, targetName, metadata, cancellation, validate, context);
                        long size = ((long)item.Stamp.SizeHigh << 32) | item.Stamp.SizeLow;
                        if (size != info.Bytes) throw new SafetyException("A device Recycle Bin payload size does not match its original record.");
                        members.Add(item);
                    }
                    used.Add(metadata); used.Add(payload); files.AddRange(members); records.Add(new TrashRecord(metadata, hash, stamp, payload, info, directory, members));
                }
                foreach (string path in items.Where(x => !used.Contains(x))) {
                    if (!Path.GetFileName(path).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || (Attributes(path) & FileAttributes.Directory) != 0) throw new SafetyException("Device Recycle Bin has an unrecognized or orphaned item. Nothing was cleared; review it in Windows first.");
                }
                Check(cancellation, validate);
            }
        }
    }
}
