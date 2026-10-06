using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        static ImportProfile OfflineProfile() {
            var state = State(); var value = new ImportDefaults().NewProfile(state.Volumes[0], state.Disks[0]);
            value.Name = "Insta360 travel card"; value.RelativeSource = "DCIM"; value.Destination = @"Z:\disconnected-backups"; return value;
        }
        static void SavedProfileTests() {
            Test("offline catalog reads only profile records without media or mounted destinations", delegate { using (var f = new Fixture()) {
                var p = OfflineProfile(); Profiles.Save(p, f.Destination); ImportDefaults.Save(new ImportDefaults(), f.Destination); AppSettings.Save(new AppSettings(), f.Destination);
                File.WriteAllText(Path.Combine(f.Destination, "completed-imports.json"), "not a profile"); File.WriteAllText(Path.Combine(f.Destination, "private.tmp"), "not a profile");
                var entries = Profiles.Catalog(f.Destination);
                Check(entries.Length == 1 && entries[0].CanEdit && entries[0].Display == p.Name && entries[0].Profile.Destination == p.Destination, "Catalog touched unrelated settings or required a mounted source/destination");
            } });
            Test("offline legacy profile migration preserves original custom options and identity", delegate { using (var f = new Fixture()) {
                var p = OfflineProfile(); p.Name = null; p.UsesImportDefaults = p.VideosOnly = p.IncludePreviews = p.CleanLayout = p.AskToEject = p.DeleteOriginals = p.RecoverTrash = null;
                Profiles.Save(p, f.Destination); var snapshot = Profiles.Catalog(f.Destination).Single(); var draft = snapshot.Profile;
                Check(!draft.FollowsDefaults && !draft.Resolve(new ImportDefaults()).VideosOnly && draft.Resolve(new ImportDefaults()).IncludePreviews && !draft.Resolve(new ImportDefaults()).CleanLayout, "Legacy profile adopted new defaults");
                draft.Name = "My original card"; Profiles.SaveOffline(snapshot, draft, f.Destination); var loaded = Profiles.Load(State().Volumes[0], State().Disks[0], f.Destination);
                Check(loaded.Name == draft.Name && loaded.VolumeId == p.VolumeId && loaded.VolumeSerial == p.VolumeSerial && loaded.DiskInstance == p.DiskInstance && loaded.VideosOnly == null && !loaded.Resolve(new ImportDefaults()).VideosOnly, "Rename altered legacy selection or device identity");
            } });
            Test("offline save keeps default inheritance and custom profiles remain independent", delegate { using (var f = new Fixture()) {
                var p = OfflineProfile(); Profiles.Save(p, f.Destination); var entry = Profiles.Catalog(f.Destination).Single(); var draft = entry.Profile; draft.Name = "Renamed default device";
                Profiles.SaveOffline(entry, draft, f.Destination); var current = Profiles.Catalog(f.Destination).Single();
                Check(!current.Profile.Resolve(new ImportDefaults { VideosOnly = false }).VideosOnly, "Offline save froze inherited defaults");
                draft = current.Profile; draft.SetCustom(true, new ImportDefaults()); draft.IncludePreviews = false; Profiles.SaveOffline(current, draft, f.Destination);
                Check(!Profiles.Catalog(f.Destination).Single().Profile.Resolve(new ImportDefaults { IncludePreviews = true }).IncludePreviews, "Custom profile changed with defaults");
            } });
            Test("stale offline save and forget refuse a newer profile without overwriting it", delegate { using (var f = new Fixture()) {
                var p = OfflineProfile(); Profiles.Save(p, f.Destination); var first = Profiles.Catalog(f.Destination).Single(); var second = Profiles.Catalog(f.Destination).Single();
                var changed = second.Profile; changed.Name = "New choices"; Profiles.SaveOffline(second, changed, f.Destination);
                var stale = first.Profile; stale.Name = "Old editor"; Throws<SafetyException>(() => Profiles.SaveOffline(first, stale, f.Destination)); Throws<SafetyException>(() => Profiles.ForgetOffline(first, f.Destination));
                Check(Profiles.Catalog(f.Destination).Single().Profile.Name == "New choices" && !File.Exists(Path.Combine(f.Destination, ".profile-write.lock")), "Stale editor changed new choices or kept write lock");
            } });
            Test("same-byte replacement and restored timestamp cannot satisfy an old profile snapshot", delegate { using (var f = new Fixture()) {
                Profiles.Save(OfflineProfile(), f.Destination); var original = Profiles.Catalog(f.Destination).Single(); string path = Path.Combine(f.Destination, original.FileName);
                byte[] bytes = File.ReadAllBytes(path); DateTime modified = File.GetLastWriteTimeUtc(path); File.Move(path, path + ".held-old"); File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, modified);
                var draft = original.Profile; draft.Name = "Stale"; Throws<SafetyException>(() => Profiles.SaveOffline(original, draft, f.Destination)); Throws<SafetyException>(() => Profiles.ForgetOffline(original, f.Destination));
                Check(File.ReadAllBytes(path).SequenceEqual(bytes), "Replacement record was altered");
            } });
            Test("offline save cannot re-enroll a formatted device or change source/owner scope", delegate { using (var f = new Fixture()) {
                Profiles.Save(OfflineProfile(), f.Destination); var entry = Profiles.Catalog(f.Destination).Single();
                foreach (Action<ImportProfile> alter in new Action<ImportProfile>[] { p => p.VolumeId = "another", p => p.VolumeSerial = "formatted", p => p.DiskInstance = "other-disk", p => p.RelativeSource = "PRIVATE", p => p.MacTrashUid = "501" }) {
                    var draft = entry.Profile; alter(draft); Throws<SafetyException>(() => Profiles.SaveOffline(entry, draft, f.Destination));
                }
                Check(Profiles.Catalog(f.Destination).Single().Hash == entry.Hash, "Offline edits changed enrollment authority");
            } });
            Test("new connected enrollment invalidates earlier offline editors after formatting", delegate { using (var f = new Fixture()) {
                var p = OfflineProfile(); Profiles.Save(p, f.Destination); var entry = Profiles.Catalog(f.Destination).Single(); p.VolumeSerial = "new-card-serial"; Profiles.Save(p, f.Destination);
                var draft = entry.Profile; draft.Name = "Old card editor"; Throws<SafetyException>(() => Profiles.SaveOffline(entry, draft, f.Destination)); Throws<SafetyException>(() => Profiles.ForgetOffline(entry, f.Destination));
                Check(Profiles.Catalog(f.Destination).Single().Profile.VolumeSerial == "new-card-serial", "Stale editor restored a formatted card's identity");
            } });
            Test("read-only profile protection survives refused offline save and forget", delegate { using (var f = new Fixture()) {
                Profiles.Save(OfflineProfile(), f.Destination); var entry = Profiles.Catalog(f.Destination).Single(); string path = Path.Combine(f.Destination, entry.FileName); File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
                try {
                    var draft = entry.Profile; draft.Name = "Cannot save"; Throws<UnauthorizedAccessException>(() => Profiles.SaveOffline(entry, draft, f.Destination)); Throws<SafetyException>(() => Profiles.ForgetOffline(entry, f.Destination));
                    Check(Profiles.Catalog(f.Destination).Single().Hash == entry.Hash && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0 && Directory.GetFiles(f.Destination).Length == 1, "Refusal altered protection or retained private work");
                } finally { File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly); }
            } });
            Test("explicitly clearing a Mac owner offline narrows scope without enabling recovery", delegate { using (var f = new Fixture()) {
                var p = OfflineProfile(); p.MacTrashUid = "501"; Profiles.Save(p, f.Destination); var entry = Profiles.Catalog(f.Destination).Single(); var draft = entry.Profile; draft.MacTrashUid = null;
                Profiles.SaveOffline(entry, draft, f.Destination); var loaded = Profiles.Catalog(f.Destination).Single().Profile;
                Check(loaded.MacTrashUid == null && !loaded.Resolve(new ImportDefaults()).RecoverTrash, "Clearing an owner broadened scope or enabled recovery");
            } });
            Test("malformed oversized mismatched and protected-folder profiles stay visible beside valid entries", delegate { using (var f = new Fixture()) {
                Profiles.Save(OfflineProfile(), f.Destination);
                File.WriteAllText(Path.Combine(f.Destination, new string('A', 64) + ".json"), "not json"); File.WriteAllText(Path.Combine(f.Destination, new string('B', 64) + ".json"), new string('x', 65537));
                var bad = OfflineProfile(); bad.VolumeId = "wrong-filename"; Profiles.WriteJson(bad, typeof(ImportProfile), f.Destination, new string('C', 64) + ".json");
                var protectedProfile = OfflineProfile(); protectedProfile.VolumeId = "protected-source"; protectedProfile.RelativeSource = "$recycle.bin"; Profiles.Save(protectedProfile, f.Destination);
                var entries = Profiles.Catalog(f.Destination);
                Check(entries.Length == 5 && entries.Count(x => x.CanEdit) == 1 && entries.Where(x => !x.CanEdit).All(x => !String.IsNullOrEmpty(x.Error)), "Corrupt records were hidden or interpreted as usable choices");
                Check(Directory.GetFiles(f.Destination).Length == 5, "Review repaired or deleted configuration implicitly");
            } });
            Test("redirected config root and profile files refuse without reading target contents", delegate { using (var f = new Fixture()) {
                var p = OfflineProfile(); Profiles.Save(p, f.Destination); string redirect = Path.Combine(f.Source, "profiles-link"); CreateJunction(f, redirect, f.Destination);
                Throws<SafetyException>(() => Profiles.Catalog(redirect)); var entry = Profiles.Catalog(f.Destination).Single(); File.Delete(Path.Combine(f.Destination, entry.FileName)); CreateJunction(f, Path.Combine(f.Destination, entry.FileName), f.Source);
                var invalid = Profiles.Catalog(f.Destination).Single(); Check(!invalid.CanEdit && invalid.Error.Contains("Reparse"), "Linked profile was followed");
            } });
            Test("profile writer conflicts fail and release permits a fresh explicit save", delegate { using (var f = new Fixture()) {
                Profiles.Save(OfflineProfile(), f.Destination); var entry = Profiles.Catalog(f.Destination).Single(); var draft = entry.Profile; draft.Name = "Updated";
                using (Profiles.WriterGate(f.Destination)) {
                    Throws<System.ComponentModel.Win32Exception>(() => Profiles.SaveOffline(entry, draft, f.Destination)); Throws<System.ComponentModel.Win32Exception>(() => Profiles.Save(draft, f.Destination)); Throws<System.ComponentModel.Win32Exception>(() => Profiles.ForgetOffline(entry, f.Destination));
                }
                Profiles.SaveOffline(entry, draft, f.Destination); Check(Profiles.Catalog(f.Destination).Single().Profile.Name == "Updated", "Writer conflict retained a lock or wrote prematurely");
            } });
            Test("forgetting an offline profile keeps media copies history defaults and other devices", delegate { using (var f = new Fixture()) {
                f.Put("original.mp4", "keep source"); File.WriteAllText(Path.Combine(f.Destination, "verified.mp4"), "keep copy"); string root = Path.Combine(f.Root, "profiles");
                var p = OfflineProfile(); Profiles.Save(p, root); var other = OfflineProfile(); other.VolumeId = "another-saved-device"; Profiles.Save(other, root); ImportDefaults.Save(new ImportDefaults(), root);
                File.WriteAllText(Path.Combine(root, "completed-imports.json"), "keep history"); var entry = Profiles.Catalog(root).Single(x => x.Profile.VolumeId == p.VolumeId); Profiles.ForgetOffline(entry, root);
                Check(Profiles.Catalog(root).Single().Profile.VolumeId == other.VolumeId && ImportDefaults.Load(root).Snapshot().VideosOnly && File.ReadAllText(Path.Combine(root, "completed-imports.json")) == "keep history" && File.ReadAllText(Path.Combine(f.Source, "original.mp4")) == "keep source" && File.ReadAllText(Path.Combine(f.Destination, "verified.mp4")) == "keep copy", "Forget touched unrelated configuration or media");
            } });
            Test("offline destination policy rejects same physical disk and volume regardless of case", delegate {
                var p = OfflineProfile(); var target = HistoryIdentity(); OfflineProfileDestination.Validate(p, target);
                target.DiskInstance = p.DiskInstance.ToUpperInvariant(); Throws<SafetyException>(() => OfflineProfileDestination.Validate(p, target)); target = HistoryIdentity(); target.VolumeId = p.VolumeId.ToUpperInvariant(); Throws<SafetyException>(() => OfflineProfileDestination.Validate(p, target));
                target = HistoryIdentity(); target.VolumeSerial = null; Throws<SafetyException>(() => OfflineProfileDestination.Validate(p, target));
            });
            Test("offline editor isolates canceled drafts and keeps source/owner selection unavailable", delegate {
                var p = OfflineProfile(); p.MacTrashUid = "501"; var defaults = new ImportDefaults();
                using (var dialog = new ImportProfileDialog(p, null, "Disconnected card", defaults, true)) {
                    Check(!dialog.ChooseSource.Enabled && !dialog.ChooseOwner.Enabled && dialog.SourceFolder.ReadOnly && dialog.SourceFolder.Text == "DCIM", "Offline editor offered a media folder or guessed owner");
                    dialog.DeviceName.Text = "Saved camera name"; dialog.UseCustom.Checked = true; dialog.Selection.Previews.Checked = false; dialog.ApplyEdits();
                    Check(dialog.Draft.Name == "Saved camera name" && !dialog.Draft.FollowsDefaults && dialog.Draft.IncludePreviews == false && dialog.Draft.RelativeSource == p.RelativeSource && dialog.Draft.MacTrashUid == "501", "Offline draft changed identity or lost selected choices");
                    Throws<SafetyException>(() => dialog.ValidateMacOwner(@"Z:\.Trashes\501"));
                }
                Check(p.Name == "Insta360 travel card" && p.FollowsDefaults && p.IncludePreviews == true && p.MacTrashUid == "501", "Cancel mutated original profile");
            });
            Test("offline removal broadening resets both removal choices and still requires enabling acknowledgment", delegate {
                var p = OfflineProfile(); p.UsesImportDefaults = false; p.DeleteOriginals = p.RecoverTrash = true;
                using (var dialog = new ImportProfileDialog(p, null, "Disconnected card", new ImportDefaults(), true)) {
                    int confirmations = 0; dialog.Selection.ConfirmRemoval = dialog.Selection.ConfirmRecovery = () => { confirmations++; return false; };
                    dialog.Selection.Videos.Checked = false; Check(!dialog.Selection.DeleteOriginals.Checked && !dialog.Selection.RecoverTrash.Checked, "Broadening retained removal");
                    dialog.Selection.DeleteOriginals.Checked = true; dialog.Selection.RecoverTrash.Checked = true; dialog.ApplyEdits();
                    Check(confirmations == 2 && dialog.Draft.DeleteOriginals == false && dialog.Draft.RecoverTrash == false, "Offline enable bypassed acknowledgment");
                }
            });
            Test("offline editor rejects empty names and network destinations without source access", delegate {
                using (var dialog = new ImportProfileDialog(OfflineProfile(), null, "Disconnected card", new ImportDefaults(), true)) {
                    dialog.DeviceName.Text = "   "; Throws<SafetyException>(() => dialog.ApplyEdits()); dialog.DeviceName.Text = "Camera"; dialog.DestinationFolder.Text = @"\\server\share"; Throws<SafetyException>(() => dialog.ApplyEdits());
                }
            });
            Test("saved profile list exposes invalid records safely and requires explicit action selection", delegate {
                using (var f = new Fixture()) {
                    var p = OfflineProfile(); Profiles.Save(p, f.Destination); File.WriteAllText(Path.Combine(f.Destination, new string('A', 64) + ".json"), "bad"); int mutations = 0;
                    using (var dialog = new SavedProfilesDialog(() => Profiles.Catalog(f.Destination), () => new ImportDefaults(), (entry, draft) => mutations++, entry => mutations++)) {
                        IntPtr handle = dialog.Handle, listHandle = dialog.ProfilesList.Handle; Check(handle != IntPtr.Zero && listHandle != IntPtr.Zero && !dialog.Edit.Enabled && !dialog.Forget.Enabled && !dialog.Changed && mutations == 0 && dialog.ProfilesList.Items.Count == 2, "List opened an action automatically");
                        dialog.ProfilesList.Items.Cast<ListViewItem>().Single(x => ((SavedProfile)x.Tag).CanEdit).Selected = true; dialog.UpdateSelection();
                        Check(dialog.Edit.Enabled && dialog.Forget.Enabled && dialog.Detail.Text.Contains("Originals are kept") && dialog.Detail.Text.Contains(p.VolumeId), "Selected profile lacks original-retention or identity disclosure: " + dialog.Detail.Text);
                        dialog.ProfilesList.SelectedItems[0].Selected = false; dialog.ProfilesList.Items.Cast<ListViewItem>().Single(x => !((SavedProfile)x.Tag).CanEdit).Selected = true; dialog.UpdateSelection();
                        Check(!dialog.Edit.Enabled && !dialog.Forget.Enabled && dialog.Detail.Text.Contains("left intact") && mutations == 0, "Invalid record granted mutation authority");
                    }
                }
            });
        }
    }
}
