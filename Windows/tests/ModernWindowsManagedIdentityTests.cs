using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static Dictionary<string, string> ModernIdentityAttributes(string version = "0.2.0.0") {
            return new Dictionary<string, string> {
                { "System.Reflection.AssemblyProductAttribute", "Easy Eject" }, { "System.Reflection.AssemblyTitleAttribute", "Easy Eject for Windows" },
                { "System.Reflection.AssemblyFileVersionAttribute", version }, { "System.Reflection.AssemblyInformationalVersionAttribute", version }
            };
        }
        static void ModernWindowsManagedIdentityTests() {
            Test("modern managed identity requires exact application title product and stable versions", delegate {
                foreach (string field in new[] { "name", "version", "AssemblyProductAttribute", "AssemblyTitleAttribute", "AssemblyFileVersionAttribute", "AssemblyInformationalVersionAttribute" }) {
                    var attributes = ModernIdentityAttributes(); new WindowsManagedIdentity("EasyEject.Modern", "0.2.0.0", attributes).RequireModern("0.2.0.0");
                    if (field.StartsWith("Assembly", StringComparison.Ordinal)) { attributes["System.Reflection." + field] = "changed"; Throws<SafetyException>(() => new WindowsManagedIdentity("EasyEject.Modern", "0.2.0.0", attributes).RequireModern("0.2.0.0")); }
                    else Throws<SafetyException>(() => new WindowsManagedIdentity(field == "name" ? "EasyEject.Windows" : "EasyEject.Modern", field == "version" ? "0.2.0.1" : "0.2.0.0", attributes).RequireModern("0.2.0.0"));
                }
            });
            Test("modern managed identity refuses unstable noncanonical and absent release versions", delegate {
                foreach (string version in new[] { null, "", "0.0.0.0", "0.2.0-dev", "0.2.0", "00.2.0.0", "0.2.0.0+source" }) Throws<SafetyException>(() => new WindowsManagedIdentity("EasyEject.Modern", version, ModernIdentityAttributes(version)).RequireModern(version));
                new WindowsManagedIdentity("EasyEject.Modern", "1.2.3.4", ModernIdentityAttributes("1.2.3.4")).RequireModern("1.2.3.4");
            });
            Test("modern and classic metadata policies never substitute each other", delegate {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, WindowsReleaseManifest.Executable);
                using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    uint entryPoint; var metadata = WindowsExecutableIdentity.ReadModernMetadata(stream, out entryPoint);
                    Check(NativeManagedMetadata.Read(metadata).Name == "EasyEject.Windows", "Classic core identity changed");
                    Throws<SafetyException>(() => NativeManagedMetadata.ReadModern(metadata, entryPoint));
                }
                Throws<SafetyException>(() => new WindowsManagedIdentity("EasyEject.Modern", "0.2.0.0", ModernIdentityAttributes()).Require("0.2.0.0"));
                Throws<SafetyException>(() => new WindowsManagedIdentity("EasyEject.Windows", "0.2.0.0", ModernIdentityAttributes()).RequireModern("0.2.0.0"));
            });
            Test("modern metadata importer refuses missing malformed snapshots and unsupported entry point tokens", delegate {
                foreach (uint token in new[] { 0U, 0x06000000U, 0x04000001U, 0x86000001U }) Throws<SafetyException>(() => NativeManagedMetadata.ReadModern(new byte[32], token));
                foreach (byte[] bytes in new[] { null, new byte[15], new byte[16777217], new byte[32], BitConverter.GetBytes(0x424a5342).Concat(new byte[28]).ToArray() }) Throws<SafetyException>(() => NativeManagedMetadata.ReadModern(bytes, 0x06000001));
            });
            Test("modern managed held-content review refuses invalid metadata cancellation and disposed ownership", delegate {
                Throws<ArgumentNullException>(() => ModernWindowsManagedApplicationIdentity.Require(null, CancellationToken.None));
                using (var fixture = new ModernPackageFixture()) {
                    var review = fixture.Review();
                    using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); Throws<OperationCanceledException>(() => ModernWindowsManagedApplicationIdentity.Require(review, cancel.Token)); }
                    Throws<SafetyException>(() => ModernWindowsManagedApplicationIdentity.Require(review, CancellationToken.None));
                    review.Dispose(); fixture.RequireReleased(); Throws<ObjectDisposedException>(() => ModernWindowsManagedApplicationIdentity.Require(review, CancellationToken.None));
                }
            });
        }
        static WindowsManagedIdentity ModernIdentityBytes(byte[] bytes, Action<WindowsManagedIdentity> check = null) {
            using (var fixture = new Fixture()) {
                string path = Path.Combine(fixture.Source, "owned-modern.dll"); File.WriteAllBytes(path, bytes);
                using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    var identity = WindowsManagedIdentity.ReadModern(stream); if (check != null) check(identity); return identity;
                }
            }
        }
        static byte[] ModernIdentityStable(byte[] sample) { byte[] bytes = (byte[])sample.Clone(); PatchFixtureAttribute(bytes, "0.2.0-dev", "0.2.0.0"); return bytes; }
        static int ModernIdentityClr(byte[] bytes) { int pe = (int)BitConverter.ToUInt32(bytes, 60); return FixtureRvaOffset(bytes, pe + 24 + 240, BitConverter.ToUInt16(bytes, pe + 6), BitConverter.ToUInt32(bytes, pe + 24 + 224)); }
        static byte[] ModernIdentityMetadata(byte[] bytes, out int start) {
            int pe = (int)BitConverter.ToUInt32(bytes, 60), clr = ModernIdentityClr(bytes);
            start = FixtureRvaOffset(bytes, pe + 24 + 240, BitConverter.ToUInt16(bytes, pe + 6), BitConverter.ToUInt32(bytes, clr + 8));
            return bytes.Skip(start).Take((int)BitConverter.ToUInt32(bytes, clr + 12)).ToArray();
        }
        static void ModernIdentityPublish(ModernPackageFixture fixture, byte[] bytes) {
            string name = "EasyEject.Modern.dll"; File.WriteAllBytes(fixture.PathFor(name), bytes); fixture.Metadata.Files.RemoveAll(file => file.Name == name);
            fixture.Metadata.Files.Add(new WindowsReleaseFile(name, bytes.Length, WindowsSignerEvidence.Hash(bytes))); fixture.Metadata.Version = "0.2.0.0"; fixture.Publish();
        }
        static void ModernWindowsManagedActualIdentityTests(byte[] sample) {
            Test("modern native metadata reads actual development app without loading or executing it", delegate {
                var before = AppDomain.CurrentDomain.GetAssemblies(); string hash = WindowsSignerEvidence.Hash(sample);
                var identity = ModernIdentityBytes(sample);
                Check(identity.Name == "EasyEject.Modern" && identity.Version == "0.2.0.0" && identity.Product == "Easy Eject" && identity.Title == "Easy Eject for Windows" && identity.FileVersion == "0.2.0.0" && identity.InformationalVersion == "0.2.0-dev", "Actual modern identity differs");
                Throws<SafetyException>(() => identity.RequireModern("0.2.0.0"));
                Check(WindowsSignerEvidence.Hash(sample) == hash && !AppDomain.CurrentDomain.GetAssemblies().Except(before).Any(assembly => assembly.GetName().Name == "EasyEject.Modern"), "Candidate loaded or source bytes changed");
            });
            Test("modern native metadata accepts owned stable attribute patch and refuses classic core policy", delegate {
                byte[] bytes = ModernIdentityStable(sample); ModernIdentityBytes(bytes, identity => identity.RequireModern("0.2.0.0"));
                int start; byte[] metadata = ModernIdentityMetadata(bytes, out start); Throws<SafetyException>(() => NativeManagedMetadata.Read(metadata));
            });
            Test("modern actual managed identity independently refuses altered application and attribute versions", delegate {
                foreach (string field in new[] { "name", "product", "title", "file", "informational" }) {
                    byte[] bytes = field == "informational" ? (byte[])sample.Clone() : ModernIdentityStable(sample);
                    // The same text also occurs inside a custom-attribute blob. The
                    // leading heap terminator selects the unique name string entry.
                    if (field == "name") Check(PatchFixtureBytes(bytes, Encoding.UTF8.GetBytes("\0EasyEject.Modern\0"), Encoding.UTF8.GetBytes("\0OtherApp!.Modern\0")) == 1, "Ambiguous modern name fixture");
                    else if (field == "product") PatchFixtureAttribute(bytes, "Easy Eject", "Other App!");
                    else if (field == "title") PatchFixtureAttribute(bytes, "Easy Eject for Windows", "Other App! for Windows");
                    else if (field == "informational") PatchFixtureAttribute(bytes, "0.2.0-dev", "0.2.0.1");
                    else PatchFixtureAttribute(bytes, "0.2.0.0", "0.2.0.1", 2);
                    Throws<SafetyException>(() => ModernIdentityBytes(bytes, identity => identity.RequireModern("0.2.0.0")));
                }
            });
            Test("modern actual metadata refuses missing and duplicate identity attributes", delegate {
                foreach (string kind in new[] { "duplicate", "missing" }) {
                    byte[] bytes = ModernIdentityStable(sample);
                    Check(PatchFixtureBytes(bytes, Encoding.UTF8.GetBytes("AssemblyProductAttribute\0"), Encoding.UTF8.GetBytes(kind == "duplicate" ? "AssemblyTitleAttribute\0\0\0" : "AssemblyUnknownAttribute\0")) == 1, "Ambiguous modern attribute fixture");
                    Throws<SafetyException>(() => ModernIdentityBytes(bytes));
                }
            });
            Test("modern actual metadata requires exact System.Runtime identity and public key token", delegate {
                foreach (string kind in new[] { "name", "token" }) {
                    byte[] bytes = ModernIdentityStable(sample); int start; byte[] metadata = ModernIdentityMetadata(bytes, out start);
                    if (kind == "name") Check(PatchFixtureBytes(metadata, Encoding.UTF8.GetBytes("System.Runtime\0"), Encoding.UTF8.GetBytes("System.Foreign\0")) == 1, "Ambiguous core name fixture");
                    else Check(PatchFixtureBytes(metadata, new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a }, new byte[8]) == 1, "Ambiguous core token fixture");
                    Array.Copy(metadata, 0, bytes, start, metadata.Length); Throws<SafetyException>(() => ModernIdentityBytes(bytes));
                }
            });
            Test("modern actual managed PE refuses absent foreign and out-of-range entry point tokens", delegate {
                foreach (uint token in new[] { 0U, 0x04000001U, 0x06ffffffU }) { byte[] bytes = ModernIdentityStable(sample); Array.Copy(BitConverter.GetBytes(token), 0, bytes, ModernIdentityClr(bytes) + 20, 4); Throws<SafetyException>(() => ModernIdentityBytes(bytes)); }
            });
            Test("modern actual managed entry point requires named Program Main rather than another method", delegate {
                byte[] bytes = ModernIdentityStable(sample); int start; byte[] metadata = ModernIdentityMetadata(bytes, out start);
                Check(PatchFixtureBytes(metadata, Encoding.UTF8.GetBytes("Main\0"), Encoding.UTF8.GetBytes("Rain\0")) == 1, "Ambiguous Main name fixture");
                Array.Copy(metadata, 0, bytes, start, metadata.Length); Throws<SafetyException>(() => ModernIdentityBytes(bytes));
                bytes = ModernIdentityStable(sample); Array.Copy(BitConverter.GetBytes(0x06000001U), 0, bytes, ModernIdentityClr(bytes) + 20, 4); Throws<SafetyException>(() => ModernIdentityBytes(bytes));
            });
            Test("modern actual managed PE refuses architecture subsystem DLL and unsupported CLR modes", delegate {
                foreach (string kind in new[] { "x86", "arm64", "console", "dll", "native", "32bit", "metadata" }) {
                    byte[] bytes = ModernIdentityStable(sample); int pe = (int)BitConverter.ToUInt32(bytes, 60), clr = ModernIdentityClr(bytes);
                    if (kind == "x86" || kind == "arm64") Array.Copy(BitConverter.GetBytes(kind == "x86" ? (ushort)0x14c : (ushort)0xaa64), 0, bytes, pe + 4, 2);
                    if (kind == "console") Array.Copy(BitConverter.GetBytes((ushort)3), 0, bytes, pe + 24 + 68, 2);
                    if (kind == "dll") Array.Copy(BitConverter.GetBytes((ushort)(BitConverter.ToUInt16(bytes, pe + 22) | 0x2000)), 0, bytes, pe + 22, 2);
                    if (kind == "native" || kind == "32bit") Array.Copy(BitConverter.GetBytes(kind == "native" ? 0x11U : 3U), 0, bytes, clr + 16, 4);
                    if (kind == "metadata") Array.Copy(BitConverter.GetBytes(UInt32.MaxValue), 0, bytes, clr + 12, 4);
                    Throws<SafetyException>(() => ModernIdentityBytes(bytes));
                }
            });
            Test("modern actual managed layer binds held authenticated version and retains bytes through disposal", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    byte[] bytes = ModernIdentityStable(sample); ModernIdentityPublish(fixture, bytes);
                    using (var review = fixture.Review()) { ModernWindowsManagedApplicationIdentity.Require(review, CancellationToken.None); Throws<IOException>(() => File.WriteAllText(fixture.PathFor("EasyEject.Modern.dll"), "changed")); }
                    Check(File.ReadAllBytes(fixture.PathFor("EasyEject.Modern.dll")).SequenceEqual(bytes), "Managed bytes changed"); fixture.RequireReleased();
                    fixture.Metadata.Version = "0.2.0.1"; fixture.Publish(); using (var review = fixture.Review()) Throws<SafetyException>(() => ModernWindowsManagedApplicationIdentity.Require(review, CancellationToken.None)); fixture.RequireReleased();
                }
            });
            Test("modern actual managed layer refreshes revoked signer and cancellation before granting identity", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    ModernIdentityPublish(fixture, ModernIdentityStable(sample));
                    using (var review = fixture.Review()) { fixture.Certificate.Throw = true; Throws<SafetyException>(() => ModernWindowsManagedApplicationIdentity.Require(review, CancellationToken.None)); }
                    fixture.RequireReleased(); fixture.Certificate.Throw = false;
                    using (var review = fixture.Review()) using (var cancel = new CancellationTokenSource()) { fixture.Certificate.During = cancel.Cancel; Throws<OperationCanceledException>(() => ModernWindowsManagedApplicationIdentity.Require(review, cancel.Token)); }
                    fixture.RequireReleased();
                }
            });
        }
        static int ModernManagedIdentitySample(string path) {
            ModernWindowsManagedIdentityTests(); path = SafePaths.Normalize(path);
            using (var directories = SafePaths.HoldDirectories(Path.GetDirectoryName(path))) using (var stream = SafePaths.ReadFile(path, 65536)) {
                directories.Validate(); var before = Native.Info(stream.SafeFileHandle);
                if (stream.Length < 64 || stream.Length > 16777216) throw new SafetyException("Unsupported actual modern metadata sample bound.");
                string hash = VerifiedImporter.Hash(stream, CancellationToken.None); stream.Position = 0; byte[] bytes = new byte[(int)stream.Length]; int done = 0;
                while (done < bytes.Length) { int read = stream.Read(bytes, done, bytes.Length - done); if (read == 0) throw new SafetyException("Incomplete modern metadata sample."); done += read; }
                ModernWindowsManagedActualIdentityTests(bytes); stream.Position = 0;
                Check(VerifiedImporter.Hash(stream, CancellationToken.None) == hash && VerifiedImporter.SameStamp(before, Native.Info(stream.SafeFileHandle)) && Native.FinalPath(stream.SafeFileHandle).Equals(path, StringComparison.OrdinalIgnoreCase), "Actual source metadata sample changed"); directories.Validate();
                Console.WriteLine("Read-only actual sample SHA256 " + hash + "; owned copies only. No candidate loading, entry point execution or full package approval.");
            }
            Console.WriteLine(passed + " passed; " + failed + " failed. Fake positive publisher trust for held fixture packages; no device action."); return failed == 0 ? 0 : 1;
        }
    }
}
