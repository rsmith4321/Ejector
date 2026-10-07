using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static void CheckConnectedForgetFixtures(Fixture f, bool profile) {
            Check(File.ReadAllText(Path.Combine(f.Destination, "unrelated.keep")) == "retain unrelated settings" && Directory.GetFiles(f.Destination).Length == (profile ? 2 : 1), "Forget changed unrelated settings or leaked private work");
            Check(File.ReadAllText(Path.Combine(f.Source, "original.mp4")) == "retain generated original", "Configuration forget changed source media");
        }
        static SavedProfile ConnectedForgetProfile(Fixture f) { f.Put("original.mp4", "retain generated original"); return PublicationProfile(f); }
        static void FreshConnectedForget(Fixture f) { var state = State(); Profiles.Remove(state.Volumes[0], state.Disks[0], f.Destination); Check(Profiles.Catalog(f.Destination).Length == 0, "Fresh connected forget retained previous reader/writer resources"); CheckConnectedForgetFixtures(f, false); }
        static void ConnectedForgetTests() {
            Test("connected forget refuses late edits with restored size and timestamp", () => {
                using (var f = new Fixture()) {
                    var expected = ConnectedForgetProfile(f); var state = State(); string path = Path.Combine(f.Destination, expected.FileName); byte[] bytes = File.ReadAllBytes(path), changed = null; DateTime time = File.GetLastWriteTimeUtc(path);
                    Throws<SafetyException>(() => Profiles.Remove(state.Volumes[0], state.Disks[0], f.Destination, stage => { if (stage == "After connected forget review") { var other = expected.Profile; other.Name = other.Name.Substring(0, other.Name.Length - 1) + "e"; changed = ReplaySerialize(other, typeof(ImportProfile)); Check(changed.Length == bytes.Length, "Fixture edit changed size"); File.WriteAllBytes(path, changed); File.SetLastWriteTimeUtc(path, time); } }));
                    Check(changed != null && File.ReadAllBytes(path).SequenceEqual(changed) && File.GetLastWriteTimeUtc(path) == time, "Late choices were deleted"); CheckConnectedForgetFixtures(f, true); FreshConnectedForget(f);
                }
            });
            Test("connected forget cannot delete late changed source enrollment", () => {
                using (var f = new Fixture()) {
                    var expected = ConnectedForgetProfile(f); var state = State(); string path = Path.Combine(f.Destination, expected.FileName); byte[] changed = null;
                    Throws<SafetyException>(() => Profiles.Remove(state.Volumes[0], state.Disks[0], f.Destination, stage => { if (stage == "After connected forget review") { var other = expected.Profile; other.VolumeSerial += "-formatted"; changed = ReplaySerialize(other, typeof(ImportProfile)); File.WriteAllBytes(path, changed); } }));
                    Check(changed != null && File.ReadAllBytes(path).SequenceEqual(changed), "New card enrollment was deleted"); CheckConnectedForgetFixtures(f, true);
                    Throws<SafetyException>(() => Profiles.Remove(state.Volumes[0], state.Disks[0], f.Destination)); state.Volumes[0].Serial += "-formatted"; Profiles.Remove(state.Volumes[0], state.Disks[0], f.Destination); CheckConnectedForgetFixtures(f, false);
                }
            });
            Test("connected forget refuses byte-identical native replacement after review", () => {
                using (var f = new Fixture()) {
                    var expected = ConnectedForgetProfile(f); var state = State(); string path = Path.Combine(f.Destination, expected.FileName); byte[] bytes = File.ReadAllBytes(path); DateTime time = File.GetLastWriteTimeUtc(path);
                    using (var old = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                        Throws<SafetyException>(() => Profiles.Remove(state.Volumes[0], state.Disks[0], f.Destination, stage => { if (stage == "After connected forget review") { File.Delete(path); File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, time); } }));
                        old.Position = 0; Check(File.ReadAllBytes(path).SequenceEqual(bytes) && VerifiedImporter.Hash(old, CancellationToken.None) == expected.Hash, "Replacement or unlinked original changed");
                    }
                    CheckConnectedForgetFixtures(f, true); FreshConnectedForget(f);
                }
            });
            Test("connected forget retains a record that becomes malformed after review", () => {
                using (var f = new Fixture()) {
                    var expected = ConnectedForgetProfile(f); var state = State(); string path = Path.Combine(f.Destination, expected.FileName); const string malformed = "generated malformed record";
                    Throws<SerializationException>(() => Profiles.Remove(state.Volumes[0], state.Disks[0], f.Destination, stage => { if (stage == "After connected forget review") File.WriteAllText(path, malformed); }));
                    Check(File.ReadAllText(path) == malformed && !Profiles.Catalog(f.Destination).Single().CanEdit, "Malformed replacement was deleted or hidden"); CheckConnectedForgetFixtures(f, true);
                    Profiles.Save(OfflineProfile(), f.Destination); FreshConnectedForget(f);
                }
            });
            Test("connected forget keeps native exclusive file ownership through disposition", () => {
                using (var f = new Fixture()) {
                    var expected = ConnectedForgetProfile(f); var state = State(); string path = Path.Combine(f.Destination, expected.FileName); bool reached = false;
                    Profiles.Remove(state.Volumes[0], state.Disks[0], f.Destination, stage => { if (stage == "Before connected forget disposition") { Throws<IOException>(() => File.WriteAllText(path, "foreign write")); reached = true; } });
                    Check(reached && !File.Exists(path), "Native disposition control was not reached"); CheckConnectedForgetFixtures(f, false);
                }
            });
            Test("connected forget callback refusal retains exact record and releases writer", () => {
                using (var f = new Fixture()) {
                    var expected = ConnectedForgetProfile(f); var state = State();
                    Throws<InvalidOperationException>(() => Profiles.Remove(state.Volumes[0], state.Disks[0], f.Destination, stage => { if (stage == "Before connected forget disposition") throw new InvalidOperationException("Generated disposition refusal"); }));
                    Check(Profiles.Catalog(f.Destination).Single().Hash == expected.Hash, "Failed callback forgot record"); CheckConnectedForgetFixtures(f, true); FreshConnectedForget(f);
                }
            });
            Test("connected forget absent record is a no-op retaining unrelated bytes", () => {
                using (var f = new Fixture()) {
                    f.Put("original.mp4", "retain generated original"); File.WriteAllText(Path.Combine(f.Destination, "unrelated.keep"), "retain unrelated settings"); var state = State();
                    Profiles.Remove(state.Volumes[0], state.Disks[0], f.Destination, stage => { throw new Exception("Missing profile must not reach review/disposition"); }); CheckConnectedForgetFixtures(f, false);
                }
            });
        }
    }
}
