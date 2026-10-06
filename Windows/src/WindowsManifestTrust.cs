using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace EasyEject {
    internal interface IWindowsManifestTrust {
        WindowsAuthenticatedManifest Verify(byte[] manifest, byte[] signature, ApprovedWindowsPublisher approved, WindowsSignerEvidence executable, CancellationToken cancel);
    }
    internal sealed class WindowsAuthenticatedManifest {
        readonly byte[] body;
        internal readonly string BodyHash, SignatureHash;
        internal readonly WindowsSignerEvidence Signer;
        internal WindowsAuthenticatedManifest(byte[] manifest, byte[] signature, WindowsSignerEvidence signer) {
            if (manifest == null || manifest.Length == 0 || manifest.Length > WindowsManifestTrust.MaximumManifest || signature == null || signature.Length == 0 || signature.Length > WindowsManifestTrust.MaximumSignature || signer == null)
                throw new SafetyException("Incomplete authenticated Windows manifest snapshot.");
            body = (byte[])manifest.Clone(); BodyHash = WindowsSignerEvidence.Hash(body); SignatureHash = WindowsSignerEvidence.Hash(signature); Signer = signer;
        }
        // Parsing/display must use this authenticated snapshot, never the caller's mutable input.
        internal byte[] CopyBytes() { return (byte[])body.Clone(); }
    }
    internal interface IWindowsCertificateTrust {
        WindowsSignerEvidence Verify(X509Certificate2 leaf, X509Certificate2Collection certificates, string digest);
    }
    // Authenticates the exact manifest bytes, not a reserialized interpretation.
    // A valid CMS signature alone does not establish Windows trust or publisher approval.
    internal sealed class WindowsManifestTrust : IWindowsManifestTrust {
        internal const int MaximumManifest = 65536, MaximumSignature = 262144, MaximumCertificates = 12;
        readonly IWindowsCertificateTrust trust;
        internal WindowsManifestTrust() : this(new WindowsCertificateTrust()) { }
        internal WindowsManifestTrust(IWindowsCertificateTrust trust) { if (trust == null) throw new ArgumentNullException("trust"); this.trust = trust; }
        public WindowsAuthenticatedManifest Verify(byte[] manifest, byte[] signature, ApprovedWindowsPublisher approved, WindowsSignerEvidence executable, CancellationToken cancel) {
            if (approved == null) throw new SafetyException("No approved Windows publisher is configured for manifest validation.");
            approved.Require(executable); cancel.ThrowIfCancellationRequested();
            if (manifest == null || manifest.Length == 0 || manifest.Length > MaximumManifest || signature == null || signature.Length == 0 || signature.Length > MaximumSignature)
                throw new SafetyException("Windows manifest/signature bytes are missing or exceed the supported bounds.");
            // Freeze caller-owned arrays before any decoding or trust callback.
            byte[] body = (byte[])manifest.Clone(), encoded = (byte[])signature.Clone();
            RequireDetachedEnvelope(encoded);
            var cms = new SignedCms(new ContentInfo(body), true); X509Certificate2Collection certificates = null;
            try {
                cms.Decode(encoded);
                if (!cms.Detached || cms.ContentInfo.ContentType.Value != "1.2.840.113549.1.7.1" || cms.SignerInfos.Count != 1)
                    throw new SafetyException("The Windows manifest requires one detached data signer.");
                var signer = cms.SignerInfos[0]; string digest = signer.DigestAlgorithm.Value;
                if (!WindowsSignerEvidence.StrongDigest(digest)) throw new SafetyException("The Windows manifest signer uses an unsupported digest.");
                certificates = cms.Certificates;
                if (certificates.Count == 0 || certificates.Count > MaximumCertificates || signer.Certificate == null)
                    throw new SafetyException("The Windows manifest has missing or excessive signing certificates.");
                byte[] leafBytes = signer.Certificate.RawData;
                if (!certificates.Cast<X509Certificate2>().Any(c => c.RawData.SequenceEqual(leafBytes)))
                    throw new SafetyException("The manifest signing certificate is not embedded in its signature.");
                // Crypto-only verification first; the separate native chain check is mandatory.
                cms.CheckSignature(true); cancel.ThrowIfCancellationRequested();
                using (var leaf = new X509Certificate2(leafBytes)) {
                    WindowsSignerEvidence evidence = trust.Verify(leaf, certificates, digest); cancel.ThrowIfCancellationRequested(); approved.Require(evidence);
                    if (evidence.CertificateHash != WindowsSignerEvidence.Hash(leaf.RawData) || evidence.SubjectHash != WindowsSignerEvidence.Hash(leaf.SubjectName.RawData) || evidence.DigestOid != digest || evidence.CertificateHash != executable.CertificateHash)
                        throw new SafetyException("The authenticated manifest and verified executable do not share the exact signing certificate.");
                    return new WindowsAuthenticatedManifest(body, encoded, evidence);
                }
            } catch (CryptographicException ex) { throw new SafetyException("The Windows manifest signature could not be authenticated: " + ex.Message); }
            finally { if (certificates != null) foreach (var certificate in certificates) certificate.Dispose(); }
        }
        // Inspect just the CMS envelope; SignedCms validates the signed data and certificates.
        // The encoded content must be absent, so an embedded body cannot override our exact bytes.
        internal static void RequireDetachedEnvelope(byte[] encoded) {
            var input = new Der(encoded); var outer = input.Read(0x30); input.End();
            outer.Oid(new byte[] { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x07, 0x02 });
            var explicitData = outer.Read(0xa0); outer.End(); var data = explicitData.Read(0x30); explicitData.End();
            data.Read(0x02); data.Read(0x31); var content = data.Read(0x30);
            content.Oid(new byte[] { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x07, 0x01 }); content.End();
        }
        sealed class Der {
            readonly byte[] bytes; readonly int limit; int offset;
            internal Der(byte[] bytes) : this(bytes, 0, bytes == null ? 0 : bytes.Length) { }
            Der(byte[] bytes, int offset, int limit) { this.bytes = bytes; this.offset = offset; this.limit = limit; }
            internal Der Read(byte tag) {
                if (offset + 2 > limit || bytes[offset++] != tag) Fail();
                int length = bytes[offset++];
                if ((length & 0x80) != 0) {
                    int count = length & 0x7f; if (count == 0 || count > 3 || count > limit - offset || bytes[offset] == 0) Fail();
                    length = 0; for (int i = 0; i < count; i++) length = (length << 8) | bytes[offset++];
                    if (length < 128) Fail();
                }
                if (length > limit - offset) Fail(); var child = new Der(bytes, offset, offset + length); offset += length; return child;
            }
            internal void Oid(byte[] expected) { var value = Read(0x06); if (value.limit - value.offset != expected.Length) Fail(); for (int i = 0; i < expected.Length; i++) if (value.bytes[value.offset + i] != expected[i]) Fail(); }
            internal void End() { if (offset != limit) Fail(); }
            static void Fail() { throw new SafetyException("The Windows manifest signature is not a bounded detached CMS data envelope."); }
        }
    }
    internal sealed class WindowsCertificateTrust : IWindowsCertificateTrust {
        internal const uint Flags = 0xc0000104; // chain/revocation cached only, revocation excluding root, no auth-root update
        internal const string CodeSigningOid = "1.3.6.1.5.5.7.3.3";
        readonly IWindowsChainApi api;
        internal WindowsCertificateTrust() : this(new NativeWindowsChainApi()) { }
        internal WindowsCertificateTrust(IWindowsChainApi api) { if (api == null) throw new ArgumentNullException("api"); this.api = api; }
        public WindowsSignerEvidence Verify(X509Certificate2 leaf, X509Certificate2Collection certificates, string digest) {
            if (leaf == null || certificates == null || certificates.Count == 0 || certificates.Count > WindowsManifestTrust.MaximumCertificates || !WindowsSignerEvidence.StrongDigest(digest)) throw new SafetyException("Incomplete Windows manifest certificate proof.");
            IntPtr store = IntPtr.Zero, chain = IntPtr.Zero;
            using (var memory = new TrustMemory()) {
                IntPtr oid = memory.Ansi(CodeSigningOid), usage = memory.AllocatePointer(oid);
                IntPtr serialized = memory.Structure(new WindowsAuthenticode.StrongSerialized { Flags = 3, Algorithms = memory.Unicode(WindowsAuthenticode.SignatureAlgorithms), Keys = memory.Unicode(WindowsAuthenticode.MinimumKeys) });
                IntPtr strong = memory.Structure(new WindowsAuthenticode.StrongPolicy { Size = 16, Choice = 1, Info = serialized });
                var parameters = new ChainParameters { Size = (uint)Marshal.SizeOf(typeof(ChainParameters)), Usage = new UsageMatch { Usage = new EnhancedUsage { Count = 1, Oids = usage } }, Strong = strong };
                try {
                    store = api.OpenMemoryStore(); if (store == IntPtr.Zero) throw new SafetyException("Windows returned no temporary certificate store.");
                    foreach (var certificate in certificates) api.AddCertificate(store, certificate.Handle);
                    chain = api.Build(leaf.Handle, store, ref parameters, Flags); if (chain == IntPtr.Zero) throw new SafetyException("Windows returned no manifest certificate chain.");
                    uint error = api.Policy(chain);
                    if (error != 0) throw new SafetyException("Windows manifest signing trust could not be established from cached evidence (0x" + error.ToString("X8") + "). No release is approved.");
                    WindowsSignerEvidence result = api.ReadSigner(chain, leaf, digest);
                    if (result == null || result.CertificateHash != WindowsSignerEvidence.Hash(leaf.RawData) || result.SubjectHash != WindowsSignerEvidence.Hash(leaf.SubjectName.RawData) || result.DigestOid != digest)
                        throw new SafetyException("Windows returned incomplete or mismatched manifest certificate evidence.");
                    return result;
                } finally { try { if (chain != IntPtr.Zero) api.Free(chain); } finally { if (store != IntPtr.Zero) api.CloseMemoryStore(store); } }
            }
        }
        [StructLayout(LayoutKind.Sequential)] internal struct EnhancedUsage { internal uint Count; internal IntPtr Oids; }
        [StructLayout(LayoutKind.Sequential)] internal struct UsageMatch { internal uint Type; internal EnhancedUsage Usage; }
        [StructLayout(LayoutKind.Sequential)] internal struct ChainParameters {
            internal uint Size; internal UsageMatch Usage, Issuance; internal uint Timeout, FreshnessCheck, Freshness;
            internal IntPtr Resync, Strong; internal uint StrongFlags;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct PolicyParameters { internal uint Size, Flags; internal IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct PolicyStatus { internal uint Size, Error; internal int Chain, Element; internal IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct ChainHeader { internal uint Size, Error, Info, Count; internal IntPtr Chains; }
        [StructLayout(LayoutKind.Sequential)] internal struct SimpleChainHeader { internal uint Size, Error, Info, Count; internal IntPtr Elements; }
        [StructLayout(LayoutKind.Sequential)] internal struct ElementHeader { internal uint Size; internal IntPtr Certificate; internal uint Error, Info; }
        sealed class TrustMemory : IDisposable {
            readonly List<IntPtr> pointers = new List<IntPtr>();
            internal IntPtr Ansi(string value) { IntPtr p = Marshal.StringToHGlobalAnsi(value); pointers.Add(p); return p; }
            internal IntPtr Unicode(string value) { IntPtr p = Marshal.StringToHGlobalUni(value); pointers.Add(p); return p; }
            internal IntPtr AllocatePointer(IntPtr value) { IntPtr p = Marshal.AllocHGlobal(IntPtr.Size); pointers.Add(p); Marshal.WriteIntPtr(p, value); return p; }
            internal IntPtr Structure<T>(T value) { IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(T))); pointers.Add(p); Marshal.StructureToPtr(value, p, false); return p; }
            public void Dispose() { foreach (IntPtr p in pointers.AsEnumerable().Reverse()) Marshal.FreeHGlobal(p); pointers.Clear(); }
        }
    }
    internal interface IWindowsChainApi {
        IntPtr OpenMemoryStore(); void AddCertificate(IntPtr store, IntPtr certificate);
        IntPtr Build(IntPtr leaf, IntPtr store, ref WindowsCertificateTrust.ChainParameters parameters, uint flags);
        uint Policy(IntPtr chain); WindowsSignerEvidence ReadSigner(IntPtr chain, X509Certificate2 leaf, string digest);
        void Free(IntPtr chain); void CloseMemoryStore(IntPtr store);
    }
    internal sealed class NativeWindowsChainApi : IWindowsChainApi {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("crypt32.dll", SetLastError = true, ExactSpelling = true)] static extern IntPtr CertOpenStore(IntPtr provider, uint encoding, IntPtr crypto, uint flags, IntPtr parameter);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("crypt32.dll", SetLastError = true, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool CertAddCertificateContextToStore(IntPtr store, IntPtr certificate, uint disposition, IntPtr output);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("crypt32.dll", SetLastError = true, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool CertGetCertificateChain(IntPtr engine, IntPtr certificate, IntPtr time, IntPtr store, ref WindowsCertificateTrust.ChainParameters parameters, uint flags, IntPtr reserved, out IntPtr chain);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("crypt32.dll", SetLastError = true, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool CertVerifyCertificateChainPolicy(IntPtr policy, IntPtr chain, ref WindowsCertificateTrust.PolicyParameters parameters, ref WindowsCertificateTrust.PolicyStatus status);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("crypt32.dll", ExactSpelling = true)] static extern void CertFreeCertificateChain(IntPtr chain);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("crypt32.dll", SetLastError = true, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool CertCloseStore(IntPtr store, uint flags);
        public IntPtr OpenMemoryStore() { IntPtr store = CertOpenStore(new IntPtr(2), 0, IntPtr.Zero, 0x2000, IntPtr.Zero); if (store == IntPtr.Zero) throw Native.Error("Cannot create in-memory manifest certificate store"); return store; }
        public void AddCertificate(IntPtr store, IntPtr certificate) { if (!CertAddCertificateContextToStore(store, certificate, 4, IntPtr.Zero)) throw Native.Error("Cannot add public manifest certificate to its temporary memory store"); }
        public IntPtr Build(IntPtr leaf, IntPtr store, ref WindowsCertificateTrust.ChainParameters parameters, uint flags) {
            IntPtr chain; if (!CertGetCertificateChain(IntPtr.Zero, leaf, IntPtr.Zero, store, ref parameters, flags, IntPtr.Zero, out chain)) {
                var error = Native.Error("Cannot build current-time cached Windows manifest signing chain"); if (chain != IntPtr.Zero) CertFreeCertificateChain(chain); throw error;
            }
            return chain;
        }
        public uint Policy(IntPtr chain) {
            var parameters = new WindowsCertificateTrust.PolicyParameters { Size = 16 };
            var status = new WindowsCertificateTrust.PolicyStatus { Size = 24 };
            if (!CertVerifyCertificateChainPolicy(new IntPtr(1), chain, ref parameters, ref status)) throw Native.Error("Cannot inspect native Windows manifest chain policy");
            return status.Error;
        }
        public WindowsSignerEvidence ReadSigner(IntPtr chain, X509Certificate2 leaf, string digest) {
            var context = Header<WindowsCertificateTrust.ChainHeader>(chain);
            if (context.Error != 0 || context.Count != 1 || context.Chains == IntPtr.Zero) throw new SafetyException("The manifest signing chain is incomplete, failed or ambiguous.");
            var simple = Header<WindowsCertificateTrust.SimpleChainHeader>(Marshal.ReadIntPtr(context.Chains));
            if (simple.Error != 0 || simple.Count < 2 || simple.Count > 12 || simple.Elements == IntPtr.Zero) throw new SafetyException("The manifest signing chain has no verified issuer.");
            var first = Header<WindowsCertificateTrust.ElementHeader>(Marshal.ReadIntPtr(simple.Elements));
            var second = Header<WindowsCertificateTrust.ElementHeader>(Marshal.ReadIntPtr(simple.Elements, IntPtr.Size));
            if (first.Error != 0 || second.Error != 0 || first.Certificate == IntPtr.Zero || second.Certificate == IntPtr.Zero) throw new SafetyException("The manifest signing certificates have incomplete native proof.");
            using (var verifiedLeaf = new X509Certificate2(first.Certificate)) using (var issuer = new X509Certificate2(second.Certificate)) {
                if (!verifiedLeaf.RawData.SequenceEqual(leaf.RawData)) throw new SafetyException("Windows verified a different manifest signing certificate.");
                return new WindowsSignerEvidence(WindowsSignerEvidence.Hash(verifiedLeaf.RawData), WindowsSignerEvidence.Hash(verifiedLeaf.SubjectName.RawData), WindowsSignerEvidence.Hash(issuer.RawData), digest);
            }
        }
        static T Header<T>(IntPtr pointer) { if (pointer == IntPtr.Zero || Marshal.ReadInt32(pointer) < Marshal.SizeOf(typeof(T))) throw new SafetyException("Windows returned incomplete manifest chain state."); return (T)Marshal.PtrToStructure(pointer, typeof(T)); }
        public void Free(IntPtr chain) { CertFreeCertificateChain(chain); }
        public void CloseMemoryStore(IntPtr store) { if (!CertCloseStore(store, 0)) throw Native.Error("Cannot release temporary manifest certificate store"); }
    }
}
