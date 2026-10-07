using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    internal sealed class SavedBatchDirectories : IDisposable {
        readonly SafePaths.DirectoryLease roots;
        readonly Dictionary<string, SafePaths.DirectoryLease> parents = new Dictionary<string, SafePaths.DirectoryLease>(StringComparer.OrdinalIgnoreCase);
        internal SavedBatchDirectories(CompletedBatch batch, CancellationToken cancel = default(CancellationToken)) {
            try {
                cancel.ThrowIfCancellationRequested();
                roots = SafePaths.HoldDirectories(batch.DestinationRoot, batch.Folder, Path.GetDirectoryName(batch.ReceiptPath));
                foreach (string parent in batch.Originals.Select(x => Path.GetDirectoryName(x.Path)).Distinct(StringComparer.OrdinalIgnoreCase)) {
                    cancel.ThrowIfCancellationRequested(); parents.Add(parent, SafePaths.HoldDirectories(parent));
                }
                cancel.ThrowIfCancellationRequested();
            } catch { Dispose(); throw; }
        }
        internal void Validate() { roots.Validate(); foreach (var parent in parents.Values) parent.Validate(); }
        internal void ValidateOriginal(string path) { roots.Validate(); parents[Path.GetDirectoryName(path)].Validate(); }
        public void Dispose() { foreach (var parent in parents.Values) parent.Dispose(); parents.Clear(); if (roots != null) roots.Dispose(); }
    }
}
