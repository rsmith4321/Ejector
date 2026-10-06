using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        const string FixtureSha256Oid = "2.16.840.1.101.3.4.2.1";
        static WindowsSignerEvidence FixtureSigner() { return new WindowsSignerEvidence(new string('a', 64), new string('b', 64), new string('c', 64), FixtureSha256Oid); }
        static ApprovedWindowsPublisher FixturePublisher() { return new ApprovedWindowsPublisher(new string('b', 64), new string('c', 64)); }
        sealed class FakeExecutableTrust : IWindowsExecutableTrust {
            internal int Calls; internal Action<string, FileStream> During; internal WindowsSignerEvidence Evidence = FixtureSigner();
            public WindowsSignerEvidence Verify(string path, FileStream held) { Calls++; if (During != null) During(path, held); return Evidence; }
        }
        sealed class FakeAuthenticodeApi : IAuthenticodeApi {
            internal int Status, CloseStatus, VerifyCalls, CloseCalls, SignerCalls; internal bool ThrowVerify, ThrowSigner, MissingState, WrongIndex;
            internal IntPtr FileHandle; internal string FilePath; internal WindowsSignerEvidence Evidence = FixtureSigner();
            public int Verify(Guid action, ref WindowsAuthenticode.TrustData data) {
                Check(action == WindowsAuthenticode.VerifyAction, "Wrong native verification action");
                if (data.StateAction == 2) { CloseCalls++; Check(data.State == (MissingState ? IntPtr.Zero : new IntPtr(123)), "Close lost verification state"); data.State = IntPtr.Zero; return CloseStatus; }
                VerifyCalls++; Check(data.StateAction == 1 && data.Size == 88 && data.Ui == 2 && data.Revocation == 1 && data.Choice == 1 && data.Flags == 0x3080 && data.PolicyCallback == IntPtr.Zero && data.Sip == IntPtr.Zero && data.Url == IntPtr.Zero, "Native review permits UI, network, absent revocation or unsafe policy");
                var file = (WindowsAuthenticode.TrustFile)Marshal.PtrToStructure(data.File, typeof(WindowsAuthenticode.TrustFile));
                Check(file.Size == 32 && file.Handle != IntPtr.Zero && file.Subject == IntPtr.Zero, "Native review reopened candidate or passed a wrong file structure"); FileHandle = file.Handle; FilePath = Marshal.PtrToStringUni(file.Path);
                var settings = (WindowsAuthenticode.SignatureSettings)Marshal.PtrToStructure(data.SignatureSettings, typeof(WindowsAuthenticode.SignatureSettings));
                Check(settings.Size == 32 && settings.Index == 0 && settings.Flags == 1 && settings.Policy != IntPtr.Zero, "Native review may select an unspecified signature");
                var policy = (WindowsAuthenticode.StrongPolicy)Marshal.PtrToStructure(settings.Policy, typeof(WindowsAuthenticode.StrongPolicy));
                var serialized = (WindowsAuthenticode.StrongSerialized)Marshal.PtrToStructure(policy.Info, typeof(WindowsAuthenticode.StrongSerialized));
                Check(policy.Size == 16 && policy.Choice == 1 && serialized.Flags == 3 && Marshal.PtrToStringUni(serialized.Algorithms) == WindowsAuthenticode.SignatureAlgorithms && Marshal.PtrToStringUni(serialized.Keys) == WindowsAuthenticode.MinimumKeys, "Native strong signature policy is missing or weaker than approved");
                data.State = MissingState ? IntPtr.Zero : new IntPtr(123); if (WrongIndex) { settings.VerifiedIndex = 1; Marshal.StructureToPtr(settings, data.SignatureSettings, false); }
                if (ThrowVerify) throw new InvalidOperationException("fixture verify failed"); return Status;
            }
            public WindowsSignerEvidence ReadSigner(IntPtr state) { SignerCalls++; Check(state == new IntPtr(123), "Signer read outside live verified state"); if (ThrowSigner) throw new InvalidOperationException("fixture signer failed"); return Evidence; }
        }
        static void WindowsAuthenticodeTests() {
            Test("Windows publisher approval requires exact lowercase subject and issuer certificate hashes", delegate {
                foreach (string invalid in new[] { null, "", new string('A', 64), new string('a', 63), new string('g', 64) }) {
                    Throws<ArgumentException>(() => new ApprovedWindowsPublisher(invalid, new string('c', 64)));
                    Throws<ArgumentException>(() => new ApprovedWindowsPublisher(new string('b', 64), invalid));
                }
            });
            Test("Windows signer evidence refuses missing certificate identity and weak or unknown digests", delegate {
                Throws<SafetyException>(() => new WindowsSignerEvidence("", new string('b', 64), new string('c', 64), FixtureSha256Oid));
                foreach (string digest in new[] { null, "", "1.3.14.3.2.26", "1.2.840.113549.2.5", "unexpected" })
                    Throws<SafetyException>(() => new WindowsSignerEvidence(new string('a', 64), new string('b', 64), new string('c', 64), digest));
                foreach (string digest in new[] { FixtureSha256Oid, "2.16.840.1.101.3.4.2.2", "2.16.840.1.101.3.4.2.3" })
                    Check(new WindowsSignerEvidence(new string('a', 64), new string('b', 64), new string('c', 64), digest).DigestOid == digest, "SHA2 evidence was lost");
            });
            Test("Windows publisher binding rejects another subject another issuer and missing native proof", delegate {
                var approved = FixturePublisher(); approved.Require(FixtureSigner()); Throws<SafetyException>(() => approved.Require(null));
                Throws<SafetyException>(() => approved.Require(new WindowsSignerEvidence(new string('a', 64), new string('d', 64), new string('c', 64), FixtureSha256Oid)));
                Throws<SafetyException>(() => approved.Require(new WindowsSignerEvidence(new string('a', 64), new string('b', 64), new string('d', 64), FixtureSha256Oid)));
                approved.Require(new WindowsSignerEvidence(new string('d', 64), new string('b', 64), new string('c', 64), FixtureSha256Oid));
            });
            Test("Windows unconfigured publisher refuses before opening or creating candidate storage", delegate {
                using (var f = new Fixture()) { var trust = new FakeExecutableTrust(); string missing = Path.Combine(f.Root, "missing", "app.exe");
                    Check(Throws<SafetyException>(() => new WindowsSignedFileReview(missing, null, trust, CancellationToken.None)).Message.Contains("No approved Windows publisher"), "Unconfigured channel became a valid review");
                    Check(trust.Calls == 0 && !Directory.Exists(Path.GetDirectoryName(missing)), "Unconfigured review created storage or queried signer");
                }
            });
            Test("Windows signature review holds exact file and folder and releases only on disposal", delegate {
                using (var f = new Fixture()) { f.Put("candidate.exe", "synthetic candidate bytes, no executable or signing proof"); string path = Path.Combine(f.Source, "candidate.exe"); var trust = new FakeExecutableTrust();
                    using (var review = new WindowsSignedFileReview(path, FixturePublisher(), trust, CancellationToken.None)) {
                        Check(trust.Calls == 1 && review.Path == path && review.Hash == WindowsSignerEvidence.Hash(File.ReadAllBytes(path)) && review.Signer.CertificateHash == new string('a', 64), "Candidate evidence not bound"); review.Validate();
                        Throws<IOException>(() => File.WriteAllText(path, "foreign write")); Throws<IOException>(() => File.Delete(path)); Throws<IOException>(() => File.Move(path, path + ".moved")); Throws<IOException>(() => Directory.Move(f.Source, f.Source + "-moved"));
                        Check(File.ReadAllText(path).Contains("synthetic candidate"), "Review changed candidate contents");
                    }
                    File.Move(path, path + ".moved"); Directory.Move(f.Source, f.Source + "-moved");
                }
            });
            Test("Windows rejected signer review releases held file without editing it", delegate {
                using (var f = new Fixture()) { f.Put("candidate.exe", "unchanged"); string path = Path.Combine(f.Source, "candidate.exe"); var trust = new FakeExecutableTrust { Evidence = new WindowsSignerEvidence(new string('a', 64), new string('d', 64), new string('c', 64), FixtureSha256Oid) };
                    Throws<SafetyException>(() => new WindowsSignedFileReview(path, FixturePublisher(), trust, CancellationToken.None)); Check(File.ReadAllText(path) == "unchanged", "Unknown publisher altered candidate"); File.Move(path, path + ".released");
                }
            });
            Test("Windows signature review refuses preexisting hard link before invoking trust", delegate {
                using (var f = new Fixture()) { f.Put("candidate.exe", "unchanged"); string path = Path.Combine(f.Source, "candidate.exe"), alias = Path.Combine(f.Destination, "alias.keep"); Check(CreateHardLink(alias, path, IntPtr.Zero), "Could not create disposable candidate alias"); var trust = new FakeExecutableTrust();
                    Throws<SafetyException>(() => new WindowsSignedFileReview(path, FixturePublisher(), trust, CancellationToken.None)); Check(trust.Calls == 0 && File.ReadAllText(path) == "unchanged" && File.ReadAllText(alias) == "unchanged", "Aliased candidate reached trust or changed contents");
                }
            });
            Test("Windows signature review refuses a new hard link created during trust", delegate {
                using (var f = new Fixture()) { f.Put("candidate.exe", "unchanged"); string path = Path.Combine(f.Source, "candidate.exe"), alias = Path.Combine(f.Destination, "alias.keep"); var trust = new FakeExecutableTrust { During = (p, stream) => Check(CreateHardLink(alias, p, IntPtr.Zero), "Could not create late disposable alias") };
                    Throws<SafetyException>(() => new WindowsSignedFileReview(path, FixturePublisher(), trust, CancellationToken.None)); Check(File.ReadAllText(path) == "unchanged" && File.ReadAllText(alias) == "unchanged", "Late alias became approval or changed candidate");
                }
            });
            Test("Windows signature review revalidation refuses a newly aliased reviewed candidate", delegate {
                using (var f = new Fixture()) { f.Put("candidate.exe", "unchanged"); string path = Path.Combine(f.Source, "candidate.exe"), alias = Path.Combine(f.Destination, "alias.keep"); using (var review = new WindowsSignedFileReview(path, FixturePublisher(), new FakeExecutableTrust(), CancellationToken.None)) {
                    Check(CreateHardLink(alias, path, IntPtr.Zero), "Could not create post-review disposable alias"); Throws<SafetyException>(review.Validate); Check(File.ReadAllText(alias) == "unchanged", "Revalidation changed alias");
                } }
            });
            Test("Windows signature review refuses empty candidate without invoking trust", delegate {
                using (var f = new Fixture()) { f.Put("candidate.exe", ""); var trust = new FakeExecutableTrust(); Throws<SafetyException>(() => new WindowsSignedFileReview(Path.Combine(f.Source, "candidate.exe"), FixturePublisher(), trust, CancellationToken.None)); Check(trust.Calls == 0, "Empty file reached signature verification"); }
            });
            Test("Windows signature review cancellation before admission performs no trust call", delegate {
                using (var f = new Fixture()) using (var cancel = new CancellationTokenSource()) { f.Put("candidate.exe", "unchanged"); cancel.Cancel(); var trust = new FakeExecutableTrust(); Throws<OperationCanceledException>(() => new WindowsSignedFileReview(Path.Combine(f.Source, "candidate.exe"), FixturePublisher(), trust, cancel.Token)); Check(trust.Calls == 0, "Cancelled review verified a candidate"); }
            });
            Test("Windows signature review cancellation during trust refuses and releases candidate", delegate {
                using (var f = new Fixture()) using (var cancel = new CancellationTokenSource()) { f.Put("candidate.exe", "unchanged"); string path = Path.Combine(f.Source, "candidate.exe"); var trust = new FakeExecutableTrust { During = (p, stream) => cancel.Cancel() };
                    Throws<OperationCanceledException>(() => new WindowsSignedFileReview(path, FixturePublisher(), trust, cancel.Token)); Check(trust.Calls == 1 && File.ReadAllText(path) == "unchanged", "Cancellation repeated trust or changed file"); File.Move(path, path + ".released");
                }
            });
            Test("Windows signature review refuses redirected candidate folder before trust", delegate {
                using (var f = new Fixture()) { f.Put("candidate.exe", "unchanged"); string link = Path.Combine(f.Root, "alias"); CreateJunction(f, link, f.Source); var trust = new FakeExecutableTrust(); Throws<SafetyException>(() => new WindowsSignedFileReview(Path.Combine(link, "candidate.exe"), FixturePublisher(), trust, CancellationToken.None)); Check(trust.Calls == 0 && File.ReadAllText(Path.Combine(f.Source, "candidate.exe")) == "unchanged", "Redirected candidate reached trust or changed target"); }
            });
            Test("Windows disposed signature review refuses further validation and is idempotent", delegate {
                using (var f = new Fixture()) { f.Put("candidate.exe", "unchanged"); var review = new WindowsSignedFileReview(Path.Combine(f.Source, "candidate.exe"), FixturePublisher(), new FakeExecutableTrust(), CancellationToken.None); review.Dispose(); review.Dispose(); Throws<ObjectDisposedException>(review.Validate); }
            });
            Test("Windows native trust structures match x64 layout", delegate {
                Check(IntPtr.Size == 8 && Marshal.SizeOf(typeof(WindowsAuthenticode.TrustData)) == 88 && Marshal.OffsetOf(typeof(WindowsAuthenticode.TrustData), "File").ToInt32() == 40 && Marshal.OffsetOf(typeof(WindowsAuthenticode.TrustData), "State").ToInt32() == 56 && Marshal.OffsetOf(typeof(WindowsAuthenticode.TrustData), "SignatureSettings").ToInt32() == 80, "Wrong WINTRUST_DATA layout");
                Check(Marshal.SizeOf(typeof(WindowsAuthenticode.ProviderSigner)) == 64 && Marshal.OffsetOf(typeof(WindowsAuthenticode.ProviderSigner), "Signer").ToInt32() == 32 && Marshal.SizeOf(typeof(WindowsAuthenticode.CertificateHeader)) == 16 && Marshal.OffsetOf(typeof(WindowsAuthenticode.SignerHeader), "Hash").ToInt32() == 40, "Wrong native signer/certificate layout");
            });
            Test("Windows native trust passes held handle strong policy and restores stream position after closing state", delegate {
                using (var f = new Fixture()) { f.Put("candidate.exe", "unchanged"); string path = Path.Combine(f.Source, "candidate.exe"); using (var stream = SafePaths.ReadFile(path)) {
                    stream.Position = 3; var api = new FakeAuthenticodeApi(); var evidence = new WindowsAuthenticode(api).Verify(path, stream);
                    Check(api.VerifyCalls == 1 && api.CloseCalls == 1 && api.SignerCalls == 1 && api.FileHandle == stream.SafeFileHandle.DangerousGetHandle() && api.FilePath == path && stream.Position == 3 && evidence.CertificateHash == new string('a', 64), "Native review lost exact handle, position, evidence or state cleanup");
                } }
            });
            Test("Windows native trust rejects positive nonzero LONG as well as failure HRESULT without reading signer", delegate {
                using (var f = new Fixture()) { f.Put("candidate.exe", "unchanged"); string path = Path.Combine(f.Source, "candidate.exe"); foreach (int status in new[] { 1, unchecked((int)0x800B0100), unchecked((int)0x80092013) }) using (var stream = SafePaths.ReadFile(path)) {
                    var api = new FakeAuthenticodeApi { Status = status }; Check(Throws<SafetyException>(() => new WindowsAuthenticode(api).Verify(path, stream)).Message.Contains(unchecked((uint)status).ToString("X8")), "Native failure code lost"); Check(api.VerifyCalls == 1 && api.CloseCalls == 1 && api.SignerCalls == 0, "Failed verification read signer or leaked state");
                } }
            });
            Test("Windows native verify exception still closes state and retains readable candidate", delegate {
                using (var f = new Fixture()) { f.Put("candidate.exe", "unchanged"); string path = Path.Combine(f.Source, "candidate.exe"); using (var stream = SafePaths.ReadFile(path)) { var api = new FakeAuthenticodeApi { ThrowVerify = true }; Throws<InvalidOperationException>(() => new WindowsAuthenticode(api).Verify(path, stream)); Check(api.CloseCalls == 1 && api.SignerCalls == 0 && stream.ReadByte() >= 0, "Verify exception leaked state or closed caller file"); } }
            });
            Test("Windows native signer extraction exception still closes state", delegate {
                using (var f = new Fixture()) { f.Put("candidate.exe", "unchanged"); string path = Path.Combine(f.Source, "candidate.exe"); using (var stream = SafePaths.ReadFile(path)) { var api = new FakeAuthenticodeApi { ThrowSigner = true }; Throws<InvalidOperationException>(() => new WindowsAuthenticode(api).Verify(path, stream)); Check(api.CloseCalls == 1 && api.SignerCalls == 1, "Signer exception leaked verified state"); } }
            });
            Test("Windows native trust refuses missing state wrong selected signature and absent signer", delegate {
                using (var f = new Fixture()) { f.Put("candidate.exe", "unchanged"); string path = Path.Combine(f.Source, "candidate.exe"); foreach (var api in new[] { new FakeAuthenticodeApi { MissingState = true }, new FakeAuthenticodeApi { WrongIndex = true }, new FakeAuthenticodeApi { Evidence = null } }) using (var stream = SafePaths.ReadFile(path)) { Throws<SafetyException>(() => new WindowsAuthenticode(api).Verify(path, stream)); Check(api.CloseCalls == 1 && api.VerifyCalls == 1, "Incomplete native proof leaked state or retried"); } }
            });
            Test("Windows native trust close failure cannot return successful approval", delegate {
                using (var f = new Fixture()) { f.Put("candidate.exe", "unchanged"); string path = Path.Combine(f.Source, "candidate.exe"); using (var stream = SafePaths.ReadFile(path)) { var api = new FakeAuthenticodeApi { CloseStatus = 1 }; Check(Throws<SafetyException>(() => new WindowsAuthenticode(api).Verify(path, stream)).Message.Contains("release signature verification state"), "State-close failure became approval"); Check(api.CloseCalls == 1 && stream.ReadByte() >= 0, "Close failure retried or closed caller file"); } }
            });
            Test("Windows native trust refuses wrong held path before calling native verification", delegate {
                using (var f = new Fixture()) { f.Put("candidate.exe", "unchanged"); string path = Path.Combine(f.Source, "candidate.exe"); using (var stream = SafePaths.ReadFile(path)) { var api = new FakeAuthenticodeApi(); Throws<SafetyException>(() => new WindowsAuthenticode(api).Verify(Path.Combine(f.Source, "other.exe"), stream)); Check(api.VerifyCalls == 0 && api.CloseCalls == 0, "Wrong candidate path reached Windows trust"); } }
            });
            Test("Windows real native trust refuses owned unsigned compiled candidate without execution", delegate {
                using (var f = new Fixture()) { string path = Path.Combine(f.Source, "unsigned.exe"); File.Copy(typeof(Tests).Assembly.Location, path); string before = WindowsSignerEvidence.Hash(File.ReadAllBytes(path));
                    using (var held = SafePaths.ReadFile(path)) { held.Position = 5; Check(Throws<SafetyException>(() => new WindowsAuthenticode().Verify(path, held)).Message.Contains("Windows signature trust could not be established"), "Unsigned native executable became trusted"); Check(held.Position == 5 && held.CanRead, "Native trust changed caller stream"); }
                    Check(WindowsSignerEvidence.Hash(File.ReadAllBytes(path)) == before && Directory.GetFileSystemEntries(f.Source).Length == 1, "Native trust modified candidate or extracted files"); File.Move(path, path + ".released");
                }
            });
        }
    }
}
