using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace EasyEject {
    internal sealed class ImportSourcePlan {
        readonly string source, selected; readonly MediaOptions options;
        readonly ImportMediaFolder mediaFolder;
        internal readonly MediaPlan Ordinary; internal readonly DeviceTrashSnapshot Trash; internal readonly DeviceTrashSelection TrashSelection;
        readonly Dictionary<string, TrashSourceFile> trashFiles = new Dictionary<string, TrashSourceFile>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, TrashRecord> trashRecords = new Dictionary<string, TrashRecord>(StringComparer.OrdinalIgnoreCase);
        internal readonly List<string> Files; internal readonly HashSet<string> Deletable;
        internal bool HasMedia { get { return Ordinary.Media.Count != 0 || trashFiles.Count != 0; } }
        internal ImportSourcePlan(string source, string selected, MediaOptions options, CancellationToken cancellation, Action validate, Func<string, string, CancellationToken, Action, DeviceTrashSnapshot> catalog, ImportMediaFolder mediaFolder) {
            this.source = source; this.selected = selected; this.options = options;
            this.mediaFolder = mediaFolder;
            string relative = selected.TrimEnd('\\').Equals(source.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? "" : SafePaths.Relative(selected, source);
            if (relative.Split('\\').Any(x => x.Equals(".Trashes", StringComparison.OrdinalIgnoreCase) || x.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase) || x.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))) throw new SafetyException("Choose an ordinary complete media folder or card. Device Trash requires the separate owner-aware recovery option.");
            Ordinary = MediaSelection.Plan(mediaFolder.Scan(cancellation), source, selected, options);
            Deletable = options.DeleteOriginals ? MediaSelection.Deletable(Ordinary, options) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (options.RecoverTrash) {
                Trash = catalog(source, options.MacTrashUid, cancellation, validate); validate(); TrashSelection = Trash.Select(options);
                if (Trash.SourceRoot != source || !SafePaths.Within(Trash.LogicalRoot, source)) throw new SafetyException("The recovery catalog belongs to another source.");
                foreach (var file in TrashSelection.Files) {
                    if (!SafePaths.Within(file.ActualPath, source) || !SafePaths.Within(file.LogicalPath, Trash.LogicalRoot)) throw new SafetyException("A recovery source escaped the selected device.");
                    trashFiles.Add(file.ActualPath, file);
                }
                foreach (var record in TrashSelection.Records) { if (!SafePaths.Within(record.MetadataPath, source)) throw new SafetyException("A recovery record escaped the selected device."); trashRecords.Add(record.MetadataPath, record); }
                Deletable.UnionWith(TrashSelection.RemovablePayloads); Deletable.UnionWith(TrashSelection.RemovableRecords);
            }
            Files = Ordinary.Files.Concat(trashFiles.Keys).Concat(trashRecords.Keys).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            if (Files.Select(x => x.Normalize(NormalizationForm.FormC)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Files.Count) throw new SafetyException("Ordinary and recovery sources contain ambiguous case or Unicode aliases.");
        }
        internal string Label(string path) {
            TrashSourceFile file;
            if (trashFiles.TryGetValue(path, out file)) return "Device Trash\\" + SafePaths.Relative(file.LogicalPath, Trash.LogicalRoot);
            if (trashRecords.ContainsKey(path)) return "Device Trash record\\" + Path.GetFileName(path);
            return SafePaths.Relative(path, selected);
        }
        internal RemovalIntent NewRemovalIntent(ImportReceipt receipt, string destination, string batch) {
            return new RemovalIntent { ImportId = receipt.Id, CreatedUtc = DateTime.UtcNow.ToString("o"), SourceVolume = receipt.SourceVolume,
                SourceRoot = source, SelectedFolder = selected, DestinationRoot = destination, BatchFolder = batch,
                WindowsTrashSid = Trash == null ? null : Trash.WindowsSid, MacTrashUid = options.MacTrashUid,
                VideosOnly = options.VideosOnly, IncludePreviews = options.IncludePreviews, CleanLayout = options.CleanLayout,
                DeleteOriginals = options.DeleteOriginals, RecoverTrash = options.RecoverTrash };
        }
        internal void LinkRemovalIntent(RemovalIntentFile entry) {
            TrashSourceFile file; TrashRecord record;
            if (trashFiles.TryGetValue(entry.SourcePath, out file)) entry.TrashRecordPath = file.RecordPath;
            if (trashRecords.TryGetValue(entry.SourcePath, out record)) { entry.TrashRecordPath = record.MetadataPath; entry.TrashPayloadPath = record.PayloadRoot; }
        }
        internal ReceiptEntry Entry(string file, string savedRelative, string hash, long bytes, bool reused) {
            return new ReceiptEntry { RelativePath = savedRelative, SourceRelativePath = Label(file), Sha256 = hash, Bytes = bytes, Reused = reused,
                IsMedia = Ordinary.Media.Contains(file) || trashFiles.ContainsKey(file), FromTrash = trashFiles.ContainsKey(file) || trashRecords.ContainsKey(file), IsTrashRecord = trashRecords.ContainsKey(file) };
        }
        internal void CheckCapturedSources(IDictionary<string, Native.FileInfo> stamps, IDictionary<string, string> hashes) {
            foreach (var file in trashFiles.Values) if (!VerifiedImporter.SameStamp(file.Stamp, stamps[file.ActualPath])) throw new SafetyException("A Trash payload changed after its original path was captured. Nothing was cleared.");
            foreach (var record in trashRecords.Values) if (!VerifiedImporter.SameStamp(record.MetadataStamp, stamps[record.MetadataPath]) || record.MetadataSha256 != hashes[record.MetadataPath]) throw new SafetyException("A Trash original-path record changed during planning. Nothing was cleared.");
        }
        internal Dictionary<string, string> Targets(string batch, string volumeId, IDictionary<string, string> hashes, Func<string, string, bool> conflict) {
            var targets = ImportLayout.Plan(Ordinary, source, selected, batch, volumeId, options.CleanLayout, hashes, conflict);
            if (Trash != null && trashFiles.Count != 0) {
                var logicalFiles = trashFiles.Values.ToDictionary(x => x.LogicalPath, x => x.ActualPath, StringComparer.OrdinalIgnoreCase);
                var logicalHashes = logicalFiles.ToDictionary(x => x.Key, x => hashes[x.Value], StringComparer.OrdinalIgnoreCase);
                var recovery = new Dictionary<string, string>();
                ImportLayout.Preserved(logicalFiles.Keys.ToList(), Trash.LogicalRoot, Path.Combine(batch, "Recovered Device Trash"), volumeId + " | Device Trash", logicalHashes, (logical, target) => conflict(logicalFiles[logical], target), recovery);
                foreach (var item in recovery) targets.Add(logicalFiles[item.Key], item.Value);
                foreach (var record in trashRecords.Values) targets.Add(record.MetadataPath, Path.Combine(batch, ".easy-eject-metadata", "Recovered Trash Records", record.MetadataSha256.Substring(0, 24), Path.GetFileName(record.MetadataPath)));
            }
            if (targets.Values.Select(x => x.Normalize(NormalizationForm.FormC)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Count) throw new SafetyException("Import and recovery targets share an ambiguous filename.");
            foreach (string target in targets.Values) { SafePaths.Normalize(target); if (!SafePaths.Within(target, batch)) throw new SafetyException("A recovery target escaped its saved batch."); }
            return targets;
        }
        internal void CheckManifest(Func<string, bool> removed, CancellationToken cancellation, Action validate) {
            var scanned = mediaFolder.Scan(cancellation);
            if (scanned.Any(removed)) throw new SafetyException("A new file appeared at a removed original's path. It is kept; review the device before retrying.");
            var virtualFiles = scanned.Concat(Ordinary.Files.Where(removed)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            var current = MediaSelection.Plan(virtualFiles, source, selected, options).Files.Where(f => !removed(f));
            if (!current.SequenceEqual(Ordinary.Files.Where(f => !removed(f)))) throw new SafetyException("Source selection changed during import/removal. Remaining originals are kept.");
            if (Trash != null) Trash.CheckManifest(options, removed, cancellation, validate);
        }
    }
}
