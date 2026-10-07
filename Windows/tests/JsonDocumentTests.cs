using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static byte[] JsonSuffix(byte[] bytes, string suffix) { return bytes.Concat(Encoding.UTF8.GetBytes(suffix)).ToArray(); }
        static void RefuseJsonRecord(string name, byte[] bytes, Action<string> read, byte[] valid) {
            using (var f = new Fixture()) {
                string path = Path.Combine(f.Destination, name); File.WriteAllBytes(path, bytes);
                Throws<SerializationException>(() => read(f.Destination));
                Check(File.ReadAllBytes(path).SequenceEqual(bytes) && Directory.GetFiles(f.Destination).Length == 1 && !Directory.GetFiles(f.Source).Any(), "Refused JSON changed records or wrote defaults");
                File.WriteAllBytes(path, valid); read(f.Destination);
                Check(File.ReadAllBytes(path).SequenceEqual(valid) && Directory.GetFiles(f.Destination).Length == 1, "Fresh complete record retained a read lease or changed data");
            }
        }
        sealed class TrailingProbeProcess : IQuickShareProcess {
            internal readonly FakeShareProcess Inner = new FakeShareProcess(); internal bool Output;
            public byte[] Run(string executable, string[] args, string directory, double timeout, CancellationToken cancel, Action validate, Action<string> progress = null) {
                byte[] value = Inner.Run(executable, args, directory, timeout, cancel, validate, progress);
                return args.Contains("-show_streams") && (args.Last() == Inner.Temporary) == Output ? JsonSuffix(value, "{}") : value;
            }
        }
        static void RefuseTrailingProbe(bool output) {
            using (var f = new Fixture()) {
                var batch = LunaBatch(f); var process = new TrailingProbeProcess { Output = output }; var operations = new Operations();
                Throws<QuickShareFailure>(() => new QuickShareEngine(process, operations).Run(batch, HistoryIdentity(), new FakeGuard(), FixtureShareTools(f), QuickSharePreset.Hd, QuickShareColor.Standard, CancellationToken.None, _ => {}));
                Check(process.Inner.Encodes == (output ? 1 : 0) && process.Inner.Decodes == 0 && !Directory.GetFiles(batch.Folder, "*-sharing-*.mp4", SearchOption.AllDirectories).Any(), "Trailing probe data authorized encoding/decode/publication");
                VerifyShareOriginals(batch); CheckSharePrivateGone(batch); using (operations.Reserve(new[] { 700, 701 })) {}
                var retry = RunShare(f, batch, new FakeShareProcess(), operations); Check(retry.Created == 1 && retry.Reused == 0, "Fresh manual retry retained failed probe work/reservations");
            }
        }
        static void JsonDocumentTests() {
            Test("settings refuse concatenated objects instead of combining cleanup choices", () => RefuseJsonRecord("app-settings.json", Encoding.UTF8.GetBytes("{\"ShowNotifications\":false}{\"CleanCardsOnEject\":true}"), root => AppSettings.Load(root), ReplaySerialize(new AppSettings(), typeof(AppSettings))));
            Test("issue state refuses a second object that dismisses the first notice", () => {
                var issue = new AppIssue(); issue.Record("Generated issue requiring review"); byte[] valid = ReplaySerialize(issue, typeof(AppIssue));
                // Omit Dismissed in the first object so the legacy decoder can combine it.
                byte[] corrupt = Encoding.UTF8.GetBytes("{\"Message\":\"Generated issue requiring review\",\"ObservedUtc\":\"" + issue.ObservedUtc + "\"}{\"Dismissed\":true}");
                RefuseJsonRecord("issue-state.json", corrupt, root => AppIssue.Load(root), valid);
            });
            Test("import defaults refuse a second automatic removal and recovery object", () => RefuseJsonRecord("import-defaults.json", Encoding.UTF8.GetBytes("{\"VideosOnly\":true}{\"AutomaticImport\":true,\"DeleteOriginals\":true,\"RecoverTrash\":true}"), root => ImportDefaults.Load(root), ReplaySerialize(new ImportDefaults(), typeof(ImportDefaults))));
            Test("connected profile rejects trailing document without enrolling or changing choices", () => {
                var state = State(); byte[] valid = ReplaySerialize(OfflineProfile(), typeof(ImportProfile));
                RefuseJsonRecord(Profiles.FileName(state.Volumes[0].Id), JsonSuffix(valid, "{}"), root => Profiles.Load(state.Volumes[0], state.Disks[0], root), valid);
            });
            Test("saved profile catalog reports concatenated JSON as corrupt and retains exact bytes", () => { using (var f = new Fixture()) {
                var profile = OfflineProfile(); string path = Path.Combine(f.Destination, Profiles.FileName(profile.VolumeId)); byte[] valid = ReplaySerialize(profile, typeof(ImportProfile)), corrupt = JsonSuffix(valid, "null"); File.WriteAllBytes(path, corrupt);
                var entry = Profiles.Catalog(f.Destination).Single(); Check(!entry.CanEdit && !String.IsNullOrWhiteSpace(entry.Error), "Trailing profile document remained editable");
                Check(File.ReadAllBytes(path).SequenceEqual(corrupt), "Catalog rewrote corrupt profile"); File.WriteAllBytes(path, valid); Check(Profiles.Catalog(f.Destination).Single().CanEdit, "Fresh catalog retained malformed record/lease");
            } });
            Test("connected review retains raw malformed JSON for explicit unchanged re-enrollment", () => { using (var f = new Fixture()) {
                var state = State(); var profile = OfflineProfile(); string path = Path.Combine(f.Destination, Profiles.FileName(profile.VolumeId)); byte[] corrupt = JsonSuffix(ReplaySerialize(profile, typeof(ImportProfile)), "{}"); File.WriteAllBytes(path, corrupt);
                var review = Profiles.ReviewConnected(state.Volumes[0], state.Disks[0], f.Destination);
                Check(review.Exists && review.Value == null && !String.IsNullOrWhiteSpace(review.Error) && File.ReadAllBytes(path).SequenceEqual(corrupt), "Connected review accepted partial choices or discarded raw evidence");
                profile.Name = "Explicit generated re-enrollment"; Profiles.SaveConnected(review, profile, f.Destination);
                Check(Profiles.ReviewConnected(state.Volumes[0], state.Disks[0], f.Destination).Value.Name == profile.Name, "Explicit exact corrupt-record re-enrollment refused or retained old leases");
            } });
            Test("Quick Share setup rejects trailing JSON and retains selected record", () => {
                byte[] valid = ReplaySerialize(new QuickShareToolSettings(), typeof(QuickShareToolSettings)); RefuseJsonRecord(QuickShareConfiguration.FileName, JsonSuffix(valid, "{}"), root => QuickShareConfiguration.Load(root), valid);
            });
            Test("completed history rejects a second document and retains exact verified batches", () => { using (var f = new Fixture()) {
                f.Put("generated.mp4", "Generated history original"); var batch = Batch(f, f.Run(_ => {})); string root = Path.Combine(f.Root, "history"); ImportHistory.Add(batch, root);
                string path = Path.Combine(root, "completed-imports.json"); byte[] valid = File.ReadAllBytes(path), corrupt = JsonSuffix(valid, "null"); File.WriteAllBytes(path, corrupt);
                Throws<SerializationException>(() => ImportHistory.Load(root)); Check(File.ReadAllBytes(path).SequenceEqual(corrupt), "History repaired/discarded malformed bytes");
                File.WriteAllBytes(path, valid); Check(ImportHistory.Load(root).Single().Id == batch.Id, "Fresh complete history lost exact batch");
            } });
            Test("import receipt refuses a concatenated object before exposing saved manifest", () => { using (var f = new Fixture()) {
                f.Put("generated.mp4", "Generated receipt original"); var result = f.Run(_ => {}); byte[] valid = File.ReadAllBytes(result.ReceiptPath), corrupt = JsonSuffix(valid, "{}"); File.WriteAllBytes(result.ReceiptPath, corrupt);
                string hash; Throws<SerializationException>(() => ImportHistory.ReadReceipt(result.ReceiptPath, out hash)); Check(File.ReadAllBytes(result.ReceiptPath).SequenceEqual(corrupt) && File.ReadAllText(Path.Combine(f.Source, "generated.mp4")) == "Generated receipt original", "Malformed receipt changed source or receipt");
                File.WriteAllBytes(result.ReceiptPath, valid); Check(ImportHistory.ReadReceipt(result.ReceiptPath, out hash).Id == result.Receipt.Id, "Fresh receipt retained refused lease");
            } });
            Test("sharing does not reuse a valid output backed by a concatenated receipt", () => { using (var f = new Fixture()) {
                var batch = LunaBatch(f); var first = RunShare(f, batch, new FakeShareProcess()); string output = first.Outputs.Single(), record = QuickShareEngine.ReceiptPath(output); byte[] saved = File.ReadAllBytes(output), corrupt = JsonSuffix(File.ReadAllBytes(record), "{}"); File.WriteAllBytes(record, corrupt);
                var process = new FakeShareProcess(); var retry = RunShare(f, batch, process);
                Check(retry.Created == 1 && retry.Reused == 0 && retry.Outputs.Single() != output && process.Encodes == 1 && process.Decodes == 1 && File.ReadAllBytes(output).SequenceEqual(saved) && File.ReadAllBytes(record).SequenceEqual(corrupt), "Malformed sharing receipt authorized reuse or replaced older evidence");
                VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
            } });
            Test("interrupted removal refuses concatenated journal even with its exact audit hash binding", () => { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f), path = IntentPath(f), audit = Path.Combine(Path.GetDirectoryName(path), "import-audit.log");
                byte[] pending = File.ReadAllBytes(record), valid = File.ReadAllBytes(path), corrupt = JsonSuffix(valid, "{}"); string oldHash, newHash;
                using (var input = new MemoryStream(valid)) oldHash = VerifiedImporter.Hash(input, CancellationToken.None);
                using (var input = new MemoryStream(corrupt)) newHash = VerifiedImporter.Hash(input, CancellationToken.None);
                string validAudit = File.ReadAllText(audit), binding = "Durable removal intent | SHA256 " + oldHash + " | " + path + " | no completion or eject authority";
                Check(validAudit.Split(new[] { binding }, StringSplitOptions.None).Length == 2, "Generated audit does not bind exactly one intent");
                string corruptAudit = validAudit.Replace(binding, binding.Replace(oldHash, newHash)); File.WriteAllBytes(path, corrupt); File.WriteAllText(audit, corruptAudit);
                int callbacks = 0; var guard = new EvidenceGuard { OnValidate = () => callbacks++ }; var operations = new Operations();
                Throws<SerializationException>(() => ReviewInterrupted(f, guard: guard, operations: operations));
                Check(callbacks == 0 && File.ReadAllBytes(path).SequenceEqual(corrupt) && File.ReadAllText(audit) == corruptAudit && File.ReadAllBytes(record).SequenceEqual(pending), "Malformed intent reached device callbacks or changed retained evidence/Trash record");
                NoRemovalCompletion(f); using (operations.Reserve(new[] { 700, 701 })) {}
                File.WriteAllBytes(path, valid); File.WriteAllText(audit, validAudit); Check(ReviewInterrupted(f, operations: operations).PendingWindowsRecords.Single() == record && File.ReadAllBytes(record).SequenceEqual(pending), "Fresh exact journal retained refusal resources or cleared a record");
            } });
            Test("manual sharing refuses concatenated original probe before encoding and permits fresh retry", () => RefuseTrailingProbe(false));
            Test("manual sharing refuses concatenated output probe before decode or publication and permits fresh retry", () => RefuseTrailingProbe(true));
            Test("complete nullable legacy settings preserve whitespace UTF8 and BOM-less UTF16 encodings", () => { using (var f = new Fixture()) {
                string json = " \t\r\n{\"ShowNotifications\":null,\"EnableShortcut\":null,\"Future\":{\"values\":[null,true,{}],\"text\":\"Luna α } ] \\\" \\\\ {\"}}\r\n\t "; string path = Path.Combine(f.Destination, "app-settings.json");
                foreach (var encoding in new Encoding[] { new UTF8Encoding(false), new UnicodeEncoding(false, false), new UnicodeEncoding(true, false) }) {
                    byte[] bytes = encoding.GetBytes(json); File.WriteAllBytes(path, bytes); var settings = AppSettings.Load(f.Destination);
                    Check(settings.NotificationsEnabled && !settings.ShortcutEnabled && !settings.CleanupCardsEnabled && File.ReadAllBytes(path).SequenceEqual(bytes), "Complete nullable/unknown/escaped settings changed legacy choices or encoding");
                }
                foreach (string rejected in new[] { "null", " \r\nnull\t " }) {
                    byte[] bytes = Encoding.UTF8.GetBytes(rejected); File.WriteAllBytes(path, bytes); Throws<SafetyException>(() => AppSettings.Load(f.Destination));
                    Check(File.ReadAllBytes(path).SequenceEqual(bytes), "Null-record refusal changed its existing exception contract or bytes");
                }
            } });
            Test("complete profile with unknown nested fields preserves nullable custom and manual sharing choices", () => { using (var f = new Fixture()) {
                var state = State(); var profile = OfflineProfile(); profile.UsesImportDefaults = profile.VideosOnly = null; profile.SharingPreset = QuickShareProfile.Uhd; profile.SharingColor = QuickShareProfile.LunaILog;
                string text = Encoding.UTF8.GetString(ReplaySerialize(profile, typeof(ImportProfile))); text = "\r\n" + text.Substring(0, text.Length - 1) + ",\"Future\":{\"note\":\"escaped } \\\" α\",\"values\":[{},null]}}\t "; byte[] bytes = Encoding.UTF8.GetBytes(text); string path = Path.Combine(f.Destination, Profiles.FileName(profile.VolumeId)); File.WriteAllBytes(path, bytes);
                var loaded = Profiles.Load(state.Volumes[0], state.Disks[0], f.Destination); var saved = Profiles.Catalog(f.Destination).Single(); var review = Profiles.ReviewConnected(state.Volumes[0], state.Disks[0], f.Destination);
                Check(loaded.UsesImportDefaults == null && loaded.VideosOnly == null && loaded.SharingPreset == profile.SharingPreset && loaded.SharingColor == profile.SharingColor && saved.CanEdit && review.Value.SharingPreset == profile.SharingPreset && File.ReadAllBytes(path).SequenceEqual(bytes), "Complete legacy profile lost unknown/nullable/manual choices or changed raw bytes");
            } });
            Test("complete probe with nested side data and trailing whitespace retains exact media dimensions", () => {
                byte[] bytes = JsonSuffix(ReplaySerialize(ShareInput(), typeof(QuickShareProbe)), "\r\n\t "); var probe = QuickShareProbe.Parse(bytes);
                Check(probe.Video.Width == 3840 && probe.Video.Height == 2160 && probe.Audio != null && SharePlan(probe).Width == 1920, "Complete probe lost media/recipe fields");
            });
        }
    }
}
