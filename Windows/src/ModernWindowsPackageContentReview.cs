using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    // Read-only content/ownership layer. Actual app/PE/runtime identity and modern
    // archive/channel approval are additional mandatory layers, not granted here.
    internal sealed class ModernWindowsPackageContentReview : IDisposable {
        sealed class HeldFile : IDisposable {
            internal readonly string Path, Name, Hash; internal readonly long Length;
            internal readonly FileStream Stream; readonly Native.FileInfo identity;
            internal HeldFile(string folder, string name, long maximum, CancellationToken cancel) {
                Name = name; Path = SafePaths.Normalize(System.IO.Path.Combine(folder, name.Replace('/', '\\')));
                try {
                    Stream = SafePaths.ReadFile(Path, 65536); identity = Native.Info(Stream.SafeFileHandle); Length = Stream.Length;
                    if (Length <= 0 || Length > maximum) throw new SafetyException("A modern package file has an unsupported size: " + name);
                    Validate(); Hash = VerifiedImporter.Hash(Stream, cancel); Stream.Position = 0; Validate();
                } catch { Dispose(); throw; }
            }
            internal void Validate() {
                var now = Native.Info(Stream.SafeFileHandle);
                if (!VerifiedImporter.SameStamp(identity, now) || now.Links != 1 || (now.Attributes & 0x1410) != 0 || !Native.FinalPath(Stream.SafeFileHandle).Equals(Path, StringComparison.OrdinalIgnoreCase))
                    throw new SafetyException("A modern package file is aliased, redirected, moved, offline or changed: " + Name);
                WindowsPackageStreams.Require(Path, Length);
            }
            internal void Revalidate(CancellationToken cancel) {
                cancel.ThrowIfCancellationRequested(); Validate(); Stream.Position = 0;
                try { if (VerifiedImporter.Hash(Stream, cancel) != Hash) throw new SafetyException("A modern package file's complete hash changed: " + Name); Validate(); cancel.ThrowIfCancellationRequested(); }
                finally { if (Stream.CanSeek) Stream.Position = 0; }
            }
            internal byte[] Bytes() {
                Validate(); Stream.Position = 0; byte[] bytes = new byte[checked((int)Length)]; int done = 0, count;
                try {
                    while (done < bytes.Length && (count = Stream.Read(bytes, done, bytes.Length - done)) > 0) done += count;
                    if (done != bytes.Length) throw new SafetyException("Modern package metadata could not be read completely."); Validate(); return bytes;
                } finally { if (Stream.CanSeek) Stream.Position = 0; }
            }
            public void Dispose() { if (Stream != null) Stream.Dispose(); }
        }
        readonly Dictionary<string, HeldFile> files = new Dictionary<string, HeldFile>(StringComparer.Ordinal);
        readonly List<string> folderNames = new List<string>();
        SafePaths.DirectoryLease directories; WindowsSignedFileReview executable;
        readonly ApprovedWindowsPublisher publisher; readonly IWindowsManifestTrust manifestTrust;
        internal readonly string Folder; internal readonly ModernWindowsReleaseManifest Release;
        internal readonly WindowsAuthenticatedManifest Authenticated;
        bool disposed;
        internal ModernWindowsPackageContentReview(string folder, ApprovedWindowsPublisher publisher, IWindowsExecutableTrust executableTrust, IWindowsManifestTrust manifestTrust, CancellationToken cancel) {
            if (publisher == null) throw new SafetyException("No independently approved modern Windows publisher is configured.");
            if (executableTrust == null || manifestTrust == null) throw new ArgumentNullException("trust");
            cancel.ThrowIfCancellationRequested(); this.publisher = publisher; this.manifestTrust = manifestTrust; Folder = SafePaths.Normalize(folder);
            try {
                CaptureTree(cancel); CheckLayout(cancel);
                executable = new WindowsSignedFileReview(PathFor("EasyEject.Modern.exe"), publisher, executableTrust, cancel);
                CheckFiles(cancel);
                Authenticated = Authenticate(cancel);
                Release = ModernWindowsReleaseManifest.Parse(Authenticated, files[ModernWindowsReleaseManifest.IndexName].Bytes());
                foreach (var entry in Release.Files) {
                    HeldFile actual;
                    if (!files.TryGetValue(entry.Name, out actual) || actual.Length != entry.Length || actual.Hash != entry.Hash) throw new SafetyException("A modern package payload differs from its authenticated complete inventory: " + entry.Name);
                }
                if (files["EasyEject.Modern.exe"].Hash != executable.Hash) throw new SafetyException("Modern apphost trust returned different held bytes.");
                CheckExpectedLayout(); CheckFiles(cancel);
            } catch { Dispose(); throw; }
        }
        string PathFor(string name) { return SafePaths.Normalize(Path.Combine(Folder, name.Replace('/', '\\'))); }
        static long Maximum(string name) {
            if (name == ModernWindowsReleaseManifest.ManifestName) return WindowsManifestTrust.MaximumManifest;
            if (name == ModernWindowsReleaseManifest.SignatureName) return WindowsManifestTrust.MaximumSignature;
            if (name == ModernWindowsReleaseManifest.IndexName) return ModernWindowsReleaseManifest.MaximumIndex;
            if (!ModernWindowsReleaseManifest.ValidPath(name)) throw new SafetyException("Unsupported modern package path: " + name);
            return ModernWindowsReleaseManifest.MaximumFile;
        }
        void CaptureTree(CancellationToken cancel) {
            var captures = new List<SafePaths.DirectoryLease>(); var pending = new Queue<KeyValuePair<string, SafePaths.DirectoryLease>>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
            try {
                var root = SafePaths.HoldDirectories(Folder); captures.Add(root); pending.Enqueue(new KeyValuePair<string, SafePaths.DirectoryLease>("", root)); visited.Add("");
                while (pending.Count != 0) {
                    cancel.ThrowIfCancellationRequested(); var current = pending.Dequeue(); string relative = current.Key, path = relative.Length == 0 ? Folder : PathFor(relative);
                    // The current frame includes every ancestor of this branch.
                    // All other captured branches are checked together before trust.
                    current.Value.Validate();
                    if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) throw new SafetyException("Redirected or offline modern package directory refuses.");
                    WindowsPackageStreams.Require(path, -1);
                    foreach (string entry in Directory.GetFileSystemEntries(path).OrderBy(value => value, StringComparer.Ordinal)) {
                        cancel.ThrowIfCancellationRequested(); var attributes = File.GetAttributes(entry);
                        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) throw new SafetyException("Redirected or offline modern package contents refuse.");
                        string name = SafePaths.Relative(entry, Folder).Replace('\\', '/');
                        if ((attributes & FileAttributes.Directory) != 0) {
                            if (folderNames.Count >= ModernWindowsReleaseManifest.MaximumFiles * 4 || !visited.Add(name)) throw new SafetyException("Modern package directories are excessive or aliased.");
                            // Capture each parent before enumerating through it; coalesce only
                            // with both old/new frames live and old identities rechecked.
                            var capture = SafePaths.HoldDirectories(entry); captures.Add(capture); folderNames.Add(name); pending.Enqueue(new KeyValuePair<string, SafePaths.DirectoryLease>(name, capture));
                        } else {
                            if (files.Count >= ModernWindowsReleaseManifest.MaximumFiles + 3) throw new SafetyException("Modern package has excessive file contents.");
                            var held = new HeldFile(Folder, name, Maximum(name), cancel);
                            try { total += held.Length; if (total > ModernWindowsReleaseManifest.MaximumTotal + WindowsManifestTrust.MaximumManifest + WindowsManifestTrust.MaximumSignature + ModernWindowsReleaseManifest.MaximumIndex) throw new SafetyException("Modern package bytes exceed their supported aggregate bound."); files.Add(name, held); }
                            catch { held.Dispose(); throw; }
                        }
                    }
                    current.Value.Validate();
                }
                foreach (var capture in captures) capture.Validate();
                directories = SafePaths.HoldDirectories(new[] { Folder }.Concat(folderNames.Select(PathFor)).ToArray());
                foreach (var capture in captures) capture.Validate(); directories.Validate();
                foreach (string name in new[] { ModernWindowsReleaseManifest.ManifestName, ModernWindowsReleaseManifest.SignatureName, ModernWindowsReleaseManifest.IndexName, "EasyEject.Modern.exe" })
                    if (!files.ContainsKey(name)) throw new SafetyException("Modern package is missing required metadata or its apphost.");
            } finally { foreach (var capture in captures.AsEnumerable().Reverse()) capture.Dispose(); }
        }
        WindowsAuthenticatedManifest Authenticate(CancellationToken cancel) {
            cancel.ThrowIfCancellationRequested(); CheckLayout(cancel);
            var body = files[ModernWindowsReleaseManifest.ManifestName]; var signature = files[ModernWindowsReleaseManifest.SignatureName];
            var result = manifestTrust.Verify(body.Bytes(), signature.Bytes(), publisher, executable.Signer, cancel);
            cancel.ThrowIfCancellationRequested();
            if (result == null || result.BodyHash != body.Hash || result.SignatureHash != signature.Hash || result.Signer.CertificateHash != executable.Signer.CertificateHash) throw new SafetyException("Modern manifest trust returned a different file or signer snapshot.");
            publisher.Require(result.Signer); CheckLayout(cancel); return result;
        }
        void CheckExpectedLayout() {
            var expected = Release.Files.Select(file => file.Name).Concat(new[] { ModernWindowsReleaseManifest.ManifestName, ModernWindowsReleaseManifest.SignatureName, ModernWindowsReleaseManifest.IndexName }).OrderBy(name => name, StringComparer.Ordinal);
            if (!files.Keys.OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(expected, StringComparer.Ordinal)) throw new SafetyException("Modern package has extra or differently named file contents.");
            var expectedFolders = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in expected) { int slash = name.IndexOf('/'); while (slash >= 0) { expectedFolders.Add(name.Substring(0, slash)); slash = name.IndexOf('/', slash + 1); } }
            if (!folderNames.OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(expectedFolders.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal)) throw new SafetyException("Modern package has extra, missing or differently named directories.");
        }
        void CheckLayout(CancellationToken cancel) {
            if (disposed) throw new ObjectDisposedException("ModernWindowsPackageContentReview");
            directories.Validate(); var actualFiles = new List<string>(); var actualFolders = new List<string>();
            foreach (string name in new[] { "" }.Concat(folderNames)) {
                cancel.ThrowIfCancellationRequested(); string path = name.Length == 0 ? Folder : PathFor(name);
                if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) throw new SafetyException("Modern package directory is redirected or offline.");
                WindowsPackageStreams.Require(path, -1);
                foreach (string entry in Directory.GetFileSystemEntries(path)) {
                    var attributes = File.GetAttributes(entry); if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) throw new SafetyException("Modern package entry is redirected or offline.");
                    string relative = SafePaths.Relative(entry, Folder).Replace('\\', '/');
                    if ((attributes & FileAttributes.Directory) != 0) actualFolders.Add(relative); else actualFiles.Add(relative);
                    if (actualFiles.Count > ModernWindowsReleaseManifest.MaximumFiles + 3 || actualFolders.Count > ModernWindowsReleaseManifest.MaximumFiles * 4) throw new SafetyException("Modern package layout exceeds its bound.");
                }
            }
            if (!actualFiles.OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(files.Keys.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal) || !actualFolders.OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(folderNames.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal))
                throw new SafetyException("Modern package contents changed during review.");
            directories.Validate();
        }
        void CheckFiles(CancellationToken cancel) {
            CheckLayout(cancel);
            foreach (var file in files.Values) { cancel.ThrowIfCancellationRequested(); file.Revalidate(cancel); }
            if (executable != null) executable.Revalidate(cancel, false); CheckLayout(cancel);
        }
        internal void Revalidate(CancellationToken cancel) {
            CheckFiles(cancel); executable.Revalidate(cancel, true);
            var fresh = Authenticate(cancel);
            if (fresh.BodyHash != Authenticated.BodyHash || fresh.SignatureHash != Authenticated.SignatureHash) throw new SafetyException("Modern authenticated release snapshot changed.");
            ModernWindowsReleaseManifest.Parse(fresh, files[ModernWindowsReleaseManifest.IndexName].Bytes()); CheckExpectedLayout(); CheckFiles(cancel);
        }
        internal void Inspect(string name, Action<FileStream> inspect, CancellationToken cancel) {
            if (inspect == null) throw new ArgumentNullException("inspect"); CheckFiles(cancel); HeldFile file;
            if (!files.TryGetValue(name, out file)) throw new SafetyException("The inspected file is outside the held modern package.");
            long position = file.Stream.Position;
            try { inspect(file.Stream); CheckFiles(cancel); }
            finally { if (file.Stream.CanSeek) file.Stream.Position = position; }
        }
        internal void InspectMany(IEnumerable<string> names, Action<string, FileStream> inspect, CancellationToken cancel) {
            if (disposed) throw new ObjectDisposedException("ModernWindowsPackageContentReview");
            if (names == null || inspect == null) throw new ArgumentNullException("inspect");
            cancel.ThrowIfCancellationRequested(); var selected = new List<string>(); var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in names) {
                cancel.ThrowIfCancellationRequested();
                if (name == null || !unique.Add(name) || selected.Count >= ModernWindowsReleaseManifest.MaximumFiles || !files.ContainsKey(name)) throw new SafetyException("Batch inspection needs unique held modern package files.");
                selected.Add(name);
            }
            CheckFiles(cancel);
            foreach (string name in selected) {
                cancel.ThrowIfCancellationRequested(); var file = files[name]; file.Validate(); long position = file.Stream.Position;
                try { inspect(name, file.Stream); file.Validate(); cancel.ThrowIfCancellationRequested(); }
                finally { if (file.Stream.CanSeek) file.Stream.Position = position; }
            }
            CheckFiles(cancel);
        }
        public void Dispose() { if (disposed) return; disposed = true; if (executable != null) executable.Dispose(); foreach (var file in files.Values) file.Dispose(); files.Clear(); if (directories != null) directories.Dispose(); }
    }
}
