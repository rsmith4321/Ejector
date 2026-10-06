using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        static MediaOptions RemoveOptions(bool videosOnly = true, bool previews = true) { return new MediaOptions(videosOnly, previews, true, false, true); }
        static ImportResult RemoveFixture(Fixture f, MediaOptions options = null, Action<string> report = null) {
            return new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, report ?? (x => {}), options ?? RemoveOptions());
        }
        static void NoRemovalCompletion(Fixture f) { Check(Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0, "Failure published a receipt or retained private work"); }
        static void RemovalTests() {
            Test("removal starts off in factory, legacy and explicit copy-only options", delegate { using (var f = new Fixture()) {
                Check(!new ImportDefaults().Snapshot().DeleteOriginals && !new ImportProfile().Resolve(new ImportDefaults { DeleteOriginals = true }).DeleteOriginals && !new MediaOptions(false, true).DeleteOriginals, "Migration enabled removal");
                f.Put("clip.mp4", "Fixture original"); var result = f.Run(x => {});
                Check(result.RemovedOriginals == 0 && result.Receipt.OriginalsKept && !result.Receipt.OriginalRemovalRequested && result.Receipt.Files.All(e => !e.RemovedSource) && File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == "Fixture original", "Copy-only receipt or originals incorrect");
            } });
            Test("video removal preserves photo pairs, ambiguous sidecars, unknown files and indexes", delegate { using (var f = new Fixture()) {
                string[] keep = { "photo.JPG", "photo.MOV", "clip.xmp", "clip.bin", "orphan.LRV", "unknown.raw", "fileinfo_list.list" };
                foreach (string name in keep.Concat(new[] { "clip.mp4", "clip.mp4.xmp" })) f.Put(name, "Fixture " + name);
                var result = RemoveFixture(f); Check(result.Files == 3 && result.RemovedOriginals == 2 && !result.Receipt.OriginalsKept && result.Receipt.OriginalRemovalRequested, "Wrong selected/removal count");
                foreach (string name in keep) Check(File.ReadAllText(Path.Combine(f.Source, name)) == "Fixture " + name, "Excluded original lost: " + name);
                foreach (string name in new[] { "clip.mp4", "clip.mp4.xmp" }) Check(!File.Exists(Path.Combine(f.Source, name)) && File.ReadAllText(Saved(result, name)) == "Fixture " + name && result.Receipt.Files.Single(e => e.SourceRelativePath == name).RemovedSource, "Recognized original not saved/removed: " + name);
                Check(!result.Receipt.Files.Single(e => !e.IsMedia).RemovedSource, "Device index removed");
            } });
            Test("all-media removal removes recognized photos audio and companions while keeping unknowns", delegate { using (var f = new Fixture()) {
                string[] recognized = { "clip.mp4", "clip.xmp", "photo.JPG", "photo.xml", "audio.wav", "audio.xmp" };
                string[] retained = { "unknown.bin", "unassociated.xml", "orphan.lrv", "fileinfo_list.list" };
                foreach (string name in recognized.Concat(retained)) f.Put(name, "Fixture " + name);
                var result = RemoveFixture(f, RemoveOptions(false)); Check(result.Files == 10 && result.RemovedOriginals == 6, "All-media removal guessed unknown eligibility");
                foreach (string name in recognized) Check(!File.Exists(Path.Combine(f.Source, name)) && File.ReadAllText(Saved(result, name)) == "Fixture " + name, "Recognized saved original missing");
                foreach (string name in retained) Check(File.Exists(Path.Combine(f.Source, name)), "Unrecognized/index removed");
            } });
            Test("lens pairs and associated previews remove together with unchanged saved filenames", delegate { using (var f = new Fixture()) {
                string[] names = { "VID_20261006_123456_00_001.insv", "VID_20261006_123456_10_001.insv", "LRV_20261006_123456_01_001.lrv" };
                foreach (string name in names) f.Put(name, "Fixture " + name);
                var result = RemoveFixture(f); Check(result.Files == 3 && result.RemovedOriginals == 3 && names.All(name => !File.Exists(Path.Combine(f.Source, name)) && File.ReadAllText(Saved(result, name)) == "Fixture " + name), "Lens/companion association changed as earlier master disappeared");
            } });
            Test("disabled previews remain while selected videos and sidecars are removed", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { "clip.mp4", "clip.xmp", "clip.lrv", "clip.thm", @"PROXY\clip.mp4" }) f.Put(name, "Fixture " + name);
                var result = RemoveFixture(f, RemoveOptions(true, false)); Check(result.Files == 2 && result.RemovedOriginals == 2 && new[] { "clip.lrv", "clip.thm", @"PROXY\clip.mp4" }.All(name => File.Exists(Path.Combine(f.Source, name))), "Skipped previews removed or saved");
            } });
            Test("video package removal keeps verified audio metadata and required thumbnails on source", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { @"PRIVATE\M4ROOT\CLIP\clip.MXF", @"PRIVATE\M4ROOT\CLIP\clip.XML", @"PRIVATE\M4ROOT\AUDIO\audio.WAV", @"PRIVATE\M4ROOT\THMBNL\clip.JPG", "fileinfo_list.list" }) f.Put(name, "Fixture " + name);
                var result = RemoveFixture(f, RemoveOptions(true, false)); Check(result.Files == 5 && result.RemovedOriginals == 2 && File.Exists(Path.Combine(f.Source, @"PRIVATE\M4ROOT\AUDIO\audio.WAV")) && File.Exists(Path.Combine(f.Source, @"PRIVATE\M4ROOT\THMBNL\clip.JPG")) && result.Receipt.Files.All(e => File.Exists(Path.Combine(result.Folder, e.RelativePath))), "Package dependencies removed or not verified");
            } });
            Test("unrecognized structured video refusal removes nothing", delegate { using (var f = new Fixture()) {
                f.Put("clip.mxf", "Fixture recording"); Check(Throws<SafetyException>(() => RemoveFixture(f)).Message.Contains("complete camera package"), "Wrong package refusal"); Check(File.Exists(Path.Combine(f.Source, "clip.mxf")) && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Partial package removed/imported");
            } });
            Test("complete selected batch copy failure occurs before any original removal", delegate { using (var f = new Fixture()) {
                f.Put("a.mp4", "Fixture first"); f.Put("b.mp4", "Fixture second"); Throws<IOException>(() => RemoveFixture(f, null, x => { if (x == "Copying b.mp4") throw new IOException("Fixture copy failure"); }));
                Check(Directory.GetFiles(f.Source).Length == 2 && Directory.GetFiles(f.Destination, "a.mp4", SearchOption.AllDirectories).Length == 1, "Failure removed an earlier original"); NoRemovalCompletion(f);
            } });
            Test("saved dependency tampering blocks all original removal", delegate { using (var f = new Fixture()) {
                f.Put("a.mp4", "Fixture first"); f.Put("b.mp4", "Fixture second"); bool changed = false;
                Throws<SafetyException>(() => RemoveFixture(f, null, x => { if (x == "Copying b.mp4") { File.WriteAllText(Directory.GetFiles(f.Destination, "a.mp4", SearchOption.AllDirectories).Single(), "Tampered fixture"); changed = true; } }));
                Check(changed && Directory.GetFiles(f.Source).Length == 2, "Tampered saved dependency permitted removal"); NoRemovalCompletion(f);
            } });
            Test("retained package dependency tampering blocks video removal", delegate { using (var f = new Fixture()) {
                f.Put(@"PRIVATE\M4ROOT\AUDIO\a.WAV", "Fixture retained audio"); f.Put(@"PRIVATE\M4ROOT\CLIP\z.MXF", "Fixture video"); bool changed = false;
                Throws<SafetyException>(() => RemoveFixture(f, null, x => { if (x.StartsWith("Copying") && x.EndsWith("z.MXF")) { File.WriteAllText(Path.Combine(f.Source, @"PRIVATE\M4ROOT\AUDIO\a.WAV"), "Changed fixture audio"); changed = true; } }));
                Check(changed && File.Exists(Path.Combine(f.Source, @"PRIVATE\M4ROOT\CLIP\z.MXF")), "Retained source dependency change allowed removal"); NoRemovalCompletion(f);
            } });
            Test("same-size source tampering with restored time fails full-batch hash verification before removal", delegate { using (var f = new Fixture()) {
                f.Put("a.mp4", "first"); f.Put("b.mp4", "second"); string original = Path.Combine(f.Source, "a.mp4"); DateTime time = File.GetLastWriteTimeUtc(original); bool changed = false;
                Throws<SafetyException>(() => RemoveFixture(f, null, x => { if (x == "Copying b.mp4") { File.WriteAllText(original, "FIRST"); File.SetLastWriteTimeUtc(original, time); changed = true; } }));
                Check(changed && Directory.GetFiles(f.Source).Length == 2, "Restored source stamp bypassed hash check"); NoRemovalCompletion(f);
            } });
            Test("verified existing copies are reused and flushed before removing originals", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "Fixture original"); var saved = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, true, true));
                var removed = RemoveFixture(f); Check(removed.Reused == 1 && removed.RemovedOriginals == 1 && Saved(saved, "clip.mp4") == Saved(removed, "clip.mp4") && File.ReadAllText(Saved(removed, "clip.mp4")) == "Fixture original", "Removal made unverified duplicate or changed saved original");
            } });
            Test("metadata-only import with removal enabled still retains the device index", delegate { using (var f = new Fixture()) {
                f.Put("fileinfo_list.list", "Fixture device index"); f.Put("photo.JPG", "Fixture photo"); var result = RemoveFixture(f);
                Check(!result.HasSelectedMedia && result.Files == 1 && result.RemovedOriginals == 0 && result.Receipt.OriginalsKept && !result.Receipt.Files[0].RemovedSource && Directory.GetFiles(f.Source).Length == 2, "Metadata-only result authorized original deletion");
            } });
            Test("audit durability failure before removal leaves all originals", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "Fixture original"); var importer = new VerifiedImporter(new Operations(), Native.FreeSpace, x => { if (x.StartsWith("Before permanent removal")) throw new IOException("Fixture durable audit failure"); });
                Throws<IOException>(() => importer.Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, x => {}, RemoveOptions()));
                Check(File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == "Fixture original", "Removal preceded durable intent audit"); NoRemovalCompletion(f);
            } });
            Test("held sources saved dependencies and their parents deny modification during removal", delegate { using (var f = new Fixture()) {
                f.Put(@"DCIM\a.mp4", "Fixture first"); f.Put(@"DCIM\b.mp4", "Fixture second"); int protectedChecks = 0;
                var result = RemoveFixture(f, null, x => { if (x.StartsWith("Removing")) {
                    string saved = Directory.GetFiles(f.Destination, "b.mp4", SearchOption.AllDirectories).Single();
                    Throws<IOException>(() => File.WriteAllText(Path.Combine(f.Source, @"DCIM\b.mp4"), "Changed")); Throws<IOException>(() => File.WriteAllText(saved, "Changed"));
                    Throws<IOException>(() => File.Move(saved, saved + ".renamed")); Throws<IOException>(() => Directory.Move(Path.Combine(f.Source, "DCIM"), Path.Combine(f.Source, "moved"))); protectedChecks++;
                } });
                Check(protectedChecks == 2 && result.RemovedOriginals == 2 && File.ReadAllText(Saved(result, @"DCIM\b.mp4")) == "Fixture second", "Dependencies released before final removal");
            } });
            Test("source sibling and destination reservations stay held during original disposition", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "Fixture original"); var operations = new Operations(); int checkedCount = 0;
                var result = new VerifiedImporter(operations).Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, x => { if (x.StartsWith("Removing")) { Throws<SafetyException>(() => operations.Reserve(new[] { 700 })); Throws<SafetyException>(() => operations.Reserve(new[] { 701 })); checkedCount++; } }, RemoveOptions());
                Check(checkedCount == 1 && result.RemovedOriginals == 1, "Removal happened outside import reservation"); using (operations.Reserve(new[] { 700, 701 })) {}
            } });
            Test("read-only candidate refuses the entire removal phase without clearing protection", delegate { using (var f = new Fixture()) {
                f.Put("a.mp4", "Fixture first"); f.Put("z.mp4", "Fixture protected"); string file = Path.Combine(f.Source, "z.mp4"); var originalAttributes = File.GetAttributes(file); File.SetAttributes(file, originalAttributes | FileAttributes.ReadOnly);
                try { Throws<SafetyException>(() => RemoveFixture(f)); Check(File.Exists(Path.Combine(f.Source, "a.mp4")) && (File.GetAttributes(file) & FileAttributes.ReadOnly) != 0, "Earlier original removed or protection changed"); NoRemovalCompletion(f); }
                finally { File.SetAttributes(file, originalAttributes); }
            } });
            Test("hard-linked candidate stops before any removal and retains both aliases", delegate { using (var f = new Fixture()) {
                f.Put("a.mp4", "Fixture first"); f.Put("z.mp4", "Fixture linked"); string alias = Path.Combine(f.Root, "alias.bin"); Check(CreateHardLink(alias, Path.Combine(f.Source, "z.mp4"), IntPtr.Zero), "Fixture hardlink failed");
                Throws<SafetyException>(() => RemoveFixture(f)); Check(Directory.GetFiles(f.Source).Length == 2 && File.ReadAllText(alias) == "Fixture linked", "Hard-linked original or earlier original removed"); NoRemovalCompletion(f);
            } });
            Test("new selected recording before first disposition stops with all originals", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "Fixture original"); bool arrived = false;
                Throws<SafetyException>(() => RemoveFixture(f, null, x => { if (x.StartsWith("Removing")) { f.Put("new.mp4", "Fixture new recording"); arrived = true; } }));
                Check(arrived && Directory.GetFiles(f.Source).Length == 2, "New recording was ignored or original removed"); NoRemovalCompletion(f);
            } });
            Test("unselected photo changes do not broaden video removal", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "Fixture original"); f.Put("photo.JPG", "Fixture photo"); var result = RemoveFixture(f, null, x => { if (x.StartsWith("Removing")) File.WriteAllText(Path.Combine(f.Source, "photo.JPG"), "Changed fixture photo"); });
                Check(result.RemovedOriginals == 1 && File.ReadAllText(Path.Combine(f.Source, "photo.JPG")) == "Changed fixture photo", "Unselected photo was held/removed or operation scope broadened");
            } });
            Test("partial removal cancellation reports exact count audit and releases reservations", delegate { using (var f = new Fixture()) using (var cancellation = new CancellationTokenSource()) {
                f.Put("a.mp4", "Fixture first"); f.Put("b.mp4", "Fixture second"); var operations = new Operations();
                var importer = new VerifiedImporter(operations, Native.FreeSpace, x => { if (x.StartsWith("Removed verified original")) cancellation.Cancel(); });
                var error = Throws<OriginalRemovalFailure>(() => importer.Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), cancellation.Token, x => {}, RemoveOptions()));
                Check(error.Removed == 1 && error.InnerException is OperationCanceledException && !File.Exists(Path.Combine(f.Source, "a.mp4")) && File.Exists(Path.Combine(f.Source, "b.mp4")) && Directory.GetFiles(f.Destination, "*.mp4", SearchOption.AllDirectories).Length == 2 && File.ReadAllText(error.AuditPath).Contains("Stopped; 1 verified originals removed") && error.Message.Contains(error.Folder), "Partial cancellation hid removals/copies/audit");
                NoRemovalCompletion(f); using (operations.Reserve(new[] { 700, 701 })) {}
            } });
            Test("disconnection after first removal reports partial progress without granting completion", delegate { using (var f = new Fixture()) {
                f.Put("a.mp4", "Fixture first"); f.Put("b.mp4", "Fixture second"); var guard = new FakeGuard();
                var importer = new VerifiedImporter(new Operations(), Native.FreeSpace, x => { if (x.StartsWith("Removed verified original")) guard.FailAt = guard.Calls + 1; });
                var error = Throws<OriginalRemovalFailure>(() => importer.Run(f.Source, f.Source, f.Destination, "fixture-volume", guard, CancellationToken.None, x => {}, RemoveOptions()));
                Check(error.Removed == 1 && error.InnerException is SafetyException && File.Exists(Path.Combine(f.Source, "b.mp4")), "Disconnection hid partial removal"); NoRemovalCompletion(f);
            } });
            Test("post-removal audit failure retains saved copies and retry reuses only remaining originals", delegate { using (var f = new Fixture()) {
                f.Put("a.mp4", "Fixture first"); f.Put("b.mp4", "Fixture second"); var operations = new Operations();
                var importer = new VerifiedImporter(operations, Native.FreeSpace, x => { if (x.StartsWith("Removed verified original")) throw new IOException("Fixture post-removal audit failure"); });
                var error = Throws<OriginalRemovalFailure>(() => importer.Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, x => {}, RemoveOptions()));
                Check(error.Removed == 1 && File.ReadAllText(error.AuditPath).Contains("Before permanent removal"), "No durable intent for partial removal"); NoRemovalCompletion(f);
                var retry = RemoveFixture(f); Check(retry.Files == 1 && retry.Reused == 1 && retry.RemovedOriginals == 1 && Directory.GetFiles(f.Source).Length == 0 && Directory.GetFiles(f.Destination, "a.mp4", SearchOption.AllDirectories).Length == 1, "Retry scanned old copies or damaged prior saved recording");
            } });
            Test("replacement at a removed path is kept and stops subsequent removal", delegate { using (var f = new Fixture()) {
                f.Put("a.mp4", "Fixture first"); f.Put("b.mp4", "Fixture second");
                var importer = new VerifiedImporter(new Operations(), Native.FreeSpace, x => { if (x.StartsWith("Removed verified original")) f.Put("a.mp4", "Fixture replacement"); });
                var error = Throws<OriginalRemovalFailure>(() => importer.Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, x => {}, RemoveOptions()));
                Check(error.Removed == 1 && File.ReadAllText(Path.Combine(f.Source, "a.mp4")) == "Fixture replacement" && File.Exists(Path.Combine(f.Source, "b.mp4")), "Replacement mistaken for previously verified original"); NoRemovalCompletion(f);
            } });
            Test("late receipt publication failure reports all removals and preserves the conflicting file", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "Fixture original"); string conflict = null;
                var error = Throws<OriginalRemovalFailure>(() => RemoveFixture(f, null, x => { if (x.StartsWith("Publishing verified")) { conflict = Path.Combine(Path.GetDirectoryName(Directory.GetFiles(f.Destination, "import-audit.log", SearchOption.AllDirectories).Single()), "verified-import.json"); File.WriteAllText(conflict, "Fixture existing record"); } }));
                Check(error.Removed == 1 && File.ReadAllText(conflict) == "Fixture existing record" && File.ReadAllText(Directory.GetFiles(f.Destination, "clip.mp4", SearchOption.AllDirectories).Single()) == "Fixture original" && Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0, "Receipt conflict overwritten or partial removal hidden");
            } });
            Test("history with removed source originals survives restart and validates full-quality saved copies", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "Fixture full-quality original"); var result = RemoveFixture(f); var batch = Batch(f, result); string history = Path.Combine(f.Root, "history"); ImportHistory.Add(batch, history);
                Check(!result.Receipt.OriginalsKept && result.Receipt.Files[0].RemovedSource && Directory.GetFiles(f.Source).Length == 0, "Removal receipt inaccurate");
                ImportHistory.Verify(ImportHistory.Load(history).Single(), HistoryIdentity(), new FakeGuard(), new Operations(), CancellationToken.None, x => {});
                Check(File.ReadAllText(batch.Originals.Single().Path) == "Fixture full-quality original", "History lost saved original after source removal");
            } });
            Test("normal import excludes hidden device Trash metadata without sweeping originals", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "Fixture recording"); foreach (string name in new[] { @".Trashes\501\trash.mp4", @"$RECYCLE.BIN\fixture-owner\trash.mp4", ".hidden.mp4", ".DS_Store" }) f.Put(name, "Fixture hidden original");
                var result = RemoveFixture(f, RemoveOptions(false)); Check(result.Files == 1 && result.RemovedOriginals == 1 && Directory.GetFiles(f.Source, "*", SearchOption.AllDirectories).Length == 4, "Ordinary import swept Trash/hidden originals");
            } });
            Test("removal defaults inheritance freezes options and broadening clears factory video deletion", delegate { using (var f = new Fixture()) {
                var defaults = new ImportDefaults { DeleteOriginals = true }; var state = State(); var profile = defaults.NewProfile(state.Volumes[0], state.Disks[0]); var frozen = profile.Resolve(defaults);
                defaults.SetVideosOnly(false); Check(frozen.DeleteOriginals && frozen.VideosOnly && !defaults.Snapshot().DeleteOriginals && !profile.Resolve(defaults).DeleteOriginals, "Broadening defaults silently deleted photos or changed a running import");
                profile.SetCustom(true, new ImportDefaults { DeleteOriginals = true }); profile.SetVideosOnly(false); Check(!profile.Resolve(defaults).DeleteOriginals, "Custom photo broadening inherited video removal");
                defaults = new ImportDefaults { DeleteOriginals = true }; ImportDefaults.Save(defaults, f.Root); Check(ImportDefaults.Load(f.Root).Snapshot().DeleteOriginals, "Removal choice persistence failed");
                profile = defaults.NewProfile(state.Volumes[0], state.Disks[0]); profile.SetCustom(true, defaults); profile.RelativeSource = "DCIM"; profile.Destination = f.Destination;
                Profiles.Save(profile, f.Root); Check(Profiles.Load(state.Volumes[0], state.Disks[0], f.Root).Resolve(new ImportDefaults()).DeleteOriginals, "Saved custom removal choice did not roundtrip");
            } });
            Test("shared and profile removal enabling require acknowledgment and broadening resets the draft", delegate {
                using (var dialog = new ImportDefaultsDialog(new ImportDefaults())) {
                    int confirmations = 0; dialog.Selection.ConfirmRemoval = () => { confirmations++; return false; }; dialog.Selection.DeleteOriginals.Checked = true;
                    Check(confirmations == 1 && !dialog.Value.Snapshot().DeleteOriginals && dialog.Selection.RemovalScope.Contains("future profiles"), "Declined shared acknowledgment enabled removal");
                    dialog.Selection.ConfirmRemoval = () => true; dialog.Selection.DeleteOriginals.Checked = true; Check(dialog.Value.Snapshot().DeleteOriginals, "Acknowledged choice lost"); dialog.Selection.Videos.Checked = false; Check(!dialog.Value.Snapshot().DeleteOriginals, "UI broadened removal to photos");
                }
                var state = State(); var defaults = new ImportDefaults { DeleteOriginals = true }; var profile = defaults.NewProfile(state.Volumes[0], state.Disks[0]);
                using (var dialog = new ImportProfileDialog(profile, @"E:\", "Fixture card", defaults, true)) {
                    Check(dialog.Selection.DeleteOriginals.Checked && !dialog.Selection.Enabled, "Inherited removal hidden or editable"); dialog.UseCustom.Checked = true; dialog.Selection.Videos.Checked = false; dialog.SaveCustomSelection();
                    Check(!dialog.Draft.Resolve(defaults).DeleteOriginals && defaults.Snapshot().DeleteOriginals && profile.Resolve(defaults).DeleteOriginals, "Draft reset changed shared or saved choices");
                }
            });
            Test("removal dialogs and success notifications disclose permanence with cancel as enabling default", delegate {
                using (var dialog = new OriginalRemovalConfirmationDialog("Every future device using defaults.", false)) {
                    Check(dialog.AcceptButton == dialog.Cancel && dialog.CancelButton == dialog.Cancel && dialog.Cancel.DialogResult == DialogResult.Cancel && dialog.EnableRemoval.DialogResult == DialogResult.Yes && dialog.Explanation.Text.Contains("photos and audio") && dialog.Explanation.Text.Contains("Sony"), "Removal enable default/scope/warning unsafe");
                }
                Check(OriginalRemovalChoice.BeforeImport(RemoveOptions()).Contains("PERMANENT") && !OriginalRemovalChoice.BeforeImport(new MediaOptions(true, true)).Contains("REMOVAL IS ON") && !OriginalRemovalChoice.Summary(2).Contains("Originals kept.") && CompletionPresentation.Notification(true, 2).Contains("Permanently removed 2"), "Manual/completion text misreported original retention");
                using (var dialog = new VerifiedImportConfirmationDialog(OriginalRemovalChoice.BeforeImport(RemoveOptions()))) {
                    IntPtr handle = dialog.Handle; CreateRenderHandles(dialog);
                    Check(handle != IntPtr.Zero && !dialog.Visible && dialog.AcceptButton == dialog.Cancel && dialog.CancelButton == dialog.Cancel && dialog.StartImport.DialogResult == DialogResult.Yes && dialog.Explanation.Text.Contains("PERMANENT"), "Manual confirmation default or review text unsafe");
                }
            });
        }
    }
}
