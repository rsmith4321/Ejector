using System;
using System.IO;
using System.Linq;
using System.Text;

namespace EasyEject {
    public static partial class Tests {
        static void ImportConfigurationReplacement(bool profile, string stage, bool existing) {
            using (var f = new Fixture()) {
                var state = State(); string name = profile ? Profiles.FileName(state.Volumes[0].Id) : "import-defaults.json";
                byte[] bytes = profile ? ReplaySerialize(OfflineProfile(), typeof(ImportProfile)) : ReplaySerialize(new ImportDefaults { VideosOnly = false, IncludePreviews = false }, typeof(ImportDefaults));
                if (existing) File.WriteAllBytes(Path.Combine(f.Destination, name), bytes); bool changed = false;
                Action<string> checkpoint = value => { if (value == stage) { ReplaceFixtureDirectory(f, f.Destination); File.WriteAllBytes(Path.Combine(f.Destination, name), bytes); changed = true; } };
                if (profile) Throws<SafetyException>(() => Profiles.Load(state.Volumes[0], state.Disks[0], f.Destination, checkpoint));
                else Throws<SafetyException>(() => ImportDefaults.Load(f.Destination, checkpoint));
                Check(changed && File.ReadAllBytes(Path.Combine(f.Destination, name)).SequenceEqual(bytes) && File.ReadAllText(Path.Combine(f.Destination, "foreign.keep")) == "retain replacement", "Import configuration replacement missed or altered");
                if (existing) Check(File.ReadAllBytes(Path.Combine(f.Destination + "-moved", name)).SequenceEqual(bytes), "Read altered moved original configuration");
                else Check(Directory.GetFiles(f.Destination + "-moved").Length == 0, "Missing-record read wrote old storage");
                if (profile) Check(Profiles.Load(state.Volumes[0], state.Disks[0], f.Destination).Name == OfflineProfile().Name, "Fresh profile read retained old lease");
                else Check(!ImportDefaults.Load(f.Destination).Snapshot().VideosOnly, "Fresh defaults read adopted an old/default snapshot");
            }
        }
        static void ImportConfigurationReadTests() {
            Test("import defaults directory record refuses silent factory fallback", () => {
                using (var f = new Fixture()) { string path = Path.Combine(f.Destination, "import-defaults.json"); Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "foreign.keep"), "retain defaults placeholder");
                    Throws<SafetyException>(() => ImportDefaults.Load(f.Destination)); Check(File.ReadAllText(Path.Combine(path, "foreign.keep")) == "retain defaults placeholder" && Directory.GetFiles(path).Length == 1, "Defaults read altered directory placeholder"); }
            });
            Test("connected profile directory record refuses silent absent-profile fallback", () => {
                using (var f = new Fixture()) { var state = State(); string path = Path.Combine(f.Destination, Profiles.FileName(state.Volumes[0].Id)); Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "foreign.keep"), "retain profile placeholder");
                    Throws<SafetyException>(() => Profiles.Load(state.Volumes[0], state.Disks[0], f.Destination)); Check(File.ReadAllText(Path.Combine(path, "foreign.keep")) == "retain profile placeholder" && Directory.GetFiles(path).Length == 1, "Profile read altered directory placeholder"); }
            });
            Test("defaults read refuses replacement before lookup", () => ImportConfigurationReplacement(false, "Before configuration read", false));
            Test("defaults absent return refuses replacement", () => ImportConfigurationReplacement(false, "After configuration read", false));
            Test("defaults decoded return refuses identical configuration in replaced root", () => ImportConfigurationReplacement(false, "After configuration read", true));
            Test("connected profile refuses identical replacement before lookup", () => ImportConfigurationReplacement(true, "Before configuration read", true));
            Test("connected profile decoded return refuses identical replacement", () => ImportConfigurationReplacement(true, "After configuration read", true));
            Test("ordinary absent import defaults and profiles write nothing and retain off defaults", () => {
                using (var f = new Fixture()) { var state = State(); var defaults = ImportDefaults.Load(f.Destination); var choices = defaults.Snapshot();
                    Check(Profiles.Load(state.Volumes[0], state.Disks[0], f.Destination) == null && choices.VideosOnly && choices.IncludePreviews && choices.CleanLayout && !choices.DeleteOriginals && !choices.RecoverTrash && !defaults.AutomaticEnabled && Directory.GetFiles(f.Destination).Length == 0, "Missing import configuration enabled actions or wrote storage");
                    string missing = Path.Combine(f.Root, "missing-profile-root"); Check(Profiles.Load(state.Volumes[0], state.Disks[0], missing) == null && !Directory.Exists(missing), "Missing profile storage was created"); }
            });
            Test("legacy custom profile keeps nullable choices and its 64KiB read contract", () => {
                using (var f = new Fixture()) { var state = State(); var profile = OfflineProfile(); profile.UsesImportDefaults = null; profile.VideosOnly = null; profile.IncludePreviews = null;
                    string text = Encoding.UTF8.GetString(ReplaySerialize(profile, typeof(ImportProfile))); text = text.Substring(0, text.Length - 1) + ",\"fixturePadding\":\"" + new string('x', 20000) + "\"}";
                    File.WriteAllText(Path.Combine(f.Destination, Profiles.FileName(profile.VolumeId)), text, new UTF8Encoding(false));
                    var loaded = Profiles.Load(state.Volumes[0], state.Disks[0], f.Destination); Check(loaded.UsesImportDefaults == null && loaded.VideosOnly == null && loaded.IncludePreviews == null && !loaded.Resolve(new ImportDefaults()).VideosOnly && loaded.Resolve(new ImportDefaults()).IncludePreviews && loaded.Name == profile.Name && !loaded.AutomaticEnabled(new ImportDefaults()), "Reader consolidation narrowed legacy custom choices or profile size contract"); }
            });
        }
    }
}
