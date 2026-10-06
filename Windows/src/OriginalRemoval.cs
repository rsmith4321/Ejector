using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    internal sealed class OriginalRemovalFailure : Exception {
        internal readonly int Removed, RemovedOriginals, ClearedTrashFiles, ClearedTrashRecords; internal readonly string Folder, AuditPath;
        internal OriginalRemovalFailure(int removed, Exception cause, string folder, string auditPath, int clearedTrash = 0, int clearedRecords = 0) : base("Import stopped. " + OriginalRemovalChoice.Summary(removed) + (clearedTrash + clearedRecords > 0 ? " Permanently cleared " + clearedTrash + " verified Trash files and " + clearedRecords + " records." : "") + " Remaining originals/Trash files and complete saved copies are kept. No completion receipt or eject permission was issued. " + cause.Message + "\n\nSaved copies: " + folder + "\nRemoval audit: " + auditPath, cause) { Removed = removed + clearedTrash + clearedRecords; RemovedOriginals = removed; ClearedTrashFiles = clearedTrash; ClearedTrashRecords = clearedRecords; Folder = folder; AuditPath = auditPath; }
    }
    // All selected sources and saved dependencies remain held through removal and receipt publication.
    internal sealed class OriginalRemovalSession : IDisposable {
        sealed class Held {
            internal string Source, Saved; internal FileStream Input, Copy; internal Native.FileInfo SourceStamp, CopyStamp;
            internal ReceiptEntry Entry; internal bool Eligible;
        }
        readonly List<Held> held = new List<Held>(); readonly HashSet<string> removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        SafePaths.DirectoryLease directories;
        internal int Removed { get { return removed.Count; } }
        internal bool WasRemoved(string path) { return removed.Contains(path); }
        internal OriginalRemovalSession(string batch, ImportReceipt receipt, IDictionary<string, string> sources, IDictionary<string, Native.FileInfo> stamps, HashSet<string> eligible) {
            try {
                directories = SafePaths.HoldDirectories(receipt.Files.SelectMany(e => new[] { Path.GetDirectoryName(sources[e.RelativePath]), Path.GetDirectoryName(Path.Combine(batch, e.RelativePath)) }).ToArray());
                foreach (var entry in receipt.Files) {
                    string source = sources[entry.RelativePath], saved = SafePaths.Normalize(Path.Combine(batch, entry.RelativePath));
                    if (!SafePaths.Within(saved, batch)) throw new SafetyException("A saved removal dependency escaped its batch.");
                    var item = new Held { Entry = entry, Source = source, Saved = saved, SourceStamp = stamps[source], Eligible = eligible.Contains(source) && !MediaSelection.IsDeviceIndex(source) };
                    held.Add(item); item.Input = SafePaths.ProtectOriginal(source, item.Eligible); item.Copy = SafePaths.ReadFile(saved, 4096); item.CopyStamp = Native.Info(item.Copy.SafeFileHandle);
                }
                CheckPaths();
            } catch { Dispose(); throw; }
        }
        void Verify(Held item, CancellationToken cancellation) {
            if (item.Input != null) {
                if (!VerifiedImporter.SameStamp(Native.Info(item.Input.SafeFileHandle), item.SourceStamp) || !Native.FinalPath(item.Input.SafeFileHandle).Equals(item.Source, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("A source recording changed before batch completion/removal.");
                item.Input.Position = 0; if (VerifiedImporter.Hash(item.Input, cancellation) != item.Entry.Sha256) throw new SafetyException("A selected original no longer matches its saved copy.");
            }
            if (!VerifiedImporter.SameStamp(Native.Info(item.Copy.SafeFileHandle), item.CopyStamp) || !Native.FinalPath(item.Copy.SafeFileHandle).Equals(item.Saved, StringComparison.OrdinalIgnoreCase) || item.Copy.Length != item.Entry.Bytes) throw new SafetyException("An earlier saved copy changed before batch completion/removal.");
            item.Copy.Position = 0; if (VerifiedImporter.Hash(item.Copy, cancellation) != item.Entry.Sha256) throw new SafetyException("An earlier saved copy changed before batch completion/removal.");
        }
        internal void CheckPaths() { directories.Validate(); }
        internal void VerifyAll(CancellationToken cancellation, Action validate) {
            CheckPaths();
            foreach (var item in held) { cancellation.ThrowIfCancellationRequested(); CheckPaths(); validate(); CheckPaths(); cancellation.ThrowIfCancellationRequested(); Verify(item, cancellation); }
            CheckPaths();
        }
        internal RemovalIntent CaptureIntent(ImportSourcePlan plan, ImportReceipt receipt, string destination, string batch, CancellationToken cancellation, IImportGuard guard) {
            VerifyAll(cancellation, guard.Validate);
            var intent = plan.NewRemovalIntent(receipt, destination, batch);
            var evidence = guard as IImportDeviceEvidence;
            if (evidence != null) { intent.SourceDevice = evidence.SourceDevice.Copy(); intent.DestinationDevice = evidence.DestinationDevice.Copy(); intent.SourceDevice.Validate(); intent.DestinationDevice.Validate(); }
            foreach (var item in held) {
                var entry = new RemovalIntentFile { SourcePath = item.Source, SavedRelativePath = item.Entry.RelativePath, SourceLabel = item.Entry.SourceRelativePath,
                    Sha256 = item.Entry.Sha256, Bytes = item.Entry.Bytes, Eligible = item.Eligible, FromTrash = item.Entry.FromTrash, IsTrashRecord = item.Entry.IsTrashRecord,
                    SourceIdentity = RemovalFileIdentity.Capture(Native.Info(item.Input.SafeFileHandle)), SavedIdentity = RemovalFileIdentity.Capture(Native.Info(item.Copy.SafeFileHandle)) };
                plan.LinkRemovalIntent(entry); intent.Files.Add(entry);
            }
            CheckPaths(); return intent;
        }
        internal void Remove(CancellationToken cancellation, Action validate, Action checkManifest, Action<string> audit, Action<string> report) {
            Action check = () => { cancellation.ThrowIfCancellationRequested(); CheckPaths(); validate(); CheckPaths(); cancellation.ThrowIfCancellationRequested(); };
            Action manifest = () => { CheckPaths(); checkManifest(); CheckPaths(); cancellation.ThrowIfCancellationRequested(); };
            Action<string> log = line => { CheckPaths(); audit(line); CheckPaths(); };
            Action<string> progress = line => { CheckPaths(); report(line); CheckPaths(); };
            manifest(); VerifyAll(cancellation, validate);
            log("Complete selected batch and all saved dependencies verified before original removal");
            foreach (var item in held.Where(x => x.Eligible).OrderBy(x => x.Entry.IsTrashRecord)) {
                check(); manifest(); Verify(item, cancellation);
                log("Before permanent removal | SHA256 " + item.Entry.Sha256 + " | " + item.Source + " -> " + item.Saved);
                progress((item.Entry.FromTrash ? "Clearing verified Trash " + (item.Entry.IsTrashRecord ? "record: " : "file: ") : "Removing verified original: ") + item.Entry.SourceRelativePath);
                check(); manifest(); Verify(item, cancellation);
                Native.DeleteOnClose(item.Input.SafeFileHandle, "Cannot remove the held verified original");
                removed.Add(item.Source); item.Entry.RemovedSource = true;
                item.Input.Dispose(); item.Input = null;
                log("Removed verified original | SHA256 " + item.Entry.Sha256 + " | " + item.Source);
            }
            manifest(); VerifyAll(cancellation, validate);
        }
        public void Dispose() {
            foreach (var item in held) { if (item.Input != null) item.Input.Dispose(); if (item.Copy != null) item.Copy.Dispose(); }
            held.Clear(); if (directories != null) { directories.Dispose(); directories = null; }
        }
    }
}
