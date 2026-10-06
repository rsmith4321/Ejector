using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        sealed class PathCallbackGuard : IImportGuard {
            internal Action Callback; internal int Calls;
            public int[] ProtectedDisks { get { return new[] { 700, 701 }; } }
            public void Validate() { Calls++; if (Callback != null) Callback(); }
        }
        static void ReplaceFixtureDirectory(Fixture f, string path) {
            Check(SafePaths.Within(path, f.Root) && !path.Equals(f.Root, StringComparison.OrdinalIgnoreCase), "Replacement escaped fixture root");
            Directory.Move(path, path + "-moved"); Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "foreign.keep"), "retain replacement");
        }
        static void PathOwnershipTests() {
            Test("completion folder validates held empty-directory identity after device callback before viewer", () => {
                using (var f = new Fixture()) { var entry = Completion(State(), f, false); bool changed = false; int opened = 0; var guard = new PathCallbackGuard { Callback = () => { if (!changed) { changed = true; ReplaceFixtureDirectory(f, f.Destination); } } }; var operations = new Operations();
                    Throws<SafetyException>(() => CompletionFolder.Open(entry, HistoryIdentity(), guard, operations, _ => opened++));
                    Check(changed && opened == 0 && File.ReadAllText(Path.Combine(f.Destination, "foreign.keep")) == "retain replacement" && Directory.Exists(f.Destination + "-moved"), "Changed folder opened or replacement was cleaned");
                    using (operations.Reserve(guard.ProtectedDisks)) { }
                }
            });
            Test("import rejects empty destination replacement from initial device validation before writing", () => {
                using (var f = new Fixture()) { f.Put("clip.mp4", "retained source"); bool changed = false; var guard = new PathCallbackGuard { Callback = () => { if (!changed) { changed = true; ReplaceFixtureDirectory(f, f.Destination); } } };
                    Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", guard, CancellationToken.None, _ => {}));
                    Check(changed && Directory.GetFileSystemEntries(f.Destination).Length == 1 && File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == "retained source", "Import published to replacement root or changed source");
                }
            });
            Test("import rejects destination replacement from scanning progress before file planning", () => {
                using (var f = new Fixture()) { f.Put("clip.mp4", "retained source"); bool changed = false;
                    Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, _ => {}, new MediaOptions(true, true), value => { if (!changed && value.Phase == "Scanning") { changed = true; ReplaceFixtureDirectory(f, f.Destination); } }));
                    Check(changed && Directory.GetFileSystemEntries(f.Destination).Length == 1 && File.Exists(Path.Combine(f.Source, "clip.mp4")), "Progress callback redirected import");
                }
            });
            Test("import rejects destination replacement from checking report before file access", () => {
                using (var f = new Fixture()) { f.Put("clip.mp4", "retained source"); bool changed = false;
                    Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, message => { if (!changed && message.StartsWith("Checking ", StringComparison.Ordinal)) { changed = true; ReplaceFixtureDirectory(f, f.Destination); } }));
                    Check(changed && Directory.GetFileSystemEntries(f.Destination).Length == 1 && File.Exists(Path.Combine(f.Source, "clip.mp4")), "Report callback redirected destination publication");
                }
            });
            Test("saved batch verification refuses renamed folder restored with byte-identical files during report", () => {
                using (var f = new Fixture()) { f.Put(@"DCIM\clip.mp4", "retained source"); var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, _ => {}, new MediaOptions(true, true, false)); var batch = Batch(f, result); string folder = Path.GetDirectoryName(batch.Originals.Single().Path);
                    Check(folder != batch.Folder, "Fixture did not preserve nested originals"); var files = Directory.GetFiles(folder).ToDictionary(Path.GetFileName, File.ReadAllBytes); bool changed = false;
                    Throws<SafetyException>(() => ImportHistory.Verify(batch, HistoryIdentity(), new FakeGuard(), new Operations(), CancellationToken.None, _ => { if (!changed) { changed = true; ReplaceFixtureDirectory(f, folder); foreach (var item in files) File.WriteAllBytes(Path.Combine(folder, item.Key), item.Value); } }));
                    Check(changed && Directory.Exists(folder + "-moved") && File.ReadAllText(Path.Combine(folder, "foreign.keep")) == "retain replacement", "Verification accepted replacement identity or cleaned it");
                }
            });
            Test("cleanup refuses renamed child at validation before removing foreign replacement metadata", () => {
                using (var f = new Fixture()) { f.Put(@"child\.DS_Store", "original metadata keep"); f.Put(@"child\clip.mp4", "original recording keep"); int calls = 0; bool changed = false; string child = Path.Combine(f.Source, "child"); var counts = new CleanupCounts();
                    Throws<SafetyException>(() => MetadataCleaner.Clean(f.Source, () => { if (++calls == 6) { changed = true; ReplaceFixtureDirectory(f, child); File.WriteAllText(Path.Combine(child, ".DS_Store"), "foreign metadata keep"); File.WriteAllText(Path.Combine(child, "clip.mp4"), "foreign recording keep"); } }, CancellationToken.None, counts, _ => {}));
                    Check(changed && counts.Files == 0 && File.ReadAllText(Path.Combine(child, ".DS_Store")) == "foreign metadata keep" && File.ReadAllText(Path.Combine(child + "-moved", ".DS_Store")) == "original metadata keep" && File.ReadAllText(Path.Combine(child + "-moved", "clip.mp4")) == "original recording keep", "Cleanup removed metadata from replacement or originals");
                }
            });
            Test("import reuse refuses byte-identical target directory replacement during reuse report", () => {
                using (var f = new Fixture()) { f.Put(@"DCIM\clip.mp4", "retained source"); var options = new MediaOptions(true, true, false); var first = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, _ => {}, options); string saved = Saved(first, @"DCIM\clip.mp4"), folder = Path.GetDirectoryName(saved); byte[] bytes = File.ReadAllBytes(saved); bool changed = false;
                    Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, message => { if (!changed && message.StartsWith("Reusing verified ", StringComparison.Ordinal)) { changed = true; ReplaceFixtureDirectory(f, folder); File.WriteAllBytes(saved, bytes); } }, options));
                    Check(changed && File.ReadAllBytes(saved).SequenceEqual(bytes) && File.ReadAllText(Path.Combine(folder, "foreign.keep")) == "retain replacement" && Directory.GetFiles(first.Folder, "verified-import.json", SearchOption.AllDirectories).Length == 1, "Replaced reuse folder accepted completion or lost copies");
                }
            });
            Test("import capacity callback cannot redirect newly pinned empty target directory", () => {
                using (var f = new Fixture()) { f.Put(@"DCIM\clip.mp4", "retained source"); bool changed = false; string folder = null; var importer = new VerifiedImporter(new Operations(), _ => { if (!changed) { folder = Directory.GetDirectories(f.Destination, "DCIM", SearchOption.AllDirectories).Single(); changed = true; ReplaceFixtureDirectory(f, folder); } return UInt64.MaxValue; }, _ => {});
                    Throws<SafetyException>(() => importer.Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, _ => {}, new MediaOptions(true, true, false)));
                    Check(changed && Directory.GetFileSystemEntries(folder).Length == 1 && File.ReadAllText(Path.Combine(folder, "foreign.keep")) == "retain replacement" && File.Exists(Path.Combine(f.Source, @"DCIM\clip.mp4")), "Capacity callback redirected publication or removed original");
                }
            });
            Test("saved batch captures every recorded parent before initial device callback", () => {
                using (var f = new Fixture()) { f.Put(@"DCIM\clip.mp4", "retained source"); var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, _ => {}, new MediaOptions(true, true, false)); var batch = Batch(f, result); string saved = batch.Originals.Single().Path, folder = Path.GetDirectoryName(saved); byte[] bytes = File.ReadAllBytes(saved); bool changed = false;
                    var guard = new PathCallbackGuard { Callback = () => { if (!changed) { changed = true; ReplaceFixtureDirectory(f, folder); File.WriteAllBytes(saved, bytes); } } };
                    Throws<SafetyException>(() => ImportHistory.Verify(batch, HistoryIdentity(), guard, new Operations(), CancellationToken.None, _ => {}));
                    Check(changed && File.ReadAllBytes(saved).SequenceEqual(bytes) && File.ReadAllText(Path.Combine(folder, "foreign.keep")) == "retain replacement", "Device callback replaced an uncaptured recorded parent");
                }
            });
            Test("completion nested folder replacement is refused independently of unchanged destination root", () => {
                using (var f = new Fixture()) { string folder = Path.Combine(f.Destination, "import-folder"); Directory.CreateDirectory(folder); var state = State(); var entry = new ImportCompletion(new ImportCompletionOrigin(state, state.Volumes[0].Id, state.Volumes[0].Mounts[0]), "Fixture", folder, f.Destination, HistoryIdentity(), false, "No media"); int opened = 0;
                    var guard = new PathCallbackGuard { Callback = () => ReplaceFixtureDirectory(f, folder) }; Throws<SafetyException>(() => CompletionFolder.Open(entry, HistoryIdentity(), guard, new Operations(), _ => opened++));
                    Check(opened == 0 && File.ReadAllText(Path.Combine(folder, "foreign.keep")) == "retain replacement", "Nested replacement reached folder viewer");
                }
            });
            Test("import no-match progress cannot replace destination and preserve a successful empty result", () => {
                using (var f = new Fixture()) { f.Put("photo.jpg", "retained photo"); bool changed = false;
                    Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, _ => {}, new MediaOptions(true, true), value => { if (value.Phase == "No matching media") { changed = true; ReplaceFixtureDirectory(f, f.Destination); } }));
                    Check(changed && Directory.GetFileSystemEntries(f.Destination).Length == 1 && File.ReadAllText(Path.Combine(f.Source, "photo.jpg")) == "retained photo", "No-match progress accepted replacement or wrote empty batch");
                }
            });
            Test("saved batch final device validation cannot replace previously verified parent", () => {
                using (var f = new Fixture()) { f.Put(@"DCIM\clip.mp4", "retained source"); var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture", new FakeGuard(), CancellationToken.None, _ => {}, new MediaOptions(true, true, false)); var batch = Batch(f, result); string folder = Path.GetDirectoryName(batch.Originals.Single().Path); var guard = new PathCallbackGuard(); bool changed = false;
                    guard.Callback = () => { if (guard.Calls == 3) { changed = true; ReplaceFixtureDirectory(f, folder); } };
                    Throws<SafetyException>(() => ImportHistory.Verify(batch, HistoryIdentity(), guard, new Operations(), CancellationToken.None, _ => {}));
                    Check(changed && File.ReadAllText(Path.Combine(folder, "foreign.keep")) == "retain replacement" && File.Exists(Path.Combine(folder + "-moved", "clip.mp4")), "Final callback accepted replacement or altered saved original");
                }
            });
            Test("cleanup terminal validation retains original root lease and exact prior removal count", () => {
                using (var f = new Fixture()) { f.Put(".DS_Store", "owned metadata"); bool changed = false; var counts = new CleanupCounts();
                    Throws<SafetyException>(() => MetadataCleaner.Clean(f.Source, () => { if (counts.Files == 1 && !changed) { changed = true; ReplaceFixtureDirectory(f, f.Source); File.WriteAllText(Path.Combine(f.Source, ".DS_Store"), "foreign metadata keep"); } }, CancellationToken.None, counts, _ => {}));
                    Check(changed && counts.Files == 1 && File.ReadAllText(Path.Combine(f.Source, ".DS_Store")) == "foreign metadata keep" && !File.Exists(Path.Combine(f.Source + "-moved", ".DS_Store")), "Final cleanup result accepted new root or lost exact partial count");
                }
            });
            Test("cleanup progress replacement stops after owned removal and retains replacement metadata", () => {
                using (var f = new Fixture()) { f.Put(@"child\.DS_Store", "owned metadata"); f.Put(@"child\clip.mp4", "owned recording"); string child = Path.Combine(f.Source, "child"); var counts = new CleanupCounts(); bool changed = false;
                    Throws<SafetyException>(() => MetadataCleaner.Clean(f.Source, () => {}, CancellationToken.None, counts, _ => { if (!changed) { changed = true; ReplaceFixtureDirectory(f, child); File.WriteAllText(Path.Combine(child, ".DS_Store"), "foreign metadata keep"); } }));
                    Check(changed && counts.Files == 1 && File.ReadAllText(Path.Combine(child, ".DS_Store")) == "foreign metadata keep" && File.ReadAllText(Path.Combine(child + "-moved", "clip.mp4")) == "owned recording", "Cleanup continued into changed progress folder");
                }
            });
        }
    }
}
