using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace EasyEject {
    // Review releases handles and grants no authority. Execution verifies the confirmation afresh,
    // then invokes its transaction while the journal, audit, full batch and all paths remain held.
    internal sealed class RemovalResumeEvidence {
        internal readonly ReadOnlyCollection<string> PendingWindowsRecords;
        internal readonly string ImportId, IntentSha256;
        internal readonly string BatchFolder;
        internal readonly MediaOptions Options;
        internal readonly ReadOnlyCollection<string> AbsentSources;
        internal readonly int RemainingOriginals, RemainingTrashFiles, RemainingTrashRecords;
        RemovalResumeEvidence(RemovalIntent intent, string hash, List<string> pending, HashSet<string> missing) {
            ImportId = intent.ImportId; IntentSha256 = hash; PendingWindowsRecords = pending.AsReadOnly(); BatchFolder = intent.BatchFolder;
            Options = new MediaOptions(intent.VideosOnly, intent.IncludePreviews, intent.CleanLayout, false, intent.DeleteOriginals, intent.RecoverTrash, intent.MacTrashUid);
            AbsentSources = missing.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
            RemainingOriginals = intent.Files.Count(x => x.Eligible && !missing.Contains(x.SourcePath) && !x.FromTrash);
            RemainingTrashFiles = intent.Files.Count(x => x.Eligible && !missing.Contains(x.SourcePath) && x.FromTrash && !x.IsTrashRecord);
            RemainingTrashRecords = intent.Files.Count(x => x.Eligible && !missing.Contains(x.SourcePath) && x.IsTrashRecord);
        }
        static void Require(bool value, string explanation) { if (!value) throw new SafetyException("Interrupted recovery evidence refused. " + explanation + " Remaining data is kept."); }
        static bool Exists(string path) {
            string[] entries = Directory.GetFileSystemEntries(Path.GetDirectoryName(path)).Where(x => x.Equals(path, StringComparison.OrdinalIgnoreCase)).ToArray();
            Require(entries.Length <= 1, "A recorded path is ambiguous."); return entries.Length == 1;
        }
        static byte[] RecordBytes(FileStream stream) {
            Require(stream.Length <= 4096, "A restoration record is too large."); stream.Position = 0;
            var data = new byte[(int)stream.Length]; int offset = 0, read;
            while (offset < data.Length && (read = stream.Read(data, offset, data.Length - offset)) > 0) offset += read;
            Require(offset == data.Length, "A restoration record was truncated."); return data;
        }
        internal static RemovalResumeEvidence Review(string intentPath, string sourceRoot, string selectedFolder, string destination, string windowsSid, IImportGuard guard, Operations operations, CancellationToken cancellation) {
            return Inspect(intentPath, sourceRoot, selectedFolder, destination, windowsSid, guard, operations, cancellation, null, null);
        }
        internal static ImportResult Resume(RemovalResumeEvidence confirmed, string intentPath, string sourceRoot, string selectedFolder, string destination, string windowsSid, IImportGuard guard, Operations operations, CancellationToken cancellation, Action<string> report, Action<string> auditCheck = null) {
            if (confirmed == null) throw new ArgumentNullException("confirmed");
            ImportResult result = null;
            Inspect(intentPath, sourceRoot, selectedFolder, destination, windowsSid, guard, operations, cancellation, confirmed, context => result = RemovalResumeTransaction.Run(context, cancellation, report, auditCheck ?? (x => {})));
            return result;
        }
        static RemovalResumeEvidence Inspect(string intentPath, string sourceRoot, string selectedFolder, string destination, string windowsSid, IImportGuard guard, Operations operations, CancellationToken cancellation, RemovalResumeEvidence confirmed, Action<RemovalResumeContext> execute) {
            intentPath = SafePaths.Normalize(intentPath); sourceRoot = SafePaths.Normalize(sourceRoot); selectedFolder = SafePaths.Normalize(selectedFolder); destination = SafePaths.Normalize(destination);
            Require(SafePaths.Within(selectedFolder, sourceRoot) && !SafePaths.Within(destination, sourceRoot) && !SafePaths.Within(sourceRoot, destination) && SafePaths.Within(intentPath, destination) && Path.GetFileName(intentPath) == "removal-intent.json", "Choose this destination's exact removal-intent.json and the original source scope.");
            string selectedRelative = selectedFolder == sourceRoot ? "" : SafePaths.Relative(selectedFolder, sourceRoot);
            Require(!selectedRelative.Split('\\').Any(x => x.Equals(".Trashes", StringComparison.OrdinalIgnoreCase) || x.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase) || x.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)), "The ordinary media folder cannot be a protected Trash/system tree.");
            var device = guard as IImportDeviceEvidence;
            Require(device != null, "A fresh source/destination device guard is required.");
            using (operations.Reserve(guard.ProtectedDisks)) using (var directories = SafePaths.HoldDirectories(sourceRoot, selectedFolder, destination, Path.GetDirectoryName(intentPath)))
            using (var intentStream = SafePaths.ReadFile(intentPath, 4096)) {
                // Parse only bounded held evidence before exposing the first device callback.
                // Its complete dependency parents must already be captured when that callback runs.
                cancellation.ThrowIfCancellationRequested(); directories.Validate(); Require(intentStream.Length <= 16777216, "The journal is too large.");
                string hash = VerifiedImporter.Hash(intentStream, cancellation); intentStream.Position = 0;
                var intent = JsonDocuments.Read<RemovalIntent>(intentStream, 16777216);
                Require(intent != null && intent.Version == 1 && !intent.IsCompletionReceipt && (intent.RecoverTrash || intent.DeleteOriginals), "This is not a supported interrupted verified source-removal operation.");
                Require(confirmed == null || (confirmed.ImportId == intent.ImportId && confirmed.IntentSha256 == hash), "The journal differs from the reviewed batch.");
                Require(intent.SourceDevice != null && intent.DestinationDevice != null && intent.SourceDevice.Matches(device.SourceDevice) && intent.DestinationDevice.Matches(device.DestinationDevice), "The source or destination device changed.");
                Require(intent.SourceVolume == intent.SourceDevice.VolumeId && intent.SourceRoot == sourceRoot && intent.SelectedFolder == selectedFolder && intent.DestinationRoot == destination, "The current device/profile scope differs from the journal.");
                Require(intent.RecoverTrash ? intent.WindowsTrashSid == windowsSid : intent.WindowsTrashSid == null, "The recorded Windows Trash owner differs from the current user.");
                Require(intent.MacTrashUid == null || DeviceTrash.ValidMacOwner(intent.MacTrashUid), "The recorded Mac owner is invalid.");
                Guid id; Require(Guid.TryParseExact(intent.ImportId, "N", out id), "The import identity is invalid.");
                string batch = SafePaths.Normalize(intent.BatchFolder), folder = Path.GetDirectoryName(intentPath);
                Require(Path.GetFileName(intentPath) == "removal-intent.json" && Path.GetFileName(folder) == intent.ImportId && folder == Path.Combine(batch, ".easy-eject-receipts", intent.ImportId) && SafePaths.Within(batch, destination) && batch != destination, "The journal is outside its exact batch record.");
                Require(!Exists(Path.Combine(folder, "verified-import.json")), "This import already has a completion receipt.");
                using (var audit = SafePaths.ReadFile(Path.Combine(folder, "import-audit.log"), 4096)) {
                    Require(audit.Length <= 67108864, "The audit is too large to review.");
                    string binding = "Durable removal intent | SHA256 " + hash + " | " + intentPath + " | no completion or eject authority";
                    int bindings = 0;
                    using (var reader = new StreamReader(audit, Encoding.UTF8, true, 4096, true)) {
                        string line; while ((line = reader.ReadLine()) != null) { cancellation.ThrowIfCancellationRequested(); if (line.EndsWith(binding, StringComparison.Ordinal)) bindings++; }
                    }
                    Require(bindings == 1, "The audit does not bind this exact journal hash.");
                    Require(intent.Files != null && intent.Files.Count > 0 && intent.Files.Count <= 100000, "The dependency list is invalid.");
                    var files = intent.Files; var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var savedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    Require(intent.RecoverTrash || files.All(x => x != null && !x.FromTrash && !x.IsTrashRecord), "A recovery-off batch contains Trash sources.");
                    foreach (var file in files) {
                        Require(file != null && file.SourceIdentity != null && file.SavedIdentity != null && file.Bytes >= 0 && file.Sha256 != null && file.Sha256.Length == 64 && file.Sha256.All(x => (x >= '0' && x <= '9') || (x >= 'a' && x <= 'f')), "A dependency has invalid identity/hash evidence.");
                        string source = SafePaths.Normalize(file.SourcePath), saved = SafePaths.Normalize(Path.Combine(batch, file.SavedRelativePath));
                        Require(source == file.SourcePath && SafePaths.Within(source, sourceRoot) && source != sourceRoot && SafePaths.Within(saved, batch) && saved != batch && file.SavedRelativePath == SafePaths.Relative(saved, batch) && paths.Add(source.Normalize(NormalizationForm.FormC)) && savedPaths.Add(saved.Normalize(NormalizationForm.FormC)), "A dependency escapes its batch or has duplicate aliases.");
                        if (!file.FromTrash) Require(!file.IsTrashRecord && SafePaths.Within(source, selectedFolder) && file.SourceLabel == SafePaths.Relative(source, selectedFolder), "An ordinary source changed selection scope or name.");
                    }
                    using (var fileDirectories = SafePaths.HoldDirectories(files.SelectMany(x => new[] { Path.GetDirectoryName(x.SourcePath), Path.GetDirectoryName(Path.Combine(batch, x.SavedRelativePath)) }).ToArray())) {
                        Action checkPaths = () => { directories.Validate(); fileDirectories.Validate(); };
                        Action validate = () => { cancellation.ThrowIfCancellationRequested(); checkPaths(); guard.Validate(); checkPaths(); cancellation.ThrowIfCancellationRequested(); };
                        validate();
                        var held = new List<FileStream>();
                        try {
                            var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var saved = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase); var inputs = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
                            foreach (var file in files) {
                                validate();
                                var copy = SafePaths.ReadFile(Path.Combine(batch, file.SavedRelativePath), 4096); held.Add(copy); saved.Add(file.SourcePath, copy);
                                Require(file.SavedIdentity.Matches(Native.Info(copy.SafeFileHandle)) && copy.Length == file.Bytes && VerifiedImporter.Hash(copy, cancellation) == file.Sha256, "A complete saved dependency changed.");
                                if (!Exists(file.SourcePath)) { Require(file.Eligible && (file.FromTrash || intent.DeleteOriginals), "A retained source dependency is missing."); missing.Add(file.SourcePath); continue; }
                                var input = execute != null ? SafePaths.ProtectOriginal(file.SourcePath, file.Eligible) : SafePaths.ReadFile(file.SourcePath, 4096); held.Add(input); inputs.Add(file.SourcePath, input);
                                Require(file.SourceIdentity.Matches(Native.Info(input.SafeFileHandle)) && input.Length == file.Bytes && VerifiedImporter.Hash(input, cancellation) == file.Sha256, "A remaining source or restoration record changed.");
                            }
                            var options = new MediaOptions(intent.VideosOnly, intent.IncludePreviews, intent.CleanLayout, false, intent.DeleteOriginals, intent.RecoverTrash, intent.MacTrashUid);
                            var ordinary = files.Where(x => !x.FromTrash).Select(x => x.SourcePath).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                            var virtualOrdinary = VerifiedImporter.Scan(selectedFolder, cancellation).Concat(ordinary.Where(missing.Contains)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                            var ordinaryPlan = MediaSelection.Plan(virtualOrdinary, sourceRoot, selectedFolder, options);
                            Require(ordinaryPlan.Files.SequenceEqual(ordinary), "The selected ordinary source manifest changed.");
                            var ordinaryEligible = MediaSelection.Deletable(ordinaryPlan, options);
                            foreach (var file in files.Where(x => !x.FromTrash)) Require(file.Eligible == (intent.DeleteOriginals && ordinaryEligible.Contains(file.SourcePath)), "Recorded ordinary deletion eligibility differs from media policy.");
                            string logical = Path.Combine(sourceRoot, ".easy-eject-trash-names"), owner = Path.Combine(sourceRoot, "$RECYCLE.BIN", windowsSid);
                            var trashFiles = new List<TrashSourceFile>(); var records = new List<TrashRecord>();
                            foreach (var file in files.Where(x => x.FromTrash && !x.IsTrashRecord)) {
                                Require(file.SourceLabel != null && file.SourceLabel.StartsWith("Device Trash\\", StringComparison.Ordinal), "A recovered original lost its logical path.");
                                string logicalPath = SafePaths.Normalize(Path.Combine(logical, file.SourceLabel.Substring("Device Trash\\".Length)));
                                Require(SafePaths.Within(logicalPath, logical) && logicalPath != logical, "A recovered original has an invalid logical path.");
                                if (file.TrashRecordPath == null) {
                                    Require(intent.MacTrashUid != null && SafePaths.Within(file.SourcePath, Path.Combine(sourceRoot, ".Trashes", intent.MacTrashUid)) && logicalPath == Path.Combine(logical, "Mac Trash", SafePaths.Relative(file.SourcePath, Path.Combine(sourceRoot, ".Trashes", intent.MacTrashUid))), "A Mac Trash original changed owner or mapping.");
                                }
                                trashFiles.Add(new TrashSourceFile(file.SourcePath, logicalPath, file.TrashRecordPath, file.SourceIdentity.Stamp()));
                            }
                            foreach (var file in files.Where(x => x.IsTrashRecord)) {
                                Require(file.FromTrash && file.TrashRecordPath == file.SourcePath && Path.GetDirectoryName(file.SourcePath) == owner && Path.GetFileName(file.SourcePath).StartsWith("$I", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(file.SourcePath).Length > 2 && file.TrashPayloadPath == Path.Combine(owner, "$R" + Path.GetFileName(file.SourcePath).Substring(2)), "A Windows restoration record changed owner or payload mapping.");
                                var info = WindowsTrashRecordInfo.Parse(RecordBytes(saved[file.SourcePath]));
                                var members = trashFiles.Where(x => x.RecordPath == file.SourcePath).ToList(); Require(members.Count > 0, "A restoration record has no recorded payload dependencies.");
                                string logicalRoot = Path.Combine(logical, "Windows Trash", info.OriginalPath.Substring(3));
                                bool directory = members.Count != 1 || members[0].ActualPath != file.TrashPayloadPath;
                                Require(members.All(x => directory ? SafePaths.Within(x.ActualPath, file.TrashPayloadPath) && x.LogicalPath == Path.Combine(logicalRoot, SafePaths.Relative(x.ActualPath, file.TrashPayloadPath)) : x.LogicalPath == logicalRoot), "A Windows payload changed its original name or package ancestry.");
                                Require(directory ? !file.Eligible : files.Single(x => x.SourcePath == file.TrashPayloadPath).Bytes == info.Bytes, "A folder record acquired clearing eligibility or its payload size differs.");
                                records.Add(new TrashRecord(file.SourcePath, file.Sha256, file.SourceIdentity.Stamp(), file.TrashPayloadPath, info, directory, members));
                            }
                            Require(trashFiles.All(x => x.RecordPath == null || records.Any(r => r.MetadataPath == x.RecordPath)), "A Windows payload has no backed-up restoration record.");
                            var snapshot = new DeviceTrashSnapshot(sourceRoot, logical, owner, intent.MacTrashUid == null ? null : Path.Combine(sourceRoot, ".Trashes", intent.MacTrashUid), trashFiles, records, windowsSid, intent.MacTrashUid);
                            var selection = snapshot.Select(options);
                            Require(selection.Files.Count == trashFiles.Count && selection.Records.Count == records.Count, "Recorded recovery selection is inconsistent.");
                            foreach (var file in files.Where(x => x.FromTrash)) Require(file.Eligible == (file.IsTrashRecord ? selection.RemovableRecords.Contains(file.SourcePath) : selection.RemovablePayloads.Contains(file.SourcePath)), "Recorded clearing eligibility differs from media policy.");
                            if (options.RecoverTrash) snapshot.CheckManifest(options, missing.Contains, cancellation, validate);
                            var pending = records.Where(x => !x.Directory && missing.Contains(x.PayloadRoot) && !missing.Contains(x.MetadataPath) && selection.RemovableRecords.Contains(x.MetadataPath)).Select(x => x.MetadataPath).ToList();
                            Require(files.Any(x => x.Eligible) && (missing.Count > 0 || files.Any(x => x.Eligible && inputs.ContainsKey(x.SourcePath))), "No interrupted source removals qualify for review.");
                            Require(confirmed == null || confirmed.AbsentSources.SequenceEqual(missing.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)), "Source presence changed after confirmation; review the batch again.");
                            validate();
                            var reviewed = new RemovalResumeEvidence(intent, hash, pending, missing);
                            if (execute != null) {
                                Action checkManifest = () => {
                                    validate();
                                    var scan = VerifiedImporter.Scan(selectedFolder, cancellation);
                                    Require(!scan.Any(missing.Contains), "A replacement appeared at a removed ordinary source path.");
                                    var virtualFiles = scan.Concat(ordinary.Where(missing.Contains)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                                    Require(MediaSelection.Plan(virtualFiles, sourceRoot, selectedFolder, options).Files.SequenceEqual(ordinary), "The selected ordinary source manifest changed.");
                                    if (options.RecoverTrash) snapshot.CheckManifest(options, missing.Contains, cancellation, validate);
                                    validate();
                                };
                                execute(new RemovalResumeContext(intent, hash, folder, saved, inputs, missing, checkManifest, validate, checkPaths));
                            }
                            // A read-only review must still validate its evidence at final return.
                            // Execution already finished its durable receipt; later source/cancel state
                            // is handled separately by completion/eject admission, as for a new import.
                            if (execute == null) { checkPaths(); cancellation.ThrowIfCancellationRequested(); }
                            return reviewed;
                        } finally { foreach (var handle in held) handle.Dispose(); }
                    }
                }
            }
        }
    }
}
