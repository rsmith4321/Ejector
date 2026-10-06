using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static QuickShareProbe ShareInput(int width = 3840, int height = 2160, string rate = "60/1", bool audio = true) {
            return QuickShareProbe.Parse(Encoding.UTF8.GetBytes("{\"streams\":[{\"codec_type\":\"video\",\"codec_name\":\"hevc\",\"width\":" + width.ToString(CultureInfo.InvariantCulture) + ",\"height\":" + height.ToString(CultureInfo.InvariantCulture) + ",\"r_frame_rate\":\"" + rate + "\",\"duration\":\"5.0\",\"color_transfer\":\"bt709\"}" + (audio ? ",{\"codec_type\":\"audio\",\"codec_name\":\"pcm_s16le\",\"duration\":\"5.0\",\"channels\":1}" : "") + "],\"format\":{\"duration\":\"5.0\"}}"));
        }
        static QuickShareProbe ShareOutput(QuickSharePlan plan) {
            return QuickShareProbe.Parse(Encoding.UTF8.GetBytes("{\"streams\":[{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":" + plan.Width.ToString(CultureInfo.InvariantCulture) + ",\"height\":" + plan.Height.ToString(CultureInfo.InvariantCulture) + ",\"r_frame_rate\":\"" + (plan.Fps == "30" ? "30/1" : plan.Fps) + "\",\"duration\":\"5.0\",\"color_space\":\"bt709\",\"color_transfer\":\"bt709\",\"color_primaries\":\"bt709\",\"color_range\":\"tv\",\"pix_fmt\":\"yuv420p\"}" + (plan.HasAudio ? ",{\"codec_type\":\"audio\",\"codec_name\":\"aac\",\"duration\":\"5.0\",\"channels\":2}" : "") + "],\"format\":{\"duration\":\"5.0\"}}"));
        }
        static QuickSharePlan SharePlan(QuickShareProbe input = null, QuickSharePreset preset = QuickSharePreset.Hd, QuickShareColor color = QuickShareColor.Standard) { return new QuickSharePlan(input ?? ShareInput(), preset, color); }
        static string ShareOption(string[] args, string name) { return args[Array.IndexOf(args, name) + 1]; }
        static void QuickShareRecipeTests() {
            Test("Quick Share recognizes only VID MP4 with exact Luna identity in its last 65536 bytes", delegate {
                foreach (string model in new[] { "Ultra", "Pro" }) using (var held = new MemoryStream(Encoding.UTF8.GetBytes("injected synthetic media Insta360 Luna " + model))) {
                    held.Position = 3; Check(QuickShareMedia.IsLunaMaster(@"C:\Fixture\vId_example.MP4", held) && held.Position == 3, "Supported master/position lost");
                    Check(!QuickShareMedia.IsLunaMaster(@"C:\Fixture\other.mp4", held) && !QuickShareMedia.IsLunaMaster(@"C:\Fixture\VID_example.insv", held), "Name/extension broadened");
                }
                var bytes = new byte[70000]; Array.Copy(Encoding.UTF8.GetBytes("Insta360 Luna Ultra"), bytes, 18);
                using (var held = new MemoryStream(bytes)) Check(!QuickShareMedia.IsLunaMaster(@"C:\Fixture\VID_old.mp4", held), "Identity outside tail accepted");
                using (var held = new MemoryStream(Encoding.UTF8.GetBytes("Insta360 X5 insta360 luna ultra"))) Check(!QuickShareMedia.IsLunaMaster(@"C:\Fixture\VID_other.mp4", held), "Unrelated/case-mismatched camera accepted");
            });
            Test("Quick Share tail boundary is exact and does not read an entire long original", delegate {
                byte[] bytes = new byte[131072]; var identity = Encoding.UTF8.GetBytes("Insta360 Luna Pro"); Array.Copy(identity, 0, bytes, 65536, identity.Length);
                using (var held = new MemoryStream(bytes)) Check(QuickShareMedia.IsLunaMaster(@"C:\Fixture\VID_boundary.mp4", held), "Identity at boundary excluded");
                Array.Clear(bytes, 0, bytes.Length); Array.Copy(identity, 0, bytes, 65535, identity.Length);
                using (var held = new MemoryStream(bytes)) Check(!QuickShareMedia.IsLunaMaster(@"C:\Fixture\VID_boundary.mp4", held), "Prefix before boundary read");
            });
            Test("Quick Share 1080p and 4K constrain both edges without stretching or enlarging", delegate {
                var hd = SharePlan(); var uhd = SharePlan(ShareInput(7680, 4320), QuickSharePreset.Uhd); var tall = SharePlan(ShareInput(2160, 3840)); var wide = SharePlan(ShareInput(4096, 2160));
                Check(hd.Width == 1920 && hd.Height == 1080 && uhd.Width == 3840 && uhd.Height == 2160 && tall.Width == 1080 && tall.Height == 1920 && wide.Width == 1920 && wide.Height == 1012, "Framing changed");
                var small = SharePlan(ShareInput(721, 1281, "60/1", false), QuickSharePreset.Uhd); Check(small.Width == 720 && small.Height == 1280 && small.FrameRate == 30 && !small.HasAudio, "Small portrait upscaled/audio added");
            });
            Test("Quick Share display rotation handles either direction and first available rotation", delegate {
                foreach (int rotation in new[] { 90, -90, 270, -270 }) {
                    var input = ShareInput(); input.Video.SideData = new System.Collections.Generic.List<QuickShareSideData> { new QuickShareSideData(), new QuickShareSideData { Rotation = rotation } };
                    var plan = SharePlan(input); Check(plan.Width == 1080 && plan.Height == 1920, "Rotated landscape not preserved");
                }
                var turn = ShareInput(); turn.Video.SideData = new System.Collections.Generic.List<QuickShareSideData> { new QuickShareSideData { Rotation = 180 }, new QuickShareSideData { Rotation = 90 } }; Check(SharePlan(turn).Width == 1920, "Later rotation overrode first");
                turn.Video.SideData[0].Rotation = Int32.MinValue; SharePlan(turn);
            });
            Test("Quick Share keeps valid low rational rates and caps malformed high or injected rates", delegate {
                foreach (string rate in new[] { "24/1", "25/1", "30000/1001", "24000/1001", "1/2" }) { var plan = SharePlan(ShareInput(rate: rate)); Check(plan.Fps == rate && plan.FrameRate <= 30, "Valid lower rate changed"); }
                foreach (string rate in new[] { "60/1", "30001/1000", "0/0", "-1/1", "NaN/1", "30", "1/1,scale=100:100", "1e3/1", "9223372036854775808/1", "1/0" }) Check(SharePlan(ShareInput(rate: rate)).Fps == "30", "Unsafe/invalid rate reached filter");
            });
            Test("Quick Share finite duration and dimensions are required with bounded probe parsing", delegate {
                foreach (string duration in new[] { "0", "-1", "NaN", "Infinity", "wrong", "1e309", "1e308" }) { var input = ShareInput(); input.Video.Duration = duration; Throws<SafetyException>(() => SharePlan(input)); }
                var missing = ShareInput(); missing.Video.Duration = null; missing.Format.Duration = "5"; Check(SharePlan(missing).Seconds == 5, "Container duration fallback lost");
                missing.Video.Width = 0; Throws<SafetyException>(() => SharePlan(missing)); missing.Video.Width = 3840; missing.Video.Height = null; Throws<SafetyException>(() => SharePlan(missing));
                Throws<SafetyException>(() => QuickShareProbe.Parse(new byte[4000000])); Throws<SafetyException>(() => QuickShareProbe.Parse(Encoding.UTF8.GetBytes("{\"streams\":[null]}"))); Throws<SafetyException>(() => QuickShareProbe.Parse(Encoding.UTF8.GetBytes("garbage")));
                Throws<SafetyException>(() => new QuickSharePlan(ShareInput(), (QuickSharePreset)99, QuickShareColor.Standard)); Throws<SafetyException>(() => new QuickSharePlan(ShareInput(), QuickSharePreset.Hd, (QuickShareColor)99));
            });
            Test("Quick Share refuses PQ HLG and Dolby Vision for both explicit color choices", delegate {
                foreach (QuickShareColor color in Enum.GetValues(typeof(QuickShareColor))) {
                    foreach (string transfer in new[] { "smpte2084", "arib-std-b67" }) { var input = ShareInput(); input.Video.Transfer = transfer; Throws<SafetyException>(() => SharePlan(input, color: color)); }
                    var dolby = ShareInput(); dolby.Video.SideData = new System.Collections.Generic.List<QuickShareSideData> { new QuickShareSideData { Type = "DOVI configuration record" } }; Throws<SafetyException>(() => SharePlan(dolby, color: color));
                }
            });
            Test("Quick Share BT709 input tags never choose LUT and plan snapshots do not alias probes", delegate {
                var input = ShareInput(); var standard = SharePlan(input); var log = SharePlan(input, color: QuickShareColor.LunaILog); input.Video.Width = 100; input.Video.Duration = "999"; input.Audio.Duration = "99";
                Check(standard.Color == QuickShareColor.Standard && standard.LutSha == null && log.LutSha == QuickShareMedia.OfficialLutSha && standard.Width == 1920 && standard.Seconds == 5 && standard.AudioSeconds == 5, "Color inferred/plan mutated");
            });
            Test("Quick Share encode keeps original input and uses no-clobber software H264 SDR AAC recipe", delegate {
                string original = @"C:\Fixture with spaces\VID_日本 & sample.mp4", output = @"C:\Fixture with spaces\.easy-share-private\output.mp4"; var hd = SharePlan(); var args = hd.EncodeArguments(original, output);
                Check(ShareOption(args, "-i") == original && args.Last() == output && args.Contains("-n") && !args.Contains("-y") && ShareOption(args, "-c:v") == "libx264" && ShareOption(args, "-b:v") == "8000000" && ShareOption(args, "-c:a") == "aac" && ShareOption(args, "-b:a") == "160k" && ShareOption(args, "-ac") == "2", "Encoding contract changed");
                Check(ShareOption(args, "-map_metadata") == "-1" && ShareOption(args, "-map_chapters") == "-1" && ShareOption(args, "-metadata:s:v:0") == "rotate=0" && ShareOption(args, "-movflags") == "+faststart" && ShareOption(args, "-vf").Contains("setsar=1") && !ShareOption(args, "-vf").Contains("lut3d"), "Metadata/framing/manual color changed");
                Check(ShareOption(SharePlan(preset: QuickSharePreset.Uhd).EncodeArguments(original, output), "-b:v") == "24000000", "4K bitrate changed"); Throws<SafetyException>(() => hd.EncodeArguments(original, original));
            });
            Test("Quick Share LUT filter uses only its fixed private filename and invariant numbers", delegate {
                var before = Thread.CurrentThread.CurrentCulture;
                try { Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); var plan = SharePlan(ShareInput(rate: "30000/1001"), color: QuickShareColor.LunaILog); var filters = ShareOption(plan.EncodeArguments(@"C:\Fixture\VID_a.mp4", @"C:\Fixture\private\output.mp4"), "-vf"); Check(filters.Contains("fps=30000/1001") && filters.Contains("scale=1920:1080:flags=lanczos") && filters.Contains("format=gbrpf32le,lut3d=file=luna.cube:interp=tetrahedral") && filters.EndsWith("colorspace=bt709"), "Locale/path altered filter"); }
                finally { Thread.CurrentThread.CurrentCulture = before; }
            });
            Test("Quick Share accepts matching output with audio and a silent portrait without audio", delegate {
                var plan = SharePlan(); plan.ValidateOutput(ShareOutput(plan)); var portrait = SharePlan(ShareInput(720, 1280, "60/1", false)); portrait.ValidateOutput(ShareOutput(portrait));
            });
            Test("Quick Share output refuses wrong codec dimensions duration frame rate pixel format or SDR tags", delegate {
                var plan = SharePlan(); foreach (Action<QuickShareProbe> change in new Action<QuickShareProbe>[] { x => x.Video.Codec = "hevc", x => x.Video.Width = 1080, x => x.Video.Height = 1920, x => x.Video.Duration = "5.25", x => x.Video.Duration = "NaN", x => x.Video.FrameRate = "60/1", x => x.Video.FrameRate = "25/1", x => x.Video.PixelFormat = "yuv420p10le", x => x.Video.Range = "pc", x => x.Video.ColorSpace = null, x => x.Video.Primaries = "bt2020", x => x.Video.Transfer = "smpte2084" }) { var output = ShareOutput(plan); change(output); Throws<SafetyException>(() => plan.ValidateOutput(output)); }
                var near = ShareOutput(plan); near.Video.Duration = "5.249"; plan.ValidateOutput(near);
            });
            Test("Quick Share output refuses retained rotation Dolby Vision wrong or changed-duration audio", delegate {
                var plan = SharePlan(); var rotated = ShareOutput(plan); rotated.Video.SideData = new System.Collections.Generic.List<QuickShareSideData> { new QuickShareSideData { Rotation = 90 } }; Throws<SafetyException>(() => plan.ValidateOutput(rotated));
                rotated.Video.SideData[0].Rotation = 0; rotated.Video.SideData[0].Type = "DOVI metadata"; Throws<SafetyException>(() => plan.ValidateOutput(rotated));
                foreach (Action<QuickShareProbe> change in new Action<QuickShareProbe>[] { x => x.Streams.Remove(x.Audio), x => x.Audio.Codec = "pcm_s16le", x => x.Audio.Channels = 1, x => x.Audio.Duration = "4.75" }) { var output = ShareOutput(plan); change(output); Throws<SafetyException>(() => plan.ValidateOutput(output)); }
                var silent = SharePlan(ShareInput(audio: false)); var extra = ShareOutput(silent); extra.Streams.Add(new QuickShareStream { Type = "audio", Codec = "aac", Channels = 2 }); Throws<SafetyException>(() => silent.ValidateOutput(extra));
            });
            Test("Quick Share full-decode command includes complete video and optional audio with deadlines", delegate {
                var plan = SharePlan(); var decode = QuickSharePlan.DecodeArguments(@"C:\Fixture\private\output.mp4"); Check(decode.Contains("-xerror") && ShareOption(decode, "-f") == "null" && decode.Contains("0:v:0") && decode.Contains("0:a:0?") && !decode.Contains("-t") && !decode.Contains("-frames:v") && plan.EncodeTimeout == 180 && plan.DecodeTimeout == 180, "Decode is partial or deadline lost");
                var input = ShareInput(); input.Video.Duration = "100"; var longPlan = SharePlan(input); Check(longPlan.EncodeTimeout == 2060 && longPlan.DecodeTimeout == 1060, "Duration deadline parity lost");
                Check(QuickSharePlan.ProbeArguments(@"C:\Fixture\output.mp4").Contains("-show_streams") && QuickSharePlan.ProbeArguments(@"C:\Fixture\output.mp4").Contains("-show_format"), "Probe scope lost");
            });
            Test("Quick Share official LUT refuses wrong empty or oversized data and cancellation without changing it", delegate {
                using (var fixture = new Fixture()) {
                    string path = Path.Combine(fixture.Source, "wrong.cube"); File.WriteAllText(path, "LUT_3D_SIZE 2"); byte[] before = File.ReadAllBytes(path); Throws<SafetyException>(() => QuickShareMedia.ReadOfficialLut(path, CancellationToken.None)); Check(File.ReadAllBytes(path).SequenceEqual(before), "Rejected LUT was modified");
                    File.WriteAllBytes(path, new byte[0]); Throws<SafetyException>(() => QuickShareMedia.ReadOfficialLut(path, CancellationToken.None)); File.WriteAllBytes(path, new byte[2000000]); Throws<SafetyException>(() => QuickShareMedia.ReadOfficialLut(path, CancellationToken.None));
                    using (var token = new CancellationTokenSource()) { token.Cancel(); Throws<OperationCanceledException>(() => QuickShareMedia.ReadOfficialLut(path, token.Token)); }
                    using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Throws<System.ComponentModel.Win32Exception>(() => QuickShareMedia.ReadOfficialLut(path, CancellationToken.None));
                }
            });
            Test("Quick Share LUT rejects redirected directories and leaves targets untouched", delegate {
                using (var fixture = new Fixture()) { File.WriteAllText(Path.Combine(fixture.Destination, "luna.cube"), "fixture LUT"); string link = Path.Combine(fixture.Source, "link"); CreateJunction(fixture, link, fixture.Destination); Throws<SafetyException>(() => QuickShareMedia.ReadOfficialLut(Path.Combine(link, "luna.cube"), CancellationToken.None)); Check(File.ReadAllText(Path.Combine(fixture.Destination, "luna.cube")) == "fixture LUT", "Redirected target changed"); }
            });
        }
    }
}
