using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EasyEject.Modern {
    public sealed partial class MainWindow {
        readonly HotkeyMessageWindow shortcut;
        GroupedEjectReview groupedReview;
        string settingsRuntimeMessage, shortcutWarning;
        async void OpenSettings() {
            if (closed || UiBusy || inventory.Scanning || SettingsPanel.Visibility == Visibility.Visible || !DefaultsPanel.TryLeave() || !ProfilesPanel.TryLeave()) return;
            returnToProfile = false; Navigation.SelectedItem = SettingsNavigation;
            StorageView.Visibility = DefaultsPanel.Visibility = ProfilesPanel.Visibility = Visibility.Collapsed; SettingsPanel.Visibility = Visibility.Visible;
            UpdateActions(); await SettingsPanel.StartEdit(); UpdateActions();
        }
        string ConfigureShortcut() {
            if (settings == null || closed) return shortcut.Status;
            try { shortcutWarning = shortcut.Configure(settings.ShortcutEnabled, settings.ShortcutKey) ? null : shortcut.Status; }
            catch (Exception ex) { shortcutWarning = "Settings remain saved; shortcut unavailable. " + ex.Message; }
            if (shortcutWarning != null) Summary.Text = shortcutWarning;
            return shortcutWarning ?? shortcut.Status;
        }
        void SettingsSaved(AppSettings value) {
            settings = AppSettingsEdit.Copy(value); string shortcutMessage = ConfigureShortcut();
            settingsRuntimeMessage = "Application settings saved. " + shortcutMessage + " No import, cleanup or removal started.";
            string warning = LogModernOperation(settingsRuntimeMessage); if (warning != null) settingsRuntimeMessage += " " + warning;
        }
        string LogModernOperation(string text) {
            if (settings == null || !settings.DebugEnabled) return null;
            try { DebugLog.Append(Profiles.Root, text); return null; }
            catch (Exception ex) { return "Debug log unavailable: " + ex.Message; }
        }
        void EjectCards_Click(object sender, RoutedEventArgs args) { ReviewCards(); }
        async void ReviewCards() {
            if (closed || StorageBusy || inventory.Scanning || !notifications.Available || settings == null) return;
            working = true; UpdateActions(); BulkEjectResult terminal = null; var counts = new CleanupCounts();
            var token = activity.Begin(TrayOperationKind.Eject, "Camera/emulator cards", "Reviewing grouped removal");
            try {
                var owner = await Task.Run(() => new GroupedEjectReview(discovery.Scan(), AppSettings.Load(), discovery, operations,
                    new WindowsBulkConnections(discovery), validate => new WindowsEjectApi(validate), WindowsDiscovery.VolumeId));
                groupedReview = owner;
                if (owner.Count == 0) { Summary.Text = "No camera or emulator cards are connected."; return; }
                confirmation = MessageDialog("Review card removal", owner.Description, owner.HasCleanup ? "Clean & request safe removal" : "Request safe removal");
                confirmation.CloseButtonText = "Keep connected"; confirmation.IsPrimaryButtonEnabled = owner.Current; UpdateActions();
                var answer = await confirmation.ShowAsync(); confirmation = null;
                if (answer != ContentDialogResult.Primary) { owner.Cancel(); Summary.Text = "Card removal review cancelled. Devices kept connected."; return; }
                owner.Confirm(); completions.InvalidateDisks(owner.Disks); operationCancel = new CancellationTokenSource();
                ImportProgress.Visibility = Visibility.Visible; ImportProgress.IsIndeterminate = true;
                Summary.Text = "Requesting and verifying grouped card removal…"; LogModernOperation("User confirmed grouped card removal | " + owner.Description); UpdateActions();
                terminal = await Task.Run(() => owner.Execute(operationCancel.Token, counts, message => QueueImportProgress(token, message)));
                operationCancel.Dispose(); operationCancel = null;
                Summary.Text = terminal.Message; NotifyImport(terminal.Success ? "Cards safely removed" : "Card removal stopped", terminal.Message, !terminal.Success);
                if (AppWindow.IsVisible) { confirmation = MessageDialog(terminal.Success ? "Cards safely removed" : "Card removal stopped", terminal.Message, null); await confirmation.ShowAsync(); confirmation = null; }
            } catch (Exception ex) {
                Summary.Text = terminal == null ? "Grouped removal stopped. " + ex.Message + " " + counts.Message : terminal.Message + " Result display unavailable: " + ex.Message;
                NotifyImport("Card removal needs attention", Summary.Text, true);
            } finally {
                if (groupedReview != null) groupedReview.Cancel(); groupedReview = null; confirmation = null;
                if (operationCancel != null) { operationCancel.Dispose(); operationCancel = null; }
                working = false; ImportStatus.Text = Summary.Text; ImportProgress.Visibility = Visibility.Collapsed;
                activity.End(Summary.Text); UpdateImportTray(); UpdateActions();
            }
            await ReadInventory();
        }
    }
}
