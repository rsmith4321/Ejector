using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        sealed class WindowsPackageFixture : IDisposable {
            internal readonly Fixture Files = new Fixture(); internal readonly ManifestSigningFixture Signing = new ManifestSigningFixture();
            internal readonly FakeExecutableTrust ExecutableTrust; internal readonly FakeManifestCertificateTrust CertificateTrust;
            internal string Root { get { return Files.Source; } }
            internal WindowsPackageFixture(bool releaseVersion = true, bool managedReleaseVersion = true) {
                ExecutableTrust = new FakeExecutableTrust { Evidence = Signing.Evidence() }; CertificateTrust = new FakeManifestCertificateTrust { Evidence = Signing.Evidence() };
                string app = Path.Combine(Path.GetDirectoryName(typeof(Tests).Assembly.Location), WindowsReleaseManifest.Executable);
                byte[] bytes = File.ReadAllBytes(app);
                if (releaseVersion) {
                    byte[] old = Encoding.Unicode.GetBytes("0.2.0-dev\0"), next = Encoding.Unicode.GetBytes("0.2.0.0\0\0\0"); int found = 0;
                    found = PatchFixtureBytes(bytes, old, next);
                    Check(found == 1, "Could not patch only the disposable product-version resource");
                    if (managedReleaseVersion) PatchFixtureAttribute(bytes, "0.2.0-dev", "0.2.0.0");
                }
                File.WriteAllBytes(Path.Combine(Root, WindowsReleaseManifest.Executable), bytes);
                File.Copy(Path.Combine(Path.GetDirectoryName(app), WindowsReleaseManifest.Configuration), Path.Combine(Root, WindowsReleaseManifest.Configuration));
                File.WriteAllText(Path.Combine(Root, "README.txt"), "Disposable test instructions. Not a release or installation.", new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(Root, "LICENSE.txt"), "Disposable test licensing text. Not an actual license grant.", new UTF8Encoding(false)); Publish();
            }
            internal string ManifestText() {
                return "{\"format\":1,\"application\":\"EasyEject.Windows\",\"platform\":\"windows-x64\",\"version\":\"0.2.0.0\",\"sourceRevision\":\"384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c\",\"files\":[" + String.Join(",", WindowsReleaseManifest.PayloadNames.Select(n => "{\"name\":\"" + n + "\",\"length\":" + new FileInfo(Path.Combine(Root, n)).Length + ",\"sha256\":\"" + WindowsSignerEvidence.Hash(File.ReadAllBytes(Path.Combine(Root, n))) + "\"}")) + "]}";
            }
            internal void Publish(string text = null, byte[] bytes = null) { byte[] body = bytes ?? Encoding.UTF8.GetBytes(text ?? ManifestText()); File.WriteAllBytes(Path.Combine(Root, WindowsReleaseManifest.ManifestName), body); File.WriteAllBytes(Path.Combine(Root, WindowsReleaseManifest.SignatureName), Signing.Sign(body)); }
            internal WindowsPackageReview Review(CancellationToken cancel = default(CancellationToken), IWindowsExecutableTrust executable = null, IWindowsManifestTrust manifest = null) { return new WindowsPackageReview(Root, Signing.Publisher(), executable ?? ExecutableTrust, manifest ?? new WindowsManifestTrust(CertificateTrust), cancel); }
            public void Dispose() { try { Signing.Dispose(); } finally { Files.Dispose(); } }
        }
        sealed class WrongManifestSnapshot : IWindowsManifestTrust {
            public WindowsAuthenticatedManifest Verify(byte[] body, byte[] signature, ApprovedWindowsPublisher approved, WindowsSignerEvidence executable, CancellationToken cancel) { body[body.Length - 1] ^= 1; return new WindowsAuthenticatedManifest(body, signature, executable); }
        }
        static string FixturePackageStream(WindowsPackageFixture fixture, string file, string name, string text = null) {
            Check((file == "" || WindowsReleaseManifest.PayloadNames.Contains(file)) && (name == "foreign" || name == "Zone.Identifier"), "Unsupported disposable stream target");
            string basis = file == "" ? fixture.Root : Path.Combine(fixture.Root, file); Check(SafePaths.Within(basis, fixture.Files.Root), "Stream fixture escaped its root");
            using (var handle = Native.CreateFile(basis + ":" + name, text == null ? 0x80000000U : 0x40000000U, 1, IntPtr.Zero, text == null ? 3U : 2U, 0x00200000, IntPtr.Zero)) {
                if (handle.IsInvalid) throw Native.Error("Cannot access disposable package stream");
                Check(Native.FinalPath(handle).StartsWith(basis + ":", StringComparison.OrdinalIgnoreCase), "Disposable stream resolved elsewhere");
                using (var stream = new FileStream(handle, text == null ? FileAccess.Read : FileAccess.Write)) {
                    if (text != null) { byte[] bytes = Encoding.UTF8.GetBytes(text); stream.Write(bytes, 0, bytes.Length); stream.Flush(true); return text; }
                    using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
                }
            }
        }
        static SafetyException RefusePackage(Func<WindowsPackageReview> review, string reason = "") {
            try { using (var unexpected = review()) { } } catch (SafetyException failure) { return failure; }
            throw new Exception("Expected package refusal: " + reason);
        }
        static int PatchFixtureBytes(byte[] bytes, byte[] old, byte[] replacement) {
            Check(replacement.Length >= old.Length, "Invalid disposable fixture resource patch length"); int found = 0;
            for (int i = 0; i <= bytes.Length - old.Length; i++) {
                if (bytes[i] != old[0]) continue; int j = 1; while (j < old.Length && bytes[i + j] == old[j]) j++;
                if (j == old.Length) { Array.Copy(replacement, 0, bytes, i, old.Length); found++; i += old.Length - 1; }
            }
            return found;
        }
        static void PatchFixtureAttribute(byte[] bytes, string old, string replacement, int expected = 1) {
            byte[] text = Encoding.UTF8.GetBytes(old), next = Encoding.UTF8.GetBytes(replacement);
            Check(text.Length < 128 && next.Length > 0 && next.Length <= text.Length, "Unsupported disposable metadata patch");
            byte[] blob = new byte[] { 1, 0, (byte)text.Length }.Concat(text).Concat(new byte[2]).ToArray(); int found = 0;
            for (int i = 1; i <= bytes.Length - blob.Length; i++) {
                if (bytes[i - 1] != blob.Length || bytes[i] != 1) continue; int j = 1; while (j < blob.Length && bytes[i + j] == blob[j]) j++;
                if (j != blob.Length) continue;
                Array.Clear(bytes, i, blob.Length); byte[] changed = new byte[] { 1, 0, (byte)next.Length }.Concat(next).Concat(new byte[2]).ToArray();
                Array.Copy(changed, 0, bytes, i, changed.Length); bytes[i - 1] = (byte)changed.Length; found++; i += blob.Length - 1;
            }
            Check(found == expected, "Disposable metadata patch for " + old + " expected " + expected + " matches; observed " + found);
        }
        static void WindowsPackageTests() {
            Test("Windows package review authenticates exact payload and actual x64 GUI app version without execution", delegate {
                using (var f = new WindowsPackageFixture()) {
                    var before = Directory.GetFiles(f.Root).ToDictionary(Path.GetFileName, p => WindowsSignerEvidence.Hash(File.ReadAllBytes(p)));
                    using (var review = f.Review()) {
                        Check(review.Release.Version == "0.2.0.0" && review.Release.SourceRevision == "384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c" && review.Release.Files.Count == 4 && review.Authenticated.BodyHash == before[WindowsReleaseManifest.ManifestName], "Package identity/inventory is incomplete");
                        review.Revalidate(CancellationToken.None); Check(f.ExecutableTrust.Calls == 2 && f.CertificateTrust.Calls == 2, "Revalidation did not refresh both native trust roles");
                        Check(Directory.GetFiles(f.Root).All(p => before[Path.GetFileName(p)] == WindowsSignerEvidence.Hash(File.ReadAllBytes(p))) && Directory.GetFileSystemEntries(f.Root).Length == 6, "Review wrote, extracted or installed contents");
                    }
                }
            });
            Test("Windows package review retains all payload metadata handles through disposal", delegate {
                using (var f = new WindowsPackageFixture()) {
                    using (var review = f.Review()) foreach (string path in Directory.GetFiles(f.Root)) { Throws<IOException>(() => File.WriteAllText(path, "changed")); Throws<IOException>(() => File.Delete(path)); }
                    foreach (string path in Directory.GetFiles(f.Root)) File.Move(path, path + ".released");
                }
            });
            Test("Windows package rejects an unsigned development executable despite fake approved metadata", delegate {
                using (var f = new WindowsPackageFixture()) {
                    var refusal = RefusePackage(() => f.Review(executable: new WindowsAuthenticode())); Check(refusal.Message.Contains("0x800B0100") && f.CertificateTrust.Calls == 0, "Unsigned app became approved or native refusal lost");
                    File.Move(Path.Combine(f.Root, WindowsReleaseManifest.ManifestName), Path.Combine(f.Root, "released.json"));
                }
            });
            Test("Windows package rejects development product version even if signed manifest says stable", delegate {
                using (var f = new WindowsPackageFixture(false)) { Check(RefusePackage(() => f.Review()).Message.Contains("application/version identity"), "Development metadata became a release"); }
            });
            Test("Windows package refuses stable resources concealing a development managed informational version", delegate {
                using (var f = new WindowsPackageFixture(managedReleaseVersion: false)) { Check(RefusePackage(() => f.Review()).Message.Contains("managed Windows application/version identity"), "Resource-only change became a managed release"); }
            });
            Test("Windows package refuses unconfigured publisher without opening creating or verifying", delegate {
                using (var f = new Fixture()) { string missing = Path.Combine(f.Root, "missing-package"); var trust = new FakeExecutableTrust(); Throws<SafetyException>(() => new WindowsPackageReview(missing, null, trust, new WindowsManifestTrust(), CancellationToken.None)); Check(!Directory.Exists(missing) && trust.Calls == 0, "Unconfigured channel performed work"); }
            });
            Test("Windows package refuses extra files extra folders missing files and noncanonical names before trust", delegate {
                foreach (string kind in new[] { "extra-file", "extra-folder", "missing", "case" }) using (var f = new WindowsPackageFixture()) {
                    if (kind == "extra-file") File.WriteAllText(Path.Combine(f.Root, "foreign.dll"), "keep");
                    if (kind == "extra-folder") Directory.CreateDirectory(Path.Combine(f.Root, "foreign"));
                    if (kind == "missing") File.Delete(Path.Combine(f.Root, "LICENSE.txt"));
                    if (kind == "case") File.Move(Path.Combine(f.Root, "README.txt"), Path.Combine(f.Root, "readme.txt"));
                    RefusePackage(() => f.Review()); Check(f.ExecutableTrust.Calls == 0, "Unexpected package inventory reached signer verification");
                }
            });
            Test("Windows package refuses unlisted alternate data stream without modifying it", delegate {
                foreach (string file in new[] { "README.txt", WindowsReleaseManifest.Executable, "" }) using (var f = new WindowsPackageFixture()) { FixturePackageStream(f, file, "foreign", "unlisted stream contents"); RefusePackage(() => f.Review(), "unlisted alternate stream"); Check(FixturePackageStream(f, file, "foreign") == "unlisted stream contents", "Package review cleaned unlisted stream"); }
            });
            Test("Windows package retains bounded download-zone metadata without using it as approval", delegate {
                using (var f = new WindowsPackageFixture()) { string zone = "[ZoneTransfer]\r\nZoneId=3\r\n"; foreach (string file in WindowsReleaseManifest.PayloadNames.Concat(new[] { "" })) FixturePackageStream(f, file, "Zone.Identifier", zone);
                    using (var review = f.Review()) { review.Revalidate(CancellationToken.None); Check(FixturePackageStream(f, WindowsReleaseManifest.Executable, "Zone.Identifier") == zone && f.ExecutableTrust.Calls == 2 && f.CertificateTrust.Calls == 2, "Review stripped zone metadata or used it as publisher proof"); }
                }
            });
            Test("Windows package refuses excessive download-zone stream without stripping it", delegate {
                using (var f = new WindowsPackageFixture()) { string zone = new string('x', 4097); FixturePackageStream(f, "README.txt", "Zone.Identifier", zone); RefusePackage(() => f.Review()); Check(FixturePackageStream(f, "README.txt", "Zone.Identifier") == zone, "Review cleared oversized download metadata"); }
            });
            Test("Windows package rejects redirected root and hard-linked payload without following aliases", delegate {
                using (var f = new WindowsPackageFixture()) {
                    string alias = Path.Combine(f.Files.Root, "alias"); CreateJunction(f.Files, alias, f.Root); Throws<SafetyException>(() => new WindowsPackageReview(alias, f.Signing.Publisher(), f.ExecutableTrust, new WindowsManifestTrust(f.CertificateTrust), CancellationToken.None)); Check(f.ExecutableTrust.Calls == 0, "Redirected package reached trust");
                    Check(CreateHardLink(Path.Combine(f.Files.Destination, "license-alias.keep"), Path.Combine(f.Root, "LICENSE.txt"), IntPtr.Zero), "Could not alias fixture payload"); RefusePackage(() => f.Review()); Check(f.ExecutableTrust.Calls == 0, "Hard-linked package payload reached trust");
                }
            });
            Test("Windows package rejects changed actual payload hash and length with existing authenticated inventory", delegate {
                foreach (string kind in new[] { "same-length", "length" }) using (var f = new WindowsPackageFixture()) {
                    string path = Path.Combine(f.Root, "README.txt"); byte[] bytes = File.ReadAllBytes(path); bytes[0] ^= 1; File.WriteAllBytes(path, kind == "same-length" ? bytes : bytes.Concat(new byte[] { 65 }).ToArray());
                    Check(RefusePackage(() => f.Review()).Message.Contains("length/SHA-256"), "Changed actual payload became approved");
                }
            });
            Test("Windows package parses only returned authenticated snapshot and refuses a retargeted snapshot", delegate {
                using (var f = new WindowsPackageFixture()) Check(RefusePackage(() => f.Review(manifest: new WrongManifestSnapshot())).Message.Contains("different package snapshot"), "Retargeted authenticated bytes became package metadata");
            });
            Test("Windows package rejects unknown missing duplicate or typed metadata fields despite valid fixture crypto", delegate {
                foreach (string kind in new[] { "unknown", "missing", "duplicate", "escaped-duplicate", "type", "annotation", "null", "trailing" }) using (var f = new WindowsPackageFixture()) {
                    string text = f.ManifestText();
                    if (kind == "unknown") text = text.Replace("{\"format\":1,", "{\"surprise\":true,\"format\":1,");
                    if (kind == "missing") text = text.Replace("\"format\":1,", "");
                    if (kind == "duplicate") text = text.Replace("\"format\":1,", "\"format\":1,\"format\":1,");
                    if (kind == "escaped-duplicate") text = text.Replace("\"format\":1,", "\"format\":1,\"\\u0066ormat\":1,");
                    if (kind == "type") text = text.Replace("\"format\":1", "\"format\":\"1\"");
                    if (kind == "annotation") text = text.Replace("{\"format\":1,", "{\"__type\":\"surprise\",\"format\":1,");
                    if (kind == "null") text = text.Replace("\"application\":\"EasyEject.Windows\"", "\"application\":null");
                    if (kind == "trailing") text += "{}";
                    f.Publish(text); RefusePackage(() => f.Review(), kind);
                }
            });
            Test("Windows package refuses wrong application platform format source and nonstable version", delegate {
                foreach (string value in new[] { "macos-x64", "windows-arm64", "EasyEject.Mac", "format2", "bad-source", "0.2.0-dev", "0.2.0", "00.2.0.0", "0.0.0.0", "65536.1.0.0", "0.2.0.1" }) using (var f = new WindowsPackageFixture()) {
                    string text = f.ManifestText();
                    if (value.StartsWith("macos") || value.StartsWith("windows")) text = text.Replace("windows-x64", value);
                    else if (value.StartsWith("Easy")) text = text.Replace("EasyEject.Windows\"", value + "\"");
                    else if (value == "format2") text = text.Replace("\"format\":1", "\"format\":2");
                    else if (value == "bad-source") text = text.Replace("384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c", new string('A', 40));
                    else text = text.Replace("0.2.0.0", value);
                    f.Publish(text); RefusePackage(() => f.Review());
                }
            });
            Test("Windows package refuses duplicate missing unknown path alias and invalid hash file records", delegate {
                foreach (string kind in new[] { "duplicate", "unknown", "escape", "stream", "hash", "length-zero", "length-fraction", "length-exponent", "missing-field" }) using (var f = new WindowsPackageFixture()) {
                    string text = f.ManifestText();
                    if (kind == "duplicate") text = text.Replace("LICENSE.txt", "README.txt");
                    if (kind == "unknown") text = text.Replace("LICENSE.txt", "foreign.dll");
                    if (kind == "escape") text = text.Replace("LICENSE.txt", "../LICENSE.txt");
                    if (kind == "stream") text = text.Replace("LICENSE.txt", "LICENSE.txt:foreign");
                    if (kind == "hash") text = text.Replace(WindowsSignerEvidence.Hash(File.ReadAllBytes(Path.Combine(f.Root, "README.txt"))), new string('A', 64));
                    string length = new FileInfo(Path.Combine(f.Root, "README.txt")).Length.ToString();
                    if (kind == "length-zero") text = text.Replace("\"length\":" + length + ",", "\"length\":0,");
                    if (kind == "length-fraction") text = text.Replace("\"length\":" + length + ",", "\"length\":" + length + ".0,");
                    if (kind == "length-exponent") text = text.Replace("\"length\":" + length + ",", "\"length\":" + length + "e0,");
                    if (kind == "missing-field") text = text.Replace("\"length\":" + length + ",", "");
                    f.Publish(text); RefusePackage(() => f.Review());
                }
            });
            Test("Windows package refuses UTF16 UTF8 BOM malformed and excessive-depth release bodies", delegate {
                using (var f = new WindowsPackageFixture()) foreach (byte[] bytes in new[] { Encoding.Unicode.GetBytes(f.ManifestText()), new byte[] { 0xff, 0xfe, 0xff }, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(f.ManifestText())).ToArray(), Encoding.UTF8.GetBytes("{\"format\":[[[[[[[[1]]]]]]]]}"), Encoding.UTF8.GetBytes("{invalid}") }) { f.Publish(bytes: bytes); RefusePackage(() => f.Review(), BitConverter.ToString(bytes.Take(8).ToArray())); }
            });
            Test("Windows package refuses actual x86 ARM64 DLL console and malformed PE headers despite matching hashes", delegate {
                foreach (string kind in new[] { "x86", "arm64", "dll", "console", "magic", "offset", "sections", "optional", "no-clr", "clr-size", "clr-native", "clr-32bit", "clr-bounds", "truncated" }) using (var f = new WindowsPackageFixture()) {
                    string path = Path.Combine(f.Root, WindowsReleaseManifest.Executable); byte[] bytes = File.ReadAllBytes(path); int offset = BitConverter.ToInt32(bytes, 60);
                    if (kind == "x86") Array.Copy(BitConverter.GetBytes((ushort)0x14c), 0, bytes, offset + 4, 2);
                    if (kind == "arm64") Array.Copy(BitConverter.GetBytes((ushort)0xaa64), 0, bytes, offset + 4, 2);
                    if (kind == "dll") Array.Copy(BitConverter.GetBytes((ushort)(BitConverter.ToUInt16(bytes, offset + 22) | 0x2000)), 0, bytes, offset + 22, 2);
                    if (kind == "console") Array.Copy(BitConverter.GetBytes((ushort)3), 0, bytes, offset + 24 + 68, 2);
                    if (kind == "magic") bytes[0] = 0;
                    if (kind == "offset") Array.Copy(BitConverter.GetBytes(UInt32.MaxValue), 0, bytes, 60, 4);
                    if (kind == "sections") Array.Copy(BitConverter.GetBytes((ushort)97), 0, bytes, offset + 6, 2);
                    if (kind == "optional") Array.Copy(BitConverter.GetBytes((ushort)0x10b), 0, bytes, offset + 24, 2);
                    if (kind == "no-clr") Array.Clear(bytes, offset + 24 + 224, 8);
                    if (kind == "clr-size") Array.Copy(BitConverter.GetBytes(UInt32.MaxValue), 0, bytes, offset + 24 + 228, 4);
                    if (kind == "clr-bounds") Array.Copy(BitConverter.GetBytes(UInt32.MaxValue), 0, bytes, offset + 24 + 224, 4);
                    if (kind == "clr-native" || kind == "clr-32bit") {
                        uint rva = BitConverter.ToUInt32(bytes, offset + 24 + 224); int table = offset + 24 + BitConverter.ToUInt16(bytes, offset + 20), sectionCount = BitConverter.ToUInt16(bytes, offset + 6); int target = -1;
                        for (int i = 0; i < sectionCount; i++) { uint address = BitConverter.ToUInt32(bytes, table + i * 40 + 12), size = BitConverter.ToUInt32(bytes, table + i * 40 + 16), raw = BitConverter.ToUInt32(bytes, table + i * 40 + 20); if (rva >= address && rva + 72 <= address + size) target = (int)(raw + rva - address); }
                        Check(target >= 0, "Could not locate fixture CLR header"); Array.Copy(BitConverter.GetBytes(kind == "clr-native" ? 17U : 3U), 0, bytes, target + 16, 4);
                    }
                    if (kind == "truncated") bytes = bytes.Take(63).ToArray();
                    File.WriteAllBytes(path, bytes); f.Publish(); Check(RefusePackage(() => f.Review()).Message.Contains("x64 Windows GUI managed"), "Actual PE identity was inferred from manifest/name: " + kind);
                }
            });
            Test("Windows package refuses actual application metadata mismatch without assembly loading", delegate {
                using (var f = new WindowsPackageFixture()) { string path = Path.Combine(f.Root, WindowsReleaseManifest.Executable); byte[] bytes = File.ReadAllBytes(path), old = Encoding.Unicode.GetBytes("Easy Eject\0"), next = Encoding.Unicode.GetBytes("Other App!\0"); int count = 0;
                    count = PatchFixtureBytes(bytes, old, next);
                    Check(count == 1, "Could not patch isolated product identity"); File.WriteAllBytes(path, bytes); f.Publish(); Check(RefusePackage(() => f.Review()).Message.Contains("application/version identity"), "Other product metadata became Easy Eject");
                }
            });
            Test("Windows package refuses changed runtime extra configuration DTD and nontext documentation", delegate {
                foreach (string kind in new[] { "runtime", "extra", "dtd", "readme", "license" }) using (var f = new WindowsPackageFixture()) {
                    string path = Path.Combine(f.Root, WindowsReleaseManifest.Configuration), config = File.ReadAllText(path);
                    if (kind == "runtime") File.WriteAllText(path, config.Replace("v4.8", "v4.7"));
                    if (kind == "extra") File.WriteAllText(path, config.Replace("</configuration>", "<runtime/></configuration>"));
                    if (kind == "dtd") File.WriteAllText(path, "<!DOCTYPE configuration [<!ENTITY x SYSTEM 'file:///C:/foreign'>]><configuration>&x;</configuration>");
                    if (kind == "readme") File.WriteAllBytes(Path.Combine(f.Root, "README.txt"), new byte[] { 0xff, 0xfe, 0x00 });
                    if (kind == "license") File.WriteAllText(Path.Combine(f.Root, "LICENSE.txt"), "\0hidden");
                    f.Publish(); RefusePackage(() => f.Review());
                }
            });
            Test("Windows package late extra payload and alias refuse at final review and later revalidation", delegate {
                using (var f = new WindowsPackageFixture()) {
                    f.CertificateTrust.During = () => File.WriteAllText(Path.Combine(f.Root, "late.keep"), "foreign"); RefusePackage(() => f.Review()); Check(File.ReadAllText(Path.Combine(f.Root, "late.keep")) == "foreign", "Late extra file was cleaned");
                }
                using (var f = new WindowsPackageFixture()) using (var review = f.Review()) {
                    Check(CreateHardLink(Path.Combine(f.Files.Destination, "late-alias.keep"), Path.Combine(f.Root, "README.txt"), IntPtr.Zero), "Could not create late fixture alias"); Throws<SafetyException>(() => review.Revalidate(CancellationToken.None)); Check(File.Exists(Path.Combine(f.Files.Destination, "late-alias.keep")), "Revalidation removed alias");
                }
            });
            Test("Windows package revalidation refuses later native executable and manifest trust failures", delegate {
                using (var f = new WindowsPackageFixture()) using (var review = f.Review()) { f.ExecutableTrust.Evidence = FixtureSigner(); Throws<SafetyException>(() => review.Revalidate(CancellationToken.None)); }
                using (var f = new WindowsPackageFixture()) using (var review = f.Review()) { f.CertificateTrust.Throw = true; Throws<SafetyException>(() => review.Revalidate(CancellationToken.None)); }
            });
            Test("Windows package cancellation during authentication releases files without deleting content", delegate {
                using (var f = new WindowsPackageFixture()) using (var cancel = new CancellationTokenSource()) {
                    f.CertificateTrust.During = cancel.Cancel; Throws<OperationCanceledException>(() => f.Review(cancel.Token)); Check(Directory.GetFiles(f.Root).Length == 6, "Cancellation changed package contents"); foreach (string path in Directory.GetFiles(f.Root)) File.Move(path, path + ".released");
                }
            });
            Test("Windows package closed review releases root and refuses further validation", delegate {
                using (var f = new WindowsPackageFixture()) { var review = f.Review(); review.Dispose(); review.Dispose(); Throws<ObjectDisposedException>(() => review.Revalidate(CancellationToken.None)); Directory.Move(f.Root, f.Root + "-released"); }
            });
        }
    }
}
