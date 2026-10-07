using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        sealed class ModernManifestFixture {
            internal readonly List<WindowsReleaseFile> Files = ModernWindowsReleaseManifest.RequiredFiles.Select(name => new WindowsReleaseFile(name, 1, new string('a', 64))).ToList();
            internal string DotNet = "10.0.12", Sdk = "2.5.1", Minimum = "10.0.19041.0", Version = "1.2.3.4", Source = new string('b', 40);
            internal string IndexText() { return "{\"format\":1,\"files\":[" + String.Join(",", Files.OrderBy(file => file.Name, StringComparer.Ordinal).Select(file => "{\"path\":\"" + Json(file.Name) + "\",\"length\":" + file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"sha256\":\"" + file.Hash + "\"}")) + "]}"; }
            internal byte[] Index() { return Encoding.UTF8.GetBytes(IndexText()); }
            internal string Body(byte[] index) { return "{\"format\":2,\"application\":\"EasyEject.Modern\",\"platform\":\"windows-x64\",\"version\":\"" + Version + "\",\"sourceRevision\":\"" + Source + "\",\"runtime\":{\"dotnet\":\"" + DotNet + "\",\"windowsAppSdk\":\"" + Sdk + "\",\"minimumWindows\":\"" + Minimum + "\"},\"fileIndex\":{\"name\":\"windows-modern-files.json\",\"length\":" + index.Length + ",\"sha256\":\"" + WindowsSignerEvidence.Hash(index) + "\",\"count\":" + Files.Count + ",\"totalLength\":" + Files.Sum(file => file.Length) + "}}"; }
            internal WindowsAuthenticatedManifest Snapshot(string body) { return new WindowsAuthenticatedManifest(Encoding.UTF8.GetBytes(body), new byte[] { 1 }, new WindowsSignerEvidence(new string('c', 64), new string('d', 64), new string('e', 64), FixtureSha256Oid)); }
            internal ModernWindowsReleaseManifest Parse(byte[] index = null, string body = null) { index = index ?? Index(); return ModernWindowsReleaseManifest.Parse(Snapshot(body ?? Body(index)), index); }
            static string Json(string text) { return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t"); }
        }
        static void ModernWindowsReleaseManifestTests() {
            Test("modern signed small envelope binds a 600-file inventory above the classic manifest bound", delegate {
                var fixture = new ModernManifestFixture(); for (int i = fixture.Files.Count; i < 600; i++) fixture.Files.Add(new WindowsReleaseFile("en-US/fixture" + i.ToString("D4") + ".mui", 2, new string('a', 64)));
                byte[] index = fixture.Index(), body = Encoding.UTF8.GetBytes(fixture.Body(index));
                Check(index.Length > WindowsManifestTrust.MaximumManifest && body.Length < WindowsManifestTrust.MaximumManifest, "Fixture does not exercise the large-index boundary");
                using (var signing = new ManifestSigningFixture()) {
                    var authenticated = new WindowsManifestTrust(new FakeManifestCertificateTrust { Evidence = signing.Evidence() }).VerifyEnvelope(body, signing.Sign(body), signing.Publisher(), CancellationToken.None);
                    var release = ModernWindowsReleaseManifest.Parse(authenticated, index);
                    Check(release.Files.Count == 600 && release.IndexHash == WindowsSignerEvidence.Hash(index) && release.TotalLength == fixture.Files.Sum(file => file.Length) && release.Authenticated.Signer.CertificateHash == signing.Evidence().CertificateHash, "Exact authenticated inventory was lost");
                }
            });
            Test("modern manifest requires an authenticated snapshot and bounded index", delegate {
                var f = new ModernManifestFixture(); byte[] index = f.Index();
                Throws<SafetyException>(() => ModernWindowsReleaseManifest.Parse(null, index));
                Throws<SafetyException>(() => f.Parse(new byte[0])); Throws<SafetyException>(() => f.Parse(new byte[ModernWindowsReleaseManifest.MaximumIndex + 1]));
                Check(WindowsManifestTrust.MaximumManifest == 65536, "Classic authentication limit changed");
            });
            Test("modern and classic release schemas refuse each other", delegate {
                var f = new ModernManifestFixture(); var modern = f.Snapshot(f.Body(f.Index()));
                Throws<SafetyException>(() => WindowsReleaseManifest.Parse(modern));
                Throws<SafetyException>(() => f.Parse(body: f.Body(f.Index()).Replace("\"format\":2", "\"format\":1")));
                Throws<SafetyException>(() => f.Parse(body: f.Body(f.Index()).Replace("EasyEject.Modern", "EasyEject.Windows")));
                Throws<SafetyException>(() => f.Parse(body: f.Body(f.Index()).Replace("windows-x64", "windows-arm64")));
            });
            Test("modern inventory binds exact bytes length digest count and aggregate", delegate {
                var f = new ModernManifestFixture(); byte[] index = f.Index(); string body = f.Body(index);
                byte[] altered = (byte[])index.Clone(); altered[altered.Length - 2] ^= 1;
                Throws<SafetyException>(() => f.Parse(altered, body));
                foreach (string changed in new[] { body.Replace("\"length\":" + index.Length, "\"length\":" + (index.Length + 1)), body.Replace(WindowsSignerEvidence.Hash(index), new string('f', 64)), body.Replace("\"count\":15", "\"count\":16"), body.Replace("\"totalLength\":15", "\"totalLength\":16"), body.Replace(ModernWindowsReleaseManifest.IndexName, "other.json") }) Throws<SafetyException>(() => f.Parse(index, changed));
            });
            Test("modern runtime and source declarations refuse unsupported families and unstable forms", delegate {
                foreach (string dotnet in new[] { "9.0.1", "11.0.1", "10.0.0-preview", "10.00.12", "10.0.12.0" }) { var f = new ModernManifestFixture { DotNet = dotnet }; Throws<SafetyException>(() => f.Parse()); }
                foreach (string sdk in new[] { "1.8.0", "3.0.0", "2.05.1", "2.5.1-preview" }) { var f = new ModernManifestFixture { Sdk = sdk }; Throws<SafetyException>(() => f.Parse()); }
                Throws<SafetyException>(() => new ModernManifestFixture { Minimum = "10.0.0.0" }.Parse());
                Throws<SafetyException>(() => new ModernManifestFixture { Version = "1.2.3-dev" }.Parse());
                Throws<SafetyException>(() => new ModernManifestFixture { Source = new string('B', 40) }.Parse());
            });
            Test("modern schemas refuse unknown duplicate fields annotations and trailing documents", delegate {
                var f = new ModernManifestFixture(); byte[] index = f.Index(); string body = f.Body(index);
                foreach (string changed in new[] { body.Replace("\"format\":2", "\"format\":2,\"format\":2"), body.Replace("\"format\":2", "\"format\":2,\"unknown\":0"), body + "{}", body.Replace("\"count\":15", "\"count\":\"15\"") }) Throws<SafetyException>(() => f.Parse(index, changed));
                foreach (string changed in new[] { f.IndexText().Replace("\"format\":1", "\"format\":1,\"format\":1"), f.IndexText().Replace("\"format\":1", "\"format\":1,\"extra\":0"), f.IndexText() + "{}", f.IndexText().Replace("\"length\":1", "\"length\":1,\"length\":1"), f.IndexText().Replace("\"format\":1", "\"format\":2") }) { byte[] bytes = Encoding.UTF8.GetBytes(changed); Throws<SafetyException>(() => f.Parse(bytes)); }
            });
            Test("modern payload paths refuse traversal absolute streams reserved devices and unsupported contents", delegate {
                foreach (string path in new[] { "../foreign.dll", "/foreign.dll", "C:/foreign.dll", "dir\\foreign.dll", "dir//foreign.dll", "dir/./foreign.dll", "dir/../foreign.dll", "foreign.dll:stream", "foreign.dll ", "foreign.dll.", "CON.dll", "nested/LPT9.txt", "AUX/file.dll", "nul.dll", "COM1/file.dll", "foreign.ps1", "foreign\n.dll", "é.dll", ModernWindowsReleaseManifest.IndexName, ModernWindowsReleaseManifest.ManifestName, ModernWindowsReleaseManifest.SignatureName }) {
                    var f = new ModernManifestFixture(); f.Files.Add(new WindowsReleaseFile(path, 1, new string('a', 64))); Throws<SafetyException>(() => f.Parse());
                }
            });
            Test("modern case aliases duplicate paths and noncanonical inventory order refuse", delegate {
                foreach (string path in new[] { "EasyEject.Runtime.dll", "easyeject.runtime.dll" }) { var f = new ModernManifestFixture(); f.Files.Add(new WindowsReleaseFile(path, 1, new string('a', 64))); Throws<SafetyException>(() => f.Parse()); }
                var order = new ModernManifestFixture(); byte[] bytes = Encoding.UTF8.GetBytes(order.IndexText().Replace("Assets/EasyEject.png", "zzzz.png")); Throws<SafetyException>(() => order.Parse(bytes));
            });
            Test("modern file directory prefix collisions refuse", delegate {
                var f = new ModernManifestFixture(); f.Files.Add(new WindowsReleaseFile("EasyEject.Runtime.dll/child.txt", 1, new string('a', 64))); Throws<SafetyException>(() => f.Parse());
            });
            Test("modern inventory requires every exact runtime entry artwork and release notices", delegate {
                foreach (string required in ModernWindowsReleaseManifest.RequiredFiles) {
                    var f = new ModernManifestFixture(); f.Files.RemoveAll(file => file.Name == required); f.Files.Add(new WindowsReleaseFile("foreign.txt", 1, new string('a', 64))); Throws<SafetyException>(() => f.Parse());
                }
                var casing = new ModernManifestFixture(); casing.Files.RemoveAll(file => file.Name == "Assets/EasyEject.png"); casing.Files.Add(new WindowsReleaseFile("assets/easyeject.png", 1, new string('a', 64))); Throws<SafetyException>(() => casing.Parse());
            });
            Test("modern payload sizes hashes and aggregate remain bounded", delegate {
                foreach (long length in new[] { 0L, -1L, ModernWindowsReleaseManifest.MaximumFile + 1 }) { var f = new ModernManifestFixture(); f.Files[0] = new WindowsReleaseFile(f.Files[0].Name, length, new string('a', 64)); Throws<SafetyException>(() => f.Parse()); }
                var oversized = new ModernManifestFixture(); for (int i = 0; i < 3; i++) oversized.Files[i] = new WindowsReleaseFile(oversized.Files[i].Name, ModernWindowsReleaseManifest.MaximumFile, new string('a', 64)); Throws<SafetyException>(() => oversized.Parse());
                var invalidHash = new ModernManifestFixture(); invalidHash.Files[0] = new WindowsReleaseFile(invalidHash.Files[0].Name, 1, new string('A', 64)); Throws<SafetyException>(() => invalidHash.Parse());
            });
            Test("modern inventory refuses excessive file counts even with a matching authenticated index", delegate {
                var f = new ModernManifestFixture(); for (int i = f.Files.Count; i <= ModernWindowsReleaseManifest.MaximumFiles; i++) f.Files.Add(new WindowsReleaseFile("file" + i.ToString("D4") + ".dll", 1, new string('a', 64))); Throws<SafetyException>(() => f.Parse());
            });
            Test("modern index decoder refuses invalid UTF8 BOM and non-object metadata", delegate {
                var f = new ModernManifestFixture(); byte[] ordinary = f.Index(); byte[] bom = new byte[] { 0xef, 0xbb, 0xbf }.Concat(ordinary).ToArray(); Throws<SafetyException>(() => f.Parse(bom));
                byte[] invalid = (byte[])ordinary.Clone(); invalid[5] = 0xff; Throws<SafetyException>(() => f.Parse(invalid));
                foreach (string value in new[] { "[]", "null", "1", "{}" }) Throws<SafetyException>(() => f.Parse(Encoding.UTF8.GetBytes(value)));
            });
            Test("modern parsed inventory retains immutable authenticated and caller byte snapshots", delegate {
                var f = new ModernManifestFixture(); byte[] index = f.Index(), body = Encoding.UTF8.GetBytes(f.Body(index));
                var snapshot = new WindowsAuthenticatedManifest(body, new byte[] { 1 }, new WindowsSignerEvidence(new string('c',64),new string('d',64),new string('e',64),FixtureSha256Oid));
                body[0] = 0; var release = ModernWindowsReleaseManifest.Parse(snapshot, index); string hash = release.IndexHash; index[0] = 0;
                Check(release.Files.Count == 15 && release.IndexHash == hash && release.Version == "1.2.3.4", "Caller mutated the parsed release");
                Throws<NotSupportedException>(() => ((IList<WindowsReleaseFile>)release.Files).Add(new WindowsReleaseFile("foreign.txt",1,new string('a',64))));
                byte[] copy = snapshot.CopyBytes(); copy[0] = 0; Check(snapshot.CopyBytes()[0] == '{', "Authenticated body is mutable");
            });
        }
    }
}
