using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        // Explicit CLI only. Never called by the default suite; no downloads, settings
        // in the user's profile, user media, discovery, or physical device operations.
        static int RealQuickShareAcceptance(string[] args) {
            try {
                if (args.Length != 3 && args.Length != 4) throw new SafetyException("Usage: --share-real-tools <ffmpeg.exe> <ffprobe.exe> [official Luna LUT.cube]. This executes the explicitly supplied tools on generated disposable clips only.");
                var tools = new QuickShareTools(args[1], args[2], args.Length == 4 ? args[3] : null);
                if (tools.Ffmpeg.Equals(tools.Ffprobe, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Select separate FFmpeg and FFprobe executables.");
                using (tools.Hold()) {
                    // Validate a supplied LUT before launching either executable.
                    if (tools.Lut != null) QuickShareMedia.ReadOfficialLut(tools.Lut, CancellationToken.None);
                    using (var f = new Fixture()) {
                        Check(Native.FreeSpace(f.Root) > 3UL * 1073741824, "Real-tool rehearsal requires more than 3 GiB free in the owned fixture location");
                        var process = new WindowsQuickShareProcess();
                        string[] paths = { tools.Ffmpeg, tools.Ffprobe }, prefixes = { "ffmpeg version ", "ffprobe version " };
                        for (int i = 0; i < paths.Length; i++) {
                            string path = paths[i], prefix = prefixes[i];
                            string version = Encoding.UTF8.GetString(process.Run(path, new[] { "-version" }, f.Root, 30, CancellationToken.None, () => {}));
                            Check(version.StartsWith(prefix, StringComparison.Ordinal), "The selected executable did not identify itself as " + prefix.Trim());
                            Console.WriteLine("TOOL " + path + " | SHA256 " + IndependentFileHash(path) + " | " + version.Split('\n')[0].Trim());
                        }
                        if (tools.Lut != null) Console.WriteLine("LUT SHA256 " + IndependentFileHash(tools.Lut));
                    }
                    Test("real tools: landscape 60 fps stereo export at 1080p with exact receipts and reuse", () => RealShareCase(tools, 3840, 2160, 60, true, QuickSharePreset.Hd, QuickShareColor.Standard, 1920, 1080, 30));
                    Test("real tools: 4K export preserves full frame", () => RealShareCase(tools, 3840, 2160, 30, true, QuickSharePreset.Uhd, QuickShareColor.Standard, 3840, 2160, 30));
                    Test("real tools: portrait export preserves framing at 1080p", () => RealShareCase(tools, 2160, 3840, 30, true, QuickSharePreset.Hd, QuickShareColor.Standard, 1080, 1920, 30));
                    Test("real tools: smaller silent clip is not enlarged and keeps 24 fps", () => RealShareCase(tools, 640, 360, 24, false, QuickSharePreset.Uhd, QuickShareColor.Standard, 640, 360, 24));
                    Test("real tools: foreign output conflict is retained and fresh export is reusable", () => RealShareConflict(tools));
                    Test("real tools: encoder progress cancellation publishes nothing and fresh manual retry works", () => RealShareCancellation(tools));
                    Test("real tools: changed saved original and destination refuse before tools run", () => RealShareRefusals(tools));
                    if (tools.Lut != null) {
                        Test("real tools: official LUT transform produces validated 1080p output", () => RealShareCase(tools, 1920, 1080, 30, true, QuickSharePreset.Hd, QuickShareColor.LunaILog, 1920, 1080, 30));
                        Test("real tools: official LUT transform produces validated 4K output", () => RealShareCase(tools, 3840, 2160, 30, true, QuickSharePreset.Uhd, QuickShareColor.LunaILog, 3840, 2160, 30));
                    } else Console.WriteLine("NOT RUN: positive official LUT conversion; that acceptance gate remains open.");
                }
                Console.WriteLine(passed + " passed; " + failed + " failed. Actual selected tools on generated clips; Luna identity is injected into an MP4 free box. Fake device guards and same-host storage do not prove camera identity, physical removal, foreground UI, complete app restart, or destination filesystem compatibility.");
                return failed == 0 ? 0 : 1;
            } catch (Exception ex) { Console.WriteLine("Real-tool rehearsal cannot start: " + ex.Message); return 2; }
        }

        sealed class ObservedRealShareProcess : IQuickShareProcess {
            readonly WindowsQuickShareProcess inner = new WindowsQuickShareProcess();
            internal int Calls, Encodes, Probes, Decodes;
            internal CancellationTokenSource StopAtEncoderProgress;
            internal bool CancelledAtProgress;
            public byte[] Run(string executable, string[] arguments, string directory, double timeout, CancellationToken cancel, Action validate, Action<string> progress = null) {
                Calls++; bool encode = arguments.Contains("-progress");
                if (encode) Encodes++;
                if (arguments.Contains("-show_streams")) Probes++;
                if (arguments.Contains("-f") && arguments.Contains("null")) Decodes++;
                Action<string> observe = line => {
                    if (encode && StopAtEncoderProgress != null && !CancelledAtProgress && line.StartsWith("frame=", StringComparison.Ordinal)) {
                        CancelledAtProgress = true; StopAtEncoderProgress.Cancel();
                    }
                    if (progress != null) progress(line);
                };
                return inner.Run(executable, arguments, directory, timeout, cancel, validate, observe);
            }
        }

        static CompletedBatch GenerateRealShareBatch(Fixture f, QuickShareTools tools, int width, int height, int rate, bool audio) {
            string path = Path.Combine(f.Source, "VID_generated.mp4");
            var args = new List<string> { "-nostdin", "-hide_banner", "-v", "error", "-xerror", "-f", "lavfi", "-i", "testsrc2=size=" + width.ToString(CultureInfo.InvariantCulture) + "x" + height.ToString(CultureInfo.InvariantCulture) + ":rate=" + rate.ToString(CultureInfo.InvariantCulture) };
            if (audio) args.AddRange(new[] { "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-map", "0:v:0", "-map", "1:a:0", "-c:a", "aac", "-ac", "1" });
            else args.AddRange(new[] { "-map", "0:v:0", "-an" });
            args.AddRange(new[] { "-t", "1", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "28", "-threads", "2", "-pix_fmt", "yuv420p", "-color_range", "tv", "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-movflags", "+faststart", "-n", path });
            new WindowsQuickShareProcess().Run(tools.Ffmpeg, args.ToArray(), f.Root, 180, CancellationToken.None, () => {});
            // A valid ignorable MP4 box models only the existing tail-selection rule.
            // It does not certify real Luna media or I-Log exposure/color accuracy.
            byte[] identity = Encoding.UTF8.GetBytes("Generated acceptance fixture: Insta360 Luna Ultra");
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                Check(stream.Length > 0, "Generator produced no clip"); stream.Position = stream.Length;
                int size = identity.Length + 8; stream.Write(new[] { (byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size, (byte)'f', (byte)'r', (byte)'e', (byte)'e' }, 0, 8);
                stream.Write(identity, 0, identity.Length); stream.Flush(true);
            }
            string hash = IndependentFileHash(path); var result = f.Run(_ => {}); var batch = Batch(f, result);
            Check(batch.Originals.Count == 1 && batch.Originals[0].Sha256 == hash && IndependentFileHash(batch.Originals[0].Path) == hash, "Generated import changed the original bytes");
            return batch;
        }

        static string RealShareSetup(Fixture f, CompletedBatch batch, QuickShareTools tools) {
            string root = ManualShareSetup(f, batch, tools: tools);
            if (tools.Lut != null) QuickShareConfiguration.Save(QuickShareConfiguration.Load(root), tools.Ffmpeg, tools.Ffprobe, tools.Lut, root, CancellationToken.None);
            HideOwnedSource(f); return root;
        }
        static ManualQuickShareService RealShares(string root, IQuickShareProcess process, Operations operations) {
            return new ManualQuickShareService(root, operations, process, _ => new FakeGuard());
        }
        static QuickShareResult RunRealShare(ManualQuickShareService service, CompletedBatch batch, QuickSharePreset preset = QuickSharePreset.Hd, QuickShareColor color = QuickShareColor.Standard, CancellationToken cancel = default(CancellationToken)) {
            return service.Run(service.Prepare(batch).Choose(preset, color), cancel, _ => {});
        }
        static void RealShareCase(QuickShareTools tools, int width, int height, int rate, bool audio, QuickSharePreset preset, QuickShareColor color, int outWidth, int outHeight, int outRate) {
            using (var f = new Fixture()) {
                var batch = GenerateRealShareBatch(f, tools, width, height, rate, audio); string root = RealShareSetup(f, batch, tools);
                var operations = new Operations(); var process = new ObservedRealShareProcess(); var service = RealShares(root, process, operations);
                var result = RunRealShare(service, batch, preset, color);
                Check(result.Created == 1 && result.Reused == 0 && result.PublishedCopies == 1 && result.PublishedReceipts == 1 && result.Outputs.Count == 1 && process.Encodes == 1 && process.Probes >= 2 && process.Decodes == 1, "New export bypassed encode, input/output probes, full decode, or exclusive receipt publication");
                string output = result.Outputs.Single();
                var probe = QuickShareProbe.Parse(new WindowsQuickShareProcess().Run(tools.Ffprobe, QuickSharePlan.ProbeArguments(output), f.Root, 30, CancellationToken.None, () => {}));
                Check(probe.Video.Width == outWidth && probe.Video.Height == outHeight && Math.Abs(probe.Seconds - 1) < 0.25 && probe.Video.FrameRate == outRate.ToString(CultureInfo.InvariantCulture) + "/1" && probe.Video.Codec == "h264" && probe.Video.Range == "tv" && probe.Video.Primaries == "bt709" && probe.Video.Transfer == "bt709" && probe.Video.ColorSpace == "bt709" && probe.Video.PixelFormat == "yuv420p", "Actual output differs from independent geometry/rate/duration/SDR contract");
                Check(probe.Streams.Count == (audio ? 2 : 1) && (audio ? probe.Audio != null && probe.Audio.Codec == "aac" && probe.Audio.Channels == 2 : probe.Audio == null), "Actual audio contract differs");
                using (var stream = SafePaths.ReadFile(QuickShareEngine.ReceiptPath(output))) {
                    var receipt = JsonDocuments.Read<QuickShareReceipt>(stream, 31999);
                    Check(receipt.SourceSha256 == batch.Originals.Single().Sha256 && receipt.OutputSha256 == IndependentFileHash(output) && receipt.Bytes == new FileInfo(output).Length && receipt.Width == outWidth && receipt.Height == outHeight && receipt.LutSha256 == (color == QuickShareColor.LunaILog ? QuickShareMedia.OfficialLutSha : null), "Published receipt differs from actual generated source/output/LUT");
                    Console.WriteLine("OUTPUT " + preset + " " + color + " " + outWidth + "x" + outHeight + " | SHA256 " + receipt.OutputSha256 + " | source " + receipt.SourceSha256);
                }
                int encodes = process.Encodes;
                var restored = ImportHistory.Load(root).Single(); var reused = RunRealShare(RealShares(root, process, operations), restored, preset, color);
                Check(reused.Created == 0 && reused.Reused == 1 && reused.Outputs.Single() == output && process.Encodes == encodes, "Fresh service/history did not reuse exact verified output");
                Check(!Directory.Exists(f.Source) && IndependentFileHash(Path.Combine(f.Root, "source-absent", "VID_generated.mp4")) == batch.Originals.Single().Sha256, "Logical source absence or original retention differs");
                VerifyShareOriginals(batch); CheckSharePrivateGone(batch); using (operations.Reserve(new[] { 700, 701 })) {}
            }
        }
        static void RealShareConflict(QuickShareTools tools) {
            using (var f = new Fixture()) {
                var batch = GenerateRealShareBatch(f, tools, 640, 360, 30, false); string root = RealShareSetup(f, batch, tools);
                var operations = new Operations(); var process = new ObservedRealShareProcess(); var service = RealShares(root, process, operations);
                string first = RunRealShare(service, batch).Outputs.Single(); File.WriteAllText(first, "owned foreign conflicting output; retain exactly"); string foreignHash = IndependentFileHash(first);
                var retry = RunRealShare(service, batch); Check(retry.Created == 1 && retry.Outputs.Single() != first && IndependentFileHash(first) == foreignHash, "Conflict replaced foreign output or skipped fresh export");
                var reuse = RunRealShare(service, batch); Check(reuse.Reused == 1 && reuse.Outputs.Single() == retry.Outputs.Single() && IndependentFileHash(first) == foreignHash && process.Encodes == 2, "Conflict retry did not use stable exact reuse");
                VerifyShareOriginals(batch); CheckSharePrivateGone(batch); using (operations.Reserve(new[] { 700, 701 })) {}
            }
        }
        static void RealShareCancellation(QuickShareTools tools) {
            using (var f = new Fixture()) using (var stop = new CancellationTokenSource()) {
                var batch = GenerateRealShareBatch(f, tools, 1920, 1080, 60, true); string root = RealShareSetup(f, batch, tools);
                var operations = new Operations(); var process = new ObservedRealShareProcess { StopAtEncoderProgress = stop }; var service = RealShares(root, process, operations);
                var failure = Throws<QuickShareFailure>(() => RunRealShare(service, batch, cancel: stop.Token));
                Check(process.CancelledAtProgress && failure.Cancelled && failure.Partial.Created == 0 && failure.Partial.PublishedCopies == 0 && failure.Partial.PublishedReceipts == 0, "Progress cancellation did not refuse before publication");
                CheckSharePrivateGone(batch); VerifyShareOriginals(batch); using (operations.Reserve(new[] { 700, 701 })) {}
                process.StopAtEncoderProgress = null;
                var retry = RunRealShare(service, batch); Check(retry.Created == 1 && retry.PublishedReceipts == 1 && process.Decodes == 1, "Fresh manual retry did not validate/publish after cancellation");
                VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
            }
        }
        static void RealShareRefusals(QuickShareTools tools) {
            using (var f = new Fixture()) {
                var batch = GenerateRealShareBatch(f, tools, 640, 360, 30, false); string root = RealShareSetup(f, batch, tools);
                var operations = new Operations(); var process = new ObservedRealShareProcess(); var service = RealShares(root, process, operations); string path = batch.Originals.Single().Path;
                byte[] original = File.ReadAllBytes(path), changed = (byte[])original.Clone(); changed[changed.Length - 1] ^= 1; File.WriteAllBytes(path, changed);
                Throws<QuickShareFailure>(() => RunRealShare(service, batch)); Check(process.Calls == 0 && File.ReadAllBytes(path).SequenceEqual(changed), "Changed-original refusal ran tools or altered retained bytes");
                File.WriteAllBytes(path, original); var destination = HistoryIdentity(); destination.VolumeSerial += "-changed";
                Throws<QuickShareFailure>(() => new QuickShareEngine(process, operations).Run(batch, destination, new FakeGuard(), tools, QuickSharePreset.Hd, QuickShareColor.Standard, CancellationToken.None, _ => {}));
                Check(process.Calls == 0 && !Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")), "Changed destination invoked tools or created sharing work");
                var retry = RunRealShare(service, batch); Check(retry.Created == 1 && retry.PublishedReceipts == 1, "Fresh valid review did not retry"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch); using (operations.Reserve(new[] { 700, 701 })) {}
            }
        }
    }
}
