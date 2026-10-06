using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace EasyEject {
    // A menu snapshot can identify a review target, never authorize removal itself.
    internal sealed class TrayStorageChoice {
        internal readonly string VolumeId, Display, Fingerprint, Error, CleanupError;
        internal readonly StorageGroup Group;
        internal readonly long Epoch;
        readonly string target, groupFingerprint; readonly uint targetNode;
        internal bool CanEject { get { return Error == null; } }
        internal bool CanClean { get { return CanEject && CleanupError == null; } }
        internal TrayStorageChoice(Inventory state, Volume volume, long currentEpoch) {
            VolumeId = volume.Id; Display = volume.Display; Fingerprint = volume.Fingerprint; Group = volume.Group; Epoch = state.Epoch;
            try {
                if (state.Epoch != currentEpoch) throw new SafetyException("This storage list is stale. Refresh and review the device.");
                var plan = EjectService.Plan(state, volume.Id); target = plan.TargetId; targetNode = plan.TargetDevInst; groupFingerprint = plan.Fingerprint;
                try { MetadataCleanupPlan.Single(state, plan, volume.Id); } catch (SafetyException ex) { CleanupError = ex.Message; }
            } catch (SafetyException ex) { Error = ex.Message; }
        }
        internal Volume Resolve(Inventory state, long currentEpoch) {
            if (!CanEject || state == null || !state.Complete || Epoch != currentEpoch || state.Epoch != Epoch) throw new SafetyException("The tray device choice expired. Refresh and review a new eject choice.");
            var volume = state.Volumes.SingleOrDefault(x => x.Id == VolumeId);
            if (volume == null || volume.Fingerprint != Fingerprint || volume.Group != Group) throw new SafetyException("The tray volume or classification changed. Review the device again.");
            var plan = EjectService.Plan(state, VolumeId);
            if (plan.TargetId != target || plan.TargetDevInst != targetNode || plan.Fingerprint != groupFingerprint) throw new SafetyException("The tray device or affected storage changed. Review every affected volume again.");
            return volume;
        }
    }
    internal static class TrayStorageMenu {
        internal static TrayStorageChoice[] Choices(Inventory state, long currentEpoch) {
            if (state == null) return new TrayStorageChoice[0];
            return state.Volumes.Where(v => !v.System && v.Group != StorageGroup.Internal && v.Group != StorageGroup.Protected)
                .OrderBy(v => v.Group).ThenBy(v => v.Display, StringComparer.CurrentCultureIgnoreCase).Select(v => new TrayStorageChoice(state, v, currentEpoch)).ToArray();
        }
        static string Caption(string value) { return new string(value.Where(c => !Char.IsControl(c)).Take(180).ToArray()).Replace("&", "&&"); }
        internal static ToolStripItem[] Build(Inventory state, long currentEpoch, bool busy, bool readOnly, bool cleanCards, Action<TrayStorageChoice, bool> review) {
            var choices = Choices(state, currentEpoch); var result = new List<ToolStripItem>();
            foreach (var group in new[] { StorageGroup.Camera, StorageGroup.Emulator, StorageGroup.External }) {
                var values = choices.Where(x => group == StorageGroup.External ? x.Group == StorageGroup.External || x.Group == StorageGroup.Unknown : x.Group == group).ToArray();
                if (values.Length == 0) continue;
                var heading = new ToolStripMenuItem(group == StorageGroup.Camera ? "Camera cards" : group == StorageGroup.Emulator ? "Emulator cards" : "Other external storage");
                foreach (var choice in values) {
                    string unavailable = readOnly ? "Unavailable in this read-only window." : busy ? "Wait for the current operation or open dialog to finish." : choice.Error;
                    if (group != StorageGroup.External) {
                        var item = ActionItem((cleanCards ? "Clean && eject " : "Eject ") + Caption(choice.Display) + "…", choice, cleanCards, unavailable, review); heading.DropDownItems.Add(item);
                    } else {
                        var item = new ToolStripMenuItem(Caption(choice.Display)) { Enabled = unavailable == null, ToolTipText = unavailable ?? "Review safe removal of this disk and all affected storage." };
                        item.DropDownItems.Add(ActionItem("Eject…", choice, false, unavailable, review));
                        item.DropDownItems.Add(ActionItem("Clean && eject…", choice, true, unavailable, review)); heading.DropDownItems.Add(item);
                    }
                }
                result.Add(heading);
            }
            if (result.Count == 0) result.Add(new ToolStripMenuItem(state == null ? "Reading connected storage…" : "No external storage in the current list") { Enabled = false });
            return result.ToArray();
        }
        static ToolStripMenuItem ActionItem(string text, TrayStorageChoice choice, bool clean, string unavailable, Action<TrayStorageChoice, bool> review) {
            string reason = unavailable ?? (clean ? choice.CleanupError : null);
            var item = new ToolStripMenuItem(text) { Enabled = reason == null && review != null, ToolTipText = reason ?? "Opens a fresh Cancel-default review; this menu does not eject directly.", Tag = choice };
            item.Click += delegate { if (item.Enabled && review != null) review(choice, clean); }; return item;
        }
    }
}
