using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        sealed class WindowsArchiveFixture : IDisposable {
            internal readonly WindowsPackageFixture Package = new WindowsPackageFixture();
            internal string Path { get { return System.IO.Path.Combine(Package.Files.Destination, "release.zip"); } }
            internal WindowsArchiveFixture() { Write(); }
            internal void Write(CompressionLevel level = CompressionLevel.Optimal, bool descriptors = false, Func<string, string> rename = null, string extra = null, string omit = null) {
                using (var file = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.None)) {
                    Stream target = descriptors ? (Stream)new FixtureZipNonSeek(file) : file;
                    using (var archive = new ZipArchive(target, ZipArchiveMode.Create, true)) {
                        foreach (string name in WindowsReleaseManifest.PayloadNames.Concat(new[] { WindowsReleaseManifest.ManifestName, WindowsReleaseManifest.SignatureName }).Where(n => n != omit)) {
                            var entry = archive.CreateEntry(rename == null ? name : rename(name), level); entry.LastWriteTime = new DateTimeOffset(2020, 3, 4, 5, 6, 8, TimeSpan.Zero);
                            using (var output = entry.Open()) using (var input = File.OpenRead(System.IO.Path.Combine(Package.Root, name))) input.CopyTo(output);
                        }
                        if (extra != null) using (var output = archive.CreateEntry(extra, level).Open()) output.WriteByte(1);
                    }
                }
            }
            internal WindowsArchiveReview Review(CancellationToken cancel = default(CancellationToken), IWindowsManifestEnvelopeTrust trust = null) { return new WindowsArchiveReview(Path, Package.Signing.Publisher(), trust ?? new WindowsManifestTrust(Package.CertificateTrust), cancel); }
            internal void Mutate(Action<byte[]> action) { byte[] bytes = File.ReadAllBytes(Path); action(bytes); File.WriteAllBytes(Path, bytes); }
            internal void ConvertToStored() {
                byte[] bytes = File.ReadAllBytes(Path); var payloads = new Dictionary<string, byte[]>(); List<WindowsZipEntry> entries;
                using (var input = File.OpenRead(Path)) {
                    entries = WindowsZipLayout.Read(input, CancellationToken.None); input.Position = 0;
                    using (var archive = new ZipArchive(input, ZipArchiveMode.Read, true)) foreach (var entry in archive.Entries) using (var content = entry.Open()) using (var copy = new MemoryStream()) { Check(entry.Length < 16777216, "Stored fixture exceeded its memory limit"); content.CopyTo(copy); payloads.Add(entry.FullName, copy.ToArray()); }
                }
                using (var output = new MemoryStream()) {
                    var offsets = new Dictionary<string, uint>(); var flags = new Dictionary<string, ushort>();
                    foreach (var entry in entries.OrderBy(e => e.Local)) {
                        offsets.Add(entry.Name, (uint)output.Position); ushort flag = (ushort)(entry.Flags & 0x808), version = (ushort)((flag & 8) != 0 ? 20 : 10); flags.Add(entry.Name, flag);
                        byte[] header = bytes.Skip((int)entry.Local).Take(30).ToArray(); FixtureZipU16(header, 4, version); FixtureZipU16(header, 6, flag); FixtureZipU16(header, 8, 0);
                        FixtureZipU32(header, 14, (flag & 8) != 0 ? 0 : entry.Crc); FixtureZipU32(header, 18, (flag & 8) != 0 ? 0 : entry.Length); FixtureZipU32(header, 22, (flag & 8) != 0 ? 0 : entry.Length);
                        output.Write(header, 0, header.Length); byte[] name = Encoding.ASCII.GetBytes(entry.Name); output.Write(name, 0, name.Length); byte[] payload = payloads[entry.Name]; output.Write(payload, 0, payload.Length);
                        if ((flag & 8) != 0) { byte[] descriptor = new byte[16]; FixtureZipU32(descriptor, 0, 0x08074b50); FixtureZipU32(descriptor, 4, entry.Crc); FixtureZipU32(descriptor, 8, entry.Length); FixtureZipU32(descriptor, 12, entry.Length); output.Write(descriptor, 0, descriptor.Length); }
                    }
                    uint central = (uint)output.Position;
                    foreach (int row in FixtureZipCentral(bytes)) {
                        int nameLength = BitConverter.ToUInt16(bytes, row + 28); string name = Encoding.ASCII.GetString(bytes, row + 46, nameLength); ushort flag = flags[name];
                        FixtureZipU16(bytes, row + 6, (ushort)((flag & 8) != 0 ? 20 : 10)); FixtureZipU16(bytes, row + 8, flag); FixtureZipU16(bytes, row + 10, 0); FixtureZipU32(bytes, row + 20, (uint)payloads[name].Length); FixtureZipU32(bytes, row + 42, offsets[name]); output.Write(bytes, row, 46 + nameLength);
                    }
                    uint size = (uint)output.Position - central; byte[] tail = bytes.Skip(bytes.Length - 22).ToArray(); FixtureZipU32(tail, 12, size); FixtureZipU32(tail, 16, central); output.Write(tail, 0, tail.Length); File.WriteAllBytes(Path, output.ToArray());
                }
            }
            internal void RemoveDescriptorSignatures() {
                byte[] bytes = File.ReadAllBytes(Path); List<WindowsZipEntry> entries;
                using (var input = File.OpenRead(Path)) entries = WindowsZipLayout.Read(input, CancellationToken.None);
                var offsets = new Dictionary<string, uint>(); using (var output = new MemoryStream()) {
                    foreach (var entry in entries.OrderBy(e => e.Local)) { offsets.Add(entry.Name, (uint)output.Position); int length = checked((int)(entry.Data + entry.Compressed - entry.Local)); output.Write(bytes, (int)entry.Local, length); output.Write(bytes, (int)(entry.Data + entry.Compressed + 4), 12); }
                    uint central = (uint)output.Position; var rows = FixtureZipCentral(bytes);
                    foreach (int row in rows) { int nameLength = BitConverter.ToUInt16(bytes, row + 28); string name = Encoding.ASCII.GetString(bytes, row + 46, nameLength); FixtureZipU32(bytes, row + 42, offsets[name]); output.Write(bytes, row, 46 + nameLength); }
                    byte[] tail = bytes.Skip(bytes.Length - 22).ToArray(); FixtureZipU32(tail, 16, central); output.Write(tail, 0, tail.Length); File.WriteAllBytes(Path, output.ToArray());
                }
            }
            public void Dispose() { Package.Dispose(); }
        }
        sealed class FixtureZipNonSeek : Stream {
            readonly Stream target; internal FixtureZipNonSeek(Stream target) { this.target = target; }
            public override bool CanRead { get { return false; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return true; } }
            public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { return target.Position; } set { throw new NotSupportedException(); } }
            public override void Flush() { target.Flush(); } public override void Write(byte[] bytes, int offset, int count) { target.Write(bytes, offset, count); }
            public override int Read(byte[] bytes, int offset, int count) { throw new NotSupportedException(); } public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); } public override void SetLength(long value) { throw new NotSupportedException(); }
        }
        sealed class MutatingEnvelopeTrust : IWindowsManifestEnvelopeTrust {
            internal IWindowsManifestEnvelopeTrust Actual; internal bool Retarget; internal int Calls;
            public WindowsAuthenticatedManifest VerifyEnvelope(byte[] body, byte[] signature, ApprovedWindowsPublisher publisher, CancellationToken cancel) {
                Calls++; var result = Actual.VerifyEnvelope(body, signature, publisher, cancel); body[body.Length - 1] ^= 1; signature[0] ^= 1;
                return Retarget ? new WindowsAuthenticatedManifest(body, signature, result.Signer) : result;
            }
        }
        static SafetyException RefuseArchive(Func<WindowsArchiveReview> review) {
            try { using (var unexpected = review()) { } } catch (SafetyException failure) { return failure; } throw new Exception("Expected archive refusal");
        }
        static List<int> FixtureZipCentral(byte[] bytes) {
            int cursor = checked((int)BitConverter.ToUInt32(bytes, bytes.Length - 22 + 16)); var rows = new List<int>();
            for (int i = 0; i < 6; i++) { rows.Add(cursor); cursor += 46 + BitConverter.ToUInt16(bytes, cursor + 28) + BitConverter.ToUInt16(bytes, cursor + 30) + BitConverter.ToUInt16(bytes, cursor + 32); } return rows;
        }
        static void FixtureZipU16(byte[] bytes, int offset, ushort value) { Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 2); }
        static void FixtureZipU32(byte[] bytes, int offset, uint value) { Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 4); }
        static void WindowsArchiveTests() {
            Test("Windows ZIP contents authenticate without extraction writes or executable verification", delegate {
                using (var f = new WindowsArchiveFixture()) {
                    var before = Directory.GetFiles(f.Package.Files.Root, "*", SearchOption.AllDirectories).ToDictionary(p => p, p => WindowsSignerEvidence.Hash(File.ReadAllBytes(p)));
                    using (var review = f.Review()) { Check(review.Release.Version == "0.2.0.0" && review.Length == new FileInfo(f.Path).Length && review.Hash == before[f.Path] && f.Package.ExecutableTrust.Calls == 0 && f.Package.CertificateTrust.Calls == 1, "Archive preflight claims executable trust or lacks contents proof"); review.Revalidate(CancellationToken.None); }
                    Check(Directory.GetFiles(f.Package.Files.Root, "*", SearchOption.AllDirectories).Length == before.Count && before.All(p => File.Exists(p.Key) && p.Value == WindowsSignerEvidence.Hash(File.ReadAllBytes(p.Key))), "Archive inspection wrote or extracted files");
                }
            });
            Test("Windows ZIP accepts explicit stored fixtures and Framework deflated streamed descriptors", delegate {
                var methods = new HashSet<ushort>(); bool sawDescriptor = false;
                using (var f = new WindowsArchiveFixture()) foreach (CompressionLevel level in new[] { CompressionLevel.NoCompression, CompressionLevel.Optimal }) foreach (bool descriptor in new[] { false, true }) { f.Write(level, descriptor); if (level == CompressionLevel.NoCompression) f.ConvertToStored(); using (var input = File.OpenRead(f.Path)) { var entries = WindowsZipLayout.Read(input, CancellationToken.None); methods.UnionWith(entries.Select(e => e.Method)); if (descriptor) { Check(entries.All(e => (e.Flags & 8) != 0), "Nonseek writer did not exercise descriptors"); sawDescriptor = true; } } using (var review = f.Review()) Check(review.Release.Files.Count == 4, "Writer variant lost payloads"); }
                Check(methods.SetEquals(new ushort[] { 0, 8 }) && sawDescriptor, "Stored/deflated/streamed evidence was incomplete");
                Check(~WindowsZipCrc.Add(UInt32.MaxValue, Encoding.ASCII.GetBytes("123456789"), 9) == 0xcbf43926, "CRC algorithm differs from standard check vector");
            });
            Test("Windows ZIP accepts consistent unsigned data descriptors and regular Unix metadata", delegate {
                using (var f = new WindowsArchiveFixture()) { f.Write(descriptors: true); f.RemoveDescriptorSignatures(); using (var review = f.Review()) Check(review.Release.Files.Count == 4, "Unsigned descriptors lost payloads");
                    f.Write(); f.Mutate(bytes => { foreach (int row in FixtureZipCentral(bytes)) { bytes[row + 5] = 3; FixtureZipU32(bytes, row + 38, 0x81a40020); } }); using (var review = f.Review()) Check(review.Release.Files.Count == 4, "Regular Unix metadata refused");
                }
            });
            Test("Windows ZIP refuses inconsistent signed unsigned and local descriptor metadata", delegate {
                using (var f = new WindowsArchiveFixture()) foreach (bool signed in new[] { false, true }) foreach (string kind in new[] { "crc", "compressed", "length", "local-mixed" }) {
                    f.Write(descriptors: true); if (!signed) f.RemoveDescriptorSignatures(); f.Mutate(bytes => { int row = FixtureZipCentral(bytes)[0], local = (int)BitConverter.ToUInt32(bytes, row + 42), data = local + 30 + BitConverter.ToUInt16(bytes, local + 26), descriptor = data + (int)BitConverter.ToUInt32(bytes, row + 20) + (signed ? 4 : 0);
                        FixtureZipU32(bytes, kind == "local-mixed" ? local + 14 : descriptor + (kind == "crc" ? 0 : kind == "compressed" ? 4 : 8), 1);
                    }); RefuseArchive(() => f.Review());
                }
            });
            Test("Windows ZIP refuses invalid compressed data and expansion beyond signed declared size", delegate {
                using (var f = new WindowsArchiveFixture()) { f.Mutate(bytes => { int c = FixtureZipCentral(bytes)[0], local = (int)BitConverter.ToUInt32(bytes, c + 42), data = local + 30 + BitConverter.ToUInt16(bytes, local + 26); bytes[data] = 255; }); Check(RefuseArchive(() => f.Review()).Message.Contains("compressed entry is invalid"), "Reserved deflate block did not refuse"); }
                using (var f = new WindowsArchiveFixture()) { string readme = System.IO.Path.Combine(f.Package.Root, "README.txt"), old = "\"name\":\"README.txt\",\"length\":" + new FileInfo(readme).Length; f.Package.Publish(f.Package.ManifestText().Replace(old, "\"name\":\"README.txt\",\"length\":1")); f.Write(); f.Mutate(bytes => { int c = FixtureZipCentral(bytes)[2], local = (int)BitConverter.ToUInt32(bytes, c + 42); FixtureZipU32(bytes, c + 24, 1); FixtureZipU32(bytes, local + 22, 1); }); Check(RefuseArchive(() => f.Review()).Message.Contains("expands beyond"), "ZIP expanded beyond tiny signed declared limit"); }
            });
            Test("Windows ZIP retains exact file and ancestor identities until disposed", delegate {
                using (var f = new WindowsArchiveFixture()) { using (var review = f.Review()) { Throws<IOException>(() => File.WriteAllText(f.Path, "foreign")); Throws<IOException>(() => File.Delete(f.Path)); Throws<IOException>(() => File.Move(f.Path, f.Path + ".moved")); Throws<IOException>(() => Directory.Move(f.Package.Files.Destination, f.Package.Files.Destination + "-moved")); } File.Move(f.Path, f.Path + ".released"); }
            });
            Test("Windows ZIP rejects unconfigured publisher or cancellation before creating storage or trust", delegate {
                using (var f = new Fixture()) using (var cancel = new CancellationTokenSource()) { string missing = System.IO.Path.Combine(f.Root, "missing", "archive.zip"); var signer = new ManifestSigningFixture(); try {
                    Throws<SafetyException>(() => new WindowsArchiveReview(missing, null, new WindowsManifestTrust(), CancellationToken.None)); cancel.Cancel(); Throws<OperationCanceledException>(() => new WindowsArchiveReview(missing, signer.Publisher(), new WindowsManifestTrust(), cancel.Token)); Check(!Directory.Exists(System.IO.Path.GetDirectoryName(missing)), "Rejected ZIP admission created storage");
                } finally { signer.Dispose(); } }
            });
            Test("Windows ZIP refuses invalid or excessive physical lengths and truncated archive structure", delegate {
                using (var f = new WindowsArchiveFixture()) {
                    foreach (byte[] bytes in new[] { new byte[0], new byte[21], new byte[22], File.ReadAllBytes(f.Path).Take(140).ToArray() }) { File.WriteAllBytes(f.Path, bytes); RefuseArchive(() => f.Review()); }
                    using (var file = new FileStream(f.Path, FileMode.Create, FileAccess.Write, FileShare.None)) file.SetLength(WindowsArchiveReview.MaximumArchive + 1); RefuseArchive(() => f.Review()); Check(f.Package.CertificateTrust.Calls == 0, "Oversized/truncated archive reached certificate trust");
                }
            });
            Test("Windows ZIP refuses traversal aliases streams duplicate extra and missing contents", delegate {
                using (var f = new WindowsArchiveFixture()) {
                    foreach (string alias in new[] { "../README.txt", "/README.txt", "C:\\README.txt", "README.TXT", "README.txt ", "README.txt:foreign", "folder/README.txt", "folder\\README.txt", "RÉADME.txt", "LICENSE.txt" }) { f.Write(rename: name => name == "README.txt" ? alias : name); RefuseArchive(() => f.Review()); }
                    f.Write(extra: "foreign.dll"); RefuseArchive(() => f.Review()); f.Write(omit: "LICENSE.txt"); RefuseArchive(() => f.Review()); Check(f.Package.CertificateTrust.Calls == 0, "Unsafe ZIP layout reached trust");
                }
            });
            Test("Windows ZIP refuses SFX trailing data archive comments split disks ZIP64 and excess directories", delegate {
                using (var f = new WindowsArchiveFixture()) foreach (string kind in new[] { "trailing", "preamble", "comment", "disk", "count", "size", "offset", "zip64" }) {
                    f.Write(); byte[] bytes = File.ReadAllBytes(f.Path); int end = bytes.Length - 22;
                    if (kind == "trailing") bytes = bytes.Concat(new byte[] { 1 }).ToArray();
                    else if (kind == "preamble") bytes[0] = 0;
                    else if (kind == "comment") FixtureZipU16(bytes, end + 20, 1);
                    else if (kind == "disk") FixtureZipU16(bytes, end + 4, 1);
                    else if (kind == "count" || kind == "zip64") FixtureZipU16(bytes, end + 10, kind == "count" ? (ushort)7 : UInt16.MaxValue);
                    else FixtureZipU32(bytes, end + (kind == "size" ? 12 : 16), UInt32.MaxValue);
                    File.WriteAllBytes(f.Path, bytes); RefuseArchive(() => f.Review());
                }
            });
            Test("Windows ZIP rejects unsupported entry metadata encryption methods links extras and invalid dates", delegate {
                using (var f = new WindowsArchiveFixture()) foreach (string kind in new[] { "method", "encrypt", "flags", "version", "host", "directory", "symlink", "mode", "extra", "comment", "disk", "internal", "date", "zero", "large", "compressed", "alias-offset" }) {
                    f.Write(); f.Mutate(bytes => { int c = FixtureZipCentral(bytes)[0];
                        if (kind == "method") FixtureZipU16(bytes, c + 10, 9);
                        if (kind == "encrypt" || kind == "flags") FixtureZipU16(bytes, c + 8, kind == "encrypt" ? (ushort)1 : (ushort)0x4000);
                        if (kind == "version") FixtureZipU16(bytes, c + 6, 45);
                        if (kind == "host") bytes[c + 5] = 19;
                        if (kind == "directory") FixtureZipU32(bytes, c + 38, 16);
                        if (kind == "symlink" || kind == "mode") { bytes[c + 5] = 3; FixtureZipU32(bytes, c + 38, kind == "symlink" ? 0xa1ff0000U : 0x91ff0000U); }
                        if (kind == "extra" || kind == "comment" || kind == "disk" || kind == "internal") FixtureZipU16(bytes, c + (kind == "extra" ? 30 : kind == "comment" ? 32 : kind == "disk" ? 34 : 36), 1);
                        if (kind == "date") FixtureZipU16(bytes, c + 14, 0);
                        if (kind == "zero" || kind == "large") FixtureZipU32(bytes, c + 24, kind == "zero" ? 0U : UInt32.MaxValue);
                        if (kind == "compressed") FixtureZipU32(bytes, c + 20, UInt32.MaxValue);
                        if (kind == "alias-offset") FixtureZipU32(bytes, c + 42, 1);
                    }); RefuseArchive(() => f.Review());
                }
            });
            Test("Windows ZIP checks local header and central directory agreement without extraction", delegate {
                using (var f = new WindowsArchiveFixture()) foreach (string kind in new[] { "signature", "name", "name-length", "extra", "flags", "method", "crc", "length", "time", "version", "overlap" }) {
                    f.Write(); f.Mutate(bytes => { int c = FixtureZipCentral(bytes)[0], local = (int)BitConverter.ToUInt32(bytes, c + 42);
                        if (kind == "signature") bytes[local] = 0;
                        else if (kind == "name") bytes[local + 30] ^= 1;
                        else if (kind == "name-length" || kind == "extra") FixtureZipU16(bytes, local + (kind == "extra" ? 28 : 26), 1);
                        else if (kind == "flags" || kind == "method" || kind == "time" || kind == "version") FixtureZipU16(bytes, local + (kind == "flags" ? 6 : kind == "method" ? 8 : kind == "time" ? 10 : 4), 1);
                        else if (kind == "overlap") FixtureZipU32(bytes, FixtureZipCentral(bytes)[1] + 42, 0);
                        else FixtureZipU32(bytes, local + (kind == "crc" ? 14 : 22), 1);
                    }); RefuseArchive(() => f.Review());
                }
            });
            Test("Windows ZIP validates CRC independently of Framework archive reader", delegate {
                using (var f = new WindowsArchiveFixture()) { f.Write(CompressionLevel.NoCompression); f.Mutate(bytes => { int c = FixtureZipCentral(bytes)[2], local = (int)BitConverter.ToUInt32(bytes, c + 42); uint crc = BitConverter.ToUInt32(bytes, c + 16) ^ 1; FixtureZipU32(bytes, c + 16, crc); FixtureZipU32(bytes, local + 14, crc); });
                    using (var file = File.OpenRead(f.Path)) using (var zip = new ZipArchive(file, ZipArchiveMode.Read)) using (var reader = new StreamReader(zip.GetEntry("README.txt").Open())) Check(reader.ReadToEnd().Contains("Disposable test instructions"), "Framework unexpectedly verified CRC; update the regression proof");
                    Check(RefuseArchive(() => f.Review()).Message.Contains("CRC"), "Corrupt CRC was accepted or refusal lost");
                }
            });
            Test("Windows ZIP refuses actual payload SHA mismatch after valid CRC and signature", delegate {
                using (var f = new WindowsArchiveFixture()) { File.AppendAllText(System.IO.Path.Combine(f.Package.Root, "README.txt"), " changed"); f.Write(); Check(RefuseArchive(() => f.Review()).Message.Contains("length disagrees"), "ZIP payload size mutation became approved");
                    string readme = System.IO.Path.Combine(f.Package.Root, "README.txt"); File.WriteAllText(readme, "Disposable test instructions. Not a release or installation.", new UTF8Encoding(false)); byte[] text = File.ReadAllBytes(readme); text[0] ^= 1; File.WriteAllBytes(readme, text); f.Write(); Check(RefuseArchive(() => f.Review()).Message.Contains("SHA-256"), "Same-size ZIP payload mutation became approved");
                }
            });
            Test("Windows ZIP refuses changed signature body unsupported signed platform config and docs", delegate {
                using (var f = new WindowsArchiveFixture()) foreach (string kind in new[] { "body", "signature", "platform", "config", "docs" }) {
                    f.Package.Publish(); if (kind == "body") File.AppendAllText(System.IO.Path.Combine(f.Package.Root, WindowsReleaseManifest.ManifestName), " ");
                    if (kind == "signature") { string p = System.IO.Path.Combine(f.Package.Root, WindowsReleaseManifest.SignatureName); byte[] bytes = File.ReadAllBytes(p); bytes[0] = 0; File.WriteAllBytes(p, bytes); }
                    if (kind == "platform") f.Package.Publish(f.Package.ManifestText().Replace("windows-x64", "macos-x64"));
                    if (kind == "config") { File.WriteAllText(System.IO.Path.Combine(f.Package.Root, WindowsReleaseManifest.Configuration), "<!DOCTYPE configuration SYSTEM 'file:///not-read'><configuration/>"); f.Package.Publish(); }
                    if (kind == "docs") { File.WriteAllText(System.IO.Path.Combine(f.Package.Root, "LICENSE.txt"), ""); f.Package.Publish(); }
                    f.Write(); RefuseArchive(() => f.Review());
                    File.Copy(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Tests).Assembly.Location), WindowsReleaseManifest.Configuration), System.IO.Path.Combine(f.Package.Root, WindowsReleaseManifest.Configuration), true); File.WriteAllText(System.IO.Path.Combine(f.Package.Root, "LICENSE.txt"), "Disposable test licensing text. Not an actual license grant.", new UTF8Encoding(false));
                }
            });
            Test("Windows ZIP trust callbacks cannot retarget retained manifest or signature bytes", delegate {
                using (var f = new WindowsArchiveFixture()) { var trust = new MutatingEnvelopeTrust { Actual = new WindowsManifestTrust(f.Package.CertificateTrust) }; using (var review = f.Review(trust: trust)) { review.Revalidate(CancellationToken.None); Check(trust.Calls == 2, "Fresh trust did not use intact frozen bytes"); } trust.Retarget = true; Check(RefuseArchive(() => f.Review(trust: trust)).Message.Contains("different snapshot"), "Retargeted ZIP authentication passed"); }
            });
            Test("Windows ZIP native untrusted fixture signer refuses without root registration", delegate {
                using (var f = new WindowsArchiveFixture()) { string refusal = RefuseArchive(() => f.Review(trust: new WindowsManifestTrust())).Message; Check(refusal.Contains("0x800B0109"), "Untrusted fixture refusal lost: " + refusal); Check(f.Package.ExecutableTrust.Calls == 0, "ZIP unexpectedly invoked executable trust"); }
            });
            Test("Windows ZIP late certificate refusal and signer mismatch release every held file", delegate {
                using (var f = new WindowsArchiveFixture()) { f.Package.CertificateTrust.Throw = true; RefuseArchive(() => f.Review()); f.Package.CertificateTrust.Throw = false; f.Package.CertificateTrust.Evidence = FixtureSigner(); RefuseArchive(() => f.Review()); File.Move(f.Path, f.Path + ".released"); }
            });
            Test("Windows ZIP cancellation during trust retains contents and releases owned handles", delegate {
                using (var f = new WindowsArchiveFixture()) using (var cancel = new CancellationTokenSource()) { string hash = WindowsSignerEvidence.Hash(File.ReadAllBytes(f.Path)); f.Package.CertificateTrust.During = cancel.Cancel; Throws<OperationCanceledException>(() => f.Review(cancel.Token)); Check(WindowsSignerEvidence.Hash(File.ReadAllBytes(f.Path)) == hash, "Cancelled ZIP review changed contents"); File.Move(f.Path, f.Path + ".released"); }
            });
            Test("Windows ZIP rejects initial or later hardlinks and redirected paths retaining foreign content", delegate {
                using (var f = new WindowsArchiveFixture()) { string alias = System.IO.Path.Combine(f.Package.Files.Source, "archive-alias.keep"); using (var review = f.Review()) { Check(CreateHardLink(alias, f.Path, IntPtr.Zero), "Disposable archive alias failed"); Throws<SafetyException>(() => review.Revalidate(CancellationToken.None)); } RefuseArchive(() => f.Review()); Check(File.Exists(alias), "Archive review removed alias"); }
                using (var f = new WindowsArchiveFixture()) { string junction = System.IO.Path.Combine(f.Package.Files.Root, "archive-redirect"); CreateJunction(f.Package.Files, junction, f.Package.Files.Destination); Throws<SafetyException>(() => new WindowsArchiveReview(System.IO.Path.Combine(junction, "release.zip"), f.Package.Signing.Publisher(), new WindowsManifestTrust(f.Package.CertificateTrust), CancellationToken.None)); Check(f.Package.CertificateTrust.Calls == 0, "Redirect reached trust"); }
            });
            Test("Windows ZIP refuses alias creation during manifest verification retaining the alias", delegate {
                using (var f = new WindowsArchiveFixture()) { string alias = System.IO.Path.Combine(f.Package.Files.Source, "during-trust.keep"); f.Package.CertificateTrust.During = () => Check(CreateHardLink(alias, f.Path, IntPtr.Zero), "During-trust disposable alias failed"); RefuseArchive(() => f.Review()); Check(File.Exists(alias), "ZIP failure removed foreign alias"); File.Move(f.Path, f.Path + ".released"); }
            });
            Test("Windows ZIP keeps bounded download-zone metadata and refuses unlisted streams", delegate {
                using (var f = new WindowsArchiveFixture()) { string zone = "[ZoneTransfer]\r\nZoneId=3\r\n"; FixtureArchiveStream(f, "Zone.Identifier", zone); using (var review = f.Review()) review.Revalidate(CancellationToken.None); Check(FixtureArchiveStream(f, "Zone.Identifier") == zone, "Review stripped download metadata"); FixtureArchiveStream(f, "foreign", "keep"); RefuseArchive(() => f.Review()); Check(FixtureArchiveStream(f, "foreign") == "keep", "Review cleaned an unlisted ZIP stream"); }
            });
            Test("Windows ZIP revalidation refreshes trust and refuses changed or untrusted proof", delegate {
                using (var f = new WindowsArchiveFixture()) using (var review = f.Review()) { f.Package.CertificateTrust.Throw = true; Throws<SafetyException>(() => review.Revalidate(CancellationToken.None)); Check(f.Package.CertificateTrust.Calls == 2, "ZIP used stale trust proof"); }
            });
            Test("Windows ZIP binds independently verified extracted exact package and preserves executable trust requirement", delegate {
                using (var f = new WindowsArchiveFixture()) using (var archive = f.Review()) { Check(RefusePackage(() => f.Package.Review(executable: new WindowsAuthenticode())).Message.Contains("0x800B0100"), "Archive content authentication approved unsigned executable"); using (var package = f.Package.Review()) archive.VerifyExtractedPackage(package, CancellationToken.None); }
            });
            Test("Windows ZIP refuses a separately valid extracted package with different authenticated metadata", delegate {
                using (var f = new WindowsArchiveFixture()) using (var archive = f.Review()) { f.Package.Publish(f.Package.ManifestText().Replace("384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")); using (var package = f.Package.Review()) Check(Throws<SafetyException>(() => archive.VerifyExtractedPackage(package, CancellationToken.None)).Message.Contains("does not match"), "Different valid package became archive's extracted copy"); }
            });
            Test("Windows ZIP closed review releases files and refuses subsequent authentication", delegate {
                using (var f = new WindowsArchiveFixture()) { var review = f.Review(); review.Dispose(); review.Dispose(); Throws<ObjectDisposedException>(() => review.Revalidate(CancellationToken.None)); File.Move(f.Path, f.Path + ".released"); }
            });
        }
        static string FixtureArchiveStream(WindowsArchiveFixture fixture, string name, string text = null) {
            Check(SafePaths.Within(fixture.Path, fixture.Package.Files.Root) && (name == "Zone.Identifier" || name == "foreign"), "Unsupported disposable archive stream");
            using (var handle = Native.CreateFile(fixture.Path + ":" + name, text == null ? 0x80000000U : 0x40000000U, 1, IntPtr.Zero, text == null ? 3U : 2U, 0x00200000, IntPtr.Zero)) {
                if (handle.IsInvalid) throw Native.Error("Cannot access disposable archive stream"); Check(Native.FinalPath(handle).StartsWith(fixture.Path + ":", StringComparison.OrdinalIgnoreCase), "Fixture archive stream resolved elsewhere");
                using (var stream = new FileStream(handle, text == null ? FileAccess.Read : FileAccess.Write)) { if (text != null) { byte[] bytes = Encoding.UTF8.GetBytes(text); stream.Write(bytes, 0, bytes.Length); stream.Flush(true); return text; } using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd(); }
            }
        }
    }
}
