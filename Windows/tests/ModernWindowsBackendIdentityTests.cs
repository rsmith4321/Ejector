using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static void ModernWindowsBackendIdentityTests() {
            Test("modern backend identity binds exact library role product title and all release versions", delegate {
                foreach (string field in new[] { "name", "version", "AssemblyProductAttribute", "AssemblyTitleAttribute", "AssemblyFileVersionAttribute", "AssemblyInformationalVersionAttribute" }) {
                    var attributes = ModernIdentityAttributes(); new WindowsManagedIdentity("EasyEject.Runtime", "0.2.0.0", attributes).RequireModernBackend("0.2.0.0");
                    if (field.StartsWith("Assembly", StringComparison.Ordinal)) { attributes["System.Reflection." + field] = "changed"; Throws<SafetyException>(() => new WindowsManagedIdentity("EasyEject.Runtime", "0.2.0.0", attributes).RequireModernBackend("0.2.0.0")); }
                    else Throws<SafetyException>(() => new WindowsManagedIdentity(field == "name" ? "EasyEject.Modern" : "EasyEject.Runtime", field == "version" ? "0.2.0.1" : "0.2.0.0", attributes).RequireModernBackend("0.2.0.0"));
                }
            });
            Test("modern backend stable version and application policies never substitute each other", delegate {
                foreach (string version in new[] { null, "", "0.0.0.0", "0.2.0-dev", "0.2.0", "00.2.0.0", "0.2.0.0+source" }) Throws<SafetyException>(() => new WindowsManagedIdentity("EasyEject.Runtime", version, ModernIdentityAttributes(version)).RequireModernBackend(version));
                var valid = new WindowsManagedIdentity("EasyEject.Runtime", "0.2.0.0", ModernIdentityAttributes());
                Throws<SafetyException>(() => valid.RequireModern("0.2.0.0")); Throws<SafetyException>(() => valid.Require("0.2.0.0"));
                new WindowsManagedIdentity("EasyEject.Runtime", "1.2.3.4", ModernIdentityAttributes("1.2.3.4")).RequireModernBackend("1.2.3.4");
            });
            Test("modern backend DLL reader refuses an actual classic executable", delegate {
                using (var stream = File.Open(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, WindowsReleaseManifest.Executable), FileMode.Open, FileAccess.Read, FileShare.Read))
                    Throws<SafetyException>(() => WindowsManagedIdentity.ReadModernBackend(stream));
            });
            Test("modern backend metadata reader refuses absent excessive and malformed bounded snapshots", delegate {
                foreach (byte[] bytes in new[] { null, new byte[15], new byte[16777217], new byte[32], BitConverter.GetBytes(0x424a5342).Concat(new byte[28]).ToArray() }) Throws<SafetyException>(() => NativeManagedMetadata.ReadModernLibrary(bytes));
            });
            Test("modern backend held review preserves cancellation disposed ownership and malformed bytes", delegate {
                Throws<ArgumentNullException>(() => ModernWindowsBackendIdentity.Require(null, CancellationToken.None));
                using (var fixture = new ModernPackageFixture()) {
                    var review = fixture.Review();
                    using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); Throws<OperationCanceledException>(() => ModernWindowsBackendIdentity.Require(review, cancel.Token)); }
                    Throws<SafetyException>(() => ModernWindowsBackendIdentity.Require(review, CancellationToken.None));
                    review.Dispose(); fixture.RequireReleased(); Throws<ObjectDisposedException>(() => ModernWindowsBackendIdentity.Require(review, CancellationToken.None));
                }
            });
        }
        static WindowsManagedIdentity BackendIdentityBytes(byte[] bytes) {
            using (var fixture = new Fixture()) {
                string path = Path.Combine(fixture.Source, "owned-backend.dll"); File.WriteAllBytes(path, bytes);
                using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) return WindowsManagedIdentity.ReadModernBackend(stream);
            }
        }
        static int ModernBackendIdentitySample(string path) {
            byte[] sample = File.ReadAllBytes(path), stable = ModernIdentityStable(sample); string originalHash = WindowsSignerEvidence.Hash(sample);
            Test("modern backend actual development metadata is read without loading or executing sample code", delegate {
                var before = AppDomain.CurrentDomain.GetAssemblies(); var identity = BackendIdentityBytes(sample);
                Check(identity.Name == "EasyEject.Runtime" && identity.Version == "0.2.0.0" && identity.FileVersion == "0.2.0.0" && identity.InformationalVersion == "0.2.0-dev", "Actual backend identity differs");
                Throws<SafetyException>(() => identity.RequireModernBackend("0.2.0.0"));
                var added = AppDomain.CurrentDomain.GetAssemblies().Except(before).ToArray();
                Console.WriteLine("Metadata sample newly loaded assemblies: " + String.Join(", ", added.Select(assembly => assembly.GetName().Name).ToArray()));
                Check(!added.Any(assembly => assembly.GetName().Name == "EasyEject.Runtime") && WindowsSignerEvidence.Hash(sample) == originalHash, "Sample backend was loaded or altered");
            });
            Test("modern backend stable owned copy admits library role and rejects main application substitution", delegate {
                BackendIdentityBytes(stable).RequireModernBackend("0.2.0.0");
                Throws<SafetyException>(() => ModernIdentityBytes(stable));
                Throws<SafetyException>(() => BackendIdentityBytes(stable).RequireModern("0.2.0.0"));
            });
            Test("modern backend PE requires x64 DLL console layout managed library flags and no method entry", delegate {
                for (int mutation = 0; mutation < 6; mutation++) {
                    byte[] bytes = (byte[])stable.Clone(); int pe = (int)BitConverter.ToUInt32(bytes, 60), clr = ModernIdentityClr(bytes);
                    if (mutation == 0) Buffer.BlockCopy(BitConverter.GetBytes((ushort)0x14c), 0, bytes, pe + 4, 2);
                    if (mutation == 1) Buffer.BlockCopy(BitConverter.GetBytes((ushort)(BitConverter.ToUInt16(bytes, pe + 22) & ~0x2000)), 0, bytes, pe + 22, 2);
                    if (mutation == 2) Buffer.BlockCopy(BitConverter.GetBytes((ushort)2), 0, bytes, pe + 24 + 68, 2);
                    if (mutation == 3) Buffer.BlockCopy(BitConverter.GetBytes(0x06000001U), 0, bytes, clr + 20, 4);
                    if (mutation == 4) Buffer.BlockCopy(BitConverter.GetBytes(0x11U), 0, bytes, clr + 16, 4);
                    if (mutation == 5) Buffer.BlockCopy(BitConverter.GetBytes(16777217U), 0, bytes, clr + 12, 4);
                    Throws<SafetyException>(() => BackendIdentityBytes(bytes));
                }
            });
            Test("modern backend actual altered product and informational version refuse release admission", delegate {
                byte[] bytes = (byte[])stable.Clone(); PatchFixtureAttribute(bytes, "Easy Eject", "Fake Eject");
                Throws<SafetyException>(() => BackendIdentityBytes(bytes).RequireModernBackend("0.2.0.0"));
                Throws<SafetyException>(() => BackendIdentityBytes(sample).RequireModernBackend("0.2.0.0"));
            });
            Test("modern backend authenticated owned library remains held through repeat identity checks", delegate {
                using (var fixture = new ModernPackageFixture()) {
                    fixture.Metadata.Version = "0.2.0.0"; BackendIdentityPublish(fixture, stable);
                    using (var review = fixture.Review()) {
                        ModernWindowsBackendIdentity.Require(review, CancellationToken.None);
                        Throws<IOException>(() => File.WriteAllText(fixture.PathFor("EasyEject.Runtime.dll"), "changed"));
                        ModernWindowsBackendIdentity.Require(review, CancellationToken.None);
                    }
                    fixture.RequireReleased();
                }
                Check(WindowsSignerEvidence.Hash(File.ReadAllBytes(path)) == originalHash, "Read-only original sample changed");
            });
            Console.WriteLine(passed + " passed, " + failed + " failed"); return failed == 0 ? 0 : 1;
        }
    }
}
