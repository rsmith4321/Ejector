using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static void ModernWindowsAppHostIdentityTests() {
            Test("modern native apphost resource policy requires exact stable application and versions", delegate {
                var values = new[] { "Easy Eject", "Easy Eject for Windows", "EasyEject.Modern.dll", "EasyEject.Modern.dll", "0.2.0.0", "0.2.0.0", "0.2.0.0" };
                NativeHostResources(values);
                for (int i = 0; i < values.Length; i++) { var changed = (string[])values.Clone(); changed[i] = i < 4 ? "Other" : "0.2.0-dev"; Throws<SafetyException>(() => NativeHostResources(changed)); changed[i] = null; Throws<SafetyException>(() => NativeHostResources(changed)); }
                var mismatch = (string[])values.Clone(); mismatch[6] = "0.2.0.1"; Throws<SafetyException>(() => NativeHostResources(mismatch));
            });
            Test("modern native apphost parser refuses absent truncated excessive and nonnative snapshots", delegate {
                foreach (var bytes in new[] { null, new byte[0], new byte[64], new byte[ModernWindowsAppHostIdentity.MaximumBytes + 1] }) Throws<SafetyException>(() => ModernWindowsAppHostIdentity.RequireLayout(bytes, CancellationToken.None));
                byte[] classic = File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EasyEject.Windows.exe")); Throws<SafetyException>(() => ModernWindowsAppHostIdentity.RequireLayout(classic, CancellationToken.None));
            });
            Test("modern native apphost held review refuses null cancellation invalid native bytes and disposed owner", delegate {
                Throws<ArgumentNullException>(() => ModernWindowsAppHostIdentity.Require(null, CancellationToken.None));
                using (var f = new ModernPackageFixture()) using (var cancel = new CancellationTokenSource()) {
                    using (var review = f.Review()) { Throws<SafetyException>(() => ModernWindowsAppHostIdentity.Require(review, CancellationToken.None)); cancel.Cancel(); Throws<OperationCanceledException>(() => ModernWindowsAppHostIdentity.Require(review, cancel.Token)); }
                    f.RequireReleased(); var ended = f.Review(); ended.Dispose(); Throws<ObjectDisposedException>(() => ModernWindowsAppHostIdentity.Require(ended, CancellationToken.None)); f.RequireReleased();
                }
            });
        }
        static void NativeHostResources(string[] values) { ModernWindowsAppHostIdentity.RequireResources(values[0], values[1], values[2], values[3], values[4], values[5], values[6]); }
        static byte[] NativeHostStable(byte[] sample) {
            byte[] bytes = (byte[])sample.Clone(); byte[] replacement = new byte[Encoding.Unicode.GetByteCount("0.2.0-dev\0")]; Encoding.Unicode.GetBytes("0.2.0.0\0").CopyTo(replacement, 0);
            Check(PatchFixtureBytes(bytes, Encoding.Unicode.GetBytes("0.2.0-dev\0"), replacement) == 1, "Ambiguous owned product-version resource patch"); return bytes;
        }
        static void NativeHostPublish(ModernPackageFixture f, byte[] bytes) {
            string name = "EasyEject.Modern.exe"; File.WriteAllBytes(f.PathFor(name), bytes); f.Metadata.Files.RemoveAll(value => value.Name == name); f.Metadata.Files.Add(new WindowsReleaseFile(name, bytes.Length, WindowsSignerEvidence.Hash(bytes))); f.Metadata.Version = "0.2.0.0"; f.Publish();
        }
        static void NativeHostActualResource(byte[] bytes, string version) {
            using (var f = new Fixture()) {
                string path = Path.Combine(f.Destination, "OwnedHost.exe"); File.WriteAllBytes(path, bytes);
                using (var parents = SafePaths.HoldDirectories(f.Destination)) using (var held = SafePaths.ReadFile(path, 65536)) {
                    var before = Native.Info(held.SafeFileHandle); string hash = VerifiedImporter.Hash(held, CancellationToken.None); ModernWindowsAppHostIdentity.ReadLayout(held, CancellationToken.None);
                    var info = FileVersionInfo.GetVersionInfo(path); ModernWindowsAppHostIdentity.RequireResources(info.ProductName, info.FileDescription, info.OriginalFilename, info.InternalName, info.FileVersion, info.ProductVersion, version);
                    parents.Validate(); held.Position = 0; Check(VerifiedImporter.SameStamp(before, Native.Info(held.SafeFileHandle)) && VerifiedImporter.Hash(held, CancellationToken.None) == hash, "Owned native resource inspection changed bytes");
                }
            }
        }
        static int NativeHostAppOffset(byte[] bytes) {
            byte[] pattern = Encoding.ASCII.GetBytes("EasyEject.Modern.dll\0"); int found = -1;
            for (int i = 0; i <= bytes.Length - pattern.Length; i++) if (bytes[i] == pattern[0] && bytes.Skip(i).Take(pattern.Length).SequenceEqual(pattern)) { Check(found < 0, "Ambiguous actual app slot fixture"); found = i; }
            Check(found >= 552, "Missing actual app slot fixture"); return found;
        }
        static void ModernWindowsAppHostActualTests(byte[] sample) {
            Test("modern native apphost reads actual owned development host and refuses its stable release resource claim", delegate {
                ModernWindowsAppHostIdentity.RequireLayout(sample, CancellationToken.None); Throws<SafetyException>(() => NativeHostActualResource(sample, "0.2.0.0"));
            });
            Test("modern native apphost accepts exact owned stable resource copy and authenticated held binding", delegate {
                byte[] bytes = NativeHostStable(sample); NativeHostActualResource(bytes, "0.2.0.0");
                using (var f = new ModernPackageFixture()) { NativeHostPublish(f, bytes); using (var review = f.Review()) { ModernWindowsAppHostIdentity.Require(review, CancellationToken.None); Throws<IOException>(() => File.WriteAllText(f.PathFor("EasyEject.Modern.exe"), "changed")); } Check(File.ReadAllBytes(f.PathFor("EasyEject.Modern.exe")).SequenceEqual(bytes), "Native host modified"); f.RequireReleased(); f.Metadata.Version = "0.2.0.1"; f.Publish(); using (var review = f.Review()) Throws<SafetyException>(() => ModernWindowsAppHostIdentity.Require(review, CancellationToken.None)); f.RequireReleased(); }
            });
            Test("modern native apphost refuses incompatible architecture subsystem CLR and unmapped entry point", delegate {
                foreach (string kind in new[] { "x86", "arm64", "console", "dll", "clr", "entry", "entrydata", "headers", "raw", "overlap" }) {
                    byte[] bytes = (byte[])sample.Clone(); int pe = (int)BitConverter.ToUInt32(bytes, 60), optional = pe + 24, table = optional + 240;
                    if (kind == "x86" || kind == "arm64") Array.Copy(BitConverter.GetBytes(kind == "x86" ? (ushort)0x14c : (ushort)0xaa64), 0, bytes, pe + 4, 2);
                    if (kind == "console") Array.Copy(BitConverter.GetBytes((ushort)3), 0, bytes, optional + 68, 2);
                    if (kind == "dll") Array.Copy(BitConverter.GetBytes((ushort)(BitConverter.ToUInt16(bytes, pe + 22) | 0x2000)), 0, bytes, pe + 22, 2);
                    if (kind == "clr") Array.Copy(BitConverter.GetBytes(72U), 0, bytes, optional + 228, 4);
                    if (kind == "entry") Array.Copy(BitConverter.GetBytes(0U), 0, bytes, optional + 16, 4);
                    if (kind == "entrydata") Array.Copy(BitConverter.GetBytes(BitConverter.ToUInt32(bytes, table + 40 + 12)), 0, bytes, optional + 16, 4);
                    if (kind == "headers") Array.Copy(BitConverter.GetBytes(UInt32.MaxValue), 0, bytes, optional + 60, 4);
                    if (kind == "raw") Array.Copy(BitConverter.GetBytes(UInt32.MaxValue), 0, bytes, table + 16, 4);
                    if (kind == "overlap") Array.Copy(bytes, table + 20, bytes, table + 40 + 20, 4);
                    Throws<SafetyException>(() => ModernWindowsAppHostIdentity.RequireLayout(bytes, CancellationToken.None));
                }
            });
            Test("modern native apphost refuses bundle custom search alternate app padding and planted locator", delegate {
                int app = NativeHostAppOffset(sample), start = app - 552;
                foreach (string kind in new[] { "bundle", "search", "relative", "application", "padding", "marker", "duplicate", "executabledata" }) {
                    byte[] bytes = (byte[])sample.Clone();
                    if (kind == "bundle") bytes[start] = 1;
                    if (kind == "search") bytes[start + 40] = 4;
                    if (kind == "relative") bytes[start + 40 + 2] = (byte)'.';
                    if (kind == "application") bytes[app] = (byte)'F';
                    if (kind == "padding") bytes[app + 1000] = 1;
                    if (kind == "marker") bytes[start + 8] ^= 1;
                    if (kind == "duplicate") Array.Copy(bytes, start + 8, bytes, bytes.Length - 40, 32);
                    if (kind == "executabledata") { int pe = (int)BitConverter.ToUInt32(bytes, 60), table = pe + 264; for (int i = 0; i < BitConverter.ToUInt16(bytes, pe + 6); i++) { int row = table + i * 40; uint raw = BitConverter.ToUInt32(bytes, row + 20), length = BitConverter.ToUInt32(bytes, row + 16); if (app >= raw && app < (long)raw + length) Array.Copy(BitConverter.GetBytes(0xe0000040U), 0, bytes, row + 36, 4); } }
                    Throws<SafetyException>(() => ModernWindowsAppHostIdentity.RequireLayout(bytes, CancellationToken.None));
                }
            });
            Test("modern native apphost actual resources reject altered product title and original file name", delegate {
                foreach (string field in new[] { "Easy Eject\0", "Easy Eject for Windows\0", "EasyEject.Modern.dll\0", "0.2.0.0\0" }) {
                    byte[] bytes = NativeHostStable(sample); byte[] pattern = Encoding.Unicode.GetBytes(field), changed = (byte[])pattern.Clone(); changed[0] = (byte)'X'; int count = PatchFixtureBytes(bytes, pattern, changed); Check(count >= 1, "Actual resource mutation found no value"); Throws<SafetyException>(() => NativeHostActualResource(bytes, "0.2.0.0"));
                }
            });
            Test("modern native apphost retains fresh signer cancellation and hold release across actual-byte review", delegate {
                using (var f = new ModernPackageFixture()) { NativeHostPublish(f, NativeHostStable(sample)); using (var review = f.Review()) { f.Certificate.Throw = true; Throws<SafetyException>(() => ModernWindowsAppHostIdentity.Require(review, CancellationToken.None)); } f.RequireReleased(); f.Certificate.Throw = false; using (var review = f.Review()) using (var cancel = new CancellationTokenSource()) { f.Certificate.During = cancel.Cancel; Throws<OperationCanceledException>(() => ModernWindowsAppHostIdentity.Require(review, cancel.Token)); } f.RequireReleased(); }
            });
        }
        static int ModernWindowsAppHostSample(string path) {
            ModernWindowsAppHostIdentityTests(); path = SafePaths.Normalize(path);
            using (var parents = SafePaths.HoldDirectories(Path.GetDirectoryName(path))) using (var held = SafePaths.ReadFile(path, 65536)) {
                var before = Native.Info(held.SafeFileHandle); if (held.Length < 64 || held.Length > ModernWindowsAppHostIdentity.MaximumBytes) throw new SafetyException("Unsupported owned native host sample size"); string hash = VerifiedImporter.Hash(held, CancellationToken.None); held.Position = 0; byte[] bytes = new byte[(int)held.Length]; int done = 0;
                while (done < bytes.Length) { int count = held.Read(bytes, done, bytes.Length - done); if (count == 0) throw new SafetyException("Incomplete owned sample"); done += count; }
                ModernWindowsAppHostActualTests(bytes); held.Position = 0; Check(VerifiedImporter.SameStamp(before, Native.Info(held.SafeFileHandle)) && VerifiedImporter.Hash(held, CancellationToken.None) == hash && Native.FinalPath(held.SafeFileHandle).Equals(path, StringComparison.OrdinalIgnoreCase), "Actual host sample changed"); parents.Validate(); Console.WriteLine("Read-only native host sample SHA256 " + hash + "; disposable patched copies only, no execution or package approval.");
            }
            Console.WriteLine(passed + " passed; " + failed + " failed. Fake positive publisher trust; no real device action."); return failed == 0 ? 0 : 1;
        }
    }
}
