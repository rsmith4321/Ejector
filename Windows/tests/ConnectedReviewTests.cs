using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        static ConnectedProfileReview ConnectedReview(Fixture f) { var s = State(); return Profiles.ReviewConnected(s.Volumes[0], s.Disks[0], f.Destination); }
        static ConnectedProfileReview ExistingConnectedReview(Fixture f) { PublicationProfile(f); return ConnectedReview(f); }
        static void CheckConnectedReviewFiles(Fixture f, byte[] expected, string name) { Check(File.ReadAllBytes(Path.Combine(f.Destination, name)).SequenceEqual(expected), "Reviewed record or replacement changed"); Check(!File.Exists(Path.Combine(f.Destination, ".profile-write.lock")) && Directory.GetFiles(f.Destination).All(p => !p.EndsWith(".tmp", StringComparison.Ordinal)), "Reviewed action leaked private work"); }
        static void FreshConnectedSave(Fixture f) { var review = ConnectedReview(f); var draft = OfflineProfile(); draft.Name = "Fresh connected review"; Profiles.SaveConnected(review, draft, f.Destination); Check(ConnectedReview(f).Value.Name == draft.Name, "Fresh explicit save retained stale leases/review"); }
        static void ChangedConnectedReview(bool forget, bool late) {
            using (var f = new Fixture()) {
                var review = ExistingConnectedReview(f); string path = Path.Combine(f.Destination, review.FileName); byte[] original = File.ReadAllBytes(path); DateTime time = File.GetLastWriteTimeUtc(path); var other = review.Value; other.Name = other.Name.Substring(0, other.Name.Length - 1) + "e"; byte[] changed = ReplaySerialize(other, typeof(ImportProfile)); Check(changed.Length == original.Length, "Fixture edit changed size");
                Action mutate = () => { File.WriteAllBytes(path, changed); File.SetLastWriteTimeUtc(path, time); };
                if (!late) mutate(); var draft = review.Value; draft.Name = "Old dialog choices";
                if (forget) Throws<SafetyException>(() => Profiles.ForgetConnected(review, f.Destination, stage => { if (late && stage == "Before connected disposition") mutate(); }));
                else Throws<SafetyException>(() => Profiles.SaveConnected(review, draft, f.Destination, stage => { if (late && stage == "Before connected publication") mutate(); }));
                CheckConnectedReviewFiles(f, changed, review.FileName); Check(File.GetLastWriteTimeUtc(path) == time && File.ReadAllText(Path.Combine(f.Destination, "unrelated.keep")) == "retain unrelated settings", "Refusal changed metadata/unrelated bytes"); FreshConnectedSave(f);
            }
        }
        static void ConnectedReviewTests() {
            Test("connected dialog save refuses changed reviewed bytes with restored size and time", () => ChangedConnectedReview(false, false));
            Test("connected dialog forget refuses changed reviewed choices", () => ChangedConnectedReview(true, false));
            Test("connected save final compare refuses a late raw edit", () => ChangedConnectedReview(false, true));
            Test("connected forget final compare refuses a late raw edit", () => ChangedConnectedReview(true, true));
            Test("connected dialog save cannot recreate a deleted reviewed enrollment", () => {
                using (var f = new Fixture()) { var review = ExistingConnectedReview(f); File.Delete(Path.Combine(f.Destination, review.FileName)); Throws<SafetyException>(() => Profiles.SaveConnected(review, review.Value, f.Destination)); Check(!File.Exists(Path.Combine(f.Destination, review.FileName)), "Deleted profile recreated"); FreshConnectedSave(f); }
            });
            Test("connected new setup cannot overwrite a profile appearing after absence review", () => {
                using (var f = new Fixture()) { var review = ConnectedReview(f); var other = OfflineProfile(); other.Name = "Appearing current choices"; byte[] bytes = ReplaySerialize(other, typeof(ImportProfile)); File.WriteAllBytes(Path.Combine(f.Destination, review.FileName), bytes); Throws<SafetyException>(() => Profiles.SaveConnected(review, OfflineProfile(), f.Destination)); CheckConnectedReviewFiles(f, bytes, review.FileName); FreshConnectedSave(f); }
            });
            Test("connected save and forget refuse byte-identical native replacements", () => {
                foreach (bool forget in new[] { false, true }) using (var f = new Fixture()) {
                    var review = ExistingConnectedReview(f); string path = Path.Combine(f.Destination, review.FileName); byte[] bytes = File.ReadAllBytes(path); DateTime time = File.GetLastWriteTimeUtc(path);
                    using (var old = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { File.Delete(path); File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, time); if (forget) Throws<SafetyException>(() => Profiles.ForgetConnected(review, f.Destination)); else Throws<SafetyException>(() => Profiles.SaveConnected(review, review.Value, f.Destination)); Check(old.Length == bytes.Length, "Unlinked original changed"); CheckConnectedReviewFiles(f, bytes, review.FileName); }
                    FreshConnectedSave(f);
                }
            });
            Test("explicit corrupt profile setup refuses a different raw record after review", () => {
                using (var f = new Fixture()) { string name = Profiles.FileName(State().Volumes[0].Id); File.WriteAllText(Path.Combine(f.Destination, name), "bad one"); var review = ConnectedReview(f); byte[] changed = Encoding.UTF8.GetBytes("bad two"); File.WriteAllBytes(Path.Combine(f.Destination, name), changed); Check(review.Exists && review.Value == null && review.Error != null, "Malformed review lost raw evidence"); Throws<SafetyException>(() => Profiles.SaveConnected(review, OfflineProfile(), f.Destination)); CheckConnectedReviewFiles(f, changed, name); FreshConnectedSave(f); }
            });
            Test("manual legacy destination binding cannot overwrite newer profile choices", () => {
                using (var f = new Fixture()) { var review = ExistingConnectedReview(f); var changed = review.Value; changed.Name = "New manual sharing choices"; changed.SharingPreset = QuickShareProfile.Uhd; byte[] bytes = ReplaySerialize(changed, typeof(ImportProfile)); File.WriteAllBytes(Path.Combine(f.Destination, review.FileName), bytes); Throws<SafetyException>(() => Profiles.BindConnectedDestination(review, HistoryIdentity(), f.Destination)); CheckConnectedReviewFiles(f, bytes, review.FileName); Check(ConnectedReview(f).Value.SavedDestination == null, "Stale migration bound new choices"); }
            });
            Test("connected review retains absent state without creating configuration storage", () => {
                using (var f = new Fixture()) { var s = State(); string root = Path.Combine(f.Root, "not-created-profiles"); var review = Profiles.ReviewConnected(s.Volumes[0], s.Disks[0], root); Check(!review.Exists && review.Value == null && review.Error == null && !Directory.Exists(root), "Missing review created storage or invented profile"); Profiles.SaveConnected(review, OfflineProfile(), root); Check(Profiles.Load(s.Volumes[0], s.Disks[0], root).Name == OfflineProfile().Name, "Explicit first setup failed"); }
            });
            Test("connected reviewed save preserves full nullable defaults and sharing data", () => {
                using (var f = new Fixture()) { var profile = OfflineProfile(); profile.Name = null; profile.UsesImportDefaults = profile.VideosOnly = profile.IncludePreviews = null; profile.SharingPreset = QuickShareProfile.Uhd; profile.SharingColor = QuickShareProfile.Standard; Profiles.Save(profile, f.Destination); var review = ConnectedReview(f); var draft = review.Value; draft.Name = "Reviewed custom card"; draft.RelativeSource = "PRIVATE"; Profiles.SaveConnected(review, draft, f.Destination); Check(ReplaySerialize(ConnectedReview(f).Value, typeof(ImportProfile)).SequenceEqual(ReplaySerialize(draft, typeof(ImportProfile))) && review.Value.Name == null && review.Value.RelativeSource == "DCIM", "Review was mutated or connected choices narrowed"); var current = ConnectedReview(f); Throws<InvalidOperationException>(() => Profiles.ForgetConnected(current, f.Destination, stage => { if (stage == "Before reviewed profile disposition") throw new InvalidOperationException("Generated held-record cancellation"); })); Check(ConnectedReview(f).Hash == current.Hash, "Refused disposition deleted reviewed data"); Profiles.ForgetConnected(current, f.Destination); Check(!File.Exists(Path.Combine(f.Destination, current.FileName)) && !File.Exists(Path.Combine(f.Destination, ".profile-write.lock")), "Exact reviewed forget did not complete or release writer"); }
            });
            Test("connected explicit re-enrollment replaces exactly reviewed corrupt and old-serial records", () => {
                foreach (bool formatted in new[] { false, true }) using (var f = new Fixture()) {
                    var profile = OfflineProfile(); profile.VolumeSerial += "-old"; string name = Profiles.FileName(profile.VolumeId); byte[] bytes = formatted ? ReplaySerialize(profile, typeof(ImportProfile)) : Encoding.UTF8.GetBytes("generated corrupt profile"); File.WriteAllBytes(Path.Combine(f.Destination, name), bytes); var review = ConnectedReview(f); Check(review.Exists && review.Value == null && review.Error != null, "Damaged/foreign serial did not require explicit setup"); var draft = OfflineProfile(); Profiles.SaveConnected(review, draft, f.Destination); Check(ConnectedReview(f).Value.VolumeSerial == draft.VolumeSerial && !ConnectedReview(f).Value.AutomaticEnabled(new ImportDefaults()), "Explicit re-enrollment failed or inherited old automatic permission"); }
            });
            Test("manual legacy destination binding preserves every other nullable choice and frozen options", () => {
                using (var f = new Fixture()) { var profile = OfflineProfile(); profile.Name = null; profile.UsesImportDefaults = profile.VideosOnly = profile.IncludePreviews = null; profile.SharingPreset = QuickShareProfile.Uhd; profile.SharingColor = QuickShareProfile.Standard; Profiles.Save(profile, f.Destination); var review = ConnectedReview(f); var frozen = review.Value.Resolve(new ImportDefaults()); var bound = Profiles.BindConnectedDestination(review, HistoryIdentity(), f.Destination); var expected = review.Value; expected.SavedDestination = HistoryIdentity(); Check(ReplaySerialize(bound, typeof(ImportProfile)).SequenceEqual(ReplaySerialize(expected, typeof(ImportProfile))) && ReplaySerialize(ConnectedReview(f).Value, typeof(ImportProfile)).SequenceEqual(ReplaySerialize(expected, typeof(ImportProfile))) && review.Value.SavedDestination == null && !frozen.VideosOnly && frozen.IncludePreviews && !frozen.DeleteOriginals, "Binding changed legacy/frozen/share state"); }
            });
            Test("connected save refuses foreign device root and missing review without storage changes", () => {
                using (var f = new Fixture()) { var state = State(); string serial = state.Volumes[0].Serial, instance = state.Disks[0].InstanceId; var review = Profiles.ReviewConnected(state.Volumes[0], state.Disks[0], f.Destination, stage => { if (stage == "After connected profile review") { state.Volumes[0].Serial += "-changed-caller"; state.Disks[0].InstanceId += "-changed-caller"; } }); Check(review.VolumeSerial == serial && review.DiskInstance == instance, "Review adopted later mutable caller identity"); var bad = OfflineProfile(); bad.VolumeSerial += "-foreign"; Throws<SafetyException>(() => Profiles.SaveConnected(review, bad, f.Destination)); Throws<SafetyException>(() => Profiles.SaveConnected(review, OfflineProfile(), f.Source)); Throws<SafetyException>(() => Profiles.SaveConnected(null, OfflineProfile(), f.Destination)); Check(Directory.GetFiles(f.Destination).Length == 0 && Directory.GetFiles(f.Source).Length == 0, "Invalid review created records"); }
            });
            Test("connected review replacement root and held alias failure retain all fixture bytes", () => {
                using (var f = new Fixture()) { ExistingConnectedReview(f); var s = State(); string name = Profiles.FileName(s.Volumes[0].Id); byte[] bytes = File.ReadAllBytes(Path.Combine(f.Destination, name)); Throws<SafetyException>(() => Profiles.ReviewConnected(s.Volumes[0], s.Disks[0], f.Destination, stage => { if (stage == "After connected profile review") { ReplaceFixtureDirectory(f, f.Destination); File.WriteAllBytes(Path.Combine(f.Destination, name), bytes); } })); Check(File.ReadAllBytes(Path.Combine(f.Destination + "-moved", name)).SequenceEqual(bytes) && File.ReadAllBytes(Path.Combine(f.Destination, name)).SequenceEqual(bytes), "Read modified moved/replacement records"); }
                using (var f = new Fixture()) { var expected = ExistingConnectedReview(f); var s = State(); string alias = Path.Combine(f.Source, "held-profile-alias.keep"); Throws<SafetyException>(() => Profiles.ReviewConnected(s.Volumes[0], s.Disks[0], f.Destination, stage => { if (stage == "After connected profile decode") Check(CreateHardLink(alias, Path.Combine(f.Destination, expected.FileName), IntPtr.Zero), "Could not alias owned held profile"); })); File.Delete(alias); Check(ConnectedReview(f).Hash == expected.Hash, "Failed review kept old leases or altered record"); }
            });
            Test("hidden connected editor cancellation keeps exact reviewed record and runs no save", () => {
                using (var f = new Fixture()) { var review = ExistingConnectedReview(f); byte[] bytes = File.ReadAllBytes(Path.Combine(f.Destination, review.FileName)); using (var dialog = new ImportProfileDialog(review.Value, f.Source, "Generated card", new ImportDefaults(), true, () => { throw new Exception("Cancelled editor must not save settings"); })) { IntPtr handle = dialog.Handle; Check(handle != IntPtr.Zero && dialog.Draft != null, "Editor not constructed"); dialog.DeviceName.Text = "Uncommitted name"; dialog.DialogResult = DialogResult.Cancel; } CheckConnectedReviewFiles(f, bytes, review.FileName); }
            });
        }
    }
}
