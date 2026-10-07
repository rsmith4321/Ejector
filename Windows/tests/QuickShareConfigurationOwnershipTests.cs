using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static void ShareSetupRootReplacement(string stage, bool existing) {
            using (var f = new Fixture()) {
                byte[] bytes = ReplaySerialize(new QuickShareToolSettings(), typeof(QuickShareToolSettings)); string path = Path.Combine(f.Destination, QuickShareConfiguration.FileName); if (existing) File.WriteAllBytes(path, bytes); bool changed = false;
                Throws<SafetyException>(() => QuickShareConfiguration.Load(f.Destination, value => {
                    if (value == stage) { ReplaceFixtureDirectory(f, f.Destination); File.WriteAllBytes(path, bytes); changed = true; }
                }));
                Check(changed && File.ReadAllBytes(path).SequenceEqual(bytes) && File.ReadAllText(Path.Combine(f.Destination, "foreign.keep")) == "retain replacement", "Setup root replacement missed/changed");
                if (existing) Check(File.ReadAllBytes(Path.Combine(f.Destination + "-moved", QuickShareConfiguration.FileName)).SequenceEqual(bytes), "Setup read changed moved original");
                else Check(Directory.GetFiles(f.Destination + "-moved").Length == 0, "Absent setup wrote old root");
                Check(QuickShareConfiguration.Load(f.Destination).Exists, "Fresh setup read kept an old lease");
            }
        }
        static void QuickShareConfigurationOwnershipTests() {
            Test("share setup refuses root replacement before lookup", () => ShareSetupRootReplacement("Before setup read", false));
            Test("share setup absent return refuses replacement", () => ShareSetupRootReplacement("After setup read", false));
            Test("share setup decoded return refuses identical root replacement", () => ShareSetupRootReplacement("After setup read", true));
            Test("share setup save refuses changed bytes with restored length and timestamp", () => {
                using (var f = new Fixture()) {
                    var saved = SaveShareSetup(f); string path = Path.Combine(f.Destination, QuickShareConfiguration.FileName), helper = FixtureShareHelper(f); byte[] original = File.ReadAllBytes(path), changed = null; DateTime timestamp = File.GetLastWriteTimeUtc(path);
                    Throws<SafetyException>(() => QuickShareConfiguration.Save(saved, helper, helper, null, f.Destination, CancellationToken.None, stage => {
                        if (stage == "Before setup publication") { var value = saved.Value; value.Ffmpeg.Sha256 = (value.Ffmpeg.Sha256[0] == 'a' ? "b" : "a") + value.Ffmpeg.Sha256.Substring(1); changed = ReplaySerialize(value, typeof(QuickShareToolSettings)); Check(changed.Length == original.Length, "Fixture edit changed length"); File.WriteAllBytes(path, changed); File.SetLastWriteTimeUtc(path, timestamp); }
                    }));
                    Check(changed != null && File.ReadAllBytes(path).SequenceEqual(changed) && File.GetLastWriteTimeUtc(path) == timestamp && Directory.GetFiles(f.Destination).Length == 1, "Setup changed bytes were overwritten or private work leaked");
                    var fresh = QuickShareConfiguration.Save(QuickShareConfiguration.Load(f.Destination), helper, helper, null, f.Destination, CancellationToken.None); Check(fresh.Value.Ffmpeg.Sha256 == saved.Value.Ffmpeg.Sha256, "Fresh explicit tool review did not release/rebind setup");
                }
            });
            Test("share setup save refuses a file appearing after an absent review", () => {
                using (var f = new Fixture()) {
                    var expected = QuickShareConfiguration.Load(f.Destination); string helper = FixtureShareHelper(f), path = Path.Combine(f.Destination, QuickShareConfiguration.FileName); byte[] appeared = ReplaySerialize(new QuickShareToolSettings(), typeof(QuickShareToolSettings));
                    Throws<SafetyException>(() => QuickShareConfiguration.Save(expected, helper, helper, null, f.Destination, CancellationToken.None, stage => { if (stage == "After setup review") File.WriteAllBytes(path, appeared); }));
                    Check(File.ReadAllBytes(path).SequenceEqual(appeared) && Directory.GetFiles(f.Destination).Length == 1, "Appearing setup was overwritten or work leaked");
                    Check(SaveShareSetup(f, helper).Value.Ffmpeg != null, "Fresh save after appearing-file refusal retained writer/tool leases");
                }
            });
            Test("share setup save refuses deletion after a present review", () => {
                using (var f = new Fixture()) {
                    var expected = SaveShareSetup(f); string helper = FixtureShareHelper(f), path = Path.Combine(f.Destination, QuickShareConfiguration.FileName);
                    Throws<SafetyException>(() => QuickShareConfiguration.Save(expected, helper, helper, null, f.Destination, CancellationToken.None, stage => { if (stage == "After setup review") File.Delete(path); }));
                    Check(!File.Exists(path) && Directory.GetFiles(f.Destination).Length == 0 && File.Exists(helper), "Deleted setup was recreated or selected tool changed");
                    Check(SaveShareSetup(f, helper).Exists, "Fresh save after deletion refusal retained writer/tool leases");
                }
            });
            Test("share setup save refuses byte-identical native replacement after review", () => {
                using (var f = new Fixture()) {
                    var expected = SaveShareSetup(f); string helper = FixtureShareHelper(f), path = Path.Combine(f.Destination, QuickShareConfiguration.FileName); byte[] original = File.ReadAllBytes(path);
                    using (var old = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                        Throws<SafetyException>(() => QuickShareConfiguration.Save(expected, helper, helper, null, f.Destination, CancellationToken.None, stage => { if (stage == "Before setup publication") { File.Delete(path); File.WriteAllBytes(path, original); } }));
                        Check(File.ReadAllBytes(path).SequenceEqual(original) && old.Length == original.Length, "Native replacement was overwritten or unlinked original changed");
                    }
                    Check(SaveShareSetup(f, helper).Exists && Directory.GetFiles(f.Destination).Length == 1, "Fresh review retained resources/leaked private work");
                }
            });
            Test("share setup existing directory refusal retains placeholder data", () => {
                using (var f = new Fixture()) { string path = Path.Combine(f.Destination, QuickShareConfiguration.FileName); Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "foreign.keep"), "retain setup placeholder");
                    Throws<SafetyException>(() => QuickShareConfiguration.Load(f.Destination)); Check(File.ReadAllText(Path.Combine(path, "foreign.keep")) == "retain setup placeholder", "Setup read altered placeholder"); }
            });
            Test("share setup failed read callback releases directory lease", () => {
                using (var f = new Fixture()) { File.WriteAllBytes(Path.Combine(f.Destination, QuickShareConfiguration.FileName), ReplaySerialize(new QuickShareToolSettings(), typeof(QuickShareToolSettings)));
                    Throws<InvalidOperationException>(() => QuickShareConfiguration.Load(f.Destination, stage => { if (stage == "After setup read") throw new InvalidOperationException("Generated setup read refusal"); }));
                    Check(SaveShareSetup(f).Exists, "Refused callback retained setup read/write resources"); }
            });
        }
    }
}
