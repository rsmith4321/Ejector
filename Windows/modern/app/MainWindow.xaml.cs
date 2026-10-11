using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Forms = System.Windows.Forms;

namespace EasyEject.Modern {
    public sealed class StorageCard {
        public string Title { get; set; }
        public string Detail { get; set; }
        public string State { get; set; }
        internal TrayStorageChoice Choice;
        internal AutomaticConnection Connection;
    }
    public sealed partial class MainWindow : Window {
        readonly WindowsDiscovery discovery = new WindowsDiscovery();
        readonly Operations operations = new Operations();
        readonly AutomaticImportScheduler automatic = new AutomaticImportScheduler();
        readonly StorageInventorySession inventory;
        readonly StorageNotificationWindow notifications;
        readonly DispatcherQueueTimer pulse;
        readonly Forms.NotifyIcon tray;
        readonly System.Drawing.Icon appIcon = AppIcons.Load();
        readonly TrayBusyIcons trayIcons = new TrayBusyIcons();
        readonly Forms.ToolStripMenuItem storageMenu = new Forms.ToolStripMenuItem("Connected storage");
        AppSettings settings;
        ManualEjectReview activeReview;
        ContentDialog confirmation;
        CancellationTokenSource operationCancel;
        bool quitting, working, closed, started, returnToProfile;
        readonly System.Threading.Tasks.TaskCompletionSource<bool> visibleRoot = new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        Task runtimeStart; readonly WindowReopenRequests reopen;
        bool UiBusy { get { return working || concurrentWork != null && concurrentWork.State.Busy || automaticReading || DefaultsPanel.Busy || ProfilesPanel.Busy || SettingsPanel.Busy; } }
        bool EditorOpen { get { return DefaultsPanel.Editing || ProfilesPanel.Editing || SettingsPanel.Editing || DefaultsPanel.Visibility == Visibility.Visible || ProfilesPanel.Visibility == Visibility.Visible || SettingsPanel.Visibility == Visibility.Visible; } }
        bool StorageBusy { get { return UiBusy || EditorOpen; } }
        public MainWindow() {
            InitializeComponent(); concurrentWork = new ConcurrentStorageWork(operations); SystemBackdrop = new MicaBackdrop();
            SettingsPanel.ReleaseDescription = () => releaseChecks.Status;
            reopen = new WindowReopenRequests(action => DispatcherQueue.TryEnqueue(() => action()), () => { if (!closed) { AppWindow.Show(); Activate(); } });
            AppWindow.SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(appIcon.Handle));
            shortcut = new HotkeyMessageWindow(() => StorageBusy || closed || inventory == null || inventory.Scanning || !notifications.Available, action => DispatcherQueue.TryEnqueue(() => action()), () => { Reveal(); ReviewCards(); });
            SettingsPanel.ShortcutDescription = () => shortcutWarning ?? shortcut.Status;
            SettingsPanel.Saved += SettingsSaved;
            SettingsPanel.Completed += delegate(string message) { ShowStorage(); Summary.Text = settingsRuntimeMessage == null ? message : settingsRuntimeMessage + "\n" + message; settingsRuntimeMessage = null; UpdateActions(); };
            inventory = new StorageInventorySession(discovery);
            notifications = new StorageNotificationWindow(discovery, () => StorageBusy, () => automatic.PowerBoundary());
            pulse = DispatcherQueue.CreateTimer(); pulse.Interval = TimeSpan.FromMilliseconds(500);
            pulse.Tick += async delegate {
                if (closed) return;
                if (inventory.Observe()) {
                    completions.InvalidateNotifications();
                    if (confirmation != null && activeReview != null && !activeReview.Current) confirmation.IsPrimaryButtonEnabled = false;
                    if (confirmation != null && importing != null && !importing.Current) confirmation.IsPrimaryButtonEnabled = false;
                    if (confirmation != null && resuming != null && !resuming.Current) confirmation.IsPrimaryButtonEnabled = false;
                    if (confirmation != null && groupedReview != null && !groupedReview.Current) confirmation.IsPrimaryButtonEnabled = false;
                    if (confirmation != null && activeCompletionId != null && completions.Get(activeCompletionId) == null) confirmation.IsPrimaryButtonEnabled = false;
                    UpdateActions();
                }
                if (refreshOtherReview != null) refreshOtherReview();
                if (CanReadStorage && inventory.Pending) await ReadInventory();
                await PulseModernReleases();
            };
            WindowLayout.InitialSize(this);
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open Easy Eject", null, delegate { Reveal(); });
            menu.Items.Add(storageMenu);
            menu.Items.Add("Eject cards…", null, delegate { Reveal(); ReviewCards(); });
            menu.Items.Add("Settings…", null, delegate { Reveal(); OpenSettings(); });
            menu.Items.Add("Import defaults…", null, delegate { Reveal(); OpenDefaults(); });
            menu.Items.Add("Saved profiles…", null, delegate { Reveal(); OpenProfiles(); });
            menu.Items.Add("Resume interrupted import…", null, delegate { Reveal(); ResumeSelectedImport(); });
            menu.Items.Add("Completed imports…", null, delegate { Reveal(); ReviewPendingImports(); });
            menu.Items.Add("Import history…", null, delegate { Reveal(); ReviewHistory(); });
            menu.Items.Add("Quick Share tools…", null, delegate { Reveal(); OpenQuickShareTools(); });
            releaseMenu.Click += delegate { ViewModernReleases(); }; menu.Items.Add(releaseMenu);
            issueMenu.Click += delegate { ViewModernIssue(); }; debugMenu.Click += delegate { ViewModernDebugLog(); };
            helpMenu.Click += delegate { ViewModernInformation(false); }; aboutMenu.Click += delegate { ViewModernInformation(true); };
            menu.Items.Add(issueMenu); menu.Items.Add(debugMenu); menu.Items.Add(helpMenu); menu.Items.Add(aboutMenu);
            menu.Items.Add("Stop current operation", null, delegate { RequestOperationStop(); });
            stopOtherMenu.Enabled = false; stopOtherMenu.Click += delegate { if (otherReview != null) otherReview.RequestStop(); }; menu.Items.Add(stopOtherMenu);
            menu.Items.Add("Quit preview", null, delegate { RequestQuit(); });
            menu.Opening += delegate { RebuildStorageMenu(); };
            tray = new Forms.NotifyIcon { Icon = appIcon, Text = "Easy Eject interface preview", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += delegate { HideTrayFlyout(); Reveal(); };
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs args) { if (args.Button == Forms.MouseButtons.Left) ToggleTrayFlyout(Forms.Control.MousePosition); };
            tray.BalloonTipClicked += delegate { Reveal(); };
            AppWindow.Closing += delegate(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args) {
                // Hiding the window keeps its dialogs, workers and tray alive.
                // Busy protection belongs to actual Quit, not close-to-tray.
                var closing = AppQuitPolicy.Closing(quitting, true, UiBusy || EditorOpen);
                if (closing == AppCloseAction.HideToTray) { args.Cancel = true; AppWindow.Hide(); return; }
                if (closing == AppCloseAction.StayOpen) { args.Cancel = true; quitting = false; Reveal(); }
            };
            Closed += delegate {
                closed = true; releaseChecks.Dispose(); pulse.Stop(); inventory.Dispose(); notifications.Dispose();
                reopen.Dispose(); visibleRoot.TrySetCanceled(); trayFlyout?.Close();
                DefaultsPanel.TryLeave(); ProfilesPanel.TryLeave(); SettingsPanel.TryLeave(); shortcut.Dispose(); tray.Dispose(); trayIcons.Dispose(); appIcon.Dispose(); menu.Dispose();
            };
            DefaultsPanel.Saved += delegate { automatic.SettingsSaved(); };
            DefaultsPanel.Completed += async delegate(string message) {
                if (returnToProfile) { returnToProfile = false; ShowProfiles(); await ProfilesPanel.ReloadDefaults(); }
                else ShowStorage();
                Summary.Text = message; UpdateActions();
            };
            ProfilesPanel.RequestDefaults += async delegate {
                if (UiBusy) return; returnToProfile = true; ProfilesPanel.Visibility = StorageView.Visibility = Visibility.Collapsed;
                Navigation.SelectedItem = DefaultsNavigation; DefaultsPanel.Visibility = Visibility.Visible; UpdateActions(); await DefaultsPanel.StartEdit(); UpdateActions();
            };
            ProfilesPanel.Initialize(discovery, async kind => {
                var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id) {
                    CommitButtonText = kind == "owner" ? "Choose my Mac Trash owner" : kind == "source" ? "Choose media folder" : "Choose destination"
                };
                var selected = await picker.PickSingleFolderAsync(); return selected == null ? null : selected.Path;
            });
            ProfilesPanel.Changed += delegate(string message) { automatic.SettingsSaved(); Summary.Text = message; };
            ProfilesPanel.RequestImport += ImportSavedProfile;
            ProfilesPanel.ActivityChanged += UpdateActions;
            ProfilesPanel.Completed += delegate(string message) { ShowStorage(); Summary.Text = message; UpdateActions(); };
            Root.Loaded += async delegate {
                visibleRoot.TrySetResult(true); await StartRuntime();
            };
        }
        internal void Reveal() { reopen.Request(); }
        void StorageView_SizeChanged(object sender, SizeChangedEventArgs args) {
            // Bound wrapped actions/results so a small window still has a device
            // list. Stop lives outside the scroller and stays directly reachable.
            StorageFooter.MaxHeight = Math.Max(120, args.NewSize.Height * 0.45);
        }
        internal Task StartRuntime() {
#if UI_TESTS
            return runtimeStart ?? (runtimeStart = UiTestHost.Review ? StartRuntimeCore() : Task.CompletedTask);
#else
            return runtimeStart ?? (runtimeStart = StartRuntimeCore());
#endif
        }
        async Task StartRuntimeCore() {
            if (closed || started) return; started = true;
            try { notifications.Start(); } catch (Exception ex) { Summary.Text = "Connection monitoring unavailable. Device actions are blocked. " + ex.Message; }
            releaseChecks.Start(); pulse.Start(); await ReadInventory();
        }
        async Task EnsureVisible() {
            if (closed) throw new OperationCanceledException("Easy Eject closed.");
            AppWindow.Show(); Activate(); if (Root.XamlRoot == null) await visibleRoot.Task;
            if (closed) throw new OperationCanceledException("Easy Eject closed.");
        }
        async void OpenDefaults() {
            if (UiBusy || inventory.Scanning || DefaultsPanel.Visibility == Visibility.Visible || (!ProfilesPanel.TryLeave() || !SettingsPanel.TryLeave())) return;
            returnToProfile = false; ProfilesPanel.Visibility = SettingsPanel.Visibility = Visibility.Collapsed;
            Navigation.SelectedItem = DefaultsNavigation; StorageView.Visibility = Visibility.Collapsed; DefaultsPanel.Visibility = Visibility.Visible;
            UpdateActions(); await DefaultsPanel.StartEdit();
        }
        async void OpenProfiles() {
            if (UiBusy || inventory.Scanning || ProfilesPanel.Visibility == Visibility.Visible || (!DefaultsPanel.TryLeave() || !SettingsPanel.TryLeave())) return;
            returnToProfile = false; ShowProfiles(); UpdateActions(); await ProfilesPanel.StartCatalog(); UpdateActions();
        }
        void ShowProfiles() { Navigation.SelectedItem = ProfilesNavigation; DefaultsPanel.Visibility = StorageView.Visibility = SettingsPanel.Visibility = Visibility.Collapsed; ProfilesPanel.Visibility = Visibility.Visible; }
        void ShowStorage() { Navigation.SelectedItem = StorageNavigation; DefaultsPanel.Visibility = ProfilesPanel.Visibility = SettingsPanel.Visibility = Visibility.Collapsed; StorageView.Visibility = Visibility.Visible; }
        void Navigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args) {
            var item = args.InvokedItemContainer as NavigationViewItem;
            if (item == null || DefaultsPanel == null || inventory == null) return;
            if (UiBusy) { Navigation.SelectedItem = SettingsPanel.Visibility == Visibility.Visible ? SettingsNavigation : DefaultsPanel.Visibility == Visibility.Visible ? DefaultsNavigation : ProfilesPanel.Visibility == Visibility.Visible ? ProfilesNavigation : StorageNavigation; return; }
            if ((string)item.Tag == "settings") { OpenSettings(); return; }
            if ((string)item.Tag == "defaults") { OpenDefaults(); return; }
            if ((string)item.Tag == "profiles") { OpenProfiles(); return; }
            if (!DefaultsPanel.TryLeave() || (!ProfilesPanel.TryLeave() || !SettingsPanel.TryLeave())) return;
            returnToProfile = false;
            ShowStorage(); UpdateActions();
        }
        void Storage_SelectionChanged(object sender, SelectionChangedEventArgs args) { if (inventory != null) UpdateActions(); }
        void UpdateActions() {
            UpdateReleaseActivity();
            var card = StorageCards.SelectedItem as StorageCard; var state = inventory.Current;
            bool fresh = state != null && state.Epoch == discovery.Epoch && notifications.Available;
            bool ready = fresh && !StorageBusy && !inventory.Scanning && settings != null && card != null;
            bool otherReady = fresh && CanUseOtherStorage && !inventory.Scanning && card != null && concurrentWork.State.Unavailable(state, discovery.Epoch, card.Choice.VolumeId) == null;
            Eject.IsEnabled = (ready || otherReady) && card.Choice.CanEject; CleanEject.IsEnabled = (ready || otherReady) && card.Choice.CanClean;
            EditProfile.IsEnabled = ready && card.Connection != null;
            Import.IsEnabled = ready && card.Connection != null;
            Resume.IsEnabled = ready && card.Connection != null;
            EjectCards.IsEnabled = fresh && !StorageBusy && !inventory.Scanning && settings != null && state.Complete && state.Volumes.Any(v => v.IsCard);
            Refresh.IsEnabled = CanReadStorage && !inventory.Scanning;
            CancelOperation.Visibility = working && operationCancel != null && confirmation == null ? Visibility.Visible : Visibility.Collapsed;
            CancelOperation.Content = quickShareSetupSaving ? "Stop setup" : activity.Current != null && activity.Current.Kind == TrayOperationKind.Share ? "Stop export" : activity.Current != null && activity.Current.Kind == TrayOperationKind.Verify ? "Stop verification" : groupedReview != null ? "Stop removal" : activity.Current != null && activity.Current.Kind == TrayOperationKind.Resume ? "Stop resume" : importOperationActive ? "Stop import" : "Stop cleanup";
            CancelOperation.IsEnabled = operationCancel != null && !operationCancel.IsCancellationRequested;
            bool canReview = !StorageBusy && !inventory.Scanning;
            IssueButton.IsEnabled = issueMenu.Enabled = canReview && (issueLoaded || appIssue.HasIssue);
            DebugButton.Visibility = settings != null && settings.DebugEnabled ? Visibility.Visible : Visibility.Collapsed;
            debugMenu.Visible = DebugButton.Visibility == Visibility.Visible; DebugButton.IsEnabled = debugMenu.Enabled = canReview;
            HelpButton.IsEnabled = AboutButton.IsEnabled = helpMenu.Enabled = aboutMenu.Enabled = canReview;
            ReleaseButton.IsEnabled = releaseMenu.Enabled = canReview;
            SelectionStatus.Text = CanUseOtherStorage && card != null ? concurrentWork.State.Unavailable(state, discovery.Epoch, card.Choice.VolumeId) ?? "This device is separate from the protected transfer disks. Review its own safe removal before unplugging."
                : StorageBusy ? "Finish the current review or operation first." : !fresh ? "Refresh the storage list before reviewing a device." : card == null ? "Select a device to review safe removal." : card.Choice.Error ?? "Safe removal affects this physical device and all its volumes. Review before unplugging.";
            stopOtherMenu.Enabled = otherReview != null && otherReview.Busy;
            if (tray != null) UpdateImportTray();
            RefreshTrayFlyout();
        }
        void RebuildStorageMenu() {
            foreach (var item in storageMenu.DropDownItems.Cast<Forms.ToolStripItem>().ToArray()) item.Dispose();
            storageMenu.DropDownItems.Clear();
            storageMenu.DropDownItems.AddRange(TrayStorageMenu.Build(inventory.Current, discovery.Epoch,
                StorageBusy && !CanUseOtherStorage || inventory.Scanning, !notifications.Available || settings == null, settings != null && settings.CleanupCardsEnabled,
                async (choice, clean) => { Reveal(); ShowStorage(); await EjectDevice(clean, choice); },
                working ? choice => concurrentWork.State.Unavailable(inventory.Current, discovery.Epoch, choice.VolumeId) : null));
        }
        async void Refresh_Click(object sender, RoutedEventArgs args) { await ReadInventory(); }
        async Task ReadInventory() {
            if (closed || !CanReadStorage || inventory.Scanning) return;
            var refresh = inventory.Refresh(); UpdateActions();
            if (settings == null || !issueLoaded) {
                try {
                    var loaded = await Task.Run(() => {
                        var value = AppSettings.Load(); IssuePersistenceState issue;
                        try { issue = IssuePersistenceState.Load(Profiles.Root); } catch (Exception ex) { issue = IssuePersistenceState.Unavailable(ex.Message); }
                        return Tuple.Create(value, issue);
                    });
                    if (closed) return;
                    settings = loaded.Item1; issueState = loaded.Item2; issueLoaded = true; ConfigureShortcut();
                }
                catch (Exception ex) { if (!closed) { Summary.Text = "Saved app settings could not be read. Device actions are blocked. " + ex.Message; RecordModernIssue(Summary.Text); } }
            }
            bool published = await refresh;
            if (closed) return;
            var state = inventory.Current;
            if (published && state != null && state.Epoch == discovery.Epoch) {
                string selectedVolume = working && StorageCards.SelectedItem is StorageCard selectedCard ? selectedCard.Choice.VolumeId : null;
                StorageCards.SelectedItem = null;
                StorageCards.ItemsSource = state.Volumes.OrderBy(v => v.Group).ThenBy(v => v.Display).Select(v => new StorageCard {
                    Title = string.IsNullOrEmpty(v.Label) ? "Unnamed volume" : v.Label,
                    Detail = v.Classification + " · " + (v.Mounts.Count == 0 ? "No drive letter" : string.Join(", ", v.Mounts)),
                    State = v.System ? "Protected" : v.Error == "" ? "Connected" : "Needs review",
                    Choice = new TrayStorageChoice(state, v, discovery.Epoch), Connection = AutomaticConnection.Capture(state, v)
                }).ToArray();
                if (selectedVolume != null) StorageCards.SelectedItem = StorageCards.Items.Cast<StorageCard>().SingleOrDefault(card => card.Choice.VolumeId == selectedVolume);
                if (!working) {
                    Summary.Text = settings == null ? "Saved app settings unavailable. Device actions are blocked." : state.Complete ? state.Volumes.Count + " volumes across " + state.Disks.Count + " physical disks" : "Storage cannot be safely acted on. " + string.Join(" ", state.Errors);
                    if (shortcutWarning != null) Summary.Text += "\n" + shortcutWarning;
                }
                tray.Text = "Easy Eject · " + state.Volumes.Count + " connected volumes";
                automatic.Observe(state);
                completions.Reconcile(state, WindowsDiscovery.VolumeId);
            } else if (inventory.Error != null) Summary.Text = "Storage could not be checked. " + inventory.Error;
            string problem = !notifications.Available ? "Connection monitoring unavailable. Device actions are blocked." : null;
            if (inventory.Error != null) problem = (problem == null ? "" : problem + "\n") + "Storage could not be checked. " + inventory.Error;
            else if (state != null && !state.Complete) problem = (problem == null ? "" : problem + "\n") + "Storage cannot be safely acted on. " + string.Join(" ", state.Errors);
            if (problem != null) RecordDiscoveryIssue(problem);
            else { discoveryIssueEpoch = null; discoveryIssueText = null; }
            if (!working && appIssue.NeedsAttention) Summary.Text += "\nChoose View issue for the retained explanation.";
            UpdateActions();
            await PumpAutomaticImports();
        }
        async void Eject_Click(object sender, RoutedEventArgs args) { await EjectDevice(false); }
        async void Profile_Click(object sender, RoutedEventArgs args) {
            var card = StorageCards.SelectedItem as StorageCard;
            if (StorageBusy || inventory.Scanning || !notifications.Available || card == null || card.Connection == null) return;
            ShowProfiles(); UpdateActions(); await ProfilesPanel.StartConnected(card.Connection); UpdateActions();
        }
        async void CleanEject_Click(object sender, RoutedEventArgs args) { await EjectDevice(true); }
        void CancelOperation_Click(object sender, RoutedEventArgs args) { RequestOperationStop(); }
        async Task EjectDevice(bool forceClean, TrayStorageChoice chosen = null, TrayFlyoutWindow surface = null) {
            if (working) { var selected = StorageCards.SelectedItem as StorageCard; await ReviewOtherStorage(forceClean, chosen ?? selected?.Choice, surface); return; }
            if (closed || StorageBusy || inventory.Scanning || !notifications.Available || settings == null) return;
            var card = StorageCards.SelectedItem as StorageCard;
            var choice = chosen ?? (card == null ? null : card.Choice); if (choice == null) return;
            working = true; UpdateActions();
            EjectResult terminal = null;
            try {
                if (surface == null) await EnsureVisible();
                var reviewedSettings = await Task.Run(() => AppSettings.Load());
                activeReview = new ManualEjectReview(inventory.Current, choice, reviewedSettings, forceClean, discovery, operations,
                    validate => new WindowsEjectApi(validate), WindowsDiscovery.VolumeId);
                var owner = activeReview;
                confirmation = MessageDialog(ManualEjectPrompt.Title, owner.Description, ManualEjectPrompt.Confirm, surface?.DialogRoot);
                confirmation.CloseButtonText = ManualEjectPrompt.Keep;
                UpdateActions(); var answer = await confirmation.ShowAsync(); confirmation = null;
                if (answer != ContentDialogResult.Primary) { owner.Cancel(); return; }
                owner.Confirm(); operationCancel = new CancellationTokenSource(); Summary.Text = owner.HasCleanup ? "Cleaning before safe removal…" : "Requesting safe removal…"; UpdateActions();
                var result = await Task.Run(() => owner.Execute(operationCancel.Token, message => DispatcherQueue.TryEnqueue(delegate { if (!closed && activeReview == owner) Summary.Text = message; })));
                terminal = result;
                Summary.Text = result.Success ? "Windows accepted safe removal." : "Device removal stopped. Review the explanation.";
                if (result.Success) ClearModernIssue(); else RecordModernIssue(result.Message);
                string logWarning = LogModernOperation("Manual safe removal | " + result.Message); if (logWarning != null) Summary.Text += " " + logWarning;
                string deliveryWarning = TryModernNotification(result.Success ? "Safe removal accepted" : "Device still connected", result.Message, !result.Success, reviewedSettings.NotificationsEnabled);
                if (deliveryWarning != null) Summary.Text += " " + deliveryWarning;
                if (surface == null) {
                    confirmation = MessageDialog(result.Success ? ManualEjectPrompt.Accepted : ManualEjectPrompt.Refused, result.Message, null);
                    await confirmation.ShowAsync(); confirmation = null;
                } else surface.Report(result.Message + (deliveryWarning == null ? "" : "\n" + deliveryWarning));
            } catch (OperationCanceledException ex) { Summary.Text = terminal == null ? ex.Message : terminal.Message; }
            catch (Exception ex) {
                if (terminal != null) { Summary.Text = terminal.Message + " Completion message unavailable: " + ex.Message; RecordModernIssue(Summary.Text); }
                else {
                    Summary.Text = "Removal stopped. Refresh and review the device again.";
                    RecordModernIssue(Summary.Text + " " + ex.Message);
                    try {
                        if (surface == null) { confirmation = MessageDialog(ManualEjectPrompt.Refused, ex.Message, null); await confirmation.ShowAsync(); }
                        else { surface.Report("Device still connected. " + ex.Message); TryModernNotification(ManualEjectPrompt.Refused, ex.Message, true, settings.NotificationsEnabled); }
                    }
                    catch (Exception displayError) { Summary.Text = "Removal stopped. Message unavailable: " + displayError.Message; }
                    finally { confirmation = null; }
                }
            } finally {
                if (activeReview != null) activeReview.Cancel(); activeReview = null;
                if (operationCancel != null) { operationCancel.Dispose(); operationCancel = null; }
                confirmation = null; working = false; UpdateActions();
            }
            await ReadInventory();
        }
        ContentDialog MessageDialog(string title, string message, string action, XamlRoot root = null) {
            return new ContentDialog { XamlRoot = root ?? Root.XamlRoot, Title = title, CloseButtonText = action == null ? "Close" : "Cancel",
                PrimaryButtonText = action ?? "", DefaultButton = ContentDialogButton.Close,
                Content = new ScrollViewer { MaxHeight = 360, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } } };
        }
    }
}
