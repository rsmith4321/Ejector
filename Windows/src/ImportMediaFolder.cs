using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace EasyEject {
    // A missing saved folder is empty only when a throwing whole-card read proves no ordinary media.
    internal sealed class ImportMediaFolder : IDisposable {
        readonly string source, selected;
        readonly Func<string, string[]> entries;
        readonly List<IDisposable> pins = new List<IDisposable>();
        readonly List<DirectoryProof> identities = new List<DirectoryProof>();
        readonly string missingParent, missingName;
        internal readonly bool Missing;
        internal ImportMediaFolder(string source, string selected, Func<string, string[]> entries = null) {
            this.source = SafePaths.Normalize(source); this.selected = SafePaths.Normalize(selected); this.entries = entries ?? Directory.GetFileSystemEntries;
            if (!SafePaths.Within(this.selected, this.source)) throw new SafetyException("The media folder is outside the selected source.");
            string relative = this.selected.TrimEnd('\\').Equals(this.source.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? "" : SafePaths.Relative(this.selected, this.source);
            if (relative.Split('\\').Any(x => x.Equals(".Trashes", StringComparison.OrdinalIgnoreCase) || x.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase) || x.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)))
                throw new SafetyException("Choose an ordinary complete media folder or card. Device Trash requires the separate owner-aware recovery option.");
            try {
                string current = this.source; Pin(current);
                foreach (string piece in relative.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries)) {
                    ValidateDirectories(); string[] items = this.entries(current); ValidateDirectories();
                    string[] matches = items.Where(path => Path.GetFileName(path).Equals(piece, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matches.Length > 1) throw new SafetyException("The selected media folder has ambiguous case aliases.");
                    if (matches.Length == 0) { missingParent = current; missingName = piece; Missing = true; break; }
                    // Includes regular-directory, canonical-path and no-reparse checks before descending.
                    current = matches[0]; Pin(current);
                }
            } catch { Dispose(); throw; }
        }
        internal List<string> Scan(CancellationToken cancellation) {
            cancellation.ThrowIfCancellationRequested(); ValidateDirectories();
            if (!Missing) { var selectedFiles = VerifiedImporter.Scan(selected, cancellation); ValidateDirectories(); return selectedFiles; }
            RequireStillMissing(); var files = VerifiedImporter.Scan(source, cancellation); RequireStillMissing();
            if (files.Any(file => !MediaSelection.IsDeviceIndex(file))) throw new SafetyException("The saved recording folder is unavailable, but the card contains other files. Choose and review the correct media folder; it is not an empty card.");
            return new List<string>();
        }
        void RequireStillMissing() {
            ValidateDirectories();
            if (entries(missingParent).Any(path => Path.GetFileName(path).Equals(missingName, StringComparison.OrdinalIgnoreCase)))
                throw new SafetyException("The missing recording folder appeared during this review. Its files are kept; review the current selection again.");
        }
        void Pin(string path) { pins.Add(SafePaths.LockDirectories(path)); var proof = new DirectoryProof(path); identities.Add(proof); pins.Add(proof); }
        void ValidateDirectories() { foreach (var proof in identities) proof.Validate(); }
        sealed class DirectoryProof : IDisposable {
            readonly string path; readonly SafeFileHandle handle; readonly Native.FileInfo identity;
            internal DirectoryProof(string path) {
                this.path = SafePaths.Normalize(path); handle = Open(this.path);
                try { identity = Native.Info(handle); CheckPath(handle); } catch { handle.Dispose(); throw; }
            }
            static SafeFileHandle Open(string path) {
                var held = Native.CreateFile(path, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (held.IsInvalid) { held.Dispose(); throw Native.Error("Cannot verify recording directory identity"); } return held;
            }
            void CheckPath(SafeFileHandle held) {
                if ((Native.Info(held).Attributes & 0x10) == 0 || !Native.FinalPath(held).TrimEnd('\\').Equals(path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new SafetyException("The recording directory path changed during review. Files are kept.");
            }
            internal void Validate() {
                CheckPath(handle);
                using (var current = Open(path)) {
                    CheckPath(current); var info = Native.Info(current);
                    if (info.VolumeSerial != identity.VolumeSerial || info.IndexHigh != identity.IndexHigh || info.IndexLow != identity.IndexLow) throw new SafetyException("The recording directory was replaced during review. Files are kept.");
                }
            }
            public void Dispose() { handle.Dispose(); }
        }
        public void Dispose() { foreach (var pin in pins.AsEnumerable().Reverse()) pin.Dispose(); pins.Clear(); identities.Clear(); }
    }
}
