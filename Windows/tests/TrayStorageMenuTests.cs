using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        static Inventory TrayState() {
            var state = State(); state.Volumes[0].Group = StorageGroup.Camera; state.Volumes[0].Label = "Travel camera";
            state.Disks.Add(new Disk { Number = 8, DevInst = 11, InstanceId = "disk-b", Serial = "serial-b", TargetId = "usb-b", TargetDevInst = 21, Ancestors = new List<string> { "disk-b", "usb-b" } });
            state.Volumes.Add(new Volume { Id = "volume-b", Label = "Backup SSD", Serial = "DEF", Disks = new List<int> { 8 }, Mounts = new List<string> { @"F:\" }, Group = StorageGroup.External });
            state.Disks.Add(new Disk { Number = 9, DevInst = 12, InstanceId = "disk-c", Serial = "serial-c", TargetId = "usb-c", TargetDevInst = 22, Ancestors = new List<string> { "disk-c", "usb-c" } });
            state.Volumes.Add(new Volume { Id = "volume-c", Label = "Emulator", Serial = "GHI", Disks = new List<int> { 9 }, Mounts = new List<string> { @"G:\" }, Group = StorageGroup.Emulator }); return state;
        }
        static IEnumerable<ToolStripMenuItem> TrayActions(IEnumerable<ToolStripItem> items) {
            foreach (var item in items.OfType<ToolStripMenuItem>()) {
                if (item.Tag is TrayStorageChoice) yield return item;
                foreach (var nested in TrayActions(item.DropDownItems.Cast<ToolStripItem>())) yield return nested;
            }
        }
        static void WithTrayMenu(Inventory state, bool busy, bool readOnly, bool clean, Action<TrayStorageChoice, bool> review, Action<ToolStripItem[]> check) {
            var items = TrayStorageMenu.Build(state, state == null ? 0 : state.Epoch, busy, readOnly, clean, review);
            try { check(items); } finally { foreach (var item in items) item.Dispose(); }
        }
        static void TrayStorageMenuTests() {
            Test("tray storage lists separate camera emulator and other external groups with no implicit action", delegate {
                int requests = 0; WithTrayMenu(TrayState(), false, false, false, (choice, clean) => requests++, items => {
                    Check(items.Select(x => x.Text).SequenceEqual(new[] { "Camera cards", "Emulator cards", "Other external storage" }) && TrayActions(items).Count() == 4 && requests == 0, "Grouping missing or menu invoked work");
                    Check(TrayActions(items).All(x => x.Enabled) && TrayActions(items).All(x => x.ToolTipText.Contains("review")), "No review description or disabled safe choice");
                });
            });
            Test("tray storage hides internal protected and system volumes including mislabeled system cards", delegate {
                var state = TrayState(); state.Volumes.Add(new Volume { Id = "internal", Group = StorageGroup.Internal }); state.Volumes.Add(new Volume { Id = "protected", Group = StorageGroup.Protected }); state.Volumes.Add(new Volume { Id = "system-card", System = true, Group = StorageGroup.Camera });
                Check(TrayStorageMenu.Choices(state, state.Epoch).Length == 3, "Protected/internal choices leaked");
            });
            Test("tray storage unknown classifications remain other storage and require safe topology proof", delegate {
                var state = TrayState(); state.Volumes[1].Group = StorageGroup.Unknown;
                WithTrayMenu(state, false, false, false, (choice, clean) => {}, items => Check(TrayActions(items).Count(x => ((TrayStorageChoice)x.Tag).VolumeId == "volume-b" && x.Enabled) == 2, "Unknown removable storage lost reviewed actions"));
                state.Disks[1].TargetId = ""; Check(!TrayStorageMenu.Choices(state, state.Epoch).Single(x => x.VolumeId == "volume-b").CanEject, "Unknown physical target allowed");
            });
            Test("tray storage clicks request exactly the captured volume and explicit clean intent", delegate {
                string requested = null; bool cleaning = false;
                WithTrayMenu(TrayState(), false, false, false, (choice, clean) => { requested = choice.VolumeId; cleaning = clean; }, items => {
                    var external = TrayActions(items).Where(x => ((TrayStorageChoice)x.Tag).VolumeId == "volume-b").ToArray();
                    external[1].PerformClick(); Check(requested == "volume-b" && cleaning, "Clean action misrouted or silently dropped cleanup");
                    external[0].PerformClick(); Check(requested == "volume-b" && !cleaning, "Plain action misrouted or enabled cleanup");
                    var camera = TrayActions(items).Single(x => ((TrayStorageChoice)x.Tag).VolumeId == "volume-a"); camera.PerformClick(); Check(requested == "volume-a" && !cleaning, "Camera review changed target");
                });
            });
            Test("tray storage card cleanup follows acknowledged app choice and normal cards start without cleanup", delegate {
                WithTrayMenu(TrayState(), false, false, true, (choice, clean) => Check(clean, "Enabled card preference dropped"), items => {
                    var cards = TrayActions(items).Where(x => ((TrayStorageChoice)x.Tag).Group != StorageGroup.External).ToArray(); Check(cards.Length == 2 && cards.All(x => x.Text.StartsWith("Clean && eject")), "Card cleanup labels lost"); foreach (var card in cards) card.PerformClick();
                });
                WithTrayMenu(TrayState(), false, false, false, (choice, clean) => {}, items => Check(TrayActions(items).Where(x => ((TrayStorageChoice)x.Tag).Group != StorageGroup.External).All(x => x.Text.StartsWith("Eject ")), "Default cleanup enabled"));
            });
            Test("tray storage busy and read-only menus disable every action and disabled clicks request nothing", delegate {
                foreach (var mode in new[] { new[] { true, false }, new[] { false, true } }) {
                    int requests = 0; WithTrayMenu(TrayState(), mode[0], mode[1], false, (choice, clean) => requests++, items => { var actions = TrayActions(items).ToArray(); Check(actions.All(x => !x.Enabled && !String.IsNullOrEmpty(x.ToolTipText)), "Busy/read-only action available"); foreach (var action in actions) action.PerformClick(); Check(requests == 0, "Disabled menu invoked review"); });
                }
            });
            Test("tray storage stale epoch and incomplete inventory disable all removal choices", delegate {
                var state = TrayState(); Check(TrayStorageMenu.Choices(state, state.Epoch + 1).All(x => !x.CanEject), "Stale list allowed eject"); state.Complete = false;
                WithTrayMenu(state, false, false, false, (choice, clean) => {}, items => Check(TrayActions(items).All(x => !x.Enabled), "Incomplete inventory allowed action"));
            });
            Test("tray storage protected or unknown sibling blocks only the affected removal group", delegate {
                var state = TrayState(); state.Volumes.Add(new Volume { Id = "hidden-system", System = true, Disks = new List<int> { 7 } });
                var choices = TrayStorageMenu.Choices(state, state.Epoch); Check(!choices.Single(x => x.VolumeId == "volume-a").CanEject && choices.Single(x => x.VolumeId == "volume-b").CanEject, "Protected sibling ignored or unrelated group blocked");
                state.Volumes.Last().System = false; state.Volumes.Last().Error = "unknown sibling"; Check(!TrayStorageMenu.Choices(state, state.Epoch).Single(x => x.VolumeId == "volume-a").CanEject, "Unknown sibling ignored");
            });
            Test("tray storage missing cleanup mount permits plain eject but never silently falls back from cleanup", delegate {
                var state = TrayState(); state.Volumes[0].Mounts.Clear(); state.Volumes[1].Mounts.Clear(); var choice = TrayStorageMenu.Choices(state, state.Epoch).Single(x => x.VolumeId == "volume-a"); Check(choice.CanEject && !choice.CanClean, "Cleanup mount conflated with eject proof");
                WithTrayMenu(state, false, false, true, (selected, clean) => {}, items => {
                    Check(!TrayActions(items).Single(x => ((TrayStorageChoice)x.Tag).VolumeId == "volume-a").Enabled, "Card cleanup preference downgraded");
                    var external = TrayActions(items).Where(x => ((TrayStorageChoice)x.Tag).VolumeId == "volume-b").ToArray(); Check(external[0].Enabled && !external[1].Enabled && external[1].ToolTipText.Contains("mount"), "Missing mount cleanup advertised");
                });
            });
            Test("tray storage snapshot resolves exactly its selected volume rather than another main-window selection", delegate {
                var state = TrayState(); var choice = TrayStorageMenu.Choices(state, state.Epoch).Single(x => x.VolumeId == "volume-a"); var resolved = choice.Resolve(state, state.Epoch);
                Check(resolved.Id == "volume-a" && resolved.Id != state.Volumes[1].Id, "Review followed another volume");
                var api = new FakeEject { Result = new EjectResult { Success = true } }; new EjectService(new FakeDiscovery { State = state }, api, new Operations()).Execute(EjectService.Plan(state, resolved.Id));
                Check(api.Calls == 1 && api.LastId == "usb-a" && api.LastNode == 20, "Explicit fake request targeted other device");
            });
            Test("tray storage disconnect reconnect and replaced volume refuse the old menu snapshot", delegate {
                foreach (Action<Inventory> change in new Action<Inventory>[] { s => s.Epoch++, s => s.Volumes[0].Serial = "new-card", s => s.Volumes[0].Id = "replacement", s => s.Volumes[0].Mounts[0] = @"H:\", s => s.Volumes.RemoveAt(0) }) {
                    var state = TrayState(); var choice = TrayStorageMenu.Choices(state, state.Epoch).Single(x => x.VolumeId == "volume-a"); change(state); Throws<SafetyException>(() => choice.Resolve(state, state.Epoch));
                }
            });
            Test("tray storage changed physical identity node topology classification and protected sibling refuse", delegate {
                foreach (Action<Inventory> change in new Action<Inventory>[] { s => s.Disks[0].Serial = "new-disk", s => s.Disks[0].TargetDevInst++, s => s.Disks[0].TargetId = "new-target", s => s.Volumes[0].Group = StorageGroup.External, s => s.Volumes.Add(new Volume { Id = "new-sibling", Disks = new List<int> { 7 } }), s => s.Volumes[0].System = true }) {
                    var state = TrayState(); var choice = TrayStorageMenu.Choices(state, state.Epoch).Single(x => x.VolumeId == "volume-a"); change(state); Throws<SafetyException>(() => choice.Resolve(state, state.Epoch));
                }
            });
            Test("tray storage snapshot is immutable while a rename still reviews current name for the same identity", delegate {
                var state = TrayState(); var choice = TrayStorageMenu.Choices(state, state.Epoch).Single(x => x.VolumeId == "volume-a"); string originalName = choice.Display;
                state.Volumes[0].Label = "Renamed same card"; Check(choice.Display == originalName && choice.Resolve(state, state.Epoch).Display.Contains("Renamed"), "Snapshot changed or stable rename misrouted");
                state.Volumes[0].Mounts.Add(@"H:\"); Check(choice.Display == originalName, "Snapshot shares mutable mount list"); Throws<SafetyException>(() => choice.Resolve(state, state.Epoch));
            });
            Test("tray storage captions escape ampersands and remove control characters without changing identity", delegate {
                var state = TrayState(); state.Volumes[0].Label = "A&B\r\ncamera";
                WithTrayMenu(state, false, false, false, (choice, clean) => {}, items => { var action = TrayActions(items).Single(x => ((TrayStorageChoice)x.Tag).VolumeId == "volume-a"); Check(action.Text.Contains("A&&Bcamera") && !action.Text.Any(Char.IsControl) && ((TrayStorageChoice)action.Tag).VolumeId == "volume-a", "Caption became mnemonic/control text or changed target"); });
            });
            Test("tray storage missing inventory empty external list and absent review handler expose no action", delegate {
                WithTrayMenu(null, false, false, false, (choice, clean) => {}, items => Check(items.Length == 1 && !items[0].Enabled && !TrayActions(items).Any(), "Missing inventory offered eject"));
                var state = TrayState(); state.Volumes.ForEach(x => x.Group = StorageGroup.Internal); WithTrayMenu(state, false, false, false, (choice, clean) => {}, items => Check(items.Length == 1 && !items[0].Enabled, "Empty list offered eject"));
                WithTrayMenu(TrayState(), false, false, false, null, items => Check(TrayActions(items).All(x => !x.Enabled), "No-handler action enabled"));
            });
            Test("tray storage explicit fake removal still revalidates after confirmation and respects import reservations", delegate {
                var state = TrayState(); var choice = TrayStorageMenu.Choices(state, state.Epoch).Single(x => x.VolumeId == "volume-a"); var plan = EjectService.Plan(state, choice.Resolve(state, state.Epoch).Id); var operations = new Operations(); var api = new FakeEject(); var discovery = new FakeDiscovery { State = state };
                using (operations.Reserve(new[] { 7 })) Throws<SafetyException>(() => new EjectService(discovery, api, operations).Execute(plan)); Check(api.Calls == 0, "Reserved disk reached fake request");
                state.Epoch++; Throws<SafetyException>(() => new EjectService(discovery, api, operations).Execute(plan)); Check(api.Calls == 0, "Changed connection reached fake request");
            });
        }
    }
}
