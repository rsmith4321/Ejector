using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace EasyEject {
    internal static class ImportLayout {
        static string Key(string value) { return value.Normalize(NormalizationForm.FormC).ToLowerInvariant(); }
        internal static string Fingerprint(IEnumerable<string> files, string root, string sourceId, IDictionary<string, string> hashes) {
            using (var sha = SHA256.Create()) {
                byte[] prefix = Encoding.UTF8.GetBytes(sourceId); sha.TransformBlock(prefix, 0, prefix.Length, null, 0);
                foreach (string file in files.OrderBy(x => x, StringComparer.Ordinal)) {
                    byte[] line = Encoding.UTF8.GetBytes("\n" + SafePaths.Relative(file, root) + ":" + hashes[file]); sha.TransformBlock(line, 0, line.Length, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0); return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant().Substring(0, 24);
            }
        }
        internal static Dictionary<string, string> Plan(MediaPlan media, string source, string selected, string dated, string sourceId, bool clean, IDictionary<string, string> hashes, Func<string, string, bool> conflict) {
            var result = new Dictionary<string, string>(); var files = media.Media;
            var names = files.Select(x => Key(SafePaths.Relative(x, selected))).ToArray();
            if (names.Distinct().Count() != names.Length) throw new SafetyException("Names differ only by case or Unicode form. Originals are kept; use a camera-specific transfer tool.");
            var recognized = MediaSelection.Recognized(files);
            bool preserved = media.Structured || files.Any(x => !recognized.Contains(x));
            if (preserved) {
                string snapshot = Path.Combine(dated, "Camera originals", Fingerprint(files, selected, sourceId, hashes));
                if (!source.TrimEnd('\\').Equals(selected.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) snapshot = Path.Combine(snapshot, Path.GetFileName(selected.TrimEnd('\\')));
                Preserved(files, selected, snapshot, sourceId, hashes, conflict, result);
            } else if (!clean) Preserved(files, selected, dated, sourceId, hashes, conflict, result);
            else {
                string[] existing = Directory.Exists(dated) ? Directory.GetFiles(dated) : new string[0];
                var associations = files.GroupBy(MediaSelection.Association).ToDictionary(x => x.Key, x => x.Select(Path.GetDirectoryName).Distinct().Count());
                foreach (var group in files.GroupBy(Path.GetDirectoryName).OrderBy(x => x.Key, StringComparer.Ordinal)) {
                    string[] members = group.OrderBy(x => x, StringComparer.Ordinal).ToArray();
                    var keys = new HashSet<string>(members.Select(MediaSelection.Association)); var baseNames = new HashSet<string>(members.Select(x => Key(Path.GetFileName(x))));
                    if (baseNames.Count != members.Length) throw new SafetyException("Filenames differ only by case or Unicode form. Originals are kept.");
                    bool occupied = members.Any(x => associations[MediaSelection.Association(x)] > 1) || existing.Any(x => keys.Contains(MediaSelection.Association(x)) && !baseNames.Contains(Key(Path.GetFileName(x))));
                    foreach (string file in members) if (conflict(file, Path.Combine(dated, Path.GetFileName(file)))) occupied = true;
                    string folder = occupied ? Path.Combine(dated, "Additional media", Fingerprint(members, selected, sourceId, hashes)) : dated;
                    foreach (string file in members) result[file] = Path.Combine(folder, Path.GetFileName(file));
                }
            }
            foreach (string index in media.Indexes) result[index] = Path.Combine(dated, ".easy-eject-metadata", Fingerprint(new[] { index }, selected, sourceId, hashes), SafePaths.Relative(index, selected));
            var targets = result.Values.Select(Key).ToArray();
            if (targets.Distinct().Count() != targets.Length) throw new SafetyException("Import groups would share a filename. Originals are kept.");
            foreach (string path in result.Values) { SafePaths.Normalize(path); if (!SafePaths.Within(path, dated)) throw new SafetyException("Import layout escaped the date folder."); }
            return result;
        }
        static void Preserved(List<string> files, string root, string targetRoot, string sourceId, IDictionary<string, string> hashes, Func<string, string, bool> conflict, Dictionary<string, string> result) {
            bool occupied = false;
            foreach (string file in files) if (conflict(file, Path.Combine(targetRoot, SafePaths.Relative(file, root)))) occupied = true;
            if (occupied) targetRoot = Path.Combine(targetRoot, "Additional media", Fingerprint(files, root, sourceId, hashes));
            foreach (string file in files) result[file] = Path.Combine(targetRoot, SafePaths.Relative(file, root));
        }
    }
}
