using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        sealed class ModernPackageFixture : IDisposable {
            internal readonly Fixture Owned = new Fixture(); internal readonly ManifestSigningFixture Signing = new ManifestSigningFixture();
            internal readonly ModernManifestFixture Metadata = new ModernManifestFixture();
            internal readonly FakeExecutableTrust Executable; internal readonly FakeManifestCertificateTrust Certificate;
            internal string Root { get { return Owned.Source; } }
            internal string PathFor(string name) { return Path.Combine(Root, name.Replace('/', '\\')); }
            internal ModernPackageFixture(bool synthetic = true) {
                Executable = new FakeExecutableTrust { Evidence = Signing.Evidence() }; Certificate = new FakeManifestCertificateTrust { Evidence = Signing.Evidence() };
                Metadata.Files.Clear();
                if (!synthetic) return;
                foreach (string name in ModernWindowsReleaseManifest.RequiredFiles.Concat(new[] { "en-US/fixture.dll.mui", "nested/fixture.json" })) {
                    string path = PathFor(name); Directory.CreateDirectory(Path.GetDirectoryName(path));
                    byte[] bytes = name == "EasyEject.Modern.exe" ? File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EasyEject.Windows.exe")) : Encoding.UTF8.GetBytes("Owned synthetic content: " + name);
                    File.WriteAllBytes(path, bytes); Metadata.Files.Add(new WindowsReleaseFile(name, bytes.Length, WindowsSignerEvidence.Hash(bytes)));
                }
                Publish();
            }
            internal void Publish() {
                byte[] index = Metadata.Index(), body = Encoding.UTF8.GetBytes(Metadata.Body(index));
                File.WriteAllBytes(PathFor(ModernWindowsReleaseManifest.IndexName), index);
                File.WriteAllBytes(PathFor(ModernWindowsReleaseManifest.ManifestName), body);
                File.WriteAllBytes(PathFor(ModernWindowsReleaseManifest.SignatureName), Signing.Sign(body));
            }
            internal ModernWindowsPackageContentReview Review(CancellationToken token = default(CancellationToken), ApprovedWindowsPublisher publisher = null, IWindowsExecutableTrust executable = null, IWindowsManifestTrust manifest = null) {
                return new ModernWindowsPackageContentReview(Root, publisher ?? Signing.Publisher(), executable ?? Executable, manifest ?? new WindowsManifestTrust(Certificate), token);
            }
            internal void RequireReleased() { foreach (string path in Directory.GetFiles(Root, "*", SearchOption.AllDirectories)) using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
            public void Dispose() { try { Signing.Dispose(); } finally { Owned.Dispose(); } }
        }
        sealed class ModernWrongSnapshot : IWindowsManifestTrust {
            internal string Kind; internal WindowsSignerEvidence Signer;
            public WindowsAuthenticatedManifest Verify(byte[] body, byte[] signature, ApprovedWindowsPublisher approved, WindowsSignerEvidence executable, CancellationToken cancel) {
                if (Kind == "body") body[body.Length - 1] ^= 1;
                if (Kind == "signature") signature[signature.Length - 1] ^= 1;
                return new WindowsAuthenticatedManifest(body, signature, Signer ?? executable);
            }
        }
        static void RefuseModernContent(Func<ModernWindowsPackageContentReview> create) { try { using (var unexpected = create()) { } } catch (SafetyException) { return; } throw new Exception("Expected modern content refusal"); }
        static string ModernFixtureStream(ModernPackageFixture fixture, string name, string streamName, string text = null) {
            string path = name.Length == 0 ? fixture.Root : fixture.PathFor(name); Check(SafePaths.Within(path, fixture.Owned.Root) && (streamName == "Zone.Identifier" || streamName == "foreign"), "Unexpected stream fixture");
            using (var handle = Native.CreateFile(path + ":" + streamName, text == null ? 0x80000000U : 0x40000000U, 1, IntPtr.Zero, text == null ? 3U : 2U, 0x00200000, IntPtr.Zero)) {
                if (handle.IsInvalid) throw Native.Error("Cannot access owned modern stream fixture");
                Check(Native.FinalPath(handle).StartsWith(path + ":", StringComparison.OrdinalIgnoreCase), "Stream escaped owned fixture");
                using (var file = new FileStream(handle, text == null ? FileAccess.Read : FileAccess.Write)) {
                    if (text != null) { byte[] bytes = Encoding.UTF8.GetBytes(text); file.Write(bytes,0,bytes.Length); file.Flush(true); return text; }
                    using (var reader = new StreamReader(file, Encoding.UTF8)) return reader.ReadToEnd();
                }
            }
        }
        static void ModernWindowsPackageContentTests() {
            Test("modern content review retains actual nested bytes and exact authenticated inventory without loading components", delegate {
                using (var f = new ModernPackageFixture()) {
                    string original = WindowsSignerEvidence.Hash(File.ReadAllBytes(f.PathFor("EasyEject.Runtime.dll")));
                    using (var review = f.Review()) {
                        Check(review.Release.Files.Count == ModernWindowsReleaseManifest.RequiredFiles.Count + 2 && review.Authenticated.Signer.CertificateHash == f.Signing.Evidence().CertificateHash, "Actual inventory or signer lost");
                        review.Revalidate(CancellationToken.None);
                        review.Inspect("EasyEject.Runtime.dll", held => Check(VerifiedImporter.Hash(held, CancellationToken.None) == original, "Inspection changed held bytes"), CancellationToken.None);
                        foreach (var file in f.Metadata.Files) Throws<IOException>(() => File.WriteAllText(f.PathFor(file.Name), "replacement"));
                    }
                    Check(WindowsSignerEvidence.Hash(File.ReadAllBytes(f.PathFor("EasyEject.Runtime.dll"))) == original, "Review changed payload"); f.RequireReleased();
                }
            });
            Test("modern content review missing approval or precancellation performs no trust callback", delegate {
                using (var f = new ModernPackageFixture()) using (var cancel = new CancellationTokenSource()) {
                    RefuseModernContent(() => new ModernWindowsPackageContentReview(f.Root,null,f.Executable,new WindowsManifestTrust(f.Certificate),CancellationToken.None));
                    cancel.Cancel(); Throws<OperationCanceledException>(() => f.Review(cancel.Token)); Check(f.Executable.Calls == 0 && f.Certificate.Calls == 0,"Unapproved cancelled review reached trust"); f.RequireReleased();
                }
            });
            Test("modern content byte or length mismatches refuse and preserve manual retry", delegate {
                foreach (bool sameSize in new[] { false,true }) using (var f = new ModernPackageFixture()) {
                    string path = f.PathFor("en-US/fixture.dll.mui"); byte[] original = File.ReadAllBytes(path), changed = sameSize ? (byte[])original.Clone() : original.Concat(new byte[] { 1 }).ToArray(); changed[0] ^= 1; File.WriteAllBytes(path,changed);
                    RefuseModernContent(() => f.Review()); Check(File.ReadAllBytes(path).SequenceEqual(changed),"Refusal cleaned changed bytes"); f.RequireReleased(); File.WriteAllBytes(path,original); using (var retry = f.Review()) { }
                }
            });
            Test("modern content extra files and empty directories refuse without cleanup", delegate {
                foreach (string name in new[] { "foreign.txt", "en-US/foreign.mui", "empty" }) using (var f = new ModernPackageFixture()) {
                    string path = f.PathFor(name); if (name == "empty") Directory.CreateDirectory(path); else File.WriteAllText(path,"retained foreign fixture");
                    RefuseModernContent(() => f.Review()); Check(File.Exists(path) || Directory.Exists(path),"Extra content cleaned"); f.RequireReleased();
                }
            });
            Test("modern content missing metadata apphost or declared components refuse with old files retained", delegate {
                foreach (string name in new[] { ModernWindowsReleaseManifest.ManifestName, ModernWindowsReleaseManifest.SignatureName, ModernWindowsReleaseManifest.IndexName, "EasyEject.Modern.exe", "EasyEject.Modern.pri", "hostpolicy.dll", "Assets/EasyEject.png" }) using (var f = new ModernPackageFixture()) {
                    string path = f.PathFor(name), moved = Path.Combine(f.Owned.Destination,"retained.bin"); File.Move(path,moved); RefuseModernContent(() => f.Review()); Check(File.Exists(moved),"Missing file was restored/deleted automatically"); f.RequireReleased(); File.Move(moved,path); using (var retry = f.Review()) { }
                }
            });
            Test("modern content exact case of nested names is bound to authenticated paths", delegate {
                using (var f = new ModernPackageFixture()) { string path = f.PathFor("en-US/fixture.dll.mui"), temporary = Path.Combine(f.Owned.Destination,"case-file.tmp"); File.Move(path,temporary); File.Move(temporary,f.PathFor("en-US/Fixture.dll.mui")); RefuseModernContent(() => f.Review()); f.RequireReleased(); }
                using (var f = new ModernPackageFixture()) { string temporary = Path.Combine(f.Owned.Destination,"case-folder.tmp"); Directory.Move(f.PathFor("nested"),temporary); Directory.Move(temporary,f.PathFor("NESTED")); RefuseModernContent(() => f.Review()); f.RequireReleased(); }
            });
            Test("modern content holds all files before executable trust callbacks", delegate {
                using (var f = new ModernPackageFixture()) {
                    int heldChecks = 0; f.Executable.During = (path,held) => {
                        foreach (string file in Directory.GetFiles(f.Root,"*",SearchOption.AllDirectories)) { Throws<IOException>(() => File.Move(file,file + ".replacement")); heldChecks++; }
                    };
                    using (var review = f.Review()) { Check(heldChecks == f.Metadata.Files.Count + 3,"Not every file was held before the callback"); } f.RequireReleased();
                }
            });
            Test("modern content refuses manifest callback addition before returning reviewed metadata", delegate {
                using (var f = new ModernPackageFixture()) { f.Certificate.During = () => File.WriteAllText(f.PathFor("late.txt"),"retained"); RefuseModernContent(() => f.Review()); Check(File.ReadAllText(f.PathFor("late.txt")) == "retained","Late file cleaned"); f.RequireReleased(); }
            });
            Test("modern content captures an empty directory before a native trust callback can replace it", delegate {
                using (var f = new ModernPackageFixture()) {
                    string empty = f.PathFor("empty"), moved = Path.Combine(f.Owned.Destination,"moved-empty"); Directory.CreateDirectory(empty);
                    f.Executable.During = (path,held) => { Directory.Move(empty,moved); Directory.CreateDirectory(empty); File.WriteAllText(Path.Combine(empty,"retained.txt"),"replacement"); };
                    bool identityRefusal = false; try { using (var unexpected = f.Review()) { } } catch (SafetyException ex) { identityRefusal = ex.Message.Contains("held directory"); }
                    Check(identityRefusal && Directory.Exists(moved) && File.ReadAllText(Path.Combine(empty,"retained.txt")) == "replacement","Old directory identity or replacement retained data lost"); f.RequireReleased();
                }
            });
            Test("modern content rejects another publisher or a different authenticated signer snapshot", delegate {
                using (var f = new ModernPackageFixture()) { RefuseModernContent(() => f.Review(publisher:FixturePublisher())); Check(f.Certificate.Calls == 0,"Wrong apphost publisher reached manifest trust"); f.RequireReleased(); }
                using (var f = new ModernPackageFixture()) { var evidence = f.Signing.Evidence(); var changed = new WindowsSignerEvidence(new string('a',64),evidence.SubjectHash,evidence.IssuerCertificateHash,evidence.DigestOid); RefuseModernContent(() => f.Review(manifest:new ModernWrongSnapshot { Signer=changed })); f.RequireReleased(); }
            });
            Test("modern content rejects returned body signature or signer substitution", delegate {
                foreach (string kind in new[] { "body","signature" }) using (var f = new ModernPackageFixture()) { RefuseModernContent(() => f.Review(manifest:new ModernWrongSnapshot { Kind=kind })); f.RequireReleased(); }
            });
            Test("modern content native signature review refuses an actual unsigned fixture apphost", delegate {
                using (var f = new ModernPackageFixture()) { RefuseModernContent(() => f.Review(executable:new WindowsAuthenticode())); Check(f.Certificate.Calls == 0,"Untrusted apphost reached manifest trust"); f.RequireReleased(); }
            });
            Test("modern content preexisting and late hard links refuse without deleting aliases", delegate {
                using (var f = new ModernPackageFixture()) { string alias = Path.Combine(f.Owned.Destination,"alias.bin"); Check(CreateHardLink(alias,f.PathFor("en-US/fixture.dll.mui"),IntPtr.Zero),"Fixture link failed"); RefuseModernContent(() => f.Review()); Check(File.Exists(alias),"Alias deleted"); f.RequireReleased(); }
                using (var f = new ModernPackageFixture()) { string alias = Path.Combine(f.Owned.Destination,"late.bin"); using (var review = f.Review()) { Check(CreateHardLink(alias,f.PathFor("LICENSE.txt"),IntPtr.Zero),"Late fixture link failed"); Throws<SafetyException>(() => review.Revalidate(CancellationToken.None)); Check(File.Exists(alias),"Late alias deleted"); } f.RequireReleased(); }
            });
            Test("modern content unlisted data streams refuse on payload and directories", delegate {
                foreach (string name in new[] { "", "Assets", "en-US/fixture.dll.mui" }) using (var f = new ModernPackageFixture()) { ModernFixtureStream(f,name,"foreign","retained stream"); RefuseModernContent(() => f.Review()); Check(ModernFixtureStream(f,name,"foreign") == "retained stream","Foreign stream cleaned"); f.RequireReleased(); }
            });
            Test("modern content retains bounded Windows download metadata without using it as trust", delegate {
                using (var f = new ModernPackageFixture()) {
                    foreach (string name in new[] { "", "Assets", "EasyEject.Modern.exe", ModernWindowsReleaseManifest.ManifestName }) ModernFixtureStream(f,name,"Zone.Identifier","[ZoneTransfer]\r\nZoneId=3\r\n");
                    using (var review = f.Review()) { review.Revalidate(CancellationToken.None); }
                    Check(ModernFixtureStream(f,"EasyEject.Modern.exe","Zone.Identifier").Contains("ZoneId=3"),"Windows download metadata removed"); f.RequireReleased();
                }
            });
            Test("modern content excessive metadata refuses before signature callbacks", delegate {
                foreach (string name in new[] { ModernWindowsReleaseManifest.ManifestName, ModernWindowsReleaseManifest.SignatureName, ModernWindowsReleaseManifest.IndexName }) using (var f = new ModernPackageFixture()) {
                    int limit = name == ModernWindowsReleaseManifest.ManifestName ? WindowsManifestTrust.MaximumManifest : name == ModernWindowsReleaseManifest.SignatureName ? WindowsManifestTrust.MaximumSignature : ModernWindowsReleaseManifest.MaximumIndex;
                    File.WriteAllBytes(f.PathFor(name),new byte[limit+1]); RefuseModernContent(() => f.Review()); Check(f.Executable.Calls == 0 && f.Certificate.Calls == 0,"Oversize metadata reached trust"); f.RequireReleased();
                }
            });
            Test("modern content cancellation in native manifest or executable callbacks releases every file", delegate {
                foreach (bool duringManifest in new[] { false,true }) using (var f = new ModernPackageFixture()) using (var cancel = new CancellationTokenSource()) {
                    if (duringManifest) f.Certificate.During = cancel.Cancel; else f.Executable.During = (path,held) => cancel.Cancel();
                    Throws<OperationCanceledException>(() => f.Review(cancel.Token)); f.RequireReleased(); Check(Directory.GetFiles(f.Root,"*",SearchOption.AllDirectories).Length == f.Metadata.Files.Count + 3,"Cancellation deleted content");
                }
            });
            Test("modern content late extra directory and trust failures revoke revalidation", delegate {
                using (var f = new ModernPackageFixture()) { using (var review = f.Review()) { Directory.CreateDirectory(f.PathFor("late-empty")); Throws<SafetyException>(() => review.Revalidate(CancellationToken.None)); Check(Directory.Exists(f.PathFor("late-empty")),"Late directory cleaned"); } f.RequireReleased(); }
                using (var f = new ModernPackageFixture()) { using (var review = f.Review()) { f.Certificate.Throw = true; Throws<SafetyException>(() => review.Revalidate(CancellationToken.None)); } f.RequireReleased(); }
                using (var f = new ModernPackageFixture()) { using (var review = f.Review()) { f.Executable.Evidence = FixtureSigner(); Throws<SafetyException>(() => review.Revalidate(CancellationToken.None)); } f.RequireReleased(); }
            });
            Test("modern content held inspection rejects foreign names and cancellation while preserving bytes", delegate {
                using (var f = new ModernPackageFixture()) { using (var review = f.Review()) using (var cancel = new CancellationTokenSource()) {
                    Throws<SafetyException>(() => review.Inspect("foreign.txt",held => { throw new Exception("Foreign callback ran"); },CancellationToken.None));
                    Throws<OperationCanceledException>(() => review.Inspect("LICENSE.txt",held => cancel.Cancel(),cancel.Token));
                } f.RequireReleased(); }
            });
            Test("modern content disposal is terminal and releases root for a fresh manual review", delegate {
                using (var f = new ModernPackageFixture()) { var review = f.Review(); review.Dispose(); review.Dispose(); Throws<ObjectDisposedException>(() => review.Revalidate(CancellationToken.None)); f.RequireReleased(); using (var retry = f.Review()) { } Directory.Move(f.Root,f.Root + "-retained"); }
            });
        }
    }
}
