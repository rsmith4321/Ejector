using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Forms = System.Windows.Forms;

namespace EasyEject.Modern {
    public sealed partial class MainWindow {
        readonly ImportCompletionRegistry completions = new ImportCompletionRegistry();
        readonly TrayActivity activity = new TrayActivity();
        ImportOperationReview importing;
        ImportResult importTerminal;
        string activeCompletionId;
        bool automaticReading, automaticPumping, importOperationActive;
        long? automaticConfigIssueEpoch;
        async void Import_Click(object sender, RoutedEventArgs args) {
            var card = StorageCards.SelectedItem as StorageCard;
            if (StorageBusy || inventory.Scanning || !notifications.Available || card == null || card.Connection == null) return;
            automatic.MarkManual(inventory.Current, card.Connection.Volume);
            await RunImport(card.Connection, null); await ReadInventory();
        }
        async Task PumpAutomaticImports() {
            if (automaticPumping || StorageBusy || inventory.Scanning || closed || !notifications.Available || settings == null) return;
            automaticPumping = true;
            try {
                while (!closed && !StorageBusy && !inventory.Scanning) {
                    var state = inventory.Current; if (state == null || !state.Complete || state.Epoch != discovery.Epoch) break;
                    Tuple<SavedProfile[], ImportDefaults> saved = null;
                    automaticReading = true; UpdateActions();
                    try { saved = await Task.Run(() => Tuple.Create(Profiles.Catalog(), ImportDefaults.Load())); }
                    catch (Exception ex) {
                        if (automaticConfigIssueEpoch != state.Epoch) { automaticConfigIssueEpoch = state.Epoch; Summary.Text = "Automatic import could not read saved choices. No import started. " + ex.Message; }
                    } finally { automaticReading = false; UpdateActions(); }
                    if (saved == null || closed || StorageBusy) break;
                    state = inventory.Current; if (state == null || !state.Complete || state.Epoch != discovery.Epoch) break;
                    if (saved.Item1.Any(p => !p.CanEdit && state.Volumes.Any(v => Profiles.FileName(v.Id) == p.FileName)) && automaticConfigIssueEpoch != state.Epoch) {
                        automaticConfigIssueEpoch = state.Epoch; Summary.Text = "A connected device's saved profile needs review and was retained. Automatic import never uses an invalid record.";
                    }
                    var request = automatic.Take(state, saved.Item1.Where(p => p.CanEdit).Select(p => p.Profile), saved.Item2, StorageBusy || inventory.Scanning);
                    if (request == null) break;
                    await RunImport(request.Connection, request); await ReadInventory();
                }
            } finally { automaticPumping = false; }
        }
        async Task RunImport(AutomaticConnection connection, AutomaticImportRequest request) {
            if (closed || working) return;
            working = true; importOperationActive = true; operationCancel = new CancellationTokenSource(); importTerminal = null;
            ImportProgress.Visibility = Visibility.Visible; ImportProgress.IsIndeterminate = true;
            var token = activity.Begin(TrayOperationKind.Import, connection.Volume.Display, request == null ? "Reviewing import" : "Preparing automatic import");
            long observedEpoch = connection.Epoch; ImportedOutcome outcome = null; ImportOperationReview owner = null;
            UpdateActions();
            try {
                if (request == null) {
                    owner = await Task.Run(() => {
                        var reviewed = Profiles.ReviewConnected(connection.Volume, connection.Disk); var choices = ImportDefaults.Load();
                        return ImportOperationReview.Manual(connection, reviewed, choices, discovery);
                    });
                } else owner = await Task.Run(() => ImportOperationReview.AutomaticRequest(request, discovery, epoch => Interlocked.Exchange(ref observedEpoch, epoch)));
                importing = owner;
                if (!owner.Automatic) {
                    confirmation = MessageDialog("Verified import", owner.Description, "Start verified import"); UpdateActions();
                    var answer = await confirmation.ShowAsync(); confirmation = null;
                    if (answer != ContentDialogResult.Primary) { Summary.Text = "Import review cancelled. No import started."; return; }
                    owner.Confirm();
                }
                completions.InvalidateSource(owner.Root);
                Summary.Text = "Preparing verified import…"; UpdateActions();
                var result = await Task.Run(() => owner.Execute(new VerifiedImporter(operations), operationCancel.Token,
                    message => QueueImportProgress(token, message), measured => QueueImportProgress(token, null, measured)));
                importTerminal = result; var profile = owner.Profile; var guard = owner.Guard; var options = owner.Options; var root = owner.Root; var destination = owner.Destination;
                operationCancel.Dispose(); operationCancel = null; importOperationActive = false; UpdateActions();
                owner.Dispose(); owner = null; importing = null; ImportProgress.IsIndeterminate = false; ImportProgress.Value = 100;
                await PresentVerifiedImport(result, profile, guard, root, destination, options, completed => outcome = completed);
            } catch (OperationCanceledException) {
                Summary.Text = importTerminal != null ? outcome == null ? "Import verified. " + importTerminal.Files + " files saved; completion display unavailable." : outcome.Message
                    : "Import cancelled. Completed verified copies and remaining source/Trash data are retained. No completion receipt was written; no automatic retry occurs on this connection.";
            } catch (Exception ex) {
                if (request != null && importTerminal == null && (owner == null || !owner.EngineStarted)) automatic.PreflightWaiting(request, Interlocked.Read(ref observedEpoch));
                Summary.Text = importTerminal != null ? (outcome == null ? "Import verified. " + importTerminal.Files + " files saved." : outcome.Message) + " Completion review unavailable: " + ex.Message
                    : "Import stopped. No automatic retry of copying/removal on this connection. " + ex.Message;
                NotifyImport(importTerminal == null ? "Import needs attention" : "Import verified; review needs attention", Summary.Text, true);
            } finally {
                if (owner != null) owner.Dispose(); importing = null; confirmation = null;
                if (operationCancel != null) { operationCancel.Dispose(); operationCancel = null; }
                working = false; importOperationActive = false; ImportStatus.Text = Summary.Text; ImportProgress.Visibility = Visibility.Collapsed; activity.End(Summary.Text); UpdateImportTray(); UpdateActions();
            }
        }
        async Task PresentVerifiedImport(ImportResult result, ImportProfile profile, VolumeImportGuard guard, string root, string destination, MediaOptions options, Action<ImportedOutcome> retainOutcome) {
            var outcome = await Task.Run(() => new ImportedOutcome(result, profile, guard, root, discovery, ImportHistory.Add));
            retainOutcome(outcome); // Preserve exact counts/history before any presentation callback.
            Summary.Text = outcome.Message; activity.End(outcome.Message); UpdateImportTray();
            ImportCompletion completion = null;
            if (options.AskToEject && outcome.SourceCurrent) {
                completion = new ImportCompletion(guard.CompletionOrigin, ProfileName.Display(profile), result.Files == 0 ? destination : result.Folder,
                    destination, guard.DestinationIdentity, result.HasSelectedMedia, outcome.Message, outcome.Batch);
                completions.Insert(completion);
            }
            NotifyImport(outcome.HistoryWarning == null ? result.HasSelectedMedia ? "Import complete" : "No matching media" : "Import verified; history needs attention", outcome.Message, outcome.HistoryWarning != null);
            // The worker has ended and released folders before any completion action.
            working = false; UpdateActions();
            if (completion != null && CompletionPresentation.ShowChoice(options.AskToEject, AppWindow.IsVisible, settings.NotificationsEnabled)) await ReviewImportCompletion(completion.Id);
            else if (AppWindow.IsVisible) {
                working = true; confirmation = MessageDialog(result.HasSelectedMedia ? "Import complete" : "No matching media", outcome.Message, null);
                UpdateActions(); await confirmation.ShowAsync(); confirmation = null;
            }
        }
        void QueueImportProgress(TrayOperation token, string message, ImportWorkProgress measured = null) {
            DispatcherQueue.TryEnqueue(delegate {
                if (closed || !activity.Report(token, message, measured)) return;
                if (message != null) Summary.Text = message;
                ImportStatus.Text = message ?? (measured == null ? "Preparing import…" : measured.Phase);
                ImportProgress.IsIndeterminate = !activity.Percent.HasValue; ImportProgress.Value = activity.Percent ?? 0;
                UpdateImportTray();
            });
        }
        void UpdateImportTray() {
            if (closed) return;
            var presentation = TrayPresentation.Create(activity, UiBusy, inventory.Scanning, inventory.Current == null ? 0 : inventory.Current.Volumes.Count(v => v.IsCard), false);
            tray.Text = presentation.Tooltip; tray.Icon = presentation.Busy ? trayIcons.Busy : appIcon;
        }
        void RequestOperationStop() {
            if (operationCancel == null) return; operationCancel.Cancel(); activity.RequestStop();
            if (confirmation != null && (importing != null || resuming != null || groupedReview != null)) confirmation.IsPrimaryButtonEnabled = false;
            Summary.Text = quickShareSetupSaving ? "Stopping setup validation. Waiting for the current file read/write…"
                : activity.Current != null && activity.Current.Kind == TrayOperationKind.Share ? "Stopping Quick Share. Saved originals and earlier verified sharing copies are kept. Retry is manual."
                : activity.Current != null && activity.Current.Kind == TrayOperationKind.Verify ? "Stopping saved-file verification. Files remain unchanged."
                : groupedReview != null ? "Stopping card removal before the next request. Wait for verified results before unplugging."
                : importOperationActive ? "Stopping after the current check. Verified copies and remaining source/Trash data are retained." : "Stopping cleanup before removal. Ordinary media and remaining metadata are retained.";
            UpdateImportTray(); UpdateActions();
        }
        void NotifyImport(string title, string message, bool warning) {
            string warningText = LogModernOperation(title + " | " + message); if (warningText != null) Summary.Text += " " + warningText;
            if (settings == null || !settings.NotificationsEnabled || closed) return;
            try { tray.ShowBalloonTip(5000, title.Length > 63 ? title.Substring(0, 63) : title, message.Length > 255 ? message.Substring(0, 255) : message, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info); }
            catch (Exception ex) { Summary.Text += " Notification unavailable: " + ex.Message + " Review the result in Easy Eject."; }
        }
        async void ReviewPendingImports() {
            if (StorageBusy || inventory.Scanning) return; var values = completions.Entries;
            if (values.Length == 0) { Summary.Text = "No current import eject choices. Exact saved batches remain in Import history."; return; }
            working = true; string selected = null;
            try {
                var list = new ListView { ItemsSource = values.Select(v => v.DeviceName + " · " + v.Message).ToArray(), SelectionMode = ListViewSelectionMode.Single, MaxHeight = 300 };
                confirmation = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Completed imports", Content = list, PrimaryButtonText = "Review import", CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false };
                list.SelectionChanged += delegate { confirmation.IsPrimaryButtonEnabled = list.SelectedIndex >= 0; };
                UpdateActions(); if (await confirmation.ShowAsync() == ContentDialogResult.Primary && list.SelectedIndex >= 0) selected = values[list.SelectedIndex].Id;
            } catch (Exception ex) { Summary.Text = "Completion list unavailable. " + ex.Message; }
            finally { confirmation = null; working = false; UpdateActions(); }
            if (selected != null) await ReviewImportCompletion(selected);
        }
        async Task ReviewImportCompletion(string id) {
            if (StorageBusy || inventory.Scanning) return; var entry = completions.Get(id);
            if (entry == null) { Summary.Text = "This import eject choice expired. Refresh and review the connected device."; return; }
            working = true; activeCompletionId = id; EjectResult terminal = null; CleanupCounts completionCounts = null; UpdateActions();
            try {
                string shareMessage = "";
                while (true) {
                    var freshSettings = await Task.Run(() => AppSettings.Load()); MetadataCleanupPlan cleanup = null;
                    bool canShare = false; string sharingNote = "";
                    if (entry.Batch != null) {
                        try { canShare = await Task.Run(() => SharingService.CanOffer(entry.Batch)); }
                        catch (Exception ex) { sharingNote = "\n\nSaved sharing choices need review. " + ex.Message; }
                    }
                    bool canEject = await Task.Run(() => {
                        try {
                            var state = discovery.Scan(); bool current = state.Epoch == discovery.Epoch && completions.Matches(id, state, WindowsDiscovery.VolumeId(entry.Origin.SourceRoot), entry.Origin.SourceRoot);
                            if (current && entry.Origin.SourceIsCard && freshSettings.CleanupCardsEnabled) cleanup = MetadataCleanupPlan.Single(state, entry.Origin.Plan, entry.Origin.VolumeId);
                            return current && entry.Origin.Plan != null;
                        } catch (Exception) { return false; }
                    });
                    string message = entry.Message + (entry.HasMedia ? "" : "\n\nPhotos, skipped previews or other files may remain on the device.")
                        + "\n\nKeep Connected for Lightroom or other transfers. Eject requests removal of the physical device and all affected volumes."
                        + (entry.Origin.Plan == null ? "\n" + entry.Origin.BlockedReason : "\n\nAffected storage:\n" + String.Join("\n", entry.Origin.Plan.Affected))
                        + (cleanup == null ? "" : "\n\n" + cleanup.Description) + sharingNote + shareMessage;
                    confirmation = MessageDialog(entry.HasMedia ? "Import complete. Eject now?" : "No matching media. Eject now?", message, cleanup == null ? "Eject Now" : "Clean & eject");
                    confirmation.CloseButtonText = "Keep Connected"; confirmation.SecondaryButtonText = "Open Import Folder"; confirmation.IsPrimaryButtonEnabled = canEject;
                    bool sharingRequested = false;
                    if (canShare) {
                        var body = new StackPanel { Spacing = 16 }; body.Children.Add(Wrapped(message));
                        var share = new Button { Content = "Quick Share…" }; body.Children.Add(share); var dialog = confirmation;
                        share.Click += delegate { sharingRequested = true; dialog.Hide(); };
                        confirmation.Content = Scrolling(body);
                    }
                    UpdateActions(); var answer = await confirmation.ShowAsync(); confirmation = null;
                    if (sharingRequested) {
                        bool shared = await PerformQuickShare(entry.Batch);
                        if (!QuickSharePresentation.ReturnToCompletion(completions, entry, shared)) break;
                        shareMessage = "\n\n" + Summary.Text;
                        continue;
                    }
                    if (answer == ContentDialogResult.Secondary) {
                        await Task.Run(() => CompletionFolder.Open(entry, entry.Destination, () => new WindowsDestinationGuard(discovery, entry.DestinationRoot, entry.Destination), operations,
                            folder => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + folder + "\"", UseShellExecute = false })));
                        continue;
                    }
                    if (answer != ContentDialogResult.Primary) { Summary.Text = "Device kept connected. " + entry.Message; break; }
                    var counts = new CleanupCounts(); completionCounts = counts; operationCancel = new CancellationTokenSource(); UpdateActions();
                    terminal = await Task.Run(() => new CompletedEjectService(completions, discovery, validate => new WindowsEjectApi(validate), operations, WindowsDiscovery.VolumeId).ExecuteReviewed(id, (initial, validate) => {
                        if (cleanup == null) return; cleanup.Preflight(initial, WindowsDiscovery.VolumeId);
                        cleanup.Clean(validate, WindowsDiscovery.VolumeId, operationCancel.Token, counts, messageText => DispatcherQueue.TryEnqueue(delegate { if (!closed) Summary.Text = messageText; }));
                    }, operationCancel.Token));
                    if (cleanup != null) terminal.CleanupMessage = counts.Message;
                    Summary.Text = terminal.Message; NotifyImport(terminal.Success ? "Safe removal accepted" : "Device still connected", terminal.Message, !terminal.Success);
                    confirmation = MessageDialog(terminal.Success ? "Safe removal accepted" : "Device still connected", terminal.Message, null); await confirmation.ShowAsync(); confirmation = null; break;
                }
            } catch (Exception ex) { Summary.Text = terminal == null ? "Import remains verified. Completion action stopped. " + ex.Message + (completionCounts == null ? "" : " " + completionCounts.Message) : terminal.Message + " Completion display unavailable: " + ex.Message; }
            finally {
                if (operationCancel != null) { operationCancel.Dispose(); operationCancel = null; }
                activeCompletionId = null; confirmation = null; working = false; UpdateActions();
            }
        }
    }
}
