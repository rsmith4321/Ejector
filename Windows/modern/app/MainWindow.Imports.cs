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
#if UI_TESTS
            if (UiTestHost.Review) return;
#endif
            if (automaticPumping || StorageBusy || inventory.Scanning || closed || !notifications.Available || settings == null) return;
            automaticPumping = true;
            try {
                while (!closed && !StorageBusy && !inventory.Scanning) {
                    var state = inventory.Current; if (state == null || !state.Complete || state.Epoch != discovery.Epoch) break;
                    Tuple<SavedProfile[], ImportDefaults> saved = null;
                    automaticReading = true; UpdateActions();
                    try { saved = await Task.Run(() => Tuple.Create(Profiles.Catalog(), ImportDefaults.Load())); }
                    catch (Exception ex) {
                        if (automaticConfigIssueEpoch != state.Epoch) { automaticConfigIssueEpoch = state.Epoch; Summary.Text = "Automatic import could not read saved choices. No import started. " + ex.Message; RecordModernIssue(Summary.Text); }
                    } finally { automaticReading = false; UpdateActions(); }
                    if (saved == null || closed || StorageBusy) break;
                    state = inventory.Current; if (state == null || !state.Complete || state.Epoch != discovery.Epoch) break;
                    if (saved.Item1.Any(p => !p.CanEdit && state.Volumes.Any(v => Profiles.FileName(v.Id) == p.FileName)) && automaticConfigIssueEpoch != state.Epoch) {
                        automaticConfigIssueEpoch = state.Epoch; Summary.Text = "A connected device's saved profile needs review and was retained. Automatic import never uses an invalid record.";
                        RecordModernIssue(Summary.Text);
                    }
                    var request = automatic.Take(state, saved.Item1.Where(p => p.CanEdit).Select(p => p.Profile), saved.Item2, StorageBusy || inventory.Scanning);
                    if (request == null) break;
                    await RunImport(request.Connection, request); await ReadInventory();
                }
            } finally { automaticPumping = false; }
        }
        async Task ImportSavedProfile(SavedProfile entry, Action handoff) {
            if (closed || working || inventory.Scanning || !notifications.Available || ProfilesPanel.Visibility != Visibility.Visible)
                throw new SafetyException("Wait for current work and connection monitoring, then choose Import now again.");
            var state = inventory.Current; var choice = SavedProfileImportChoice.Capture(entry, state, discovery.Epoch);
            automatic.MarkManual(state, choice.Connection.Volume);
            StorageCards.SelectedItem = StorageCards.Items.Cast<StorageCard>().SingleOrDefault(card => card.Connection != null && card.Connection.Token == choice.Connection.Token);
            bool transferred = false; ShowStorage();
            try {
                await RunImport(choice.Connection, null, choice, () => { transferred = true; handoff(); }); await ReadInventory();
            } catch (Exception ex) {
                if (!transferred) throw;
                if (!closed) { Summary.Text = "Saved-profile import status needs attention. Review Connected storage and Import history. " + ex.Message; RecordModernIssue(Summary.Text); UpdateActions(); }
            }
        }
        async Task RunImport(AutomaticConnection connection, AutomaticImportRequest request, SavedProfileImportChoice savedChoice = null, Action handoff = null) {
            if (closed || working) return;
            working = true; importOperationActive = true; operationCancel = new CancellationTokenSource(); importTerminal = null;
            ImportProgress.Visibility = Visibility.Visible; ImportProgress.IsIndeterminate = true;
            var token = activity.Begin(TrayOperationKind.Import, connection.Volume.Display, request == null ? "Reviewing import" : "Preparing automatic import");
            long observedEpoch = connection.Epoch; ImportedOutcome outcome = null; ImportOperationReview owner = null;
            UpdateActions();
            try {
                if (handoff != null) handoff();
                if (request == null) {
                    owner = await Task.Run(() => {
                        if (savedChoice != null) return savedChoice.Prepare(discovery, ImportDefaults.Load(), Profiles.Root);
                        var reviewed = Profiles.ReviewConnected(connection.Volume, connection.Disk); var choices = ImportDefaults.Load();
                        return ImportOperationReview.Manual(connection, reviewed, choices, discovery);
                    });
                } else owner = await Task.Run(() => ImportOperationReview.AutomaticRequest(request, discovery, epoch => Interlocked.Exchange(ref observedEpoch, epoch)));
                importing = owner;
                if (!owner.Automatic) {
                    await EnsureVisible(); operationCancel.Token.ThrowIfCancellationRequested(); owner.ValidateForPresentation();
                    confirmation = MessageDialog("Verified import", owner.Description, "Start verified import"); UpdateActions();
                    var answer = await confirmation.ShowAsync(); confirmation = null;
                    if (answer != ContentDialogResult.Primary) { Summary.Text = "Import review cancelled. No import started."; return; }
                    owner.Confirm();
                }
                completions.InvalidateSource(owner.Root);
                Summary.Text = "Preparing verified import…"; UpdateActions();
                var result = await StartStorageTask(token, () => Task.Run(() => owner.Execute(new VerifiedImporter(operations), operationCancel.Token,
                    message => QueueImportProgress(token, message), measured => QueueImportProgress(token, null, measured))), owner.Guard);
                importTerminal = result; var profile = owner.Profile; var guard = owner.Guard; var options = owner.Options; var root = owner.Root; var destination = owner.Destination;
                await DrainStorageTask(token);
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
                await DrainStorageTask(token);
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
            NotifyImport(outcome.HistoryWarning == null ? result.HasSelectedMedia ? "Import complete" : "No matching media" : "Import verified; history needs attention", outcome.Message, outcome.HistoryWarning != null, outcome.HistoryWarning == null);
            // The worker has ended and released folders before any completion action.
            working = false; UpdateActions();
            if (completion != null && CompletionPresentation.ShowChoice(options.AskToEject, AppWindow.IsVisible, settings.NotificationsEnabled)) { await EnsureVisible(); await ReviewImportCompletion(completion.Id); }
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
            var presentation = TrayPresentation.Create(activity, UiBusy, inventory.Scanning, TrayPresentation.CardCount(inventory.Current), appIssue.NeedsAttention);
            tray.Text = presentation.Tooltip; tray.Icon = presentation.Busy ? trayIcons.Busy : presentation.Attention ? trayIcons.Attention : appIcon;
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
        void NotifyImport(string title, string message, bool warning, bool resolved = false) {
            if (warning) RecordModernIssue(message); else if (resolved) ClearModernIssue();
            string warningText = LogModernOperation(title + " | " + message); if (warningText != null) Summary.Text += " " + warningText;
            string deliveryWarning = TryModernNotification(title, message, warning, settings != null && settings.NotificationsEnabled);
            if (deliveryWarning != null) Summary.Text += " " + deliveryWarning;
        }
        async void ReviewPendingImports() {
            if (StorageBusy || inventory.Scanning) return; var values = completions.Entries;
            if (values.Length == 0) { Summary.Text = "No current import eject choices. Exact saved batches remain in Import history."; return; }
            working = true; string selected = null;
            try {
                await EnsureVisible();
                var list = new ListView { ItemsSource = values.Select(v => v.DeviceName + " · " + v.Message).ToArray(), SelectionMode = ListViewSelectionMode.Single, MaxHeight = 300 };
                var dialog = PendingImportsDialog(list);
                confirmation = dialog; bool selecting = true;
                SelectionChangedEventHandler selection = delegate {
                    if (selecting && !closed && Object.ReferenceEquals(confirmation, dialog)) dialog.IsPrimaryButtonEnabled = list.SelectedIndex >= 0 && list.SelectedIndex < values.Length;
                };
                list.SelectionChanged += selection;
                try {
                    UpdateActions(); var answer = await dialog.ShowAsync(); int index = list.SelectedIndex;
                    if (!dialog.SafeInitialFocus) throw new SafetyException("Completed imports review could not focus Close. Open a fresh review.");
                    if (answer == ContentDialogResult.Primary && index >= 0 && index < values.Length) selected = values[index].Id;
                } finally { selecting = false; list.SelectionChanged -= selection; }
            } catch (Exception ex) { Summary.Text = "Completion list unavailable. " + ex.Message; }
            finally { confirmation = null; working = false; UpdateActions(); }
            if (selected != null) await ReviewImportCompletion(selected);
        }
        KeepConnectedDialog PendingImportsDialog(ListView list) {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, "Completed imports awaiting review");
            list.ItemTemplate = (DataTemplate)Root.Resources["PendingImportRowTemplate"];
            list.ItemContainerStyle = (Style)Root.Resources["PendingImportRowStyle"];
            ScrollViewer.SetHorizontalScrollMode(list, ScrollMode.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            return new KeepConnectedDialog { XamlRoot = Root.XamlRoot, Title = "Completed imports", Content = list,
                PrimaryButtonText = "Review import", CloseButtonText = "Close", IsPrimaryButtonEnabled = false };
        }
        async Task ReviewImportCompletion(string id) {
            if (StorageBusy || inventory.Scanning) return; var entry = completions.Get(id);
            if (entry == null) { Summary.Text = "This import eject choice expired. Refresh and review the connected device."; return; }
            working = true; activeCompletionId = id; EjectResult terminal = null; CleanupCounts completionCounts = null; UpdateActions();
            try {
                await EnsureVisible();
                string shareMessage = "";
                while (true) {
                    var freshSettings = await Task.Run(() => AppSettings.Load()); MetadataCleanupPlan cleanup = null;
                    bool canShare = false, gyroflowFirst = false; string sharingNote = "";
                    if (entry.Batch != null) {
                        try { canShare = await Task.Run(() => SharingService.CanOffer(entry.Batch)); gyroflowFirst = canShare && await Task.Run(() => SharingService.GyroflowFirst(entry.Batch)); }
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
                        + CompletionPresentation.OtherStorageWarning(entry)
                        + "\n\nKeep Connected for Lightroom or other transfers. Eject requests removal of the physical device and all affected volumes."
                        + (entry.Origin.Plan == null ? "\n" + entry.Origin.BlockedReason : "\n\nAffected storage:\n" + String.Join("\n", entry.Origin.Plan.Affected))
                        + (cleanup == null ? "" : "\n\n" + cleanup.Description) + sharingNote
                        + (gyroflowFirst ? "\n\n" + QuickShareProfile.GyroflowGuidance + " Quick Share is available afterwards in Import history." : "") + shareMessage;
                    var dialog = new KeepConnectedDialog { XamlRoot = Root.XamlRoot, Title = CompletionPresentation.ChoiceTitle(entry.HasMedia),
                        Content = new ScrollViewer { MaxHeight = 360, Content = Wrapped(message) },
                        PrimaryButtonText = cleanup == null ? CompletionPresentation.EjectLabel(entry) : "Clean & eject",
                        SecondaryButtonText = "Open Import Folder", IsPrimaryButtonEnabled = canEject };
                    confirmation = dialog;
                    bool sharingRequested = false, choosing = true; Button share = null; RoutedEventHandler shareChoice = null;
                    if (canShare && !gyroflowFirst) {
                        var body = new StackPanel { Spacing = 16 }; body.Children.Add(Wrapped(message));
                        share = new Button { Content = "Quick Share…" }; body.Children.Add(share);
                        shareChoice = delegate { if (!choosing || closed || !Object.ReferenceEquals(confirmation, dialog)) return; sharingRequested = true; dialog.Hide(); };
                        share.Click += shareChoice;
                        confirmation.Content = Scrolling(body);
                    }
                    ContentDialogResult answer;
                    try { UpdateActions(); answer = await dialog.ShowAsync(); }
                    finally { choosing = false; if (share != null) share.Click -= shareChoice; }
                    confirmation = null;
                    if (!dialog.SafeInitialFocus) throw new SafetyException("Completion review could not focus Keep Connected. No removal was requested. Open a fresh review.");
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
                    Summary.Text = terminal.Message; NotifyImport(terminal.Success ? "Safe removal accepted" : "Device still connected", terminal.Message, !terminal.Success, terminal.Success);
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
