using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace EasyEject {
    // Read-only ZIP contents review. The whole archive hash is an observed snapshot,
    // not authenticated channel metadata. Executable trust requires extracted review.
    internal sealed class WindowsArchiveReview : IDisposable {
        internal const long MaximumArchive = 2L * 1024 * 1024 * 1024;
        readonly SafePaths.DirectoryLease directories; readonly FileStream held; readonly Native.FileInfo identity;
        readonly ApprovedWindowsPublisher publisher; readonly IWindowsManifestEnvelopeTrust trust;
        readonly byte[] manifest, signature; readonly string manifestHash, signatureHash;
        internal readonly string Path, Hash; internal readonly long Length;
        internal readonly WindowsReleaseManifest Release; internal readonly WindowsAuthenticatedManifest Authenticated;
        bool disposed;
        internal WindowsArchiveReview(string path, ApprovedWindowsPublisher publisher, IWindowsManifestEnvelopeTrust trust, CancellationToken cancel) {
            if (publisher == null) throw new SafetyException("No approved Windows publisher is configured. Archive review cannot authenticate contents.");
            if (trust == null) throw new ArgumentNullException("trust"); this.publisher = publisher; this.trust = trust; cancel.ThrowIfCancellationRequested(); Path = SafePaths.Normalize(path);
            try {
                directories = SafePaths.HoldDirectories(System.IO.Path.GetDirectoryName(Path)); held = SafePaths.ReadFile(Path); identity = Native.Info(held.SafeFileHandle); Length = held.Length;
                if (Length < 22 || Length > MaximumArchive) throw new SafetyException("The Windows ZIP archive length is unsupported."); Validate();
                held.Position = 0; Hash = VerifiedImporter.Hash(held, cancel); var entries = WindowsZipLayout.Read(held, cancel);
                manifest = ReadEntry(entries.Single(e => e.Name == WindowsReleaseManifest.ManifestName), null, cancel);
                signature = ReadEntry(entries.Single(e => e.Name == WindowsReleaseManifest.SignatureName), null, cancel);
                manifestHash = WindowsSignerEvidence.Hash(manifest); signatureHash = WindowsSignerEvidence.Hash(signature);
                Authenticated = Authenticate(cancel); Release = WindowsReleaseManifest.Parse(Authenticated);
                foreach (var file in Release.Files) {
                    var entry = entries.Single(e => e.Name == file.Name); if (entry.Length != file.Length) throw new SafetyException("A Windows ZIP payload length disagrees with its authenticated manifest: " + file.Name);
                    byte[] bytes = ReadEntry(entry, file.Hash, cancel);
                    if (file.Name == WindowsReleaseManifest.Configuration) WindowsExecutableIdentity.ValidateConfiguration(bytes);
                    if (file.Name == "README.txt" || file.Name == "LICENSE.txt") WindowsExecutableIdentity.ValidateText(bytes);
                }
                CheckHash(cancel);
            } catch { Dispose(); throw; }
        }
        byte[] ReadEntry(WindowsZipEntry entry, string expectedHash, CancellationToken cancel) {
            Validate(); using (var range = new WindowsZipRange(held, entry.Data, entry.Compressed)) {
                Stream input = range; DeflateStream inflate = null; MemoryStream small = null;
                try {
                    if (entry.Method == 8) input = inflate = new DeflateStream(range, CompressionMode.Decompress, true);
                    if (entry.Name != WindowsReleaseManifest.Executable) small = new MemoryStream();
                    using (var sha = SHA256.Create()) {
                        byte[] buffer = new byte[32768]; long length = 0; uint crc = UInt32.MaxValue; int count;
                        while (true) {
                            cancel.ThrowIfCancellationRequested(); count = input.Read(buffer, 0, buffer.Length); if (count == 0) break;
                            length += count; if (length > entry.Length) throw new SafetyException("A Windows ZIP entry expands beyond its bounded declared length: " + entry.Name);
                            sha.TransformBlock(buffer, 0, count, buffer, 0); crc = WindowsZipCrc.Add(crc, buffer, count); if (small != null) small.Write(buffer, 0, count);
                        }
                        sha.TransformFinalBlock(new byte[0], 0, 0); string hash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                        if (length != entry.Length || ~crc != entry.Crc || (expectedHash != null && hash != expectedHash)) throw new SafetyException("A Windows ZIP entry has mismatched length, CRC or authenticated SHA-256: " + entry.Name);
                    }
                    cancel.ThrowIfCancellationRequested(); Validate(); return small == null ? null : small.ToArray();
                } catch (InvalidDataException ex) { throw new SafetyException("A Windows ZIP compressed entry is invalid: " + ex.Message); }
                finally { if (inflate != null) inflate.Dispose(); if (small != null) small.Dispose(); }
            }
        }
        WindowsAuthenticatedManifest Authenticate(CancellationToken cancel) {
            var result = trust.VerifyEnvelope((byte[])manifest.Clone(), (byte[])signature.Clone(), publisher, cancel); cancel.ThrowIfCancellationRequested();
            if (result == null || result.BodyHash != manifestHash || result.SignatureHash != signatureHash) throw new SafetyException("Archive manifest authentication returned a different snapshot.");
            publisher.Require(result.Signer); return result;
        }
        void Validate() {
            if (disposed) throw new ObjectDisposedException("WindowsArchiveReview"); directories.Validate(); var current = Native.Info(held.SafeFileHandle);
            if (!VerifiedImporter.SameStamp(identity, current) || current.Links != 1 || (current.Attributes & 0x1010) != 0 || !Native.FinalPath(held.SafeFileHandle).Equals(Path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("The Windows archive is aliased, moved, offline or changed.");
            WindowsPackageStreams.Require(Path, Length); directories.Validate();
        }
        void CheckHash(CancellationToken cancel) { Validate(); cancel.ThrowIfCancellationRequested(); held.Position = 0; if (VerifiedImporter.Hash(held, cancel) != Hash) throw new SafetyException("The whole reviewed Windows archive changed."); cancel.ThrowIfCancellationRequested(); Validate(); }
        internal void Revalidate(CancellationToken cancel) {
            CheckHash(cancel); var fresh = Authenticate(cancel);
            if (fresh.Signer.CertificateHash != Authenticated.Signer.CertificateHash || fresh.Signer.DigestOid != Authenticated.Signer.DigestOid) throw new SafetyException("The archive's authenticated manifest signer changed."); CheckHash(cancel);
        }
        internal void VerifyExtractedPackage(WindowsPackageReview package, CancellationToken cancel) {
            if (package == null) throw new ArgumentNullException("package"); Revalidate(cancel); package.Revalidate(cancel);
            if (package.Authenticated.BodyHash != Authenticated.BodyHash || package.Authenticated.SignatureHash != Authenticated.SignatureHash || package.Authenticated.Signer.CertificateHash != Authenticated.Signer.CertificateHash) throw new SafetyException("The independently verified extracted Windows package does not match this exact archive's authenticated contents and signer.");
            CheckHash(cancel); package.Revalidate(cancel);
        }
        public void Dispose() { if (disposed) return; disposed = true; if (held != null) held.Dispose(); if (directories != null) directories.Dispose(); }
    }
    internal sealed class WindowsZipEntry {
        internal readonly string Name; internal readonly uint Crc, Compressed, Length, Local;
        internal readonly ushort Flags, Method, Version, Time, Date; internal long Data;
        internal WindowsZipEntry(string name, byte[] header) {
            Name = name; Version = U16(header, 6); Flags = U16(header, 8); Method = U16(header, 10); Time = U16(header, 12); Date = U16(header, 14);
            Crc = U32(header, 16); Compressed = U32(header, 20); Length = U32(header, 24); Local = U32(header, 42);
        }
        internal static ushort U16(byte[] bytes, int offset) { return BitConverter.ToUInt16(bytes, offset); }
        internal static uint U32(byte[] bytes, int offset) { return BitConverter.ToUInt32(bytes, offset); }
    }
    internal static class WindowsZipLayout {
        internal static List<WindowsZipEntry> Read(FileStream stream, CancellationToken cancel) {
            return Read(stream, cancel, 6, 6, 64, 1024, StringComparer.Ordinal,
                name => WindowsReleaseManifest.PayloadNames.Concat(new[] { WindowsReleaseManifest.ManifestName, WindowsReleaseManifest.SignatureName }).Contains(name)
                    ? WindowsReleaseManifest.MaximumLength(name) : 0, WindowsArchiveReview.MaximumArchive * 6);
        }
        internal static List<WindowsZipEntry> ReadModern(FileStream stream, CancellationToken cancel) {
            return Read(stream, cancel, ModernWindowsReleaseManifest.RequiredFiles.Count + 3, ModernWindowsReleaseManifest.MaximumFiles + 3,
                200, (ModernWindowsReleaseManifest.MaximumFiles + 3) * 246, StringComparer.OrdinalIgnoreCase,
                name => name == ModernWindowsReleaseManifest.ManifestName ? WindowsManifestTrust.MaximumManifest
                    : name == ModernWindowsReleaseManifest.SignatureName ? WindowsManifestTrust.MaximumSignature
                    : name == ModernWindowsReleaseManifest.IndexName ? ModernWindowsReleaseManifest.MaximumIndex
                    : ModernWindowsReleaseManifest.ValidPath(name) ? ModernWindowsReleaseManifest.MaximumFile : 0,
                ModernWindowsReleaseManifest.MaximumTotal + WindowsManifestTrust.MaximumManifest + WindowsManifestTrust.MaximumSignature + ModernWindowsReleaseManifest.MaximumIndex);
        }
        static List<WindowsZipEntry> Read(FileStream stream, CancellationToken cancel, int minimumCount, int maximumCount,
            int maximumName, int maximumCentral, StringComparer comparer, Func<string, long> maximumLength, long maximumTotal) {
            long end = stream.Length - 22; byte[] tail = ReadAt(stream, end, 22);
            int count = WindowsZipEntry.U16(tail, 10);
            if (WindowsZipEntry.U32(tail, 0) != 0x06054b50 || WindowsZipEntry.U16(tail, 4) != 0 || WindowsZipEntry.U16(tail, 6) != 0 || WindowsZipEntry.U16(tail, 8) != count || count < minimumCount || count > maximumCount || WindowsZipEntry.U16(tail, 20) != 0) Fail();
            long size = WindowsZipEntry.U32(tail, 12), central = WindowsZipEntry.U32(tail, 16), cursor = central;
            if (central == 0 || size < count * 46 || size > maximumCentral || central + size != end) Fail();
            var names = new HashSet<string>(comparer); var entries = new List<WindowsZipEntry>(); long total = 0;
            for (int i = 0; i < count; i++) {
                cancel.ThrowIfCancellationRequested(); byte[] header = ReadAt(stream, cursor, 46);
                if (WindowsZipEntry.U32(header, 0) != 0x02014b50 || WindowsZipEntry.U16(header, 30) != 0 || WindowsZipEntry.U16(header, 32) != 0 || WindowsZipEntry.U16(header, 34) != 0 || WindowsZipEntry.U16(header, 36) != 0) Fail();
                int nameLength = WindowsZipEntry.U16(header, 28); if (nameLength == 0 || nameLength > maximumName || cursor + 46 + nameLength > end) Fail();
                byte[] nameBytes = ReadAt(stream, cursor + 46, nameLength); if (nameBytes.Any(b => b > 127)) Fail(); string name = Encoding.ASCII.GetString(nameBytes);
                long maximum = maximumLength(name); if (maximum == 0 || !names.Add(name)) Fail();
                var entry = new WindowsZipEntry(name, header); uint attributes = WindowsZipEntry.U32(header, 38); int host = header[5];
                if ((host != 0 && host != 3) || (attributes & 0xffff & ~0x21U) != 0) Fail();
                uint mode = attributes >> 16; if (host == 0 && mode != 0 || host == 3 && mode != 0 && ((mode & 0xf000) != 0x8000 || (mode & ~0x81ffU) != 0)) Fail();
                if ((entry.Version != 10 && entry.Version != 20) || (entry.Method != 0 && entry.Method != 8) || (entry.Method == 8 && entry.Version != 20) || (entry.Flags & ~0x80e) != 0 || (entry.Method == 0 && (entry.Flags & 6) != 0)) Fail();
                if (entry.Length == 0 || entry.Length > maximum || entry.Compressed == 0 || entry.Compressed > WindowsArchiveReview.MaximumArchive || (entry.Method == 0 && entry.Compressed != entry.Length)) Fail();
                total += entry.Length; if (total > maximumTotal) Fail();
                RequireDate(entry); entries.Add(entry); cursor += 46 + nameLength;
            }
            if (cursor != end) Fail(); cursor = 0; var ordered = entries.OrderBy(e => e.Local).ToArray();
            for (int i = 0; i < ordered.Length; i++) {
                var entry = ordered[i];
                cancel.ThrowIfCancellationRequested(); if (entry.Local != cursor) Fail(); byte[] header = ReadAt(stream, cursor, 30); byte[] name = Encoding.ASCII.GetBytes(entry.Name);
                if (WindowsZipEntry.U32(header, 0) != 0x04034b50 || WindowsZipEntry.U16(header, 4) != entry.Version || WindowsZipEntry.U16(header, 6) != entry.Flags || WindowsZipEntry.U16(header, 8) != entry.Method || WindowsZipEntry.U16(header, 10) != entry.Time || WindowsZipEntry.U16(header, 12) != entry.Date || WindowsZipEntry.U16(header, 26) != name.Length || WindowsZipEntry.U16(header, 28) != 0 || !ReadAt(stream, cursor + 30, name.Length).SequenceEqual(name)) Fail();
                uint crc = WindowsZipEntry.U32(header, 14), compressed = WindowsZipEntry.U32(header, 18), length = WindowsZipEntry.U32(header, 22);
                if ((entry.Flags & 8) == 0) { if (crc != entry.Crc || compressed != entry.Compressed || length != entry.Length) Fail(); }
                else if ((crc != 0 || compressed != 0 || length != 0) && (crc != entry.Crc || compressed != entry.Compressed || length != entry.Length)) Fail();
                entry.Data = cursor + 30 + name.Length; cursor = entry.Data + entry.Compressed; if (cursor > central) Fail();
                if ((entry.Flags & 8) != 0) {
                    long boundary = i + 1 < ordered.Length ? ordered[i + 1].Local : central, descriptorLength = boundary - cursor;
                    if (descriptorLength != 12 && descriptorLength != 16) Fail(); int prefix = descriptorLength == 16 ? 4 : 0;
                    // The next proved record boundary disambiguates a CRC equal to
                    // the optional descriptor signature, without guessing from CRC bytes.
                    if (prefix != 0 && WindowsZipEntry.U32(ReadAt(stream, cursor, 4), 0) != 0x08074b50) Fail();
                    byte[] descriptor = ReadAt(stream, cursor + prefix, 12);
                    if (WindowsZipEntry.U32(descriptor, 0) != entry.Crc || WindowsZipEntry.U32(descriptor, 4) != entry.Compressed || WindowsZipEntry.U32(descriptor, 8) != entry.Length) Fail(); cursor += 12 + prefix; if (cursor > central) Fail();
                }
            }
            if (cursor != central) Fail(); return entries;
        }
        static void RequireDate(WindowsZipEntry entry) {
            try { new DateTime(1980 + (entry.Date >> 9), (entry.Date >> 5) & 15, entry.Date & 31, entry.Time >> 11, (entry.Time >> 5) & 63, (entry.Time & 31) * 2); }
            catch (ArgumentOutOfRangeException) { Fail(); }
        }
        static byte[] ReadAt(FileStream stream, long offset, int length) {
            if (offset < 0 || length < 0 || offset > stream.Length - length) Fail(); stream.Position = offset; var bytes = new byte[length]; int done = 0, count;
            while (done < length && (count = stream.Read(bytes, done, length - done)) > 0) done += count; if (done != length) Fail(); return bytes;
        }
        static void Fail() { throw new SafetyException("The ZIP is not a supported bounded Windows release archive with consistent regular-file headers."); }
    }
    internal sealed class WindowsZipRange : Stream {
        readonly Stream stream; long remaining;
        internal WindowsZipRange(Stream stream, long start, long length) { this.stream = stream; stream.Position = start; remaining = length; }
        public override int Read(byte[] bytes, int offset, int count) { count = (int)Math.Min(count, remaining); if (count == 0) return 0; int read = stream.Read(bytes, offset, count); if (read == 0) throw new InvalidDataException("Truncated ZIP compressed range."); remaining -= read; return read; }
        public override bool CanRead { get { return true; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); } public override void SetLength(long value) { throw new NotSupportedException(); } public override void Write(byte[] bytes, int offset, int count) { throw new NotSupportedException(); }
    }
    internal static class WindowsZipCrc {
        static readonly uint[] Table = MakeTable();
        static uint[] MakeTable() { var table = new uint[256]; for (uint i = 0; i < 256; i++) { uint value = i; for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320U ^ (value >> 1) : value >> 1; table[i] = value; } return table; }
        internal static uint Add(uint crc, byte[] bytes, int count) { for (int i = 0; i < count; i++) crc = Table[(crc ^ bytes[i]) & 255] ^ (crc >> 8); return crc; }
    }
}
