using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace EasyEject {
    // Windows 11-style tray flyout. Everything here is UI-free and shared with the main window,
    // so the flyout can never grant more than the window: it only identifies a device and opens
    // the same reviewed flows. Nothing here ejects, imports, exports or deletes.

    internal enum TrayFlyoutDeviceState { Connected, Importing, InUse, NeedsReview }

    internal sealed class TrayFlyoutDevice {
        internal readonly string VolumeId, Title, Capacity, StateText, Reason;
        internal readonly StorageGroup Group;
        internal readonly TrayFlyoutDeviceState State;
        internal readonly bool CanEject, CanImport, CanQuickShare, ShowQuickShare;
        internal readonly TrayStorageChoice Choice;
        internal TrayFlyoutDevice(string volumeId, string title, string capacity, StorageGroup group, TrayFlyoutDeviceState state, string stateText,
            bool canEject, bool canImport, bool showQuickShare, bool canQuickShare, string reason, TrayStorageChoice choice) {
            VolumeId = volumeId; Title = title; Capacity = capacity; Group = group; State = state; StateText = stateText;
            CanEject = canEject; CanImport = canImport; ShowQuickShare = showQuickShare; CanQuickShare = showQuickShare && canQuickShare; Reason = reason; Choice = choice;
        }
    }

    internal sealed class TrayFlyoutGroup {
        internal readonly string Heading; internal readonly TrayFlyoutDevice[] Devices;
        internal TrayFlyoutGroup(string heading, TrayFlyoutDevice[] devices) { Heading = heading; Devices = devices; }
    }

    internal struct TrayFlyoutSpace {
        internal readonly ulong Total, Free;
        internal TrayFlyoutSpace(ulong total, ulong free) { Total = total; Free = free; }
    }

    // Inputs mirror the main window's own action rules (MainWindow.UpdateActions).
    internal sealed class TrayFlyoutInput {
        internal Inventory State = null; internal long Epoch = 0;
        internal bool MonitoringAvailable = false, SettingsLoaded = false, Scanning = false;
        internal bool Work = false;          // an import, eject, share, verification or modal review is running
        internal bool EditorOpen = false;    // an unsaved defaults/profile/settings editor is open
        internal bool OtherStorageAllowed = false; // the window's concurrent review of unrelated storage is available
        internal Func<string, string> ProtectedReason = _ => null; // non-null for an active import source/destination
        internal Func<string, TrayOperationKind?> ActiveKind = _ => null;
        internal Func<Volume, bool> HasConnection = _ => true;
        internal Func<Volume, bool> SharingOptedIn = _ => false;
        internal Func<Volume, TrayFlyoutSpace?> Space = _ => null;
    }

    internal sealed class TrayFlyoutView {
        internal readonly TrayFlyoutGroup[] Groups; internal readonly string EmptyText, Status;
        internal readonly bool CanOpenWindow, CanOpenSettings;
        internal readonly AppCloseAction QuitAction; internal readonly string QuitRefusal;
        internal TrayFlyoutView(TrayFlyoutGroup[] groups, string empty, string status, bool settings, AppCloseAction quit, string refusal) {
            Groups = groups; EmptyText = empty; Status = status; CanOpenWindow = true; CanOpenSettings = settings; QuitAction = quit; QuitRefusal = refusal;
        }
        internal IEnumerable<TrayFlyoutDevice> Devices { get { return Groups.SelectMany(g => g.Devices); } }
    }

    internal static class TrayFlyoutModel {
        internal static readonly StorageGroup[] GroupOrder = { StorageGroup.Camera, StorageGroup.Emulator, StorageGroup.External };
        internal static string Heading(StorageGroup group) { return group == StorageGroup.Camera ? "Camera cards" : group == StorageGroup.Emulator ? "Emulator cards" : "Other external storage"; }

        internal static TrayFlyoutView Build(TrayFlyoutInput input) {
            if (input == null) throw new ArgumentNullException("input");
            var state = input.State;
            bool fresh = state != null && state.Epoch == input.Epoch && input.MonitoringAvailable;
            bool storageBusy = input.Work || input.EditorOpen;
            bool ready = fresh && !storageBusy && !input.Scanning && input.SettingsLoaded;
            var groups = new List<TrayFlyoutGroup>();
            var choices = TrayStorageMenu.Choices(state, input.Epoch);
            foreach (var group in GroupOrder) {
                var devices = new List<TrayFlyoutDevice>();
                foreach (var choice in choices.Where(c => group == StorageGroup.External ? c.Group == StorageGroup.External || c.Group == StorageGroup.Unknown : c.Group == group)) {
                    var volume = state.Volumes.Single(v => v.Id == choice.VolumeId);
                    string protectedReason = input.Work ? input.ProtectedReason(volume.Id) : null;
                    TrayOperationKind? kind = protectedReason == null ? null : input.ActiveKind(volume.Id);
                    bool otherReady = fresh && input.SettingsLoaded && !input.EditorOpen && input.Work && input.OtherStorageAllowed && !input.Scanning && protectedReason == null;
                    bool canEject = (ready || otherReady) && choice.CanEject;
                    bool connection = input.HasConnection(volume);
                    bool canImport = ready && connection;
                    bool showShare = input.SharingOptedIn(volume);
                    var stateValue = protectedReason != null ? (kind == TrayOperationKind.Import || kind == TrayOperationKind.Resume ? TrayFlyoutDeviceState.Importing : TrayFlyoutDeviceState.InUse)
                        : choice.Error != null ? TrayFlyoutDeviceState.NeedsReview : TrayFlyoutDeviceState.Connected;
                    string stateText = stateValue == TrayFlyoutDeviceState.Importing ? "Importing…" : stateValue == TrayFlyoutDeviceState.InUse ? "In use"
                        : stateValue == TrayFlyoutDeviceState.NeedsReview ? "Needs review" : "Connected";
                    string reason = protectedReason ?? (!fresh ? "Refresh the storage list before reviewing a device." : input.Scanning ? "Checking connected storage…"
                        : storageBusy && !otherReady ? (input.EditorOpen && !input.Work ? "Save or cancel the open editor first." : "Wait for the current operation or review to finish.")
                        : !input.SettingsLoaded ? "Saved app settings are unavailable. Device actions are blocked." : choice.Error);
                    devices.Add(new TrayFlyoutDevice(volume.Id, Title(volume), CapacityText(input.Space(volume)), choice.Group, stateValue, stateText,
                        canEject, canImport, showShare, ready && connection, reason, choice));
                }
                if (devices.Count > 0) groups.Add(new TrayFlyoutGroup(Heading(group), devices.ToArray()));
            }
            string empty = groups.Count > 0 ? null : state == null ? "Reading connected storage…" : "No cards or external drives connected.";
            string status = input.Work ? "Working… Easy Eject keeps transfer devices connected until it finishes." : input.EditorOpen ? "An editor is open in Easy Eject."
                : input.Scanning ? "Checking connected storage…" : !input.MonitoringAvailable ? "Connection monitoring unavailable. Device actions are blocked." : null;
            var quit = AppQuitPolicy.QuitRequested(input.Work, input.EditorOpen);
            return new TrayFlyoutView(groups.ToArray(), empty, status, !storageBusy, quit, AppQuitPolicy.Refusal(input.Work, input.EditorOpen));
        }

        internal static string Title(Volume volume) {
            string label = String.IsNullOrWhiteSpace(volume.Label) ? "Unnamed volume" : volume.Label;
            string clean = new string(label.Where(c => !Char.IsControl(c)).Take(120).ToArray());
            return volume.Mounts.Count == 0 ? clean : clean + " (" + String.Join(", ", volume.Mounts.Select(m => m.TrimEnd('\\'))) + ")";
        }

        internal static string CapacityText(TrayFlyoutSpace? space) {
            if (!space.HasValue || space.Value.Total == 0) return "Capacity unavailable";
            return Size(space.Value.Free) + " free of " + Size(space.Value.Total);
        }

        internal static string Size(ulong bytes) {
            string[] units = { "bytes", "KB", "MB", "GB", "TB" }; double value = bytes; int unit = 0;
            while (value >= 1000 && unit < units.Length - 1) { value /= 1000; unit++; }
            return unit == 0 ? bytes.ToString(CultureInfo.CurrentCulture) + " bytes" : value.ToString(value >= 100 ? "0" : "0.0", CultureInfo.CurrentCulture) + " " + units[unit];
        }
    }

    // The window and flyout ask the same question. Keep Connected is the Return/Escape default.
    internal static class ManualEjectPrompt {
        internal const string Title = "Safe eject", Confirm = "Request safe removal", Keep = "Keep Connected";
        internal const bool KeepIsDefault = true;
        internal const string Accepted = "Safe removal accepted", Refused = "Device still connected";
    }

    internal enum TaskbarEdge { Bottom, Top, Left, Right }

    // Pure placement in physical pixels for one monitor. The flyout opens beside the taskbar,
    // near the clicked icon, inside the monitor's work area, at the monitor's own scale.
    internal static class TrayFlyoutPlacement {
        internal const int MarginDip = 12;
        internal static TaskbarEdge Edge(Rectangle monitor, Rectangle work, Point anchor) {
            if (work.Bottom < monitor.Bottom) return TaskbarEdge.Bottom;
            if (work.Top > monitor.Top) return TaskbarEdge.Top;
            if (work.Left > monitor.Left) return TaskbarEdge.Left;
            if (work.Right < monitor.Right) return TaskbarEdge.Right;
            // Auto-hidden taskbar: use the monitor edge nearest the click.
            int bottom = monitor.Bottom - anchor.Y, top = anchor.Y - monitor.Top, left = anchor.X - monitor.Left, right = monitor.Right - anchor.X;
            int min = Math.Min(Math.Min(bottom, top), Math.Min(left, right));
            return min == bottom ? TaskbarEdge.Bottom : min == top ? TaskbarEdge.Top : min == left ? TaskbarEdge.Left : TaskbarEdge.Right;
        }
        internal static Rectangle Place(Rectangle monitor, Rectangle work, Point anchor, SizeF sizeDip, double scale) {
            if (Double.IsNaN(scale) || Double.IsInfinity(scale) || scale <= 0 || sizeDip.Width <= 0 || sizeDip.Height <= 0 ||
                Single.IsNaN(sizeDip.Width) || Single.IsInfinity(sizeDip.Width) || Single.IsNaN(sizeDip.Height) || Single.IsInfinity(sizeDip.Height) || work.Width <= 0 || work.Height <= 0) throw new ArgumentOutOfRangeException("scale");
            int margin = (int)Math.Min(Math.Round(MarginDip * scale), Math.Max(0, (Math.Min(work.Width, work.Height) - 1) / 2));
            int width = (int)Math.Max(1, Math.Min(Math.Round(sizeDip.Width * scale), work.Width - 2 * margin));
            int height = (int)Math.Max(1, Math.Min(Math.Round(sizeDip.Height * scale), work.Height - 2 * margin));
            var edge = Edge(monitor, work, anchor);
            int x, y;
            if (edge == TaskbarEdge.Bottom || edge == TaskbarEdge.Top) {
                x = Clamp(anchor.X - width / 2, work.Left + margin, work.Right - margin - width);
                y = edge == TaskbarEdge.Bottom ? work.Bottom - margin - height : work.Top + margin;
            } else {
                y = Clamp(anchor.Y - height / 2, work.Top + margin, work.Bottom - margin - height);
                x = edge == TaskbarEdge.Left ? work.Left + margin : work.Right - margin - width;
            }
            return new Rectangle(x, y, width, height);
        }
        static int Clamp(int value, int low, int high) { return high < low ? low : Math.Max(low, Math.Min(high, value)); }
    }

    internal enum TrayFlyoutDismissReason { Escape, Deactivated, ActionStarted, OpenWindow }

    // Light dismiss: click-away and Escape close the flyout, except while its own eject
    // confirmation is open (that dialog owns Escape = Keep Connected) or its request is running.
    internal static class TrayFlyoutDismiss {
        internal static bool ShouldClose(TrayFlyoutDismissReason reason, bool dialogOpen, bool requestRunning) {
            if (reason == TrayFlyoutDismissReason.OpenWindow) return true;
            if (dialogOpen || requestRunning) return false;
            return true;
        }
    }
}
