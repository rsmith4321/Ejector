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
        internal readonly CompletedBatch Batch; internal readonly List<Original> Originals = new List<Original>();
        readonly IImportGuard guard; readonly CancellationToken cancel; IDisposable reservation, directories; FileStream receipt;
        internal QuickShareBatch(CompletedBatch batch, DestinationIdentity current, IImportGuard guard, Operations operations, CancellationToken cancel, Action<string> report) {
            this.guard = guard; this.cancel = cancel; Batch = ImportHistory.Clone(batch); ImportHistory.Validate(Batch);
            if (!Batch.Destination.Matches(current)) throw new SafetyException("The saved destination changed. Quick Share is refused.");
            try {
                Check(); reservation = operations.Reserve(guard.ProtectedDisks);
                directories = SafePaths.LockDirectories(new[] { Batch.DestinationRoot, Batch.Folder, Path.GetDirectoryName(Batch.ReceiptPath) }.Concat(Batch.Originals.Select(x => Path.GetDirectoryName(x.Path))).ToArray());
                Check(); receipt = SafePaths.ReadFile(Batch.ReceiptPath); string hash; ImportHistory.RequireExactReceipt(Batch, ImportHistory.ReadReceipt(receipt, cancel, out hash), hash);
                foreach (var original in Batch.Originals) {
                    Check(); report("Verifying saved " + SafePaths.Relative(original.Path, Batch.Folder)); var stream = SafePaths.ReadFile(original.Path);
                    Original held; try { held = new Original(original, stream); } catch { stream.Dispose(); throw; }
                    Originals.Add(held); held.Verify(cancel);
                }
                Check();
            } catch { Dispose(); throw; }
        }
        internal void Check() { cancel.ThrowIfCancellationRequested(); guard.Validate(); }
        internal void VerifyOriginal(Original original) { Check(); foreach (var held in Originals) held.VerifyStamp(); original.Verify(cancel); Check(); }
        internal void VerifyAll() { foreach (var original in Originals) { Check(); original.Verify(cancel); } Check(); }
        public void Dispose() { foreach (var original in Originals.AsEnumerable().Reverse()) original.Stream.Dispose(); Originals.Clear(); if (receipt != null) receipt.Dispose(); if (directories != null) directories.Dispose(); if (reservation != null) reservation.Dispose(); }
    }
}
