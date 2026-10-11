using System;
using System.Drawing;
using System.Linq;

namespace EasyEject {
    public static partial class Tests {
        static TrayFlyoutInput FlyoutInput() { var state = TrayState(); return new TrayFlyoutInput { State = state, Epoch = state.Epoch, MonitoringAvailable = true, SettingsLoaded = true }; }
        static void TrayFlyoutTests() {
            Test("flyout groups only external storage and offers no implicit sharing", delegate {
                var input = FlyoutInput(); input.State.Volumes.Add(new Volume { Id = "protected", Group = StorageGroup.Protected }); input.State.Volumes.Add(new Volume { Id = "system", Group = StorageGroup.Camera, System = true });
                var view = TrayFlyoutModel.Build(input);
                Check(view.Groups.Select(g => g.Heading).SequenceEqual(new[] { "Camera cards", "Emulator cards", "Other external storage" }), "Wrong groups");
                Check(view.Devices.Count() == 3 && view.Devices.All(d => d.CanEject && d.CanImport && !d.ShowQuickShare && !d.CanQuickShare), "Unsafe initial actions");
                Check(view.CanOpenSettings && view.QuitAction == AppCloseAction.Exit, "Idle footer blocked");
            });
            Test("flyout shares only opted-in connected devices and keeps stale choices unavailable", delegate {
                var input = FlyoutInput(); input.SharingOptedIn = v => v.Id == "volume-a"; input.HasConnection = v => v.Id != "volume-b";
                var view = TrayFlyoutModel.Build(input);
                Check(view.Devices.Single(d => d.VolumeId == "volume-a").CanQuickShare && !view.Devices.Single(d => d.VolumeId == "volume-b").CanImport, "Opt-in/connection bypassed");
                input.Epoch++; view = TrayFlyoutModel.Build(input);
                Check(view.Devices.All(d => !d.CanEject && !d.CanImport && !d.CanQuickShare && d.Reason != null), "Stale inventory admitted");
            });
            Test("flyout protects active transfer disks and permits only separately proven storage", delegate {
                var input = FlyoutInput(); input.Work = true; input.OtherStorageAllowed = true;
                input.ProtectedReason = id => id == "volume-b" ? null : "Transfer disk is protected";
                input.ActiveKind = id => id == "volume-a" ? TrayOperationKind.Import : TrayOperationKind.Share;
                var view = TrayFlyoutModel.Build(input); var a = view.Devices.Single(d => d.VolumeId == "volume-a");
                Check(a.State == TrayFlyoutDeviceState.Importing && !a.CanEject && !a.CanImport, "Import source unprotected");
                Check(view.Devices.Single(d => d.VolumeId == "volume-c").State == TrayFlyoutDeviceState.InUse, "Destination state missing");
                Check(view.Devices.Single(d => d.VolumeId == "volume-b").CanEject && view.Devices.All(d => !d.CanImport && !d.CanQuickShare), "Concurrent availability wrong");
                Check(!view.CanOpenSettings && view.QuitAction == AppCloseAction.StayOpen, "Busy footer permitted Quit");
                input.EditorOpen = true; Check(TrayFlyoutModel.Build(input).Devices.All(d => !d.CanEject), "Editor bypassed");
                input.EditorOpen = false; input.SettingsLoaded = false; Check(TrayFlyoutModel.Build(input).Devices.All(d => !d.CanEject), "Missing settings bypassed");
            });
            Test("flyout blocks scanning monitoring failure incomplete topology and open editors", delegate {
                foreach (int mode in Enumerable.Range(0, 5)) {
                    var input = FlyoutInput(); input.Scanning = mode == 0; input.MonitoringAvailable = mode != 1; input.SettingsLoaded = mode != 2; input.EditorOpen = mode == 3; input.State.Complete = mode != 4;
                    var view = TrayFlyoutModel.Build(input); Check(view.Devices.All(d => !d.CanEject), "Removal admitted in blocked state " + mode);
                    if (mode == 3) Check(view.QuitAction == AppCloseAction.StayOpen && !view.CanOpenSettings, "Editor Quit admitted");
                }
            });
            Test("flyout safe removal confirmation keeps Return and Escape connected", delegate { Check(ManualEjectPrompt.KeepIsDefault && ManualEjectPrompt.Keep == "Keep Connected" && ManualEjectPrompt.Confirm == "Request safe removal", "Unsafe default"); });
            Test("flyout light dismiss respects its confirmation and active request", delegate {
                foreach (TrayFlyoutDismissReason reason in Enum.GetValues(typeof(TrayFlyoutDismissReason))) {
                    Check(TrayFlyoutDismiss.ShouldClose(reason, false, false), "Idle dismiss blocked");
                    if (reason != TrayFlyoutDismissReason.OpenWindow) Check(!TrayFlyoutDismiss.ShouldClose(reason, true, false) && !TrayFlyoutDismiss.ShouldClose(reason, false, true), "Dialog/request abandoned");
                }
            });
            Test("flyout placement follows each taskbar edge at fixed monitor scales", delegate {
                foreach (double scale in new[] { 1.0, 1.5, 2.25 }) foreach (TaskbarEdge edge in Enum.GetValues(typeof(TaskbarEdge))) {
                    var monitor = new Rectangle(-2560, -200, 2560, 1440); var work = monitor;
                    if (edge == TaskbarEdge.Bottom) work.Height -= 60; if (edge == TaskbarEdge.Top) { work.Y += 60; work.Height -= 60; }
                    if (edge == TaskbarEdge.Left) { work.X += 60; work.Width -= 60; } if (edge == TaskbarEdge.Right) work.Width -= 60;
                    var anchor = new Point(monitor.Right - 24, monitor.Bottom - 24);
                    Check(TrayFlyoutPlacement.Edge(monitor, work, anchor) == edge, "Taskbar edge wrong");
                    var placed = TrayFlyoutPlacement.Place(monitor, work, anchor, new SizeF(400, 520), scale);
                    Check(work.Contains(placed) && placed.Width == (int)Math.Round(400 * scale), "Flyout outside work area or wrong DPI");
                }
            });
            Test("flyout placement handles auto-hide tiny work areas and invalid sizes", delegate {
                var monitor = new Rectangle(0, 0, 1920, 1080);
                Check(TrayFlyoutPlacement.Edge(monitor, monitor, new Point(1900, 500)) == TaskbarEdge.Right, "Auto-hide edge lost");
                var tiny = new Rectangle(0, 0, 10, 10); Check(tiny.Contains(TrayFlyoutPlacement.Place(tiny, tiny, Point.Empty, new SizeF(400, 520), 2.25)), "Tiny work area overflow");
                foreach (double invalid in new[] { 0.0, -1, Double.NaN, Double.PositiveInfinity }) Throws<ArgumentOutOfRangeException>(() => TrayFlyoutPlacement.Place(monitor, monitor, Point.Empty, new SizeF(400, 520), invalid));
            });
        }
    }
}
