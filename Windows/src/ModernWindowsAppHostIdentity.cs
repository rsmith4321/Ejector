using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace EasyEject {
    // Declared native host layout/resources only. Publisher/content approval is
    // independent; this does not verify native code bodies or grant execution.
    internal static class ModernWindowsAppHostIdentity {
        internal const int MaximumBytes = 16777216;
        static readonly byte[] Marker = { 0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38, 0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32, 0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18, 0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae };
        static readonly byte[] Search = Encoding.ASCII.GetBytes("19ff3e9c3602ae8e841925bb461a0adb064a1f1903667a5e0d87e8f608f425ac");
        static readonly byte[] Application = Encoding.ASCII.GetBytes("EasyEject.Modern.dll");
        internal static void Require(ModernWindowsPackageContentReview content, CancellationToken cancel) {
            if (content == null) throw new ArgumentNullException("content"); cancel.ThrowIfCancellationRequested(); content.Revalidate(cancel);
            content.Inspect("EasyEject.Modern.exe", stream => {
                ReadLayout(stream, cancel);
                // SafePaths opens a native handle, so FileStream.Name can be
                // "[Unknown]". Bind the resource API's path to that held handle.
                string path = SafePaths.Normalize(Path.Combine(content.Folder, "EasyEject.Modern.exe"));
                if (!Native.FinalPath(stream.SafeFileHandle).Equals(path, StringComparison.OrdinalIgnoreCase)) Fail("Native apphost resource path differs from held bytes.");
                var info = FileVersionInfo.GetVersionInfo(path);
                RequireResources(info.ProductName, info.FileDescription, info.OriginalFilename, info.InternalName, info.FileVersion, info.ProductVersion, content.Release.Version);
                cancel.ThrowIfCancellationRequested();
            }, cancel);
            content.Revalidate(cancel);
        }
        internal static void RequireResources(string product, string title, string original, string name, string fileVersion, string productVersion, string version) {
            if (!WindowsReleaseManifest.StableVersion(version) || product != "Easy Eject" || title != "Easy Eject for Windows" || original != "EasyEject.Modern.dll" || name != "EasyEject.Modern.dll" || fileVersion != version || productVersion != version)
                Fail("Native apphost application/version resources differ from the authenticated release.");
        }
        internal static void ReadLayout(FileStream stream, CancellationToken cancel) {
            if (stream == null) throw new ArgumentNullException("stream"); cancel.ThrowIfCancellationRequested();
            if (stream.Length < 64 || stream.Length > MaximumBytes) Fail("Native apphost size is unsupported.");
            var bytes = new byte[(int)stream.Length]; stream.Position = 0; int done = 0;
            while (done < bytes.Length) { cancel.ThrowIfCancellationRequested(); int read = stream.Read(bytes, done, Math.Min(65536, bytes.Length - done)); if (read == 0) Fail("Incomplete native apphost bytes."); done += read; }
            RequireLayout(bytes, cancel);
        }
        internal static void RequireLayout(byte[] bytes, CancellationToken cancel) {
            cancel.ThrowIfCancellationRequested();
            if (bytes == null || bytes.Length < 64 || bytes.Length > MaximumBytes || U16(bytes, 0) != 0x5a4d) Fail("Missing bounded native PE image.");
            uint offset = U32(bytes, 60); if (offset < 64 || offset > 1048576) Fail("Unsupported PE header location."); int pe = (int)offset;
            ushort count = U16(bytes, pe + 6), flags = U16(bytes, pe + 22);
            if (U32(bytes, pe) != 0x4550 || U16(bytes, pe + 4) != 0x8664 || count == 0 || count > 96 || U16(bytes, pe + 20) != 240 || (flags & 2) == 0 || (flags & 0x2000) != 0) Fail("Unsupported native x64 executable header.");
            int optional = pe + 24, table = optional + 240; Bounds(bytes, table, count * 40);
            if (U16(bytes, optional) != 0x20b || U16(bytes, optional + 68) != 2 || U32(bytes, optional + 108) != 16 || U32(bytes, optional + 224) != 0 || U32(bytes, optional + 228) != 0) Fail("Native GUI apphost cannot have a managed CLR directory.");
            uint entry = U32(bytes, optional + 16), headers = U32(bytes, optional + 60); bool entryFound = false;
            if (entry == 0 || headers < table + count * 40 || headers > bytes.Length) Fail("Native apphost has no bounded entry point/headers.");
            for (int i = 0; i < count; i++) {
                int row = table + i * 40; uint raw = U32(bytes, row + 20), size = U32(bytes, row + 16), rva = U32(bytes, row + 12), characteristics = U32(bytes, row + 36);
                if (size == 0) continue;
                if (raw < headers || (ulong)raw + size > (ulong)bytes.Length) Fail("Native section exceeds its image.");
                for (int j = 0; j < i; j++) {
                    int other = table + j * 40; uint otherRaw = U32(bytes, other + 20), otherSize = U32(bytes, other + 16), otherRva = U32(bytes, other + 12);
                    if (otherSize != 0 && (Overlap(raw, size, otherRaw, otherSize) || Overlap(rva, size, otherRva, otherSize))) Fail("Native section mapping is ambiguous.");
                }
                if ((ulong)entry >= rva && (ulong)entry < (ulong)rva + size) {
                    if (entryFound || (characteristics & 0xe0000000U) != 0x60000000U) Fail("Native entry point is not uniquely readable executable code."); entryFound = true;
                }
            }
            if (!entryFound) Fail("Native entry point is outside executable section bytes.");
            // Reviewed .NET 10 Windows template: writable non-executable data
            // contains the bundle locator, default search slot and app slot.
            // Reject other layouts rather than treating a planted name as binding.
            int signature = Unique(bytes, Marker, cancel), start = signature - 8;
            Bounds(bytes, start, 40 + 512 + 1025); Zero(bytes, start, 8);
            int search = start + 40, app = search + 512; Zero(bytes, search, 2); Match(bytes, search + 2, Search); Zero(bytes, search + 2 + Search.Length, 512 - 2 - Search.Length);
            Match(bytes, app, Application); Zero(bytes, app + Application.Length, 1025 - Application.Length);
            int mapped = 0;
            for (int i = 0; i < count; i++) {
                int row = table + i * 40; uint raw = U32(bytes, row + 20), size = U32(bytes, row + 16), characteristics = U32(bytes, row + 36);
                if ((ulong)start >= raw && (ulong)start + 40 + 512 + 1025 <= (ulong)raw + size) { if ((characteristics & 0xe0000040U) != 0xc0000040U) Fail("Host binding is outside readable writable non-executable initialized data."); mapped++; }
            }
            if (mapped != 1) Fail("Host binding has no unique bounded native section."); cancel.ThrowIfCancellationRequested();
        }
        static bool Overlap(uint a, uint length, uint b, uint otherLength) { return (ulong)a < (ulong)b + otherLength && (ulong)b < (ulong)a + length; }
        static ushort U16(byte[] bytes, int offset) { Bounds(bytes, offset, 2); return BitConverter.ToUInt16(bytes, offset); }
        static uint U32(byte[] bytes, int offset) { Bounds(bytes, offset, 4); return BitConverter.ToUInt32(bytes, offset); }
        static void Bounds(byte[] bytes, int offset, int size) { if (bytes == null || offset < 0 || size < 0 || offset > bytes.Length - size) Fail("Truncated native apphost structure."); }
        static void Match(byte[] bytes, int offset, byte[] expected) { Bounds(bytes, offset, expected.Length); for (int i = 0; i < expected.Length; i++) if (bytes[offset + i] != expected[i]) Fail("Unsupported native host application/search binding."); }
        static void Zero(byte[] bytes, int offset, int size) { Bounds(bytes, offset, size); for (int i = 0; i < size; i++) if (bytes[offset + i] != 0) Fail("Native bundle/search/application slot differs from reviewed policy."); }
        static int Unique(byte[] bytes, byte[] pattern, CancellationToken cancel) {
            int found = -1;
            for (int i = 0; i <= bytes.Length - pattern.Length; i++) {
                if ((i & 4095) == 0) cancel.ThrowIfCancellationRequested();
                if (bytes[i] != pattern[0]) continue; int j = 1; while (j < pattern.Length && bytes[i + j] == pattern[j]) j++;
                if (j != pattern.Length) continue; if (found >= 0) Fail("Ambiguous native host locator."); found = i;
            }
            if (found < 8) Fail("Missing native host bundle locator."); return found;
        }
        static void Fail(string reason) { throw new SafetyException(reason); }
    }
}
