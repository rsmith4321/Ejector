using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        sealed class SavedActionGuard : IImportGuard {
            internal Action OnProtected; internal int PropertyCalls;
            public int[] ProtectedDisks { get { PropertyCalls++; if (OnProtected != null) OnProtected(); return new[] { 700, 701 }; } }
            public void Validate() {}
        }
        static CompletedBatch SavedActionBatch(Fixture f) { f.Put("parent/clip.mp4", "owned saved action bytes"); return Batch(f, f.Run(line => {})); }
        static ImportCompletion SavedActionCompletion(Fixture f) {
            string folder = Path.Combine(f.Destination, "completed-folder"); Directory.CreateDirectory(folder);
            var state = State(); return new ImportCompletion(new ImportCompletionOrigin(state, state.Volumes[0].Id, state.Volumes[0].Mounts[0]), "Owned completion", folder, f.Destination, HistoryIdentity(), false, "Owned test result");
        }
        static void ReplaceSavedActionFolder(Fixture f, string path) {
            Check(path != f.Root && SafePaths.Within(path, f.Root), "Saved action replacement escaped owned root");
            var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories).ToDictionary(file => SafePaths.Relative(file, path), File.ReadAllBytes);
            ReplaceFixtureDirectory(f, path);
            foreach (var file in files) { string target = Path.Combine(path, file.Key); Directory.CreateDirectory(Path.GetDirectoryName(target)); File.WriteAllBytes(target, file.Value); }
        }
        static void RestoreSavedActionFolder(Fixture f, string path) {
            string moved = path + "-moved", replacement = path + "-replacement";
            Check(new[] { path, moved, replacement }.All(value => value != f.Root && SafePaths.Within(value, f.Root)), "Saved action restore escaped owned root");
            Directory.Move(path, replacement); Directory.Move(moved, path);
            Check(File.ReadAllText(Path.Combine(replacement, "foreign.keep")) == "retain replacement", "Restore changed retained foreign data");
        }
        static ScopedDiscovery SavedActionDiscovery(Fixture f) {
            var d = new ScopedDiscovery(f); d.State.Volumes[1].Id = HistoryIdentity().VolumeId; d.State.Volumes[1].Serial = HistoryIdentity().VolumeSerial; d.State.Disks[1].Serial = HistoryIdentity().DiskSerial; return d;
        }
        static void SavedActionOwnershipTests() {
            SavedActionPropertyTests();
            Test("completion factory cannot replace the selected leaf folder before viewer admission", () => {
                using (var f = new Fixture()) {
                    var entry = SavedActionCompletion(f); var d = SavedActionDiscovery(f); bool changed = false; int opened = 0; var operations = new Operations();
                    d.AfterScan = () => { if (!changed) { changed = true; ReplaceSavedActionFolder(f, entry.Folder); } };
                    Throws<SafetyException>(() => CompletionFolder.Open(entry, HistoryIdentity(), () => new WindowsDestinationGuard(d, f.Destination, HistoryIdentity(), path => HistoryIdentity().VolumeId), operations, path => opened++));
                    Check(changed && opened == 0 && File.ReadAllText(Path.Combine(entry.Folder, "foreign.keep")) == "retain replacement", "Completion guard factory adopted replacement folder");
                    using (operations.Reserve(new[] { 701 })) {}
                    d.AfterScan = null; RestoreSavedActionFolder(f, entry.Folder);
                    CompletionFolder.Open(entry, HistoryIdentity(), () => new WindowsDestinationGuard(d, f.Destination, HistoryIdentity(), path => HistoryIdentity().VolumeId), operations, path => opened++);
                    Check(opened == 1, "Fresh factory completion retry retained resources");
                }
            });
            Test("history factory captures saved parents before native inventory callbacks", () => {
                using (var f = new Fixture()) {
                    var batch = SavedActionBatch(f); var d = SavedActionDiscovery(f); string parent = Path.GetDirectoryName(batch.Originals.Single().Path); bool changed = false; int reports = 0; var operations = new Operations();
                    d.AfterScan = () => { if (!changed) { changed = true; ReplaceSavedActionFolder(f, parent); } };
                    Throws<SafetyException>(() => ImportHistory.Verify(batch, HistoryIdentity(), selected => new WindowsDestinationGuard(d, selected.DestinationRoot, selected.Destination, path => HistoryIdentity().VolumeId), operations, CancellationToken.None, line => reports++));
                    Check(changed && reports == 0 && File.ReadAllText(batch.Originals.Single().Path) == "owned saved action bytes", "History factory adopted replaced saved originals or read them before refusal");
                    using (operations.Reserve(new[] { 701 })) {}
                    d.AfterScan = null; RestoreSavedActionFolder(f, parent);
                    ImportHistory.Verify(batch, HistoryIdentity(), selected => new WindowsDestinationGuard(d, selected.DestinationRoot, selected.Destination, path => HistoryIdentity().VolumeId), operations, CancellationToken.None, line => reports++);
                    Check(reports == 1, "Fresh factory history retry retained resources");
                }
            });
            Test("history readiness callback cannot replace saved parents before verification", () => {
                using (var f = new Fixture()) {
                    var batch = SavedActionBatch(f); string parent = Path.GetDirectoryName(batch.Originals.Single().Path); int reports = 0, ready = 0; var operations = new Operations();
                    Throws<SafetyException>(() => ImportHistory.Verify(batch, HistoryIdentity(), selected => new FakeGuard(), operations, CancellationToken.None, line => reports++, guard => { ready++; ReplaceSavedActionFolder(f, parent); }));
                    Check(ready == 1 && reports == 0 && File.ReadAllText(batch.Originals.Single().Path) == "owned saved action bytes", "Readiness callback adopted a replaced parent");
                    using (operations.Reserve(new[] { 7, 8 })) {}
                    RestoreSavedActionFolder(f, parent); ImportHistory.Verify(batch, HistoryIdentity(), selected => new FakeGuard(), operations, CancellationToken.None, line => reports++);
                    Check(reports == 1, "Readiness refusal retained resources");
                }
            });
            Test("history factory mutations cannot retarget private exact batch membership", () => {
                using (var f = new Fixture()) {
                    var batch = SavedActionBatch(f); var expected = ImportHistory.Clone(batch); var current = HistoryIdentity(); int reports = 0;
                    File.WriteAllText(Path.Combine(batch.Folder, "unrecorded.mp4"), "retain unrecorded data");
                    ImportHistory.Verify(batch, current, selected => {
                        selected.Originals.Clear(); selected.Folder = Path.Combine(f.Root, "factory-only-folder"); selected.Destination.VolumeSerial = "factory-only-volume";
                        batch.Originals.Clear(); current.VolumeSerial = "caller-only-volume"; return new FakeGuard();
                    }, new Operations(), CancellationToken.None, line => reports++);
                    Check(reports == 1 && batch.Originals.Count == 0 && current.VolumeSerial == "caller-only-volume" && File.ReadAllText(expected.Originals.Single().Path) == "owned saved action bytes" && File.ReadAllText(Path.Combine(expected.Folder, "unrecorded.mp4")) == "retain unrecorded data", "Factory mutation retargeted membership or rewrote caller/foreign data");
                    ImportHistory.Verify(expected, HistoryIdentity(), selected => new FakeGuard(), new Operations(), CancellationToken.None, line => {});
                }
            });
            Test("completion factory permits ordinary folder changes unrelated events and releases native leases", () => {
                using (var f = new Fixture()) {
                    var entry = SavedActionCompletion(f); var d = SavedActionDiscovery(f); int opened = 0; var current = HistoryIdentity();
                    CompletionFolder.Open(entry, current, () => {
                        var guard = new WindowsDestinationGuard(d, f.Destination, HistoryIdentity(), path => HistoryIdentity().VolumeId);
                        File.WriteAllText(Path.Combine(entry.Folder, "ordinary.keep"), "retain ordinary viewer data"); d.Event("other-usb"); current.VolumeSerial = "caller-only-volume"; return guard;
                    }, new Operations(), path => { Check(path == entry.Folder, "Viewer was retargeted"); opened++; });
                    Check(opened == 1 && File.ReadAllText(Path.Combine(entry.Folder, "ordinary.keep")) == "retain ordinary viewer data" && current.VolumeSerial == "caller-only-volume", "Ordinary changes were refused or caller data was rolled back");
                    string moved = entry.Folder + "-released"; Check(SafePaths.Within(moved, f.Root), "Released viewer path escaped fixture"); Directory.Move(entry.Folder, moved); Directory.Move(moved, entry.Folder);
                }
            });
            Test("pre-cancelled history factory performs no callback or missing-folder access", () => {
                using (var f = new Fixture()) using (var cancel = new CancellationTokenSource()) {
                    var batch = SavedActionBatch(f); string moved = batch.Folder + "-absent"; Check(SafePaths.Within(moved, f.Root), "Absent path escaped fixture"); Directory.Move(batch.Folder, moved);
                    int factories = 0, ready = 0, reports = 0; cancel.Cancel();
                    Throws<OperationCanceledException>(() => ImportHistory.Verify(batch, HistoryIdentity(), selected => { factories++; return new FakeGuard(); }, new Operations(), cancel.Token, line => reports++, guard => ready++));
                    Check(factories == 0 && ready == 0 && reports == 0 && Directory.Exists(moved) && !Directory.Exists(batch.Folder), "Pre-cancelled factory accessed callbacks or recreated missing folders");
                    Directory.Move(moved, batch.Folder); ImportHistory.Verify(batch, HistoryIdentity(), selected => new FakeGuard(), new Operations(), CancellationToken.None, line => {});
                }
            });
        }
        static void SavedActionPropertyTests() {
            Test("completion folder captures identity before protected-disk property callbacks", () => {
                using (var f = new Fixture()) {
                    var entry = SavedActionCompletion(f); bool changed = false; int opened = 0; var operations = new Operations();
                    var guard = new SavedActionGuard { OnProtected = () => { if (!changed) { changed = true; ReplaceSavedActionFolder(f, entry.Folder); } } };
                    Throws<SafetyException>(() => CompletionFolder.Open(entry, HistoryIdentity(), guard, operations, path => opened++));
                    Check(changed && opened == 0 && File.ReadAllText(Path.Combine(entry.Folder, "foreign.keep")) == "retain replacement", "Protected-disk callback redirected the viewer");
                    using (operations.Reserve(new[] { 700, 701 })) {}
                    RestoreSavedActionFolder(f, entry.Folder); CompletionFolder.Open(entry, HistoryIdentity(), new FakeGuard(), operations, path => opened++);
                    Check(opened == 1, "Fresh completion retry retained leases/reservations");
                }
            });
            Test("history captures exact saved parents before protected-disk property callbacks", () => {
                using (var f = new Fixture()) {
                    var batch = SavedActionBatch(f); string parent = Path.GetDirectoryName(batch.Originals.Single().Path); bool changed = false; int reports = 0; var operations = new Operations();
                    var guard = new SavedActionGuard { OnProtected = () => { if (!changed) { changed = true; ReplaceSavedActionFolder(f, parent); } } };
                    Throws<SafetyException>(() => ImportHistory.Verify(batch, HistoryIdentity(), guard, operations, CancellationToken.None, line => reports++));
                    Check(changed && reports == 0 && File.ReadAllText(Path.Combine(f.Source, "parent/clip.mp4")) == "owned saved action bytes", "History adopted a replaced parent or changed generated source");
                    using (operations.Reserve(new[] { 700, 701 })) {}
                    RestoreSavedActionFolder(f, parent); ImportHistory.Verify(batch, HistoryIdentity(), new FakeGuard(), operations, CancellationToken.None, line => reports++);
                    Check(reports == 1, "Fresh history retry retained leases/reservations");
                }
            });
            Test("sharing captures exact saved parents before protected-disk property callbacks", () => {
                using (var f = new Fixture()) {
                    var batch = SavedActionBatch(f); string parent = Path.GetDirectoryName(batch.Originals.Single().Path); bool changed = false; int reports = 0; var operations = new Operations();
                    var guard = new SavedActionGuard { OnProtected = () => { if (!changed) { changed = true; ReplaceSavedActionFolder(f, parent); } } };
                    Throws<SafetyException>(() => { using (var held = new QuickShareBatch(batch, HistoryIdentity(), guard, operations, CancellationToken.None, line => reports++)) {} });
                    Check(changed && reports == 0 && !Directory.Exists(Path.Combine(batch.Folder, "SharingCopies")) && File.ReadAllText(Path.Combine(f.Source, "parent/clip.mp4")) == "owned saved action bytes", "Sharing adopted a replaced parent or started export work");
                    using (operations.Reserve(new[] { 700, 701 })) {}
                    RestoreSavedActionFolder(f, parent); using (var held = new QuickShareBatch(batch, HistoryIdentity(), new FakeGuard(), operations, CancellationToken.None, line => reports++)) { Check(held.Originals.Count == 1, "Fresh sharing retry lost recorded membership"); }
                    Check(reports == 1, "Fresh sharing retry retained leases/reservations");
                }
            });
            Test("pre-cancelled history invokes no device properties", () => {
                using (var f = new Fixture()) using (var cancel = new CancellationTokenSource()) {
                    var batch = SavedActionBatch(f); var guard = new SavedActionGuard(); cancel.Cancel();
                    Throws<OperationCanceledException>(() => ImportHistory.Verify(batch, HistoryIdentity(), guard, new Operations(), cancel.Token, line => { throw new Exception("Cancelled history reported work"); }));
                    Check(guard.PropertyCalls == 0 && File.ReadAllText(batch.Originals.Single().Path) == "owned saved action bytes", "Pre-cancelled history invoked a device property or changed saved data");
                }
            });
        }
    }
}
