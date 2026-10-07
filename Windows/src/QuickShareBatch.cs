using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    internal sealed class QuickShareDirectories : IDisposable {
        readonly SafePaths.DirectoryLease roots;
        readonly Dictionary<string, SafePaths.DirectoryLease> parents = new Dictionary<string, SafePaths.DirectoryLease>(StringComparer.OrdinalIgnoreCase);
        internal QuickShareDirectories(CompletedBatch batch) {
            try {
                roots = SafePaths.HoldDirectories(batch.DestinationRoot, batch.Folder, Path.GetDirectoryName(batch.ReceiptPath));
                foreach (string parent in batch.Originals.Select(x => Path.GetDirectoryName(x.Path)).Distinct(StringComparer.OrdinalIgnoreCase)) parents.Add(parent, SafePaths.HoldDirectories(parent));
            } catch { Dispose(); throw; }
        }
        internal void Validate() { roots.Validate(); foreach (var parent in parents.Values) parent.Validate(); }
        internal void ValidateOriginal(string path) { roots.Validate(); parents[Path.GetDirectoryName(path)].Validate(); }
        public void Dispose() { foreach (var parent in parents.Values) parent.Dispose(); parents.Clear(); if (roots != null) roots.Dispose(); }
    }
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
        internal readonly CompletedBatch Batch; internal readonly List<Original> Originals = new List<Original>();
        readonly IImportGuard guard; readonly CancellationToken cancel; IDisposable reservation; QuickShareDirectories directories; FileStream receipt;
        internal static QuickShareDirectories HoldDirectories(CompletedBatch batch) { return new QuickShareDirectories(batch); }
        internal QuickShareBatch(CompletedBatch batch, DestinationIdentity current, IImportGuard guard, Operations operations, CancellationToken cancel, Action<string> report) {
            this.guard = guard; this.cancel = cancel; Batch = ImportHistory.Clone(batch); ImportHistory.Validate(Batch);
            if (!Batch.Destination.Matches(current)) throw new SafetyException("The saved destination changed. Quick Share is refused.");
            try {
                // Capture every exact parent before the first device callback. Attribute
                // handles alone do not prevent a leaf directory moving on Windows.
                cancel.ThrowIfCancellationRequested(); reservation = operations.Reserve(guard.ProtectedDisks);
                directories = HoldDirectories(Batch);
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
        internal void VerifyOriginal(Original original) { Check(); foreach (var held in Originals) held.VerifyStamp(); original.Verify(cancel); Check(); }
        internal void VerifyAll() { foreach (var original in Originals) { Check(); original.Verify(cancel); } Check(); }
        public void Dispose() { foreach (var original in Originals.AsEnumerable().Reverse()) original.Stream.Dispose(); Originals.Clear(); if (receipt != null) receipt.Dispose(); if (directories != null) directories.Dispose(); if (reservation != null) reservation.Dispose(); }
    }
}
