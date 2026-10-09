using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using EasyEject;

internal static class PublicationGuardTests {
    static string root;
    static int passed;
    const string BackendHash = "F810DFB734CF47AE09FA8A9558DA5F819056FE059692CE31857ABE2AF1F35FAD";
    sealed class Case {
        internal string Config, Ledger;
        internal System.Collections.Generic.Dictionary<string, OwnedRecordPublication.Pin> Before;
        internal OwnedRecordPublication.Record[] Records = { new OwnedRecordPublication.Record("owned-profile.json", "{\"fixture\":\"manual-off\"}"), new OwnedRecordPublication.Record("completed-imports.json", "[{\"fixture\":\"source-absent\"}]") };
        internal Case(string name) {
            string folder = Path.Combine(root, name); Directory.CreateDirectory(folder);
            Config = Path.Combine(folder, "config"); Ledger = Path.Combine(folder, "ledger"); Directory.CreateDirectory(Config); Directory.CreateDirectory(Ledger);
            File.WriteAllText(Path.Combine(Config, "issue-state.json"), "{\"preserve\":\"issue\"}"); File.WriteAllText(Path.Combine(Config, "quick-share-tools.json"), "{\"preserve\":\"tools\"}");
            Before = OwnedRecordPublication.Baseline(Config);
        }
        internal void Publish(Action<int> point = null) { OwnedRecordPublication.Publish(Config, Ledger, Records, Before, point); }
        internal int Recover() { return OwnedRecordPublication.Recover(Config, Ledger); }
        internal string Profile { get { return Path.Combine(Config, Records[0].Name); } }
        internal string History { get { return Path.Combine(Config, Records[1].Name); } }
        internal void Preserved() {
            foreach (var pin in Before) { var now = OwnedRecordPublication.Capture(Path.Combine(Config, pin.Key)); Require(now.Identity == pin.Value.Identity && now.Sha256 == pin.Value.Sha256 && now.Bytes == pin.Value.Bytes, "Unrelated config changed"); }
        }
    }
    static void Require(bool value, string message) { if (!value) throw new IOException(message); }
    static void Refuse(Action action) { bool refused = false; try { action(); } catch (Exception) { refused = true; } Require(refused, "Expected refusal"); }
    static void Run(string name, Action<Case> test) { var item = new Case(name); test(item); item.Preserved(); passed++; Console.WriteLine("PASS " + name); }
    [STAThread] static int Main(string[] args) {
        try {
            if (args.Length != 2 || args[0] != "--self-test") throw new ArgumentException("Only --self-test NEW_OWNED_TEST_ROOT is supported. No live publication mode.");
            string backend = typeof(Profiles).Assembly.Location;
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(backend))) == BackendHash, "Wrong backend");
            root = SafePaths.Normalize(args[1]);
            string allowed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectBuildTools");
            Require(SafePaths.Within(root, allowed) && Path.GetFileName(root).StartsWith("PublicationGuardTest-", StringComparison.Ordinal) && !Directory.Exists(root) && !File.Exists(root), "Tests require a new owned root");
            using (SafePaths.LockDirectories(Path.GetDirectoryName(root))) Directory.CreateDirectory(root);
            Run("publish-recover-idempotent", c => { c.Publish(); Require(File.Exists(c.Profile) && File.Exists(c.History), "Publication missing"); Require(c.Recover() == 2 && !File.Exists(c.Profile) && !File.Exists(c.History), "Recovery incomplete"); Require(c.Recover() == 0, "Recovery not idempotent"); });
            Run("occupied-first", c => { File.WriteAllText(c.Profile, "unrelated first"); var pin = OwnedRecordPublication.Capture(c.Profile); Refuse(() => c.Publish()); Require(OwnedRecordPublication.Capture(c.Profile).Sha256 == pin.Sha256 && !File.Exists(c.History) && Directory.GetFiles(c.Ledger).Length == 0, "Occupied first changed"); });
            Run("occupied-second", c => { File.WriteAllText(c.History, "unrelated second"); var pin = OwnedRecordPublication.Capture(c.History); Refuse(() => c.Publish()); Require(OwnedRecordPublication.Capture(c.History).Sha256 == pin.Sha256 && !File.Exists(c.Profile), "Occupied second changed"); });
            Run("late-second-collision", c => { Refuse(() => c.Publish(index => { if (index == 0) File.WriteAllText(c.History, "competing history"); })); Require(File.Exists(c.Profile), "First publication lost"); Require(c.Recover() == 2 && !File.Exists(c.Profile) && File.ReadAllText(c.History) == "competing history" && Directory.GetFiles(c.Config, "*.tmp").Length == 0, "Partial recovery touched competitor"); });
            Run("changed-owned-record", c => { c.Publish(); File.WriteAllText(c.Profile, "changed own bytes"); Refuse(() => c.Recover()); Require(File.ReadAllText(c.Profile) == "changed own bytes" && File.Exists(c.History), "Changed record moved"); });
            Run("same-bytes-replacement", c => { c.Publish(); var old = OwnedRecordPublication.Capture(c.Profile); byte[] bytes = File.ReadAllBytes(c.Profile); File.Move(c.Profile, Path.Combine(c.Ledger, "replacement-before.json")); File.WriteAllBytes(c.Profile, bytes); Require(OwnedRecordPublication.Capture(c.Profile).Identity != old.Identity, "Replacement fixture identity unchanged"); Refuse(() => c.Recover()); Require(File.Exists(c.Profile) && File.Exists(c.History), "Replacement recovered incorrectly"); });
            Run("second-record-changed-all-preflight", c => { c.Publish(); File.WriteAllText(c.History, "changed later record"); Refuse(() => c.Recover()); Require(File.Exists(c.Profile) && File.ReadAllText(c.History) == "changed later record", "First moved before later validation"); });
            Run("occupied-recovery-target", c => { c.Publish(); File.WriteAllText(Path.Combine(c.Ledger, "recovered-1.json"), "quarantine competitor"); Refuse(() => c.Recover()); Require(File.Exists(c.Profile) && File.Exists(c.History) && File.ReadAllText(Path.Combine(c.Ledger, "recovered-1.json")) == "quarantine competitor", "Recovery overwrote competitor"); });
            Run("crash-before-final-rename", c => { c.Publish(); var entry = JsonSerializer.Deserialize<OwnedRecordPublication.Entry>(File.ReadAllBytes(Path.Combine(c.Ledger, "ready-1.json"))); File.Move(c.History, entry.Temporary); Require(c.Recover() == 2 && !File.Exists(entry.Temporary), "Ready ledger cannot recover unpublished name"); });
            Run("crash-after-first-recovery", c => { c.Publish(); File.Move(c.Profile, Path.Combine(c.Ledger, "recovered-0.json")); Require(c.Recover() == 1 && c.Recover() == 0, "Partial recovery retry failed"); });
            Run("root-scope-mismatch", c => { c.Publish(); string other = Path.Combine(root, "other-config"); Directory.CreateDirectory(other); Refuse(() => OwnedRecordPublication.Recover(other, c.Ledger)); Require(File.Exists(c.Profile) && File.Exists(c.History), "Wrong root affected original"); });
            Run("escaped-record-name", c => { c.Records = new[] { new OwnedRecordPublication.Record("..\\escape.json", "{}") }; Refuse(() => c.Publish()); Require(!File.Exists(Path.Combine(Path.GetDirectoryName(c.Config), "escape.json")), "Escaped record created"); });
            Run("duplicate-record-names", c => { c.Records = new[] { new OwnedRecordPublication.Record("same.json", "{}"), new OwnedRecordPublication.Record("same.json", "[]") }; Refuse(() => c.Publish()); Require(!File.Exists(Path.Combine(c.Config, "same.json")), "Duplicate record created"); });
            Run("new-unrelated-config-refusal", c => { File.WriteAllText(Path.Combine(c.Config, "new-user-record.json"), "retain new config"); Refuse(() => c.Publish()); Require(File.ReadAllText(Path.Combine(c.Config, "new-user-record.json")) == "retain new config" && !File.Exists(c.Profile), "Unrelated record changed"); });
            Run("modified-baseline-refusal", c => { string path = Path.Combine(c.Config, "issue-state.json"); File.WriteAllText(path, "new app issue"); Refuse(() => c.Publish()); Require(File.ReadAllText(path) == "new app issue" && !File.Exists(c.Profile), "New issue overwritten"); c.Before.Remove("issue-state.json"); });
            Run("read-only-owned-refusal", c => { c.Publish(); File.SetAttributes(c.History, File.GetAttributes(c.History) | FileAttributes.ReadOnly); Refuse(() => c.Recover()); Require(File.Exists(c.Profile) && File.Exists(c.History) && (File.GetAttributes(c.History) & FileAttributes.ReadOnly) != 0, "Read-only protection altered"); });
            Run("incomplete-write-refusal", c => { c.Publish(); string proof = Path.Combine(c.Ledger, "ready-1.json"); File.Move(proof, Path.Combine(c.Ledger, "retained-ready-1.json")); Refuse(() => c.Recover()); Require(File.Exists(c.Profile) && File.Exists(c.History), "Incomplete proof silently recovered"); });
            File.WriteAllText(Path.Combine(root, "test-result.json"), JsonSerializer.Serialize(new { Passed = passed, Failed = 0, Backend = backend, BackendHash, LivePublicationMode = false, Scope = "Only new disposable configuration/ledger roots; all generated records retained in place or quarantine; no live app/config/device/tool action" }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(passed + " passed / 0 failed"); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
}
