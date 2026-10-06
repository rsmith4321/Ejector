using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        sealed class EvidenceGuard : IImportGuard, IImportDeviceEvidence {
            internal readonly DestinationIdentity SourceEvidence = new DestinationIdentity { VolumeId = "fixture-volume", VolumeSerial = "source-serial", DiskInstance = "fixture-source-disk", DiskSerial = "source-disk-serial" };
            internal readonly DestinationIdentity DestinationEvidence = HistoryIdentity();
            public int[] ProtectedDisks { get { return new[] { 700, 701 }; } }
            public void Validate() {}
            public DestinationIdentity SourceDevice { get { return SourceEvidence; } }
            public DestinationIdentity DestinationDevice { get { return DestinationEvidence; } }
        }
        static RemovalIntent ReadRemovalIntent(string path) {
            using (var input = SafePaths.ReadFile(path)) return (RemovalIntent)new DataContractJsonSerializer(typeof(RemovalIntent)).ReadObject(input);
        }
        static string IntentPath(Fixture fixture) { return Directory.GetFiles(fixture.Destination, "removal-intent.json", System.IO.SearchOption.AllDirectories).Single(); }
        static void RemovalIntentTests() {
            Test("copy-only imports never publish a removal intent", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "Fixture original"); f.Run(x => {});
                Check(Directory.GetFiles(f.Destination, "removal-intent.json", SearchOption.AllDirectories).Length == 0 && File.Exists(Path.Combine(f.Source, "clip.mp4")), "Copy-only acquired removal evidence or removed an original");
            } });
            Test("durable intent binds the complete held batch before the first original disposition", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "Fixture original"); f.Put("fileinfo_list.list", "Fixture retained index"); bool observed = false; string planHash = null;
                var result = new VerifiedImporter(new Operations(), Native.FreeSpace, line => {
                    if (line.StartsWith("Durable removal intent")) {
                        observed = true; planHash = line.Split(' ')[5];
                        Check(File.Exists(Path.Combine(f.Source, "clip.mp4")) && File.Exists(Path.Combine(f.Source, "fileinfo_list.list")), "Intent was recorded after removal");
                        string planPath = IntentPath(f); Throws<System.ComponentModel.Win32Exception>(() => { using (SafePaths.ReadFile(planPath)) {} });
                    }
                }).Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, true, true, false, true));
                string intentPath = IntentPath(f); var intent = ReadRemovalIntent(intentPath);
                Check(observed && intent.Version == 1 && !intent.IsCompletionReceipt && intent.ImportId == result.Receipt.Id && intent.SourceVolume == "fixture-volume" && intent.SourceRoot == f.Source && intent.SelectedFolder == f.Source && intent.DestinationRoot == f.Destination && intent.BatchFolder == result.Folder && intent.DeleteOriginals && !intent.RecoverTrash && intent.Files.Count == 2, "Intent lost complete frozen scope or supplied completion authority");
                Check(intent.Files.Single(x => x.SourcePath.EndsWith("clip.mp4")).Eligible && !intent.Files.Single(x => x.SourcePath.EndsWith("fileinfo_list.list")).Eligible, "Retained dependency gained deletion eligibility");
                foreach (var entry in intent.Files) {
                    using (var copy = SafePaths.ReadFile(Path.Combine(result.Folder, entry.SavedRelativePath))) Check(entry.SavedIdentity.Matches(Native.Info(copy.SafeFileHandle)) && entry.Bytes == copy.Length && entry.Sha256 == VerifiedImporter.Hash(copy, CancellationToken.None), "Saved dependency evidence differs from held copy");
                }
                using (var input = SafePaths.ReadFile(intentPath)) Check(planHash == VerifiedImporter.Hash(input, CancellationToken.None), "Audit hash did not bind the flushed published intent");
            } });
            Test("intent keeps Windows payload and restoration-record links after a partial clear", delegate { using (var f = new Fixture()) using (var cancellation = new CancellationTokenSource()) {
                string payload = PutRecycle(f, "INTENT", @"E:\DCIM\clip.mp4", "Fixture trashed original"); string record = Path.Combine(Path.GetDirectoryName(payload), "$IINTENT");
                var error = Throws<OriginalRemovalFailure>(() => RecoverFixture(f, null, null, line => { if (line.StartsWith("Removed verified original") && line.EndsWith(payload)) cancellation.Cancel(); }, cancellation.Token));
                var intent = ReadRemovalIntent(IntentPath(f)); var media = intent.Files.Single(x => !x.IsTrashRecord); var metadata = intent.Files.Single(x => x.IsTrashRecord);
                Check(error.ClearedTrashFiles == 1 && error.ClearedTrashRecords == 0 && intent.RecoverTrash && !intent.DeleteOriginals && intent.WindowsTrashSid == FixtureTrashSid && !intent.IsCompletionReceipt && media.TrashRecordPath == record && metadata.TrashRecordPath == record && metadata.TrashPayloadPath == payload && media.Eligible && metadata.Eligible, "Interrupted record cannot be tied to its original verified payload and owner");
                Check(!File.Exists(payload) && File.Exists(record) && intent.Files.All(x => File.Exists(Path.Combine(intent.BatchFolder, x.SavedRelativePath))), "Incomplete operation lost its copies or record evidence"); NoRemovalCompletion(f);
            } });
            Test("failed durable intent audit prevents every original disposition and retains evidence", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "Fixture original"); var operations = new Operations();
                var failure = Throws<IOException>(() => new VerifiedImporter(operations, Native.FreeSpace, line => { if (line.StartsWith("Durable removal intent")) throw new IOException("Fixture intent audit failed"); }).Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, true, true, false, true)));
                Check(failure.Message == "Fixture intent audit failed" && File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == "Fixture original" && ReadRemovalIntent(IntentPath(f)).Files.Count == 1, "Intent failure removed sources or lost diagnosis");
                NoRemovalCompletion(f); using (operations.Reserve(new[] { 700, 701 })) {}
                Check(Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0, "Intent failure left private work");
            } });
            Test("cancellation at durable intent stops before clearing payload or restoration record", delegate { using (var f = new Fixture()) using (var cancellation = new CancellationTokenSource()) {
                string payload = PutRecycle(f, "CANCELINTENT", @"E:\clip.mp4", "Fixture original");
                Throws<OperationCanceledException>(() => RecoverFixture(f, null, null, line => { if (line.StartsWith("Durable removal intent")) cancellation.Cancel(); }, cancellation.Token));
                Check(File.Exists(payload) && Directory.GetFiles(Path.GetDirectoryName(payload)).Length == 2 && ReadRemovalIntent(IntentPath(f)).Files.Count == 2, "Cancel-after-intent removed payload/record"); NoRemovalCompletion(f);
            } });
            Test("published intent stays exclusively protected through payload and record clearing", delegate { using (var f = new Fixture()) {
                PutRecycle(f, "PINNEDINTENT", @"E:\clip.mp4", "Fixture original"); int denied = 0;
                var result = RecoverFixture(f, null, report => { if (report.StartsWith("Clearing verified Trash")) {
                    string path = IntentPath(f); Throws<IOException>(() => File.WriteAllText(path, "Fixture replacement")); Throws<IOException>(() => File.Delete(path)); denied++;
                } });
                Check(denied == 2 && result.ClearedTrashFiles == 1 && result.ClearedTrashRecords == 1 && ReadRemovalIntent(IntentPath(f)).Files.Count == 2, "Removal evidence was mutable while clearing");
            } });
            Test("exclusive intent publication refuses a conflicting record path and cleans only private work", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "Fixture original"); string conflict = null;
                Throws<System.ComponentModel.Win32Exception>(() => new VerifiedImporter(new Operations(), Native.FreeSpace, line => {
                    if (line.StartsWith("Batch verification complete")) {
                        string record = Path.GetDirectoryName(Directory.GetFiles(f.Destination, "import-audit.log", SearchOption.AllDirectories).Single()); conflict = Path.Combine(record, "removal-intent.json"); File.WriteAllText(conflict, "Existing fixture record");
                    }
                }).Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, x => {}, new MediaOptions(true, true, true, false, true)));
                Check(File.ReadAllText(conflict) == "Existing fixture record" && File.Exists(Path.Combine(f.Source, "clip.mp4")) && Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0, "Intent conflict overwrote prior data or removed an original"); NoRemovalCompletion(f);
            } });
            Test("partial intent is cleaned when device identity fails before publication", delegate { using (var f = new Fixture()) {
                string folder = Path.Combine(f.Destination, "record"); Directory.CreateDirectory(folder); var intent = new RemovalIntent(); int calls = 0;
                Throws<SafetyException>(() => { using (intent.Publish(folder, CancellationToken.None, () => { if (++calls == 2) throw new SafetyException("Fixture lost destination"); }, x => {})) {} });
                Check(Directory.GetFileSystemEntries(folder).Length == 0, "Guard failure published intent or retained a private partial");
            } });
            Test("removal intent freezes source and destination volume and physical disk evidence", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "Fixture original"); var guard = new EvidenceGuard();
                new VerifiedImporter(new Operations(), Native.FreeSpace, line => {
                    if (line.StartsWith("Durable removal intent")) { guard.SourceEvidence.DiskSerial = "Mutated fixture caller"; guard.DestinationEvidence.VolumeSerial = "Mutated fixture caller"; }
                }).Run(f.Source, f.Source, f.Destination, "fixture-volume", guard, CancellationToken.None, x => {}, new MediaOptions(true, true, true, false, true));
                var intent = ReadRemovalIntent(IntentPath(f));
                Check(intent.SourceDevice.VolumeId == "fixture-volume" && intent.SourceDevice.VolumeSerial == "source-serial" && intent.SourceDevice.DiskInstance == "fixture-source-disk" && intent.SourceDevice.DiskSerial == "source-disk-serial" && intent.DestinationDevice.Matches(HistoryIdentity()), "Caller changes corrupted persistent device evidence");
            } });
            Test("incomplete device evidence stops before intent publication and original removal", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "Fixture original"); var guard = new EvidenceGuard(); guard.DestinationEvidence.DiskInstance = "";
                Throws<SafetyException>(() => new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture-volume", guard, CancellationToken.None, x => {}, new MediaOptions(true, true, true, false, true)));
                Check(File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == "Fixture original" && Directory.GetFiles(f.Destination, "removal-intent.json", SearchOption.AllDirectories).Length == 0, "Invalid identity acquired removal evidence or changed originals"); NoRemovalCompletion(f);
            } });
        }
    }
}
