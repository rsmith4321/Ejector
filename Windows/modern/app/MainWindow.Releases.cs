using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Forms = System.Windows.Forms;

namespace EasyEject.Modern {
    public sealed partial class MainWindow {
        readonly WindowsUpdateCoordinator releaseChecks = WindowsReleaseChecks.CreateModern(true);
        readonly Forms.ToolStripMenuItem releaseMenu = new Forms.ToolStripMenuItem("Windows updates & installation…");
        ReleaseView releaseView; ContentDialog releaseDialog; Task releasePulse; bool releaseEnding; Action releaseRender;
        string releaseStatus;
        void UpdateReleaseActivity() {
            if (releaseChecks == null) return;
            releaseChecks.SetAutomatic(settings == null || settings.AutomaticUpdatesEnabled);
            if (releaseView != null) { releaseView.SetAvailable(!closed && !releaseEnding && working && confirmation == releaseDialog && releaseDialog != null && !inventory.Scanning && !automaticReading && !DefaultsPanel.Busy && !ProfilesPanel.Busy && !SettingsPanel.Busy); if (releaseRender != null) releaseRender(); }
            else { releaseChecks.SetReviewOpen(StorageBusy || confirmation != null); releaseChecks.PublishIdle(!closed && started && !StorageBusy && !inventory.Scanning && confirmation == null); }
        }
        async Task PulseModernReleases() {
            if (closed || !started || releasePulse != null && !releasePulse.IsCompleted) return;
            try {
                UpdateReleaseActivity(); var operation = releaseChecks.PulseAsync(); releasePulse = operation; var result = await operation;
                if (closed || !releaseChecks.TryPresent(result)) return;
                releaseStatus = result.Message; releaseMenu.ToolTipText = releaseStatus;
                if (result.Outcome == WindowsUpdateCheckOutcome.Accepted && result.Offer.Release.Compare(ApplicationInformation.Version) == WindowsReleaseComparison.NewerAvailable && settings != null && settings.NotificationsEnabled)
                    tray.ShowBalloonTip(5000, "Windows release available", "Open Windows updates & installation to review signed release " + result.Offer.Release.Version + ". Installation remains manual.", Forms.ToolTipIcon.Info);
            } catch (Exception ex) { if (!closed) { releaseStatus = "Windows release check unavailable. " + ex.Message; releaseMenu.ToolTipText = releaseStatus; } }
        }
        void Releases_Click(object sender, RoutedEventArgs args) { ViewModernReleases(); }
        async void ViewModernReleases() {
            if (!BeginSavedReview()) return;
            ReleaseView owner = null; Task pendingWork = null, pendingPicker = null; ReleaseViewChoice pendingChoice = null;
            bool selecting = false; Action render = () => {};
            try {
                await EnsureVisible(); var folders = new InstallationFolders(Path.GetDirectoryName(Environment.ProcessPath), Profiles.Root);
                try { owner = new ReleaseView(WindowsReleaseViewBackend.ForModern(releaseChecks, WindowsReleaseChecks.ModernProductionChannel, ApplicationInformation.Version), folders); }
                catch { folders.Dispose(); throw; }
                releaseView = owner;
                var body = new StackPanel { Spacing = 12 };
                var page = new ComboBox { ItemsSource = new[] { "Release review", "Update history", "Installation guide" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
                var releases = new StackPanel { Spacing = 10 }; var history = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed }; var guide = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
                var status = Wrapped(releaseStatus ?? releaseChecks.Status);
                var details = Wrapped(owner.Guide(ApplicationInformation.Version, releaseChecks.Status)); details.IsTextSelectionEnabled = true;
                var releaseText = Wrapped(releaseChecks.Configured ? "Choose Check release metadata. Download and extraction remain manual." : WindowsUpdateSafety.ChannelStatus);
                var backupText = Wrapped("Review only update history from your settings backup. Restore is a separate confirmed action; other settings and media are retained.");
                var confirmText = Wrapped(""); var confirmBody = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
                var check = new Button { Content = "Check release metadata" }; var archive = new Button { Content = "Verify selected ZIP…" }; var extracted = new Button { Content = "Verify extracted folder…" };
                var backup = new Button { Content = "Review history backup…" }; var restore = new Button { Content = "Restore reviewed history…" };
                var stop = new Button { Content = "Stop review" }; var cancelBackup = new Button { Content = "Cancel backup review" };
                var keepHistory = new Button { Content = "Keep current history" }; var applyHistory = new Button { Content = "Restore reviewed history" };
                var openApp = new Button { Content = "Open app folder" }; var openSettings = new Button { Content = "Open settings folder" };
                var releaseActions = new ActionWrapPanel(); releaseActions.Children.Add(check); releaseActions.Children.Add(archive); releaseActions.Children.Add(extracted);
                var recoveryActions = new ActionWrapPanel(); recoveryActions.Children.Add(backup); recoveryActions.Children.Add(restore); recoveryActions.Children.Add(cancelBackup);
                var confirmActions = new ActionWrapPanel(); confirmActions.Children.Add(keepHistory); confirmActions.Children.Add(applyHistory);
                var folderActions = new ActionWrapPanel(); folderActions.Children.Add(openApp); folderActions.Children.Add(openSettings);
                releases.Children.Add(releaseText); releases.Children.Add(releaseActions); history.Children.Add(backupText); history.Children.Add(recoveryActions);
                confirmBody.Children.Add(confirmText); confirmBody.Children.Add(confirmActions); history.Children.Add(confirmBody); guide.Children.Add(details); guide.Children.Add(folderActions);
                body.Children.Add(page); body.Children.Add(releases); body.Children.Add(history); body.Children.Add(guide); body.Children.Add(status); body.Children.Add(stop);
                var dialog = MessageDialog("Windows updates & installation", "", null); dialog.Content = Scrolling(body);
                confirmation = releaseDialog = dialog; UpdateReleaseActivity();
                render = () => {
                    if (closed || releaseEnding || confirmation != dialog || !owner.Open) return;
                    check.IsEnabled = owner.CanAction(WindowsReleaseReviewAction.Check); archive.IsEnabled = owner.CanAction(WindowsReleaseReviewAction.Archive); extracted.IsEnabled = owner.CanAction(WindowsReleaseReviewAction.Extracted);
                    backup.IsEnabled = owner.CanAction(WindowsReleaseReviewAction.Backup); restore.IsEnabled = owner.CanAction(WindowsReleaseReviewAction.Restore);
                    cancelBackup.IsEnabled = !selecting && !owner.Busy && owner.CanAction(WindowsReleaseReviewAction.Restore);
                    openApp.IsEnabled = owner.CanOpenFolder(false); openSettings.IsEnabled = owner.CanOpenFolder(true);
                    stop.IsEnabled = owner.Busy && !owner.Nested; page.IsEnabled = !owner.Busy;
                    keepHistory.IsEnabled = applyHistory.IsEnabled = pendingChoice != null && !selecting;
                    confirmBody.Visibility = pendingChoice != null && !selecting ? Visibility.Visible : Visibility.Collapsed;
                };
                Func<Task<ReleaseViewResult>, Task> finish = async operation => {
                    pendingWork = operation; render();
                    try {
                        var result = await operation; if (closed || releaseEnding || confirmation != dialog) return;
                        if (result == null) { status.Text = "Choice cancelled. No new action started."; return; }
                        if (!result.TryPresent()) { status.Text = "Review ended after activity or ownership changed. Any already published restrictive history is retained; reread before another manual action."; return; }
                        status.Text = result.Message;
                        if (result.Action == WindowsReleaseReviewAction.Backup || result.Action == WindowsReleaseReviewAction.Restore) { backupText.Text = result.Description.Length == 0 ? result.Message : result.Description; releaseText.Text = "Check release metadata again before reviewing release files."; }
                        else { releaseText.Text = result.Description.Length == 0 ? result.Message : result.Description; backupText.Text = "No history backup is selected. Restore requires a fresh manual review."; }
                    } finally { if (!closed && confirmation == dialog) { UpdateReleaseActivity(); render(); } }
                };
                Func<WindowsReleaseReviewAction, Task> pick = async action => {
                    if (!owner.CanAction(action)) return; pendingChoice = owner.BeginChoice(action); selecting = true; UpdateReleaseActivity(); render(); string path = null;
                    try {
                        if (action == WindowsReleaseReviewAction.Extracted) {
                            var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id) { CommitButtonText = "Review extracted folder" };
                            var selected = await picker.PickSingleFolderAsync(); path = selected == null ? null : selected.Path;
                        } else {
                            var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id) { CommitButtonText = action == WindowsReleaseReviewAction.Backup ? "Review history backup" : "Verify ZIP" };
                            picker.FileTypeFilter.Add(action == WindowsReleaseReviewAction.Backup ? ".json" : ".zip"); var selected = await picker.PickSingleFileAsync(); path = selected == null ? null : selected.Path;
                        }
                    } catch (Exception ex) { owner.Cancel(); status.Text = "Selection unavailable. " + ex.Message; }
                    finally { selecting = false; }
                    var choice = pendingChoice; pendingChoice = null;
                    try { await finish(owner.FinishSelection(choice, path)); } catch (Exception ex) { status.Text = "Selection was not applied. " + ex.Message; UpdateReleaseActivity(); render(); }
                };
                check.Click += async delegate { try { await finish(owner.Check()); } catch (Exception ex) { status.Text = ex.Message; render(); } };
                Func<WindowsReleaseReviewAction, Task> beginPick = async action => { if (pendingPicker != null && !pendingPicker.IsCompleted) return; pendingPicker = pick(action); await pendingPicker; };
                archive.Click += async delegate { await beginPick(WindowsReleaseReviewAction.Archive); }; extracted.Click += async delegate { await beginPick(WindowsReleaseReviewAction.Extracted); }; backup.Click += async delegate { await beginPick(WindowsReleaseReviewAction.Backup); };
                restore.Click += delegate {
                    if (!owner.CanAction(WindowsReleaseReviewAction.Restore)) return; pendingChoice = owner.BeginChoice(WindowsReleaseReviewAction.Restore);
                    confirmText.Text = pendingChoice.Description + "\n\nRestore only this reviewed restrictive update history? Other settings and media are retained. Keep current history cancels.";
                    UpdateReleaseActivity(); render(); keepHistory.Focus(FocusState.Programmatic);
                };
                Func<bool, Task> confirm = async accepted => {
                    var choice = pendingChoice; pendingChoice = null;
                    try { await finish(owner.FinishConfirmation(choice, accepted)); } catch (Exception ex) { status.Text = "Restore was not started. " + ex.Message; UpdateReleaseActivity(); render(); }
                };
                keepHistory.Click += async delegate { await confirm(false); }; applyHistory.Click += async delegate { await confirm(true); };
                stop.Click += delegate { owner.Cancel(); status.Text = "Stopping review. Waiting for its exact worker; any already published history is retained."; render(); };
                cancelBackup.Click += delegate { owner.Cancel(); backupText.Text = "Backup review cancelled. Restore requires a new review."; render(); };
                openApp.Click += delegate { try { owner.OpenFolder(false); } catch (Exception ex) { status.Text = ex.Message; } }; openSettings.Click += delegate { try { owner.OpenFolder(true); } catch (Exception ex) { status.Text = ex.Message; } };
                page.SelectionChanged += delegate { releases.Visibility = page.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed; history.Visibility = page.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; guide.Visibility = page.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed; };
                dialog.Closing += delegate(ContentDialog sender, ContentDialogClosingEventArgs args) {
                    if (pendingChoice != null && !selecting && owner.Nested) { var cancelled = owner.FinishConfirmation(pendingChoice, false); pendingChoice = null; }
                    if (!owner.CanClose || selecting) { args.Cancel = true; owner.Cancel(); status.Text = "Wait for the current picker or release worker before closing."; render(); }
                };
                releaseRender = render; render(); await dialog.ShowAsync();
            } catch (Exception ex) { if (!closed) Summary.Text = "Windows update review did not finish. " + ex.Message; }
            finally {
                releaseEnding = true; releaseRender = null; if (owner != null) { owner.SetAvailable(false); owner.Cancel(); }
                if (pendingPicker != null) try { await pendingPicker; } catch { }
                if (pendingWork != null) try { await pendingWork; } catch { }
                if (pendingChoice != null && owner != null) try { if (selecting) await owner.FinishSelection(pendingChoice, null); else await owner.FinishConfirmation(pendingChoice, false); } catch { }
                if (owner != null) owner.Dispose(); releaseView = null; releaseDialog = null; releaseEnding = false; releaseChecks.SetReviewOpen(false); FinishSavedReview();
            }
        }
    }
}
