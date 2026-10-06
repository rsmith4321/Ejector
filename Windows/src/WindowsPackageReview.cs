using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    // Reviews an already-extracted folder only. No extraction, execution or installation authority.
    internal sealed class WindowsPackageReview : IDisposable {
        sealed class HeldFile : IDisposable {
            internal readonly string Path, Name, Hash; internal readonly long Length; internal readonly FileStream Stream; readonly Native.FileInfo identity;
            internal HeldFile(string root, string name, CancellationToken cancel) {
                Name = name; Path = System.IO.Path.Combine(root, name);
                try { Stream = SafePaths.ReadFile(Path); identity = Native.Info(Stream.SafeFileHandle); Length = Stream.Length; Validate();
                    if (Length <= 0 || Length > WindowsReleaseManifest.MaximumLength(name)) throw new SafetyException("A Windows package file has an unsupported length: " + name);
                    Hash = VerifiedImporter.Hash(Stream, cancel); Validate(); Stream.Position = 0;
                } catch { Dispose(); throw; }
            }
            internal void Validate() { var now = Native.Info(Stream.SafeFileHandle); if (!VerifiedImporter.SameStamp(identity, now) || now.Links != 1 || (now.Attributes & 0x1010) != 0 || !Native.FinalPath(Stream.SafeFileHandle).Equals(Path, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("A Windows package file is aliased, moved, offline or changed: " + Name); WindowsPackageStreams.Require(Path, Length); }
            internal byte[] Bytes() { Validate(); Stream.Position = 0; var bytes = new byte[(int)Length]; int done = 0, count; while (done < bytes.Length && (count = Stream.Read(bytes, done, bytes.Length - done)) > 0) done += count; Stream.Position = 0; if (done != bytes.Length) throw new SafetyException("A Windows package file could not be read completely."); Validate(); return bytes; }
            internal void Revalidate(CancellationToken cancel) { Validate(); Stream.Position = 0; if (VerifiedImporter.Hash(Stream, cancel) != Hash) throw new SafetyException("A reviewed Windows package file's complete hash changed: " + Name); Stream.Position = 0; cancel.ThrowIfCancellationRequested(); Validate(); }
            public void Dispose() { if (Stream != null) Stream.Dispose(); }
        }
        readonly Dictionary<string, HeldFile> files = new Dictionary<string, HeldFile>(StringComparer.Ordinal);
        readonly SafePaths.DirectoryLease directories; readonly WindowsSignedFileReview executable;
        readonly ApprovedWindowsPublisher publisher; readonly IWindowsManifestTrust manifestTrust;
        internal readonly string Folder; internal readonly WindowsReleaseManifest Release; internal readonly WindowsAuthenticatedManifest Authenticated;
        bool disposed;
        internal WindowsPackageReview(string folder, ApprovedWindowsPublisher publisher, IWindowsExecutableTrust executableTrust, IWindowsManifestTrust manifestTrust, CancellationToken cancel) {
            if (publisher == null) throw new SafetyException("No approved Windows publisher is configured. Package review cannot approve a release.");
            if (executableTrust == null || manifestTrust == null) throw new ArgumentNullException("trust");
            this.publisher = publisher; this.manifestTrust = manifestTrust; cancel.ThrowIfCancellationRequested(); Folder = SafePaths.Normalize(folder);
            try {
                directories = SafePaths.HoldDirectories(Folder); CheckNames();
                foreach (string name in WindowsReleaseManifest.PayloadNames.Concat(new[] { WindowsReleaseManifest.ManifestName, WindowsReleaseManifest.SignatureName }).Where(n => n != WindowsReleaseManifest.Executable)) { cancel.ThrowIfCancellationRequested(); files.Add(name, new HeldFile(Folder, name, cancel)); }
                executable = new WindowsSignedFileReview(Path.Combine(Folder, WindowsReleaseManifest.Executable), publisher, executableTrust, cancel);
                Authenticated = Authenticate(cancel); Release = WindowsReleaseManifest.Parse(Authenticated);
                foreach (var entry in Release.Files) {
                    long length = entry.Name == WindowsReleaseManifest.Executable ? new FileInfo(executable.Path).Length : files[entry.Name].Length;
                    string hash = entry.Name == WindowsReleaseManifest.Executable ? executable.Hash : files[entry.Name].Hash;
                    if (length != entry.Length || hash != entry.Hash) throw new SafetyException("A Windows package payload does not match its authenticated length/SHA-256: " + entry.Name);
                }
                WindowsExecutableIdentity.Validate(executable, Release.Version); WindowsExecutableIdentity.ValidateConfiguration(files[WindowsReleaseManifest.Configuration].Bytes());
                WindowsExecutableIdentity.ValidateText(files["README.txt"].Bytes()); WindowsExecutableIdentity.ValidateText(files["LICENSE.txt"].Bytes()); CheckFiles(cancel);
            } catch { Dispose(); throw; }
        }
        WindowsAuthenticatedManifest Authenticate(CancellationToken cancel) {
            var body = files[WindowsReleaseManifest.ManifestName]; var signature = files[WindowsReleaseManifest.SignatureName];
            var result = manifestTrust.Verify(body.Bytes(), signature.Bytes(), publisher, executable.Signer, cancel);
            if (result == null || result.BodyHash != body.Hash || result.SignatureHash != signature.Hash || result.Signer.CertificateHash != executable.Signer.CertificateHash) throw new SafetyException("Windows manifest authentication returned a different package snapshot.");
            publisher.Require(result.Signer); return result;
        }
        void CheckNames() {
            directories.Validate(); WindowsPackageStreams.Require(Folder, -1); var expected = WindowsReleaseManifest.PayloadNames.Concat(new[] { WindowsReleaseManifest.ManifestName, WindowsReleaseManifest.SignatureName }).OrderBy(n => n, StringComparer.Ordinal);
            var actual = Directory.GetFileSystemEntries(Folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal);
            if (!actual.SequenceEqual(expected, StringComparer.Ordinal)) throw new SafetyException("The Windows package has missing, extra or differently named contents."); directories.Validate();
        }
        void CheckFiles(CancellationToken cancel) { if (disposed) throw new ObjectDisposedException("WindowsPackageReview"); CheckNames(); foreach (var file in files.Values) { cancel.ThrowIfCancellationRequested(); file.Revalidate(cancel); } executable.Inspect(s => WindowsPackageStreams.Require(executable.Path, s.Length)); executable.Revalidate(cancel, false); CheckNames(); }
        internal void Revalidate(CancellationToken cancel) {
            CheckFiles(cancel); executable.Revalidate(cancel, true); var fresh = Authenticate(cancel);
            if (fresh.BodyHash != Authenticated.BodyHash || fresh.SignatureHash != Authenticated.SignatureHash) throw new SafetyException("The reviewed Windows release manifest changed."); CheckFiles(cancel);
        }
        public void Dispose() { if (disposed) return; disposed = true; if (executable != null) executable.Dispose(); foreach (var file in files.Values) file.Dispose(); files.Clear(); if (directories != null) directories.Dispose(); }
    }
}
