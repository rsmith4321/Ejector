using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static ManualImportReview ReviewManualFixture(Fixture f, ScopedDiscovery d, Action<string> resolve = null) {
            return new ManualImportReview(f.Source, Path.Combine(f.Source, "recordings"), f.Destination, (source, destination) => new VolumeImportGuard(d, source, destination, d.State.Volumes[0], d.State.Disks[0], d.Epoch, path => {
                if (resolve != null) resolve(path); return SafePaths.Within(path, f.Source) ? "source-volume" : "destination-volume";
            }));
        }
        static ImportResult RunManualFixture(ManualImportReview review, Operations operations, MediaOptions options = null, CancellationToken cancel = default(CancellationToken), Action<ImportWorkProgress> progress = null) {
            return review.Run(new VerifiedImporter(operations), "source-volume", options ?? new MediaOptions(true, false, true), cancel, _ => {}, progress);
        }
        static void RestoreManualFolder(Fixture f, string path) {
            Check(new[] { path, path + "-moved", path + "-replacement" }.All(x => x != f.Root && SafePaths.Within(x, f.Root)), "Manual restore escaped owned fixture");
            Directory.Move(path, path + "-replacement"); Directory.Move(path + "-moved", path);
            Check(File.ReadAllText(Path.Combine(path + "-replacement", "foreign.keep")) == "retain replacement", "Foreign marker changed during restore");
        }
        static void CheckManualFolderRetained(Fixture f, string path) {
            Check(Directory.Exists(path + "-moved") && File.ReadAllText(Path.Combine(path, "foreign.keep")) == "retain replacement" && File.ReadAllText(Path.Combine(f.Source, "retained.mp4")) == "retain generated connected original", "Refusal changed moved/replacement folders or generated original");
            Check(Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Refused review published a receipt");
        }
        static void CheckManualLeasesReleased(params string[] paths) {
            foreach (string path in paths) using (var handle = Native.CreateFile(path, 0x10000, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero)) Check(!handle.IsInvalid, "Manual review retained a directory handle: " + path);
        }
        static void CheckManualCopy(Fixture f, ImportResult result, Operations operations, string contents = "fresh generated selected clip") {
            Check(result.Files == 1 && result.HasSelectedMedia && result.RemovedOriginals == 0 && result.Receipt.OriginalsKept && File.ReadAllText(Path.Combine(f.Source, @"recordings\clip.mp4")) == contents && File.ReadAllText(Saved(result, "clip.mp4")) == contents, "Manual import lost selected membership, original, layout or verified copy");
            Check(File.Exists(result.ReceiptPath) && result.Receipt.Files.Single().Sha256 == IndependentFileHash(Saved(result, "clip.mp4")) && Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0, "Manual import lost receipt/hash or leaked private work");
            Check(File.ReadAllText(Path.Combine(f.Source, "retained.mp4")) == "retain generated connected original", "Unselected generated media changed"); using (operations.Reserve(new[] { 700, 701 })) {}
        }
        static void ManualImportReviewTests() {
            foreach (bool source in new[] { true, false }) {
                bool changeSource = source;
                Test("manual review captures " + (changeSource ? "selected media" : "destination") + " before guard resolution", () => { using (var f = new Fixture()) {
                    var d = new ScopedDiscovery(f); ImportProfile draft; SelectionProfile(f, d, out draft); string selected = Path.Combine(f.Source, "recordings"), target = changeSource ? selected : f.Destination; bool replaced = false;
                    Throws<SafetyException>(() => { using (ReviewManualFixture(f, d, path => { if (!replaced && path == target) { replaced = true; ReplaceFixtureDirectory(f, target); } })) {} });
                    Check(replaced, "Resolver replacement did not run"); CheckManualFolderRetained(f, target); RestoreManualFolder(f, target); CheckManualLeasesReleased(f.Source, selected, f.Destination);
                    f.Put(@"recordings\clip.mp4", "fresh generated selected clip"); var operations = new Operations(); ImportResult result; using (var retry = ReviewManualFixture(f, d)) result = RunManualFixture(retry, operations); CheckManualCopy(f, result, operations);
                } });
                Test("manual confirmation refuses " + (changeSource ? "selected media" : "destination") + " replacement before copying", () => { using (var f = new Fixture()) {
                    var d = new ScopedDiscovery(f); ImportProfile draft; SelectionProfile(f, d, out draft); string target = changeSource ? Path.Combine(f.Source, "recordings") : f.Destination; var operations = new Operations();
                    using (var review = ReviewManualFixture(f, d)) { ReplaceFixtureDirectory(f, target); if (changeSource) f.Put(@"recordings\foreign.mp4", "owned replacement clip, do not adopt"); Throws<SafetyException>(() => RunManualFixture(review, operations)); }
                    CheckManualFolderRetained(f, target); if (changeSource) Check(File.ReadAllText(Path.Combine(target, "foreign.mp4")) == "owned replacement clip, do not adopt", "Replacement media changed"); RestoreManualFolder(f, target);
                    f.Put(@"recordings\clip.mp4", "fresh generated selected clip"); ImportResult result; using (var retry = ReviewManualFixture(f, d)) result = RunManualFixture(retry, operations); CheckManualCopy(f, result, operations);
                } });
                Test("manual legacy binding refuses " + (changeSource ? "selected" : "destination") + " folder replacement before publication", () => { using (var f = new Fixture()) {
                    var d = new ScopedDiscovery(f); ImportProfile draft; var profile = SelectionProfile(f, d, out draft, true); byte[] old = File.ReadAllBytes(Path.Combine(profile.Root, profile.FileName)); string target = changeSource ? Path.Combine(f.Source, "recordings") : f.Destination; bool replaced = false;
                    using (var review = ReviewManualFixture(f, d)) Throws<SafetyException>(() => review.BindLegacy(profile, stage => { if (stage == "Before publication") { replaced = true; ReplaceFixtureDirectory(f, target); } }));
                    Check(replaced && File.ReadAllBytes(Path.Combine(profile.Root, profile.FileName)).SequenceEqual(old), "Refused migration changed reviewed configuration"); CheckManualFolderRetained(f, target); RestoreManualFolder(f, target); CheckManualLeasesReleased(f.Source, Path.Combine(f.Source, "recordings"), f.Destination);
                    using (var retry = ReviewManualFixture(f, d)) Check(retry.BindLegacy(profile).SavedDestination.Matches(retry.Guard.DestinationIdentity), "Fresh migration did not bind current destination");
                } });
                Test("manual legacy binding refuses " + (changeSource ? "source" : "destination") + " reconnect before publication", () => { using (var f = new Fixture()) {
                    var d = new ScopedDiscovery(f); ImportProfile draft; var profile = SelectionProfile(f, d, out draft, true); byte[] old = File.ReadAllBytes(Path.Combine(profile.Root, profile.FileName)); bool changed = false;
                    using (var review = ReviewManualFixture(f, d)) Throws<SafetyException>(() => review.BindLegacy(profile, stage => { if (stage == "Before publication") { changed = true; d.Event(changeSource ? "source-usb" : "destination-controller"); } }));
                    Check(changed && File.ReadAllBytes(Path.Combine(profile.Root, profile.FileName)).SequenceEqual(old) && File.ReadAllText(Path.Combine(f.Source, "retained.mp4")) == "retain generated connected original", "Reconnect migration changed saved choices/media");
                    using (var retry = ReviewManualFixture(f, d)) Check(retry.BindLegacy(profile).SavedDestination != null, "Fresh connection review did not retry migration");
                } });
            }
            Test("manual confirmation refuses an initially missing recording folder that appears", () => { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); ImportProfile draft; SelectionProfile(f, d, out draft); string selected = Path.Combine(f.Source, "recordings"); Directory.Delete(selected); var operations = new Operations();
                using (var review = ReviewManualFixture(f, d)) { f.Put(@"recordings\clip.mp4", "new folder generated clip"); Throws<SafetyException>(() => RunManualFixture(review, operations)); }
                Check(File.ReadAllText(Path.Combine(selected, "clip.mp4")) == "new folder generated clip" && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Missing-folder refusal changed new media or published completion");
                ImportResult result; using (var retry = ReviewManualFixture(f, d)) result = RunManualFixture(retry, operations); CheckManualCopy(f, result, operations, "new folder generated clip");
            } });
            Test("manual review permits ordinary contents and unrelated events with frozen legacy choices", () => { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); ImportProfile draft; var profile = SelectionProfile(f, d, out draft, true); var options = profile.Value.Resolve(new ImportDefaults()); var operations = new Operations(); ImportResult result;
                using (var review = ReviewManualFixture(f, d)) {
                    f.Put(@"recordings\clip.mp4", "fresh generated selected clip"); f.Put(@"recordings\ordinary.txt", "retain unknown file"); d.Event("other-usb");
                    var bound = review.BindLegacy(profile); var expected = profile.Value; expected.SavedDestination = review.Guard.DestinationIdentity;
                    Check(ReplaySerialize(bound, typeof(ImportProfile)).SequenceEqual(ReplaySerialize(expected, typeof(ImportProfile))) && profile.Value.SavedDestination == null, "Migration altered reviewed choices"); result = RunManualFixture(review, operations, options);
                }
                CheckManualCopy(f, result, operations); Check(File.ReadAllText(Path.Combine(f.Source, @"recordings\ordinary.txt")) == "retain unknown file", "Unknown file changed"); CheckManualLeasesReleased(f.Source, Path.Combine(f.Source, "recordings"), f.Destination);
                var identity = DestinationIdentity.Capture(d.State, "destination-volume"); string history = Path.Combine(f.Root, "history"); Directory.CreateDirectory(history); ImportHistory.Add(ImportHistory.Create(result, profile.Value, "Fixture manual camera", identity), history); ImportHistory.Verify(ImportHistory.Load(history).Single(), identity, new FakeGuard(), operations, CancellationToken.None, _ => {});
            } });
            Test("cancelled manual review releases folders without binding or importing", () => { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); ImportProfile draft; var profile = SelectionProfile(f, d, out draft, true); byte[] old = File.ReadAllBytes(Path.Combine(profile.Root, profile.FileName)); using (ReviewManualFixture(f, d)) {} CheckManualLeasesReleased(f.Source, Path.Combine(f.Source, "recordings"), f.Destination);
                Check(File.ReadAllBytes(Path.Combine(profile.Root, profile.FileName)).SequenceEqual(old) && Directory.GetFiles(f.Destination, "*", SearchOption.AllDirectories).Length == 0 && profile.Value.SavedDestination == null, "Cancelled review changed configuration or started import");
            } });
            Test("manual factory failure releases captured folders", () => { using (var f = new Fixture()) {
                string selected = Path.Combine(f.Source, "recordings"); Directory.CreateDirectory(selected); Throws<InvalidOperationException>(() => new ManualImportReview(f.Source, selected, f.Destination, (source, destination) => { throw new InvalidOperationException("Owned factory failure"); })); CheckManualLeasesReleased(f.Source, selected, f.Destination); Check(Directory.GetFiles(f.Destination).Length == 0, "Factory failure began work");
            } });
            Test("manual review retains durable receipt after terminal cancellation and reconnect", () => { using (var f = new Fixture()) using (var stop = new CancellationTokenSource()) {
                var d = new ScopedDiscovery(f); ImportProfile draft; var profile = SelectionProfile(f, d, out draft, true); f.Put(@"recordings\clip.mp4", "fresh generated selected clip"); var operations = new Operations(); ImportResult result; VolumeImportGuard guard; bool reached = false;
                using (var review = ReviewManualFixture(f, d)) {
                    guard = review.Guard; review.BindLegacy(profile); result = RunManualFixture(review, operations, cancel: stop.Token, progress: value => { if (value.Phase == "Import complete") { reached = true; Check(Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 1, "Terminal display preceded receipt"); d.Event("source-usb"); stop.Cancel(); throw new OperationCanceledException("Owned terminal cancellation"); } });
                }
                Check(reached && stop.IsCancellationRequested, "Terminal event did not occur"); CheckManualCopy(f, result, operations); Throws<SafetyException>(() => guard.Validate()); CheckManualLeasesReleased(f.Source, Path.Combine(f.Source, "recordings"), f.Destination);
            } });
            foreach (bool index in new[] { false, true }) {
                bool indexOnly = index;
                Test("manual review preserves missing recording-folder " + (indexOnly ? "index-only" : "empty-card") + " no-media behavior", () => { using (var f = new Fixture()) {
                    if (indexOnly) f.Put("fileinfo_list.list", "retain generated device index"); var d = new ScopedDiscovery(f); var operations = new Operations(); ImportResult result;
                    using (var review = ReviewManualFixture(f, d)) { d.Event("other-usb"); result = RunManualFixture(review, operations); }
                    Check(result.Files == 0 && !result.HasSelectedMedia && result.ReceiptPath == null && !Directory.Exists(Path.Combine(f.Source, "recordings")) && Directory.GetFiles(f.Destination, "*", SearchOption.AllDirectories).Length == 0, "Missing selection created folders, receipt or false media completion");
                    if (indexOnly) Check(File.ReadAllText(Path.Combine(f.Source, "fileinfo_list.list")) == "retain generated device index", "Missing selection changed the device index"); CheckManualLeasesReleased(f.Source, f.Destination); using (operations.Reserve(new[] { 700, 701 })) {}
                } });
            }
            Test("manual legacy late publication loss reports the actual saved binding without starting import", () => { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); ImportProfile draft; var profile = SelectionProfile(f, d, out draft, true); bool changed = false; SafetyException failure;
                using (var review = ReviewManualFixture(f, d)) failure = Throws<SafetyException>(() => review.BindLegacy(profile, stage => { if (stage == "After publication") { changed = true; d.Event("source-usb"); } }));
                Check(changed && failure.Message.Contains("Configuration was published"), "Late migration failure denied its actual publication");
                var current = Profiles.ReviewConnected(d.State.Volumes[0], d.State.Disks[0], profile.Root);
                Check(current.Value.SavedDestination.Matches(DestinationIdentity.Capture(d.State, "destination-volume")) && profile.Value.SavedDestination == null && Directory.GetFiles(profile.Root, "*.partial").Length == 0 && Directory.GetFiles(f.Destination, "*", SearchOption.AllDirectories).Length == 0, "Late publication lost exact choices or began media work");
                CheckManualLeasesReleased(f.Source, Path.Combine(f.Source, "recordings"), f.Destination); using (var fresh = ReviewManualFixture(f, d)) fresh.Validate();
            } });
            Test("manual review folder ownership spans worker construction binding copying and caller disposal", () => { using (var f = new Fixture()) {
                var d = new ScopedDiscovery(f); ImportProfile draft; var profile = SelectionProfile(f, d, out draft, true); f.Put(@"recordings\clip.mp4", "fresh generated selected clip"); var operations = new Operations();
                int caller = Thread.CurrentThread.ManagedThreadId, construction = caller, binding = caller; bool workerProgress = false; ImportResult result;
                using (var review = System.Threading.Tasks.Task.Run(() => ReviewManualFixture(f, d, _ => construction = Thread.CurrentThread.ManagedThreadId)).GetAwaiter().GetResult()) {
                    var bound = System.Threading.Tasks.Task.Run(() => review.BindLegacy(profile, _ => binding = Thread.CurrentThread.ManagedThreadId)).GetAwaiter().GetResult();
                    Check(bound.SavedDestination.Matches(review.Guard.DestinationIdentity), "Worker binding changed the selected destination");
                    result = System.Threading.Tasks.Task.Run(() => RunManualFixture(review, operations, progress: value => { Check(Thread.CurrentThread.ManagedThreadId != caller, "Storage progress ran on the caller thread"); workerProgress = true; })).GetAwaiter().GetResult();
                }
                Check(construction != caller && binding != caller && workerProgress, "Worker stages were not observed"); CheckManualCopy(f, result, operations); CheckManualLeasesReleased(f.Source, Path.Combine(f.Source, "recordings"), f.Destination);
            } });
        }
    }
}
