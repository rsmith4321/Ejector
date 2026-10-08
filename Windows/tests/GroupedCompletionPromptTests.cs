using System;
using System.Collections.Generic;
using System.IO;

namespace EasyEject {
    public static partial class Tests {
        static void GroupedCompletionPromptCase(bool hasMedia) {
            foreach (bool multiDisk in new[] { false, true }) using (var f = new Fixture()) {
                var state = State(); var source = state.Volumes[0]; source.Group = StorageGroup.Camera; source.Label = "Internal"; source.Mounts[0] = f.Source;
                string siblingRoot = Path.Combine(f.Root, "other-storage"); Directory.CreateDirectory(siblingRoot);
                int siblingDisk = 7;
                if (multiDisk) {
                    siblingDisk = 8; state.Disks.Add(new Disk { Number = 8, DevInst = 11, InstanceId = "disk-b", Serial = "serial-b", TargetId = "usb-a", TargetDevInst = 20, Ancestors = new List<string> { "disk-b", "usb-a" } });
                }
                state.Volumes.Add(new Volume { Id = "volume-b", Label = "SD Card", Serial = "SD123", Extents = siblingDisk + ":1000:1000", Group = StorageGroup.Camera,
                    Disks = new List<int> { siblingDisk }, Mounts = new List<string> { siblingRoot } });
                f.Put(@"DCIM\PHOTO.JPG", "source photo retained");
                if (hasMedia) f.Put(@"DCIM\VIDEO.MP4", "selected source video");
                File.WriteAllText(Path.Combine(siblingRoot, "OTHER.MP4"), "other storage video retained");
                var profile = DefaultsContractProfile(f, state, new ImportDefaults { AskToEject = true });
                var origin = new ImportCompletionOrigin(state, source.Id, f.Source);
                var result = DefaultsContractImport(f, profile, profile.Resolve(new ImportDefaults { AskToEject = true }));
                var completion = new ImportCompletion(origin, "Fixture camera", result.Files == 0 ? f.Destination : result.Folder, f.Destination, HistoryIdentity(), result.HasSelectedMedia, "Selected-source import result.");
                Check(origin.Plan != null && origin.Plan.Affected.Length == 2 && origin.Plan.Disks.Length == (multiDisk ? 2 : 1), "Fixture did not exercise grouped partitions/LUNs");
                source.Label = "Later label edit";
                using (var dialog = new ImportCompletionDialog(completion, true)) {
                    Check(dialog.Explanation.Text.Contains(hasMedia ? "Import from Internal" : "No matching media on Internal"), "Grouped result does not identify the original selected source");
                    Check(dialog.Explanation.Text.Contains("Other storage may still have files to import") && dialog.Explanation.Text.Contains("SD Card"), "Grouped review omits other-storage import warning/names");
                    Check(!dialog.Explanation.Text.Contains("Later label edit") && dialog.Eject.Text == "Eject Device" && dialog.AcceptButton == dialog.Keep && dialog.CancelButton == dialog.Keep,
                        "Display-only mutation changed source description or grouped Keep/default semantics");
                }
                Check(result.Files == (hasMedia ? 1 : 0) && result.HasSelectedMedia == hasMedia && result.RemovedOriginals == 0 && File.ReadAllText(Path.Combine(siblingRoot, "OTHER.MP4")) == "other storage video retained"
                    && File.ReadAllText(Path.Combine(f.Source, @"DCIM\PHOTO.JPG")) == "source photo retained", "Review broadened import scope or changed source/sibling files");
                if (hasMedia) Check(File.ReadAllText(Saved(result, "VIDEO.MP4")) == "selected source video" && File.ReadAllText(Path.Combine(f.Source, @"DCIM\VIDEO.MP4")) == "selected source video", "Selected original/copy changed");
            }
        }
        static void GroupedCompletionPromptTests() {
            Test("grouped filtered import names its source and warns other partitions or LUNs may still have media", () => GroupedCompletionPromptCase(false));
            Test("grouped successful import names its source and retains other partitions or LUN media with Keep as default", () => GroupedCompletionPromptCase(true));
        }
    }
}
