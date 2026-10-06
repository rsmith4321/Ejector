using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    internal sealed class OriginalRemovalFailure : Exception {
        internal readonly int Removed; internal readonly string Folder, AuditPath;
        internal OriginalRemovalFailure(int removed, Exception cause, string folder, string auditPath) : base("Import stopped after permanently removing " + removed + " verified originals. Remaining originals and complete saved copies are kept. No completion receipt or eject permission was issued. " + cause.Message + "\n\nSaved copies: " + folder + "\nRemoval audit: " + auditPath, cause) { Removed = removed; Folder = folder; AuditPath = auditPath; }
    }
    // All selected sources and saved dependencies remain held through removal and receipt publication.
    internal sealed class OriginalRemovalSession : IDisposable {
        sealed class Held {
            internal string Source, Saved; internal FileStream Input, Copy; internal Native.FileInfo SourceStamp, CopyStamp;
            internal ReceiptEntry Entry; internal bool Eligible;
        }
        readonly List<Held> held = new List<Held>(); readonly HashSet<string> removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IDisposable directories;
        internal int Removed { get { return removed.Count; } }
        internal bool WasRemoved(string path) { return removed.Contains(path); }
        internal OriginalRemovalSession(string batch, ImportReceipt receipt, IDictionary<string, string> sources, IDictionary<string, Native.FileInfo> stamps, HashSet<string> eligible) {
            try {
                directories = SafePaths.LockDirectories(receipt.Files.SelectMany(e => new[] { Path.GetDirectoryName(sources[e.RelativePath]), Path.GetDirectoryName(Path.Combine(batch, e.RelativePath)) }).ToArray());
                foreach (var entry in receipt.Files) {
                    string source = sources[entry.RelativePath], saved = SafePaths.Normalize(Path.Combine(batch, entry.RelativePath));
                    if (!SafePaths.Within(saved, batch)) throw new SafetyException("A saved removal dependency escaped its batch.");
                    var item = new Held { Entry = entry, Source = source, Saved = saved, SourceStamp = stamps[source], Eligible = eligible.Contains(source) && !MediaSelection.IsDeviceIndex(source) };
                    held.Add(item); item.Input = SafePaths.ProtectOriginal(source, item.Eligible); item.Copy = SafePaths.ReadFile(saved, 4096); item.CopyStamp = Native.Info(item.Copy.SafeFileHandle);
                }
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
        internal void VerifyAll(CancellationToken cancellation, Action validate) { foreach (var item in held) { cancellation.ThrowIfCancellationRequested(); validate(); Verify(item, cancellation); } }
        internal void Remove(CancellationToken cancellation, Action validate, Action checkManifest, Action<string> audit, Action<string> report) {
            checkManifest(); VerifyAll(cancellation, validate);
            audit("Complete selected batch and all saved dependencies verified before original removal");
            foreach (var item in held.Where(x => x.Eligible)) {
                cancellation.ThrowIfCancellationRequested(); validate(); checkManifest(); Verify(item, cancellation);
                audit("Before permanent removal | SHA256 " + item.Entry.Sha256 + " | " + item.Source + " -> " + item.Saved);
                report("Removing verified original: " + item.Entry.SourceRelativePath);
                cancellation.ThrowIfCancellationRequested(); validate(); checkManifest(); Verify(item, cancellation);
                Native.DeleteOnClose(item.Input.SafeFileHandle, "Cannot remove the held verified original");
                removed.Add(item.Source); item.Entry.RemovedSource = true;
                item.Input.Dispose(); item.Input = null;
                audit("Removed verified original | SHA256 " + item.Entry.Sha256 + " | " + item.Source);
            }
            checkManifest(); VerifyAll(cancellation, validate);
        }
        public void Dispose() {
            foreach (var item in held) { if (item.Input != null) item.Input.Dispose(); if (item.Copy != null) item.Copy.Dispose(); }
            held.Clear(); if (directories != null) { directories.Dispose(); directories = null; }
        }
    }
}
