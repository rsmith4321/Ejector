using System;
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
    byte[] Run(string tool, string[] args, string directory) {
        // Use the exact reviewed backend's already-contained native tool runner.
        var type = typeof(VerifiedImporter).Assembly.GetType("EasyEject.WindowsQuickShareProcess", true);
        var process = Activator.CreateInstance(type, true);
        return (byte[])type.GetMethod("Run", BindingFlags.Public | BindingFlags.Instance).Invoke(process,
            new object[] { tool, args, directory, 30d, CancellationToken.None, new Action(() => {}), null });
    }
    internal void Generate(string root, string source) {
        string path = Path.Combine(source, "VID_generated_first.mp4");
        Run(ffmpeg, new[] { "-nostdin", "-hide_banner", "-v", "error", "-xerror", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30",
            "-map", "0:v:0", "-an", "-t", "1", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "28", "-threads", "2", "-pix_fmt", "yuv420p",
            "-color_range", "tv", "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-movflags", "+faststart", "-n", path }, root);
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
                if (streams.GetArrayLength() != 1 || streams[0].GetProperty("codec_type").GetString() != "video" ||
                    streams[0].GetProperty("width").GetInt32() != 640 || streams[0].GetProperty("height").GetInt32() != 360 ||
                    streams[0].GetProperty("avg_frame_rate").GetString() != "30/1") throw new IOException("Generated video probe differs from the bounded fixture recipe.");
            }
            Run(ffmpeg, new[] { "-nostdin", "-v", "error", "-xerror", "-i", path, "-map", "0:v:0", "-map", "0:a:0?", "-f", "null", "-" }, root);
            using (var receipt = new FileStream(Path.Combine(root, "generated-video-probe.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None)) { receipt.Write(json); receipt.Flush(true); }
            File.Copy(path, Path.Combine(source, "VID_generated_second.mp4"), false);
        }
    }
    public void Dispose() { probe?.Dispose(); encoder?.Dispose(); directories?.Dispose(); probe = encoder = null; directories = null; }
}
