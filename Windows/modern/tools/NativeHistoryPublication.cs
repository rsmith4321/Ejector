using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EasyEject;

internal static class NativeHistoryPublication {
    const string Backend = "F810DFB734CF47AE09FA8A9558DA5F819056FE059692CE31857ABE2AF1F35FAD";
    const string Exe = "786AD10735F65BBC544B76E1F6EA5DC6E1E8D34153EDA6165B048B245C434E2E";
    const string Manifest = "76F6D2C0109ED12C7D6042D6DFA504AC32B1AE3F4A7B4538856C5B551169A7A2";
    const string Profile = "F9CA0F4C7A33916BFDC6D65FED170C0D4A982F9657D508BE5018071E87038EC1";
    const string History = "C3CDE0E7D39437A79CBB339A8BB80D271AB27F3E5D32BFE7EE2C1819D4CDDB97";
    const string Receipt = "43741D791FFCAAA3BFF55F6358D06EC1962BA427106953970B35AC226E7D6A29";
    const string Original = "E8DCD8BB1916DB8933332A811C98931973A4D3293CA49BD563A0DD9D449AA03B";
    const string Issue = "2DA0274519483D90BBC19964F254B4A2A4E651A40F24726267A27FC6C3C85BC0";
    const string Tools = "C366C5017700613C17F734FFC01C8AEACC0F386B61EF19558FD8DE57EE94728D";
    const string ProfileName = "50FB7A97E4A3092B2441228AF78A80382AB367C4CD2C5CD6A24CEBB480ECCDF4.json";
    static readonly string Owned = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectBuildTools");
    static readonly string Fixture = Path.Combine(Owned, "NativeHistoryVideo-20261008", "fixture");
    static readonly string App = Path.Combine(Owned, "PowerConnectionAtomic-20261008", "review", "app");
    static readonly string Config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Packages", "OpenAI.Codex_2p2nqsd0c76g0", "LocalCache", "Local", "EasyEject", "Profiles");
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };
    internal sealed class Admission {
        public Admission() { }
        public string BackendHash { get; set; } = Backend;
        public string ExeHash { get; set; } = Exe;
        public string ManifestHash { get; set; } = Manifest;
        public string ProfileHash { get; set; } = Profile;
        public string HistoryHash { get; set; } = History;
        public string ReceiptHash { get; set; } = Receipt;
        public string FirstHash { get; set; } = Original;
        public string SecondHash { get; set; } = Original;
        public string IssueHash { get; set; } = Issue;
        public string ToolsHash { get; set; } = Tools;
        public int ConfigCount { get; set; } = 2;
        public int Pid { get; set; } = 21224;
        public int ExpectedPid { get; set; } = 21224;
        public string StartedUtc { get; set; } = "2026-10-09T02:46:46.1826671Z";
        public string ExpectedStartedUtc { get; set; } = "2026-10-09T02:46:46.1826671Z";
        public long Window { get; set; } = 1967754;
        public long ExpectedWindow { get; set; } = 1967754;
        public double IdleAgeSeconds { get; set; } = 0;
        public bool Idle { get; set; } = true;
        public bool ExactRoots { get; set; } = true;
        public bool SourceAbsent { get; set; } = true;
        public bool TargetsAbsent { get; set; } = true;
        public bool OriginalsMatch { get; set; } = true;
    }
    static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
    static string Hash(string path) { return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))); }
    static string Hash(byte[] bytes) { return Convert.ToHexString(SHA256.HashData(bytes)); }
    // Exclusive backend readers retain canonical, non-reparse file identities and
    // checked bytes through the publication handoff. Never reread by filename.
    sealed class Captured : IDisposable {
        readonly IDisposable directories;
        readonly List<(FileStream Stream, OwnedRecordPublication.Pin Pin)> held = new();
        readonly List<IDisposable> fileDirectories = new();
        bool disposed;
        internal Admission Value;
        internal DateTimeOffset ProofCapturedUtc;
        internal byte[] ProfileBytes, HistoryBytes;
        internal Captured(params string[] roots) { directories = SafePaths.LockDirectories(roots); }
        internal byte[] Read(string path, string expectedHash = null) {
            Require(!disposed, "Captured ownership already released.");
            fileDirectories.Add(SafePaths.LockDirectories(Path.GetDirectoryName(path)));
            var stream = OwnedRecordPublication.OpenRecord(path); held.Add((stream, OwnedRecordPublication.Capture(stream)));
            byte[] bytes = OwnedRecordPublication.ReadRecord(stream);
            Require(expectedHash == null || Hash(bytes) == expectedHash, "Captured source differs: " + path);
            return bytes;
        }
        internal OwnedRecordPublication.Record[] Records() {
            Require(!disposed, "Captured ownership already released.");
            if (Value != null) {
                using var process = Process.GetProcessById(Value.ExpectedPid);
                Require(process.StartTime.ToUniversalTime().ToString("o") == Value.ExpectedStartedUtc && process.MainWindowHandle.ToInt64() == Value.ExpectedWindow && process.MainModule.FileName.Equals(Path.Combine(App, "EasyEject.Modern.exe"), StringComparison.OrdinalIgnoreCase), "Current app changed before publication.");
                Value.IdleAgeSeconds = (DateTimeOffset.UtcNow - ProofCapturedUtc).TotalSeconds;
                Validate(Value, true);
            }
            foreach (var item in held) { var pin = OwnedRecordPublication.Capture(item.Stream); Require(pin.Identity == item.Pin.Identity && pin.Bytes == item.Pin.Bytes && pin.Sha256 == item.Pin.Sha256, "Held source changed."); }
            Require(Hash(ProfileBytes) == Profile && Hash(HistoryBytes) == History, "Admitted publication payload changed.");
            return new[] { new OwnedRecordPublication.Record(ProfileName, ProfileBytes), new OwnedRecordPublication.Record("completed-imports.json", HistoryBytes) };
        }
        public void Dispose() { if (disposed) return; disposed = true; foreach (var item in held) item.Stream.Dispose(); foreach (var lease in fileDirectories) lease.Dispose(); directories.Dispose(); }
    }
    static void Validate(Admission value, bool publishing) {
        Require(value != null, "Missing admission record.");
        Require(value.BackendHash == Backend && value.ExeHash == Exe && value.ManifestHash == Manifest, "Current backend/application binding differs.");
        Require(value.ProfileHash == Profile && value.HistoryHash == History && value.ReceiptHash == Receipt && value.FirstHash == Original && value.SecondHash == Original && value.OriginalsMatch, "Prepared fixture or originals changed.");
        Require(value.Pid > 0 && value.Pid == value.ExpectedPid && value.StartedUtc == value.ExpectedStartedUtc, "Current PID/start binding differs.");
        Require(value.Window > 0 && value.Window == value.ExpectedWindow && value.Idle && value.IdleAgeSeconds >= -5 && value.IdleAgeSeconds <= 60, "Fresh native idle proof required.");
        Require(value.ExactRoots && value.SourceAbsent, "Fixture/configuration scope differs or source returned.");
        Require(value.ToolsHash == Tools, "Existing tool setup changed.");
        if (publishing) Require(value.ConfigCount == 2 && value.IssueHash == Issue && value.TargetsAbsent, "Initial live configuration or publication targets changed.");
    }
    static Captured Capture(int expectedPid, string expectedStart, string proofPath, string proofHash) {
        proofPath = SafePaths.Normalize(proofPath);
        Require(SafePaths.Within(proofPath, Owned), "Native proof path differs.");
        var capturedFiles = new Captured(Config, Fixture, App, Path.GetDirectoryName(proofPath));
        try {
        using var proof = JsonDocument.Parse(capturedFiles.Read(proofPath, proofHash.ToUpperInvariant()));
        var window = proof.RootElement.GetProperty("window"); string tree = proof.RootElement.GetProperty("tree").GetString();
        var captured = DateTimeOffset.Parse(proof.RootElement.GetProperty("capturedUtc").GetString(), System.Globalization.CultureInfo.InvariantCulture);
        capturedFiles.ProofCapturedUtc = captured;
        using var process = Process.GetProcessById(expectedPid);
        string processPath = process.MainModule.FileName;
        Require(processPath.Equals(Path.Combine(App, "EasyEject.Modern.exe"), StringComparison.OrdinalIgnoreCase), "Current process belongs to another copy.");
        Require(window.GetProperty("app").GetString().Equals("process:" + processPath, StringComparison.OrdinalIgnoreCase) && window.GetProperty("title").GetString() == "Easy Eject", "Native proof belongs to another app.");
        Require(process.Modules.Cast<ProcessModule>().Any(m => m.FileName.Equals(Path.Combine(App, "EasyEject.Runtime.dll"), StringComparison.OrdinalIgnoreCase)), "F810 backend is not loaded in current process.");
            capturedFiles.ProfileBytes = capturedFiles.Read(Path.Combine(Fixture, "prepared-profile.json"), Profile);
            capturedFiles.HistoryBytes = capturedFiles.Read(Path.Combine(Fixture, "prepared-history.json"), History);
            byte[] receipt = capturedFiles.Read(Path.Combine(Fixture, "fixture-receipt.json"), Receipt);
            using var history = JsonDocument.Parse(capturedFiles.HistoryBytes);
            var batch = history.RootElement.EnumerateArray().Single(); var originals = batch.GetProperty("Originals").EnumerateArray().ToArray();
            Require(originals.Length == 2, "Prepared selection differs.");
            var originalBytes = originals.Select(o => { string path = SafePaths.Normalize(o.GetProperty("Path").GetString()); Require(SafePaths.Within(path, Path.Combine(Fixture, "destination")), "Original escaped exact fixture."); Require(o.GetProperty("Bytes").GetInt64() == 169551, "Original recorded length differs."); return capturedFiles.Read(path, Original); }).ToArray();
            bool idle = Regex.IsMatch(tree, @"button Refresh connected storage ID: Refresh") && !tree.Contains("Finish the current review or operation first.", StringComparison.Ordinal) && !Regex.IsMatch(tree, @"window Popup|ID: PrimaryButton|ID: CloseButton|ID: Save\b");
            Require(Hash(Path.Combine(App, "EasyEject.Runtime.dll")) == Backend, "Live application backend changed.");
            capturedFiles.Value = new Admission { BackendHash = Hash(typeof(Profiles).Assembly.Location), ExeHash = Hash(processPath), ManifestHash = Hash(Path.Combine(Path.GetDirectoryName(App), "review-build.json")), ProfileHash = Hash(capturedFiles.ProfileBytes), HistoryHash = Hash(capturedFiles.HistoryBytes), ReceiptHash = Hash(receipt), FirstHash = Hash(originalBytes[0]), SecondHash = Hash(originalBytes[1]), OriginalsMatch = originalBytes.All(b => b.Length == 169551), IssueHash = Hash(Path.Combine(Config, "issue-state.json")), ToolsHash = Hash(Path.Combine(Config, "quick-share-tools.json")), ConfigCount = Directory.GetFileSystemEntries(Config, "*", SearchOption.AllDirectories).Length, ExpectedPid = expectedPid, Pid = process.Id, ExpectedStartedUtc = expectedStart, StartedUtc = process.StartTime.ToUniversalTime().ToString("o"), Window = process.MainWindowHandle.ToInt64(), ExpectedWindow = window.GetProperty("id").GetInt64(), Idle = idle, IdleAgeSeconds = (DateTimeOffset.UtcNow - captured).TotalSeconds, SourceAbsent = !Directory.Exists(Path.Combine(Fixture, "source")), TargetsAbsent = !File.Exists(Path.Combine(Config, ProfileName)) && !File.Exists(Path.Combine(Config, "completed-imports.json")) };
            return capturedFiles;
        } catch { capturedFiles.Dispose(); throw; }
    }
    static string Ledger(string root, bool creating) {
        root = SafePaths.Normalize(root);
        Require(SafePaths.Within(root, Owned) && Path.GetDirectoryName(root) == Owned && Path.GetFileName(root).StartsWith("NativeHistoryLive-", StringComparison.Ordinal), "Choose a direct new owned live ledger root.");
        Require(creating ? !Directory.Exists(root) && !File.Exists(root) : Directory.Exists(root), "Live ledger existence differs.");
        return root;
    }
    static void ExclusiveJson(string path, object value) { using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json); stream.Write(bytes); stream.Flush(true); }
    static void ValidateEdit(byte[] candidate) {
        using var captured = new Captured(Fixture);
        ValidateEdit(candidate, captured.Read(Path.Combine(Fixture, "prepared-profile.json"), Profile));
    }
    static void ValidateEdit(byte[] candidate, byte[] beforeBytes) {
        Require(Hash(beforeBytes) == Profile, "Profile edit baseline changed.");
        using var before = JsonDocument.Parse(beforeBytes); using var after = JsonDocument.Parse(candidate);
        var old = before.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
        var current = after.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
        Require(old.Count == current.Count && old.Keys.All(current.ContainsKey), "Fixture profile schema changed.");
        foreach (var pair in old) if (pair.Key != "SharingPreset") Require(JsonElement.DeepEquals(pair.Value, current[pair.Key]), "Unreviewed fixture profile change: " + pair.Key);
        string preset = after.RootElement.GetProperty("SharingPreset").GetString(); Require(preset == "1080p" || preset == "4K", "Only explicit Standard-color fixture sharing opt-in is supported.");
    }
    static object BackendCall(string name, int arguments, params object[] values) {
        return typeof(Profiles).GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single(m => m.Name == name && m.GetParameters().Length == arguments && m.GetParameters().Select((p, index) => values[index] == null || p.ParameterType.IsInstanceOfType(values[index])).All(v => v)).Invoke(null, values);
    }
    static ImportProfile Model(byte[] bytes) { using var stream = new MemoryStream(bytes); return (ImportProfile)new DataContractJsonSerializer(typeof(ImportProfile)).ReadObject(stream); }
    static byte[] Serialize(ImportProfile value) { using var stream = new MemoryStream(); new DataContractJsonSerializer(typeof(ImportProfile)).WriteObject(stream, value); return stream.ToArray(); }
    static byte[] EditorSave(string root, bool uhd) {
        // Exactly the production Saved Profiles path: ReadSaved -> Copy draft ->
        // ImportProfileEdit.SetSharing/Save -> Profiles.SaveOffline -> real writer.
        object saved = BackendCall("ReadSaved", 2, root, ProfileName);
        var profile = (ImportProfile)saved.GetType().GetProperty("Profile", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(saved);
        Action<ImportProfile, bool> save = (value, chosen) => { Require(!chosen, "Unexpected destination selection."); BackendCall("SaveOffline", 5, saved, value, root, null, null); };
        Type type = typeof(Profiles).Assembly.GetType("EasyEject.ImportProfileEdit", true);
        object editor = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { profile, new ImportDefaults(), null, save, null }, null);
        type.GetMethod("SetSharing", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, new object[] { true, uhd, false });
        type.GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, null);
        return File.ReadAllBytes(Path.Combine(root, ProfileName));
    }
    static int IntegratedTests(string root, byte[] profile, byte[] history) {
        int count = 0;
        void Refuse(Action action) { bool refused = false; try { action(); } catch (Exception) { refused = true; } Require(refused, "Expected integrated refusal."); }
        void Run(string name, Action<string, string> action) {
            string caseRoot = Path.Combine(root, name), config = Path.Combine(caseRoot, "config"), ledger = Path.Combine(caseRoot, "ledger");
            Directory.CreateDirectory(config); Directory.CreateDirectory(ledger);
            File.WriteAllText(Path.Combine(config, "issue-state.json"), "{\"preserve\":\"issue\"}"); File.WriteAllText(Path.Combine(config, "quick-share-tools.json"), "{\"preserve\":\"tools\"}");
            var baseline = OwnedRecordPublication.Baseline(config);
            action(config, ledger);
            foreach (var item in baseline) { var now = OwnedRecordPublication.Capture(Path.Combine(config, item.Key)); Require(now.Identity == item.Value.Identity && now.Sha256 == item.Value.Sha256, "Unrelated disposable config changed."); }
            count++; Console.WriteLine("PASS " + name);
        }
        void Publish(string config, string ledger) { OwnedRecordPublication.Publish(config, ledger, new[] { new OwnedRecordPublication.Record(ProfileName, profile), new OwnedRecordPublication.Record("completed-imports.json", history) }, OwnedRecordPublication.Baseline(config)); }
        void Present(string config) { Require(File.Exists(Path.Combine(config, ProfileName)) && File.Exists(Path.Combine(config, "completed-imports.json")), "Refusal moved a record."); }
        void Empty(string config, string ledger) { Require(!File.Exists(Path.Combine(config, ProfileName)) && !File.Exists(Path.Combine(config, "completed-imports.json")) && Directory.GetFiles(ledger).Length == 0, "Refusal wrote a target or ledger."); }
        foreach (bool uhd in new[] { false, true }) Run("real-editor-" + (uhd ? "4K" : "1080p") + "-checkpoint-recover", (config, ledger) => {
            Publish(config, ledger); var before = OwnedRecordPublication.Capture(Path.Combine(config, ProfileName)); byte[] edited = EditorSave(config, uhd);
            ValidateEdit(edited, profile); Require(OwnedRecordPublication.Capture(Path.Combine(config, ProfileName)).Identity != before.Identity, "Real writer did not replace profile identity.");
            Refuse(() => OwnedRecordPublication.Recover(config, ledger)); Present(config);
            string checkpoint = OwnedRecordPublication.CheckpointReviewed(config, ledger, ProfileName, Hash(edited), bytes => ValidateEdit(bytes, profile));
            Require(OwnedRecordPublication.RecoverReviewed(config, ledger, ProfileName, checkpoint, bytes => ValidateEdit(bytes, profile)) == 2, "Reviewed recovery incomplete.");
            Require(OwnedRecordPublication.RecoverReviewed(config, ledger, ProfileName, checkpoint, bytes => ValidateEdit(bytes, profile)) == 0, "Reviewed retry not idempotent.");
        });
        Run("wrong-review-hash", (config, ledger) => { Publish(config, ledger); EditorSave(config, false); Refuse(() => OwnedRecordPublication.CheckpointReviewed(config, ledger, ProfileName, "wrong", bytes => ValidateEdit(bytes, profile))); Require(!File.Exists(Path.Combine(ledger, "reviewed-profile-edit.json")), "Rejected checkpoint written."); Present(config); });
        Run("changed-history-before-checkpoint", (config, ledger) => { Publish(config, ledger); byte[] edited = EditorSave(config, false); File.WriteAllText(Path.Combine(config, "completed-imports.json"), "changed"); Refuse(() => OwnedRecordPublication.CheckpointReviewed(config, ledger, ProfileName, Hash(edited), bytes => ValidateEdit(bytes, profile))); Require(!File.Exists(Path.Combine(ledger, "reviewed-profile-edit.json")), "Rejected checkpoint written."); Present(config); });
        Run("unsafe-profile-checkpoint", (config, ledger) => { Publish(config, ledger); var model = Model(EditorSave(config, false)); model.DeleteOriginals = true; byte[] edited = Serialize(model); File.WriteAllBytes(Path.Combine(config, ProfileName), edited); Refuse(() => OwnedRecordPublication.CheckpointReviewed(config, ledger, ProfileName, Hash(edited), bytes => ValidateEdit(bytes, profile))); Require(!File.Exists(Path.Combine(ledger, "reviewed-profile-edit.json")), "Unsafe checkpoint written."); Present(config); });
        foreach (string variant in new[] { "wrong-checkpoint-hash", "changed-checkpoint", "same-bytes-profile-replacement", "changed-profile-after-checkpoint", "changed-history-after-checkpoint" }) Run(variant, (config, ledger) => {
            Publish(config, ledger); byte[] edited = EditorSave(config, false); string checkpoint = OwnedRecordPublication.CheckpointReviewed(config, ledger, ProfileName, Hash(edited), bytes => ValidateEdit(bytes, profile));
            if (variant == "wrong-checkpoint-hash") checkpoint = "wrong";
            if (variant == "changed-checkpoint") File.AppendAllText(Path.Combine(ledger, "reviewed-profile-edit.json"), " ");
            if (variant == "same-bytes-profile-replacement") { File.Move(Path.Combine(config, ProfileName), Path.Combine(ledger, "retained-edited-profile.json")); File.WriteAllBytes(Path.Combine(config, ProfileName), edited); }
            if (variant == "changed-profile-after-checkpoint") File.WriteAllText(Path.Combine(config, ProfileName), "changed profile");
            if (variant == "changed-history-after-checkpoint") File.WriteAllText(Path.Combine(config, "completed-imports.json"), "changed history");
            Refuse(() => OwnedRecordPublication.RecoverReviewed(config, ledger, ProfileName, checkpoint, bytes => ValidateEdit(bytes, profile))); Present(config);
        });
        foreach (string variant in new[] { "changed-source-before-construction", "mutated-admitted-payload", "released-source-ownership", "held-source-write-blocked" }) Run(variant, (config, ledger) => {
            string inputs = Path.Combine(Path.GetDirectoryName(config), "inputs"); Directory.CreateDirectory(inputs);
            string profilePath = Path.Combine(inputs, "profile.json"), historyPath = Path.Combine(inputs, "history.json"); File.WriteAllBytes(profilePath, profile); File.WriteAllBytes(historyPath, history);
            using var captured = new Captured(inputs);
            if (variant == "changed-source-before-construction") { File.WriteAllText(profilePath, "changed after snapshot admission"); Refuse(() => captured.Read(profilePath, Profile)); Empty(config, ledger); return; }
            captured.ProfileBytes = captured.Read(profilePath, Profile); captured.HistoryBytes = captured.Read(historyPath, History);
            if (variant == "mutated-admitted-payload") captured.ProfileBytes[0] ^= 1;
            if (variant == "released-source-ownership") { captured.Dispose(); File.WriteAllText(profilePath, "replaced after release"); }
            if (variant == "held-source-write-blocked") {
                Refuse(() => File.WriteAllText(profilePath, "changed while held")); Require(Hash(captured.ProfileBytes) == Profile, "Admitted bytes changed.");
                OwnedRecordPublication.Publish(config, ledger, captured.Records(), OwnedRecordPublication.Baseline(config)); Require(Hash(Path.Combine(config, ProfileName)) == Profile && Hash(Path.Combine(config, "completed-imports.json")) == History, "Unchecked payload published."); return;
            }
            Refuse(() => OwnedRecordPublication.Publish(config, ledger, captured.Records(), OwnedRecordPublication.Baseline(config))); Empty(config, ledger);
        });
        foreach (string field in new[] { "AutomaticImport", "AskToEject", "DeleteOriginals", "RecoverTrash", "UsesImportDefaults", "Destination", "VolumeId", "SharingColor" }) {
            var changed = Model(profile); changed.SharingPreset = "1080p";
            var member = typeof(ImportProfile).GetField(field); member.SetValue(changed, member.FieldType == typeof(bool?) ? (object)(bool?)true : "changed");
            Refuse(() => ValidateEdit(Serialize(changed), profile)); count++; Console.WriteLine("PASS unreviewed-" + field);
        }
        return count;
    }
    static int AdmissionTests(string root) {
        root = SafePaths.Normalize(root); Require(SafePaths.Within(root, Owned) && Path.GetFileName(root).StartsWith("PublicationAdmissionTest-", StringComparison.Ordinal) && !Directory.Exists(root) && !File.Exists(root), "Tests require a new disposable root.");
        Directory.CreateDirectory(root); int count = 0;
        void Refusal(string label, Action<Admission> change) { var value = new Admission(); change(value); bool refused = false; try { Validate(value, true); } catch (IOException) { refused = true; } Require(refused, "Admission failed to refuse " + label); Require(!Directory.Exists(Path.Combine(root, "live-ledger")), "Admission created a publication root."); count++; Console.WriteLine("PASS " + label); }
        Validate(new Admission(), true); count++; Console.WriteLine("PASS valid-admission-logic-only");
        Refusal("wrong-backend", v => v.BackendHash = "wrong"); Refusal("wrong-exe", v => v.ExeHash = "wrong"); Refusal("changed-manifest", v => v.ManifestHash = "wrong");
        Refusal("changed-profile", v => v.ProfileHash = "wrong"); Refusal("changed-history", v => v.HistoryHash = "wrong"); Refusal("changed-receipt", v => v.ReceiptHash = "wrong");
        Refusal("changed-first-original", v => v.FirstHash = "wrong"); Refusal("changed-second-original", v => v.SecondHash = "wrong"); Refusal("changed-original-length", v => v.OriginalsMatch = false);
        Refusal("wrong-pid", v => v.Pid++); Refusal("wrong-start", v => v.StartedUtc = "wrong"); Refusal("wrong-window", v => v.Window++);
        Refusal("missing-window", v => v.Window = 0); Refusal("stale-idle", v => v.IdleAgeSeconds = 61); Refusal("future-idle", v => v.IdleAgeSeconds = -6); Refusal("busy-idle", v => v.Idle = false);
        Refusal("wrong-scope", v => v.ExactRoots = false); Refusal("source-returned", v => v.SourceAbsent = false); Refusal("changed-tool-config", v => v.ToolsHash = "wrong");
        Refusal("new-config-file", v => v.ConfigCount = 3); Refusal("changed-issue", v => v.IssueHash = "wrong"); Refusal("occupied-target", v => v.TargetsAbsent = false);
        bool missing = false; try { Validate(null, true); } catch (IOException) { missing = true; } Require(missing, "Missing admission accepted"); count++; Console.WriteLine("PASS missing-admission");
        using var profile = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Fixture, "prepared-profile.json")));
        var edited = profile.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => JsonSerializer.Deserialize<object>(p.Value.GetRawText()));
        edited["SharingPreset"] = "1080p"; ValidateEdit(JsonSerializer.SerializeToUtf8Bytes(edited)); count++; Console.WriteLine("PASS exact-sharing-edit");
        edited["DeleteOriginals"] = true; bool unsafeEdit = false; try { ValidateEdit(JsonSerializer.SerializeToUtf8Bytes(edited)); } catch (IOException) { unsafeEdit = true; } Require(unsafeEdit, "Unreviewed removal change accepted"); count++; Console.WriteLine("PASS unreviewed-edit-refusal");
        count += IntegratedTests(root, File.ReadAllBytes(Path.Combine(Fixture, "prepared-profile.json")), File.ReadAllBytes(Path.Combine(Fixture, "prepared-history.json")));
        ExclusiveJson(Path.Combine(root, "result.json"), new { Passed = count, Failed = 0, Scope = "Synthetic admission policy plus disposable native ownership/publication/recovery and actual ImportProfileEdit/SaveOffline serialization; no live UI proof or live configuration mutation" });
        Console.WriteLine(count + " passed / 0 failed"); return 0;
    }
    [STAThread] static int Main(string[] args) {
        try {
            Require(Hash(typeof(Profiles).Assembly.Location) == Backend, "Wrong loaded backend.");
            if (args.Length == 2 && args[0] == "--core-self-test") return (int)typeof(PublicationGuardTests).GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { new[] { "--self-test", args[1] } });
            if (args.Length == 2 && args[0] == "--admission-self-test") return AdmissionTests(args[1]);
            Require(args.Length == 7 || args.Length == 8, "Usage: --publish-exact-video-history (or --recover-exact-video-history / --checkpoint-exact-profile) LEDGER IDLE_JSON IDLE_SHA PID START_UTC --broker-grant [REVIEWED_PROFILE_SHA]. No general configuration mode.");
            Require(args[6] == "--broker-grant", "Explicit scoped broker grant argument required.");
            bool publish = args[0] == "--publish-exact-video-history", recover = args[0] == "--recover-exact-video-history", checkpoint = args[0] == "--checkpoint-exact-profile", reviewed = args[0] == "--recover-reviewed-exact-video-history";
            Require(publish || recover || checkpoint || reviewed, "Unsupported mode."); Require(checkpoint || reviewed ? args.Length == 8 : args.Length == 7, "Exact mode arguments required.");
            string ledger = Ledger(args[1], publish); using var captured = Capture(Int32.Parse(args[4]), args[5], args[2], args[3]); var admission = captured.Value; Validate(admission, publish);
            if (publish) {
                var baseline = OwnedRecordPublication.Baseline(Config); Require(baseline.Count == 2 && baseline["issue-state.json"].Sha256 == Issue && baseline["quick-share-tools.json"].Sha256 == Tools, "Fresh baseline differs.");
                var records = captured.Records();
                using (SafePaths.LockDirectories(Owned)) Directory.CreateDirectory(ledger);
                ExclusiveJson(Path.Combine(ledger, "admission.json"), admission);
                OwnedRecordPublication.Publish(Config, ledger, records, baseline); Console.WriteLine("Published only the exact manual/off fixture profile and history. No import/export/eject started."); return 0;
            }
            Require(File.Exists(Path.Combine(ledger, "admission.json")), "Owned live admission ledger missing.");
            var originalAdmission = JsonSerializer.Deserialize<Admission>(File.ReadAllBytes(Path.Combine(ledger, "admission.json"))); Validate(originalAdmission, true);
            if (checkpoint) {
                string hash = OwnedRecordPublication.CheckpointReviewed(Config, ledger, ProfileName, args[7].ToUpperInvariant(), bytes => ValidateEdit(bytes, captured.ProfileBytes));
                Console.WriteLine("Explicit reviewed checkpoint SHA256: " + hash); return 0;
            }
            int moved = reviewed ? OwnedRecordPublication.RecoverReviewed(Config, ledger, ProfileName, args[7].ToUpperInvariant(), bytes => ValidateEdit(bytes, captured.ProfileBytes)) : OwnedRecordPublication.Recover(Config, ledger);
            Console.WriteLine("Recovered " + moved + " exact owned records into retained quarantine. Other configuration preserved."); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
}
