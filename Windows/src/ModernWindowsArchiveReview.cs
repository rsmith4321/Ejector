using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace EasyEject {
    // Read-only archive contents. This neither extracts nor grants execution,
    // component/publisher approval, channel freshness or installation authority.
    internal sealed class ModernWindowsArchiveReview : IDisposable {
        readonly SafePaths.DirectoryLease directories; readonly FileStream held; readonly Native.FileInfo identity;
        readonly ApprovedWindowsPublisher publisher; readonly IWindowsManifestEnvelopeTrust trust;
        readonly byte[] manifest, signature, index; readonly string manifestHash, signatureHash;
        internal readonly string Path, Hash; internal readonly long Length;
        internal readonly ModernWindowsReleaseManifest Release; internal readonly WindowsAuthenticatedManifest Authenticated;
        bool disposed;
        internal ModernWindowsArchiveReview(string path, ApprovedWindowsPublisher publisher, IWindowsManifestEnvelopeTrust trust, CancellationToken cancel) {
            if (publisher == null) throw new SafetyException("No independently approved modern Windows publisher is configured.");
            if (trust == null) throw new ArgumentNullException("trust"); cancel.ThrowIfCancellationRequested();
            this.publisher = publisher; this.trust = trust; Path = SafePaths.Normalize(path);
            try {
                directories = SafePaths.HoldDirectories(System.IO.Path.GetDirectoryName(Path)); held = SafePaths.ReadFile(Path);
                identity = Native.Info(held.SafeFileHandle); Length = held.Length;
                if (Length < 22 || Length > WindowsArchiveReview.MaximumArchive) throw new SafetyException("Modern Windows ZIP archive length is unsupported.");
                Validate(); held.Position = 0; Hash = VerifiedImporter.Hash(held, cancel);
                var entries = WindowsZipLayout.ReadModern(held, cancel);
                foreach (var entry in entries) {
                    int slash = entry.Name.IndexOf('/');
                    while (slash >= 0) {
                        string parent = entry.Name.Substring(0, slash);
                        if (entries.Any(other => other.Name.Equals(parent, StringComparison.OrdinalIgnoreCase))) throw new SafetyException("Modern ZIP file and directory identities conflict.");
                        slash = entry.Name.IndexOf('/', slash + 1);
                    }
                }
                foreach (string name in new[] { ModernWindowsReleaseManifest.ManifestName, ModernWindowsReleaseManifest.SignatureName, ModernWindowsReleaseManifest.IndexName })
                    if (!entries.Any(entry => entry.Name == name)) throw new SafetyException("Modern ZIP required metadata is missing or differently named.");
                manifest = ReadEntry(entries.Single(entry => entry.Name == ModernWindowsReleaseManifest.ManifestName), null, true, cancel);
                signature = ReadEntry(entries.Single(entry => entry.Name == ModernWindowsReleaseManifest.SignatureName), null, true, cancel);
                index = ReadEntry(entries.Single(entry => entry.Name == ModernWindowsReleaseManifest.IndexName), null, true, cancel);
                manifestHash = WindowsSignerEvidence.Hash(manifest); signatureHash = WindowsSignerEvidence.Hash(signature);
                Authenticated = Authenticate(cancel); Release = ModernWindowsReleaseManifest.Parse(Authenticated, index);
                var expected = Release.Files.Select(file => file.Name).Concat(new[] { ModernWindowsReleaseManifest.ManifestName, ModernWindowsReleaseManifest.SignatureName, ModernWindowsReleaseManifest.IndexName });
                if (!entries.Select(entry => entry.Name).OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(expected.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal))
                    throw new SafetyException("Modern ZIP contents do not match their authenticated exact inventory.");
                foreach (var file in Release.Files) {
                    var entry = entries.Single(value => value.Name == file.Name);
                    if (entry.Length != file.Length) throw new SafetyException("Modern ZIP payload length differs from its authenticated inventory: " + file.Name);
                    ReadEntry(entry, file.Hash, false, cancel);
                }
                CheckHash(cancel);
            } catch { Dispose(); throw; }
        }
        byte[] ReadEntry(WindowsZipEntry entry, string expectedHash, bool capture, CancellationToken cancel) {
            Validate(); using (var range = new WindowsZipRange(held, entry.Data, entry.Compressed)) {
                Stream input = range; DeflateStream inflate = null; MemoryStream small = null;
                try {
                    if (entry.Method == 8) input = inflate = new DeflateStream(range, CompressionMode.Decompress, true);
                    if (capture) small = new MemoryStream();
                    using (var sha = SHA256.Create()) {
                        byte[] buffer = new byte[32768]; long length = 0; uint crc = UInt32.MaxValue; int count;
                        while (true) {
                            cancel.ThrowIfCancellationRequested(); count = input.Read(buffer, 0, buffer.Length); if (count == 0) break;
                            length += count; if (length > entry.Length) throw new SafetyException("Modern ZIP entry expands beyond its bounded declared length: " + entry.Name);
                            sha.TransformBlock(buffer, 0, count, buffer, 0); crc = WindowsZipCrc.Add(crc, buffer, count); if (small != null) small.Write(buffer, 0, count);
                        }
                        sha.TransformFinalBlock(new byte[0], 0, 0); string hash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                        if (length != entry.Length || ~crc != entry.Crc || expectedHash != null && hash != expectedHash) throw new SafetyException("Modern ZIP entry has mismatched length, CRC or authenticated SHA-256: " + entry.Name);
                    }
                    cancel.ThrowIfCancellationRequested(); Validate(); return small == null ? null : small.ToArray();
                } catch (InvalidDataException ex) { throw new SafetyException("Modern ZIP compressed entry is invalid: " + ex.Message); }
                finally { if (inflate != null) inflate.Dispose(); if (small != null) small.Dispose(); }
            }
        }
        WindowsAuthenticatedManifest Authenticate(CancellationToken cancel) {
            var result = trust.VerifyEnvelope((byte[])manifest.Clone(), (byte[])signature.Clone(), publisher, cancel); cancel.ThrowIfCancellationRequested();
            if (result == null || result.BodyHash != manifestHash || result.SignatureHash != signatureHash) throw new SafetyException("Modern archive manifest authentication returned a different snapshot.");
            publisher.Require(result.Signer); return result;
        }
        void Validate() {
            if (disposed) throw new ObjectDisposedException("ModernWindowsArchiveReview"); directories.Validate(); var current = Native.Info(held.SafeFileHandle);
            if (!VerifiedImporter.SameStamp(identity, current) || current.Links != 1 || (current.Attributes & 0x1410) != 0 || !Native.FinalPath(held.SafeFileHandle).Equals(Path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Modern Windows archive is aliased, redirected, moved, offline or changed.");
            WindowsPackageStreams.Require(Path, Length); directories.Validate();
        }
        void CheckHash(CancellationToken cancel) {
            Validate(); cancel.ThrowIfCancellationRequested(); held.Position = 0;
            if (VerifiedImporter.Hash(held, cancel) != Hash) throw new SafetyException("The whole reviewed modern Windows archive changed."); cancel.ThrowIfCancellationRequested(); Validate();
        }
        internal void Revalidate(CancellationToken cancel) {
            CheckHash(cancel); var fresh = Authenticate(cancel);
            if (fresh.Signer.CertificateHash != Authenticated.Signer.CertificateHash || fresh.Signer.DigestOid != Authenticated.Signer.DigestOid) throw new SafetyException("Modern archive authenticated signer changed.");
            ModernWindowsReleaseManifest.Parse(fresh, index); CheckHash(cancel);
        }
        internal void VerifyExtractedPackage(ModernWindowsApplicationPackageReview package, CancellationToken cancel) {
            if (package == null) throw new ArgumentNullException("package"); Revalidate(cancel); package.Revalidate(cancel);
            var other = package.Release.Authenticated;
            if (other.BodyHash != Authenticated.BodyHash || other.SignatureHash != Authenticated.SignatureHash || other.Signer.CertificateHash != Authenticated.Signer.CertificateHash || package.Release.IndexHash != Release.IndexHash)
                throw new SafetyException("Independently reviewed modern application folder differs from this exact archive's authenticated inventory and signer.");
            CheckHash(cancel); package.Revalidate(cancel);
        }
        public void Dispose() { if (disposed) return; disposed = true; if (held != null) held.Dispose(); if (directories != null) directories.Dispose(); }
    }
}
