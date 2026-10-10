using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    // Exact completed history permits reading saved copies only; it never restores source/eject rights.
    internal sealed class QuickShareBatch : IDisposable {
        internal sealed class Original {
            internal readonly VerifiedCopy Record; internal readonly FileStream Stream; internal readonly Native.FileInfo Stamp;
            internal Original(VerifiedCopy record, FileStream stream) { Record = record; Stream = stream; Stamp = Native.Info(stream.SafeFileHandle); }
            internal void VerifyStamp() {
                if (!VerifiedImporter.SameStamp(Stamp, Native.Info(Stream.SafeFileHandle)) || Stream.Length != Record.Bytes || !Native.FinalPath(Stream.SafeFileHandle).Equals(Record.Path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("A saved original's identity or size changed. Quick Share is refused.");
            }
            internal void Verify(CancellationToken cancel) {
                VerifyStamp();
                Stream.Position = 0; if (ImportHistory.Hash(Stream, cancel) != Record.Sha256) throw new SafetyException("A saved original changed. Quick Share is refused.");
                VerifyStamp();
            }
        }
        // A Gyroflow export beside a held DJI O4 Pro original. It is not part of the
        // verified import; its bytes are hashed once here and held through publication.
        internal sealed class Export {
            internal readonly VerifiedCopy Record; internal readonly Original Master; internal readonly FileStream Stream; internal readonly Native.FileInfo Stamp;
            internal Export(Original master, string path, FileStream stream, CancellationToken cancel) {
                Master = master; Stream = stream; Stamp = Native.Info(stream.SafeFileHandle);
                if (stream.Length <= 0) throw new SafetyException("A Gyroflow export is empty. Export it again before Quick Share.");
                stream.Position = 0; Record = new VerifiedCopy { Path = path, Bytes = stream.Length, Sha256 = ImportHistory.Hash(stream, cancel) };
                VerifyStamp();
            }
            internal void VerifyStamp() {
                if (!VerifiedImporter.SameStamp(Stamp, Native.Info(Stream.SafeFileHandle)) || Stream.Length != Record.Bytes || !Native.FinalPath(Stream.SafeFileHandle).Equals(Record.Path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("A Gyroflow export's identity or size changed. Quick Share is refused.");
            }
            internal void Verify(CancellationToken cancel) {
                VerifyStamp();
                Stream.Position = 0; if (ImportHistory.Hash(Stream, cancel) != Record.Sha256) throw new SafetyException("A Gyroflow export changed. Quick Share is refused.");
                VerifyStamp();
            }
        }
        internal readonly CompletedBatch Batch; internal readonly List<Original> Originals = new List<Original>(); internal readonly List<Export> Exports = new List<Export>();
        readonly IImportGuard guard; readonly CancellationToken cancel; IDisposable reservation; SavedBatchDirectories directories; FileStream receipt;
        internal static SavedBatchDirectories HoldDirectories(CompletedBatch batch, CancellationToken cancel = default(CancellationToken)) { return new SavedBatchDirectories(batch, cancel); }
        internal QuickShareBatch(CompletedBatch batch, DestinationIdentity current, IImportGuard guard, Operations operations, CancellationToken cancel, Action<string> report) {
            this.guard = guard; this.cancel = cancel; Batch = ImportHistory.Clone(batch); ImportHistory.Validate(Batch);
            if (!Batch.Destination.Matches(current)) throw new SafetyException("The saved destination changed. Quick Share is refused.");
            try {
                // Capture every exact parent before the first device callback. Attribute
                // handles alone do not prevent a leaf directory moving on Windows.
                cancel.ThrowIfCancellationRequested(); directories = HoldDirectories(Batch, cancel); directories.Validate();
                reservation = operations.Reserve(guard.ProtectedDisks); directories.Validate();
                Check(); receipt = SafePaths.ReadFile(Batch.ReceiptPath); string hash; ImportHistory.RequireExactReceipt(Batch, ImportHistory.ReadReceipt(receipt, cancel, out hash), hash);
                foreach (var original in Batch.Originals) {
                    // Every parent was captured together; recheck the current one while
                    // opening originals, then all at the boundary. Avoid a quadratic
                    // directory walk when a saved batch has many distinct parents.
                    CheckOriginalParent(original.Path); report("Verifying saved " + SafePaths.Relative(original.Path, Batch.Folder)); directories.ValidateOriginal(original.Path); cancel.ThrowIfCancellationRequested(); var stream = SafePaths.ReadFile(original.Path);
                    Original held; try { held = new Original(original, stream); } catch { stream.Dispose(); throw; }
                    Originals.Add(held); held.Verify(cancel);
                }
                Check();
            } catch { Dispose(); throw; }
        }
        internal void Check() { cancel.ThrowIfCancellationRequested(); directories.Validate(); guard.Validate(); directories.Validate(); cancel.ThrowIfCancellationRequested(); }
        void CheckOriginalParent(string path) { cancel.ThrowIfCancellationRequested(); directories.ValidateOriginal(path); guard.Validate(); directories.ValidateOriginal(path); cancel.ThrowIfCancellationRequested(); }
        internal void VerifyOriginal(Original original) { Check(); foreach (var held in Originals) held.VerifyStamp(); foreach (var held in Exports) held.VerifyStamp(); original.Verify(cancel); Check(); }
        internal void VerifyExport(Export export) { VerifyOriginal(export.Master); export.Verify(cancel); Check(); }
        internal void VerifyAll() { foreach (var original in Originals) { Check(); original.Verify(cancel); } foreach (var export in Exports) { Check(); export.Verify(cancel); } Check(); }
        // Present only when the exact default-named export exists in the master's held folder.
        internal Export HoldExport(Original master) {
            if (!Originals.Contains(master)) throw new SafetyException("Gyroflow exports must belong to a held original in this batch.");
            string path = QuickShareMedia.GyroflowExportPath(master.Record.Path);
            if (!SafePaths.Within(path, Batch.Folder) || Originals.Any(x => x.Record.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) || Exports.Any(x => x.Record.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) throw new SafetyException("Choose a Gyroflow export matching a verified DJI O4 Pro original in this import.");
            CheckOriginalParent(path); var stream = SafePaths.ReadFile(path);
            Export held; try { held = new Export(master, path, stream, cancel); } catch { stream.Dispose(); throw; }
            Exports.Add(held); CheckOriginalParent(path); return held;
        }
        public void Dispose() { foreach (var export in Exports.AsEnumerable().Reverse()) export.Stream.Dispose(); Exports.Clear(); foreach (var original in Originals.AsEnumerable().Reverse()) original.Stream.Dispose(); Originals.Clear(); if (receipt != null) receipt.Dispose(); if (directories != null) directories.Dispose(); if (reservation != null) reservation.Dispose(); }
    }
}
