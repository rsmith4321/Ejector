using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace EasyEject {
    // These primitives do not configure a release channel or authorize installation.
    // Publisher approval is supplied by code, never inferred from an inspected file.
    internal sealed class WindowsSignerEvidence {
        internal readonly string CertificateHash, SubjectHash, IssuerCertificateHash, DigestOid;
        internal WindowsSignerEvidence(string certificate, string subject, string issuer, string digest) {
            if (!ImportHistory.ValidHash(certificate) || !ImportHistory.ValidHash(subject) || !ImportHistory.ValidHash(issuer) || !StrongDigest(digest))
                throw new SafetyException("Windows signer evidence is incomplete or uses an unsupported digest.");
            CertificateHash = certificate; SubjectHash = subject; IssuerCertificateHash = issuer; DigestOid = digest;
        }
        internal static bool StrongDigest(string oid) { return oid == "2.16.840.1.101.3.4.2.1" || oid == "2.16.840.1.101.3.4.2.2" || oid == "2.16.840.1.101.3.4.2.3"; }
        internal static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
    internal sealed class ApprovedWindowsPublisher {
        internal readonly string SubjectHash, IssuerCertificateHash;
        internal ApprovedWindowsPublisher(string subject, string issuer) {
            if (!ImportHistory.ValidHash(subject) || !ImportHistory.ValidHash(issuer)) throw new ArgumentException("Publisher approval needs exact SHA-256 subject and issuing-certificate bindings.");
            SubjectHash = subject; IssuerCertificateHash = issuer;
        }
        internal void Require(WindowsSignerEvidence evidence) {
            if (evidence == null || evidence.SubjectHash != SubjectHash || evidence.IssuerCertificateHash != IssuerCertificateHash)
                throw new SafetyException("The verified Windows signer is not the approved Easy Eject publisher.");
        }
    }
    internal interface IWindowsExecutableTrust {
        WindowsSignerEvidence Verify(string path, FileStream held);
    }
    internal sealed class WindowsSignedFileReview : IDisposable {
        const long MaximumExecutableBytes = 1024L * 1024 * 1024;
        readonly SafePaths.DirectoryLease directories; readonly FileStream held; readonly Native.FileInfo identity;
        readonly ApprovedWindowsPublisher approved; readonly IWindowsExecutableTrust trust;
        internal readonly string Path, Hash; internal readonly WindowsSignerEvidence Signer;
        bool disposed;
        internal WindowsSignedFileReview(string input, ApprovedWindowsPublisher approved, IWindowsExecutableTrust trust, CancellationToken cancel) {
            if (approved == null) throw new SafetyException("No approved Windows publisher is configured. Signature review cannot approve a release.");
            if (trust == null) throw new ArgumentNullException("trust");
            this.approved = approved; this.trust = trust;
            cancel.ThrowIfCancellationRequested(); Path = SafePaths.Normalize(input);
            try {
                directories = SafePaths.HoldDirectories(System.IO.Path.GetDirectoryName(Path));
                held = SafePaths.ReadFile(Path); identity = Native.Info(held.SafeFileHandle); Validate();
                if (held.Length == 0 || held.Length > MaximumExecutableBytes) throw new SafetyException("The Windows candidate size is unsupported.");
                Hash = VerifiedImporter.Hash(held, cancel); held.Position = 0;
                Signer = trust.Verify(Path, held); cancel.ThrowIfCancellationRequested(); Validate(); approved.Require(Signer);
                held.Position = 0;
                if (VerifiedImporter.Hash(held, cancel) != Hash) throw new SafetyException("The held Windows candidate changed during signature review.");
                cancel.ThrowIfCancellationRequested(); Validate(); held.Position = 0;
            } catch { Dispose(); throw; }
        }
        internal void Validate() {
            if (disposed) throw new ObjectDisposedException("WindowsSignedFileReview");
            directories.Validate(); var current = Native.Info(held.SafeFileHandle);
            if (!VerifiedImporter.SameStamp(identity, current) || current.Links != 1 || (current.Attributes & 0x1010) != 0 || !Native.FinalPath(held.SafeFileHandle).Equals(Path, StringComparison.OrdinalIgnoreCase))
                throw new SafetyException("The reviewed Windows candidate is aliased, offline, moved or changed. It is not approved.");
        }
        internal void Inspect(Action<FileStream> inspect) {
            Validate(); long position = held.Position;
            try { inspect(held); Validate(); } finally { if (held.CanSeek) held.Position = position; }
        }
        internal void Revalidate(CancellationToken cancel, bool refreshTrust) {
            Validate(); cancel.ThrowIfCancellationRequested(); long position = held.Position;
            try {
                held.Position = 0; if (VerifiedImporter.Hash(held, cancel) != Hash) throw new SafetyException("The reviewed Windows executable's complete hash changed.");
                Validate();
                if (refreshTrust) {
                    var fresh = trust.Verify(Path, held); cancel.ThrowIfCancellationRequested(); approved.Require(fresh);
                    if (fresh.CertificateHash != Signer.CertificateHash || fresh.DigestOid != Signer.DigestOid) throw new SafetyException("The reviewed Windows executable's verified signature changed.");
                    held.Position = 0; if (VerifiedImporter.Hash(held, cancel) != Hash) throw new SafetyException("The Windows executable changed during fresh signature verification.");
                }
                cancel.ThrowIfCancellationRequested(); Validate();
            } finally { if (held.CanSeek) held.Position = position; }
        }
        public void Dispose() { if (disposed) return; disposed = true; if (held != null) held.Dispose(); if (directories != null) directories.Dispose(); }
    }
    internal sealed class WindowsAuthenticode : IWindowsExecutableTrust {
        internal static readonly Guid VerifyAction = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        internal const uint ProviderFlags = 0x1000 | 0x2000 | 0x80; // cache only, no MD2/MD4, chain revocation excluding root
        internal const string SignatureAlgorithms = "RSA/SHA256;RSA/SHA384;RSA/SHA512;ECDSA/SHA256;ECDSA/SHA384;ECDSA/SHA512";
        internal const string MinimumKeys = "RSA/2048;ECDSA/256";
        readonly IAuthenticodeApi api;
        internal WindowsAuthenticode() : this(new NativeAuthenticodeApi()) { }
        internal WindowsAuthenticode(IAuthenticodeApi api) { if (api == null) throw new ArgumentNullException("api"); this.api = api; }
        public WindowsSignerEvidence Verify(string path, FileStream held) {
            if (held == null || !held.CanRead || !held.CanSeek) throw new ArgumentException("Signature verification needs the live held candidate.");
            string canonical = SafePaths.Normalize(path); var initial = Native.Info(held.SafeFileHandle);
            RequireFile(canonical, held, initial);
            bool added = false, attempted = false; var data = new TrustData();
            IntPtr pathPtr = IntPtr.Zero, filePtr = IntPtr.Zero, algorithms = IntPtr.Zero, keys = IntPtr.Zero, serialized = IntPtr.Zero, strong = IntPtr.Zero, signatures = IntPtr.Zero;
            long position = held.Position;
            try {
                held.SafeFileHandle.DangerousAddRef(ref added);
                pathPtr = Marshal.StringToHGlobalUni(canonical);
                filePtr = Allocate(new TrustFile { Size = (uint)Marshal.SizeOf(typeof(TrustFile)), Path = pathPtr, Handle = held.SafeFileHandle.DangerousGetHandle() });
                algorithms = Marshal.StringToHGlobalUni(SignatureAlgorithms); keys = Marshal.StringToHGlobalUni(MinimumKeys);
                serialized = Allocate(new StrongSerialized { Flags = 3, Algorithms = algorithms, Keys = keys });
                strong = Allocate(new StrongPolicy { Size = (uint)Marshal.SizeOf(typeof(StrongPolicy)), Choice = 1, Info = serialized });
                signatures = Allocate(new SignatureSettings { Size = (uint)Marshal.SizeOf(typeof(SignatureSettings)), Index = 0, Flags = 1, Policy = strong });
                data = new TrustData { Size = (uint)Marshal.SizeOf(typeof(TrustData)), Ui = 2, Revocation = 1, Choice = 1, File = filePtr, StateAction = 1, Flags = ProviderFlags, SignatureSettings = signatures };
                attempted = true; int status = api.Verify(VerifyAction, ref data);
                // WinVerifyTrust returns LONG, not HRESULT: only exact zero is success.
                if (status != 0) throw new SafetyException("Windows signature trust could not be established from cached evidence (0x" + unchecked((uint)status).ToString("X8") + "). No release is approved.");
                var selected = (SignatureSettings)Marshal.PtrToStructure(signatures, typeof(SignatureSettings));
                if (selected.VerifiedIndex != 0 || data.State == IntPtr.Zero) throw new SafetyException("Windows returned incomplete or unexpected signature verification state.");
                WindowsSignerEvidence evidence = api.ReadSigner(data.State);
                if (evidence == null) throw new SafetyException("Windows returned no verified signer evidence.");
                RequireFile(canonical, held, initial); return evidence;
            } finally {
                try { if (attempted) { data.StateAction = 2; int closed = api.Verify(VerifyAction, ref data); if (closed != 0) throw new SafetyException("Windows could not release signature verification state. No release is approved."); } }
                finally {
                    foreach (IntPtr allocation in new[] { signatures, strong, serialized, keys, algorithms, filePtr, pathPtr }) if (allocation != IntPtr.Zero) Marshal.FreeHGlobal(allocation);
                    if (added) held.SafeFileHandle.DangerousRelease();
                    if (held.CanSeek) held.Position = position;
                }
            }
        }
        static void RequireFile(string path, FileStream held, Native.FileInfo initial) {
            var current = Native.Info(held.SafeFileHandle);
            if (current.Links != 1 || (current.Attributes & 0x1010) != 0 || !VerifiedImporter.SameStamp(initial, current) || !Native.FinalPath(held.SafeFileHandle).Equals(path, StringComparison.OrdinalIgnoreCase))
                throw new SafetyException("Signature verification requires the exact regular single-link local candidate.");
        }
        static IntPtr Allocate<T>(T value) { IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(T))); try { Marshal.StructureToPtr(value, pointer, false); return pointer; } catch { Marshal.FreeHGlobal(pointer); throw; } }
        [StructLayout(LayoutKind.Sequential)] internal struct TrustFile { internal uint Size; internal IntPtr Path, Handle, Subject; }
        [StructLayout(LayoutKind.Sequential)] internal struct TrustData {
            internal uint Size; internal IntPtr PolicyCallback, Sip; internal uint Ui, Revocation, Choice; internal IntPtr File;
            internal uint StateAction; internal IntPtr State, Url; internal uint Flags, Context; internal IntPtr SignatureSettings;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct SignatureSettings { internal uint Size, Index, Flags, SecondaryCount, VerifiedIndex; internal IntPtr Policy; }
        [StructLayout(LayoutKind.Sequential)] internal struct StrongPolicy { internal uint Size, Choice; internal IntPtr Info; }
        [StructLayout(LayoutKind.Sequential)] internal struct StrongSerialized { internal uint Flags; internal IntPtr Algorithms, Keys; }
        [StructLayout(LayoutKind.Sequential)] internal struct ProviderSigner {
            internal uint Size; internal System.Runtime.InteropServices.ComTypes.FILETIME AsOf; internal uint Count; internal IntPtr Certificates;
            internal uint Type; internal IntPtr Signer; internal uint Error, CounterCount; internal IntPtr Counters, Chain;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct CertificateHeader { internal uint Size; internal IntPtr Certificate; }
        [StructLayout(LayoutKind.Sequential)] internal struct Blob { internal uint Size; internal IntPtr Data; }
        [StructLayout(LayoutKind.Sequential)] internal struct Algorithm { internal IntPtr Oid; internal Blob Parameters; }
        [StructLayout(LayoutKind.Sequential)] internal struct SignerHeader { internal uint Version; internal Blob Issuer, Serial; internal Algorithm Hash; }
    }
    internal interface IAuthenticodeApi {
        int Verify(Guid action, ref WindowsAuthenticode.TrustData data);
        WindowsSignerEvidence ReadSigner(IntPtr state);
    }
    internal sealed class NativeAuthenticodeApi : IAuthenticodeApi {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("wintrust.dll", ExactSpelling = true)] static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WindowsAuthenticode.TrustData data);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("wintrust.dll", ExactSpelling = true)] static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("wintrust.dll", ExactSpelling = true)] static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr data, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counter, uint counterIndex);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("wintrust.dll", ExactSpelling = true)] static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificate);
        public int Verify(Guid action, ref WindowsAuthenticode.TrustData data) { return WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
        public WindowsSignerEvidence ReadSigner(IntPtr state) {
            if (state == IntPtr.Zero) throw new SafetyException("Missing native Windows signature state.");
            IntPtr provider = WTHelperProvDataFromStateData(state); if (provider == IntPtr.Zero) throw new SafetyException("Missing native Windows trust provider.");
            IntPtr signer = WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            if (signer == IntPtr.Zero || Marshal.ReadInt32(signer) < Marshal.SizeOf(typeof(WindowsAuthenticode.ProviderSigner))) throw new SafetyException("Incomplete verified Windows signer.");
            var info = (WindowsAuthenticode.ProviderSigner)Marshal.PtrToStructure(signer, typeof(WindowsAuthenticode.ProviderSigner));
            if (info.Error != 0 || info.Type != 0 || info.Count < 2 || info.Signer == IntPtr.Zero) throw new SafetyException("Incomplete or failed Windows signing chain.");
            var header = (WindowsAuthenticode.SignerHeader)Marshal.PtrToStructure(info.Signer, typeof(WindowsAuthenticode.SignerHeader));
            string digest = header.Hash.Oid == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(header.Hash.Oid);
            if (!WindowsSignerEvidence.StrongDigest(digest)) throw new SafetyException("The verified Windows signer uses an unsupported digest.");
            using (var leaf = Certificate(signer, 0)) using (var issuer = Certificate(signer, 1))
                return new WindowsSignerEvidence(WindowsSignerEvidence.Hash(leaf.RawData), WindowsSignerEvidence.Hash(leaf.SubjectName.RawData), WindowsSignerEvidence.Hash(issuer.RawData), digest);
        }
        static X509Certificate2 Certificate(IntPtr signer, uint index) {
            IntPtr pointer = WTHelperGetProvCertFromChain(signer, index);
            if (pointer == IntPtr.Zero || Marshal.ReadInt32(pointer) < Marshal.SizeOf(typeof(WindowsAuthenticode.CertificateHeader))) throw new SafetyException("Missing verified signing certificate.");
            var info = (WindowsAuthenticode.CertificateHeader)Marshal.PtrToStructure(pointer, typeof(WindowsAuthenticode.CertificateHeader));
            if (info.Certificate == IntPtr.Zero) throw new SafetyException("Missing verified signing certificate context.");
            // This constructor duplicates the public certificate context before trust-state close.
            return new X509Certificate2(info.Certificate);
        }
    }
}
