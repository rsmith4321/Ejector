using System;
using System.IO;
using System.Linq;

namespace EasyEject {
    public static partial class Tests {
        static SavedProfile PublicationProfile(Fixture f) {
            Profiles.Save(OfflineProfile(), f.Destination);
            File.WriteAllText(Path.Combine(f.Destination, "unrelated.keep"), "retain unrelated settings");
            return Profiles.Catalog(f.Destination).Single();
        }
        static void CheckProfilePublicationFiles(Fixture f, int records) {
            Check(File.ReadAllText(Path.Combine(f.Destination, "unrelated.keep")) == "retain unrelated settings" && Directory.GetFiles(f.Destination).Length == records + 1, "Profile refusal changed unrelated bytes or leaked private work");
        }
        static void FreshProfilePublication(Fixture f) {
            var current = Profiles.Catalog(f.Destination).Single(); var fresh = current.Profile; fresh.Name = "Fresh reviewed choices";
            Profiles.SaveOffline(current, fresh, f.Destination);
            Check(Profiles.Catalog(f.Destination).Single().Profile.Name == fresh.Name, "Fresh explicit save retained prior review/writer resources"); CheckProfilePublicationFiles(f, 1);
        }
        static void ProfilePublicationTests() {
            Test("offline profile publication refuses late edited bytes with restored size and time", () => {
                using (var f = new Fixture()) {
                    var expected = PublicationProfile(f); var draft = expected.Profile; draft.Name = "Stale editor choices"; string path = Path.Combine(f.Destination, expected.FileName); byte[] original = File.ReadAllBytes(path), changed = null; DateTime time = File.GetLastWriteTimeUtc(path);
                    Throws<SafetyException>(() => Profiles.SaveOffline(expected, draft, f.Destination, checkpoint: stage => {
                        if (stage == "After profile review") { var other = expected.Profile; other.Name = other.Name.Substring(0, other.Name.Length - 1) + "e"; changed = ReplaySerialize(other, typeof(ImportProfile)); Check(changed.Length == original.Length, "Generated profile edit changed length"); File.WriteAllBytes(path, changed); File.SetLastWriteTimeUtc(path, time); }
                    }));
                    Check(changed != null && File.ReadAllBytes(path).SequenceEqual(changed) && File.GetLastWriteTimeUtc(path) == time, "Late edited record was overwritten"); CheckProfilePublicationFiles(f, 1); FreshProfilePublication(f);
                }
            });
            Test("offline profile publication refuses deletion after reviewed record", () => {
                using (var f = new Fixture()) {
                    var expected = PublicationProfile(f); var draft = expected.Profile; draft.Name = "Stale editor choices"; string path = Path.Combine(f.Destination, expected.FileName);
                    Throws<SafetyException>(() => Profiles.SaveOffline(expected, draft, f.Destination, checkpoint: stage => { if (stage == "After profile review") File.Delete(path); }));
                    Check(!File.Exists(path) && Profiles.Catalog(f.Destination).Length == 0, "Deleted enrollment was recreated"); CheckProfilePublicationFiles(f, 0);
                    Profiles.Save(OfflineProfile(), f.Destination); FreshProfilePublication(f);
                }
            });
            Test("offline profile publication refuses identical native replacement after review", () => {
                using (var f = new Fixture()) {
                    var expected = PublicationProfile(f); var draft = expected.Profile; draft.Name = "Stale editor choices"; string path = Path.Combine(f.Destination, expected.FileName); byte[] bytes = File.ReadAllBytes(path); DateTime time = File.GetLastWriteTimeUtc(path);
                    using (var old = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                        Throws<SafetyException>(() => Profiles.SaveOffline(expected, draft, f.Destination, checkpoint: stage => { if (stage == "After profile review") { File.Delete(path); File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, time); } }));
                        Check(File.ReadAllBytes(path).SequenceEqual(bytes) && old.Length == bytes.Length, "Replacement or unlinked original changed");
                    }
                    CheckProfilePublicationFiles(f, 1); FreshProfilePublication(f);
                }
            });
            Test("offline profile publication cannot revert late changed source enrollment", () => {
                using (var f = new Fixture()) {
                    var expected = PublicationProfile(f); var draft = expected.Profile; draft.Name = "Stale editor choices"; string path = Path.Combine(f.Destination, expected.FileName); byte[] changed = null;
                    Throws<SafetyException>(() => Profiles.SaveOffline(expected, draft, f.Destination, checkpoint: stage => { if (stage == "After profile review") { var other = expected.Profile; other.VolumeSerial += "-formatted"; changed = ReplaySerialize(other, typeof(ImportProfile)); File.WriteAllBytes(path, changed); } }));
                    Check(changed != null && File.ReadAllBytes(path).SequenceEqual(changed) && Profiles.Catalog(f.Destination).Single().Profile.VolumeSerial == expected.Profile.VolumeSerial + "-formatted", "Stale offline editor restored old card identity"); CheckProfilePublicationFiles(f, 1); FreshProfilePublication(f);
                }
            });
            Test("offline profile publication exclusively holds prior record through native replacement", () => {
                using (var f = new Fixture()) {
                    var expected = PublicationProfile(f); var draft = expected.Profile; draft.Name = "Explicit reviewed choices"; string path = Path.Combine(f.Destination, expected.FileName); bool checkedHold = false;
                    Profiles.SaveOffline(expected, draft, f.Destination, checkpoint: stage => { if (stage == "Before publication") { Throws<IOException>(() => File.WriteAllText(path, "foreign raw edit")); checkedHold = true; } });
                    Check(checkedHold && Profiles.Catalog(f.Destination).Single().Profile.Name == draft.Name, "Existing native ownership/publication control failed"); CheckProfilePublicationFiles(f, 1);
                }
            });
            Test("offline profile publication callback refusal releases exact record and writer", () => {
                using (var f = new Fixture()) {
                    var expected = PublicationProfile(f); var draft = expected.Profile; draft.Name = "Uncommitted choices";
                    Throws<InvalidOperationException>(() => Profiles.SaveOffline(expected, draft, f.Destination, checkpoint: stage => { if (stage == "After profile review") throw new InvalidOperationException("Generated review refusal"); }));
                    Check(Profiles.Catalog(f.Destination).Single().Hash == expected.Hash, "Failed callback published profile"); CheckProfilePublicationFiles(f, 1); FreshProfilePublication(f);
                }
            });
            Test("offline profile publication preserves inherited legacy and independent sharing choices", () => {
                using (var f = new Fixture()) {
                    var expected = PublicationProfile(f); var draft = expected.Profile; draft.Name = "Reviewed inherited card"; draft.SharingPreset = QuickShareProfile.Uhd; draft.SharingColor = QuickShareProfile.Standard;
                    Profiles.SaveOffline(expected, draft, f.Destination); var current = Profiles.Catalog(f.Destination).Single();
                    Check(ReplaySerialize(current.Profile, typeof(ImportProfile)).SequenceEqual(ReplaySerialize(draft, typeof(ImportProfile))) && current.Profile.FollowsDefaults && !current.Profile.AutomaticEnabled(new ImportDefaults()), "Complete profile/default/share choices changed during publication"); CheckProfilePublicationFiles(f, 1);
                }
            });
        }
    }
}
