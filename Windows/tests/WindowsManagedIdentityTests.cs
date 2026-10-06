using System;
using System.IO;
using System.Linq;
using System.Text;

namespace EasyEject {
    public static partial class Tests {
        static void WindowsManagedIdentityTests() {
            Test("Native managed assembly metadata uses the x64 SDK layout", delegate {
                Check(System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeManagedMetadata.AssemblyMetadata)) == 56 && System.Runtime.InteropServices.Marshal.OffsetOf(typeof(NativeManagedMetadata.AssemblyMetadata), "Locale").ToInt32() == 8 && System.Runtime.InteropServices.Marshal.OffsetOf(typeof(NativeManagedMetadata.AssemblyMetadata), "Processors").ToInt32() == 24 && System.Runtime.InteropServices.Marshal.OffsetOf(typeof(NativeManagedMetadata.AssemblyMetadata), "Systems").ToInt32() == 40, "Wrong x64 native assembly metadata layout");
            });
            Test("Native metadata reads actual development application without loading candidate assembly", delegate {
                using (var f = new Fixture()) {
                    string path = Path.Combine(f.Source, "candidate.exe"); File.Copy(Path.Combine(Path.GetDirectoryName(typeof(Tests).Assembly.Location), WindowsReleaseManifest.Executable), path);
                    var before = AppDomain.CurrentDomain.GetAssemblies(); string hash = WindowsSignerEvidence.Hash(File.ReadAllBytes(path));
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                        var identity = WindowsManagedIdentity.Read(stream);
                        Check(identity.Name == "EasyEject.Windows" && identity.Version == "0.1.0.0" && identity.Title == "Easy Eject for Windows" && identity.Product == "Easy Eject" && identity.FileVersion == "0.1.0.0" && identity.InformationalVersion == "0.1.0-dev", "Native metadata identity differs from actual app");
                        Throws<SafetyException>(() => identity.Require("0.1.0.0"));
                    }
                    Check(!AppDomain.CurrentDomain.GetAssemblies().Except(before).Any(a => a.GetName().Name == "EasyEject.Windows") && hash == WindowsSignerEvidence.Hash(File.ReadAllBytes(path)), "Metadata review loaded or altered candidate");
                }
            });
            Test("Managed identity attribute parser rejects malformed UTF8 lengths prologs and named arguments", delegate {
                Check(NativeManagedMetadata.StringAttribute(new byte[] { 1, 0, 3, 97, 98, 99, 0, 0 }) == "abc", "Canonical string attribute failed");
                foreach (byte[] bytes in new[] { new byte[0], new byte[] { 0, 0, 1, 97, 0, 0 }, new byte[] { 1, 0, 0, 0, 0 }, new byte[] { 1, 0, 255, 0, 0 }, new byte[] { 1, 0, 128, 1, 97, 0, 0 }, new byte[] { 1, 0, 1, 97, 1, 0 }, new byte[] { 1, 0, 1, 255, 0, 0 }, new byte[] { 1, 0, 1, 10, 0, 0 }, new byte[] { 1, 0, 2, 97, 0, 0 } }) Throws<SafetyException>(() => NativeManagedMetadata.StringAttribute(bytes));
                byte[] longText = Encoding.UTF8.GetBytes(new string('x', 128)); Check(NativeManagedMetadata.StringAttribute(new byte[] { 1, 0, 128, 128 }.Concat(longText).Concat(new byte[2]).ToArray()).Length == 128, "Canonical two-byte length failed");
            });
            Test("Native metadata importer refuses malformed and excessive snapshots", delegate {
                foreach (byte[] bytes in new[] { null, new byte[15], new byte[16777217], new byte[32], BitConverter.GetBytes(0x424a5342).Concat(new byte[28]).ToArray() }) Throws<SafetyException>(() => NativeManagedMetadata.Read(bytes));
            });
            Test("Managed identity checks every declared application and version field", delegate {
                foreach (string field in new[] { "name", "version", "AssemblyProductAttribute", "AssemblyTitleAttribute", "AssemblyFileVersionAttribute", "AssemblyInformationalVersionAttribute" }) {
                    var attributes = new System.Collections.Generic.Dictionary<string, string> { { "System.Reflection.AssemblyProductAttribute", "Easy Eject" }, { "System.Reflection.AssemblyTitleAttribute", "Easy Eject for Windows" }, { "System.Reflection.AssemblyFileVersionAttribute", "0.1.0.0" }, { "System.Reflection.AssemblyInformationalVersionAttribute", "0.1.0.0" } };
                    new WindowsManagedIdentity("EasyEject.Windows", "0.1.0.0", attributes).Require("0.1.0.0");
                    if (field.StartsWith("Assembly", StringComparison.Ordinal)) { attributes["System.Reflection." + field] = "changed"; Throws<SafetyException>(() => new WindowsManagedIdentity("EasyEject.Windows", "0.1.0.0", attributes).Require("0.1.0.0")); attributes.Remove("System.Reflection." + field); Throws<SafetyException>(() => new WindowsManagedIdentity("EasyEject.Windows", "0.1.0.0", attributes)); }
                    else Throws<SafetyException>(() => new WindowsManagedIdentity(field == "name" ? "OtherApp" : "EasyEject.Windows", field == "version" ? "0.1.0.1" : "0.1.0.0", attributes).Require("0.1.0.0"));
                }
            });
            Test("Package review rejects changed managed identity despite unchanged matching native resources", delegate {
                foreach (string field in new[] { "product", "title", "file", "informational", "name" }) using (var f = new WindowsPackageFixture()) {
                    string path = Path.Combine(f.Root, WindowsReleaseManifest.Executable); byte[] bytes = File.ReadAllBytes(path);
                    if (field == "name") Check(PatchFixtureBytes(bytes, Encoding.UTF8.GetBytes("EasyEject.Windows\0"), Encoding.UTF8.GetBytes("OtherApp!.Windows\0")) == 1, "Ambiguous fixture assembly name");
                    else if (field == "product") PatchFixtureAttribute(bytes, "Easy Eject", "Other App!");
                    else if (field == "title") PatchFixtureAttribute(bytes, "Easy Eject for Windows", "Other App! for Windows");
                    else {
                        // File and informational attributes may share a single identical heap blob.
                        // A resource-stable development fixture retains a distinct informational blob.
                        if (field == "informational") { bytes = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(typeof(Tests).Assembly.Location), WindowsReleaseManifest.Executable)); Check(PatchFixtureBytes(bytes, Encoding.Unicode.GetBytes("0.1.0-dev\0"), Encoding.Unicode.GetBytes("0.1.0.0\0\0\0")) == 1, "Fixture resource patch missing"); PatchFixtureAttribute(bytes, "0.1.0-dev", "0.1.0.1"); }
                        else PatchFixtureAttribute(bytes, "0.1.0.0", "0.1.0.1", 2);
                    }
                    File.WriteAllBytes(path, bytes); f.Publish(); Check(RefusePackage(() => f.Review()).Message.Contains("managed Windows application/version identity"), "Native resource identity concealed changed managed " + field);
                }
            });
            Test("Native metadata refuses duplicate missing or unexpected core identity attributes", delegate {
                foreach (string kind in new[] { "duplicate", "missing", "core-name", "core-token" }) using (var f = new WindowsPackageFixture()) {
                    string path = Path.Combine(f.Root, WindowsReleaseManifest.Executable); byte[] bytes = File.ReadAllBytes(path);
                    if (kind == "duplicate") Check(PatchFixtureBytes(bytes, Encoding.UTF8.GetBytes("AssemblyProductAttribute\0"), Encoding.UTF8.GetBytes("AssemblyTitleAttribute\0\0\0")) == 1, "Fixture duplicate patch missing");
                    if (kind == "missing") Check(PatchFixtureBytes(bytes, Encoding.UTF8.GetBytes("AssemblyProductAttribute\0"), Encoding.UTF8.GetBytes("AssemblyUnknownAttribute\0")) == 1, "Fixture missing patch ambiguous");
                    if (kind == "core-name") Check(PatchFixtureBytes(bytes, Encoding.UTF8.GetBytes("mscorlib\0"), Encoding.UTF8.GetBytes("otherlib\0")) == 1, "Fixture core name missing");
                    if (kind == "core-token") {
                        int pe = (int)BitConverter.ToUInt32(bytes, 60), sections = pe + 24 + 240, count = BitConverter.ToUInt16(bytes, pe + 6);
                        int clr = FixtureRvaOffset(bytes, sections, count, BitConverter.ToUInt32(bytes, pe + 24 + 224)), start = FixtureRvaOffset(bytes, sections, count, BitConverter.ToUInt32(bytes, clr + 8));
                        byte[] snapshot = bytes.Skip(start).Take((int)BitConverter.ToUInt32(bytes, clr + 12)).ToArray();
                        Check(PatchFixtureBytes(snapshot, new byte[] { 0xb7, 0x7a, 0x5c, 0x56, 0x19, 0x34, 0xe0, 0x89 }, new byte[8]) == 1, "Fixture metadata core token missing"); Array.Copy(snapshot, 0, bytes, start, snapshot.Length);
                    }
                    File.WriteAllBytes(path, bytes); f.Publish(); string refusal = RefusePackage(() => f.Review()).Message;
                    Check(refusal.Contains(kind == "duplicate" ? "Duplicate managed" : kind == "missing" ? "missing identity" : "unexpected core"), "Wrong native attribute refusal: " + refusal);
                }
            });
            Test("PE metadata range and root validation refuses malformed candidate snapshots", delegate {
                foreach (string kind in new[] { "missing", "too-small", "too-large", "outside", "signature", "streams" }) using (var f = new WindowsPackageFixture()) {
                    string path = Path.Combine(f.Root, WindowsReleaseManifest.Executable); byte[] bytes = File.ReadAllBytes(path); int pe = (int)BitConverter.ToUInt32(bytes, 60), optional = pe + 24, sections = optional + 240;
                    uint clrRva = BitConverter.ToUInt32(bytes, optional + 224); int clr = FixtureRvaOffset(bytes, sections, BitConverter.ToUInt16(bytes, pe + 6), clrRva);
                    int metadata = FixtureRvaOffset(bytes, sections, BitConverter.ToUInt16(bytes, pe + 6), BitConverter.ToUInt32(bytes, clr + 8));
                    if (kind == "missing" || kind == "outside") Array.Copy(BitConverter.GetBytes(kind == "missing" ? 0U : UInt32.MaxValue), 0, bytes, clr + 8, 4);
                    if (kind == "too-small" || kind == "too-large") Array.Copy(BitConverter.GetBytes(kind == "too-small" ? 15U : 16777217U), 0, bytes, clr + 12, 4);
                    if (kind == "signature") bytes[metadata] = 0;
                    if (kind == "streams") Array.Clear(bytes, metadata + 8, 8);
                    File.WriteAllBytes(path, bytes); f.Publish(); RefusePackage(() => f.Review(), kind);
                }
            });
        }
        static int FixtureRvaOffset(byte[] bytes, int sections, int count, uint rva) {
            for (int i = 0; i < count; i++) { int row = sections + i * 40; uint address = BitConverter.ToUInt32(bytes, row + 12), size = BitConverter.ToUInt32(bytes, row + 16); if ((ulong)rva >= address && (ulong)rva < (ulong)address + size) return checked((int)(BitConverter.ToUInt32(bytes, row + 20) + rva - address)); }
            throw new Exception("Fixture RVA missing");
        }
    }
}
