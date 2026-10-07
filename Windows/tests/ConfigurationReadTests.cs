using System;
using System.IO;
using System.Linq;

namespace EasyEject {
    public static partial class Tests {
        static void ConfigurationReadTests() {
            Test("settings directory-shaped record refuses fallback and retains its contents", () => {
                using (var f = new Fixture()) {
                    string path = Path.Combine(f.Destination, "app-settings.json"); Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "foreign.keep"), "retain settings placeholder");
                    Throws<SafetyException>(() => AppSettings.Load(f.Destination));
                    Check(Directory.GetFiles(path).Length == 1 && File.ReadAllText(Path.Combine(path, "foreign.keep")) == "retain settings placeholder", "Settings fallback altered directory contents");
                }
            });
            Test("issue directory-shaped record refuses silent cleared warning fallback", () => {
                using (var f = new Fixture()) {
                    string path = Path.Combine(f.Destination, "issue-state.json"); Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "foreign.keep"), "retain issue placeholder");
                    Throws<SafetyException>(() => AppIssue.Load(f.Destination));
                    Check(Directory.GetFiles(path).Length == 1 && File.ReadAllText(Path.Combine(path, "foreign.keep")) == "retain issue placeholder", "Issue fallback altered directory contents");
                }
            });
            Test("settings read refuses replacement before lookup instead of adopting another root", () => {
                using (var f = new Fixture()) {
                    byte[] replacement = ReplaySerialize(new AppSettings { ShowNotifications = false, CleanCardsOnEject = true }, typeof(AppSettings)); bool changed = false;
                    Throws<SafetyException>(() => AppSettings.Load(f.Destination, stage => {
                        if (stage == "Before configuration read") { ReplaceFixtureDirectory(f, f.Destination); File.WriteAllBytes(Path.Combine(f.Destination, "app-settings.json"), replacement); changed = true; }
                    }));
                    Check(changed && File.ReadAllBytes(Path.Combine(f.Destination, "app-settings.json")).SequenceEqual(replacement) && File.ReadAllText(Path.Combine(f.Destination, "foreign.keep")) == "retain replacement", "Replaced settings root was missed or modified");
                    var current = AppSettings.Load(f.Destination); Check(!current.NotificationsEnabled && current.CleanupCardsEnabled, "Fresh read did not release old root or load explicitly current stored settings");
                }
            });
            Test("settings absent-file default refuses replacement at read return", () => {
                using (var f = new Fixture()) {
                    bool changed = false; byte[] replacement = ReplaySerialize(new AppSettings { DebugLogging = true }, typeof(AppSettings));
                    Throws<SafetyException>(() => AppSettings.Load(f.Destination, stage => {
                        if (stage == "After configuration read") { ReplaceFixtureDirectory(f, f.Destination); File.WriteAllBytes(Path.Combine(f.Destination, "app-settings.json"), replacement); changed = true; }
                    }));
                    Check(changed && Directory.GetFiles(f.Destination + "-moved").Length == 0 && File.ReadAllBytes(Path.Combine(f.Destination, "app-settings.json")).SequenceEqual(replacement), "Absent default returned from or modified a replacement root");
                    Check(AppSettings.Load(f.Destination).DebugEnabled, "Fresh read retained a refused lease/default");
                }
            });
            Test("issue read refuses byte-identical root replacement after decoding", () => {
                using (var f = new Fixture()) {
                    var issue = new AppIssue(); issue.Record("Generated warning still needs attention"); AppIssue.Save(issue, f.Destination); string path = Path.Combine(f.Destination, "issue-state.json"); byte[] original = File.ReadAllBytes(path); bool changed = false;
                    Throws<SafetyException>(() => AppIssue.Load(f.Destination, stage => {
                        if (stage == "After configuration read") { ReplaceFixtureDirectory(f, f.Destination); File.WriteAllBytes(path, original); changed = true; }
                    }));
                    Check(changed && File.ReadAllBytes(path).SequenceEqual(original) && File.ReadAllBytes(Path.Combine(f.Destination + "-moved", "issue-state.json")).SequenceEqual(original), "Issue read missed replacement or changed retained warning");
                    Check(AppIssue.Load(f.Destination).NeedsAttention, "Fresh warning read retained an old lease or lost attention");
                }
            });
            Test("configuration missing records retain safe defaults without creating files", () => {
                using (var f = new Fixture()) {
                    var settings = AppSettings.Load(f.Destination); var issue = AppIssue.Load(f.Destination);
                    Check(settings.NotificationsEnabled && settings.WarnOtherEnabled && !settings.DebugEnabled && !settings.ShortcutEnabled && !settings.CleanupCardsEnabled && !issue.HasIssue && Directory.GetFiles(f.Destination).Length == 0, "Ordinary absence wrote storage or enabled an off-default action");
                }
            });
            Test("configuration failed read callback releases leases for fresh explicit save", () => {
                using (var f = new Fixture()) {
                    AppSettings.Save(new AppSettings { ShowNotifications = false }, f.Destination); string path = Path.Combine(f.Destination, "app-settings.json"); byte[] original = File.ReadAllBytes(path);
                    Throws<InvalidOperationException>(() => AppSettings.Load(f.Destination, stage => { if (stage == "After configuration read") throw new InvalidOperationException("Generated read refusal"); }));
                    Check(File.ReadAllBytes(path).SequenceEqual(original), "Failed read changed preferences"); AppSettings.Save(new AppSettings { DebugLogging = true }, f.Destination);
                    Check(AppSettings.Load(f.Destination).DebugEnabled && Directory.GetFiles(f.Destination).Length == 1, "Failed read leaked handles/writer or refused fresh explicit save");
                }
            });
        }
    }
}
