using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static ImportResult ParityImport(Fixture f, string selected, IImportGuard guard = null, Action<string> report = null, MediaOptions options = null, Action<string> audit = null) {
            return new VerifiedImporter(new Operations(), path => UInt64.MaxValue, audit ?? (line => {})).Run(f.Source, selected, f.Destination, "fixture-volume", guard ?? new FakeGuard(), CancellationToken.None, report ?? (line => {}), options ?? new MediaOptions(false, true));
        }
        sealed class EmptySelectionGuard : IImportGuard {
            internal Action<int> Check; int count;
            public int[] ProtectedDisks { get { return new[] { 700, 701 }; } }
            public void Validate() { if (Check != null) Check(++count); }
        }
        static void MediaParityTests() {
            Test("Mac parity readable empty card with missing recording folder reports no media without creating folders", delegate {
                using (var f = new Fixture()) {
                    string selected = Path.Combine(f.Source, "DCIM", "recordings"); var result = ParityImport(f, selected);
                    Check(result.Files == 0 && !result.HasSelectedMedia && result.Receipt == null && !Directory.Exists(Path.Combine(f.Source, "DCIM")) && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Empty source created media folders or a misleading batch");
                }
            });
            Test("Mac parity missing recording folder permits root device indexes but keeps them unchanged", delegate {
                using (var f = new Fixture()) {
                    f.Put("fileinfo_list.list", "device index"); string selected = Path.Combine(f.Source, "DCIM"); var result = ParityImport(f, selected);
                    Check(result.Files == 0 && !result.HasSelectedMedia && File.ReadAllText(Path.Combine(f.Source, "fileinfo_list.list")) == "device index" && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Root index was removed or mistaken for selected media");
                }
            });
            Test("Mac parity missing recording folder on a nonempty card refuses rather than importing elsewhere", delegate {
                using (var f = new Fixture()) {
                    f.Put("elsewhere.mp4", "kept recording"); Throws<Exception>(() => ParityImport(f, Path.Combine(f.Source, "DCIM")));
                    Check(File.ReadAllText(Path.Combine(f.Source, "elsewhere.mp4")) == "kept recording" && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Missing selection broadened to other media");
                }
            });
            Test("missing recording folder cannot call a photos-only or unknown nonempty card empty", delegate {
                foreach (string name in new[] { "photos/photo.jpg", "unfamiliar.bin" }) using (var f = new Fixture()) {
                    f.Put(name, "kept elsewhere"); Throws<Exception>(() => ParityImport(f, Path.Combine(f.Source, "DCIM")));
                    Check(File.ReadAllText(Path.Combine(f.Source, name.Replace('/', '\\'))) == "kept elsewhere" && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Unselected content mistaken for an empty card");
                }
            });
            Test("missing selection cannot hide a file where its recording directory should be", delegate {
                using (var f = new Fixture()) {
                    f.Put("DCIM", "not a folder"); Throws<Exception>(() => ParityImport(f, Path.Combine(f.Source, "DCIM", "recordings")));
                    Check(File.ReadAllText(Path.Combine(f.Source, "DCIM")) == "not a folder" && Directory.GetFileSystemEntries(f.Destination).Length == 0, "File-shaped selection called empty or changed");
                }
            });
            Test("missing selection refuses a redirected existing ancestor without following its target", delegate {
                using (var f = new Fixture()) {
                    string target = Path.Combine(f.Root, "separate-target"); Directory.CreateDirectory(target); string link = Path.Combine(f.Source, "DCIM"); CreateJunction(f, link, target);
                    Throws<SafetyException>(() => ParityImport(f, Path.Combine(link, "recordings"))); Check(Directory.GetFileSystemEntries(target).Length == 0 && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Redirected selection followed or populated");
                }
            });
            Test("missing empty selection rechecks new ordinary recordings before returning no media", delegate {
                using (var f = new Fixture()) {
                    var guard = new EmptySelectionGuard { Check = count => { if (count == 2) f.Put("new.mp4", "arrived during empty review"); } };
                    Throws<SafetyException>(() => ParityImport(f, Path.Combine(f.Source, "DCIM"), guard)); Check(File.ReadAllText(Path.Combine(f.Source, "new.mp4")) == "arrived during empty review" && Directory.GetFileSystemEntries(f.Destination).Length == 0, "New recording was ignored or touched");
                }
            });
            Test("missing empty selection refuses a recording folder that appears during final review", delegate {
                using (var f = new Fixture()) {
                    string selected = Path.Combine(f.Source, "DCIM"); var guard = new EmptySelectionGuard { Check = count => { if (count == 2) Directory.CreateDirectory(selected); } };
                    Throws<SafetyException>(() => ParityImport(f, selected, guard)); Check(Directory.Exists(selected) && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Changed folder accepted as the earlier missing selection");
                }
            });
            Test("native volume resolution for an owned missing recording path retains the source volume identity", delegate {
                using (var f = new Fixture()) Check(WindowsDiscovery.VolumeId(Path.Combine(f.Source, "DCIM", "absent")) == WindowsDiscovery.VolumeId(f.Source), "Missing child cannot retain source volume identity");
            });
            Test("unreadable media-parent enumeration never becomes an empty-card result and releases directory pins", delegate {
                using (var f = new Fixture()) {
                    Throws<UnauthorizedAccessException>(() => { using (var folder = new ImportMediaFolder(f.Source, Path.Combine(f.Source, "DCIM"), path => { throw new UnauthorizedAccessException("fixture directory read refused"); })) {} });
                    string moved = Path.Combine(f.Root, "source-after-refusal"); Directory.Move(f.Source, moved); Check(Directory.Exists(moved) && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Enumeration refusal hid an empty result or leaked pins");
                }
            });
            Test("existing empty selection rechecks arrivals before returning no media", delegate {
                using (var f = new Fixture()) {
                    var guard = new EmptySelectionGuard { Check = count => { if (count == 2) f.Put("new.mp4", "arrived in empty folder"); } };
                    Throws<SafetyException>(() => ParityImport(f, f.Source, guard)); Check(File.ReadAllText(Path.Combine(f.Source, "new.mp4")) == "arrived in empty folder" && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Existing empty selection ignored arrival");
                }
            });
            Test("renamed and replaced empty selection ancestor refuses no-media completion and releases its proofs", delegate {
                using (var f = new Fixture()) {
                    string parent = Path.Combine(f.Source, "DCIM", "Camera"); Directory.CreateDirectory(parent); string moved = Path.Combine(f.Source, "Camera-renamed"); bool changed = false;
                    var guard = new EmptySelectionGuard { Check = count => { if (count == 2) { Directory.Move(parent, moved); Directory.CreateDirectory(parent); changed = true; } } };
                    Throws<SafetyException>(() => ParityImport(f, Path.Combine(parent, "recordings"), guard)); Check(changed && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Replaced empty ancestor became a successful no-media result"); Directory.Delete(parent); Directory.Move(moved, parent); Check(Directory.Exists(parent), "Directory proofs leaked after refusal");
                }
            });
            Test("cancelled missing selection creates no source folder or destination evidence", delegate {
                using (var f = new Fixture()) using (var stop = new CancellationTokenSource()) {
                    stop.Cancel(); string selected = Path.Combine(f.Source, "DCIM"); var operations = new Operations();
                    Throws<OperationCanceledException>(() => new VerifiedImporter(operations).Run(f.Source, selected, f.Destination, "fixture-volume", new FakeGuard(), stop.Token, line => {}));
                    Check(!Directory.Exists(selected) && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Cancelled empty selection created evidence"); using (operations.Reserve(new[] { 700, 701 })) {}
                }
            });
            Test("Mac parity Sony full-card and M4ROOT selections retain every required dependency with previews disabled", delegate {
                foreach (bool packageSelected in new[] { false, true }) using (var f = new Fixture()) {
                    string[] paths = { "M4ROOT/CLIP/C0001.MP4", "M4ROOT/CLIP/C0001M01.XML", "M4ROOT/MEDIAPRO.XML", "M4ROOT/THMB/C0001.THM", "M4ROOT/Proxy/C0001.MP4" };
                    foreach (string path in paths) f.Put(path, path); f.Put("DCIM/photo.ARW", "photo retained");
                    var result = ParityImport(f, packageSelected ? Path.Combine(f.Source, "M4ROOT") : f.Source, options: new MediaOptions(true, false, true));
                    Check(result.Files == paths.Length && result.HasSelectedMedia, "Required Sony dependencies skipped");
                    foreach (string path in paths) { string suffix = path.Replace('/', '\\'); var entries = result.Receipt.Files.Where(x => Path.Combine(result.Folder, x.RelativePath).EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).ToArray(); Check(entries.Length == 1 && File.ReadAllText(Path.Combine(result.Folder, entries[0].RelativePath)) == path && File.ReadAllText(Path.Combine(f.Source, suffix)) == path, "Sony package path/content changed: " + path); }
                    Check(File.ReadAllText(Path.Combine(f.Source, "DCIM", "photo.ARW")) == "photo retained", "Unselected photo changed");
                }
            });
            Test("Mac parity same-name GoPro master and Proxy MP4 retain distinct hierarchy and bytes", delegate {
                using (var f = new Fixture()) {
                    string master = Path.Combine("DCIM", "100GOPRO", "GX010001.MP4"), proxy = Path.Combine("DCIM", "100GOPRO", "Proxy", "GX010001.MP4"); f.Put(master, "full-quality fixture"); f.Put(proxy, "smaller proxy fixture");
                    var result = ParityImport(f, f.Source, options: new MediaOptions(true, true, true)); string savedMaster = Saved(result, master), savedProxy = Saved(result, proxy);
                    Check(result.Files == 2 && savedMaster != savedProxy && savedMaster.EndsWith(master) && savedProxy.EndsWith(proxy) && File.ReadAllText(savedMaster) == "full-quality fixture" && File.ReadAllText(savedProxy) == "smaller proxy fixture", "Proxy flattened into or replaced master");
                }
            });
            Test("Mac parity orphan LRV is excluded in videos-only and retained after opted-in all-media backup", delegate {
                using (var f = new Fixture()) {
                    f.Put("orphan.LRV", "unpaired proxy fixture"); var skipped = ParityImport(f, f.Source, options: new MediaOptions(true, true, true, true)); Check(skipped.Files == 0 && !skipped.HasSelectedMedia, "Orphan treated as a selected master");
                    var backed = ParityImport(f, f.Source, options: new MediaOptions(false, true, true, true)); Check(backed.Files == 1 && backed.RemovedOriginals == 0 && File.ReadAllText(Saved(backed, "orphan.LRV")) == "unpaired proxy fixture" && File.ReadAllText(Path.Combine(f.Source, "orphan.LRV")) == "unpaired proxy fixture", "Unrecognized orphan was deleted or not backed up");
                }
            });
            Test("Mac parity late Live Photo companion prevents opted-in video removal", delegate {
                using (var f = new Fixture()) {
                    f.Put("live.MOV", "movie retained"); bool arrived = false;
                    Throws<SafetyException>(() => ParityImport(f, f.Source, options: new MediaOptions(true, true, true, true), audit: line => { if (line.StartsWith("Copied verified SHA256", StringComparison.Ordinal)) { f.Put("live.HEIC", "photo arrived"); arrived = true; } }));
                    Check(arrived && File.ReadAllText(Path.Combine(f.Source, "live.MOV")) == "movie retained" && File.ReadAllText(Path.Combine(f.Source, "live.HEIC")) == "photo arrived" && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Late photo association lost an original or allowed completion");
                }
            });
            Test("Mac parity lost saved Sony companion prevents every opted-in original disposition", delegate {
                using (var f = new Fixture()) {
                    string[] paths = { "M4ROOT/CLIP/C0001.MP4", "M4ROOT/CLIP/C0001M01.XML", "M4ROOT/MEDIAPRO.XML" }; foreach (string path in paths) f.Put(path, path); int copies = 0; bool removed = false;
                    Throws<Exception>(() => ParityImport(f, f.Source, options: new MediaOptions(true, true, true, true), audit: line => { if (line.StartsWith("Copied verified SHA256", StringComparison.Ordinal) && ++copies == paths.Length) { File.Delete(Directory.GetFiles(f.Destination, "C0001M01.XML", SearchOption.AllDirectories).Single()); removed = true; } }));
                    Check(removed && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Companion loss not exercised or batch completed"); foreach (string path in paths) Check(File.ReadAllText(Path.Combine(f.Source, path.Replace('/', '\\'))) == path, "Original disposed despite incomplete saved package");
                }
            });
            Test("Mac parity same-size saved-copy mutation with restored time prevents all original removal", delegate {
                using (var f = new Fixture()) {
                    f.Put("a.mp4", "original-a"); f.Put("z.mp4", "original-z"); int copies = 0; bool changed = false;
                    Throws<SafetyException>(() => ParityImport(f, f.Source, options: new MediaOptions(true, true, true, true), audit: line => { if (line.StartsWith("Copied verified SHA256", StringComparison.Ordinal) && ++copies == 2) { string saved = Directory.GetFiles(f.Destination, "a.mp4", SearchOption.AllDirectories).Single(); DateTime time = File.GetLastWriteTimeUtc(saved); File.WriteAllText(saved, "changed--a"); File.SetLastWriteTimeUtc(saved, time); changed = true; } }));
                    Check(changed && File.ReadAllText(Path.Combine(f.Source, "a.mp4")) == "original-a" && File.ReadAllText(Path.Combine(f.Source, "z.mp4")) == "original-z" && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0, "Restored stamp bypassed batch verification");
                }
            });
        }
    }
}
