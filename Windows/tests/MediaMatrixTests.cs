using System;
using System.IO;
using System.Linq;

namespace EasyEject {
    public static partial class Tests {
        static readonly string[] MatrixMasters = { "clip.MP4", "clip.SRT" };
        static readonly string[] MatrixPreviews = { "clip.LRV", "clip.LRF", "clip.THM", "Proxy\\clip.MP4", "Proxies\\extra.MP4" };
        static void PutMatrix(Fixture f, string prefix, string[] paths) { foreach (string path in paths) f.Put(Path.Combine(prefix, path), path); }
        static void CheckMatrixKept(Fixture f, string prefix, string[] paths) { foreach (string path in paths) Check(File.ReadAllText(Path.Combine(f.Source, prefix, path)) == path, "Source changed: " + path); }
        static void CheckMatrixSaved(ImportResult result, string prefix, string[] paths) { foreach (string path in paths) Check(File.ReadAllText(Saved(result, Path.Combine(prefix, path))) == path, "Saved bytes changed: " + path); }
        static void MediaMatrixTests() {
            Test("Mac matrix nested selected folder copies verifies and removes without flattening", delegate { using (var f = new Fixture()) {
                const string relative = @"DJI_001\clip.MP4"; f.Put(Path.Combine("DCIM", relative), new string('I', 200000));
                var result = ParityImport(f, Path.Combine(f.Source, "DCIM"), options: new MediaOptions(false, true, false, false, true));
                Check(result.Files == 1 && result.Bytes == 200000 && result.RemovedOriginals == 1 && result.Receipt.OriginalRemovalRequested && !File.Exists(Path.Combine(f.Source, "DCIM", relative)), "Nested removal/counts incorrect");
                string saved = Saved(result, relative); Check(saved.EndsWith(relative) && File.ReadAllText(saved) == new string('I', 200000), "Selected-relative path or full bytes lost");
            } });
            Test("Mac matrix repeated index-only import reuses one metadata backup and keeps source", delegate { using (var f = new Fixture()) {
                f.Put("fileinfo_list.list", "index fixture"); var first = ParityImport(f, f.Source, options: RemoveOptions()); var second = ParityImport(f, f.Source, options: RemoveOptions());
                Check(first.Files == 1 && second.Files == 1 && second.Reused == 1 && !first.HasSelectedMedia && !second.HasSelectedMedia && first.RemovedOriginals == 0 && second.RemovedOriginals == 0, "Index promoted to media or repeated instead of reused");
                Check(Saved(first, "fileinfo_list.list") == Saved(second, "fileinfo_list.list") && Directory.GetFiles(f.Destination, "fileinfo_list.list", SearchOption.AllDirectories).Length == 1 && File.ReadAllText(Saved(second, "fileinfo_list.list")) == "index fixture" && File.ReadAllText(Path.Combine(f.Source, "fileinfo_list.list")) == "index fixture", "Index duplicated, changed or removed");
            } });
            Test("Mac matrix RAW future format INDEX and hidden metadata remove only recognized RAW", delegate { using (var f = new Fixture()) {
                string[] visible = { "photo.CR3", "motion.futureformat", "INDEX" }; PutMatrix(f, "DCIM", visible); f.Put(@"DCIM\.DS_Store", "hidden metadata");
                var result = ParityImport(f, Path.Combine(f.Source, "DCIM"), options: RemoveOptions(false));
                Check(result.Files == 3 && result.RemovedOriginals == 1 && !File.Exists(Path.Combine(f.Source, "DCIM", "photo.CR3")), "RAW removal broadened or backup incomplete"); CheckMatrixSaved(result, "", visible);
                CheckMatrixKept(f, "DCIM", visible.Skip(1).ToArray()); Check(File.ReadAllText(Path.Combine(f.Source, "DCIM", ".DS_Store")) == "hidden metadata" && Directory.GetFiles(f.Destination, ".DS_Store", SearchOption.AllDirectories).Length == 0, "Hidden metadata copied or removed");
            } });
            Test("Mac matrix clean Insta360 default previews exact lens names separate index repeat and removal", delegate { using (var f = new Fixture()) {
                string[] names = { "VID_20260926_103000_00_001.insv", "VID_20260926_103000_10_001.insv", "LRV_20260926_103000_11_001.lrv" }; PutMatrix(f, @"DCIM\Camera01", names); f.Put(@"DCIM\fileinfo_list.list", "calibration index");
                string selected = Path.Combine(f.Source, "DCIM"); var first = ParityImport(f, selected, options: new MediaOptions(true, true, true)); var repeated = ParityImport(f, selected, options: new MediaOptions(true, true, true));
                Check(first.Files == 4 && repeated.Reused == 4 && first.HasSelectedMedia && repeated.HasSelectedMedia && first.Folder == repeated.Folder, "Lens/index group failed repeat verification");
                foreach (string name in names) Check(Saved(first, Path.Combine("Camera01", name)) == Path.Combine(first.Folder, name) && File.ReadAllText(Path.Combine(first.Folder, name)) == name && Directory.GetFiles(f.Destination, name, SearchOption.AllDirectories).Length == 1, "Lens name flattened incorrectly or duplicated");
                string index = Saved(first, "fileinfo_list.list"); Check(index.Contains(@"\.easy-eject-metadata\") && File.ReadAllText(index) == "calibration index", "Index mixed with clean visible files");
                var removed = ParityImport(f, selected, options: RemoveOptions()); Check(removed.Reused == 4 && removed.RemovedOriginals == 3 && names.All(name => !File.Exists(Path.Combine(selected, "Camera01", name))) && File.ReadAllText(Path.Combine(selected, "fileinfo_list.list")) == "calibration index", "Verified lens removal changed index or missed preview");
            } });
            Test("Mac matrix GoPro LRV removes only exact same-folder prefix-associated master pair", delegate { using (var f = new Fixture()) {
                string[] pair = { "GH010001.MP4", "GL010001.LRV" }; string[] orphans = { @"elsewhere\GL010001.LRV", "GL020002.LRV" }; PutMatrix(f, "DCIM", pair.Concat(orphans).ToArray());
                var result = ParityImport(f, Path.Combine(f.Source, "DCIM"), options: RemoveOptions()); Check(result.Files == 2 && result.RemovedOriginals == 2 && pair.All(path => !File.Exists(Path.Combine(f.Source, "DCIM", path))), "GoPro pair not copied/removed exclusively");
                CheckMatrixSaved(result, "", pair); CheckMatrixKept(f, "DCIM", orphans);
            } });
            Test("Mac matrix default previews include complete ordinary matrix with preserved hierarchy", delegate { using (var f = new Fixture()) {
                string[] paths = MatrixMasters.Concat(MatrixPreviews).ToArray(); PutMatrix(f, "DCIM", paths);
                var result = ParityImport(f, f.Source, options: new ImportDefaults().Snapshot()); Check(result.Files == paths.Length && result.RemovedOriginals == 0 && result.HasSelectedMedia, "Factory preview matrix incomplete");
                CheckMatrixSaved(result, "DCIM", paths); CheckMatrixKept(f, "DCIM", paths); foreach (string path in paths) Check(Saved(result, Path.Combine("DCIM", path)).EndsWith(Path.Combine("DCIM", path)), "Proxy hierarchy lost");
            } });
            foreach (bool videosOnly in new[] { false, true }) { bool videos = videosOnly;
                Test("Mac matrix disabled previews retain every ordinary marker in " + (videos ? "videos-only" : "all-media") + " removal", delegate { using (var f = new Fixture()) {
                    PutMatrix(f, "DCIM", MatrixMasters.Concat(MatrixPreviews).ToArray()); var result = ParityImport(f, f.Source, options: RemoveOptions(videos, false));
                    Check(result.Files == 2 && result.RemovedOriginals == 2 && MatrixMasters.All(path => !File.Exists(Path.Combine(f.Source, "DCIM", path))), "Preview filtering changed master selection/removal"); CheckMatrixSaved(result, "DCIM", MatrixMasters); CheckMatrixKept(f, "DCIM", MatrixPreviews);
                } });
            }
            Test("Mac matrix explicit Mac owner Trash recovery retains every disabled preview marker", delegate { using (var f = new Fixture()) {
                const string owner = @".Trashes\501"; PutMatrix(f, owner, MatrixMasters.Concat(MatrixPreviews).ToArray()); f.Put(@".Trashes\502\other.MP4", "other owner");
                var result = RecoverFixture(f, RecoverOptions(previews: false, macUid: "501")); Check(result.Files == 2 && result.ClearedTrashFiles == 2 && result.RemovedOriginals == 0 && result.ClearedTrashRecords == 0 && MatrixMasters.All(path => !File.Exists(Path.Combine(f.Source, owner, path))), "Mac Trash selection broadened");
                foreach (string path in MatrixMasters) Check(File.ReadAllText(TrashSaved(result, path)) == path, "Recovered bytes changed"); CheckMatrixKept(f, owner, MatrixPreviews); Check(File.ReadAllText(Path.Combine(f.Source, ".Trashes", "502", "other.MP4")) == "other owner", "Other owner's Trash altered");
            } });
            Test("Mac matrix repeated skipped-preview metadata-only import stays no-media and reuses index", delegate { using (var f = new Fixture()) {
                string[] paths = MatrixPreviews.Concat(new[] { "fileinfo_list.list" }).ToArray(); PutMatrix(f, "DCIM", paths); var options = RemoveOptions(true, false);
                var first = ParityImport(f, Path.Combine(f.Source, "DCIM"), options: options); var second = ParityImport(f, Path.Combine(f.Source, "DCIM"), options: options);
                Check(first.Files == 1 && second.Files == 1 && !first.HasSelectedMedia && !second.HasSelectedMedia && second.Reused == 1 && first.RemovedOriginals == 0 && second.RemovedOriginals == 0 && first.Folder == second.Folder, "Skipped previews promoted or duplicate metadata generated");
                CheckMatrixKept(f, "DCIM", paths); Check(File.ReadAllText(Saved(second, "fileinfo_list.list")) == "fileinfo_list.list" && Directory.GetFiles(f.Destination, "fileinfo_list.list", SearchOption.AllDirectories).Length == 1 && second.Receipt.Files.All(entry => !entry.IsMedia), "Index backup identity or no-media receipt incorrect");
            } });
            Test("Mac matrix second copied-file audit failure prevents every opted-in removal", delegate { using (var f = new Fixture()) {
                string[] paths = { "a.mp4", "z.mp4" }; PutMatrix(f, "", paths); int copies = 0;
                Throws<IOException>(() => ParityImport(f, f.Source, options: RemoveOptions(), audit: line => { if (line.StartsWith("Copied verified SHA256", StringComparison.Ordinal) && ++copies == 2) throw new IOException("fixture second copied-file audit failure"); }));
                Check(copies == 2 && paths.All(path => Directory.GetFiles(f.Destination, path, SearchOption.AllDirectories).Length == 1), "Failure did not reach second publication"); CheckMatrixKept(f, "", paths); NoRemovalCompletion(f);
            } });
            Test("Mac matrix absent destination refuses without recreating it or changing original", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "retained original"); Directory.Delete(f.Destination); Throws<Exception>(() => ParityImport(f, f.Source, options: RemoveOptions()));
                Check(!Directory.Exists(f.Destination) && File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == "retained original", "Missing destination recreated or source changed");
            } });
            Test("Mac matrix new selected recording after copy-only publication refuses completion", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "retained original"); bool arrived = false;
                Throws<SafetyException>(() => ParityImport(f, f.Source, options: new MediaOptions(true, true, true), audit: line => { if (line.StartsWith("Copied verified SHA256", StringComparison.Ordinal)) { f.Put("new.mp4", "new recording"); arrived = true; } }));
                Check(arrived && Directory.GetFiles(f.Destination, "clip.mp4", SearchOption.AllDirectories).Length == 1 && File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == "retained original" && File.ReadAllText(Path.Combine(f.Source, "new.mp4")) == "new recording", "Arrival check did not run or changed originals"); NoRemovalCompletion(f);
            } });
            Test("Mac matrix earlier source mutation after publication with deletion disabled refuses completion", delegate { using (var f = new Fixture()) {
                f.Put("a.mp4", "original-a"); f.Put("z.mp4", "original-z"); bool changed = false;
                Throws<SafetyException>(() => ParityImport(f, f.Source, options: new MediaOptions(true, true, true), report: line => { if (line == "Copying z.mp4") { string path = Path.Combine(f.Source, "a.mp4"); DateTime time = File.GetLastWriteTimeUtc(path); File.WriteAllText(path, "changed--a"); File.SetLastWriteTimeUtc(path, time); changed = true; } }));
                Check(changed && File.ReadAllText(Path.Combine(f.Source, "a.mp4")) == "changed--a" && File.ReadAllText(Path.Combine(f.Source, "z.mp4")) == "original-z" && File.ReadAllText(Directory.GetFiles(f.Destination, "a.mp4", SearchOption.AllDirectories).Single()) == "original-a", "Changed original overwritten or source not retained"); NoRemovalCompletion(f);
            } });
        }
    }
}
