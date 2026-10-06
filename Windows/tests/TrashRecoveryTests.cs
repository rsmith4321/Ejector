using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        static MediaOptions RecoverOptions(bool videos = true, bool previews = true, bool delete = false, string macUid = null) { return new MediaOptions(videos, previews, true, false, delete, true, macUid); }
        static ImportResult RecoverFixture(Fixture f, MediaOptions options = null, Action<string> report = null, Action<string> audit = null, CancellationToken cancellation = default(CancellationToken), Operations operations = null) {
            return new VerifiedImporter(operations ?? new Operations(), Native.FreeSpace, audit ?? (x => {}), (root, uid, token, validate) => DeviceTrash.Read(root, FixtureTrashSid, uid, token, validate))
                .Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), cancellation, report ?? (x => {}), options ?? RecoverOptions());
        }
        static string TrashSaved(ImportResult result, string originalName) { return Path.Combine(result.Folder, result.Receipt.Files.Single(x => x.FromTrash && !x.IsTrashRecord && x.SourceRelativePath.EndsWith("\\" + originalName, StringComparison.OrdinalIgnoreCase)).RelativePath); }
        static void TrashRecoveryTests() {
            Test("Trash recovery independently clears verified payloads and records while retaining ordinary originals", delegate { using (var f = new Fixture()) {
                f.Put("ordinary.mp4", "Fixture ordinary original"); string payload = PutRecycle(f, "RANDOM", @"E:\DCIM\clip.mp4", "Fixture trashed original"); var result = RecoverFixture(f);
                Check(result.Files == 3 && result.HasSelectedMedia && result.RecoveredTrashFiles == 1 && result.ClearedTrashFiles == 1 && result.ClearedTrashRecords == 1 && result.RemovedOriginals == 0 && result.Receipt.OriginalsKept && result.Receipt.TrashRecoveryRequested && !result.Receipt.OriginalRemovalRequested, "Trash clearing changed ordinary deletion authority/counts");
                Check(!File.Exists(payload) && Directory.GetFiles(Path.GetDirectoryName(payload)).Length == 0 && File.ReadAllText(TrashSaved(result, "clip.mp4")) == "Fixture trashed original" && File.ReadAllText(Path.Combine(f.Source, "ordinary.mp4")) == "Fixture ordinary original", "Recovery failed to save exact original name/content or removed ordinary file");
                Check(result.Receipt.Files.Single(x => x.IsTrashRecord).RelativePath.StartsWith(@".easy-eject-metadata\Recovered Trash Records") && result.Receipt.Files.Single(x => x.IsTrashRecord).RemovedSource && !result.Receipt.Files.Single(x => x.IsTrashRecord).IsMedia, "Record dependency lost, not backed up, or promoted to media");
            } });
            Test("recovery-off never enumerates or clears malformed device Trash", delegate { using (var f = new Fixture()) {
                f.Put("ordinary.mp4", "Fixture ordinary original"); f.Put(Path.Combine("$RECYCLE.BIN", FixtureTrashSid, "$IBROKEN"), "Malformed fixture record");
                int catalogs = 0; var importer = new VerifiedImporter(new Operations(), Native.FreeSpace, x => {}, (root, uid, token, validate) => { catalogs++; throw new SafetyException("Should not read Trash"); });
                var result = importer.Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, true));
                Check(catalogs == 0 && result.Files == 1 && result.ClearedTrashFiles == 0 && !result.Receipt.TrashRecoveryRequested, "Copy-only swept Trash or failed on unrelated records");
            } });
            Test("explicit Mac Trash recovery saves videos and companions while leaving photos unknowns and other owners", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { "video.MOV", "video.SRT", "photo.DNG", "photo.XMP", "live.JPG", "live.MOV", "live.XMP", "unknown.data" }) f.Put(@".Trashes\501\" + name, "Fixture " + name);
                f.Put(@".Trashes\502\other.mp4", "Other owner fixture"); var result = RecoverFixture(f, RecoverOptions(macUid: "501"));
                Check(result.Files == 2 && result.ClearedTrashFiles == 2 && result.ClearedTrashRecords == 0 && result.Receipt.OriginalsKept && File.ReadAllText(TrashSaved(result, "video.MOV")) == "Fixture video.MOV" && File.ReadAllText(TrashSaved(result, "video.SRT")) == "Fixture video.SRT", "Mac video/companion recovery wrong");
                Check(Directory.GetFiles(f.Source, "*", SearchOption.AllDirectories).Length == 7 && File.Exists(Path.Combine(f.Source, @".Trashes\502\other.mp4")), "Photo/unknown/other owner cleared");
            } });
            Test("separate Windows lens records and companions remain associated through clearing", delegate { using (var f = new Fixture()) {
                string[] names = { "VID_20261006_123456_00_001.insv", "VID_20261006_123456_10_001.insv", "LRV_20261006_123456_11_001.lrv" };
                for (int i = 0; i < names.Length; i++) PutRecycle(f, "TOKEN" + i, @"E:\DCIM\" + names[i], "Fixture " + names[i]);
                var result = RecoverFixture(f); Check(result.RecoveredTrashFiles == 3 && result.ClearedTrashFiles == 3 && result.ClearedTrashRecords == 3 && names.All(name => File.ReadAllText(TrashSaved(result, name)) == "Fixture " + name), "Master clearing dropped later lens/preview dependencies");
            } });
            Test("Windows video recovery leaves excluded previews photos unknowns and their records intact", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { "clip.mp4", "clip.xmp", "clip.lrv", "photo.JPG", "unknown.data", "fileinfo_list.list" }) PutRecycle(f, name, @"E:\DCIM\" + name, "Fixture " + name);
                var result = RecoverFixture(f, RecoverOptions(previews: false)); Check(result.RecoveredTrashFiles == 2 && result.ClearedTrashFiles == 2 && result.ClearedTrashRecords == 2 && Directory.GetFiles(f.Source, "*", SearchOption.AllDirectories).Length == 8, "Excluded payload or its restoration record removed");
                Check(result.Receipt.Files.Where(x => x.IsMedia).All(x => !x.SourceRelativePath.EndsWith("fileinfo_list.list")), "Trash index became a selected-media result");
            } });
            Test("all-media recovery copies unfamiliar data without clearing it or its record", delegate { using (var f = new Fixture()) {
                string unknown = PutRecycle(f, "UNKNOWN", @"E:\DCIM\unknown.data", "Fixture unfamiliar bytes"); PutRecycle(f, "PHOTO", @"E:\DCIM\photo.JPG", "Fixture photo bytes");
                var result = RecoverFixture(f, RecoverOptions(videos: false)); Check(result.RecoveredTrashFiles == 2 && result.ClearedTrashFiles == 1 && result.ClearedTrashRecords == 1 && File.ReadAllText(unknown) == "Fixture unfamiliar bytes" && File.ReadAllText(TrashSaved(result, "unknown.data")) == "Fixture unfamiliar bytes" && Directory.GetFiles(Path.GetDirectoryName(unknown)).Length == 2, "Unfamiliar Trash removed or not saved");
            } });
            Test("whole selected ordinary and Trash batches are verified before any clearing", delegate { using (var f = new Fixture()) {
                f.Put("zordinary.mp4", "Fixture ordinary original"); string payload = PutRecycle(f, "TRASH", @"E:\clip.mp4", "Fixture trashed original");
                Throws<IOException>(() => RecoverFixture(f, RecoverOptions(delete: true), x => { if (x == "Copying zordinary.mp4") throw new IOException("Fixture later ordinary copy failed"); }));
                Check(File.Exists(payload) && File.Exists(Path.Combine(f.Source, "zordinary.mp4")) && Directory.GetFiles(Path.GetDirectoryName(payload)).Length == 2, "Trash removed before later ordinary copy verified"); NoRemovalCompletion(f);
            } });
            Test("Trash original-path metadata tampering after catalog capture stops before copy publication", delegate { using (var f = new Fixture()) {
                string payload = PutRecycle(f, "CHANGE", @"E:\clip.mp4", "Fixture original"); string metadata = Path.Combine(Path.GetDirectoryName(payload), "$ICHANGE"); bool changed = false;
                Check(Throws<SafetyException>(() => RecoverFixture(f, null, x => { if (x.StartsWith("Checking Device Trash record")) { File.WriteAllBytes(metadata, RecycleRecord(@"E:\renamed.mp4", new FileInfo(payload).Length)); changed = true; } })).Message.Contains("record changed"), "Changed original-path record trusted");
                Check(changed && File.Exists(payload) && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Catalog tampering copied under a stale name or cleared source");
            } });
            Test("earlier saved Trash metadata tampering blocks all clearing", delegate { using (var f = new Fixture()) {
                string payload = PutRecycle(f, "RECORD", @"E:\clip.mp4", "Fixture original"); bool changed = false;
                Throws<SafetyException>(() => RecoverFixture(f, null, x => { if (x.StartsWith("Copying Device Trash\\")) { string saved = Directory.GetFiles(f.Destination, "$IRECORD", SearchOption.AllDirectories).Single(); File.AppendAllText(saved, "Tampered fixture record"); changed = true; } }));
                Check(changed && File.Exists(payload) && Directory.GetFiles(Path.GetDirectoryName(payload)).Length == 2, "Saved record dependency tamper allowed clearing"); NoRemovalCompletion(f);
            } });
            Test("held Trash payloads records and saved copies deny mutation during disposition", delegate { using (var f = new Fixture()) {
                string payload = PutRecycle(f, "PROTECTED", @"E:\clip.mp4", "Fixture original"); string metadata = Path.Combine(Path.GetDirectoryName(payload), "$IPROTECTED"); int checks = 0;
                var result = RecoverFixture(f, null, x => { if (x.StartsWith("Clearing verified Trash file")) { Throws<IOException>(() => File.WriteAllText(payload, "Changed")); Throws<IOException>(() => File.WriteAllText(metadata, "Changed")); Throws<IOException>(() => File.WriteAllText(Directory.GetFiles(f.Destination, "clip.mp4", SearchOption.AllDirectories).Single(), "Changed")); checks++; } });
                Check(checks == 1 && result.ClearedTrashFiles == 1 && result.ClearedTrashRecords == 1, "Dependency handles released before disposition");
            } });
            Test("record removal follows its payload and a cancelled partial clear reports exact counts", delegate { using (var f = new Fixture()) using (var cancellation = new CancellationTokenSource()) {
                string payload = PutRecycle(f, "ORDER", @"E:\clip.mp4", "Fixture original"); string metadata = Path.Combine(Path.GetDirectoryName(payload), "$IORDER"); var operations = new Operations();
                var error = Throws<OriginalRemovalFailure>(() => RecoverFixture(f, null, null, x => { if (x.StartsWith("Removed verified original") && x.EndsWith(payload)) cancellation.Cancel(); }, cancellation.Token, operations));
                Check(error.RemovedOriginals == 0 && error.ClearedTrashFiles == 1 && error.ClearedTrashRecords == 0 && !File.Exists(payload) && File.Exists(metadata) && error.Message.Contains("1 verified Trash files"), "Record deleted first or partial clearing counted as ordinary original deletion");
                NoRemovalCompletion(f); using (operations.Reserve(new[] { 700, 701 })) {}
            } });
            Test("new selected Trash entry stops clearing and preserves both new and original records", delegate { using (var f = new Fixture()) {
                string payload = PutRecycle(f, "FIRST", @"E:\clip.mp4", "Fixture original"); bool added = false;
                Throws<SafetyException>(() => RecoverFixture(f, null, x => { if (x.StartsWith("Clearing verified Trash file")) { PutRecycle(f, "NEW", @"E:\new.mp4", "Fixture new Trash recording"); added = true; } }));
                Check(added && File.Exists(payload) && Directory.GetFiles(Path.GetDirectoryName(payload)).Length == 4, "New selected Trash recording ignored or original cleared"); NoRemovalCompletion(f);
            } });
            Test("new unrelated Trash photo does not broaden video clearing", delegate { using (var f = new Fixture()) {
                PutRecycle(f, "FIRST", @"E:\clip.mp4", "Fixture original"); string photo = null;
                var result = RecoverFixture(f, null, x => { if (x.StartsWith("Clearing verified Trash file")) photo = PutRecycle(f, "PHOTO", @"E:\photo.JPG", "Fixture new photo"); });
                Check(result.ClearedTrashFiles == 1 && File.ReadAllText(photo) == "Fixture new photo" && Directory.GetFiles(Path.GetDirectoryName(photo)).Length == 2, "Unrelated Trash photo blocked video recovery or was removed");
            } });
            Test("new replacement at a cleared Trash path is preserved and stops record deletion", delegate { using (var f = new Fixture()) {
                string payload = PutRecycle(f, "REPLACE", @"E:\clip.mp4", "Fixture original"); string metadata = Path.Combine(Path.GetDirectoryName(payload), "$IREPLACE");
                var error = Throws<OriginalRemovalFailure>(() => RecoverFixture(f, null, null, x => { if (x.StartsWith("Removed verified original") && x.EndsWith(payload)) File.WriteAllText(payload, "Fixture replacement"); }));
                Check(error.ClearedTrashFiles == 1 && error.ClearedTrashRecords == 0 && File.ReadAllText(payload) == "Fixture replacement" && File.Exists(metadata), "Replacement or its restoration record removed"); NoRemovalCompletion(f);
            } });
            Test("recovered original history excludes Windows record metadata and survives source absence", delegate { using (var f = new Fixture()) {
                PutRecycle(f, "HISTORY", @"E:\DCIM\VID_20261006_123456_00_001.mp4", "Fixture original bytes"); var result = RecoverFixture(f); var batch = Batch(f, result);
                Check(batch.Originals.Count == 1 && batch.Originals[0].Path.EndsWith("VID_20261006_123456_00_001.mp4") && Directory.GetFiles(f.Source, "*", SearchOption.AllDirectories).Length == 0, "Recovery metadata entered Quick Share history or source still required");
                ImportHistory.Verify(batch, HistoryIdentity(), new FakeGuard(), new Operations(), CancellationToken.None, x => {});
            } });
            Test("direct ordinary-source selection of protected Trash cannot bypass recovery ownership", delegate { using (var f = new Fixture()) {
                f.Put(@".Trashes\502\other.mp4", "Other-owner fixture"); string selected = Path.Combine(f.Source, @".Trashes\502");
                Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, selected, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(false, true, true, false, true)));
                Check(File.Exists(Path.Combine(selected, "other.mp4")) && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Ordinary import bypassed owner-aware recovery");
            } });
            Test("deleted Windows camera package saves every dependency and retains support and folder record", delegate { using (var f = new Fixture()) {
                string owner = Path.Combine(f.Source, "$RECYCLE.BIN", FixtureTrashSid), payload = Path.Combine(owner, "$RPACKAGE"); Directory.CreateDirectory(payload);
                foreach (string name in new[] { @"M4ROOT\CLIP\clip.MXF", @"M4ROOT\CLIP\clip.XML", @"M4ROOT\AUDIO\audio.WAV", @"M4ROOT\THMBNL\clip.JPG" }) { string path = Path.Combine(payload, name); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "Fixture " + name); }
                string metadata = Path.Combine(owner, "$IPACKAGE"); File.WriteAllBytes(metadata, RecycleRecord(@"E:\PRIVATE", 0));
                var result = RecoverFixture(f, RecoverOptions(previews: false));
                Check(result.RecoveredTrashFiles == 4 && result.ClearedTrashFiles == 2 && result.ClearedTrashRecords == 0 && File.Exists(metadata) && File.Exists(Path.Combine(payload, @"M4ROOT\AUDIO\audio.WAV")) && File.Exists(Path.Combine(payload, @"M4ROOT\THMBNL\clip.JPG")) && result.Receipt.Files.All(x => File.Exists(Path.Combine(result.Folder, x.RelativePath))), "Package support or restoration record removed before retained contents");
            } });
            Test("all-media Trash unknowns reuse the same saved paths after recognized originals are cleared", delegate { using (var f = new Fixture()) {
                PutRecycle(f, "UNKNOWN", @"E:\DCIM\unknown.data", "Fixture unknown"); PutRecycle(f, "PHOTO", @"E:\DCIM\photo.JPG", "Fixture photo");
                var first = RecoverFixture(f, RecoverOptions(videos: false)); var second = RecoverFixture(f, RecoverOptions(videos: false));
                Check(first.ClearedTrashFiles == 1 && second.ClearedTrashFiles == 0 && second.Reused == 2 && TrashSaved(first, "unknown.data") == TrashSaved(second, "unknown.data") && Directory.GetFiles(f.Destination, "unknown.data", SearchOption.AllDirectories).Length == 1, "Retained unknown lost verified reuse as media selection shrank");
            } });
            Test("recovered video conflicts preserve the whole Trash group and never overwrite prior originals", delegate { using (var f = new Fixture()) {
                PutRecycle(f, "CLIP", @"E:\DCIM\clip.mp4", "FixtureA"); PutRecycle(f, "SIDECAR", @"E:\DCIM\clip.xmp", "SidecarA"); var first = RecoverFixture(f);
                PutRecycle(f, "CLIP", @"E:\DCIM\clip.mp4", "FixtureB"); PutRecycle(f, "SIDECAR", @"E:\DCIM\clip.xmp", "SidecarB"); var second = RecoverFixture(f);
                Check(File.ReadAllText(TrashSaved(first, "clip.mp4")) == "FixtureA" && File.ReadAllText(TrashSaved(second, "clip.mp4")) == "FixtureB" && Path.GetDirectoryName(TrashSaved(second, "clip.mp4")) == Path.GetDirectoryName(TrashSaved(second, "clip.xmp")) && TrashSaved(second, "clip.mp4").Contains("Additional media"), "Trash group renamed individually or prior saved originals overwritten");
            } });
            Test("read-only restoration record refuses the complete clear phase before any payload removal", delegate { using (var f = new Fixture()) {
                string payload = PutRecycle(f, "READONLY", @"E:\clip.mp4", "Fixture original"), metadata = Path.Combine(Path.GetDirectoryName(payload), "$IREADONLY"); var attributes = File.GetAttributes(metadata); File.SetAttributes(metadata, attributes | FileAttributes.ReadOnly);
                try { Throws<SafetyException>(() => RecoverFixture(f)); Check(File.Exists(payload) && (File.GetAttributes(metadata) & FileAttributes.ReadOnly) != 0, "Payload removed before protected record preflight or protection changed"); NoRemovalCompletion(f); }
                finally { File.SetAttributes(metadata, attributes); }
            } });
            Test("recovery and ordinary deletion counts stay distinct when both options are enabled", delegate { using (var f = new Fixture()) {
                f.Put("ordinary.mp4", "Fixture original"); PutRecycle(f, "TRASH", @"E:\trash.mp4", "Fixture trash"); var result = RecoverFixture(f, RecoverOptions(delete: true));
                Check(result.RemovedOriginals == 1 && result.ClearedTrashFiles == 1 && result.ClearedTrashRecords == 1 && !result.Receipt.OriginalsKept && result.Receipt.Files.Count(x => x.RemovedSource) == 3 && !File.Exists(Path.Combine(f.Source, "ordinary.mp4")), "Original/Trash/record counts conflated");
            } });
            Test("recovery defaults migrate off and custom owner choices persist independently of shared defaults", delegate { using (var f = new Fixture()) {
                var defaults = new ImportDefaults(); Check(!defaults.Snapshot().RecoverTrash && !new ImportProfile().Resolve(new ImportDefaults { RecoverTrash = true }).RecoverTrash, "Legacy recovery enabled");
                defaults.RecoverTrash = true; var state = State(); var profile = defaults.NewProfile(state.Volumes[0], state.Disks[0]); profile.MacTrashUid = "501"; profile.RelativeSource = "DCIM"; profile.Destination = f.Destination;
                var frozen = profile.Resolve(defaults); defaults.SetVideosOnly(false); Check(frozen.RecoverTrash && frozen.MacTrashUid == "501" && !profile.Resolve(defaults).RecoverTrash && profile.Resolve(defaults).MacTrashUid == "501", "Broadening changed a running operation or erased explicit owner");
                defaults = new ImportDefaults { RecoverTrash = true, DeleteOriginals = true }; profile.SetCustom(true, defaults); profile.SetVideosOnly(false); Check(!profile.Resolve(defaults).RecoverTrash && !profile.Resolve(defaults).DeleteOriginals && defaults.Snapshot().RecoverTrash, "Custom broadening changed shared recovery or retained clearing");
                profile.RecoverTrash = true; Profiles.Save(profile, f.Root); var restored = Profiles.Load(state.Volumes[0], state.Disks[0], f.Root); Check(restored.Resolve(new ImportDefaults()).RecoverTrash && restored.MacTrashUid == "501", "Recovery/owner profile roundtrip lost explicit choice");
                ImportDefaults.Save(defaults, f.Root); Check(ImportDefaults.Load(f.Root).Snapshot().RecoverTrash && ImportDefaults.Load(f.Root).Snapshot().MacTrashUid == null, "Per-device owner leaked into global defaults");
            } });
            Test("recovery enabling acknowledgment and photo broadening reset remain independent of original deletion", delegate {
                using (var dialog = new ImportDefaultsDialog(new ImportDefaults())) {
                    int prompts = 0; dialog.Selection.ConfirmRecovery = () => { prompts++; return false; }; dialog.Selection.RecoverTrash.Checked = true; Check(prompts == 1 && !dialog.Value.Snapshot().RecoverTrash, "Declined recovery acknowledgment enabled clearing");
                    dialog.Selection.ConfirmRecovery = () => true; dialog.Selection.RecoverTrash.Checked = true; Check(dialog.Value.Snapshot().RecoverTrash && !dialog.Value.Snapshot().DeleteOriginals, "Recovery silently enabled ordinary deletion");
                    dialog.Selection.Videos.Checked = false; Check(!dialog.Value.Snapshot().RecoverTrash && !dialog.Value.Snapshot().DeleteOriginals, "Photo broadening retained removal choices");
                }
            });
            Test("profile owner picker restricts numeric folders to the original device and remains a draft", delegate {
                var state = State(); var defaults = new ImportDefaults { RecoverTrash = true }; var profile = defaults.NewProfile(state.Volumes[0], state.Disks[0]);
                using (var dialog = new ImportProfileDialog(profile, @"E:\", "Fixture card", defaults, true)) {
                    Check(dialog.Selection.RecoverTrash.Checked && !dialog.Selection.Enabled && dialog.MacTrashOwner.Text.Contains("no owner selected"), "Inherited recovery hidden or Mac owner guessed");
                    Check(dialog.ValidateMacOwner(@"E:\.Trashes\501") == "501", "Explicit owner refused");
                    foreach (string path in new[] { @"D:\.Trashes\501", @"E:\.Trashes\501\nested", @"E:\.Trashes\not-a-uid", @"E:\DCIM\501" }) Throws<SafetyException>(() => dialog.ValidateMacOwner(path));
                    dialog.Draft.MacTrashUid = "501"; dialog.UpdateOwner(); Check(dialog.MacTrashOwner.Text.Contains(".Trashes\\501") && profile.MacTrashUid == null && defaults.Snapshot().MacTrashUid == null, "Owner editing changed saved/shared settings");
                }
            });
            Test("recovery enabling and manual review keep cancel as default and disclose owner scope", delegate {
                using (var dialog = new DeviceTrashRecoveryConfirmationDialog("Every device using defaults, including future profiles.")) { Check(dialog.AcceptButton == dialog.Cancel && dialog.CancelButton == dialog.Cancel && dialog.EnableRecovery.DialogResult == DialogResult.Yes && dialog.Explanation.Text.Contains("Mac owners are never guessed"), "Recovery default/scope acknowledgment unsafe"); }
                var options = RecoverOptions(macUid: "501"); Check(DeviceTrashChoice.BeforeImport(options).Contains(".Trashes\\501") && DeviceTrashChoice.BeforeImport(RecoverOptions()).Contains("Mac Trash is left") && DeviceTrashChoice.Summary(2, 1, 1).Contains("permanently cleared 1") && CompletionPresentation.Notification(true, 0, 1).Contains("Cleared 1 verified Trash"), "Manual/completion text hid scope or counts");
            });
        }
    }
}
