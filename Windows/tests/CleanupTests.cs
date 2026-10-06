using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateHardLink(string path, string existing, IntPtr reserved);
        static void CleanFixture(Fixture fixture, Action validate, CancellationToken token, CleanupCounts counts, Action<string> report) { MetadataCleaner.Clean(fixture.Source, validate, token, counts, report); }
        static void MountCleanupFixture(Inventory state, Fixture fixture) { state.Volumes.Single(v => v.Id == "volume-a").Mounts = new List<string> { fixture.Source }; var second = state.Volumes.SingleOrDefault(v => v.Id == "volume-b"); if (second != null) second.Mounts = new List<string> { fixture.Destination }; }
        static string ResolveFixture(Fixture fixture, string path) { return path.Equals(fixture.Source, StringComparison.OrdinalIgnoreCase) ? "volume-a" : path.Equals(fixture.Destination, StringComparison.OrdinalIgnoreCase) ? "volume-b" : "unknown-fixture-volume"; }
        static void CleanupTests() {
            Test("cleanup removes only supported metadata and never arbitrary __MACOSX contents", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { ".DS_Store", ".apdisk", "._clip", @"DCIM\._index", @"__MACOSX\._junk", @"empty\__MACOSX\._empty" }) f.Put(name, "Disposable metadata fixture");
                foreach (string name in new[] { "clip.mp4", @"DCIM\clip.xml", @"__MACOSX\keep.mp4", "unknown.bin", "DS_Store", "apdisk", @"._named-folder\keep.mp4" }) f.Put(name, "Untouched fixture original");
                var counts = new CleanupCounts(); CleanFixture(f, () => {}, CancellationToken.None, counts, x => {});
                Check(counts.Files == 6 && counts.Folders == 1 && Directory.Exists(Path.Combine(f.Source, "__MACOSX")), "Wrong removal count or recursive folder deletion");
                foreach (string name in new[] { "clip.mp4", @"DCIM\clip.xml", @"__MACOSX\keep.mp4", "unknown.bin", "DS_Store", "apdisk", @"._named-folder\keep.mp4" }) Check(File.ReadAllText(Path.Combine(f.Source, name)) == "Untouched fixture original", "Original removed " + name);
            } });
            Test("cleanup protects Mac and Windows Trash/system folders case-insensitively", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { ".Trashes", ".Spotlight-V100", ".fseventsd", ".TemporaryItems", "$RECYCLE.BIN", "System Volume Information", ".Trash", ".Trash-1000" }) f.Put(Path.Combine(name.ToUpperInvariant(), "._keep"), "Fixture protected file");
                f.Put("._remove", "Fixture metadata"); var counts = new CleanupCounts(); CleanFixture(f, () => {}, CancellationToken.None, counts, x => {});
                Check(counts.Files == 1 && Directory.GetFiles(f.Source, "._keep", SearchOption.AllDirectories).Length == 8, "Trash or system contents cleaned");
                Throws<SafetyException>(() => MetadataCleaner.Clean(Path.Combine(f.Source, ".TRASHES"), () => {}, CancellationToken.None, counts, x => {}));
            } });
            Test("new user files inside __MACOSX survive cleanup and prevent folder removal", delegate { using (var f = new Fixture()) {
                f.Put(@"__MACOSX\._junk", "Fixture metadata"); var counts = new CleanupCounts(); string original = Path.Combine(f.Source, "__MACOSX", "new.mp4");
                CleanFixture(f, () => {}, CancellationToken.None, counts, x => File.WriteAllText(original, "Fixture new recording"));
                Check(counts.Files == 1 && counts.Folders == 0 && File.ReadAllText(original) == "Fixture new recording", "New recording removed with metadata folder");
            } });
            Test("cleanup skips reparse directories and never touches their targets", delegate { using (var f = new Fixture()) {
                File.WriteAllText(Path.Combine(f.Destination, ".DS_Store"), "Outside selected root fixture"); string link = Path.Combine(f.Source, "__MACOSX"); CreateJunction(f, link, f.Destination);
                var counts = new CleanupCounts(); CleanFixture(f, () => {}, CancellationToken.None, counts, x => {});
                Check(counts.Files == 0 && counts.Folders == 0 && File.Exists(Path.Combine(f.Destination, ".DS_Store")) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "Link/target removed");
                Throws<SafetyException>(() => MetadataCleaner.Clean(link, () => {}, CancellationToken.None, counts, x => {}));
            } });
            Test("cleanup retains system-attributed and hard-linked metadata names", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "Fixture media"); f.Put(".DS_Store", "Protected metadata fixture"); File.SetAttributes(Path.Combine(f.Source, ".DS_Store"), FileAttributes.System);
                Check(CreateHardLink(Path.Combine(f.Source, "._clip.mp4"), Path.Combine(f.Source, "clip.mp4"), IntPtr.Zero), "Fixture hardlink creation failed");
                var counts = new CleanupCounts(); CleanFixture(f, () => {}, CancellationToken.None, counts, x => {});
                Check(counts.Files == 0 && File.Exists(Path.Combine(f.Source, ".DS_Store")) && File.Exists(Path.Combine(f.Source, "._clip.mp4")) && File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == "Fixture media", "Protected/link alias removed");
            } });
            Test("cleanup cancellation preserves originals and reports completed metadata removals", delegate { using (var f = new Fixture()) using (var cancellation = new CancellationTokenSource()) {
                f.Put("._a", "Fixture a"); f.Put("._b", "Fixture b"); f.Put("clip.mp4", "Fixture original"); var counts = new CleanupCounts();
                Throws<OperationCanceledException>(() => CleanFixture(f, () => {}, cancellation.Token, counts, x => cancellation.Cancel()));
                Check(counts.Files == 1 && Directory.GetFiles(f.Source, "._*").Length == 1 && File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == "Fixture original", "Cancellation lost partial accounting/original");
            } });
            Test("cleanup identity failure before work deletes nothing", delegate { using (var f = new Fixture()) {
                f.Put(".DS_Store", "Fixture metadata"); var counts = new CleanupCounts(); Throws<SafetyException>(() => CleanFixture(f, () => { throw new SafetyException("Fixture disconnected"); }, CancellationToken.None, counts, x => {}));
                Check(counts.Files == 0 && File.Exists(Path.Combine(f.Source, ".DS_Store")), "Deletion before identity guard");
            } });
            Test("cleanup frozen plan refuses changed roots, serials and unrelated volumes", delegate { using (var f = new Fixture()) {
                var state = CardState(); MountCleanupFixture(state, f); var plan = MetadataCleanupPlan.Single(state, EjectService.Plan(state, "volume-a"), "volume-a");
                state.Volumes[0].Mounts[0] = f.Destination; Throws<SafetyException>(() => plan.Validate(state, p => "volume-a"));
                state = CardState(); MountCleanupFixture(state, f); plan = MetadataCleanupPlan.Single(state, EjectService.Plan(state, "volume-a"), "volume-a"); state.Volumes[0].Serial = "reformatted"; Throws<SafetyException>(() => plan.Validate(state, p => "volume-a"));
                state = CardState(); MountCleanupFixture(state, f); Throws<SafetyException>(() => MetadataCleanupPlan.Single(state, EjectService.Plan(state, "volume-a"), "volume-b"));
                state.Volumes[0].Mounts.Clear(); Throws<SafetyException>(() => MetadataCleanupPlan.Single(state, EjectService.Plan(state, "volume-a"), "volume-a"));
            } });
            Test("single clean-and-eject reserves disks and rechecks identity before fake API", delegate { using (var f = new Fixture()) {
                f.Put(".DS_Store", "Fixture metadata"); f.Put("clip.mp4", "Fixture original"); var d = new FakeDiscovery { State = State() }; MountCleanupFixture(d.State, f); var plan = EjectService.Plan(d.State, "volume-a"); var selection = MetadataCleanupPlan.Single(d.State, plan, "volume-a"); var counts = new CleanupCounts(); var ops = new Operations(); var api = new FakeEject();
                var result = new EjectService(d, api, ops).Execute(plan, initial => { selection.Preflight(initial, p => ResolveFixture(f, p)); selection.Clean(() => { Throws<SafetyException>(() => ops.Reserve(new[] { 7 })); var fresh = d.Scan(); EjectService.Validate(d, fresh, plan); return fresh; }, p => ResolveFixture(f, p), CancellationToken.None, counts, x => {}); });
                Check(result.Success && api.Calls == 1 && counts.Files == 1 && File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == "Fixture original", "Cleanup/removal order wrong"); using (ops.Reserve(new[] { 7 })) {}
            } });
            Test("read-only metadata failure stops eject without clearing protection or retrying", delegate { using (var f = new Fixture()) {
                f.Put("._a", "Fixture removable metadata"); f.Put("._z", "Fixture protected metadata"); f.Put("clip.mp4", "Fixture original"); string protectedPath = Path.Combine(f.Source, "._z"); File.SetAttributes(protectedPath, FileAttributes.ReadOnly);
                try {
                    var d = new FakeDiscovery { State = State() }; MountCleanupFixture(d.State, f); var plan = EjectService.Plan(d.State, "volume-a"); var selection = MetadataCleanupPlan.Single(d.State, plan, "volume-a"); var counts = new CleanupCounts(); var api = new FakeEject();
                    var error = Throws<SafetyException>(() => new EjectService(d, api, new Operations()).Execute(plan, initial => selection.Clean(() => d.Scan(), p => ResolveFixture(f, p), CancellationToken.None, counts, x => {})));
                    Check(api.Calls == 0 && counts.Files == 1 && error.Message.Contains("Removed 1") && File.Exists(protectedPath) && (File.GetAttributes(protectedPath) & FileAttributes.ReadOnly) != 0 && File.Exists(Path.Combine(f.Source, "clip.mp4")), "Failed cleanup ejected or bypassed protection");
                } finally { File.SetAttributes(protectedPath, FileAttributes.Normal); }
            } });
            Test("connection change after metadata removal prevents the subsequent eject", delegate { using (var f = new Fixture()) {
                f.Put(".DS_Store", "Fixture metadata"); var d = new FakeDiscovery { State = State() }; MountCleanupFixture(d.State, f); var plan = EjectService.Plan(d.State, "volume-a"); var selection = MetadataCleanupPlan.Single(d.State, plan, "volume-a"); var api = new FakeEject(); var counts = new CleanupCounts();
                Throws<SafetyException>(() => new EjectService(d, api, new Operations()).Execute(plan, initial => selection.Clean(() => { var fresh = d.Scan(); EjectService.Validate(d, fresh, plan); return fresh; }, p => ResolveFixture(f, p), CancellationToken.None, counts, x => d.State.Epoch++)));
                Check(counts.Files == 1 && api.Calls == 0, "Changed connection ejected after cleanup");
            } });
            Test("cancellation after cleanup preparation prevents single and bulk API requests", delegate {
                using (var cancellation = new CancellationTokenSource()) {
                    var d = new FakeDiscovery { State = State() }; var api = new FakeEject(); Throws<OperationCanceledException>(() => new EjectService(d, api, new Operations()).Execute(EjectService.Plan(d.State, "volume-a"), initial => cancellation.Cancel(), cancellation.Token)); Check(api.Calls == 0, "Single removal ignored cancellation after preparation");
                }
                using (var cancellation = new CancellationTokenSource()) {
                    var d = new CardDiscovery(); var api = new CardApi { JustBeforeValidation = () => cancellation.Cancel(), OnRequest = id => new EjectResult { Success = true } };
                    var service = new BulkEjectService(d, d, new Operations(), validate => { api.Validate = validate; return api; }, () => {}, 2);
                    var result = service.Execute(BulkEjectPolicy.Plan(d.State), initial => {}, (target, validate) => {}, cancellation.Token); Check(!result.Success && api.Calls.Count == 0, "Bulk live request ignored cancellation");
                }
            });
            Test("bulk cleanup preflights every root before deleting or requesting any device", delegate { using (var f = new Fixture()) {
                f.Put(".DS_Store", "Fixture A metadata"); string badRoot = Path.Combine(f.Destination, "link"); CreateJunction(f, badRoot, f.Source); var d = new CardDiscovery(); MountCleanupFixture(d.State, f); d.State.Volumes[1].Mounts[0] = badRoot;
                var bulk = BulkEjectPolicy.Plan(d.Scan()); var selections = bulk.Targets.Select(t => MetadataCleanupPlan.Bulk(d.State, t)).ToArray(); var api = new CardApi { OnRequest = id => new EjectResult { Success = true } }; var counts = new CleanupCounts();
                var service = new BulkEjectService(d, d, new Operations(), validate => { api.Validate = validate; return api; }, () => {}, 2);
                var result = service.Execute(bulk, initial => { foreach (var selection in selections) selection.Preflight(initial, p => p == f.Source ? "volume-a" : "volume-b"); }, (target, validate) => selections.Single(s => s.TargetId == target.Id).Clean(validate, p => "volume-a", CancellationToken.None, counts, x => {}));
                Check(!result.Success && api.Calls.Count == 0 && counts.Files == 0 && File.Exists(Path.Combine(f.Source, ".DS_Store")), "Partial cleanup before peer root refusal");
            } });
            Test("bulk cleanup preserves earlier successes when later cleanup fails", delegate { using (var f = new Fixture()) {
                f.Put(".DS_Store", "Fixture A metadata"); string second = Path.Combine(f.Destination, ".DS_Store"); File.WriteAllText(second, "Fixture B metadata"); File.SetAttributes(second, FileAttributes.ReadOnly);
                try {
                    var d = new CardDiscovery(); MountCleanupFixture(d.State, f); var plan = BulkEjectPolicy.Plan(d.Scan()); var selections = plan.Targets.Select(t => MetadataCleanupPlan.Bulk(d.State, t)).ToArray(); var counts = new CleanupCounts(); var api = new CardApi { OnRequest = id => { d.Remove(id); return new EjectResult { Success = true }; } };
                    var service = new BulkEjectService(d, d, new Operations(), validate => { api.Validate = validate; return api; }, () => {}, 2);
                    var result = service.Execute(plan, initial => { foreach (var selection in selections) selection.Preflight(initial, p => ResolveFixture(f, p)); }, (target, validate) => selections.Single(s => s.TargetId == target.Id).Clean(validate, p => ResolveFixture(f, p), CancellationToken.None, counts, x => {}));
                    Check(!result.Success && result.Confirmed == 1 && result.SafeDevices.Count == 1 && api.Calls.Count == 1 && counts.Files == 1 && File.Exists(second) && result.Detail.Contains("Removed 1"), "Later cleanup failed to stop/preserve prior result");
                } finally { File.SetAttributes(second, FileAttributes.Normal); }
            } });
            Test("cleanup settings migrate off, require enabling confirmation and stay a draft", delegate { using (var f = new Fixture()) {
                var settings = new AppSettings(); Check(!settings.CleanupCardsEnabled && settings.WarnOtherEnabled, "Unsafe defaults"); AppSettings.Save(settings, f.Destination); Check(!AppSettings.Load(f.Destination).CleanupCardsEnabled, "Legacy opted into deletion");
                using (var dialog = new AppSettingsDialog(settings)) { dialog.Cleanup.Checked = true; dialog.WarnOther.Checked = false; Check(!settings.CleanupCardsEnabled && AppSettings.NeedsCleanupConfirmation(settings, dialog.Value), "Enabling bypasses acknowledgment or edits live state"); AppSettings.Save(dialog.Value, f.Destination); }
                var saved = AppSettings.Load(f.Destination); Check(saved.CleanupCardsEnabled && !saved.WarnOtherEnabled && !AppSettings.NeedsCleanupConfirmation(saved, saved), "Settings lost explicit choices");
            } });
            Test("completion cleanup authority is consumed once before a cleanup failure", delegate { using (var f = new Fixture()) {
                var d = new FakeDiscovery { State = State() }; MountCleanupFixture(d.State, f); d.State.Volumes[0].Group = StorageGroup.Camera; var registry = new ImportCompletionRegistry(); var entry = Completion(d.State, f, true); registry.Insert(entry); var api = new FakeEject();
                Check(entry.Origin.SourceIsCard, "Card state not captured"); Throws<SafetyException>(() => new CompletedEjectService(registry, d, api, new Operations(), p => "volume-a").Execute(entry.Id, initial => { throw new SafetyException("Fixture cleanup failed"); }));
                Check(api.Calls == 0 && registry.Get(entry.Id) == null, "Failed cleanup reused completed eject authority");
            } });
        }
    }
}
