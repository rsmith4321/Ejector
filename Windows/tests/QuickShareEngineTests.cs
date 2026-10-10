using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        sealed class FakeShareProcess : IQuickShareProcess {
            internal QuickShareProbe Input = ShareInput(); internal Action<string, string> OnStage = (stage, path) => {};
            internal Func<string, QuickShareProbe> InputFor; internal string[] LastEncode;
            internal Action<QuickShareProbe> ChangeOutput = _ => {}; internal int Encodes, Decodes, Probes;
            internal readonly List<string> Inputs = new List<string>(); readonly Dictionary<string, QuickSharePlan> outputs = new Dictionary<string, QuickSharePlan>();
            internal string Temporary;
            public byte[] Run(string executable, string[] args, string directory, double timeout, CancellationToken cancel, Action validate, Action<string> progress = null) {
                cancel.ThrowIfCancellationRequested(); validate();
                bool probe = args.Contains("-show_streams"), decode = args.Contains("null"); string path = probe ? args.Last() : ShareOption(args, "-i");
                if (probe) {
                    Probes++; bool output = outputs.ContainsKey(path); OnStage(output ? "Probe output" : "Probe original", path);
                    var value = output ? ShareOutput(outputs[path]) : InputFor == null ? Input : InputFor(path); if (output) ChangeOutput(value);
                    using (var json = new MemoryStream()) { new DataContractJsonSerializer(typeof(QuickShareProbe)).WriteObject(json, value); return json.ToArray(); }
                }
                if (decode) { Decodes++; Check(args.Contains("-xerror") && args.Contains("0:v:0") && args.Contains("0:a:0?") && !args.Contains("-t"), "Engine did not request full video/audio decode"); OnStage("Decode", path); }
                else {
                    Encodes++; Inputs.Add(path); LastEncode = args; Temporary = args.Last(); OnStage("Before encode", path); validate();
                    var plan = SharePlan(InputFor == null ? Input : InputFor(path), ShareOption(args, "-b:v") == "24000000" ? QuickSharePreset.Uhd : QuickSharePreset.Hd, ShareOption(args, "-vf").Contains("lut3d") ? QuickShareColor.LunaILog : QuickShareColor.Standard);
                    Check(args.Contains("-n") && !File.Exists(Temporary), "Encoder was allowed to overwrite private work");
                    File.WriteAllText(Temporary, "synthetic encoded output " + plan.PresetName + " " + plan.Color + " " + File.ReadAllText(path)); outputs[Temporary] = plan;
                    OnStage("After encode", Temporary); if (progress != null) progress("out_time_us=1000000");
                }
                cancel.ThrowIfCancellationRequested(); validate(); return new byte[0];
            }
        }
        static QuickShareTools FixtureShareTools(Fixture fixture) {
            string folder = Path.Combine(fixture.Root, "synthetic-tools"); Directory.CreateDirectory(folder); string ffmpeg = Path.Combine(folder, "ffmpeg.exe"), ffprobe = Path.Combine(folder, "ffprobe.exe");
            if (!File.Exists(ffmpeg)) File.WriteAllText(ffmpeg, "synthetic tool, never executed"); if (!File.Exists(ffprobe)) File.WriteAllText(ffprobe, "synthetic tool, never executed");
            return new QuickShareTools(ffmpeg, ffprobe);
        }
        static CompletedBatch LunaBatch(Fixture fixture, string name = "VID_fixture.mp4") { fixture.Put(name, "synthetic fixture with injected Insta360 Luna Ultra identity"); return Batch(fixture, fixture.Run(_ => {})); }
        static QuickShareResult RunShare(Fixture fixture, CompletedBatch batch, FakeShareProcess process, Operations operations = null, IImportGuard guard = null, CancellationToken cancel = default(CancellationToken), QuickSharePreset preset = QuickSharePreset.Hd, QuickShareColor color = QuickShareColor.Standard, Action<string> audit = null, Func<string, ulong> free = null, Action<string, string> checkpoint = null) {
            return new QuickShareEngine(process, operations ?? new Operations(), audit, free, checkpoint).Run(batch, HistoryIdentity(), guard ?? new FakeGuard(), FixtureShareTools(fixture), preset, color, cancel, _ => {});
        }
        static string ShareFile(CompletedBatch batch, string name = "VID_fixture", string size = "1080p") { return Path.Combine(batch.Folder, "Sharing Copies", name + "-sharing-" + size + ".mp4"); }
        static void CheckSharePrivateGone(CompletedBatch batch) { string folder = Path.Combine(batch.Folder, "Sharing Copies"); if (Directory.Exists(folder)) Check(!Directory.GetDirectories(folder).Any(x => Path.GetFileName(x).StartsWith(".easy-share-", StringComparison.Ordinal) && Path.GetFileName(x) != ".easy-share-audits"), "Private encoder work leaked"); }
        static void VerifyShareOriginals(CompletedBatch batch) { foreach (var file in batch.Originals) using (var stream = SafePaths.ReadFile(file.Path)) Check(stream.Length == file.Bytes && ImportHistory.Hash(stream, CancellationToken.None) == file.Sha256, "Quick Share modified an original"); }
        // This native child is a synthetic protocol helper, not FFmpeg or a media decoder.
        static int ShareToolFixture(string[] args) {
            string allowed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectFixtures");
            if (!SafePaths.Within(SafePaths.Normalize(Environment.CurrentDirectory), allowed)) return 92;
            bool probe = args.Contains("-show_streams"), decode = args.Contains("null"); string input = probe ? args.Last() : ShareOption(args, "-i");
            if (!SafePaths.Within(SafePaths.Normalize(input), allowed)) return 92;
            if (probe) {
                var value = Path.GetFileName(input) == "output.mp4" ? ShareOutput(SharePlan(preset: File.ReadAllText(input).Contains("|4K|") ? QuickSharePreset.Uhd : QuickSharePreset.Hd)) : ShareInput();
                using (var stream = Console.OpenStandardOutput()) new DataContractJsonSerializer(typeof(QuickShareProbe)).WriteObject(stream, value); return 0;
            }
            if (decode) {
                using (var stream = File.OpenRead(input)) { ImportHistory.Hash(stream, CancellationToken.None); }
                return File.ReadAllText(input).StartsWith("SYNTHETIC_SHARE|") ? 0 : 18;
            }
            string output = args.Last(); if (!args.Contains("-n") || !SafePaths.Within(SafePaths.Normalize(output), allowed) || File.Exists(output)) return 19;
            File.WriteAllText(output, "SYNTHETIC_SHARE|" + (ShareOption(args, "-b:v") == "24000000" ? "4K" : "1080p") + "|" + File.ReadAllText(input)); Console.WriteLine("out_time_us=1000000"); return 0;
        }
        static void QuickShareEngineTests() {
            Test("manual Quick Share replaced empty work refuses cleanup before touching same-named foreign files", delegate {
                using (var fixture = new Fixture()) {
                    var batch = LunaBatch(fixture); var process = new FakeShareProcess(); string replacement = null, moved = null;
                    process.OnStage = (stage, path) => { if (stage == "Before encode") {
                        replacement = Path.GetDirectoryName(process.Temporary); moved = replacement + "-moved"; Directory.Move(replacement, moved); Directory.CreateDirectory(replacement);
                        foreach (string name in new[] { "output.mp4", "luna.cube", "receipt.partial" }) File.WriteAllText(Path.Combine(replacement, name), "foreign " + name);
                        throw new SafetyException("Injected stop after work replacement");
                    } };
                    var failure = Throws<QuickShareFailure>(() => RunShare(fixture, batch, process));
                    Check(failure.Partial.PublishedCopies == 0 && failure.Partial.CleanupWarnings.Count == 1 && Directory.Exists(moved), "Changed work was published or swept");
                    foreach (string name in new[] { "output.mp4", "luna.cube", "receipt.partial" }) Check(File.Exists(Path.Combine(replacement, name)) && File.ReadAllText(Path.Combine(replacement, name)) == "foreign " + name, "Cleanup touched replacement file " + name);
                    VerifyShareOriginals(batch);
                }
            });
            Test("manual Quick Share detects empty work replacement at process validation before writing output", delegate {
                using (var fixture = new Fixture()) {
                    var batch = LunaBatch(fixture); var process = new FakeShareProcess(); string replacement = null, moved = null;
                    process.OnStage = (stage, path) => { if (stage == "Before encode") { replacement = Path.GetDirectoryName(process.Temporary); moved = replacement + "-moved"; Directory.Move(replacement, moved); Directory.CreateDirectory(replacement); } };
                    var failure = Throws<QuickShareFailure>(() => RunShare(fixture, batch, process));
                    Check(failure.Partial.PublishedCopies == 0 && failure.Partial.CleanupWarnings.Count == 1 && Directory.Exists(moved) && Directory.Exists(replacement) && Directory.GetFileSystemEntries(replacement).Length == 0 && process.Decodes == 0, "Encoder or cleanup used replacement work");
                    VerifyShareOriginals(batch);
                }
            });
            Test("manual Quick Share cancellation preserves replacement work and releases reservations", delegate {
                using (var fixture = new Fixture()) using (var token = new CancellationTokenSource()) {
                    var batch = LunaBatch(fixture); var process = new FakeShareProcess(); var operations = new Operations(); string replacement = null;
                    process.OnStage = (stage, path) => { if (stage == "Before encode") { replacement = Path.GetDirectoryName(process.Temporary); Directory.Move(replacement, replacement + "-moved"); Directory.CreateDirectory(replacement); File.WriteAllText(Path.Combine(replacement, "receipt.partial"), "keep cancelled replacement"); token.Cancel(); token.Token.ThrowIfCancellationRequested(); } };
                    var failure = Throws<QuickShareFailure>(() => RunShare(fixture, batch, process, operations: operations, cancel: token.Token));
                    Check(failure.Cancelled && failure.Partial.PublishedCopies == 0 && failure.Partial.CleanupWarnings.Count == 1 && File.Exists(Path.Combine(replacement, "receipt.partial")) && File.ReadAllText(Path.Combine(replacement, "receipt.partial")) == "keep cancelled replacement", "Cancellation deleted foreign work");
                    using (operations.Reserve(new[] { 700, 701 })) {} VerifyShareOriginals(batch);
                }
            });
            Test("manual Quick Share orders probe and full-decode checks before exclusive output and bound receipt", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); var process = new FakeShareProcess(); var result = RunShare(fixture, batch, process); string output = ShareFile(batch);
                    Check(result.Created == 1 && result.Reused == 0 && result.PublishedCopies == 1 && result.PublishedReceipts == 1 && process.Encodes == 1 && process.Decodes == 1 && process.Probes == 2, "Validation/publication stages missing");
                    using (var stream = SafePaths.ReadFile(QuickShareEngine.ReceiptPath(output))) { var receipt = (QuickShareReceipt)new DataContractJsonSerializer(typeof(QuickShareReceipt)).ReadObject(stream); using (var copy = SafePaths.ReadFile(output)) Check(receipt.Recipe == QuickSharePlan.Recipe && receipt.SourceSha256 == batch.Originals[0].Sha256 && receipt.LutSha256 == null && receipt.OutputSha256 == ImportHistory.Hash(copy, CancellationToken.None) && receipt.Bytes == copy.Length, "Receipt is not bound to source/recipe/output"); }
                    Check(File.ReadAllText(result.AuditPath).Contains("Before exclusive publication") && File.ReadAllText(result.AuditPath).Contains("Completed manual Quick Share"), "Durable publication audit missing"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
                }
            });
            Test("manual Quick Share reuses only matching hash verified recipe and preserves separate 4K copies", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); RunShare(fixture, batch, new FakeShareProcess()); byte[] former = File.ReadAllBytes(ShareFile(batch)); var repeated = new FakeShareProcess(); var reuse = RunShare(fixture, batch, repeated); Check(reuse.Reused == 1 && reuse.Created == 0 && repeated.Encodes == 0 && repeated.Decodes == 0 && repeated.Probes == 1, "Matching export not reused");
                    var four = RunShare(fixture, batch, new FakeShareProcess(), preset: QuickSharePreset.Uhd); Check(four.Created == 1 && File.Exists(ShareFile(batch, size: "4K")) && File.ReadAllBytes(ShareFile(batch)).SequenceEqual(former), "4K replaced 1080p"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
                }
            });
            Test("manual Quick Share native runner orchestrates held-file probe encode decode and publication", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); string folder = Path.Combine(fixture.Root, "native-tool-fixture"); Directory.CreateDirectory(folder); string helper = Path.Combine(folder, "EasyEject.ShareFixture.exe"); File.Copy(ShareHelper, helper);
                    var engine = new QuickShareEngine(new WindowsQuickShareProcess(), new Operations()); var tools = new QuickShareTools(helper, helper); var result = engine.Run(batch, HistoryIdentity(), new FakeGuard(), tools, QuickSharePreset.Hd, QuickShareColor.Standard, CancellationToken.None, _ => {}); Check(result.Created == 1 && File.ReadAllText(result.Outputs.Single()).StartsWith("SYNTHETIC_SHARE|1080p|"), "Native orchestration/file sharing failed");
                    result = engine.Run(batch, HistoryIdentity(), new FakeGuard(), tools, QuickSharePreset.Uhd, QuickShareColor.Standard, CancellationToken.None, _ => {}); Check(result.Created == 1 && File.ReadAllText(result.Outputs.Single()).StartsWith("SYNTHETIC_SHARE|4K|"), "Native 4K recipe failed"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
                }
            });
            Test("manual Quick Share reused copy and receipt remain held through audit and result acceptance", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); RunShare(fixture, batch, new FakeShareProcess()); int held = 0; var result = RunShare(fixture, batch, new FakeShareProcess(), audit: line => { if (line.StartsWith("Reused verified")) { Throws<IOException>(() => File.WriteAllText(ShareFile(batch), "forbidden change")); Throws<IOException>(() => File.WriteAllText(QuickShareEngine.ReceiptPath(ShareFile(batch)), "forbidden receipt change")); held++; } }); Check(result.Reused == 1 && held == 1, "Reuse gap remains"); VerifyShareOriginals(batch); }
            });
            Test("manual Quick Share newly published copy and receipt remain held through completion audit", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); int held = 0; var result = RunShare(fixture, batch, new FakeShareProcess(), audit: line => { if (line.StartsWith("Verified sharing copy")) { Throws<IOException>(() => File.WriteAllText(ShareFile(batch), "forbidden change")); Throws<IOException>(() => File.WriteAllText(QuickShareEngine.ReceiptPath(ShareFile(batch)), "forbidden receipt change")); held++; } }); Check(result.Created == 1 && held == 1, "Publication handles released early"); VerifyShareOriginals(batch); }
            });
            Test("manual Quick Share exact history survives source absence and restart without rescanning dates", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); string root = Path.Combine(fixture.Root, "history"); Directory.CreateDirectory(root); ImportHistory.Add(batch, root); File.Delete(Path.Combine(fixture.Source, "VID_fixture.mp4")); File.WriteAllText(Path.Combine(batch.Folder, "VID_unrecorded.mp4"), "unrecorded Insta360 Luna Pro fixture");
                    var loaded = ImportHistory.Load(root).Single(); var process = new FakeShareProcess(); var result = RunShare(fixture, loaded, process); Check(result.Created == 1 && process.Inputs.SequenceEqual(new[] { loaded.Originals[0].Path }) && !File.Exists(ShareFile(batch, "VID_unrecorded")), "History scanned or required source/eject permission"); VerifyShareOriginals(batch);
                }
            });
            Test("manual Quick Share verifies the whole exact saved batch before starting any conversion", delegate {
                using (var fixture = new Fixture()) { fixture.Put("photo.jpg", "still fixture"); var batch = LunaBatch(fixture); string still = batch.Originals.Single(x => x.Path.EndsWith(".jpg")).Path; File.WriteAllText(still, "changed still"); var process = new FakeShareProcess(); var failure = Throws<QuickShareFailure>(() => RunShare(fixture, batch, process)); Check(failure.InnerException is SafetyException && process.Probes == 0 && !Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")), "Changed noncandidate ignored before export"); }
            });
            Test("manual Quick Share changed destination receipt and recorded manifest refuse before tools", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); var process = new FakeShareProcess(); var changed = ImportHistory.Clone(batch); changed.Destination.VolumeSerial = "changed"; Throws<QuickShareFailure>(() => RunShare(fixture, changed, process)); changed = ImportHistory.Clone(batch); changed.Originals[0].Path = Path.Combine(batch.Folder, "VID_missing.mp4"); Throws<QuickShareFailure>(() => RunShare(fixture, changed, process)); File.AppendAllText(batch.ReceiptPath, "tampered"); Throws<QuickShareFailure>(() => RunShare(fixture, batch, process)); Check(process.Probes == 0, "Stale history reached tools"); }
            });
            Test("manual Quick Share holds originals receipt and tools against mutation through full decode", delegate {
                using (var fixture = new Fixture()) { fixture.Put("still.jpg", "still fixture"); var batch = LunaBatch(fixture); var tools = FixtureShareTools(fixture); var process = new FakeShareProcess(); int protectedChecks = 0;
                    process.OnStage = (stage, path) => { if (stage == "Decode") { foreach (var original in batch.Originals) Throws<IOException>(() => File.WriteAllText(original.Path, "forbidden change")); Throws<IOException>(() => File.WriteAllText(batch.ReceiptPath, "forbidden receipt change")); Throws<IOException>(() => File.WriteAllText(tools.Ffmpeg, "forbidden tool change")); protectedChecks++; } };
                    RunShare(fixture, batch, process); Check(protectedChecks == 1, "Held-file boundary not exercised"); VerifyShareOriginals(batch);
                }
            });
            Test("manual Quick Share reservations prevent overlap and release after success failure or cancellation", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); var operations = new Operations(); var guard = new FakeGuard(); using (operations.Reserve(guard.ProtectedDisks)) Throws<QuickShareFailure>(() => RunShare(fixture, batch, new FakeShareProcess(), operations, guard));
                    var process = new FakeShareProcess(); process.OnStage = (stage, path) => { if (stage == "Before encode") Throws<SafetyException>(() => operations.Reserve(guard.ProtectedDisks)); }; RunShare(fixture, batch, process, operations, guard); using (operations.Reserve(guard.ProtectedDisks)) {}
                    using (var token = new CancellationTokenSource()) { token.Cancel(); Throws<QuickShareFailure>(() => RunShare(fixture, batch, process, operations, guard, token.Token)); } using (operations.Reserve(guard.ProtectedDisks)) {}
                }
            });
            Test("manual Quick Share skips unsupported recordings and creates no sharing work for no Luna clips", delegate {
                using (var fixture = new Fixture()) { fixture.Put("other.mp4", "Insta360 Luna Ultra but no VID filename"); fixture.Put("VID_other.mp4", "Insta360 X5 unrelated fixture"); fixture.Put("photo.jpg", "still"); var batch = Batch(fixture, fixture.Run(_ => {})); var process = new FakeShareProcess(); var result = RunShare(fixture, batch, process, color: QuickShareColor.LunaILog); Check(result.Skipped == 2 && result.Created == 0 && result.AuditPath == null && process.Probes == 0 && !Directory.Exists(result.Folder), "Unsupported recordings reached tool/LUT/work"); }
            });
            Test("manual Quick Share preserves wrong receipts changed outputs and occupied names with conflict suffixes", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); var first = RunShare(fixture, batch, new FakeShareProcess()); string path = first.Outputs.Single(); File.AppendAllText(path, "changed export"); byte[] changed = File.ReadAllBytes(path); var second = RunShare(fixture, batch, new FakeShareProcess()); Check(second.Created == 1 && second.Outputs.Single().Contains(batch.Originals[0].Sha256.Substring(0, 10) + "-1") && File.ReadAllBytes(path).SequenceEqual(changed), "Changed output overwritten/reused");
                    File.WriteAllText(QuickShareEngine.ReceiptPath(second.Outputs.Single()), "damaged receipt"); var third = RunShare(fixture, batch, new FakeShareProcess()); Check(third.Outputs.Single().EndsWith("-2.mp4") && File.ReadAllText(QuickShareEngine.ReceiptPath(second.Outputs.Single())) == "damaged receipt", "Wrong receipt overwritten"); VerifyShareOriginals(batch);
                }
            });
            Test("manual Quick Share recipe mismatch never reuses an otherwise intact previous export", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); RunShare(fixture, batch, new FakeShareProcess()); string receipt = QuickShareEngine.ReceiptPath(ShareFile(batch)); File.WriteAllText(receipt, File.ReadAllText(receipt).Replace(QuickSharePlan.Recipe, "other-recipe")); var result = RunShare(fixture, batch, new FakeShareProcess()); Check(result.Created == 1 && result.Reused == 0 && File.ReadAllText(receipt).Contains("other-recipe"), "Foreign recipe reused/replaced"); }
            });
            Test("manual Quick Share refuses excessive conflict names and keeps every occupied file", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); string basePath = ShareFile(batch); Directory.CreateDirectory(Path.GetDirectoryName(basePath)); for (int i = 0; i < 100; i++) File.WriteAllText(i == 0 ? basePath : Path.Combine(Path.GetDirectoryName(basePath), "VID_fixture-sharing-1080p-" + batch.Originals[0].Sha256.Substring(0, 10) + "-" + i + ".mp4"), "existing export"); var process = new FakeShareProcess(); var failure = Throws<QuickShareFailure>(() => RunShare(fixture, batch, process)); Check(failure.Message.Contains("Too many") && process.Encodes == 0 && Directory.GetFiles(Path.GetDirectoryName(basePath), "*.mp4").All(x => File.ReadAllText(x) == "existing export"), "Conflicts replaced or unbounded retry"); }
            });
            Test("manual Quick Share output probe or full decode failure clears private output and keeps originals", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); var process = new FakeShareProcess(); process.ChangeOutput = x => x.Video.Width = 2; var failure = Throws<QuickShareFailure>(() => RunShare(fixture, batch, process)); Check(failure.Partial.PublishedCopies == 0 && process.Decodes == 0, "Invalid probe output published"); CheckSharePrivateGone(batch);
                    process = new FakeShareProcess(); process.OnStage = (stage, path) => { if (stage == "Decode") throw new SafetyException("injected full-decode corruption"); }; Throws<QuickShareFailure>(() => RunShare(fixture, batch, process)); Check(!File.Exists(ShareFile(batch)), "Undecodable export published"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
                }
            });
            Test("manual Quick Share HDR input refuses before encoding or publishing", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); var process = new FakeShareProcess(); process.Input.Video.Transfer = "smpte2084"; var failure = Throws<QuickShareFailure>(() => RunShare(fixture, batch, process)); Check(failure.Message.Contains("HDR") && process.Encodes == 0 && failure.Partial.PublishedCopies == 0 && !File.Exists(ShareFile(batch)), "HDR reached export/publication"); CheckSharePrivateGone(batch); VerifyShareOriginals(batch); }
            });
            Test("manual Quick Share cancellation during encoding keeps history and supports a clean manual retry", delegate {
                using (var fixture = new Fixture()) using (var token = new CancellationTokenSource()) { var batch = LunaBatch(fixture); var process = new FakeShareProcess(); process.OnStage = (stage, path) => { if (stage == "After encode") token.Cancel(); }; var failure = Throws<QuickShareFailure>(() => RunShare(fixture, batch, process, cancel: token.Token)); Check(failure.Cancelled && failure.Partial.Created == 0 && failure.Partial.PublishedCopies == 0 && !File.Exists(ShareFile(batch)), "Cancellation reported success/published output"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch); Check(RunShare(fixture, batch, new FakeShareProcess()).Created == 1, "Manual retry failed"); }
            });
            Test("manual Quick Share destination disconnection during decode prevents publication and releases resources", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); var guard = new FakeGuard(); var operations = new Operations(); var process = new FakeShareProcess(); process.OnStage = (stage, path) => { if (stage == "Decode") guard.FailAt = guard.Calls + 1; }; Throws<QuickShareFailure>(() => RunShare(fixture, batch, process, operations, guard)); Check(!File.Exists(ShareFile(batch)), "Lost destination published export"); using (operations.Reserve(guard.ProtectedDisks)) {} VerifyShareOriginals(batch); CheckSharePrivateGone(batch); }
            });
            Test("manual Quick Share insufficient space and missing official LUT stop before encoding", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); var process = new FakeShareProcess(); Throws<QuickShareFailure>(() => RunShare(fixture, batch, process, free: _ => 1073741824)); Check(process.Encodes == 0, "Insufficient space encoded"); CheckSharePrivateGone(batch); process = new FakeShareProcess(); Throws<QuickShareFailure>(() => RunShare(fixture, batch, process, color: QuickShareColor.LunaILog)); Check(process.Probes == 0 && process.Encodes == 0, "Missing official LUT was ignored"); VerifyShareOriginals(batch); }
            });
            Test("manual Quick Share byte-identical replaced temporary identity refuses after validation", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); var process = new FakeShareProcess(); var failure = Throws<QuickShareFailure>(() => RunShare(fixture, batch, process, checkpoint: (stage, path) => { if (stage == "Before publication handle") { string replacement = Path.Combine(Path.GetDirectoryName(path), "replacement.mp4"); File.WriteAllBytes(replacement, File.ReadAllBytes(path)); File.SetLastWriteTimeUtc(replacement, File.GetLastWriteTimeUtc(path)); File.Delete(path); File.Move(replacement, path); } })); Check(failure.Message.Contains("replaced") && failure.Partial.PublishedCopies == 0, "Replacement escaped identity comparison"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch); }
            });
            Test("manual Quick Share exclusive publication preserves a late occupied output filename", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); var failure = Throws<QuickShareFailure>(() => RunShare(fixture, batch, new FakeShareProcess(), checkpoint: (stage, path) => { if (stage == "Before output publication") File.WriteAllText(path, "late existing file"); })); Check(File.ReadAllText(ShareFile(batch)) == "late existing file" && failure.Partial.PublishedCopies == 0, "Late conflict overwritten"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch); }
            });
            Test("manual Quick Share receipt race keeps published verified copy and permits preserved-name retry", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); var failure = Throws<QuickShareFailure>(() => RunShare(fixture, batch, new FakeShareProcess(), checkpoint: (stage, path) => { if (stage == "Before receipt publication") File.WriteAllText(path, "late existing receipt"); })); byte[] orphan = File.ReadAllBytes(ShareFile(batch)); Check(failure.Partial.PublishedCopies == 1 && failure.Partial.PublishedReceipts == 0 && failure.Partial.Created == 0 && File.ReadAllText(QuickShareEngine.ReceiptPath(ShareFile(batch))) == "late existing receipt", "Partial publication concealed/overwritten"); var retry = RunShare(fixture, batch, new FakeShareProcess()); Check(retry.Created == 1 && retry.Outputs.Single().EndsWith("-1.mp4") && File.ReadAllBytes(ShareFile(batch)).SequenceEqual(orphan), "Retry discarded prior published copy"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch); }
            });
            Test("manual Quick Share cancellation between output and receipt reports exact retained partial publication", delegate {
                using (var fixture = new Fixture()) using (var token = new CancellationTokenSource()) { var batch = LunaBatch(fixture); var failure = Throws<QuickShareFailure>(() => RunShare(fixture, batch, new FakeShareProcess(), cancel: token.Token, checkpoint: (stage, path) => { if (stage == "Before receipt publication") token.Cancel(); })); Check(failure.Cancelled && failure.Partial.PublishedCopies == 1 && failure.Partial.PublishedReceipts == 0 && File.Exists(ShareFile(batch)) && !File.Exists(QuickShareEngine.ReceiptPath(ShareFile(batch))), "Canceled partial publication inaccurate"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch); }
            });
            Test("manual Quick Share late audit failure reports actual receipt completion and retry reuses it", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); var failure = Throws<QuickShareFailure>(() => RunShare(fixture, batch, new FakeShareProcess(), audit: line => { if (line.StartsWith("Verified sharing copy")) throw new IOException("injected audit failure"); })); Check(failure.Partial.Created == 1 && failure.Partial.PublishedCopies == 1 && failure.Partial.PublishedReceipts == 1, "Actual completed copy hidden by late failure"); var retry = RunShare(fixture, batch, new FakeShareProcess()); Check(retry.Reused == 1 && retry.Created == 0, "Receipt-completed retry duplicated file"); VerifyShareOriginals(batch); }
            });
            Test("manual Quick Share later-clip cancellation retains earlier complete copies with exact counts", delegate {
                using (var fixture = new Fixture()) using (var token = new CancellationTokenSource()) { fixture.Put("VID_first.mp4", "synthetic Insta360 Luna Pro first"); var batch = LunaBatch(fixture, "VID_second.mp4"); var process = new FakeShareProcess(); process.OnStage = (stage, path) => { if (stage == "After encode" && process.Encodes == 2) token.Cancel(); }; var failure = Throws<QuickShareFailure>(() => RunShare(fixture, batch, process, cancel: token.Token)); Check(failure.Partial.Created == 1 && failure.Partial.PublishedCopies == 1 && failure.Partial.Outputs.Count == 1 && File.Exists(failure.Partial.Outputs[0]), "Earlier complete output discarded/count wrong"); var retry = RunShare(fixture, batch, new FakeShareProcess()); Check(retry.Reused == 1 && retry.Created == 1, "Retry failed to reuse only prior completed copy"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch); }
            });
            Test("manual Quick Share redirected sharing directories refuse without touching link targets", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); string target = Path.Combine(fixture.Root, "outside-sharing"); Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "keep"), "unaltered"); CreateJunction(fixture, Path.Combine(batch.Folder, "Sharing Copies"), target); var process = new FakeShareProcess(); Throws<QuickShareFailure>(() => RunShare(fixture, batch, process)); Check(process.Probes == 0 && Directory.GetFileSystemEntries(target).Length == 1 && File.ReadAllText(Path.Combine(target, "keep")) == "unaltered", "Sharing junction used/target changed"); }
            });
            Test("manual Quick Share unknown private work is retained rather than recursively deleted", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); var process = new FakeShareProcess(); string unknown = null; process.OnStage = (stage, path) => { if (stage == "After encode") { unknown = Path.Combine(Path.GetDirectoryName(path), "unknown.keep"); File.WriteAllText(unknown, "retain unknown private data"); } }; var result = RunShare(fixture, batch, process); Check(result.Created == 1 && result.CleanupWarnings.Count == 1 && File.ReadAllText(unknown) == "retain unknown private data" && !File.Exists(process.Temporary), "Cleanup broadened or removed published copy"); VerifyShareOriginals(batch); }
            });
            Test("manual Quick Share protected private output refuses publication and cleanup without changing protection", delegate {
                using (var fixture = new Fixture()) { var batch = LunaBatch(fixture); var process = new FakeShareProcess(); process.OnStage = (stage, path) => { if (stage == "After encode") File.SetAttributes(path, FileAttributes.ReadOnly); };
                    try { var failure = Throws<QuickShareFailure>(() => RunShare(fixture, batch, process)); Check(failure.Partial.PublishedCopies == 0 && failure.Partial.CleanupWarnings.Count == 1 && (File.GetAttributes(process.Temporary) & FileAttributes.ReadOnly) != 0 && !File.Exists(ShareFile(batch)), "Protection bypassed or failure concealed"); VerifyShareOriginals(batch); }
                    finally { if (process.Temporary != null && File.Exists(process.Temporary)) File.SetAttributes(process.Temporary, FileAttributes.Normal); }
                }
            });
        }
    }
}
