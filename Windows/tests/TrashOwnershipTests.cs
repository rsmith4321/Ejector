using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static void TrashOwnershipTests() {
            foreach (int callback in new[] { 1, 3 }) {
                int selected = callback;
                Test("Trash root replacement at " + (selected == 1 ? "initial" : "final") + " device callback refuses a successful empty catalog", () => { using (var f = new Fixture()) {
                    int calls = 0; bool changed = false;
                    Throws<SafetyException>(() => DeviceTrash.Read(f.Source, FixtureTrashSid, null, CancellationToken.None, () => { if (++calls == selected) { changed = true; ReplaceFixtureDirectory(f, f.Source); } }));
                    Check(changed && Directory.Exists(f.Source + "-moved") && File.ReadAllText(Path.Combine(f.Source, "foreign.keep")) == "retain replacement", "Root replacement was not exercised or its contents were changed");
                } });
            }
            foreach (bool nested in new[] { false, true }) {
                bool useNested = nested;
                Test("Trash selected Mac " + (useNested ? "nested payload" : "owner") + " replacement cannot broaden read-only recovery catalog", () => { using (var f = new Fixture()) {
                    string target = Path.Combine(f.Source, ".Trashes", "501"); if (useNested) target = Path.Combine(target, "recordings"); Directory.CreateDirectory(target); int calls = 0; bool changed = false;
                    Throws<SafetyException>(() => DeviceTrash.Read(f.Source, FixtureTrashSid, "501", CancellationToken.None, () => { if (++calls == (useNested ? 7 : 5)) { changed = true; ReplaceFixtureDirectory(f, target); File.WriteAllText(Path.Combine(target, "foreign.mp4"), "replacement recording"); } }));
                    Check(changed && Directory.Exists(target + "-moved") && File.ReadAllText(Path.Combine(target, "foreign.mp4")) == "replacement recording" && File.ReadAllText(Path.Combine(target, "foreign.keep")) == "retain replacement", "Selected Mac directory replacement was not exercised or mutated");
                } });
            }
            Test("Trash selected Windows owner replacement cannot admit fresh valid metadata and payload", () => { using (var f = new Fixture()) {
                string owner = Path.Combine(f.Source, "$RECYCLE.BIN", FixtureTrashSid); Directory.CreateDirectory(owner); int calls = 0; bool changed = false; string payload = null;
                Throws<SafetyException>(() => DeviceTrash.Read(f.Source, FixtureTrashSid, null, CancellationToken.None, () => { if (++calls == 4) { changed = true; ReplaceFixtureDirectory(f, owner); File.Delete(Path.Combine(owner, "foreign.keep")); payload = PutRecycle(f, "FOREIGN", @"E:\DCIM\foreign.mp4", "replacement Windows recording"); } }));
                Check(changed && File.ReadAllText(payload) == "replacement Windows recording" && File.Exists(Path.Combine(owner, "$IFOREIGN")) && Directory.Exists(owner + "-moved"), "Windows owner replacement was not exercised or its valid files were mutated");
            } });
            Test("Trash Windows folder payload replacement cannot admit different nested recordings", () => { using (var f = new Fixture()) {
                string owner = Path.Combine(f.Source, "$RECYCLE.BIN", FixtureTrashSid), payload = Path.Combine(owner, "$RFOLDER"); Directory.CreateDirectory(payload); File.WriteAllBytes(Path.Combine(owner, "$IFOLDER"), RecycleRecord(@"E:\DCIM", 0)); int calls = 0; bool changed = false;
                Throws<SafetyException>(() => DeviceTrash.Read(f.Source, FixtureTrashSid, null, CancellationToken.None, () => { if (++calls == 7) { changed = true; ReplaceFixtureDirectory(f, payload); File.WriteAllText(Path.Combine(payload, "foreign.mp4"), "replacement folder recording"); } }));
                Check(changed && Directory.Exists(payload + "-moved") && File.ReadAllText(Path.Combine(payload, "foreign.mp4")) == "replacement folder recording" && File.ReadAllText(Path.Combine(payload, "foreign.keep")) == "retain replacement" && File.Exists(Path.Combine(owner, "$IFOLDER")), "Windows folder replacement was not exercised or original record/replacement was changed");
            } });
            foreach (bool protectedRead in new[] { false, true }) {
                bool useFrozen = protectedRead;
                Test("Trash " + (useFrozen ? "protected manifest reread" : "final Windows owner callback") + " refuses byte-identical files in a replaced owner", () => { using (var f = new Fixture()) {
                    string payload = PutRecycle(f, "SAME", @"E:\DCIM\same.mp4", "unchanged verified recording"), owner = Path.GetDirectoryName(payload), metadata = Path.Combine(owner, "$ISAME"); byte[] payloadBytes = File.ReadAllBytes(payload), metadataBytes = File.ReadAllBytes(metadata);
                    var frozen = ReadTrash(f); var selected = frozen.Select(new MediaOptions(false, true)); int calls = 0; bool changed = false;
                    Action validate = () => { if (++calls == (useFrozen ? 4 : 8)) { changed = true; ReplaceFixtureDirectory(f, owner); File.Delete(Path.Combine(owner, "foreign.keep")); File.WriteAllBytes(payload, payloadBytes); File.WriteAllBytes(metadata, metadataBytes); } };
                    Throws<SafetyException>(() => { if (useFrozen) DeviceTrash.ReadCurrent(frozen, selected, _ => false, CancellationToken.None, validate); else DeviceTrash.Read(f.Source, FixtureTrashSid, null, CancellationToken.None, validate); });
                    Check(changed && File.ReadAllBytes(payload).SequenceEqual(payloadBytes) && File.ReadAllBytes(metadata).SequenceEqual(metadataBytes) && File.ReadAllBytes(Path.Combine(owner + "-moved", "$RSAME")).SequenceEqual(payloadBytes) && File.ReadAllBytes(Path.Combine(owner + "-moved", "$ISAME")).SequenceEqual(metadataBytes), "Protected/final owner replacement was not exercised or owned files changed");
                    // A fresh read is separate authority; failure must release every old directory lease.
                    var current = ReadTrash(f); Check(current.Files.Count == 1 && current.Records.Count == 1 && current.Files[0].ActualPath == payload, "Refused read leaked a lease or poisoned a later explicit catalog");
                } });
            }
            Test("Trash cancellation requested by final device callback cannot return an empty successful snapshot", () => { using (var f = new Fixture()) using (var stop = new CancellationTokenSource()) {
                int calls = 0; Throws<OperationCanceledException>(() => DeviceTrash.Read(f.Source, FixtureTrashSid, null, stop.Token, () => { if (++calls == 3) stop.Cancel(); }));
                Check(calls == 3 && Directory.GetFileSystemEntries(f.Source).Length == 0 && ReadTrash(f).Files.Count == 0, "Final cancellation was ignored, mutated the fixture or retained a lease");
            } });
        }
    }
}
