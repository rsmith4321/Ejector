using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace EasyEject {
    internal enum QuickSharePreset { Hd, Uhd }
    internal enum QuickShareColor { Standard, LunaILog }

    // This policy does not discover clips, execute tools, publish files or grant eject authority.
    // Its caller must hold the exact receipt/originals and a fresh destination reservation.
    internal static class QuickShareMedia {
        internal const string OfficialLutName = "Luna_I-Log_to_Rec709_BT1886_s33_v2.cube";
        internal const string OfficialLutSha = "e3e5c3ab4ca7c166f3ebb740b45ed8cbce649b0c18dca9d5661754c53b9ee17d";
        internal static bool IsLunaMaster(string path, Stream held) {
            if (!Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(path).StartsWith("VID_", StringComparison.OrdinalIgnoreCase)) return false;
            long position = held.Position;
            try {
                held.Position = Math.Max(0, held.Length - 65536); byte[] tail = new byte[held.Length - held.Position];
                int offset = 0, count; while (offset < tail.Length && (count = held.Read(tail, offset, tail.Length - offset)) > 0) offset += count;
                if (offset != tail.Length) throw new SafetyException("The saved original could not be read completely.");
                string identity = Encoding.UTF8.GetString(tail);
                return identity.Contains("Insta360 Luna Ultra") || identity.Contains("Insta360 Luna Pro");
            } finally { held.Position = position; }
        }
        internal static byte[] ReadOfficialLut(string path, CancellationToken cancel) {
            path = SafePaths.Normalize(path);
            using (SafePaths.LockDirectories(Path.GetDirectoryName(path))) using (var held = SafePaths.ReadFile(path)) {
                cancel.ThrowIfCancellationRequested();
                if (held.Length <= 0 || held.Length >= 2000000) throw new SafetyException("Choose the official Luna Rec.709 s33 v2 LUT.");
                if (ImportHistory.Hash(held, cancel) != OfficialLutSha) throw new SafetyException("Choose " + OfficialLutName + " from Insta360's official Luna LUT download.");
                held.Position = 0; var bytes = new byte[held.Length]; int offset = 0, count;
                while (offset < bytes.Length && (count = held.Read(bytes, offset, bytes.Length - offset)) > 0) { cancel.ThrowIfCancellationRequested(); offset += count; }
                if (offset != bytes.Length) throw new SafetyException("The Luna LUT could not be read completely.");
                cancel.ThrowIfCancellationRequested(); return bytes;
            }
        }
    }
    [DataContract] internal sealed class QuickShareSideData {
        [DataMember(Name = "rotation")] internal int? Rotation { get; set; }
        [DataMember(Name = "side_data_type")] internal string Type { get; set; }
    }
    [DataContract] internal sealed class QuickShareStream {
        [DataMember(Name = "codec_type")] internal string Type { get; set; }
        [DataMember(Name = "codec_name")] internal string Codec { get; set; }
        [DataMember(Name = "width")] internal int? Width { get; set; }
        [DataMember(Name = "height")] internal int? Height { get; set; }
        [DataMember(Name = "r_frame_rate")] internal string FrameRate { get; set; }
        [DataMember(Name = "duration")] internal string Duration { get; set; }
        [DataMember(Name = "color_space")] internal string ColorSpace { get; set; }
        [DataMember(Name = "color_transfer")] internal string Transfer { get; set; }
        [DataMember(Name = "color_primaries")] internal string Primaries { get; set; }
        [DataMember(Name = "color_range")] internal string Range { get; set; }
        [DataMember(Name = "pix_fmt")] internal string PixelFormat { get; set; }
        [DataMember(Name = "channels")] internal int? Channels { get; set; }
        [DataMember(Name = "side_data_list")] internal List<QuickShareSideData> SideData { get; set; }
    }
    [DataContract] internal sealed class QuickShareFormat { [DataMember(Name = "duration")] internal string Duration { get; set; } [DataMember(Name = "format_name")] internal string Name { get; set; } }
    [DataContract] internal sealed class QuickShareProbe {
        [DataMember(Name = "streams")] internal List<QuickShareStream> Streams { get; set; }
        [DataMember(Name = "format")] internal QuickShareFormat Format { get; set; }
        internal QuickShareStream Video { get { return Streams == null ? null : Streams.FirstOrDefault(x => x != null && x.Type == "video"); } }
        internal QuickShareStream Audio { get { return Streams == null ? null : Streams.FirstOrDefault(x => x != null && x.Type == "audio"); } }
        internal double Seconds { get { return Number(Video == null || Video.Duration == null ? (Format == null ? null : Format.Duration) : Video.Duration) ?? 0; } }
        internal static double? Number(string value) { double result; return Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && Finite(result) ? result : (double?)null; }
        internal static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        internal static QuickShareProbe Parse(byte[] json) {
            if (json == null || json.Length == 0 || json.Length >= 4000000) throw new SafetyException("Quick Share received invalid or excessive probe output.");
            try {
                using (var data = new MemoryStream(json)) {
                    var value = (QuickShareProbe)new DataContractJsonSerializer(typeof(QuickShareProbe)).ReadObject(data);
                    if (value == null || value.Streams == null || value.Streams.Any(x => x == null || x.SideData != null && x.SideData.Any(s => s == null))) throw new SafetyException("Quick Share probe streams are invalid.");
                    return value;
                }
            } catch (SerializationException ex) { throw new SafetyException("Quick Share could not read the video probe: " + ex.Message); }
        }
    }
    internal sealed class QuickSharePlan {
        // Windows uses an explicit software encoder, with no silent hardware/recipe fallback.
        // A distinct recipe prevents reuse of exports whose encoder contract differs.
        internal const string Recipe = "windows-luna-share-v1-libx264-h264-30fps-aac160";
        internal readonly QuickSharePreset Preset; internal readonly QuickShareColor Color;
        internal readonly int Width, Height, Bitrate; internal readonly string Fps;
        internal readonly double Seconds, FrameRate, EncodeTimeout, DecodeTimeout;
        internal readonly bool HasAudio; internal readonly double? AudioSeconds;
        internal string PresetName { get { return Preset == QuickSharePreset.Hd ? "1080p" : "4K"; } }
        internal string LutSha { get { return Color == QuickShareColor.LunaILog ? QuickShareMedia.OfficialLutSha : null; } }
        internal QuickSharePlan(QuickShareProbe input, QuickSharePreset preset, QuickShareColor color) {
            if (!Enum.IsDefined(typeof(QuickSharePreset), preset) || !Enum.IsDefined(typeof(QuickShareColor), color)) throw new SafetyException("Choose a supported Quick Share size and color.");
            if (input == null || input.Video == null || input.Video.Width.GetValueOrDefault() <= 0 || input.Video.Height.GetValueOrDefault() <= 0 || input.Seconds <= 0 || !QuickShareProbe.Finite(input.Seconds)) throw new SafetyException("Quick Share could not read the Luna video's duration or dimensions.");
            var video = input.Video;
            if (video.Transfer == "smpte2084" || video.Transfer == "arib-std-b67" || video.SideData != null && video.SideData.Any(x => x != null && x.Type != null && x.Type.IndexOf("DOVI", StringComparison.OrdinalIgnoreCase) >= 0)) throw new SafetyException("Quick Share supports Luna I-Log and standard SDR clips. This clip is HDR or Dolby Vision.");
            long rotation = video.SideData == null ? 0 : video.SideData.Where(x => x != null && x.Rotation.HasValue).Select(x => (long)x.Rotation.Value).FirstOrDefault();
            bool turned = Math.Abs(rotation) % 180 == 90;
            int width = turned ? video.Height.Value : video.Width.Value, height = turned ? video.Width.Value : video.Height.Value;
            double scale = Math.Min(1, Math.Min((preset == QuickSharePreset.Hd ? 1920.0 : 3840.0) / Math.Max(width, height), (preset == QuickSharePreset.Hd ? 1080.0 : 2160.0) / Math.Min(width, height)));
            Width = Math.Max(2, (int)(width * scale) / 2 * 2); Height = Math.Max(2, (int)(height * scale) / 2 * 2);
            Preset = preset; Color = color; Seconds = input.Seconds; Bitrate = preset == QuickSharePreset.Hd ? 8000000 : 24000000;
            double rate; string fps; ParseRate(video.FrameRate, out rate, out fps); FrameRate = rate > 0 && rate <= 30 ? rate : 30; Fps = rate > 0 && rate <= 30 ? fps : "30";
            HasAudio = input.Audio != null; AudioSeconds = HasAudio ? QuickShareProbe.Number(input.Audio.Duration) : null;
            EncodeTimeout = Math.Max(180, Seconds * 20 + 60); DecodeTimeout = Math.Max(180, Seconds * 10 + 60);
            if (!QuickShareProbe.Finite(EncodeTimeout) || !QuickShareProbe.Finite(DecodeTimeout)) throw new SafetyException("Quick Share duration exceeds a safe process deadline.");
        }
        static void ParseRate(string value, out double rate, out string safe) {
            rate = 0; safe = "30"; if (value == null) return;
            string[] parts = value.Split('/'); if (parts.Length != 2) return;
            // Only integer rational tokens can enter the FFmpeg filter. Probe text is untrusted.
            long numerator, denominator;
            if (!Int64.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out numerator) || !Int64.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out denominator) || numerator <= 0 || denominator <= 0) return;
            rate = (double)numerator / denominator; safe = numerator.ToString(CultureInfo.InvariantCulture) + "/" + denominator.ToString(CultureInfo.InvariantCulture);
        }
        internal string[] EncodeArguments(string original, string temporary) {
            original = SafePaths.Normalize(original); temporary = SafePaths.Normalize(temporary);
            if (original.Equals(temporary, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Quick Share cannot replace its original.");
            var filters = new List<string> { "fps=" + Fps, "scale=" + Width.ToString(CultureInfo.InvariantCulture) + ":" + Height.ToString(CultureInfo.InvariantCulture) + ":flags=lanczos" };
            if (Color == QuickShareColor.LunaILog) filters.AddRange(new[] { "format=gbrpf32le", "lut3d=file=luna.cube:interp=tetrahedral" });
            filters.AddRange(new[] { "scale=in_range=auto:out_range=tv:out_color_matrix=bt709", "format=yuv420p", "setsar=1", "setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709" });
            return new[] { "-nostdin", "-hide_banner", "-v", "error", "-xerror", "-nostats", "-stats_period", "0.5", "-progress", "pipe:1",
                "-i", original, "-map", "0:v:0", "-map", "0:a:0?", "-map_metadata", "-1", "-map_chapters", "-1", "-vf", String.Join(",", filters),
                "-c:v", "libx264", "-preset", "medium", "-b:v", Bitrate.ToString(CultureInfo.InvariantCulture), "-profile:v", "high", "-pix_fmt", "yuv420p",
                "-color_range", "tv", "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-metadata:s:v:0", "rotate=0",
                "-c:a", "aac", "-b:a", "160k", "-ac", "2", "-movflags", "+faststart", "-n", temporary };
        }
        internal static string[] ProbeArguments(string path) { return new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", SafePaths.Normalize(path) }; }
        internal static string[] DecodeArguments(string path) { return new[] { "-nostdin", "-v", "error", "-xerror", "-i", SafePaths.Normalize(path), "-map", "0:v:0", "-map", "0:a:0?", "-f", "null", "-" }; }
        internal void ValidateOutput(QuickShareProbe output) {
            var video = output == null ? null : output.Video; string ignored; double rate = 0; if (video != null) ParseRate(video.FrameRate, out rate, out ignored);
            if (video == null || output.Streams == null || output.Streams.Count != (HasAudio ? 2 : 1) || output.Streams.Count(x => x != null && x.Type == "video") != 1 || output.Streams.Count(x => x != null && x.Type == "audio") != (HasAudio ? 1 : 0)
                || output.Format == null || output.Format.Name == null || !output.Format.Name.Split(',').Contains("mp4") || video.Codec != "h264" || video.Width != Width || video.Height != Height || Math.Abs(output.Seconds - Seconds) >= 0.25 || output.Seconds <= 0
                || video.Primaries != "bt709" || video.Transfer != "bt709" || video.ColorSpace != "bt709" || video.Range != "tv" || video.PixelFormat != "yuv420p"
                || rate <= 0 || rate > 30 || Math.Abs(rate - FrameRate) > 0.001
                || video.SideData != null && video.SideData.Any(x => x != null && (x.Rotation.GetValueOrDefault() % 360 != 0 || x.Type != null && x.Type.IndexOf("DOVI", StringComparison.OrdinalIgnoreCase) >= 0))
                || HasAudio && (output.Audio == null || output.Audio.Codec != "aac" || output.Audio.Channels != 2) || !HasAudio && output.Audio != null)
                throw new SafetyException("The sharing copy failed dimension, duration, frame-rate, SDR color or audio verification. The full-quality original is unchanged.");
            double? audio = output.Audio == null ? null : QuickShareProbe.Number(output.Audio.Duration);
            if (AudioSeconds.HasValue && audio.HasValue && Math.Abs(AudioSeconds.Value - audio.Value) >= 0.25) throw new SafetyException("The sharing copy's audio duration differs from the original.");
        }
    }
}
