using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace EasyEject {
    public static partial class Tests {
        static CompletedBatch HistoryNext(CompletedBatch batch) {
            var next = ImportHistory.Clone(batch); next.Id = Guid.NewGuid().ToString("N");
            next.ReceiptPath = Path.Combine(next.Folder, ".easy-eject-receipts", next.Id, "verified-import.json"); return next;
        }
        static void HistoryPublicationTests() {
            Test("history publication refuses changed bytes after its loaded snapshot", () => {
                using (var f = new Fixture()) {
                    f.Put("clip.mov", "generated original"); var first = Batch(f, f.Run(_ => {})); string root = Path.Combine(f.Root, "history"); ImportHistory.Add(first, root);
                    string path = Path.Combine(root, "completed-imports.json"); byte[] original = File.ReadAllBytes(path), changed = null; DateTime timestamp = File.GetLastWriteTimeUtc(path);
                    Throws<SafetyException>(() => ImportHistory.Add(HistoryNext(first), root, stage => {
                        var external = ImportHistory.Load(root); external[0].DeviceName = "Changed camera";
                        changed = ReplaySerialize(external, typeof(List<CompletedBatch>)); Check(changed.Length == original.Length, "Fixture edit changed byte length");
                        File.WriteAllBytes(path, changed); File.SetLastWriteTimeUtc(path, timestamp);
                    }));
                    Check(changed != null && File.ReadAllBytes(path).SequenceEqual(changed) && File.GetLastWriteTimeUtc(path) == timestamp && Directory.GetFiles(root).Length == 1, "Changed history was overwritten or private work leaked");
                    ImportHistory.Add(HistoryNext(first), root); Check(ImportHistory.Load(root).Count == 2 && ImportHistory.Load(root)[1].DeviceName == "Changed camera", "Fresh explicit append lost retained history or writer remained held");
                }
            });
            Test("history publication refuses a record appearing after an absent snapshot", () => {
                using (var f = new Fixture()) {
                    f.Put("clip.mov", "generated original"); var first = Batch(f, f.Run(_ => {})); string root = Path.Combine(f.Root, "history"), path = Path.Combine(root, "completed-imports.json");
                    byte[] appeared = ReplaySerialize(new List<CompletedBatch> { first }, typeof(List<CompletedBatch>));
                    Throws<SafetyException>(() => ImportHistory.Add(HistoryNext(first), root, stage => File.WriteAllBytes(path, appeared)));
                    Check(File.ReadAllBytes(path).SequenceEqual(appeared) && Directory.GetFiles(root).Length == 1, "Appearing history was overwritten or private work leaked");
                    ImportHistory.Add(HistoryNext(first), root); Check(ImportHistory.Load(root).Count == 2, "Fresh append after appearing-file refusal failed");
                }
            });
            Test("history publication refuses deletion after a present snapshot", () => {
                using (var f = new Fixture()) {
                    f.Put("clip.mov", "generated original"); var first = Batch(f, f.Run(_ => {})); string root = Path.Combine(f.Root, "history"), path = Path.Combine(root, "completed-imports.json"); ImportHistory.Add(first, root);
                    Throws<SafetyException>(() => ImportHistory.Add(HistoryNext(first), root, stage => File.Delete(path)));
                    Check(!File.Exists(path) && Directory.GetFiles(root).Length == 0 && File.Exists(first.ReceiptPath) && File.Exists(first.Originals[0].Path), "Deleted history was recreated or verified saved bytes changed");
                    ImportHistory.Add(first, root); Check(ImportHistory.Load(root).Single().Id == first.Id, "Fresh append could not release the refused writer");
                }
            });
            Test("history publication refuses byte-identical native file replacement", () => {
                using (var f = new Fixture()) {
                    f.Put("clip.mov", "generated original"); var first = Batch(f, f.Run(_ => {})); string root = Path.Combine(f.Root, "history"), path = Path.Combine(root, "completed-imports.json"); ImportHistory.Add(first, root); byte[] original = File.ReadAllBytes(path);
                    // Keep the unlinked old file alive so its native identity cannot be recycled.
                    using (var old = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                        Throws<SafetyException>(() => ImportHistory.Add(HistoryNext(first), root, stage => { File.Delete(path); File.WriteAllBytes(path, original); }));
                        Check(File.ReadAllBytes(path).SequenceEqual(original) && old.Length == original.Length, "Identical replacement was overwritten or unlinked original changed");
                    }
                    ImportHistory.Add(HistoryNext(first), root); Check(ImportHistory.Load(root).Count == 2 && Directory.GetFiles(root).Length == 1, "Fresh append failed or temporary/writer leaked");
                }
            });
            Test("history owns shared configuration writer before reading and appending", () => {
                using (var f = new Fixture()) {
                    f.Put("clip.mov", "generated original"); var first = Batch(f, f.Run(_ => {})); string root = Path.Combine(f.Root, "history"); ImportHistory.Add(first, root); bool blocked = false;
                    ImportHistory.Add(HistoryNext(first), root, stage => {
                        try { Profiles.WriteJson(new List<CompletedBatch> { HistoryNext(first), first }, typeof(List<CompletedBatch>), root, "completed-imports.json"); }
                        catch (Win32Exception ex) { Check((ex.NativeErrorCode == 5 || ex.NativeErrorCode == 32) && ex.Message.Contains("Saved configuration is being changed"), "Competing writer failed for an unrelated native reason: " + ex.NativeErrorCode); blocked = true; }
                    });
                    Check(blocked && ImportHistory.Load(root).Count == 2 && Directory.GetFiles(root).Length == 1, "Loaded history did not own the shared gate; competing publication could be lost");
                    Profiles.WriteJson(ImportHistory.Load(root), typeof(List<CompletedBatch>), root, "completed-imports.json");
                }
            });
            Test("history writer prevents settings-root movement across its loaded snapshot", () => {
                using (var f = new Fixture()) {
                    f.Put("clip.mov", "generated original"); var first = Batch(f, f.Run(_ => {})); string root = Path.Combine(f.Root, "history"); ImportHistory.Add(first, root); bool blocked = false;
                    ImportHistory.Add(HistoryNext(first), root, stage => { try { ReplaceFixtureDirectory(f, root); } catch (IOException) { blocked = true; } });
                    Check(blocked && !Directory.Exists(root + "-moved") && !File.Exists(Path.Combine(root, "foreign.keep")) && ImportHistory.Load(root).Count == 2, "History published into a replacement settings root");
                    Check(File.Exists(first.ReceiptPath) && File.ReadAllText(first.Originals[0].Path) == "generated original", "History root check changed saved media/receipt");
                }
            });
            Test("history null append neither creates storage nor invokes callbacks", () => {
                using (var f = new Fixture()) {
                    string root = Path.Combine(f.Root, "absent-history"); int calls = 0; ImportHistory.Add(null, root, stage => calls++);
                    Check(calls == 0 && !Directory.Exists(root), "Index-only history created storage or entered publication");
                }
            });
            Test("history append refuses a case-equivalent batch identity without corrupting the record", () => {
                using (var f = new Fixture()) {
                    f.Put("clip.mov", "generated original"); var first = Batch(f, f.Run(_ => {}));
                    first.Id = "0123456789abcdef0123456789abcdef";
                    first.ReceiptPath = Path.Combine(first.Folder, ".easy-eject-receipts", first.Id, "verified-import.json");
                    string root = Path.Combine(f.Root, "history"), path = Path.Combine(root, "completed-imports.json"); ImportHistory.Add(first, root); byte[] saved = File.ReadAllBytes(path);
                    var duplicate = ImportHistory.Clone(first); duplicate.Id = duplicate.Id.ToUpperInvariant(); duplicate.ReceiptPath = Path.Combine(duplicate.Folder, ".easy-eject-receipts", duplicate.Id, "verified-import.json");
                    Throws<SafetyException>(() => ImportHistory.Add(duplicate, root));
                    Check(File.ReadAllBytes(path).SequenceEqual(saved) && ImportHistory.Load(root).Single().Id == first.Id, "Case-equivalent append corrupted valid saved history");
                    ImportHistory.Add(HistoryNext(first), root); Check(ImportHistory.Load(root).Count == 2 && Directory.GetFiles(root).Length == 1, "Duplicate refusal leaked writer or prevented a distinct later append");
                }
            });
        }
    }
}
