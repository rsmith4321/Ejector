using System;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EasyEject.Modern {
    public sealed partial class MainWindow {
        ResumeOperationReview resuming;
        void Resume_Click(object sender, RoutedEventArgs args) { ResumeSelectedImport(); }
        async void ResumeSelectedImport() {
            var card = StorageCards.SelectedItem as StorageCard;
            if (StorageBusy || inventory.Scanning || !notifications.Available) return;
            if (card == null || card.Connection == null) { Summary.Text = "Select the original connected device, then choose Resume interrupted import."; return; }
            var connection = card.Connection;
            working = true; importOperationActive = true; importTerminal = null;
            ResumeOperationReview owner = null; ImportedOutcome outcome = null;
            var token = activity.Begin(TrayOperationKind.Resume, connection.Volume.Display, "Choosing interrupted batch"); UpdateActions();
            try {
                await EnsureVisible();
                var saved = await Task.Run(() => Tuple.Create(Profiles.ReviewConnected(connection.Volume, connection.Disk), ImportDefaults.Load()));
                if (saved.Item1.Value == null) throw new SafetyException("Set up this device with its original media folder and destination first. Saving a profile starts no import; then choose Resume interrupted import.");
                Summary.Text = "Choose this destination's removal-intent.json beside the interrupted batch's import audit. Nothing resumes until you review and confirm it.";
                var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id) { CommitButtonText = "Review interrupted batch" };
                picker.FileTypeFilter.Add(".json");
                var selected = await picker.PickSingleFileAsync();
                if (selected == null) { Summary.Text = "Resume selection cancelled. No resume started."; return; }
                string sid;
                using (var identity = WindowsIdentity.GetCurrent()) {
                    if (identity.User == null) throw new SafetyException("The current Windows Trash owner is unavailable."); sid = identity.User.Value;
                }
                automatic.MarkManual(inventory.Current, connection.Volume);
                operationCancel = new CancellationTokenSource(); ImportProgress.Visibility = Visibility.Visible; ImportProgress.IsIndeterminate = true;
                Summary.Text = "Checking the exact journal, devices, owners, saved copies and remaining sources…"; UpdateActions();
                owner = await Task.Run(() => ResumeOperationReview.Create(connection, saved.Item1, saved.Item2, selected.Path, sid, discovery, operations, operationCancel.Token));
                resuming = owner;
                confirmation = MessageDialog("Resume interrupted verified import?", owner.Description, "Resume verified batch");
                confirmation.IsPrimaryButtonEnabled = owner.Current && !operationCancel.IsCancellationRequested; UpdateActions();
                var answer = await confirmation.ShowAsync(); confirmation = null;
                if (answer != ContentDialogResult.Primary) { Summary.Text = "Resume review cancelled. Remaining sources and complete saved copies are kept."; return; }
                owner.Confirm(); completions.InvalidateSource(owner.Root); UpdateActions();
                var result = await Task.Run(() => owner.Execute(operations, operationCancel.Token, message => QueueImportProgress(token, message)));
                // Preserve the durable result before releasing folders or displaying completion.
                importTerminal = result; var profile = owner.Profile; var guard = owner.Guard; var options = owner.Options; var root = owner.Root; var destination = owner.Destination;
                operationCancel.Dispose(); operationCancel = null; importOperationActive = false;
                owner.Dispose(); owner = null; resuming = null; ImportProgress.IsIndeterminate = false; ImportProgress.Value = 100;
                await PresentVerifiedImport(result, profile, guard, root, destination, options, completed => outcome = completed);
            } catch (OperationCanceledException) {
                Summary.Text = importTerminal == null
                    ? "Resume cancelled. Previously absent files remain absent; remaining source data and complete saved copies are kept. No completion receipt was written. Retry requires a fresh manual review."
                    : outcome == null ? "Resume verified. " + importTerminal.Files + " files saved; completion display unavailable." : outcome.Message;
            } catch (Exception ex) {
                Summary.Text = importTerminal == null ? "Resume stopped. " + ex.Message
                    : (outcome == null ? "Resume verified. " + importTerminal.Files + " files saved." : outcome.Message) + " Completion review unavailable: " + ex.Message;
                NotifyImport(importTerminal == null ? "Resume needs attention" : "Resume verified; review needs attention", Summary.Text, true);
            } finally {
                if (owner != null) owner.Dispose(); resuming = null; confirmation = null;
                if (operationCancel != null) { operationCancel.Dispose(); operationCancel = null; }
                working = false; importOperationActive = false; ImportStatus.Text = Summary.Text; ImportProgress.Visibility = Visibility.Collapsed;
                activity.End(Summary.Text); UpdateImportTray(); UpdateActions();
            }
            await ReadInventory();
        }
    }
}
