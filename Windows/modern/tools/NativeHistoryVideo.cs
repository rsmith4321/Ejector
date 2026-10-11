using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using EasyEject;

// Explicit supplied-tool preparation only. No camera input, configuration writes,
// LUT, export/eject permission or child-process implementation is introduced here.
internal sealed class NativeHistoryVideo : IDisposable {
    readonly string ffmpeg, ffprobe;
    IDisposable directories;
    FileStream encoder, probe;
    internal NativeHistoryVideo(string ffmpeg, string encoderHash, string ffprobe, string probeHash) {
        this.ffmpeg = SafePaths.Normalize(Path.GetFullPath(ffmpeg));
        this.ffprobe = SafePaths.Normalize(Path.GetFullPath(ffprobe));
        if (!Path.GetExtension(this.ffmpeg).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetExtension(this.ffprobe).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Supply existing native Windows tool executables.");
        try {
            directories = SafePaths.LockDirectories(Path.GetDirectoryName(this.ffmpeg), Path.GetDirectoryName(this.ffprobe));
            encoder = Pin(this.ffmpeg, encoderHash); probe = Pin(this.ffprobe, probeHash);
        } catch { Dispose(); throw; }
    }
    static FileStream Pin(string path, string expected) {
        var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try {
            if (held.Length == 0 || !Convert.ToHexString(SHA256.HashData(held)).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("Supplied tool hash differs; no generated media was created.");
            held.Position = 0; return held;
        } catch { held.Dispose(); throw; }
    }
    byte[] Run(string tool, string[] args, string directory, double timeout = 30) {
        // Use the exact reviewed backend's already-contained native tool runner.
        var type = typeof(VerifiedImporter).Assembly.GetType("EasyEject.WindowsQuickShareProcess", true);
        var process = Activator.CreateInstance(type, true);
        return (byte[])type.GetMethod("Run", BindingFlags.Public | BindingFlags.Instance).Invoke(process,
            new object[] { tool, args, directory, timeout, CancellationToken.None, new Action(() => {}), null });
    }
    internal void Generate(string root, string source, bool uhd = false) {
        string path = Path.Combine(source, "VID_generated_first.mp4");
        int width = uhd ? 3840 : 640, height = uhd ? 2160 : 360;
        var arguments = new List<string> { "-nostdin", "-hide_banner", "-v", "error", "-xerror", "-f", "lavfi", "-i", uhd ? "testsrc2=size=3840x2160:rate=30" : "testsrc2=size=640x360:rate=30" };
        if (uhd) arguments.AddRange(new[] { "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-map", "0:v:0", "-map", "1:a:0" });
        else arguments.AddRange(new[] { "-map", "0:v:0", "-an" });
        arguments.AddRange(new[] { "-t", uhd ? "15" : "1", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "28", "-threads", "2", "-pix_fmt", "yuv420p",
            "-color_range", "tv", "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709" });
        if (uhd) arguments.AddRange(new[] { "-c:a", "aac", "-b:a", "160k", "-ac", "1", "-ar", "48000" });
        arguments.AddRange(new[] { "-movflags", "+faststart", "-n", path });
        Run(ffmpeg, arguments.ToArray(), root, uhd ? 180 : 30);
        // Valid ignorable box models the existing identity-selection rule only.
        byte[] identity = Encoding.UTF8.GetBytes("Generated foreground fixture: Insta360 Luna Ultra");
        using (var output = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            if (output.Length == 0) throw new IOException("Generator returned an empty clip.");
            output.Position = output.Length; int size = identity.Length + 8;
            output.Write(new[] { (byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size, (byte)'f', (byte)'r', (byte)'e', (byte)'e' });
            output.Write(identity); output.Flush(true);
        }
        using (var original = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            byte[] json = Run(ffprobe, new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", path }, root);
            using (var document = JsonDocument.Parse(json)) {
                var streams = document.RootElement.GetProperty("streams");
                if (streams.GetArrayLength() != (uhd ? 2 : 1) || streams[0].GetProperty("codec_type").GetString() != "video" ||
                    streams[0].GetProperty("codec_name").GetString() != "h264" ||
                    streams[0].GetProperty("width").GetInt32() != width || streams[0].GetProperty("height").GetInt32() != height ||
                    streams[0].GetProperty("avg_frame_rate").GetString() != "30/1") throw new IOException("Generated video probe differs from the bounded fixture recipe.");
                if (uhd && (streams[1].GetProperty("codec_type").GetString() != "audio" || streams[1].GetProperty("codec_name").GetString() != "aac" ||
                    streams[1].GetProperty("sample_rate").GetString() != "48000" || streams[1].GetProperty("channels").GetInt32() != 1 ||
                    !(Math.Abs(Double.Parse(document.RootElement.GetProperty("format").GetProperty("duration").GetString(), System.Globalization.CultureInfo.InvariantCulture) - 15) <= 0.1)))
                    throw new IOException("Generated UHD/audio probe differs from the bounded fixture recipe.");
            }
            Run(ffmpeg, new[] { "-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v:0", "-map", "0:a:0?", "-f", "null", "-" }, root, uhd ? 90 : 30);
            using (var receipt = new FileStream(Path.Combine(root, "generated-video-probe.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None)) { receipt.Write(json); receipt.Flush(true); }
            File.Copy(path, Path.Combine(source, "VID_generated_second.mp4"), false);
        }
    }
    public void Dispose() { probe?.Dispose(); encoder?.Dispose(); directories?.Dispose(); probe = encoder = null; directories = null; }
}
