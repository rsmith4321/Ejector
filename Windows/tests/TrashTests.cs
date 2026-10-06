using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        const string FixtureTrashSid = "S-1-5-21-700-701-702-1001";
        static byte[] RecycleRecord(string originalPath, long bytes, ulong version = 2) {
            byte[] path = Encoding.Unicode.GetBytes(originalPath + '\0');
            byte[] data = new byte[version == 1 ? 544 : 28 + path.Length];
            Array.Copy(BitConverter.GetBytes(version), data, 8); Array.Copy(BitConverter.GetBytes(bytes), 0, data, 8, 8);
            Array.Copy(BitConverter.GetBytes(new DateTime(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc).ToFileTimeUtc()), 0, data, 16, 8);
            if (version != 1) Array.Copy(BitConverter.GetBytes((uint)(path.Length / 2)), 0, data, 24, 4);
            Array.Copy(path, 0, data, version == 1 ? 24 : 28, path.Length); return data;
        }
        static string PutRecycle(Fixture fixture, string suffix, string originalPath, string text, ulong version = 2) {
            string owner = Path.Combine(fixture.Source, "$RECYCLE.BIN", FixtureTrashSid); Directory.CreateDirectory(owner);
            string payload = Path.Combine(owner, "$R" + suffix), metadata = Path.Combine(owner, "$I" + suffix);
            File.WriteAllText(payload, text); File.WriteAllBytes(metadata, RecycleRecord(originalPath, new FileInfo(payload).Length, version)); return payload;
        }
        static DeviceTrashSnapshot ReadTrash(Fixture fixture, string macUid = null) { return DeviceTrash.Read(fixture.Source, FixtureTrashSid, macUid, CancellationToken.None, () => {}); }
        static void TrashCatalogTests() {
            Test("absent device Trash is distinguished from invalid owner configuration", delegate { using (var f = new Fixture()) {
                var empty = ReadTrash(f); Check(empty.Files.Count == 0 && empty.Records.Count == 0 && empty.WindowsOwnerRoot == null && empty.MacOwnerRoot == null, "Missing Trash fabricated files");
                Throws<SafetyException>(() => DeviceTrash.Read(f.Source, @"..\other-user", null, CancellationToken.None, () => {}));
                foreach (string owner in new[] { "../501", "501\\other", "0501", "-1", "4294967296", "" }) Throws<SafetyException>(() => ReadTrash(f, owner));
                Check(DeviceTrash.ValidMacOwner("0") && DeviceTrash.ValidMacOwner("501") && DeviceTrash.ValidMacOwner("4294967295"), "Canonical explicit UIDs refused");
            } });
            Test("Windows Trash maps randomized payloads back to original Unicode names and old-drive paths", delegate { using (var f = new Fixture()) {
                string a = PutRecycle(f, "ABC001.mp4", @"Z:\DCIM\VID_20261006_123456_00_001.mp4", "Fixture full-quality recording", 1);
                string b = PutRecycle(f, "XYZ002.xmp", "Z:\\DCIM\\r\u00e9sum\u00e9.xmp", "Fixture sidecar");
                var snapshot = ReadTrash(f); Check(snapshot.Files.Count == 2 && snapshot.Records.Count == 2 && snapshot.Files.Single(x => x.ActualPath == a).LogicalPath.EndsWith(@"Windows Trash\DCIM\VID_20261006_123456_00_001.mp4") && snapshot.Files.Single(x => x.ActualPath == b).LogicalPath.EndsWith("r\u00e9sum\u00e9.xmp"), "Payload token or current drive replaced original name");
                Check(snapshot.Records.All(x => x.MetadataSha256.Length == 64 && x.Original.DeletedUtc.Kind == DateTimeKind.Utc) && File.ReadAllText(a) == "Fixture full-quality recording" && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Read-only catalog mutated data or lost metadata proof");
            } });
            Test("Trash catalog uses only selected Windows SID and explicit Mac UID without guessing owners", delegate { using (var f = new Fixture()) {
                PutRecycle(f, "selected.mp4", @"E:\clip.mp4", "Fixture selected owner");
                f.Put(@"$RECYCLE.BIN\S-1-5-21-999-998-997-1002\$IBROKEN", "Invalid other-user metadata");
                f.Put(@".Trashes\501\mine.mp4", "Fixture selected Mac owner"); f.Put(@".Trashes\502\other.mp4", "Fixture other Mac owner");
                var windowsOnly = ReadTrash(f); var selected = ReadTrash(f, "501");
                Check(windowsOnly.Files.Count == 1 && windowsOnly.MacOwnerRoot == null && selected.Files.Count == 2 && selected.Files.Any(x => x.LogicalPath.EndsWith(@"Mac Trash\mine.mp4")) && !selected.Files.Any(x => x.ActualPath.Contains("502")), "Guessed or swept another Trash owner");
                Check(Directory.GetFiles(f.Source, "*", SearchOption.AllDirectories).Length == 5 && File.ReadAllText(Path.Combine(f.Source, @".Trashes\502\other.mp4")) == "Fixture other Mac owner", "Owner catalog cleared data");
            } });
            Test("original-path Trash selection preserves Live Photos and ambiguous sidecars across separate records", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { "clip.mp4", "clip.mp4.xmp", "clip.xmp", "clip.bin", "live.JPG", "live.MOV", "live.XMP", "unknown.data" }) PutRecycle(f, Guid.NewGuid().ToString("N") + Path.GetExtension(name), @"E:\DCIM\" + name, "Fixture " + name);
                var selected = ReadTrash(f).Select(new MediaOptions(true, true));
                Check(selected.Files.Count == 2 && selected.Files.Any(x => x.LogicalPath.EndsWith("clip.mp4")) && selected.Files.Any(x => x.LogicalPath.EndsWith("clip.mp4.xmp")) && selected.RemovablePayloads.Count == 2 && selected.RemovableRecords.Count == 2, "Random payload names broke conservative video companion selection");
                Check(selected.Records.Count == 2 && Directory.GetFiles(f.Source, "*", SearchOption.AllDirectories).Length == 16, "Unselected metadata joined the batch or catalog cleared data");
            } });
            Test("Trash selection keeps complete lens groups but leaves previews when disabled", delegate { using (var f = new Fixture()) {
                string[] names = { "VID_20261006_123456_00_001.insv", "VID_20261006_123456_10_001.insv", "LRV_20261006_123456_11_001.lrv" };
                for (int i = 0; i < names.Length; i++) PutRecycle(f, "record" + i, @"E:\DCIM\" + names[i], "Fixture " + names[i]);
                var snapshot = ReadTrash(f); Check(snapshot.Select(new MediaOptions(true, true)).Files.Count == 3 && snapshot.Select(new MediaOptions(true, false)).Files.Count == 2 && snapshot.Select(new MediaOptions(true, false)).RemovableRecords.Count == 2, "Lens/preview names were inferred from payload tokens");
            } });
            Test("deleted camera folder catalog preserves package ancestry audio and required previews", delegate { using (var f = new Fixture()) {
                string owner = Path.Combine(f.Source, "$RECYCLE.BIN", FixtureTrashSid), payload = Path.Combine(owner, "$RFOLDER"); Directory.CreateDirectory(payload);
                foreach (string name in new[] { @"M4ROOT\CLIP\clip.MXF", @"M4ROOT\CLIP\clip.XML", @"M4ROOT\AUDIO\audio.WAV", @"M4ROOT\THMBNL\clip.JPG" }) { string path = Path.Combine(payload, name); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "Fixture " + name); }
                File.WriteAllBytes(Path.Combine(owner, "$IFOLDER"), RecycleRecord(@"E:\PRIVATE", 0));
                var snapshot = ReadTrash(f); var selected = snapshot.Select(new MediaOptions(true, false));
                Check(selected.Structured && selected.Files.Count == 4 && selected.Records.Count == 1 && selected.RemovablePayloads.Count == 2 && selected.RemovableRecords.Count == 0 && snapshot.Files.All(x => x.LogicalPath.Contains(@"Windows Trash\PRIVATE\M4ROOT")), "Deleted package support/ancestry dropped or folder record scheduled before retained contents");
            } });
            Test("structured Trash without a complete package refuses video-only recovery", delegate { using (var f = new Fixture()) {
                PutRecycle(f, "PARTIAL", @"E:\orphan.MXF", "Fixture structured recording"); var snapshot = ReadTrash(f);
                Throws<SafetyException>(() => snapshot.Select(new MediaOptions(true, true))); Check(snapshot.Select(new MediaOptions(false, true)).Files.Count == 1, "All-media package fallback lost original");
            } });
            Test("all-media Trash plan copies unknowns but never schedules unknown indexes or orphan companions for removal", delegate { using (var f = new Fixture()) {
                foreach (string name in new[] { "photo.JPG", "photo.XMP", "unknown.bin", "orphan.LRV", "fileinfo_list.list" }) PutRecycle(f, Guid.NewGuid().ToString("N"), @"E:\" + name, "Fixture " + name);
                var selected = ReadTrash(f).Select(new MediaOptions(false, true));
                Check(selected.Files.Count == 4 && selected.RemovablePayloads.Count == 2 && selected.RemovableRecords.Count == 2 && selected.Files.All(x => !MediaSelection.IsDeviceIndex(x.LogicalPath)), "Unknown/index classified for permanent Trash clearing");
            } });
            Test("explicit Mac Trash includes hidden files and preserves distinct Windows/Mac original namespaces", delegate { using (var f = new Fixture()) {
                f.Put(@".Trashes\501\.hidden.mp4", "Fixture hidden recording"); f.Put(@".Trashes\501\DCIM\clip.mp4", "Fixture Mac original"); PutRecycle(f, "CLIP", @"E:\DCIM\clip.mp4", "Fixture Windows original");
                var snapshot = ReadTrash(f, "501"); var selected = snapshot.Select(new MediaOptions(true, true));
                Check(selected.Files.Count == 3 && selected.RemovablePayloads.Count == 3 && selected.Records.Count == 1 && selected.Files.Single(x => x.RecordPath == null && x.LogicalPath.EndsWith("clip.mp4")).LogicalPath.Contains("Mac Trash"), "Hidden Trash or same name across owners lost");
            } });
            Test("duplicate original paths and Unicode-equivalent names refuse ambiguous recovery", delegate {
                foreach (bool unicode in new[] { false, true }) using (var f = new Fixture()) {
                    PutRecycle(f, "FIRST", unicode ? "E:\\\u00e9.mp4" : @"E:\clip.mp4", "Fixture first");
                    PutRecycle(f, "SECOND", unicode ? "E:\\e\u0301.mp4" : @"E:\clip.mp4", "Fixture second");
                    Check(Throws<SafetyException>(() => ReadTrash(f)).Message.Contains("duplicate original names") && Directory.GetFiles(f.Source, "*", SearchOption.AllDirectories).Length == 4, "Ambiguous originals flattened or guessed");
                }
            });
            Test("Trash path substitution and links are refused without following targets", delegate { using (var f = new Fixture()) {
                Directory.CreateDirectory(Path.Combine(f.Source, "$RECYCLE.BIN")); string target = Path.Combine(f.Destination, "outside"); Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "private.mp4"), "Fixture outside Trash");
                CreateJunction(f, Path.Combine(f.Source, "$RECYCLE.BIN", FixtureTrashSid), target);
                Throws<SafetyException>(() => ReadTrash(f)); Check(File.ReadAllText(Path.Combine(target, "private.mp4")) == "Fixture outside Trash", "Owner link followed or target touched");
            } });
            Test("payload links and selected Mac Trash links are refused without scanning other owners", delegate {
                using (var f = new Fixture()) {
                    string owner = Path.Combine(f.Source, "$RECYCLE.BIN", FixtureTrashSid); Directory.CreateDirectory(owner); File.WriteAllBytes(Path.Combine(owner, "$ILINK"), RecycleRecord(@"E:\PRIVATE", 0));
                    CreateJunction(f, Path.Combine(owner, "$RLINK"), f.Destination); Throws<SafetyException>(() => ReadTrash(f));
                }
                using (var f = new Fixture()) { Directory.CreateDirectory(Path.Combine(f.Source, ".Trashes")); CreateJunction(f, Path.Combine(f.Source, @".Trashes\501"), f.Destination); Check(ReadTrash(f).Files.Count == 0, "Unselected Mac owner traversed"); Throws<SafetyException>(() => ReadTrash(f, "501")); }
            });
            Test("metadata/payload size mismatches and orphan records stop rather than reporting an empty Trash", delegate {
                using (var f = new Fixture()) { string payload = PutRecycle(f, "SIZE", @"E:\clip.mp4", "Fixture bytes"); File.AppendAllText(payload, "changed"); Check(Throws<SafetyException>(() => ReadTrash(f)).Message.Contains("size"), "Mismatched payload accepted"); }
                using (var f = new Fixture()) { string payload = PutRecycle(f, "MISSING", @"E:\clip.mp4", "Fixture bytes"); File.Delete(payload); Check(Throws<SafetyException>(() => ReadTrash(f)).Message.Contains("no unique payload"), "Missing payload treated as absent Trash"); }
                using (var f = new Fixture()) { f.Put(Path.Combine("$RECYCLE.BIN", FixtureTrashSid, "$RORPHAN"), "Fixture orphan"); Check(Throws<SafetyException>(() => ReadTrash(f)).Message.Contains("orphaned"), "Orphan payload guessed from token"); }
            });
            Test("unsupported truncated and inconsistent Recycle Bin metadata never yields a usable original path", delegate {
                byte[] good = RecycleRecord(@"E:\clip.mp4", 7); var cases = new List<byte[]> { new byte[0], new byte[25], new byte[4097], good.Take(good.Length - 1).ToArray() };
                byte[] version = (byte[])good.Clone(); version[0] = 3; cases.Add(version);
                byte[] size = (byte[])good.Clone(); Array.Copy(BitConverter.GetBytes(UInt64.MaxValue), 0, size, 8, 8); cases.Add(size);
                byte[] time = (byte[])good.Clone(); Array.Copy(BitConverter.GetBytes(Int64.MaxValue), 0, time, 16, 8); cases.Add(time);
                byte[] count = (byte[])good.Clone(); Array.Copy(BitConverter.GetBytes(UInt32.MaxValue), 0, count, 24, 4); cases.Add(count);
                byte[] noTerminator = (byte[])good.Clone(); noTerminator[noTerminator.Length - 2] = 1; cases.Add(noTerminator);
                byte[] surrogate = (byte[])good.Clone(); surrogate[28] = 0; surrogate[29] = 0xD8; cases.Add(surrogate);
                foreach (byte[] data in cases) Throws<SafetyException>(() => WindowsTrashRecordInfo.Parse(data));
            });
            Test("record paths cannot escape through UNC devices streams traversal or ambiguous components", delegate {
                foreach (string path in new[] { @"\\server\share\clip.mp4", @"\\?\E:\clip.mp4", @"E:\DCIM\..\clip.mp4", @"E:\clip.mp4:stream", @"E:\DCIM.\clip.mp4", @"E:/clip.mp4", @"E:\" }) Throws<SafetyException>(() => WindowsTrashRecordInfo.Parse(RecycleRecord(path, 1)));
            });
            Test("catalog cancellation and failed device guards leave all payloads and records intact", delegate { using (var f = new Fixture()) using (var cancellation = new CancellationTokenSource()) {
                string payload = PutRecycle(f, "KEEP", @"E:\clip.mp4", "Fixture retained"); cancellation.Cancel();
                Throws<OperationCanceledException>(() => DeviceTrash.Read(f.Source, FixtureTrashSid, null, cancellation.Token, () => {}));
                int checks = 0; Throws<SafetyException>(() => DeviceTrash.Read(f.Source, FixtureTrashSid, null, CancellationToken.None, () => { if (++checks == 5) throw new SafetyException("Fixture disconnected"); }));
                Check(checks == 5 && File.ReadAllText(payload) == "Fixture retained" && Directory.GetFiles(f.Source, "*", SearchOption.AllDirectories).Length == 2, "Read-only failure cleared or lost data");
            } });
            Test("files in place of selected Trash directories fail without being mistaken for absent Trash", delegate {
                using (var f = new Fixture()) { f.Put("$RECYCLE.BIN", "Fixture wrong type"); Throws<SafetyException>(() => ReadTrash(f)); Check(File.ReadAllText(Path.Combine(f.Source, "$RECYCLE.BIN")) == "Fixture wrong type", "Wrong-type Trash altered"); }
                using (var f = new Fixture()) { f.Put(@".Trashes\501", "Fixture wrong owner type"); Check(ReadTrash(f).Files.Count == 0, "Unselected Mac owner inspected"); Throws<SafetyException>(() => ReadTrash(f, "501")); }
            });
            Test("catalog preserves captured original metadata hashes and payload identities after later mutation", delegate { using (var f = new Fixture()) {
                string payload = PutRecycle(f, "CAPTURE", @"E:\clip.mp4", "Fixture recording"); var snapshot = ReadTrash(f); var record = snapshot.Records.Single(); string before = record.MetadataSha256;
                File.WriteAllBytes(record.MetadataPath, RecycleRecord(@"E:\renamed.mp4", new FileInfo(payload).Length));
                Check(snapshot.Files.Single().LogicalPath.EndsWith("clip.mp4") && snapshot.Files.Single().Stamp.IndexLow != 0 && snapshot.Records.Single().MetadataSha256 == before && ReadTrash(f).Records.Single().MetadataSha256 != before, "Frozen mapping followed changed metadata or omitted identity/hash proof");
            } });
            Test("catalog snapshots expose immutable collections and preserve selected metadata dependencies", delegate { using (var f = new Fixture()) {
                PutRecycle(f, "COPY", @"E:\clip.mp4", "Fixture recording"); var snapshot = ReadTrash(f); var selection = snapshot.Select(new MediaOptions(true, true));
                Throws<NotSupportedException>(() => ((IList<TrashSourceFile>)snapshot.Files).Clear()); Throws<NotSupportedException>(() => ((IList<TrashRecord>)selection.Records).Clear());
                Check(selection.Files.Single().RecordPath == selection.Records.Single().MetadataPath && selection.Records.Single().MetadataSha256.Length == 64 && selection.RemovableRecords.Count == 1, "Metadata dependency detached from exact payload");
            } });
        }
    }
}
