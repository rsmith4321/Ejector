using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEject {
    public static partial class Tests {
        static ImportProfile ModernProfile(string destination = @"C:\fixture") { return new ImportProfile { Name = "Owned fixture camera", VolumeId = "modern-volume", VolumeSerial = "ABC", DiskInstance = "modern-disk", RelativeSource = "", Destination = destination }; }
        static ImportProfileEdit ProfileEditor(ImportProfile p = null, ImportDefaults defaults = null, string source = null, Action<ImportProfile, bool> save = null, Action forget = null) {
            return new ImportProfileEdit(p ?? ModernProfile(), defaults ?? new ImportDefaults(), source, save ?? ((value, chosen) => {}), forget);
        }
        static void ProfileEditTests() {
            Test("modern shared-default editing retains profile draft custom choices and sharing", delegate {
                var edit = ProfileEditor(); edit.SetName("Retained draft"); edit.SetSharing(true, true, false); edit.SetChoice(ImportDefaultChoice.IncludePreviews, false);
                var review = edit.BeginReview(ProfileReviewKind.RemoveOriginals); var shared = new ImportDefaults { IncludePreviews = true, AutomaticImport = true, CleanLayout = true };
                edit.UpdateDefaults(shared); shared.AutomaticImport = false;
                Check(edit.Snapshot().Name == "Retained draft" && !edit.Options.IncludePreviews && !edit.Automatic && !edit.Accept(review, true) && edit.Snapshot().SharingPreset == QuickShareProfile.Uhd, "Reload rewrote custom/name/sharing or retained old review");
                edit.SetFollowing(true); Check(edit.Options.IncludePreviews && edit.Automatic && edit.Options.CleanLayout && edit.Snapshot().SharingColor == QuickShareProfile.Standard, "Following profile lost cloned updated defaults");
            });
            Test("modern legacy profile preserves all-media custom manual migration", delegate {
                var p = ModernProfile(); var defaults = new ImportDefaults { AutomaticImport = true, DeleteOriginals = true };
                var edit = ProfileEditor(p, defaults); Check(!edit.Snapshot().FollowsDefaults && !edit.Options.VideosOnly && edit.Options.IncludePreviews && !edit.Options.CleanLayout && !edit.Options.DeleteOriginals && !edit.Automatic, "Legacy profile adopted new defaults");
                edit.SetFollowing(true); Check(edit.Options.VideosOnly && edit.Options.DeleteOriginals && edit.Automatic, "Explicit following choices lost");
                edit.SetSharing(true, true, false); edit.SetFollowing(false); edit.SetChoice(ImportDefaultChoice.VideosOnly, false);
                Check(!edit.Options.DeleteOriginals && !edit.Options.RecoverTrash && edit.Snapshot().SharingPreset == QuickShareProfile.Uhd, "Broader scope or import mode changed sharing or retained removal");
                Check(p.UsesImportDefaults == null && p.SharingPreset == null && defaults.VideosOnly == null, "Caller state was mutated");
            });
            Test("modern new profile follows exact cloned defaults without enabling sharing", delegate {
                var defaults = new ImportDefaults { IncludePreviews = false, CleanLayout = true, AskToEject = true };
                var p = defaults.NewProfile(new Volume { Id = "modern-volume", Serial = "ABC", Label = "Fixture" }, new Disk { InstanceId = "modern-disk" });
                var edit = ProfileEditor(p, defaults); defaults.IncludePreviews = true;
                Check(edit.Snapshot().FollowsDefaults && edit.Options.VideosOnly && !edit.Options.IncludePreviews && edit.Options.CleanLayout && edit.Options.AskToEject && !edit.Automatic && !QuickShareProfile.Enabled(edit.Snapshot()), "New device defaults drifted or enabled sharing");
                Throws<SafetyException>(() => edit.SetChoice(ImportDefaultChoice.IncludePreviews, true)); Throws<SafetyException>(() => edit.BeginReview(ProfileReviewKind.RemoveOriginals));
            });
            Test("modern profile removal reviews refuse cancellation foreign owner edits and replay", delegate {
                var edit = ProfileEditor(); var other = ProfileEditor(); edit.SetChoice(ImportDefaultChoice.VideosOnly, true);
                var cancelled = edit.BeginReview(ProfileReviewKind.RecoverTrash); Check(!edit.Accept(cancelled, false) && !edit.Accept(cancelled, true), "Cancel review replayed");
                var review = edit.BeginReview(ProfileReviewKind.RemoveOriginals); Check(review.VideosOnly && !other.Accept(review, true), "Foreign scope accepted");
                edit.SetName("Changed reviewed device"); Check(!edit.Accept(review, true), "Name edit retained old scope");
                review = edit.BeginReview(ProfileReviewKind.RemoveOriginals); Check(edit.Accept(review, true) && !edit.Accept(review, true) && edit.Options.DeleteOriginals, "Review was not one-use");
                var recovery = edit.BeginReview(ProfileReviewKind.RecoverTrash); edit.SetFollowing(true); edit.SetFollowing(false); Check(!edit.Accept(recovery, true), "Mode change retained old review");
                review = edit.BeginReview(ProfileReviewKind.RemoveOriginals); edit.Cancel(); Check(!edit.Accept(review, true), "Closed editor accepted late choice");
            });
            Test("modern profile source and Mac owner require exact connected selection", delegate {
                string root = ModernDefaultsFixture(); var offline = ProfileEditor();
                Throws<SafetyException>(() => offline.SetSource(root)); Throws<SafetyException>(() => offline.BeginReview(ProfileReviewKind.MacOwner, Path.Combine(root, ".Trashes", "501")));
                var edit = ProfileEditor(source: root); edit.SetSource(Path.Combine(root, "DCIM")); Check(edit.Snapshot().RelativeSource == "DCIM", "Relative source not captured");
                Throws<SafetyException>(() => edit.SetSource(Path.GetDirectoryName(root))); Throws<SafetyException>(() => edit.BeginReview(ProfileReviewKind.MacOwner, Path.Combine(root, "501")));
                var review = edit.BeginReview(ProfileReviewKind.MacOwner, Path.Combine(root, ".Trashes", "501")); Check(!edit.Accept(review, false) && edit.Snapshot().MacTrashUid == null, "Owner inferred or accepted after cancellation");
                review = edit.BeginReview(ProfileReviewKind.MacOwner, Path.Combine(root, ".Trashes", "501")); Check(edit.Accept(review, true) && edit.Snapshot().MacTrashUid == "501" && !edit.Options.RecoverTrash, "Owner enabled recovery or wrong scope");
                edit.ClearMacOwner(); edit.SetSource(root); Check(edit.Snapshot().MacTrashUid == null && edit.Snapshot().RelativeSource == "", "Clear owner/whole source failed");
            });
            Test("modern sharing preferences are independent snapshots and remain manual", delegate {
                var edit = ProfileEditor(); edit.SetSharing(true, true, true); var frozen = edit.Snapshot();
                edit.SetFollowing(true); edit.SetSharing(false, false, false); frozen.VolumeId = "caller-only"; frozen.SharingPreset = null;
                var current = edit.Snapshot(); Check(current.VolumeId == "modern-volume" && current.FollowsDefaults && current.SharingPreset == null && current.SharingColor == QuickShareProfile.Standard && !edit.Automatic, "Sharing/defaults alias or automatic work enabled");
            });
            Test("modern profile save freezes destination sharing and rejects edits while worker owns save", delegate {
                string root = ModernDefaultsFixture(); ImportProfileEdit edit = null; int writes = 0;
                using (var entered = new ManualResetEventSlim()) using (var finish = new ManualResetEventSlim()) {
                    edit = ProfileEditor(save: (value, chosen) => { writes++; entered.Set(); Check(finish.Wait(5000), "Save fixture wait timed out"); Check(chosen && value.Destination == root && value.SharingPreset == QuickShareProfile.Uhd, "Frozen save scope changed"); value.VolumeId = "callback-only"; });
                    edit.SetDestination(root); edit.SetSharing(true, true, false); var old = edit.BeginReview(ProfileReviewKind.RemoveOriginals);
                    var save = Task.Run(() => edit.Save()); Check(entered.Wait(5000) && edit.Saving && !edit.Cancel(), "Save was not live or allowed discard");
                    Throws<InvalidOperationException>(() => edit.SetName("late")); Throws<InvalidOperationException>(() => edit.Save()); Check(!edit.Accept(old, true), "Save accepted old review");
                    finish.Set(); Check(save.Wait(5000), "Save did not finish"); save.GetAwaiter().GetResult(); Check(writes == 1 && edit.Closed && edit.Snapshot().VolumeId == "modern-volume", "Callback alias or one-use failure");
                }
            });
            Test("modern older automatic profile requires explicit destination reverification", delegate {
                int writes = 0; var edit = ProfileEditor(save: (p, chosen) => { Check(chosen, "Destination was not explicit"); writes++; });
                edit.SetChoice(ImportDefaultChoice.AutomaticImport, true); Throws<SafetyException>(() => edit.Save()); Check(writes == 0 && !edit.Saving, "Older automatic profile persisted without identity");
                edit.SetDestination(ModernDefaultsFixture()); edit.Save(); Check(writes == 1, "Explicit destination never reached guarded persistence");
            });
            Test("modern failed profile save permits fresh retry but expires confirmations", delegate {
                int writes = 0; var edit = ProfileEditor(save: (p, chosen) => { if (++writes == 1) throw new IOException("Owned failure"); });
                var review = edit.BeginReview(ProfileReviewKind.RemoveOriginals); Throws<IOException>(() => edit.Save());
                Check(!edit.Closed && !edit.Saving && !edit.Accept(review, true), "Failure retained busy/old confirmation"); edit.Save(); Check(writes == 2 && edit.Closed, "Fresh retry failed");
            });
            Test("modern offline save uses exact record compare and preserves media history defaults", delegate {
                string root = ModernDefaultsFixture(), config = Path.Combine(root, "profiles"), target = Path.Combine(root, "destination"); Directory.CreateDirectory(target);
                var p = ModernProfile(target); Profiles.Save(p, config); File.WriteAllText(Path.Combine(root, "retained.mp4"), "owned original");
                ImportDefaults.Save(new ImportDefaults(), config); File.WriteAllText(Path.Combine(config, "history.keep"), "owned history"); var entry = Profiles.Catalog(config).Single();
                var edit = ProfileEditor(entry.Profile, save: (value, chosen) => Profiles.SaveOffline(entry, value, config)); edit.SetName("Renamed offline"); edit.SetSharing(true, true, false); edit.Save();
                var saved = Profiles.Catalog(config).Single(); Check(saved.Profile.Name == "Renamed offline" && saved.Profile.SharingPreset == QuickShareProfile.Uhd, "Offline choices not published");
                var stale = ProfileEditor(entry.Profile, save: (value, chosen) => Profiles.SaveOffline(entry, value, config)); stale.SetName("stale overwrite"); Throws<SafetyException>(() => stale.Save());
                Check(Profiles.Catalog(config).Single().Profile.Name == "Renamed offline" && File.ReadAllText(Path.Combine(root, "retained.mp4")) == "owned original" && File.ReadAllText(Path.Combine(config, "history.keep")) == "owned history" && File.Exists(Path.Combine(config, "import-defaults.json")), "Stale save or preferences affected media/history/defaults");
            });
            Test("modern explicit forget consumes cancellation and retains unrelated saved data", delegate {
                string root = ModernDefaultsFixture(); var p = ModernProfile(root); Profiles.Save(p, root); File.WriteAllText(Path.Combine(root, "unrelated.keep"), "retain"); var entry = Profiles.Catalog(root).Single(); int forgets = 0;
                var edit = ProfileEditor(p, forget: () => { Profiles.ForgetOffline(entry, root); forgets++; });
                var review = edit.BeginReview(ProfileReviewKind.Forget); Check(!edit.Forget(review, false) && !edit.Forget(review, true) && Profiles.Catalog(root).Length == 1, "Cancelled forget replayed");
                review = edit.BeginReview(ProfileReviewKind.Forget); Check(edit.Forget(review, true) && edit.Closed && !edit.Forget(review, true) && forgets == 1 && Profiles.Catalog(root).Length == 0 && File.ReadAllText(Path.Combine(root, "unrelated.keep")) == "retain", "Forget replay or unrelated deletion");
            });
            Test("modern connected profile save binds frozen folders and both fake disk identities", delegate {
                string root = ModernDefaultsFixture(), source = Path.Combine(root, "source"), target = Path.Combine(root, "destination"), config = Path.Combine(root, "profiles");
                Directory.CreateDirectory(source); Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(source, "retained.mp4"), "owned original");
                var d = new ModernStorageDiscovery { State = ModernStorageState(source) };
                d.State.Disks.Add(new Disk { Number = 8, InstanceId = "modern-target", Serial = "target-serial", DevInst = 11 });
                d.State.Volumes.Add(new Volume { Id = "modern-target-volume", Serial = "DEF", Extents = "8:0:1000", Disks = new System.Collections.Generic.List<int> { 8 }, Mounts = new System.Collections.Generic.List<string> { target } });
                var reviewed = Profiles.ReviewConnected(d.State.Volumes[0], d.State.Disks[0], config); var p = new ImportDefaults().NewProfile(d.State.Volumes[0], d.State.Disks[0]); p.Destination = target;
                var edit = ProfileEditor(p, source: source, save: (value, chosen) => Profiles.SaveConnectedSelection(reviewed, value, d.State.Volumes[0], d.State.Disks[0], 1, source, d, chosen, path => path == source ? "modern-volume" : "modern-target-volume"));
                edit.SetDestination(target); edit.SetSharing(true, false, false); edit.Save(); var saved = Profiles.Catalog(config).Single().Profile;
                Check(saved.SavedDestination != null && saved.SavedDestination.VolumeId == "modern-target-volume" && saved.SharingPreset == QuickShareProfile.Hd && saved.FollowsDefaults && File.ReadAllText(Path.Combine(source, "retained.mp4")) == "owned original", "Connected enrollment lost binding or touched source");
                reviewed = Profiles.ReviewConnected(d.State.Volumes[0], d.State.Disks[0], config);
                var stale = ProfileEditor(saved, source: source, save: (value, chosen) => Profiles.SaveConnectedSelection(reviewed, value, d.State.Volumes[0], d.State.Disks[0], 1, source, d, chosen, path => path == source ? "modern-volume" : "modern-target-volume"));
                d.Version = d.State.Epoch = 2; stale.SetName("reconnected stale"); Throws<SafetyException>(() => stale.Save()); Check(Profiles.Catalog(config).Single().Profile.Name == saved.Name, "Reconnect overwrote profile");
            });
        }
    }
}
