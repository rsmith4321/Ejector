using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        sealed class ModernArchiveFixture : IDisposable {
            internal readonly ModernPackageFixture Package = new ModernPackageFixture();
            internal string Path { get { return System.IO.Path.Combine(Package.Owned.Destination, "modern.zip"); } }
            internal ModernArchiveFixture() { Write(); }
            // Independent bounded fixture writer exercises stored and both descriptor forms.
            internal void Write(bool stored = false, int descriptor = 0, Func<string, string> rename = null, string omit = null, string extra = null) {
                var data = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                foreach (string name in Package.Metadata.Files.Select(file => file.Name).Concat(new[] { ModernWindowsReleaseManifest.ManifestName, ModernWindowsReleaseManifest.SignatureName, ModernWindowsReleaseManifest.IndexName }).Where(name => name != omit))
                    data.Add(rename == null ? name : rename(name), File.ReadAllBytes(Package.PathFor(name)));
                if (extra != null) data.Add(extra, new byte[] { 1 });
                using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream, Encoding.ASCII, true)) {
                    var rows = new List<byte[]>();
                    foreach (var pair in data) {
                        byte[] name = Encoding.UTF8.GetBytes(pair.Key), compressed = pair.Value; Check(pair.Value.Length < 16777216, "Archive fixture memory bound");
                        if (!stored) using (var small = new MemoryStream()) { using (var deflate = new DeflateStream(small, CompressionLevel.Optimal, true)) deflate.Write(pair.Value, 0, pair.Value.Length); compressed = small.ToArray(); }
                        uint offset = checked((uint)stream.Position), crc = ~WindowsZipCrc.Add(UInt32.MaxValue, pair.Value, pair.Value.Length);
                        ushort flags = (ushort)(descriptor == 0 ? 0 : 8), method = (ushort)(stored ? 0 : 8), version = (ushort)(stored && descriptor == 0 ? 10 : 20), date = 20580, time = 10436;
                        writer.Write(0x04034b50U); writer.Write(version); writer.Write(flags); writer.Write(method); writer.Write(time); writer.Write(date);
                        writer.Write(descriptor == 0 ? crc : 0); writer.Write(descriptor == 0 ? (uint)compressed.Length : 0); writer.Write(descriptor == 0 ? (uint)pair.Value.Length : 0);
                        writer.Write((ushort)name.Length); writer.Write((ushort)0); writer.Write(name); writer.Write(compressed);
                        if (descriptor != 0) { if (descriptor == 16) writer.Write(0x08074b50U); writer.Write(crc); writer.Write((uint)compressed.Length); writer.Write((uint)pair.Value.Length); }
                        using (var row = new MemoryStream()) using (var central = new BinaryWriter(row)) {
                            central.Write(0x02014b50U); central.Write((ushort)20); central.Write(version); central.Write(flags); central.Write(method); central.Write(time); central.Write(date);
                            central.Write(crc); central.Write((uint)compressed.Length); central.Write((uint)pair.Value.Length); central.Write((ushort)name.Length);
                            central.Write((ushort)0); central.Write((ushort)0); central.Write((ushort)0); central.Write((ushort)0); central.Write(0U); central.Write(offset); central.Write(name); rows.Add(row.ToArray());
                        }
                    }
                    uint start = checked((uint)stream.Position); foreach (byte[] row in rows) writer.Write(row); uint length = checked((uint)stream.Position) - start;
                    writer.Write(0x06054b50U); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)rows.Count); writer.Write((ushort)rows.Count); writer.Write(length); writer.Write(start); writer.Write((ushort)0);
                    File.WriteAllBytes(Path, stream.ToArray());
                }
            }
            internal ModernWindowsArchiveReview Review(CancellationToken cancel = default(CancellationToken), IWindowsManifestEnvelopeTrust trust = null) {
                return new ModernWindowsArchiveReview(Path, Package.Signing.Publisher(), trust ?? new WindowsManifestTrust(Package.Certificate), cancel);
            }
            internal void Mutate(Action<byte[]> mutate) { byte[] bytes = File.ReadAllBytes(Path); mutate(bytes); File.WriteAllBytes(Path, bytes); }
            internal void Released() { using (File.Open(Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } Package.RequireReleased(); }
            public void Dispose() { Package.Dispose(); }
        }
        static List<int> ModernZipRows(byte[] bytes) {
            var rows = new List<int>(); int cursor = checked((int)BitConverter.ToUInt32(bytes, bytes.Length - 6)), count = BitConverter.ToUInt16(bytes, bytes.Length - 12);
            for (int i = 0; i < count; i++) { rows.Add(cursor); cursor += 46 + BitConverter.ToUInt16(bytes, cursor + 28); } return rows;
        }
        static SafetyException RefuseModernArchive(Func<ModernWindowsArchiveReview> create) {
            try { using (var unexpected = create()) { } } catch (SafetyException error) { return error; } throw new Exception("Expected modern archive refusal");
        }
        static void ModernWindowsArchiveTests() {
            Test("modern ZIP authenticates a 600-file nested inventory above the classic metadata bound", delegate {
                using (var f = new ModernArchiveFixture()) {
                    for (int i = f.Package.Metadata.Files.Count; i < 600; i++) {
                        string name = "en-US/fixture" + i.ToString("D4") + ".mui"; byte[] bytes = new byte[] { 1, 2 };
                        File.WriteAllBytes(f.Package.PathFor(name), bytes); f.Package.Metadata.Files.Add(new WindowsReleaseFile(name, bytes.Length, WindowsSignerEvidence.Hash(bytes)));
                    }
                    f.Package.Publish(); f.Write(); Check(f.Package.Metadata.Index().Length > WindowsManifestTrust.MaximumManifest, "Large inventory boundary not exercised");
                    using (var review = f.Review()) Check(review.Release.Files.Count == 600, "Complete large inventory lost"); f.Released();
                }
            });
            Test("modern ZIP reviews exact nested contents read-only and retains archive ownership", delegate {
                using (var f = new ModernArchiveFixture()) {
                    var before = Directory.GetFiles(f.Package.Owned.Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, path => WindowsSignerEvidence.Hash(File.ReadAllBytes(path)));
                    f.Package.Certificate.During = () => { Throws<IOException>(() => File.WriteAllText(f.Path, "changed")); Throws<IOException>(() => File.Move(f.Path, f.Path + ".moved")); };
                    var review = f.Review();
                    try {
                        Check(review.Hash == before[f.Path] && review.Release.Files.Count == f.Package.Metadata.Files.Count && f.Package.Executable.Calls == 0, "Archive lost contents or claimed executable trust");
                        review.Revalidate(CancellationToken.None);
                        using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); Throws<OperationCanceledException>(() => review.Revalidate(cancel.Token)); }
                        review.Revalidate(CancellationToken.None);
                    } finally { review.Dispose(); }
                    review.Dispose(); Throws<ObjectDisposedException>(() => review.Revalidate(CancellationToken.None)); f.Released();
                    Check(before.Count == Directory.GetFiles(f.Package.Owned.Root, "*", SearchOption.AllDirectories).Length && before.All(pair => pair.Value == WindowsSignerEvidence.Hash(File.ReadAllBytes(pair.Key))), "Review extracted or changed data");
                }
            });
            Test("modern ZIP accepts stored deflated and signed or unsigned descriptors", delegate {
                using (var f = new ModernArchiveFixture()) foreach (bool stored in new[] { false, true }) foreach (int descriptor in new[] { 0, 12, 16 }) {
                    f.Write(stored, descriptor); using (var input = File.OpenRead(f.Path)) {
                        var entries = WindowsZipLayout.ReadModern(input, CancellationToken.None);
                        Check(entries.All(entry => entry.Method == (stored ? 0 : 8) && (entry.Flags & 8) == (descriptor == 0 ? 0 : 8)), "Fixture writer did not exercise selected method/descriptor");
                    }
                    using (var review = f.Review()) Check(review.Release.IndexHash == WindowsSignerEvidence.Hash(f.Package.Metadata.Index()), "Writer variant lost inventory binding"); f.Released();
                }
            });
            Test("modern ZIP refuses missing approval and precancellation before opening or trust", delegate {
                using (var f = new ModernArchiveFixture()) using (var cancel = new CancellationTokenSource()) {
                    RefuseModernArchive(() => new ModernWindowsArchiveReview(f.Path, null, new WindowsManifestTrust(f.Package.Certificate), CancellationToken.None));
                    cancel.Cancel(); Throws<OperationCanceledException>(() => f.Review(cancel.Token)); Check(f.Package.Certificate.Calls == 0, "Admission invoked trust"); f.Released();
                }
            });
            Test("modern ZIP refuses unsafe paths casing aliases directory entries and prefix collisions before trust", delegate {
                foreach (string name in new[] { "../bad.dll", "/bad.dll", "C:/bad.dll", "nested\\bad.dll", "bad.dll:stream", "CON.dll", "nested/", "bad\u00e9.dll", "nested/fixture.json/child.dll", "ASSETS/EasyEject.png" }) using (var f = new ModernArchiveFixture()) {
                    f.Write(extra: name); RefuseModernArchive(() => f.Review()); Check(f.Package.Certificate.Calls == 0, "Unsafe layout reached trust: " + name); f.Released();
                }
            });
            Test("modern ZIP refuses missing or differently cased envelope signature and index before trust", delegate {
                foreach (string name in new[] { ModernWindowsReleaseManifest.ManifestName, ModernWindowsReleaseManifest.SignatureName, ModernWindowsReleaseManifest.IndexName }) foreach (bool wrongCase in new[] { false, true }) using (var f = new ModernArchiveFixture()) {
                    if (wrongCase) f.Write(rename: value => value == name ? value.ToUpperInvariant() : value); else f.Write(omit: name);
                    RefuseModernArchive(() => f.Review()); Check(f.Package.Certificate.Calls == 0, "Missing metadata reached trust"); f.Released();
                }
            });
            Test("modern ZIP refuses extra omitted or case-changed payload against authenticated inventory", delegate {
                foreach (int mode in new[] { 0, 1, 2 }) using (var f = new ModernArchiveFixture()) {
                    if (mode == 0) f.Write(extra: "extra.txt"); if (mode == 1) f.Write(omit: "nested/fixture.json"); if (mode == 2) f.Write(rename: name => name == "nested/fixture.json" ? "nested/Fixture.json" : name);
                    RefuseModernArchive(() => f.Review()); Check(f.Package.Certificate.Calls == 1, "Inventory mismatch skipped authenticated control"); f.Released();
                }
            });
            Test("modern ZIP refuses payload hash and length differences even with valid ZIP CRC", delegate {
                foreach (bool resize in new[] { false, true }) using (var f = new ModernArchiveFixture()) {
                    string path = f.Package.PathFor("nested/fixture.json"); byte[] original = File.ReadAllBytes(path), changed = resize ? original.Concat(new byte[] { 1 }).ToArray() : (byte[])original.Clone(); changed[0] ^= 1;
                    File.WriteAllBytes(path, changed); f.Write(); RefuseModernArchive(() => f.Review()); f.Released();
                    Check(File.ReadAllBytes(path).SequenceEqual(changed), "Refusal cleaned payload"); File.WriteAllBytes(path, original); f.Write(); using (var retry = f.Review()) { }
                }
            });
            Test("modern ZIP refuses index substitution and signature damage and preserves manual retry", delegate {
                foreach (string name in new[] { ModernWindowsReleaseManifest.IndexName, ModernWindowsReleaseManifest.SignatureName }) using (var f = new ModernArchiveFixture()) {
                    string path = f.Package.PathFor(name); byte[] original = File.ReadAllBytes(path), changed = (byte[])original.Clone(); changed[changed.Length / 2] ^= 1; File.WriteAllBytes(path, changed); f.Write();
                    RefuseModernArchive(() => f.Review()); f.Released(); Check(File.ReadAllBytes(path).SequenceEqual(changed), "Refusal cleaned metadata"); File.WriteAllBytes(path, original); f.Write(); using (var retry = f.Review()) { }
                }
            });
            Test("modern ZIP rejects returned authentication snapshot substitution", delegate {
                using (var f = new ModernArchiveFixture()) {
                    var trust = new MutatingEnvelopeTrust { Actual = new WindowsManifestTrust(f.Package.Certificate), Retarget = true };
                    RefuseModernArchive(() => f.Review(trust: trust)); f.Released();
                    trust.Retarget = false; using (var review = f.Review(trust: trust)) review.Revalidate(CancellationToken.None);
                }
            });
            Test("modern ZIP cancellation during authentication releases held resources for a manual retry", delegate {
                using (var f = new ModernArchiveFixture()) using (var cancel = new CancellationTokenSource()) {
                    f.Package.Certificate.During = () => cancel.Cancel(); Throws<OperationCanceledException>(() => f.Review(cancel.Token)); f.Released();
                    f.Package.Certificate.During = null; using (var retry = f.Review()) { }
                }
            });
            Test("modern ZIP rejects encryption symlinks impossible counts and declared size bounds before trust", delegate {
                foreach (int kind in Enumerable.Range(0, 5)) using (var f = new ModernArchiveFixture()) {
                    f.Mutate(bytes => {
                        int row = ModernZipRows(bytes)[0];
                        if (kind == 0) FixtureZipU16(bytes, row + 8, 1);
                        if (kind == 1) { bytes[row + 5] = 3; FixtureZipU32(bytes, row + 38, 0xa1ff0000); }
                        if (kind == 2) { FixtureZipU16(bytes, bytes.Length - 14, 2052); FixtureZipU16(bytes, bytes.Length - 12, 2052); }
                        if (kind == 3) FixtureZipU32(bytes, row + 24, 1073741825);
                        if (kind == 4) foreach (int current in ModernZipRows(bytes).Take(3)) FixtureZipU32(bytes, current + 24, 1073741824);
                    });
                    RefuseModernArchive(() => f.Review()); Check(f.Package.Certificate.Calls == 0, "Malformed header reached trust"); f.Released();
                }
            });
            Test("modern ZIP rejects CRC damage consistent local headers and overlapping records", delegate {
                foreach (bool overlap in new[] { false, true }) using (var f = new ModernArchiveFixture()) {
                    f.Write(stored: true); f.Mutate(bytes => {
                        int row = ModernZipRows(bytes)[0], local = checked((int)BitConverter.ToUInt32(bytes, row + 42));
                        if (overlap) FixtureZipU32(bytes, row + 42, 1);
                        else { uint crc = BitConverter.ToUInt32(bytes, row + 16) ^ 1; FixtureZipU32(bytes, row + 16, crc); FixtureZipU32(bytes, local + 14, crc); }
                    });
                    RefuseModernArchive(() => f.Review()); f.Released();
                }
            });
            Test("modern ZIP refuses classic six-file archives and classic policy refuses modern archives", delegate {
                using (var classic = new WindowsArchiveFixture()) RefuseModernArchive(() => new ModernWindowsArchiveReview(classic.Path, classic.Package.Signing.Publisher(), new WindowsManifestTrust(classic.Package.CertificateTrust), CancellationToken.None));
                using (var modern = new ModernArchiveFixture()) RefuseArchive(() => new WindowsArchiveReview(modern.Path, modern.Package.Signing.Publisher(), new WindowsManifestTrust(modern.Package.Certificate), CancellationToken.None));
            });
        }
        static int ModernArchiveApplicationSample(string folder) {
            byte[] host = File.ReadAllBytes(System.IO.Path.Combine(folder, "EasyEject.Modern.exe")), managed = File.ReadAllBytes(System.IO.Path.Combine(folder, "EasyEject.Modern.dll"));
            byte[] backendBytes = File.ReadAllBytes(System.IO.Path.Combine(folder, "EasyEject.Runtime.dll"));
            Test("modern ZIP binds separately held application review to exact inventory envelope and signer", delegate {
                using (var f = new ModernArchiveFixture()) {
                    ApplicationPackageFixture(f.Package, host, managed, backendBytes); f.Write();
                    using (var archive = f.Review()) using (var application = ApplicationPackage(f.Package)) archive.VerifyExtractedPackage(application, CancellationToken.None);
                    f.Released();
                }
            });
            Test("modern ZIP refuses another independently valid application snapshot", delegate {
                using (var f = new ModernArchiveFixture()) {
                    ApplicationPackageFixture(f.Package, host, managed, backendBytes); f.Write();
                    using (var archive = f.Review()) {
                        string path = f.Package.PathFor("nested/fixture.json"); File.WriteAllText(path, "another valid snapshot"); byte[] bytes = File.ReadAllBytes(path);
                        f.Package.Metadata.Files.RemoveAll(file => file.Name == "nested/fixture.json"); f.Package.Metadata.Files.Add(new WindowsReleaseFile("nested/fixture.json", bytes.Length, WindowsSignerEvidence.Hash(bytes))); f.Package.Publish();
                        using (var application = ApplicationPackage(f.Package)) Throws<SafetyException>(() => archive.VerifyExtractedPackage(application, CancellationToken.None));
                    }
                    f.Released();
                }
            });
            Console.WriteLine(passed + " passed, " + failed + " failed"); return failed == 0 ? 0 : 1;
        }
    }
}
