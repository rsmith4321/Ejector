using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyEject;
using Microsoft.Win32.SafeHandles;

// Opt-in fixture support only. Uses the exact supplied backend's held-directory,
// configuration-writer, exclusive file and nonreplace rename contracts.
internal static class OwnedRecordPublication {
    static readonly Assembly Backend = typeof(Profiles).Assembly;
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
    static object Call(Type type, string method, params object[] args) {
        var matches = type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Where(m => m.Name == method && m.GetParameters().Length == args.Length).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Ambiguous backend contract: " + method);
        try { return matches[0].Invoke(null, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    static Type Native { get { return Backend.GetType("EasyEject.Native", true); } }
    sealed class Directories : IDisposable {
        readonly object lease;
        internal Directories(params string[] roots) { lease = Call(typeof(SafePaths), "HoldDirectories", (object)roots); }
        internal void Validate() { lease.GetType().GetMethod("Validate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(lease, null); }
        public void Dispose() { ((IDisposable)lease).Dispose(); }
    }
    internal sealed class Pin {
        public Pin() { }
        public string Identity { get; set; }
        public string Sha256 { get; set; }
        public long Bytes { get; set; }
    }
    internal sealed class Entry {
        public Entry() { }
        public string Name { get; set; }
        public string Temporary { get; set; }
        public string Target { get; set; }
        public string Recovered { get; set; }
        public Pin Expected { get; set; }
    }
    internal sealed class Record {
        internal string Name;
        internal byte[] Bytes;
        internal Record(string name, string text) { Name = name; Bytes = Encoding.UTF8.GetBytes(text); }
    }
    static FileStream New(string path) { return (FileStream)Call(typeof(SafePaths), "NewFile", path); }
    static FileStream Existing(string path) { return (FileStream)Call(Backend.GetType("EasyEject.ConfigurationWriter", true), "OpenPrevious", path); }
    static void Rename(FileStream stream, string path) { Call(Native, "Rename", stream.SafeFileHandle, path); }
    static string Field(object value, string name) { return value.GetType().GetField(name).GetValue(value).ToString(); }
    static string Identity(SafeFileHandle handle) {
        object info = Call(Native, "Info", handle), created = info.GetType().GetField("Created").GetValue(info);
        return String.Join(":", Field(info, "VolumeSerial"), Field(info, "IndexHigh"), Field(info, "IndexLow"), Field(created, "dwHighDateTime"), Field(created, "dwLowDateTime"));
    }
    internal static Pin Capture(FileStream stream) {
        stream.Position = 0; string hash = Convert.ToHexString(SHA256.HashData(stream));
        return new Pin { Identity = Identity(stream.SafeFileHandle), Sha256 = hash, Bytes = stream.Length };
    }
    internal static Pin Capture(string path) { using (var stream = Existing(path)) { if (stream == null) throw new IOException("Expected record absent: " + path); return Capture(stream); } }
    static bool Same(Pin left, Pin right) { return left.Identity == right.Identity && left.Sha256 == right.Sha256 && left.Bytes == right.Bytes; }
    static string Child(string root, string name) {
        if (String.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || !name.EndsWith(".json", StringComparison.Ordinal) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new IOException("Invalid owned record name.");
        string path = SafePaths.Normalize(Path.Combine(root, name));
        if (!SafePaths.Within(path, root)) throw new IOException("Owned record escaped root.");
        return path;
    }
    static void Journal(string ledger, string name, object value) {
        string path = Child(ledger, name);
        using (var stream = New(path)) { byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json); stream.Write(bytes); stream.Flush(true); }
    }
    internal static Dictionary<string, Pin> Baseline(string root) {
        return Directory.GetFiles(root).ToDictionary(Path.GetFileName, Capture, StringComparer.OrdinalIgnoreCase);
    }
    static void CheckBaseline(string root, Dictionary<string, Pin> baseline) {
        string[] names = Directory.GetFiles(root).Select(Path.GetFileName).Where(n => n != ".profile-write.lock").ToArray();
        if (names.Length != baseline.Count || names.Any(n => !baseline.ContainsKey(n))) throw new IOException("Configuration file set changed before publication.");
        foreach (var pair in baseline) if (!Same(pair.Value, Capture(Child(root, pair.Key)))) throw new IOException("Existing configuration changed before publication.");
    }
    internal static void Publish(string root, string ledger, Record[] records, Dictionary<string, Pin> baseline, Action<int> afterRecord = null) {
        root = SafePaths.Normalize(root); ledger = SafePaths.Normalize(ledger);
        if (root.Equals(ledger, StringComparison.OrdinalIgnoreCase) || records.Length == 0 || records.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != records.Length) throw new IOException("Invalid ownership scope.");
        using (var directories = new Directories(root, ledger))
        using (var writer = (IDisposable)Call(typeof(Profiles), "WriterGate", root)) {
            directories.Validate(); CheckBaseline(root, baseline);
            foreach (var record in records) { string target = Child(root, record.Name); if (File.Exists(target) || Directory.Exists(target)) throw new IOException("Publication target already exists."); }
            Journal(ledger, "scope.json", new { Root = root, Ledger = ledger, Names = records.Select(r => r.Name).ToArray(), Baseline = baseline });
            for (int index = 0; index < records.Length; index++) {
                var record = records[index];
                var entry = new Entry { Name = record.Name, Temporary = SafePaths.Normalize(Path.Combine(root, "owned-history-" + Guid.NewGuid().ToString("N") + ".tmp")), Target = Child(root, record.Name), Recovered = Child(ledger, "recovered-" + index + ".json") };
                using (var stream = New(entry.Temporary)) {
                    Journal(ledger, "created-" + index + ".json", new { entry.Name, entry.Temporary, entry.Target, Identity = Identity(stream.SafeFileHandle) });
                    stream.Write(record.Bytes); stream.Flush(true); entry.Expected = Capture(stream);
                    // This durable proof precedes the native rename, so recovery can
                    // locate the exact handle identity at either name after a crash.
                    Journal(ledger, "ready-" + index + ".json", entry);
                    directories.Validate(); Rename(stream, entry.Target); directories.Validate();
                    if (!Same(entry.Expected, Capture(stream))) throw new IOException("Published record changed.");
                    Journal(ledger, "published-" + index + ".json", entry);
                }
                if (afterRecord != null) afterRecord(index);
            }
            directories.Validate(); Journal(ledger, "publication-complete.json", new { Count = records.Length });
        }
    }
    internal static int Recover(string root, string ledger) {
        root = SafePaths.Normalize(root); ledger = SafePaths.Normalize(ledger);
        using (var directories = new Directories(root, ledger))
        using (var writer = (IDisposable)Call(typeof(Profiles), "WriterGate", root)) {
            using var scope = JsonDocument.Parse(File.ReadAllBytes(Child(ledger, "scope.json")));
            if (scope.RootElement.GetProperty("Root").GetString() != root || scope.RootElement.GetProperty("Ledger").GetString() != ledger) throw new IOException("Recovery scope differs.");
            string[] names = scope.RootElement.GetProperty("Names").EnumerateArray().Select(v => v.GetString()).ToArray();
            var held = new List<(Entry Entry, FileStream Stream, bool Recovered)>();
            try {
                for (int index = 0; index < names.Length; index++) {
                    string proof = Child(ledger, "ready-" + index + ".json");
                    if (!File.Exists(proof)) { if (File.Exists(Child(ledger, "created-" + index + ".json"))) throw new IOException("Incomplete private write retained; manual review required."); continue; }
                    Entry entry = JsonSerializer.Deserialize<Entry>(File.ReadAllBytes(proof), Json);
                    if (entry.Name != names[index] || entry.Target != Child(root, names[index]) || entry.Recovered != Child(ledger, "recovered-" + index + ".json") || !SafePaths.Within(entry.Temporary, root) || Path.GetDirectoryName(entry.Temporary) != root || !Path.GetFileName(entry.Temporary).StartsWith("owned-history-", StringComparison.Ordinal) || entry.Expected == null) throw new IOException("Invalid recovery ledger.");
                    FileStream selected = null; bool recovered = false;
                    foreach (string candidate in new[] { entry.Recovered, entry.Temporary, entry.Target }) {
                        FileStream stream = Existing(candidate); if (stream == null) continue;
                        try { if (Same(entry.Expected, Capture(stream))) { selected = stream; stream = null; recovered = candidate == entry.Recovered; break; } }
                        finally { if (stream != null) stream.Dispose(); }
                    }
                    if (selected == null) throw new IOException("Owned record changed or lost; recovery refused.");
                    held.Add((entry, selected, recovered));
                    if (!recovered && (File.Exists(entry.Recovered) || Directory.Exists(entry.Recovered))) throw new IOException("Recovery destination occupied.");
                }
                // Acquire and validate every record before moving the first one.
                directories.Validate(); int moved = 0;
                foreach (var item in held) {
                    if (item.Recovered) continue;
                    if (!Same(item.Entry.Expected, Capture(item.Stream))) throw new IOException("Held recovery record changed.");
                    directories.Validate(); Rename(item.Stream, item.Entry.Recovered); directories.Validate(); moved++;
                }
                return moved;
            } finally { foreach (var item in held) item.Stream.Dispose(); }
        }
    }
}
