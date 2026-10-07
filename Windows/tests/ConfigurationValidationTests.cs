using System;
using System.IO;
using System.Linq;

namespace EasyEject {
    public static partial class Tests {
        static void AliasedConfiguration(string name, byte[] bytes, Action<string> read) {
            using (var f = new Fixture()) {
                string path = Path.Combine(f.Destination, name), alias = Path.Combine(f.Source, "configuration-alias.keep");
                File.WriteAllBytes(path, bytes); Check(CreateHardLink(alias, path, IntPtr.Zero), "Could not create owned configuration alias");
                Throws<SafetyException>(() => read(f.Destination));
                Check(File.ReadAllBytes(path).SequenceEqual(bytes) && File.ReadAllBytes(alias).SequenceEqual(bytes) && Directory.GetFiles(f.Destination).Length == 1 && Directory.GetFiles(f.Source).Length == 1, "Aliased configuration read changed data or wrote defaults");
                File.Delete(alias); read(f.Destination); Check(File.ReadAllBytes(path).SequenceEqual(bytes), "Fresh single-link read retained old lease or changed record");
            }
        }
        static void InvalidConnectedProfileFolders(string[] values, bool destination) {
            using (var f = new Fixture()) {
                var state = State(); string path = Path.Combine(f.Destination, Profiles.FileName(state.Volumes[0].Id));
                foreach (string folder in values) {
                    var profile = OfflineProfile(); if (destination) profile.Destination = folder; else profile.RelativeSource = folder;
                    byte[] bytes = ReplaySerialize(profile, typeof(ImportProfile)); File.WriteAllBytes(path, bytes);
                    Check(!Profiles.Catalog(f.Destination).Single().CanEdit, "Expected the existing offline folder contract to reject this record");
                    Throws<SafetyException>(() => Profiles.Load(state.Volumes[0], state.Disks[0], f.Destination));
                    Check(File.ReadAllBytes(path).SequenceEqual(bytes) && Directory.GetFiles(f.Destination).Length == 1, "Connected invalid-folder read altered enrollment");
                }
                Profiles.Save(OfflineProfile(), f.Destination); Check(Profiles.Load(state.Volumes[0], state.Disks[0], f.Destination).RelativeSource == "DCIM", "Fresh valid folder selection retained an old read lease");
            }
        }
        static void ConfigurationValidationTests() {
            Test("shared settings refuse hard-linked action preferences and retain both names", () => AliasedConfiguration("app-settings.json", ReplaySerialize(new AppSettings { CleanCardsOnEject = true }, typeof(AppSettings)), root => AppSettings.Load(root)));
            Test("shared issue state refuses hard-linked dismissed records and retains notice", () => {
                var issue = new AppIssue(); issue.Record("Generated issue requiring review"); issue.Dismiss();
                AliasedConfiguration("issue-state.json", ReplaySerialize(issue, typeof(AppIssue)), root => AppIssue.Load(root));
            });
            Test("shared import defaults refuse hard-linked automatic and removal choices", () => AliasedConfiguration("import-defaults.json", ReplaySerialize(new ImportDefaults { AutomaticImport = true, DeleteOriginals = true, RecoverTrash = true }, typeof(ImportDefaults)), root => ImportDefaults.Load(root)));
            Test("connected profile refuses hard-linked enrollment like the offline catalog", () => {
                var state = State(); var profile = OfflineProfile(); profile.AutomaticImport = true;
                AliasedConfiguration(Profiles.FileName(profile.VolumeId), ReplaySerialize(profile, typeof(ImportProfile)), root => Profiles.Load(state.Volumes[0], state.Disks[0], root));
            });
            Test("all shared readers refuse a native alias added while decoded record stays held", () => {
                var state = State(); var issue = new AppIssue(); issue.Record("Generated held record notice");
                var names = new[] { "app-settings.json", "issue-state.json", "import-defaults.json", Profiles.FileName(state.Volumes[0].Id) };
                var bytes = new[] { ReplaySerialize(new AppSettings(), typeof(AppSettings)), ReplaySerialize(issue, typeof(AppIssue)), ReplaySerialize(new ImportDefaults(), typeof(ImportDefaults)), ReplaySerialize(OfflineProfile(), typeof(ImportProfile)) };
                for (int i = 0; i < names.Length; i++) using (var f = new Fixture()) {
                    string path = Path.Combine(f.Destination, names[i]), alias = Path.Combine(f.Source, "late-configuration-alias.keep"); File.WriteAllBytes(path, bytes[i]); bool reached = false;
                    Action<string> checkpoint = stage => { if (stage == "After record decode") { Check(CreateHardLink(alias, path, IntPtr.Zero), "Could not alias owned held record"); reached = true; } };
                    int kind = i; Action<Action<string>> read = hook => { if (kind == 0) AppSettings.Load(f.Destination, hook); else if (kind == 1) AppIssue.Load(f.Destination, hook); else if (kind == 2) ImportDefaults.Load(f.Destination, hook); else Profiles.Load(state.Volumes[0], state.Disks[0], f.Destination, hook); };
                    Throws<SafetyException>(() => read(checkpoint)); Check(reached && File.ReadAllBytes(path).SequenceEqual(bytes[i]) && File.ReadAllBytes(alias).SequenceEqual(bytes[i]), "Late alias not reached or record changed");
                    File.Delete(alias); read(null); Check(Directory.GetFiles(f.Destination).Length == 1, "Fresh decoded read retained resources or wrote defaults");
                }
            });
            Test("connected profile rejects rooted and forward-slash media folders", () => InvalidConnectedProfileFolders(new[] { @"C:\generated-media", @"\DCIM", "C:DCIM", "DCIM/generated" }, false));
            Test("connected profile rejects traversal and ambiguous media components", () => InvalidConnectedProfileFolders(new[] { @"DCIM\..\PRIVATE", @".\DCIM", @"DCIM.\clip", @"DCIM \clip" }, false));
            Test("connected profile rejects protected Trash and system media scopes", () => InvalidConnectedProfileFolders(new[] { ".Trashes", @"DCIM\$recycle.bin", @"System Volume Information\media" }, false));
            Test("connected profile rejects unsupported destination records before returning choices", () => InvalidConnectedProfileFolders(new[] { @"\\fixture-host\photos", @"Z:\photos:stream", @"Z:\photos\..\outside" }, true));
            Test("connected root nested and nullable legacy folder profiles agree with offline catalog", () => {
                using (var f = new Fixture()) {
                    var state = State(); foreach (string folder in new[] { "", "DCIM", @"DCIM\Luna α" }) {
                        var profile = OfflineProfile(); profile.RelativeSource = folder; profile.Name = null; profile.UsesImportDefaults = profile.VideosOnly = profile.IncludePreviews = null; profile.SharingPreset = QuickShareProfile.Uhd; profile.SharingColor = QuickShareProfile.Standard;
                        Profiles.Save(profile, f.Destination); var current = Profiles.Load(state.Volumes[0], state.Disks[0], f.Destination); var saved = Profiles.Catalog(f.Destination).Single();
                        Check(saved.CanEdit && ReplaySerialize(current, typeof(ImportProfile)).SequenceEqual(ReplaySerialize(saved.Profile, typeof(ImportProfile))) && current.UsesImportDefaults == null && !current.Resolve(new ImportDefaults()).VideosOnly && current.Resolve(new ImportDefaults()).IncludePreviews && !current.AutomaticEnabled(new ImportDefaults()), "Consistent read changed complete nullable/custom/share profile or required mounted folders");
                    }
                }
            });
            Test("single-link read-only settings issue defaults and profile remain readable without changes", () => {
                using (var f = new Fixture()) {
                    var state = State(); var issue = new AppIssue(); issue.Record("Retain generated notice");
                    var names = new[] { "app-settings.json", "issue-state.json", "import-defaults.json", Profiles.FileName(state.Volumes[0].Id) };
                    var bytes = new[] { ReplaySerialize(new AppSettings { ShowNotifications = false }, typeof(AppSettings)), ReplaySerialize(issue, typeof(AppIssue)), ReplaySerialize(new ImportDefaults { IncludePreviews = false }, typeof(ImportDefaults)), ReplaySerialize(OfflineProfile(), typeof(ImportProfile)) };
                    for (int i = 0; i < names.Length; i++) { string path = Path.Combine(f.Destination, names[i]); File.WriteAllBytes(path, bytes[i]); File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly); }
                    try {
                        Check(!AppSettings.Load(f.Destination).NotificationsEnabled && AppIssue.Load(f.Destination).NeedsAttention && !ImportDefaults.Load(f.Destination).Snapshot().IncludePreviews && Profiles.Load(state.Volumes[0], state.Disks[0], f.Destination).Name == OfflineProfile().Name, "Read-only records were hidden or changed effective choices");
                        for (int i = 0; i < names.Length; i++) { string path = Path.Combine(f.Destination, names[i]); Check(File.ReadAllBytes(path).SequenceEqual(bytes[i]) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0, "Reader changed record/protection"); }
                    } finally { foreach (string name in names) { string path = Path.Combine(f.Destination, name); File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly); } }
                    Check(Directory.GetFiles(f.Destination).Length == 4, "Read-only reader wrote unexpected state");
                }
            });
        }
    }
}
