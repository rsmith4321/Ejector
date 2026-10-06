using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        sealed class ManifestSigningFixture : IDisposable {
            readonly RSA key; internal readonly X509Certificate2 Certificate;
            internal readonly byte[] Body = Encoding.UTF8.GetBytes("{\"platform\":\"windows-x64\",\"fixture\":true}");
            internal ManifestSigningFixture(bool expired = false) {
                // CreateEphemeralKey plus PersistKeyInCsp=false: no key file, account or trust-store import.
                var rsa = new RSACryptoServiceProvider(2048, new CspParameters { Flags = CspProviderFlags.CreateEphemeralKey }); rsa.PersistKeyInCsp = false; key = rsa;
                var request = new CertificateRequest("CN=Easy Eject Disposable Manifest Fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid(WindowsCertificateTrust.CodeSigningOid) }, false));
                Certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-2), expired ? DateTimeOffset.UtcNow.AddHours(-1) : DateTimeOffset.UtcNow.AddHours(1));
            }
            internal byte[] Sign(byte[] body = null, bool detached = true, string digest = FixtureSha256Oid, bool includeCertificate = true, ManifestSigningFixture other = null) {
                var cms = new SignedCms(new ContentInfo(body ?? Body), detached);
                cms.ComputeSignature(new CmsSigner(Certificate) { DigestAlgorithm = new Oid(digest), IncludeOption = includeCertificate ? X509IncludeOption.EndCertOnly : X509IncludeOption.None }, true);
                if (other != null) cms.ComputeSignature(new CmsSigner(other.Certificate) { DigestAlgorithm = new Oid(digest), IncludeOption = X509IncludeOption.EndCertOnly }, true);
                return cms.Encode();
            }
            internal WindowsSignerEvidence Evidence(string digest = FixtureSha256Oid) { return new WindowsSignerEvidence(WindowsSignerEvidence.Hash(Certificate.RawData), WindowsSignerEvidence.Hash(Certificate.SubjectName.RawData), new string('c', 64), digest); }
            internal ApprovedWindowsPublisher Publisher() { return new ApprovedWindowsPublisher(Evidence().SubjectHash, Evidence().IssuerCertificateHash); }
            public void Dispose() { Certificate.Dispose(); key.Dispose(); }
        }
        sealed class FakeManifestCertificateTrust : IWindowsCertificateTrust {
            internal int Calls; internal Action During; internal WindowsSignerEvidence Evidence; internal bool Throw;
            public WindowsSignerEvidence Verify(X509Certificate2 leaf, X509Certificate2Collection certificates, string digest) {
                Calls++; Check(leaf != null && certificates.Count > 0 && WindowsSignerEvidence.StrongDigest(digest), "Unexpected certificate proof request");
                if (During != null) During(); if (Throw) throw new SafetyException("fixture native trust refusal"); return Evidence;
            }
        }
        sealed class FakeWindowsChainApi : IWindowsChainApi {
            internal string Failure; internal uint PolicyError; internal int OpenCalls, Adds, Builds, Policies, Reads, Frees, Closes;
            internal bool MissingStore, MissingChain; internal WindowsSignerEvidence OverrideEvidence;
            internal readonly List<string> Order = new List<string>();
            void Stage(string stage) { Order.Add(stage); if (Failure == stage) throw new InvalidOperationException("fixture " + stage); }
            public IntPtr OpenMemoryStore() { OpenCalls++; Stage("Open"); return MissingStore ? IntPtr.Zero : new IntPtr(111); }
            public void AddCertificate(IntPtr store, IntPtr certificate) { Adds++; Stage("Add"); Check(store == new IntPtr(111) && certificate != IntPtr.Zero, "Wrong temporary store/certificate"); }
            public IntPtr Build(IntPtr leaf, IntPtr store, ref WindowsCertificateTrust.ChainParameters parameters, uint flags) {
                Builds++; Stage("Build"); Check(leaf != IntPtr.Zero && store == new IntPtr(111) && flags == 0xc0000104 && parameters.Size == 96 && parameters.Usage.Type == 0 && parameters.Usage.Usage.Count == 1 && parameters.Usage.Usage.Oids != IntPtr.Zero && parameters.Issuance.Usage.Count == 0 && parameters.StrongFlags == 0 && parameters.Resync == IntPtr.Zero && parameters.FreshnessCheck == 0, "Chain verification permits network, unknown usage, historical time or weaker policy");
                Check(Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(parameters.Usage.Usage.Oids)) == WindowsCertificateTrust.CodeSigningOid, "Chain accepts unrelated certificate usage");
                var policy = (WindowsAuthenticode.StrongPolicy)Marshal.PtrToStructure(parameters.Strong, typeof(WindowsAuthenticode.StrongPolicy));
                var strong = (WindowsAuthenticode.StrongSerialized)Marshal.PtrToStructure(policy.Info, typeof(WindowsAuthenticode.StrongSerialized));
                Check(policy.Size == 16 && policy.Choice == 1 && strong.Flags == 3 && Marshal.PtrToStringUni(strong.Algorithms) == WindowsAuthenticode.SignatureAlgorithms && Marshal.PtrToStringUni(strong.Keys) == WindowsAuthenticode.MinimumKeys, "Chain strong-signature policy lost");
                return MissingChain ? IntPtr.Zero : new IntPtr(222);
            }
            public uint Policy(IntPtr chain) { Policies++; Stage("Policy"); Check(chain == new IntPtr(222), "Wrong policy chain"); return PolicyError; }
            public WindowsSignerEvidence ReadSigner(IntPtr chain, X509Certificate2 leaf, string digest) { Reads++; Stage("Read"); Check(chain == new IntPtr(222), "Wrong signer chain"); return OverrideEvidence ?? new WindowsSignerEvidence(WindowsSignerEvidence.Hash(leaf.RawData), WindowsSignerEvidence.Hash(leaf.SubjectName.RawData), new string('c', 64), digest); }
            public void Free(IntPtr chain) { Frees++; Stage("Free"); Check(chain == new IntPtr(222), "Freed wrong chain"); }
            public void CloseMemoryStore(IntPtr store) { Closes++; Stage("Close"); Check(store == new IntPtr(111), "Closed wrong memory store"); }
        }
        sealed class ObservedWindowsChainApi : IWindowsChainApi {
            readonly NativeWindowsChainApi native = new NativeWindowsChainApi(); internal uint Errors; internal int Frees, Closes;
            public IntPtr OpenMemoryStore() { return native.OpenMemoryStore(); }
            public void AddCertificate(IntPtr store, IntPtr certificate) { native.AddCertificate(store, certificate); }
            public IntPtr Build(IntPtr leaf, IntPtr store, ref WindowsCertificateTrust.ChainParameters parameters, uint flags) {
                IntPtr chain = native.Build(leaf, store, ref parameters, flags);
                try { Errors = ((WindowsCertificateTrust.ChainHeader)Marshal.PtrToStructure(chain, typeof(WindowsCertificateTrust.ChainHeader))).Error; return chain; }
                catch { native.Free(chain); throw; }
            }
            public uint Policy(IntPtr chain) { return native.Policy(chain); }
            public WindowsSignerEvidence ReadSigner(IntPtr chain, X509Certificate2 leaf, string digest) { return native.ReadSigner(chain, leaf, digest); }
            public void Free(IntPtr chain) { Frees++; native.Free(chain); }
            public void CloseMemoryStore(IntPtr store) { Closes++; native.CloseMemoryStore(store); }
        }
        static void WindowsManifestTrustTests() {
            Test("Windows manifest uses exact detached signed bytes with separate required certificate trust", delegate {
                using (var f = new ManifestSigningFixture()) { var trust = new FakeManifestCertificateTrust { Evidence = f.Evidence() }; byte[] signature = f.Sign();
                    var result = new WindowsManifestTrust(trust).Verify(f.Body, signature, f.Publisher(), f.Evidence(), CancellationToken.None);
                    Check(trust.Calls == 1 && result.Signer.CertificateHash == f.Evidence().CertificateHash && result.BodyHash == WindowsSignerEvidence.Hash(f.Body) && result.SignatureHash == WindowsSignerEvidence.Hash(signature) && result.CopyBytes().SequenceEqual(f.Body), "Manifest lost exact snapshot or crypto/publisher/executable agreement");
                }
            });
            Test("Windows manifest validates SHA384 and SHA512 detached signatures with exact digest evidence", delegate {
                using (var f = new ManifestSigningFixture()) foreach (string digest in new[] { "2.16.840.1.101.3.4.2.2", "2.16.840.1.101.3.4.2.3" }) {
                    var trust = new FakeManifestCertificateTrust { Evidence = f.Evidence(digest) }; var result = new WindowsManifestTrust(trust).Verify(f.Body, f.Sign(digest: digest), f.Publisher(), f.Evidence(), CancellationToken.None);
                    Check(trust.Calls == 1 && result.Signer.DigestOid == digest && result.BodyHash == WindowsSignerEvidence.Hash(f.Body), "Supported actual CMS digest evidence was lost");
                }
            });
            Test("Windows manifest refuses changed bytes before querying certificate trust", delegate {
                using (var f = new ManifestSigningFixture()) { var trust = new FakeManifestCertificateTrust { Evidence = f.Evidence() }; byte[] signature = f.Sign(), changed = (byte[])f.Body.Clone(); changed[changed.Length - 2] ^= 1;
                    Throws<SafetyException>(() => new WindowsManifestTrust(trust).Verify(changed, signature, f.Publisher(), f.Evidence(), CancellationToken.None)); Check(trust.Calls == 0, "Changed exact bytes reached native trust");
                }
            });
            Test("Windows manifest refuses altered signature bytes before querying certificate trust", delegate {
                using (var f = new ManifestSigningFixture()) { var trust = new FakeManifestCertificateTrust { Evidence = f.Evidence() }; byte[] signature = f.Sign(); signature[signature.Length - 3] ^= 1;
                    Throws<SafetyException>(() => new WindowsManifestTrust(trust).Verify(f.Body, signature, f.Publisher(), f.Evidence(), CancellationToken.None)); Check(trust.Calls == 0, "Altered cryptographic signature reached native trust");
                }
            });
            Test("Windows manifest refuses embedded content even when its signature is valid", delegate {
                using (var f = new ManifestSigningFixture()) { var trust = new FakeManifestCertificateTrust { Evidence = f.Evidence() }; byte[] signature = f.Sign(detached: false);
                    Throws<SafetyException>(() => new WindowsManifestTrust(trust).Verify(f.Body, signature, f.Publisher(), f.Evidence(), CancellationToken.None)); Check(trust.Calls == 0, "Embedded body overrode supplied manifest bytes");
                }
            });
            Test("Windows manifest refuses multiple signers and absent embedded certificate", delegate {
                using (var f = new ManifestSigningFixture()) using (var other = new ManifestSigningFixture()) { var trust = new FakeManifestCertificateTrust { Evidence = f.Evidence() };
                    foreach (byte[] signature in new[] { f.Sign(other: other), f.Sign(includeCertificate: false) }) Throws<SafetyException>(() => new WindowsManifestTrust(trust).Verify(f.Body, signature, f.Publisher(), f.Evidence(), CancellationToken.None));
                    Check(trust.Calls == 0, "Ambiguous or absent signer reached certificate trust");
                }
            });
            Test("Windows manifest refuses SHA1 even with a mathematically valid detached signature", delegate {
                using (var f = new ManifestSigningFixture()) { var trust = new FakeManifestCertificateTrust { Evidence = f.Evidence() }; byte[] signature = f.Sign(digest: "1.3.14.3.2.26");
                    Throws<SafetyException>(() => new WindowsManifestTrust(trust).Verify(f.Body, signature, f.Publisher(), f.Evidence(), CancellationToken.None)); Check(trust.Calls == 0, "Weak digest reached certificate trust");
                }
            });
            Test("Windows manifest refuses unconfigured approval unapproved executable and unapproved manifest publisher", delegate {
                using (var f = new ManifestSigningFixture()) { var trust = new FakeManifestCertificateTrust { Evidence = f.Evidence() }; var validator = new WindowsManifestTrust(trust);
                    Throws<SafetyException>(() => validator.Verify(null, null, null, f.Evidence(), CancellationToken.None)); Throws<SafetyException>(() => validator.Verify(f.Body, f.Sign(), f.Publisher(), null, CancellationToken.None));
                    Throws<SafetyException>(() => validator.Verify(f.Body, f.Sign(), FixturePublisher(), FixtureSigner(), CancellationToken.None)); Check(trust.Calls == 1, "Unexpected unconfigured/unapproved proof calls");
                }
            });
            Test("Windows manifest crypto-only success cannot bypass native certificate refusal", delegate {
                using (var f = new ManifestSigningFixture()) { var trust = new FakeManifestCertificateTrust { Evidence = f.Evidence(), Throw = true };
                    Check(Throws<SafetyException>(() => new WindowsManifestTrust(trust).Verify(f.Body, f.Sign(), f.Publisher(), f.Evidence(), CancellationToken.None)).Message.Contains("native trust refusal"), "Crypto-only verification became release trust"); Check(trust.Calls == 1, "Native refusal bypassed or retried");
                }
            });
            Test("Windows manifest requires same exact certificate as executable not only approved subject issuer", delegate {
                using (var f = new ManifestSigningFixture()) using (var other = new ManifestSigningFixture()) { var trust = new FakeManifestCertificateTrust { Evidence = other.Evidence() };
                    Throws<SafetyException>(() => new WindowsManifestTrust(trust).Verify(other.Body, other.Sign(), f.Publisher(), f.Evidence(), CancellationToken.None)); Check(trust.Calls == 1, "Exact certificate mismatch was not checked after trust");
                }
            });
            Test("Windows manifest refuses incomplete mismatched certificate or changed digest evidence", delegate {
                using (var f = new ManifestSigningFixture()) { foreach (var evidence in new[] { null, FixtureSigner(), new WindowsSignerEvidence(f.Evidence().CertificateHash, f.Evidence().SubjectHash, new string('c', 64), "2.16.840.1.101.3.4.2.2") }) {
                    var trust = new FakeManifestCertificateTrust { Evidence = evidence }; Throws<SafetyException>(() => new WindowsManifestTrust(trust).Verify(f.Body, f.Sign(), f.Publisher(), f.Evidence(), CancellationToken.None)); Check(trust.Calls == 1, "Malformed native evidence did not reach its refusal");
                } }
            });
            Test("Windows manifest cancellation before and during certificate trust returns no approval", delegate {
                using (var f = new ManifestSigningFixture()) { byte[] signature = f.Sign(); using (var cancel = new CancellationTokenSource()) {
                    cancel.Cancel(); var trust = new FakeManifestCertificateTrust { Evidence = f.Evidence() }; Throws<OperationCanceledException>(() => new WindowsManifestTrust(trust).Verify(f.Body, signature, f.Publisher(), f.Evidence(), cancel.Token)); Check(trust.Calls == 0, "Cancelled request reached native trust");
                } using (var cancel = new CancellationTokenSource()) {
                    var trust = new FakeManifestCertificateTrust { Evidence = f.Evidence(), During = cancel.Cancel }; Throws<OperationCanceledException>(() => new WindowsManifestTrust(trust).Verify(f.Body, signature, f.Publisher(), f.Evidence(), cancel.Token)); Check(trust.Calls == 1, "Late cancellation retried or became approval");
                } }
            });
            Test("Windows manifest bounds missing oversized truncated appended and indefinite signatures", delegate {
                using (var f = new ManifestSigningFixture()) { byte[] signature = f.Sign(); var trust = new FakeManifestCertificateTrust { Evidence = f.Evidence() }; var validator = new WindowsManifestTrust(trust);
                    foreach (byte[] body in new[] { null, new byte[0], new byte[WindowsManifestTrust.MaximumManifest + 1] }) Throws<SafetyException>(() => validator.Verify(body, signature, f.Publisher(), f.Evidence(), CancellationToken.None));
                    foreach (byte[] bad in new[] { null, new byte[0], new byte[WindowsManifestTrust.MaximumSignature + 1], signature.Take(signature.Length - 1).ToArray(), signature.Concat(new byte[] { 0 }).ToArray(), new byte[] { 0x30, 0x80, 0, 0 } }) Throws<SafetyException>(() => validator.Verify(f.Body, bad, f.Publisher(), f.Evidence(), CancellationToken.None));
                    Check(trust.Calls == 0, "Unbounded/malformed envelope reached certificate trust");
                }
            });
            Test("Windows manifest freezes caller arrays before native certificate callback", delegate {
                using (var f = new ManifestSigningFixture()) { byte[] body = (byte[])f.Body.Clone(), signature = f.Sign(); var trust = new FakeManifestCertificateTrust { Evidence = f.Evidence(), During = () => { Array.Clear(body, 0, body.Length); Array.Clear(signature, 0, signature.Length); } };
                    var result = new WindowsManifestTrust(trust).Verify(body, signature, f.Publisher(), f.Evidence(), CancellationToken.None);
                    Check(result.Signer.CertificateHash == f.Evidence().CertificateHash && result.BodyHash == WindowsSignerEvidence.Hash(f.Body) && result.CopyBytes().SequenceEqual(f.Body) && body.All(b => b == 0), "Caller mutation retargeted authenticated snapshot");
                    byte[] exposed = result.CopyBytes(); Array.Clear(exposed, 0, exposed.Length); Check(result.CopyBytes().SequenceEqual(f.Body), "Returned bytes can mutate the authenticated snapshot");
                }
            });
            Test("Windows manifest native certificate API holds usage strong policy chain and memory store through verification", delegate {
                using (var f = new ManifestSigningFixture()) { var api = new FakeWindowsChainApi(); var result = new WindowsCertificateTrust(api).Verify(f.Certificate, new X509Certificate2Collection(f.Certificate), FixtureSha256Oid);
                    Check(result.CertificateHash == f.Evidence().CertificateHash && api.Order.SequenceEqual(new[] { "Open", "Add", "Build", "Policy", "Read", "Free", "Close" }), "Native certificate lifecycle/policy/order changed");
                }
            });
            Test("Windows manifest native certificate failure stages release all owned resources without retry", delegate {
                using (var f = new ManifestSigningFixture()) foreach (string stage in new[] { "Open", "Add", "Build", "Policy", "Read", "Free", "Close" }) {
                    var api = new FakeWindowsChainApi { Failure = stage }; Throws<InvalidOperationException>(() => new WindowsCertificateTrust(api).Verify(f.Certificate, new X509Certificate2Collection(f.Certificate), FixtureSha256Oid));
                    Check(api.OpenCalls == 1 && api.Closes == (stage == "Open" ? 0 : 1) && api.Frees == (new[] { "Policy", "Read", "Free", "Close" }.Contains(stage) ? 1 : 0), "Failure leaked chain/store or retried: " + stage);
                }
            });
            Test("Windows manifest native certificate policy errors never read signer and always release chain", delegate {
                using (var f = new ManifestSigningFixture()) foreach (uint error in new[] { 1U, 0x800B0109U, 0x800B0101U, 0x80092013U, 0x800B0110U, 0x80092010U }) {
                    var api = new FakeWindowsChainApi { PolicyError = error }; Check(Throws<SafetyException>(() => new WindowsCertificateTrust(api).Verify(f.Certificate, new X509Certificate2Collection(f.Certificate), FixtureSha256Oid)).Message.Contains(error.ToString("X8")), "Native policy refusal lost exact error"); Check(api.Reads == 0 && api.Frees == 1 && api.Closes == 1, "Failed trust read signer or leaked native resources");
                }
            });
            Test("Windows manifest native certificate missing handles and signer mismatches refuse", delegate {
                using (var f = new ManifestSigningFixture()) foreach (var api in new[] { new FakeWindowsChainApi { MissingStore = true }, new FakeWindowsChainApi { MissingChain = true }, new FakeWindowsChainApi { OverrideEvidence = FixtureSigner() } }) {
                    Throws<SafetyException>(() => new WindowsCertificateTrust(api).Verify(f.Certificate, new X509Certificate2Collection(f.Certificate), FixtureSha256Oid)); Check(api.OpenCalls == 1 && api.Closes == (api.MissingStore ? 0 : 1) && api.Frees == (api.MissingStore || api.MissingChain ? 0 : 1), "Missing/mismatched proof leaked resources");
                }
            });
            Test("Windows manifest native certificate structures match x64 layout", delegate {
                Check(Marshal.SizeOf(typeof(WindowsCertificateTrust.ChainParameters)) == 96 && Marshal.OffsetOf(typeof(WindowsCertificateTrust.ChainParameters), "Usage").ToInt32() == 8 && Marshal.OffsetOf(typeof(WindowsCertificateTrust.ChainParameters), "Strong").ToInt32() == 80 && Marshal.SizeOf(typeof(WindowsCertificateTrust.UsageMatch)) == 24, "Wrong CERT_CHAIN_PARA layout");
                Check(Marshal.SizeOf(typeof(WindowsCertificateTrust.PolicyParameters)) == 16 && Marshal.SizeOf(typeof(WindowsCertificateTrust.PolicyStatus)) == 24 && Marshal.OffsetOf(typeof(WindowsCertificateTrust.ChainHeader), "Chains").ToInt32() == 16 && Marshal.OffsetOf(typeof(WindowsCertificateTrust.ElementHeader), "Certificate").ToInt32() == 8 && Marshal.OffsetOf(typeof(WindowsCertificateTrust.ElementHeader), "Error").ToInt32() == 16, "Wrong native chain/policy layout");
            });
            Test("Windows real native manifest trust refuses untrusted ephemeral self-signed certificate after crypto verifies", delegate {
                using (var f = new ManifestSigningFixture()) { byte[] signature = f.Sign(); var cms = new SignedCms(new ContentInfo(f.Body), true); cms.Decode(signature); cms.CheckSignature(true);
                    var observed = new ObservedWindowsChainApi(); var refusal = Throws<SafetyException>(() => new WindowsManifestTrust(new WindowsCertificateTrust(observed)).Verify(f.Body, signature, f.Publisher(), f.Evidence(), CancellationToken.None));
                    Check(refusal.Message.Contains("Windows manifest signing trust could not be established") && refusal.Message.Contains("0x800B0109") && (observed.Errors & 0x20) != 0 && observed.Frees == 1 && observed.Closes == 1, "Native chain did not return untrusted-root refusal and release resources: " + refusal.Message);
                }
            });
            Test("Windows expired manifest certificate still requires current-time native trust", delegate {
                using (var f = new ManifestSigningFixture(true)) { byte[] signature = f.Sign(); var observed = new ObservedWindowsChainApi(); var refusal = Throws<SafetyException>(() => new WindowsManifestTrust(new WindowsCertificateTrust(observed)).Verify(f.Body, signature, f.Publisher(), f.Evidence(), CancellationToken.None));
                    Check(refusal.Message.Contains("Windows manifest signing trust could not be established") && (observed.Errors & 1) != 0 && observed.Frees == 1 && observed.Closes == 1, "Expired certificate escaped current-time native chain validity and cleanup: " + refusal.Message);
                }
            });
        }
    }
}
