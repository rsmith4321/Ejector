using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace EasyEject.Modern {
    public sealed partial class MainWindow {
        TrayFlyoutWindow trayFlyout;
        Dictionary<string, TrayFlyoutSpace> flyoutSpace = new Dictionary<string, TrayFlyoutSpace>();
        ImportProfile[] flyoutProfiles = new ImportProfile[0]; long flyoutEpoch = -1; int flyoutRevision;
        void RequestQuit() {
            quitting = false;
            string refusal = AppQuitPolicy.Refusal(UiBusy, EditorOpen);
            if (refusal != null) {
                if (DefaultsPanel.Editing) { StorageView.Visibility = ProfilesPanel.Visibility = SettingsPanel.Visibility = Visibility.Collapsed; Navigation.SelectedItem = DefaultsNavigation; DefaultsPanel.Visibility = Visibility.Visible; DefaultsPanel.QuitRefused(refusal); }
                else if (ProfilesPanel.Editing) { StorageView.Visibility = DefaultsPanel.Visibility = SettingsPanel.Visibility = Visibility.Collapsed; Navigation.SelectedItem = ProfilesNavigation; ProfilesPanel.Visibility = Visibility.Visible; ProfilesPanel.QuitRefused(refusal); }
                else if (SettingsPanel.Editing) { StorageView.Visibility = DefaultsPanel.Visibility = ProfilesPanel.Visibility = Visibility.Collapsed; Navigation.SelectedItem = SettingsNavigation; SettingsPanel.Visibility = Visibility.Visible; SettingsPanel.QuitRefused(refusal); }
                Summary.Text = refusal; Reveal(); return;
            }
            quitting = true; Close();
        }
        void HideTrayFlyout() { trayFlyout?.Dismiss(TrayFlyoutDismissReason.OpenWindow); }
        void ToggleTrayFlyout(System.Drawing.Point point) {
            if (closed) return;
            if (trayFlyout == null) trayFlyout = new TrayFlyoutWindow(Reveal, () => { Reveal(); OpenSettings(); }, RequestQuit, FlyoutAction, RefreshTrayFlyout);
            if (trayFlyout.IsFlyoutVisible) { trayFlyout.Dismiss(TrayFlyoutDismissReason.Escape); return; }
            // Re-read opt-in preferences on every opening; a cached enabled profile
            // must not flash a sharing action after that preference was disabled.
            flyoutEpoch = -1;
            trayFlyout.ShowAt(point, FlyoutView()); _ = LoadFlyoutDetails();
        }
        TrayFlyoutView FlyoutView() {
#if UI_TESTS
            if (UiTestHost.Inspect) return FixtureFlyout();
#endif
            var state = inventory.Current;
            return TrayFlyoutModel.Build(new TrayFlyoutInput {
                State = state, Epoch = discovery.Epoch, MonitoringAvailable = notifications.Available, SettingsLoaded = settings != null,
                Scanning = inventory.Scanning, Work = UiBusy, EditorOpen = EditorOpen, OtherStorageAllowed = CanUseOtherStorage,
                ProtectedReason = id => concurrentWork.State.Unavailable(state, discovery.Epoch, id), ActiveKind = id => activity.Current?.Kind,
                HasConnection = v => AutomaticConnection.Capture(state, v) != null,
                SharingOptedIn = v => flyoutEpoch == discovery.Epoch && flyoutProfiles.Any(p => p.VolumeId == v.Id && p.VolumeSerial == v.Serial &&
                    state.Disks.Any(d => v.Disks.Contains(d.Number) && d.InstanceId == p.DiskInstance) && QuickShareProfile.Enabled(p)),
                Space = v => { TrayFlyoutSpace space; return flyoutEpoch == discovery.Epoch && flyoutSpace.TryGetValue(v.Id, out space) ? (TrayFlyoutSpace?)space : null; }
            });
        }
        void RefreshTrayFlyout() {
            if (trayFlyout == null || !trayFlyout.IsFlyoutVisible || trayFlyout.RequestRunning || closed) return;
            trayFlyout.Render(FlyoutView());
        }
        async Task LoadFlyoutDetails() {
            int revision = ++flyoutRevision; var state = inventory.Current; long epoch = discovery.Epoch;
            if (state == null || state.Epoch != epoch) return;
            var space = new Dictionary<string, TrayFlyoutSpace>(); ImportProfile[] profiles = new ImportProfile[0];
            await Task.Run(() => {
                try { profiles = Profiles.Catalog().Where(p => p.CanEdit).Select(p => p.Profile).ToArray(); } catch { /* Sharing remains hidden when preferences cannot be read. */ }
                foreach (var choice in TrayStorageMenu.Choices(state, epoch)) {
                    try { space[choice.VolumeId] = Native.VolumeSpace(choice.VolumeId); } catch { /* Capacity is informational, never authority. */ }
                }
            });
            if (closed || revision != flyoutRevision || epoch != discovery.Epoch) return;
            flyoutSpace = space; flyoutProfiles = profiles; flyoutEpoch = epoch; RefreshTrayFlyout();
        }
        async Task FlyoutAction(TrayFlyoutDevice device, string kind) {
            if (closed || trayFlyout == null) return;
            var current = FlyoutView().Devices.SingleOrDefault(d => d.VolumeId == device.VolumeId);
            if (current == null || (kind == "eject" ? !current.CanEject : kind == "import" ? !current.CanImport : !current.CanQuickShare)) { await FlyoutFailure(kind, "This choice expired. Reopen the tray and review the device again."); return; }
            try {
                var volume = device.Choice.Resolve(inventory.Current, discovery.Epoch);
                if (kind == "eject") { await EjectDevice(false, device.Choice, trayFlyout); return; }
                var connection = AutomaticConnection.Capture(inventory.Current, volume);
                if (connection == null || StorageBusy) { await FlyoutFailure(kind, "This device is no longer available for the selected action."); return; }
                await EnsureVisible(); ShowStorage();
                if (kind == "share") { ReviewHistory(volume.Id); return; }
                automatic.MarkManual(inventory.Current, volume); await RunImport(connection, null); await ReadInventory();
            } catch (Exception ex) { await FlyoutFailure(kind, "The action stopped. " + ex.Message); }
        }
        async Task FlyoutFailure(string kind, string message) {
            if (kind == "eject") { trayFlyout.Report(message); return; }
            await EnsureVisible(); Summary.Text = message;
        }
    }
}
