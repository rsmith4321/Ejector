using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static ImportProfile DefaultsContractProfile(Fixture f, Inventory state, ImportDefaults defaults, int index = 0) {
            var volume = state.Volumes[index]; var disk = state.Disks.Single(d => d.Number == volume.Disks.Single());
            var profile = defaults.NewProfile(volume, disk); profile.RelativeSource = "DCIM"; profile.Destination = f.Destination;
            profile.SavedDestination = HistoryIdentity(); profile.MacTrashUid = "501"; return profile;
        }
        static string DefaultsProfileJson(ImportProfile profile) {
            using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(ImportProfile)).WriteObject(stream, profile); return Encoding.UTF8.GetString(stream.ToArray()); }
        }
        static ImportProfile DefaultsProfileRoundTrip(ImportProfile profile, Inventory state, string root) {
            string expected = DefaultsProfileJson(profile); Profiles.Save(profile, root);
            var volume = state.Volumes.Single(v => v.Id == profile.VolumeId); var disk = state.Disks.Single(d => d.InstanceId == profile.DiskInstance);
            var loaded = Profiles.Load(volume, disk, root);
            Check(DefaultsProfileJson(loaded) == expected, "Full profile roundtrip changed a persisted identity, folder, nullable seed, owner, destination binding or sharing field"); return loaded;
        }
        static void DefaultsChoices(ImportProfile profile, ImportDefaults defaults, bool videos, bool previews, bool clean, bool ask, bool delete, bool recover, bool automatic) {
            var value = profile.Resolve(defaults);
            Check(value.VideosOnly == videos && value.IncludePreviews == previews && value.CleanLayout == clean && value.AskToEject == ask && value.DeleteOriginals == delete && value.RecoverTrash == recover && profile.AutomaticEnabled(defaults) == automatic,
                "Combined effective import choices differ from the saved/default/custom contract");
        }
        static ImportResult DefaultsContractImport(Fixture f, ImportProfile profile, MediaOptions options, Action<string> report = null) {
            return new VerifiedImporter(new Operations(), _ => UInt64.MaxValue, _ => {}, (root, uid, token, validate) => DeviceTrash.Read(root, FixtureTrashSid, uid, token, validate))
                .Run(f.Source, Path.Combine(f.Source, profile.RelativeSource), profile.Destination, profile.VolumeId, new FakeGuard(), CancellationToken.None, report ?? (_ => {}), options);
        }
        static void DefaultsContractTests() {
            Test("persisted Mac defaults sequence preserves full seeds custom legacy sharing and frozen choices", () => { using (var f = new Fixture()) {
                var state = CardState(); string root = Path.Combine(f.Root, "defaults-contract"); Directory.CreateDirectory(root);
                var factory = ImportDefaults.Load(root); var initial = DefaultsContractProfile(f, state, factory);
                DefaultsChoices(initial, factory, true, true, true, false, false, false, false);
                initial = DefaultsProfileRoundTrip(initial, state, root); string initialJson = DefaultsProfileJson(initial);
                Check(initial.FollowsDefaults && initial.SharingPreset == null && initial.SharingColor == null, "Factory inheritance or manual sharing opt-out lost");
                ImportDefaults.Save(new ImportDefaults { VideosOnly = true, IncludePreviews = false, CleanLayout = false, AskToEject = true, AutomaticImport = true }, root);
                var changed = ImportDefaults.Load(root); var customized = DefaultsContractProfile(f, state, changed, 1);
                customized = DefaultsProfileRoundTrip(customized, state, root);
                DefaultsChoices(customized, changed, true, false, false, true, false, false, true);
                var inherited = initial.Resolve(changed); bool inheritedAutomatic = initial.AutomaticEnabled(changed);
                Check(inherited.VideosOnly && !inherited.IncludePreviews && !inherited.CleanLayout && inherited.AskToEject && inheritedAutomatic && !inherited.DeleteOriginals && !inherited.RecoverTrash, "Stored initial card failed live inheritance");
                Check(DefaultsProfileJson(initial) == initialJson && DefaultsProfileJson(Profiles.Load(state.Volumes[0], state.Disks[0], root)) == initialJson, "Defaults resolution/save mutated the stored seed profile");
                var custom = customized.Copy(); custom.SetCustom(true, changed); custom.CleanLayout = true;
                custom = DefaultsProfileRoundTrip(custom, state, root); string customJson = DefaultsProfileJson(custom); var customSnapshot = custom.Copy();
                Check(!ImportDefaults.Load(root).Snapshot().CleanLayout, "Per-card override changed shared defaults");
                ImportDefaults.Save(new ImportDefaults { VideosOnly = false, IncludePreviews = false, CleanLayout = false, AskToEject = true, AutomaticImport = false }, root);
                var next = ImportDefaults.Load(root);
                DefaultsChoices(custom, next, true, false, true, true, false, false, true);
                Check(DefaultsProfileJson(custom) == customJson && inherited.VideosOnly && inheritedAutomatic, "Custom or in-flight snapshot changed after another defaults save");
                custom.DeleteOriginals = custom.RecoverTrash = true; custom.SetCustom(false, next);
                DefaultsChoices(custom, next, false, false, false, true, false, false, false);
                Check(custom.FollowsDefaults && custom.VideosOnly == false && custom.AutomaticImport == false && custom.DeleteOriginals == false && custom.RecoverTrash == false, "Switch to defaults retained destructive/automatic seeds");
                var malformed = custom.Copy(); malformed.DeleteOriginals = malformed.RecoverTrash = malformed.AutomaticImport = true;
                malformed = DefaultsProfileRoundTrip(malformed, state, root);
                DefaultsChoices(malformed, next, false, false, false, true, false, false, false);
                custom.SetCustom(true, changed); custom = DefaultsProfileRoundTrip(custom, state, root);
                DefaultsChoices(custom, next, true, false, false, true, false, false, true);
                Check(custom.UsesImportDefaults == false && initial.FollowsDefaults, "Explicit custom/inherited roundtrip flags changed");
                var sharing = initial.Copy(); sharing.SharingPreset = QuickShareProfile.Uhd; sharing.SharingColor = QuickShareProfile.Standard;
                sharing = DefaultsProfileRoundTrip(sharing, state, root); sharing.Resolve(changed);
                Check(sharing.SharingPreset == QuickShareProfile.Uhd && sharing.SharingColor == QuickShareProfile.Standard && sharing.MacTrashUid == "501", "Import resolution altered independent sharing/owner choices");
                var legacy = initial.Copy(); legacy.UsesImportDefaults = legacy.VideosOnly = legacy.IncludePreviews = legacy.CleanLayout = null; legacy.DeleteOriginals = true;
                legacy = DefaultsProfileRoundTrip(legacy, state, root); string legacyJson = DefaultsProfileJson(legacy);
                DefaultsChoices(legacy, next, false, true, false, false, true, false, false);
                Check(legacy.CleanLayout == null && !legacy.FollowsDefaults && legacy.DeleteOriginals == true, "Legacy nullable seeds or explicit deletion were silently migrated");
                Check(!DefaultsContractProfile(f, state, next).Resolve(next).DeleteOriginals, "New card adopted legacy per-device deletion");
                next.DeleteOriginals = next.RecoverTrash = true; ImportDefaults.Save(next, root); var removal = ImportDefaults.Load(root);
                DefaultsChoices(DefaultsContractProfile(f, state, removal), removal, false, false, false, true, true, true, false);
                var running = initial.Resolve(removal); Check(running.DeleteOriginals && running.RecoverTrash, "Existing default card missed simultaneous removal/recovery");
                DefaultsChoices(customSnapshot, removal, true, false, true, true, false, false, true);
                Check(DefaultsProfileJson(customSnapshot) == customJson, "Simultaneous removal/recovery defaults changed the earlier full custom snapshot");
                DefaultsChoices(legacy, removal, false, true, false, false, true, false, false);
                Check(DefaultsProfileJson(legacy) == legacyJson, "Removal defaults rewrote legacy fields");
                var switcher = customSnapshot.Copy(); switcher.SetCustom(false, removal);
                Check(switcher.FollowsDefaults && switcher.DeleteOriginals == true && switcher.RecoverTrash == true, "Switching a custom card to shared defaults did not capture both enabled destructive seeds");
                switcher.SetCustom(true, removal);
                switcher = DefaultsProfileRoundTrip(switcher, state, root);
                DefaultsChoices(switcher, factory, false, false, false, true, true, true, false);
                Check(!switcher.FollowsDefaults, "Simultaneous inherited destructive flags did not freeze when switched to custom");
                removal.SetVideosOnly(true); ImportDefaults.Save(removal, root); removal = ImportDefaults.Load(root);
                Check(removal.Snapshot().VideosOnly && removal.Snapshot().DeleteOriginals && removal.Snapshot().RecoverTrash, "Persisted narrowing selection cleared acknowledged removal choices");
                removal.SetVideosOnly(false); ImportDefaults.Save(removal, root); var broadened = ImportDefaults.Load(root);
                DefaultsChoices(initial, broadened, false, false, false, true, false, false, false);
                Check(running.DeleteOriginals && running.RecoverTrash, "Broadening changed a running destructive snapshot");
                broadened.VideosOnly = null; broadened.DeleteOriginals = broadened.RecoverTrash = true; ImportDefaults.Save(broadened, root);
                var implicitVideo = ImportDefaults.Load(root); Check(implicitVideo.Snapshot().VideosOnly, "Absent video default did not retain factory videos-only");
                implicitVideo.SetVideosOnly(false); ImportDefaults.Save(implicitVideo, root);
                DefaultsChoices(initial, ImportDefaults.Load(root), false, false, false, true, false, false, false);
            } });
            Test("legacy omitted fields retain explicit deletion without adopting new recovery automatic or prompt defaults", () => { using (var f = new Fixture()) {
                var state = State(); string root = Path.Combine(f.Root, "legacy-contract"); var profile = DefaultsContractProfile(f, state, new ImportDefaults());
                profile.UsesImportDefaults = profile.VideosOnly = profile.IncludePreviews = profile.CleanLayout = profile.AskToEject = profile.AutomaticImport = profile.RecoverTrash = null; profile.DeleteOriginals = true;
                Profiles.Save(profile, root); string path = Path.Combine(root, Profiles.FileName(profile.VolumeId)); string json = File.ReadAllText(path);
                const string omitted = @",?""(?:UsesImportDefaults|VideosOnly|IncludePreviews|CleanLayout|AskToEject|AutomaticImport|RecoverTrash)"":null";
                Check(Regex.Matches(json, omitted).Count == 7, "Legacy fixture did not start with all nullable action fields");
                File.WriteAllText(path, Regex.Replace(json, omitted, "").Replace("{,", "{"));
                var loaded = Profiles.Load(state.Volumes[0], state.Disks[0], root); Check(DefaultsProfileJson(loaded) == DefaultsProfileJson(profile), "Omitted nullable fields changed full legacy profile");
                var newlyEnabled = new ImportDefaults { VideosOnly = true, IncludePreviews = false, CleanLayout = true, AskToEject = true, AutomaticImport = true, DeleteOriginals = true, RecoverTrash = true };
                DefaultsChoices(loaded, newlyEnabled, false, true, false, false, true, false, false);
                loaded = DefaultsProfileRoundTrip(loaded, state, root); f.Put(@"DCIM\old.mp4", "legacy-video"); f.Put(@"DCIM\photo.jpg", "legacy-photo"); f.Put(@"DCIM\unknown.bin", "legacy-unknown");
                f.Put(Path.Combine("$RECYCLE.BIN", FixtureTrashSid, "$IBROKEN"), "unselected malformed record"); int catalogs = 0;
                var result = new VerifiedImporter(new Operations(), _ => UInt64.MaxValue, _ => {}, (card, uid, token, validate) => { catalogs++; throw new Exception("Legacy recovery must stay off"); })
                    .Run(f.Source, Path.Combine(f.Source, loaded.RelativeSource), loaded.Destination, loaded.VolumeId, new FakeGuard(), CancellationToken.None, _ => {}, loaded.Resolve(newlyEnabled));
                Check(result.Files == 3 && result.RemovedOriginals == 2 && !result.Receipt.VideosOnly && !result.Receipt.CleanLayout && result.Receipt.OriginalRemovalRequested && !result.Receipt.TrashRecoveryRequested && catalogs == 0, "Legacy actual importer adopted new shared choices or lost explicit deletion");
                Check(File.ReadAllText(Saved(result, "old.mp4")) == "legacy-video" && File.ReadAllText(Saved(result, "photo.jpg")) == "legacy-photo" && File.ReadAllText(Path.Combine(f.Source, @"DCIM\unknown.bin")) == "legacy-unknown" && File.Exists(Path.Combine(f.Source, "$RECYCLE.BIN", FixtureTrashSid, "$IBROKEN")), "Legacy generated copies, unknown or unselected Trash changed");
            } });
            Test("persisted malformed inherited flags cannot copy photos or enter original removal and Trash clearing", () => { using (var f = new Fixture()) {
                var state = State(); string root = Path.Combine(f.Root, "inherited-contract"); var profile = DefaultsContractProfile(f, state, new ImportDefaults());
                profile.VideosOnly = profile.CleanLayout = profile.AskToEject = profile.IncludePreviews = false; profile.DeleteOriginals = profile.RecoverTrash = profile.AutomaticImport = true;
                profile = DefaultsProfileRoundTrip(profile, state, root); string stored = DefaultsProfileJson(profile);
                ImportDefaults.Save(new ImportDefaults { AskToEject = true }, root); var defaults = ImportDefaults.Load(root);
                DefaultsChoices(profile, defaults, true, true, true, true, false, false, false);
                f.Put(@"DCIM\TEST001.MP4", "inherited-video"); f.Put(@"DCIM\PHOTO001.JPG", "inherited-photo"); f.Put(@".Trashes\501\RECOVER001.MP4", "unselected-trash-video"); int catalogs = 0;
                var result = new VerifiedImporter(new Operations(), _ => UInt64.MaxValue, _ => {}, (card, uid, token, validate) => { catalogs++; throw new Exception("Inherited recovery is disabled"); })
                    .Run(f.Source, Path.Combine(f.Source, profile.RelativeSource), profile.Destination, profile.VolumeId, new FakeGuard(), CancellationToken.None, _ => {}, profile.Resolve(defaults));
                Check(result.Files == 1 && result.HasSelectedMedia && result.RemovedOriginals == 0 && result.ClearedTrashFiles == 0 && catalogs == 0 && result.Receipt.OriginalsKept && result.Receipt.VideosOnly && result.Receipt.CleanLayout && !result.Receipt.OriginalRemovalRequested && !result.Receipt.TrashRecoveryRequested, "Actual inherited import entered stale destructive settings");
                Check(result.Receipt.Files.Single().RelativePath == "TEST001.MP4" && File.ReadAllText(Saved(result, "TEST001.MP4")) == "inherited-video" && Directory.GetFiles(Path.Combine(f.Source, "DCIM")).Length == 2 && File.ReadAllText(Path.Combine(f.Source, @".Trashes\501\RECOVER001.MP4")) == "unselected-trash-video" && DefaultsProfileJson(Profiles.Load(state.Volumes[0], state.Disks[0], root)) == stored, "Clean saved bytes, retained originals/Trash or stored profile changed");
            } });
            Test("persisted shared removal recovery reuse and completion chain keeps running choices frozen", () => { using (var f = new Fixture()) {
                var state = State(); state.Volumes[0].Group = StorageGroup.Camera; state.Volumes[0].Mounts[0] = f.Source;
                string root = Path.Combine(f.Root, "integration-contract"); var profile = DefaultsContractProfile(f, state, new ImportDefaults());
                profile.DeleteOriginals = profile.RecoverTrash = true; profile.VideosOnly = profile.CleanLayout = false;
                profile = DefaultsProfileRoundTrip(profile, state, root); ImportDefaults.Save(new ImportDefaults { AskToEject = true }, root); AppSettings.Save(new AppSettings { ShowNotifications = false }, root);
                f.Put(@"DCIM\TEST001.MP4", "selected-video"); f.Put(@"DCIM\PHOTO001.JPG", "retained-photo");
                var registry = new ImportCompletionRegistry(); var fake = new FakeEject(); var eject = new CompletedEjectService(registry, new FakeDiscovery { State = state }, fake, new Operations(), _ => profile.VolumeId);
                var firstOrigin = new ImportCompletionOrigin(state, profile.VolumeId, f.Source); var firstOptions = profile.Resolve(ImportDefaults.Load(root)); var first = DefaultsContractImport(f, profile, firstOptions);
                var firstBatch = ImportHistory.Create(first, profile, profile.Name, HistoryIdentity()); ImportHistory.Add(firstBatch, root);
                var firstChoice = new ImportCompletion(firstOrigin, profile.Name, first.Folder, f.Destination, HistoryIdentity(), first.HasSelectedMedia, "Verified generated video; originals retained.", firstBatch); registry.Insert(firstChoice);
                Check(CompletionPresentation.ShowChoice(firstOptions.AskToEject, true, AppSettings.Load(root).NotificationsEnabled) && first.RemovedOriginals == 0 && first.Files == 1 && first.Receipt.Files.Single().RelativePath == "TEST001.MP4" && fake.Calls == 0 && registry.Get(firstChoice.Id) != null, "First inherited import or notification-off manual completion changed");
                AppSettings.Save(new AppSettings { ShowNotifications = true }, root);
                var secondOrigin = new ImportCompletionOrigin(state, profile.VolumeId, f.Source); var secondOptions = profile.Resolve(ImportDefaults.Load(root)); var second = DefaultsContractImport(f, profile, secondOptions);
                var secondBatch = ImportHistory.Create(second, profile, profile.Name, HistoryIdentity()); ImportHistory.Add(secondBatch, root);
                var secondChoice = new ImportCompletion(secondOrigin, profile.Name, second.Folder, f.Destination, HistoryIdentity(), second.HasSelectedMedia, "Verified generated video reused.", secondBatch);
                Check(registry.Insert(secondChoice).SequenceEqual(new[] { firstChoice.Id }) && firstChoice.Id != secondChoice.Id && second.Files == 1 && second.Reused == 1 && CompletionPresentation.ShowChoice(secondOptions.AskToEject, true, AppSettings.Load(root).NotificationsEnabled), "Verified reuse did not create a fresh explicit choice");
                Throws<SafetyException>(() => eject.Execute(firstChoice.Id)); Check(fake.Calls == 0 && registry.Get(secondChoice.Id) != null, "Superseded completion performed removal");
                f.Put(@".Trashes\501\RECOVER001.MP4", "selected-trash-video"); f.Put(@".Trashes\501\RETAIN001.JPG", "retained-trash-photo"); f.Put(@".Trashes\502\OTHER.MP4", "other-owner-video");
                var enabled = ImportDefaults.Load(root); enabled.DeleteOriginals = enabled.RecoverTrash = true; ImportDefaults.Save(enabled, root);
                var removalOrigin = new ImportCompletionOrigin(state, profile.VolumeId, f.Source); var running = profile.Resolve(ImportDefaults.Load(root)); bool edited = false;
                var removed = DefaultsContractImport(f, profile, running, message => { if (!edited && message.StartsWith("Copying")) { edited = true; var changed = ImportDefaults.Load(root); changed.SetVideosOnly(false); ImportDefaults.Save(changed, root); } });
                Check(edited && running.VideosOnly && running.DeleteOriginals && running.RecoverTrash && removed.RemovedOriginals == 1 && removed.ClearedTrashFiles == 1 && removed.RecoveredTrashFiles == 1 && removed.Reused == 1 && removed.Receipt.OriginalRemovalRequested && removed.Receipt.TrashRecoveryRequested, "Mid-copy defaults save changed frozen original/removal/recovery choices");
                Check(!File.Exists(Path.Combine(f.Source, @"DCIM\TEST001.MP4")) && !File.Exists(Path.Combine(f.Source, @".Trashes\501\RECOVER001.MP4")) && File.ReadAllText(Path.Combine(f.Source, @"DCIM\PHOTO001.JPG")) == "retained-photo" && File.ReadAllText(Path.Combine(f.Source, @".Trashes\501\RETAIN001.JPG")) == "retained-trash-photo" && File.ReadAllText(Path.Combine(f.Source, @".Trashes\502\OTHER.MP4")) == "other-owner-video", "Generated videos were not cleared selectively or photos/other owner changed");
                Check(File.ReadAllText(Saved(removed, "TEST001.MP4")) == "selected-video" && File.ReadAllText(TrashSaved(removed, "RECOVER001.MP4")) == "selected-trash-video", "Verified full-quality generated copies changed");
                var removalBatch = ImportHistory.Create(removed, profile, profile.Name, HistoryIdentity()); ImportHistory.Add(removalBatch, root);
                var removalChoice = new ImportCompletion(removalOrigin, profile.Name, removed.Folder, f.Destination, HistoryIdentity(), removed.HasSelectedMedia, "Selected generated videos verified and removed.", removalBatch); registry.Insert(removalChoice);
                var next = ImportDefaults.Load(root); Check(!next.Snapshot().DeleteOriginals && !next.Snapshot().RecoverTrash && !next.Snapshot().VideosOnly, "Persisted photo broadening kept destructive defaults"); next.SetVideosOnly(true); ImportDefaults.Save(next, root);
                var emptyOrigin = new ImportCompletionOrigin(state, profile.VolumeId, f.Source); var emptyOptions = profile.Resolve(ImportDefaults.Load(root)); var empty = DefaultsContractImport(f, profile, emptyOptions);
                var emptyChoice = new ImportCompletion(emptyOrigin, profile.Name, empty.Folder, f.Destination, HistoryIdentity(), empty.HasSelectedMedia, "No matching generated video remains."); registry.Insert(emptyChoice);
                Check(!empty.HasSelectedMedia && empty.Files == 0 && empty.RemovedOriginals == 0 && !emptyOptions.DeleteOriginals && !emptyOptions.RecoverTrash && CompletionPresentation.ShowChoice(emptyOptions.AskToEject, true, AppSettings.Load(root).NotificationsEnabled) && fake.Calls == 0 && Directory.Exists(f.Source), "Next filtered import adopted stale seeds or automatically ejected");
                foreach (var batch in ImportHistory.Load(root)) ImportHistory.Verify(batch, HistoryIdentity(), new FakeGuard(), new Operations(), CancellationToken.None, _ => {});
                Check(ImportHistory.Load(root).Count == 3, "Empty import fabricated a verified history batch");
                Check(eject.Execute(emptyChoice.Id).Success && fake.Calls == 1, "Explicit fake eject did not use the current choice"); Throws<SafetyException>(() => eject.Execute(emptyChoice.Id)); Check(fake.Calls == 1 && File.Exists(Path.Combine(f.Source, @"DCIM\PHOTO001.JPG")), "Duplicate choice requested removal again or changed generated photo");
            } });
        }
    }
}
