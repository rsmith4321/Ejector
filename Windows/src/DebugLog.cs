using System;
using System.IO;
using System.Text;
using System.Threading;

namespace EasyEject {
    internal sealed class DebugLogSnapshot {
        internal readonly string Text, Hash;
        internal readonly bool Exists;
        internal readonly Native.FileInfo Stamp;
        internal DebugLogSnapshot() { Text = "No debug events have been recorded."; }
        internal DebugLogSnapshot(string text, string hash, Native.FileInfo stamp) { Text = text; Hash = hash; Stamp = stamp; Exists = true; }
    }
    internal static class DebugLog {
        const long MaximumBytes = 1048576;
        static readonly object gate = new object();
        static void CheckFile(FileStream stream, bool writing) {
            var info = Native.Info(stream.SafeFileHandle);
            if (info.Links != 1 || (info.Attributes & (writing ? 0x1005 : 0x1004)) != 0)
                throw new SafetyException("Debug log is hard-linked, offline or protected. Its protections and contents were kept.");
            if (stream.Length > MaximumBytes) throw new SafetyException("Debug log exceeds 1 MiB. Existing diagnostics were kept.");
        }
        static string LogPath(string root) {
            string path = Path.Combine(root, "debug.log");
            if (Directory.Exists(path)) throw new SafetyException("Debug log is a directory instead of a private log file.");
            return path;
        }
        internal static void Append(string root, string message) {
            root = SafePaths.Normalize(root);
            lock (gate) using (SafePaths.LockDirectories(root)) {
                string path = LogPath(root);
                using (var stream = File.Exists(path) ? SafePaths.ExistingCopy(path) : SafePaths.NewFile(path)) {
                    CheckFile(stream, true);
                    byte[] line = Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("o") + " " + message.Replace("\r", " ").Replace("\n", " ") + Environment.NewLine);
                    if (stream.Length + line.Length > MaximumBytes) throw new SafetyException("Debug log reached 1 MiB. Existing diagnostics were kept.");
                    stream.Position = stream.Length; stream.Write(line, 0, line.Length); stream.Flush(true);
                }
            }
        }
        internal static string Read(string root) { return Load(root).Text; }
        internal static DebugLogSnapshot Load(string root) {
            root = SafePaths.Normalize(root);
            lock (gate) using (SafePaths.LockDirectories(root)) {
                string path = LogPath(root); if (!File.Exists(path)) return new DebugLogSnapshot();
                using (var stream = SafePaths.ReadFile(path)) return ReadSnapshot(stream);
            }
        }
        static DebugLogSnapshot ReadSnapshot(FileStream stream) {
            CheckFile(stream, false); var stamp = Native.Info(stream.SafeFileHandle);
            string hash = ImportHistory.Hash(stream, CancellationToken.None); stream.Position = 0;
            // Leave the held file open until both text and exact identity have been captured.
            using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true)) return new DebugLogSnapshot(reader.ReadToEnd(), hash, stamp);
        }
        internal static void Clear(DebugLogSnapshot expected, string root) {
            root = SafePaths.Normalize(root);
            lock (gate) using (SafePaths.LockDirectories(root)) {
                string path = LogPath(root);
                if (expected == null || expected.Exists != File.Exists(path)) throw Changed();
                if (!expected.Exists) return;
                // Only the displayed, exclusive, single-link log is eligible. No directory sweep.
                using (var stream = SafePaths.ProtectOriginal(path, true)) {
                    DebugLogSnapshot current = ReadSnapshot(stream);
                    if (expected.Hash != current.Hash || !VerifiedImporter.SameStamp(expected.Stamp, current.Stamp)) throw Changed();
                    Native.DeleteOnClose(stream.SafeFileHandle, "Cannot clear the displayed private debug log");
                }
            }
        }
        static SafetyException Changed() { return new SafetyException("Debug log changed while this window was open. Refresh and review the current log before clearing it."); }
    }
}
