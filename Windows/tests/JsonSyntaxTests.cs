using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static byte[] JsonWithFuture(byte[] valid, string future) {
            string text = Encoding.UTF8.GetString(valid); Check(text.EndsWith("}", StringComparison.Ordinal), "Expected generated JSON object");
            return Encoding.UTF8.GetBytes(text.Substring(0, text.Length - 1) + ",\"Future\":" + future + "}");
        }
        sealed class InvalidSyntaxProbeProcess : IQuickShareProcess {
            internal readonly FakeShareProcess Inner = new FakeShareProcess(); internal bool Output;
            public byte[] Run(string executable, string[] args, string directory, double timeout, CancellationToken cancel, Action validate, Action<string> progress = null) {
                byte[] value = Inner.Run(executable, args, directory, timeout, cancel, validate, progress);
                if (!args.Contains("-show_streams") || (args.Last() == Inner.Temporary) != Output) return value;
                return JsonWithFuture(value, "[1 2]");
            }
        }
        static void RefuseInvalidSyntaxProbe(bool output) { using (var f = new Fixture()) {
            var batch = LunaBatch(f); var process = new InvalidSyntaxProbeProcess { Output = output }; var operations = new Operations();
            Throws<QuickShareFailure>(() => new QuickShareEngine(process, operations).Run(batch, HistoryIdentity(), new FakeGuard(), FixtureShareTools(f), QuickSharePreset.Hd, QuickShareColor.Standard, CancellationToken.None, _ => {}));
            Check(process.Inner.Encodes == (output ? 1 : 0) && process.Inner.Decodes == 0 && !Directory.GetFiles(batch.Folder, "*-sharing-*.mp4", SearchOption.AllDirectories).Any(), "Invalid probe syntax reached encoding/decode/publication");
            VerifyShareOriginals(batch); CheckSharePrivateGone(batch); using (operations.Reserve(new[] { 700, 701 })) {}
            Check(RunShare(f, batch, new FakeShareProcess(), operations).Created == 1, "Fresh manual retry retained failed probe work/resources");
        } }
        static void RefusePackageSyntax(Func<string, string> corrupt) { using (var f = new WindowsPackageFixture()) {
            string text = corrupt(f.ManifestText()); f.Publish(text); string body = Path.Combine(f.Root, WindowsReleaseManifest.ManifestName), signature = Path.Combine(f.Root, WindowsReleaseManifest.SignatureName);
            byte[] before = File.ReadAllBytes(body), signed = File.ReadAllBytes(signature);
            Throws<SafetyException>(() => { using (var review = f.Review()) {} });
            Check(File.ReadAllBytes(body).SequenceEqual(before) && File.ReadAllBytes(signature).SequenceEqual(signed), "Malformed package syntax rewrote authenticated evidence");
            f.Publish(); using (var fresh = f.Review()) Check(fresh.Release.Version == "0.1.0.0", "Fresh exact signed fixture retained refused leases");
        } }
        static void JsonSyntaxTests() {
            Test("saved cleanup settings reject a trailing object comma without rewriting preferences", () => RefuseJsonRecord("app-settings.json", Encoding.UTF8.GetBytes("{\"CleanCardsOnEject\":true,}"), root => AppSettings.Load(root), ReplaySerialize(new AppSettings(), typeof(AppSettings))));
            Test("saved settings reject missing separators inside unknown nested arrays", () => {
                byte[] valid = ReplaySerialize(new AppSettings { CleanCardsOnEject = true }, typeof(AppSettings));
                foreach (string future in new[] { "[1 2]", "{\"nested\":[true false]}" }) RefuseJsonRecord("app-settings.json", JsonWithFuture(valid, future), root => AppSettings.Load(root), valid);
            });
            Test("automatic removal defaults reject malformed numeric extension tokens", () => {
                byte[] valid = ReplaySerialize(new ImportDefaults { AutomaticImport = true, DeleteOriginals = true, RecoverTrash = true }, typeof(ImportDefaults));
                foreach (string number in new[] { "01", "-01", "1.", "1.e2", "1e", "1e+", "+1", ".5" }) RefuseJsonRecord("import-defaults.json", JsonWithFuture(valid, number), root => ImportDefaults.Load(root), valid);
            });
            Test("connected catalog and raw profile review reject malformed nested metadata and preserve enrollment", () => { using (var f = new Fixture()) {
                var state = State(); var profile = OfflineProfile(); profile.SharingPreset = QuickShareProfile.Uhd; string path = Path.Combine(f.Destination, Profiles.FileName(profile.VolumeId));
                byte[] valid = ReplaySerialize(profile, typeof(ImportProfile)), corrupt = JsonWithFuture(valid, "[1 2]"); File.WriteAllBytes(path, corrupt);
                Throws<SerializationException>(() => Profiles.Load(state.Volumes[0], state.Disks[0], f.Destination)); Check(!Profiles.Catalog(f.Destination).Single().CanEdit, "Malformed profile remained editable");
                var review = Profiles.ReviewConnected(state.Volumes[0], state.Disks[0], f.Destination); Check(review.Exists && review.Value == null && !String.IsNullOrWhiteSpace(review.Error) && File.ReadAllBytes(path).SequenceEqual(corrupt), "Raw profile review accepted partial choices or discarded evidence");
                Profiles.SaveConnected(review, profile, f.Destination); Check(Profiles.Load(state.Volumes[0], state.Disks[0], f.Destination).SharingPreset == profile.SharingPreset, "Fresh explicit corrupt re-enrollment lost manual sharing choices");
            } });
            Test("tool setup rejects invalid nested JSON without discarding the selected record", () => {
                byte[] valid = ReplaySerialize(new QuickShareToolSettings(), typeof(QuickShareToolSettings)); RefuseJsonRecord(QuickShareConfiguration.FileName, JsonWithFuture(valid, "01"), root => QuickShareConfiguration.Load(root), valid);
            });
            Test("exact completed history rejects a trailing array comma and permits fresh unchanged retry", () => { using (var f = new Fixture()) {
                f.Put("generated.mp4", "Generated history original"); var batch = Batch(f, f.Run(_ => {})); string root = Path.Combine(f.Root, "history"); ImportHistory.Add(batch, root);
                string path = Path.Combine(root, "completed-imports.json"), text = File.ReadAllText(path); byte[] valid = File.ReadAllBytes(path), corrupt = Encoding.UTF8.GetBytes(text.Substring(0, text.Length - 1) + ",]"); File.WriteAllBytes(path, corrupt);
                Throws<SerializationException>(() => ImportHistory.Load(root)); Check(File.ReadAllBytes(path).SequenceEqual(corrupt), "Malformed history was reset or rewritten");
                File.WriteAllBytes(path, valid); Check(ImportHistory.Load(root).Single().Id == batch.Id, "Fresh complete history lost exact batch or retained refusal resources");
            } });
            Test("sharing refuses reuse through malformed receipt content and preserves the prior output", () => { using (var f = new Fixture()) {
                var batch = LunaBatch(f); var first = RunShare(f, batch, new FakeShareProcess()); string output = first.Outputs.Single(), path = QuickShareEngine.ReceiptPath(output); byte[] saved = File.ReadAllBytes(output), corrupt = JsonWithFuture(File.ReadAllBytes(path), "[1 2]"); File.WriteAllBytes(path, corrupt);
                var process = new FakeShareProcess(); var retry = RunShare(f, batch, process);
                Check(retry.Created == 1 && retry.Reused == 0 && retry.Outputs.Single() != output && process.Encodes == 1 && process.Decodes == 1 && File.ReadAllBytes(path).SequenceEqual(corrupt) && File.ReadAllBytes(output).SequenceEqual(saved), "Malformed content authorized reuse or replaced retained evidence"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
            } });
            Test("interrupted removal rejects malformed journal content despite exact audit binding before device callbacks", () => { using (var f = new Fixture()) {
                string record = InterruptedRecovery(f), path = IntentPath(f), audit = Path.Combine(Path.GetDirectoryName(path), "import-audit.log"); byte[] pending = File.ReadAllBytes(record), valid = File.ReadAllBytes(path), corrupt = JsonWithFuture(valid, "[1 2]"); string oldHash, newHash;
                using (var input = new MemoryStream(valid)) oldHash = VerifiedImporter.Hash(input, CancellationToken.None); using (var input = new MemoryStream(corrupt)) newHash = VerifiedImporter.Hash(input, CancellationToken.None);
                string validAudit = File.ReadAllText(audit), binding = "Durable removal intent | SHA256 " + oldHash + " | " + path + " | no completion or eject authority"; Check(validAudit.Split(new[] { binding }, StringSplitOptions.None).Length == 2, "Expected one generated journal binding");
                string corruptAudit = validAudit.Replace(binding, binding.Replace(oldHash, newHash)); File.WriteAllBytes(path, corrupt); File.WriteAllText(audit, corruptAudit); int callbacks = 0; var operations = new Operations();
                Throws<SerializationException>(() => ReviewInterrupted(f, guard: new EvidenceGuard { OnValidate = () => callbacks++ }, operations: operations));
                Check(callbacks == 0 && File.ReadAllBytes(path).SequenceEqual(corrupt) && File.ReadAllText(audit) == corruptAudit && File.ReadAllBytes(record).SequenceEqual(pending), "Malformed journal reached device callbacks or changed retained bytes"); NoRemovalCompletion(f); using (operations.Reserve(new[] { 700, 701 })) {}
                File.WriteAllBytes(path, valid); File.WriteAllText(audit, validAudit); Check(ReviewInterrupted(f, operations: operations).PendingWindowsRecords.Single() == record, "Fresh valid review retained refusal resources");
            } });
            Test("sharing rejects missing array separators in original probe metadata before encode and permits manual retry", () => RefuseInvalidSyntaxProbe(false));
            Test("sharing rejects missing array separators in output probe metadata before decode or publication and permits manual retry", () => RefuseInvalidSyntaxProbe(true));
            Test("held signed package review rejects authenticated trailing-comma JSON without rewriting files", () => RefusePackageSyntax(text => text.Substring(0, text.Length - 1) + ",}"));
            Test("held signed package review rejects missing file-array separators without weakening exact trust", () => RefusePackageSyntax(text => { Check(text.Contains("},{"), "Expected generated package file separators"); return text.Replace("},{", "} {"); }));
            Test("signed Windows channel rejects authenticated trailing-comma offer and permits fresh exact offer", () => { using (var f = new WindowsChannelFixture()) {
                string valid = f.Text(), corrupt = valid.Substring(0, valid.Length - 1) + ",}"; Throws<SafetyException>(() => f.Offer(corrupt));
                Check(f.Offer(valid).Release.Version == "0.1.0.0", "Fresh valid channel offer failed after syntax refusal");
            } });
            Test("valid unknown JSON tokens escapes whitespace and deeply nested legacy values preserve settings and encodings", () => { using (var f = new Fixture()) {
                string future = "[null,true,false,0,-0,0.0,-1.25e-2,1E+2,1e400,{},[],{\"text\":\"α } ] \\\" \\\\ \\/ \\b \\f \\n \\r \\t \\u0041 \\uD83D\\uDE00\"}," + new string('[', 1024) + "0" + new string(']', 1024) + "]";
                byte[] raw = JsonWithFuture(ReplaySerialize(new AppSettings { ShowNotifications = false, CleanCardsOnEject = null }, typeof(AppSettings)), future); string json = " \r\n\t" + Encoding.UTF8.GetString(raw) + "\r\n\t ", path = Path.Combine(f.Destination, "app-settings.json");
                foreach (var encoding in new Encoding[] { new UTF8Encoding(false), new UnicodeEncoding(false, false), new UnicodeEncoding(true, false) }) {
                    byte[] bytes = encoding.GetBytes(json); File.WriteAllBytes(path, bytes); var value = AppSettings.Load(f.Destination); Check(!value.NotificationsEnabled && !value.CleanupCardsEnabled && File.ReadAllBytes(path).SequenceEqual(bytes), "Valid legacy unknown tokens/encoding altered saved action choices or bytes");
                }
            } });
            Test("signed metadata valid whitespace still binds trust while semantic schema restrictions remain", () => { using (var f = new WindowsChannelFixture()) {
                string valid = "\r\n\t " + f.Text() + " \r\n\t"; Check(f.Offer(valid).Release.Version == "0.1.0.0", "Valid signed whitespace changed exact offer");
                Throws<SafetyException>(() => f.Offer(f.Text().Replace("\"format\":1", "\"format\":1e0")));
            } });
            Test("malformed literal escape control and truncated nested content keep the serialization error contract", () => {
                byte[] valid = ReplaySerialize(new AppSettings(), typeof(AppSettings));
                foreach (string future in new[] { "TRUE", "nul", "\"\\q\"", "\"\\u12xz\"", "\"raw\ncontrol\"", "[true,false", "{\"name\":}" }) RefuseJsonRecord("app-settings.json", JsonWithFuture(valid, future), root => AppSettings.Load(root), valid);
            });
        }
    }
}
