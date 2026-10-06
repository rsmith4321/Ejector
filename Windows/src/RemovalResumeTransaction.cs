using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace EasyEject {
    internal sealed class RemovalResumeContext {
        internal readonly RemovalIntent Intent; internal readonly string Hash, RecordFolder;
        internal readonly Dictionary<string, FileStream> Copies, Inputs;
        internal readonly HashSet<string> Missing, PriorMissing;
        internal readonly Action CheckManifest;
        internal RemovalResumeContext(RemovalIntent intent, string hash, string folder, Dictionary<string, FileStream> copies, Dictionary<string, FileStream> inputs, HashSet<string> missing, Action checkManifest) {
            Intent = intent; Hash = hash; RecordFolder = folder; Copies = copies; Inputs = inputs; Missing = missing; PriorMissing = new HashSet<string>(missing, StringComparer.OrdinalIgnoreCase); CheckManifest = checkManifest;
        }
        internal void Verify(RemovalIntentFile entry, CancellationToken cancellation) {
            FileStream input;
            if (Inputs.TryGetValue(entry.SourcePath, out input) && input != null) {
                if (!entry.SourceIdentity.Matches(Native.Info(input.SafeFileHandle)) || !Native.FinalPath(input.SafeFileHandle).Equals(entry.SourcePath, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("A held resume source changed. Remaining data is kept.");
                input.Position = 0; if (VerifiedImporter.Hash(input, cancellation) != entry.Sha256) throw new SafetyException("A held resume source no longer matches its copy.");
            }
            var copy = Copies[entry.SourcePath]; string target = Path.Combine(Intent.BatchFolder, entry.SavedRelativePath);
            if (!entry.SavedIdentity.Matches(Native.Info(copy.SafeFileHandle)) || !Native.FinalPath(copy.SafeFileHandle).Equals(target, StringComparison.OrdinalIgnoreCase) || copy.Length != entry.Bytes) throw new SafetyException("A complete saved resume dependency changed.");
            copy.Position = 0; if (VerifiedImporter.Hash(copy, cancellation) != entry.Sha256) throw new SafetyException("A complete saved resume dependency no longer matches its hash.");
        }
        internal void VerifyAll(CancellationToken cancellation, Action validate) {
            foreach (var entry in Intent.Files) { cancellation.ThrowIfCancellationRequested(); validate(); Verify(entry, cancellation); }
        }
    }
    internal sealed class RemovalResumeFailure : Exception {
        internal readonly int RemovedOriginals, ClearedTrashFiles, ClearedTrashRecords;
        internal readonly string Folder, AuditPath;
        internal RemovalResumeFailure(ImportResult result, string audit, Exception cause) : base("Resume stopped. This action permanently removed " + result.RemovedOriginals + " verified originals and cleared " + result.ClearedTrashFiles + " Trash files and " + result.ClearedTrashRecords + " restoration records. Files already absent before this action are counted separately. Remaining source data and complete saved copies are kept. No completion or eject authority was issued. " + cause.Message + "\n\nSaved copies: " + result.Folder + "\nResume audit: " + audit, cause) {
            RemovedOriginals = result.RemovedOriginals; ClearedTrashFiles = result.ClearedTrashFiles; ClearedTrashRecords = result.ClearedTrashRecords; Folder = result.Folder; AuditPath = audit;
        }
    }
    internal static class RemovalResumeTransaction {
        internal static ImportResult Run(RemovalResumeContext context, CancellationToken cancellation, Action validate, Action<string> report, Action<string> auditCheck) {
            var intent = context.Intent;
            var receipt = new ImportReceipt { Id = intent.ImportId, StartedUtc = intent.CreatedUtc, SourceVolume = intent.SourceVolume, VideosOnly = intent.VideosOnly, IncludePreviews = intent.IncludePreviews, CleanLayout = intent.CleanLayout, OriginalRemovalRequested = intent.DeleteOriginals, TrashRecoveryRequested = intent.RecoverTrash,
                ResumedUtc = DateTime.UtcNow.ToString("o"), RemovalIntentSha256 = context.Hash };
            var result = new ImportResult { Folder = intent.BatchFolder, Receipt = receipt, Files = intent.Files.Count, Reused = intent.Files.Count,
                PriorAbsentOriginals = intent.Files.Count(x => context.PriorMissing.Contains(x.SourcePath) && !x.FromTrash),
                PriorAbsentTrashFiles = intent.Files.Count(x => context.PriorMissing.Contains(x.SourcePath) && x.FromTrash && !x.IsTrashRecord),
                PriorAbsentTrashRecords = intent.Files.Count(x => context.PriorMissing.Contains(x.SourcePath) && x.IsTrashRecord) };
            string auditPath = Path.Combine(context.RecordFolder, "resume-" + Guid.NewGuid().ToString("N").Substring(0, 16) + "-audit.log");
            using (var auditStream = SafePaths.NewFile(auditPath)) using (var writer = new StreamWriter(auditStream, new UTF8Encoding(false), 4096, true)) {
                Action<string> audit = line => { writer.WriteLine(DateTime.UtcNow.ToString("o") + " " + line); writer.Flush(); auditStream.Flush(true); auditCheck(line); };
                try {
                    cancellation.ThrowIfCancellationRequested(); validate(); context.CheckManifest(); context.VerifyAll(cancellation, validate);
                    audit("Started explicit resume | intent SHA256 " + context.Hash + " | " + intent.ImportId + " | prior absent: " + result.PriorAbsentOriginals + " originals, " + result.PriorAbsentTrashFiles + " Trash files, " + result.PriorAbsentTrashRecords + " records");
                    foreach (var entry in intent.Files.Where(x => x.Eligible && !context.PriorMissing.Contains(x.SourcePath)).OrderBy(x => x.IsTrashRecord)) {
                        cancellation.ThrowIfCancellationRequested(); validate(); context.CheckManifest(); context.Verify(entry, cancellation);
                        audit("Before resumed permanent removal | SHA256 " + entry.Sha256 + " | " + entry.SourcePath + " -> " + Path.Combine(intent.BatchFolder, entry.SavedRelativePath));
                        report((entry.FromTrash ? "Resuming verified Trash " + (entry.IsTrashRecord ? "record: " : "file: ") : "Resuming verified original removal: ") + entry.SourceLabel);
                        cancellation.ThrowIfCancellationRequested(); validate(); context.CheckManifest(); context.Verify(entry, cancellation);
                        var input = context.Inputs[entry.SourcePath]; Native.DeleteOnClose(input.SafeFileHandle, "Cannot remove the held verified resume source");
                        context.Missing.Add(entry.SourcePath);
                        if (!entry.FromTrash) result.RemovedOriginals++; else if (entry.IsTrashRecord) result.ClearedTrashRecords++; else result.ClearedTrashFiles++;
                        context.Inputs[entry.SourcePath] = null; input.Dispose();
                        audit("Removed resumed verified source | SHA256 " + entry.Sha256 + " | " + entry.SourcePath);
                    }
                    cancellation.ThrowIfCancellationRequested(); validate(); context.CheckManifest(); context.VerifyAll(cancellation, validate);
                    foreach (var entry in intent.Files) {
                        bool media = !entry.IsTrashRecord && (entry.FromTrash || !MediaSelection.IsDeviceIndex(entry.SourcePath));
                        receipt.Files.Add(new ReceiptEntry { RelativePath = entry.SavedRelativePath, SourceRelativePath = entry.SourceLabel, Sha256 = entry.Sha256, Bytes = entry.Bytes, Reused = true, IsMedia = media, FromTrash = entry.FromTrash, IsTrashRecord = entry.IsTrashRecord, RemovedSource = context.Missing.Contains(entry.SourcePath), SourceAbsentAtResume = context.PriorMissing.Contains(entry.SourcePath) });
                        result.Bytes = checked(result.Bytes + entry.Bytes);
                    }
                    receipt.HasSelectedMedia = result.HasSelectedMedia = receipt.Files.Any(x => x.IsMedia); receipt.OriginalsKept = !receipt.Files.Any(x => !x.FromTrash && x.RemovedSource);
                    result.RecoveredTrashFiles = receipt.Files.Count(x => x.FromTrash && !x.IsTrashRecord); receipt.ResumeAuditPath = SafePaths.Relative(auditPath, intent.BatchFolder);
                    audit("Complete resume batch verified before receipt | removed this action: " + result.RemovedOriginals + " originals, " + result.ClearedTrashFiles + " Trash files, " + result.ClearedTrashRecords + " records; device remains connected");
                    report("Publishing verified resumed import receipt. Device remains connected.");
                    cancellation.ThrowIfCancellationRequested(); validate(); context.CheckManifest(); context.VerifyAll(cancellation, validate);
                    string target = Path.Combine(context.RecordFolder, "verified-import.json");
                    using (var output = SafePaths.NewFile(Path.Combine(context.RecordFolder, ".resume-receipt-" + Guid.NewGuid().ToString("N").Substring(0, 16) + ".partial"))) {
                        bool complete = false;
                        try { new DataContractJsonSerializer(typeof(ImportReceipt)).WriteObject(output, receipt); output.Flush(true); cancellation.ThrowIfCancellationRequested(); validate(); context.CheckManifest(); Native.Rename(output.SafeFileHandle, target); output.Flush(true); complete = true; }
                        finally { if (!complete) Native.DeleteOnClose(output.SafeFileHandle); }
                    }
                    result.ReceiptPath = target; return result;
                } catch (Exception ex) {
                    try { audit("Resume stopped | removed this action: " + result.RemovedOriginals + " originals, " + result.ClearedTrashFiles + " Trash files, " + result.ClearedTrashRecords + " records | " + ex.Message); } catch {}
                    if (result.RemovedOriginals + result.ClearedTrashFiles + result.ClearedTrashRecords > 0) throw new RemovalResumeFailure(result, auditPath, ex); throw;
                }
            }
        }
    }
}
