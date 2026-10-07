using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        static void ImportSelectionTests() {
            foreach (string stage in new[] { "volume resolver", "inventory disk", "mount list", "selection predicate" }) {
                string selectedStage = stage;
                Test("import selection refuses shared identity mutation during " + selectedStage, () => {
                    using (var f = new Fixture()) {
                        f.Put("clip.mp4", "owned selection fixture"); var d = new ScopedDiscovery(f); var state = d.Scan();
                        var expectedVolume = state.Volumes[0]; var expectedDisk = state.Disks[0];
                        bool changed = false;
                        Action mutate = () => {
                            if (changed) return; changed = true;
                            if (selectedStage == "inventory disk") expectedDisk.Serial = "replacement-disk";
                            else if (selectedStage == "mount list") expectedVolume.Mounts.Add(Path.Combine(f.Root, "another-mount"));
                            else expectedVolume.Extents = "replacement-extents";
                        };
                        if (selectedStage == "inventory disk") d.AfterScan = mutate;
                        Func<string, string> resolver = path => {
                            if (selectedStage == "volume resolver" || selectedStage == "mount list") mutate();
                            return path == f.Source ? "source-volume" : "destination-volume";
                        };
                        Func<Inventory, bool> predicate = selectedStage == "selection predicate" ? (Func<Inventory, bool>)(current => { mutate(); return true; }) : null;
                        Throws<SafetyException>(() => new VolumeImportGuard(d, f.Source, f.Destination, expectedVolume, expectedDisk, state.Epoch, resolver, predicate));
                        Check(changed && File.ReadAllText(Path.Combine(f.Source, "clip.mp4")) == "owned selection fixture" && !Directory.EnumerateFileSystemEntries(f.Destination).Any(), "Refusal changed originals or created destination evidence");
                        Check(selectedStage == "inventory disk" ? expectedDisk.Serial == "replacement-disk" : selectedStage == "mount list" ? expectedVolume.Mounts.Count == 2 : expectedVolume.Extents == "replacement-extents", "Refusal rolled back the observed identity");
                        d.AfterScan = null; var fresh = ScopedImportGuard(f, d);
                        SelectionImportProof(f, d, fresh);
                    }
                });
            }
            Test("import selection ignores later mutation of isolated caller records", () => {
                using (var f = new Fixture()) {
                    f.Put("clip.mp4", "owned selection fixture"); var d = new ScopedDiscovery(f); var request = ScopedAutomaticRequest(f, d);
                    var volume = request.Connection.Volume; var disk = request.Connection.Disk;
                    Check(!Object.ReferenceEquals(volume, d.State.Volumes[0]) && !Object.ReferenceEquals(disk, d.State.Disks[0]), "Expected caller records are not isolated");
                    bool changed = false;
                    var guard = new VolumeImportGuard(d, f.Source, f.Destination, volume, disk, d.Epoch, path => {
                        if (!changed) { changed = true; volume.Id = "caller-only-volume"; volume.Serial = "caller-only-serial"; volume.Mounts.Clear(); disk.Serial = "caller-only-disk"; }
                        return path == f.Source ? "source-volume" : "destination-volume";
                    });
                    Check(changed && volume.Id == "caller-only-volume" && d.State.Volumes[0].Id == "source-volume", "Caller mutation retargeted inventory or was rolled back");
                    SelectionImportProof(f, d, guard);
                }
            });
            Test("import selection permits display-only labels and models without relaxing connection proof", () => {
                using (var f = new Fixture()) {
                    f.Put("clip.mp4", "owned selection fixture"); var d = new ScopedDiscovery(f); var state = d.Scan();
                    var guard = new VolumeImportGuard(d, f.Source, f.Destination, state.Volumes[0], state.Disks[0], state.Epoch, path => {
                        state.Volumes[0].Label = "Updated display label"; state.Disks[0].Model = "Updated display model";
                        return path == f.Source ? "source-volume" : "destination-volume";
                    });
                    SelectionImportProof(f, d, guard);
                }
            });
        }
        static void SelectionImportProof(Fixture f, ScopedDiscovery d, VolumeImportGuard guard) {
            Check(guard.ProtectedDisks.SequenceEqual(new[] { 700, 701 }), "Selection changed protected source/destination disks");
            var evidence = (IImportDeviceEvidence)guard;
            Check(evidence.SourceDevice.Matches(DestinationIdentity.Capture(d.State, "source-volume")) && evidence.DestinationDevice.Matches(DestinationIdentity.Capture(d.State, "destination-volume")), "Selection adopted caller-only device evidence");
            d.Event("other-usb"); guard.Validate(); var operations = new Operations();
            var result = new VerifiedImporter(operations, path => UInt64.MaxValue, line => {}).Run(f.Source, f.Source, f.Destination, "source-volume", guard, CancellationToken.None, line => {});
            var entry = result.Receipt.Files.Single(x => x.IsMedia); string original = Path.Combine(f.Source, "clip.mp4");
            Check(result.Files == 1 && result.HasSelectedMedia && File.Exists(result.ReceiptPath) && File.ReadAllText(original) == "owned selection fixture" && File.ReadAllText(Saved(result, "clip.mp4")) == "owned selection fixture" && entry.Sha256 == IndependentFileHash(original), "Import failed to preserve exact generated original/receipt/hash");
            using (operations.Reserve(new[] { 700, 701 })) {}
            d.Event("source-usb"); Throws<SafetyException>(() => guard.Validate());
        }
    }
}
