using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32.SafeHandles;

namespace EasyEject {
    public static class SafePaths {
        public static string Normalize(string path) {
            if (String.IsNullOrWhiteSpace(path) || path.Length < 3 || !Char.IsLetter(path[0]) || path[1] != ':' || path[2] != '\\')
                throw new SafetyException("Choose an absolute folder on a local drive. Network and device paths are unsupported.");
            if (path.Length > 240 || path.Substring(2).Contains(":")) throw new SafetyException("Long paths and alternate streams are unsupported.");
            foreach (string part in path.Substring(3).Split('\\')) {
                if (part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ")) throw new SafetyException("Ambiguous path components are not allowed.");
            }
            string full = Path.GetFullPath(path).TrimEnd('\\'); if (full.Length == 2) full += "\\";
            return full;
        }
        public static bool Within(string child, string parent) {
            return child.Equals(parent, StringComparison.OrdinalIgnoreCase) || child.StartsWith(parent.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }
        public static string Relative(string file, string root) {
            if (!Within(file, root) || file.Equals(root, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Source path escaped the selected folder.");
            return file.Substring(root.TrimEnd('\\').Length + 1);
        }
        public static IDisposable LockDirectories(params string[] directories) { return new DirectoryLease(directories); }
        internal static DirectoryLease HoldDirectories(params string[] directories) { return new DirectoryLease(directories); }
        internal sealed class DirectoryLease : IDisposable {
            sealed class Entry { internal string Path; internal SafeFileHandle Handle; internal Native.FileInfo Identity; }
            readonly List<Entry> entries = new List<Entry>(); bool disposed;
            internal DirectoryLease(IEnumerable<string> paths) {
                try {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string input in paths) {
                        string path = Normalize(input), root = Path.GetPathRoot(path), current = root;
                        var pieces = new List<string> { root }; foreach (string part in path.Substring(root.Length).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries)) { current = Path.Combine(current, part); pieces.Add(current); }
                        foreach (string piece in pieces) {
                            if (!seen.Add(piece)) continue;
                            // Hold directory attributes without write/delete sharing. Empty directories can
                            // still be renamed on Windows; users of a long-lived path must recheck identity.
                            var h = Native.CreateFile(piece, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                            if (h.IsInvalid) { h.Dispose(); throw Native.Error("Cannot lock import directory: " + piece); }
                            var entry = new Entry { Path = piece, Handle = h }; entries.Add(entry); var info = Native.Info(h); entry.Identity = info;
                            if ((info.Attributes & 0x10) == 0) throw new SafetyException("Import path is not a directory.");
                            string resolved = Native.FinalPath(h);
                            if (!resolved.TrimEnd('\\').Equals(piece.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Directory resolves to another path: " + piece + " -> " + resolved);
                        }
                    }
                } catch { Dispose(); throw; }
            }
            internal void Validate() {
                if (disposed) throw new ObjectDisposedException("DirectoryLease");
                foreach (var entry in entries) {
                    Check(entry.Handle, entry);
                    using (var current = Native.CreateFile(entry.Path, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero)) {
                        if (current.IsInvalid) throw Native.Error("Cannot recheck held directory: " + entry.Path);
                        Check(current, entry);
                    }
                }
            }
            internal Native.FileInfo Identity(string path) {
                Validate(); path = Normalize(path);
                foreach (var entry in entries) if (entry.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) return entry.Identity;
                throw new SafetyException("The directory is not owned by this review.");
            }
            static void Check(SafeFileHandle handle, Entry entry) {
                var info = Native.Info(handle);
                if ((info.Attributes & 0x10) == 0 || info.VolumeSerial != entry.Identity.VolumeSerial || info.IndexHigh != entry.Identity.IndexHigh || info.IndexLow != entry.Identity.IndexLow || !Native.FinalPath(handle).TrimEnd('\\').Equals(entry.Path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    throw new SafetyException("A held directory moved or was replaced. Existing files are kept: " + entry.Path);
            }
            public void Dispose() { disposed = true; foreach (var entry in entries.AsEnumerable().Reverse()) entry.Handle.Dispose(); entries.Clear(); }
        }
        internal static FileStream ReadFile(string path, int bufferSize = 1048576) {
            var h = Native.CreateFile(path, 0x80000000, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); throw Native.Error("Cannot open source without following links"); }
            try {
                var info = Native.Info(h); if ((info.Attributes & 0x10) != 0) throw new SafetyException("Expected a regular source file.");
                if (!Native.FinalPath(h).Equals(path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Opened file resolves outside its planned path.");
                return new FileStream(h, FileAccess.Read, bufferSize, false);
            } catch { h.Dispose(); throw; }
        }
        internal static FileStream ProtectOriginal(string path, bool remove) {
            var h = Native.CreateFile(path, remove ? 0x80010000U : 0x80000000U, remove ? 0U : 1U, IntPtr.Zero, 3, 0x00300000, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); throw Native.Error("Cannot hold source for complete-batch verification/removal"); }
            try {
                var info = Native.Info(h);
                if ((info.Attributes & 0x10) != 0 || !Native.FinalPath(h).Equals(path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("The held source is not the expected regular file.");
                if (remove && ((info.Attributes & 0x1005) != 0 || info.Links != 1)) throw new SafetyException("Read-only, system, offline or hard-linked originals cannot be removed. No protection is changed.");
                return new FileStream(h, FileAccess.Read, 4096, false);
            } catch { h.Dispose(); throw; }
        }
        internal static FileStream NewFile(string path) {
            // DELETE permission permits handle-based, exclusive finalization while the file remains locked.
            var h = Native.CreateFile(path, 0xC0010000, 0, IntPtr.Zero, 1, 0x80200000, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); throw Native.Error("Cannot create exclusive destination file"); }
            try { Native.Info(h); return new FileStream(h, FileAccess.ReadWrite, 1048576, false); } catch { h.Dispose(); throw; }
        }
        internal static FileStream ExistingCopy(string path) {
            var h = Native.CreateFile(path, 0xC0000000, 1, IntPtr.Zero, 3, 0x80200000, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); throw Native.Error("Cannot open saved copy for verification and flush"); }
            try {
                var info = Native.Info(h); if ((info.Attributes & 0x10) != 0 || !Native.FinalPath(h).Equals(path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Saved copy is not a regular file at its expected path.");
                return new FileStream(h, FileAccess.ReadWrite, 1048576, false);
            } catch { h.Dispose(); throw; }
        }
    }
}
