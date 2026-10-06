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
        public static IDisposable LockDirectories(params string[] directories) { return new DirectoryLocks(directories); }
        sealed class DirectoryLocks : IDisposable {
            readonly List<SafeFileHandle> handles = new List<SafeFileHandle>();
            public DirectoryLocks(IEnumerable<string> paths) {
                try {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string input in paths) {
                        string path = Normalize(input), root = Path.GetPathRoot(path), current = root;
                        var pieces = new List<string> { root }; foreach (string part in path.Substring(root.Length).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries)) { current = Path.Combine(current, part); pieces.Add(current); }
                        foreach (string piece in pieces) {
                            if (!seen.Add(piece)) continue;
                            // Deny write/delete sharing on directories to pin the path and prevent reparse/rename races.
                            var h = Native.CreateFile(piece, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                            if (h.IsInvalid) { h.Dispose(); throw Native.Error("Cannot lock import directory: " + piece); }
                            handles.Add(h); var info = Native.Info(h);
                            if ((info.Attributes & 0x10) == 0) throw new SafetyException("Import path is not a directory.");
                            string resolved = Native.FinalPath(h);
                            if (!resolved.TrimEnd('\\').Equals(piece.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Directory resolves to another path: " + piece + " -> " + resolved);
                        }
                    }
                } catch { Dispose(); throw; }
            }
            public void Dispose() { foreach (var h in handles.AsEnumerable().Reverse()) h.Dispose(); handles.Clear(); }
        }
        internal static FileStream ReadFile(string path) {
            var h = Native.CreateFile(path, 0x80000000, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); throw Native.Error("Cannot open source without following links"); }
            try {
                var info = Native.Info(h); if ((info.Attributes & 0x10) != 0) throw new SafetyException("Expected a regular source file.");
                if (!Native.FinalPath(h).Equals(path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Opened file resolves outside its planned path.");
                return new FileStream(h, FileAccess.Read, 1048576, false);
            } catch { h.Dispose(); throw; }
        }
        internal static FileStream NewFile(string path) {
            // DELETE permission permits handle-based, exclusive finalization while the file remains locked.
            var h = Native.CreateFile(path, 0xC0010000, 0, IntPtr.Zero, 1, 0x80200000, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); throw Native.Error("Cannot create exclusive destination file"); }
            try { Native.Info(h); return new FileStream(h, FileAccess.ReadWrite, 1048576, false); } catch { h.Dispose(); throw; }
        }
    }
}
