using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static string InterruptedRecovery(Fixture f) {
            f.Put("ordinary.mp4", "Fixture ordinary original"); f.Put("fileinfo_list.list", "Fixture retained index");
            string payload = PutRecycle(f, "RESUME", @"E:\DCIM\clip.mp4", "Fixture Trash original");
            using (var cancellation = new CancellationTokenSource()) {
                Throws<OriginalRemovalFailure>(() => new VerifiedImporter(new Operations(), Native.FreeSpace,
                    line => { if (line.StartsWith("Removed verified original") && line.EndsWith(payload)) cancellation.Cancel(); },
                    (root, uid, token, validate) => DeviceTrash.Read(root, FixtureTrashSid, uid, token, validate))
                    .Run(f.Source, f.Source, f.Destination, "fixture-volume", new EvidenceGuard(), cancellation.Token, x => {}, RecoverOptions()));
            }
            return Path.Combine(Path.GetDirectoryName(payload), "$IRESUME");
        }
        static RemovalResumeEvidence ReviewInterrupted(Fixture f, IImportGuard guard = null, string sid = FixtureTrashSid, Operations operations = null, CancellationToken token = default(CancellationToken)) {
            return RemovalResumeEvidence.Review(IntentPath(f), f.Source, f.Source, f.Destination, sid, guard ?? new EvidenceGuard(), operations ?? new Operations(), token);
        }
        static void RemovalResumeTests() {
            Test("read-only interrupted recovery review binds exact copies owners devices and manifests without clearing", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); byte[] before = File.ReadAllBytes(record); var operations = new Operations(); var evidence = ReviewInterrupted(f, operations: operations);
                Check(evidence.PendingWindowsRecords.Count == 1 && evidence.PendingWindowsRecords[0] == record && evidence.ImportId == ReadRemovalIntent(IntentPath(f)).ImportId && evidence.IntentSha256.Length == 64 && before.SequenceEqual(File.ReadAllBytes(record)) && File.ReadAllText(Path.Combine(f.Source, "ordinary.mp4")) == "Fixture ordinary original", "Review cleared a record, lost exact binding, or changed retained originals");
                NoRemovalCompletion(f); using (operations.Reserve(new[] { 700, 701 })) {}
                string copy = Directory.GetFiles(f.Destination, "clip.mp4", SearchOption.AllDirectories).Single(); File.AppendAllText(copy, "Fixture proves review released its handles");
            } });
            Test("changed complete saved dependency with restored timestamp refuses resume review", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); string copy = Directory.GetFiles(f.Destination, "ordinary.mp4", SearchOption.AllDirectories).Single(); DateTime time = File.GetLastWriteTimeUtc(copy);
                File.WriteAllText(copy, "Fixture ORDINARY original"); File.SetLastWriteTimeUtc(copy, time);
                Check(Throws<SafetyException>(() => ReviewInterrupted(f)).Message.Contains("saved dependency changed") && File.Exists(record), "Changed retained copy yielded resume evidence"); NoRemovalCompletion(f);
            } });
            Test("changed remaining Windows restoration record refuses resume review", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); DateTime time = File.GetLastWriteTimeUtc(record); byte[] data = File.ReadAllBytes(record); data[data.Length - 4] ^= 1; File.WriteAllBytes(record, data); File.SetLastWriteTimeUtc(record, time);
                Check(Throws<SafetyException>(() => ReviewInterrupted(f)).Message.Contains("remaining source") && File.Exists(record), "Edited restoration metadata retained authority"); NoRemovalCompletion(f);
            } });
            Test("changed source or destination identity refuses interrupted recovery review", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); var guard = new EvidenceGuard(); guard.SourceEvidence.VolumeSerial = "Changed fixture source";
                Check(Throws<SafetyException>(() => ReviewInterrupted(f, guard)).Message.Contains("device changed"), "Changed source identity accepted");
                guard = new EvidenceGuard(); guard.DestinationEvidence.DiskSerial = "Changed fixture destination";
                Check(Throws<SafetyException>(() => ReviewInterrupted(f, guard)).Message.Contains("device changed") && File.Exists(record), "Changed destination disk identity accepted");
            } });
            Test("different Windows owner and missing production identity evidence refuse resume review", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f);
                Check(Throws<SafetyException>(() => ReviewInterrupted(f, sid: "S-1-5-21-800-801-802-1002")).Message.Contains("current user"), "Another Windows owner accepted");
                Check(Throws<SafetyException>(() => ReviewInterrupted(f, new FakeGuard())).Message.Contains("fresh source/destination") && File.Exists(record), "Incomplete device evidence accepted");
            } });
            Test("modified journal without matching durable audit binding refuses resume review", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); string path = IntentPath(f); string journal = File.ReadAllText(path); File.WriteAllText(path, journal + " ");
                Check(Throws<SafetyException>(() => ReviewInterrupted(f)).Message.Contains("exact journal hash") && File.Exists(record), "Journal edit bypassed audit binding");
            } });
            Test("replacement at the cleared payload path refuses review and preserves replacement and record", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); string payload = Path.Combine(Path.GetDirectoryName(record), "$RRESUME"); File.WriteAllText(payload, "Fixture replacement");
                Throws<SafetyException>(() => ReviewInterrupted(f)); Check(File.ReadAllText(payload) == "Fixture replacement" && File.Exists(record), "Review removed a replacement or its record");
            } });
            Test("new selected ordinary or Trash recording invalidates the interrupted review manifest", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); f.Put("new.mp4", "Fixture new ordinary recording");
                Check(Throws<SafetyException>(() => ReviewInterrupted(f)).Message.Contains("ordinary source manifest"), "New ordinary recording ignored"); File.Delete(Path.Combine(f.Source, "new.mp4"));
                string added = PutRecycle(f, "NEWRESUME", @"E:\new.mp4", "Fixture new Trash recording");
                Throws<SafetyException>(() => ReviewInterrupted(f)); Check(File.Exists(added) && File.Exists(record), "Changed Trash selection was cleared");
            } });
            Test("unselected new photo does not broaden video-only interrupted review", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); f.Put("photo.JPG", "Fixture unselected ordinary photo"); string photo = PutRecycle(f, "PHOTOREVIEW", @"E:\photo.JPG", "Fixture unselected Trash photo");
                var evidence = ReviewInterrupted(f); Check(evidence.PendingWindowsRecords.Single() == record && File.Exists(photo) && File.Exists(record), "Unselected photo blocked or expanded video review");
            } });
            Test("unexplained orphan record refuses journal-bound review of an otherwise valid interruption", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); string orphan = Path.Combine(Path.GetDirectoryName(record), "$IUNEXPLAINED"); File.WriteAllBytes(orphan, RecycleRecord(@"E:\unknown.mp4", 10));
                Throws<SafetyException>(() => ReviewInterrupted(f)); Check(File.Exists(orphan) && File.Exists(record), "Arbitrary orphan acquired journal-bound authority");
            } });
            Test("completed batch and cancellation refuse interrupted review and release reservations", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); string completion = Path.Combine(Path.GetDirectoryName(IntentPath(f)), "verified-import.json"); File.WriteAllText(completion, "Fixture existing completion");
                Check(Throws<SafetyException>(() => ReviewInterrupted(f)).Message.Contains("completion receipt"), "Completed batch treated as interrupted"); File.Delete(completion);
                var operations = new Operations(); using (var cancellation = new CancellationTokenSource()) { cancellation.Cancel(); Throws<OperationCanceledException>(() => ReviewInterrupted(f, operations: operations, token: cancellation.Token)); }
                using (operations.Reserve(new[] { 700, 701 })) {} Check(File.Exists(record), "Cancelled review cleared remaining record"); NoRemovalCompletion(f);
            } });
            Test("redirected saved dependency refuses interrupted review without following its target", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); string copy = Directory.GetFiles(f.Destination, "clip.mp4", SearchOption.AllDirectories).Single(); string parent = Path.GetDirectoryName(copy), moved = Path.Combine(f.Root, "redirected-copy-fixture");
                Directory.Move(parent, moved); CreateJunction(f, parent, moved);
                Throws<SafetyException>(() => ReviewInterrupted(f)); Check(File.Exists(record) && File.ReadAllText(Path.Combine(moved, "clip.mp4")) == "Fixture Trash original", "Redirected copy followed or changed");
            } });
            Test("missing retained source index refuses interrupted review despite complete saved copies", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); File.Delete(Path.Combine(f.Source, "fileinfo_list.list"));
                Check(Throws<SafetyException>(() => ReviewInterrupted(f)).Message.Contains("retained source dependency is missing") && File.Exists(record), "Missing retained source dependency accepted");
            } });
            Test("byte-identical replacement saved copy with restored time refuses file identity reuse", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); string copy = Directory.GetFiles(f.Destination, "clip.mp4", SearchOption.AllDirectories).Single(); DateTime time = File.GetLastWriteTimeUtc(copy); byte[] bytes = File.ReadAllBytes(copy);
                File.Move(copy, Path.Combine(f.Root, "previous-saved-copy.mp4")); File.WriteAllBytes(copy, bytes); File.SetLastWriteTimeUtc(copy, time);
                Check(Throws<SafetyException>(() => ReviewInterrupted(f)).Message.Contains("saved dependency changed") && File.Exists(record), "Different saved file identity accepted through equal bytes/time");
            } });
            Test("byte-identical replacement restoration record refuses original file identity reuse", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); DateTime time = File.GetLastWriteTimeUtc(record); byte[] bytes = File.ReadAllBytes(record); File.Move(record, Path.Combine(f.Root, "previous-record.bin")); File.WriteAllBytes(record, bytes); File.SetLastWriteTimeUtc(record, time);
                Check(Throws<SafetyException>(() => ReviewInterrupted(f)).Message.Contains("remaining source") && File.Exists(record), "Different restoration-record identity accepted");
            } });
            Test("new still pairing changes video eligibility and refuses interrupted Windows record review", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); string photo = PutRecycle(f, "NEWPAIR", @"E:\DCIM\clip.JPG", "Fixture paired photo");
                Throws<SafetyException>(() => ReviewInterrupted(f)); Check(File.Exists(photo) && File.Exists(record), "New ambiguous still/movie pair retained stale clearing eligibility");
            } });
            Test("missing or duplicate journal hash binding refuses interrupted review", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); string audit = Path.Combine(Path.GetDirectoryName(IntentPath(f)), "import-audit.log"); string[] lines = File.ReadAllLines(audit); string binding = lines.Single(x => x.Contains("Durable removal intent | SHA256"));
                File.AppendAllText(audit, binding + Environment.NewLine); Check(Throws<SafetyException>(() => ReviewInterrupted(f)).Message.Contains("exact journal hash"), "Duplicate binding accepted");
                File.WriteAllLines(audit, lines.Where(x => !x.Contains("Durable removal intent | SHA256"))); Check(Throws<SafetyException>(() => ReviewInterrupted(f)).Message.Contains("exact journal hash") && File.Exists(record), "Missing binding accepted");
            } });
            Test("destination guard loss during resume review releases every held dependency and reservation", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); var operations = new Operations(); var guard = new EvidenceGuard(); int checks = 0; guard.OnValidate = () => { if (++checks == 3) throw new SafetyException("Fixture lost destination during review"); };
                Check(Throws<SafetyException>(() => ReviewInterrupted(f, guard, operations: operations)).Message.Contains("lost destination"), "Device loss did not stop review");
                using (operations.Reserve(new[] { 700, 701 })) {} File.AppendAllText(record, "Fixture proves source handle released"); File.AppendAllText(IntentPath(f), " ");
                Check(checks == 3, "Review continued after device loss"); NoRemovalCompletion(f);
            } });
            Test("busy source or destination reservation refuses interrupted review without clearing", delegate { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f); var operations = new Operations(); using (operations.Reserve(new[] { 701 })) { Throws<SafetyException>(() => ReviewInterrupted(f, operations: operations)); }
                Check(File.Exists(record) && ReviewInterrupted(f, operations: operations).PendingWindowsRecords.Single() == record, "Busy refusal changed data or retained reservation");
            } });
        }
    }
}
