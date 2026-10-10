using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    // Windows counterparts of Mac DeletionRaceTests (07de127) cases 1, 2, 4 and 5. Windows deletes
    // through an exclusively held, re-verified handle, so swaps at the last audit step must not land.
    // Mac cases 3 and 6-8 (Trash and metadata cleanup) map to existing Windows tests: "Trash path
    // substitution and links are refused without following targets", "cleanup skips reparse
    // directories and never touches their targets", "cleanup refuses renamed child at validation
    // before removing foreign replacement metadata" and the reparse cleanup-root case in CleanupTests.
    public static partial class Tests {
        static Exception TryRaceSwap(Action swap) { try { swap(); return null; } catch (Exception ex) { return ex; } }
        static void DeletionRaceParityTests() {
            Test("Windows original replaced by a directory at final removal cannot be swapped or deleted recursively", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "Fixture verified original"); string source = Path.Combine(f.Source, "clip.mp4"); Exception swap = null;
                var importer = new VerifiedImporter(new Operations(), Native.FreeSpace, x => { if (x.StartsWith("Before permanent removal")) swap = TryRaceSwap(() => { File.Delete(source); Directory.CreateDirectory(source); File.WriteAllText(Path.Combine(source, "unverified.mp4"), "unverified media"); }); });
                var result = importer.Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, x => {}, RemoveOptions());
                Check(swap is IOException || swap is UnauthorizedAccessException, "Held original could be replaced: " + swap);
                Check(result.RemovedOriginals == 1 && !File.Exists(source) && !Directory.Exists(source) && File.ReadAllText(Directory.GetFiles(f.Destination, "clip.mp4", SearchOption.AllDirectories).Single()) == "Fixture verified original", "Verified original removal differs");
            } });
            Test("Windows original replaced by another regular file at final removal cannot be swapped", delegate { using (var f = new Fixture()) {
                f.Put("clip.mp4", "Fixture verified original"); string source = Path.Combine(f.Source, "clip.mp4"), aside = Path.Combine(f.Root, "aside.mp4"); Exception swap = null;
                var importer = new VerifiedImporter(new Operations(), Native.FreeSpace, x => { if (x.StartsWith("Before permanent removal")) swap = TryRaceSwap(() => { File.Move(source, aside); File.WriteAllText(source, "different unverified file"); }); });
                var result = importer.Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, x => {}, RemoveOptions());
                Check(swap is IOException || swap is UnauthorizedAccessException, "Held original could be moved: " + swap);
                Check(result.RemovedOriginals == 1 && !File.Exists(source) && !File.Exists(aside), "Removal touched a file other than the verified original");
            } });
            Test("Windows ancestor folder swapped for a junction at final removal never deletes outside the card", delegate { using (var f = new Fixture()) {
                f.Put(@"DCIM\clip.mp4", "Fixture verified original"); string folder = Path.Combine(f.Source, "DCIM"), moved = folder + "-moved", outside = Path.Combine(f.Root, "outside-card"), foreign = Path.Combine(outside, "clip.mp4");
                Directory.CreateDirectory(outside); File.WriteAllText(foreign, "foreign file outside the card"); Exception swap = null;
                var importer = new VerifiedImporter(new Operations(), Native.FreeSpace, x => { if (x.StartsWith("Before permanent removal")) swap = TryRaceSwap(() => { Directory.Move(folder, moved); CreateJunction(f, folder, outside); }); });
                Exception run = TryRaceSwap(() => importer.Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, x => {}, RemoveOptions()));
                Check(File.Exists(foreign) && File.ReadAllText(foreign) == "foreign file outside the card", "Deletion reached outside the card through a junction");
                if (swap == null) Check(run is OriginalRemovalFailure && File.ReadAllText(Path.Combine(moved, "clip.mp4")) == "Fixture verified original", "Swapped ancestor was not refused with the original kept");
                else Check(run == null && !File.Exists(Path.Combine(folder, "clip.mp4")), "Blocked ancestor swap changed verified removal");
            } });
        }
    }
}
