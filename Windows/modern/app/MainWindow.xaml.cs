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
        readonly Forms.ToolStripMenuItem storageMenu = new Forms.ToolStripMenuItem("Connected storage");
        AppSettings settings;
        ManualEjectReview activeReview;
        ContentDialog confirmation;
        CancellationTokenSource operationCancel;
        bool quitting, working, closed, started, returnToProfile;
        bool UiBusy { get { return working || automaticReading || DefaultsPanel.Busy || ProfilesPanel.Busy; } }
        bool StorageBusy { get { return UiBusy || DefaultsPanel.Visibility == Visibility.Visible || ProfilesPanel.Visibility == Visibility.Visible; } }
        public MainWindow() {
            InitializeComponent(); SystemBackdrop = new MicaBackdrop();
            inventory = new StorageInventorySession(discovery);
            notifications = new StorageNotificationWindow(discovery, () => StorageBusy);
            pulse = DispatcherQueue.CreateTimer(); pulse.Interval = TimeSpan.FromMilliseconds(500);
            pulse.Tick += async delegate {
                if (closed) return;
                if (inventory.Observe()) {
                    completions.InvalidateNotifications();
                    if (confirmation != null && activeReview != null && !activeReview.Current) confirmation.IsPrimaryButtonEnabled = false;
                    if (confirmation != null && importing != null && !importing.Current) confirmation.IsPrimaryButtonEnabled = false;
                    if (confirmation != null && activeCompletionId != null && completions.Get(activeCompletionId) == null) confirmation.IsPrimaryButtonEnabled = false;
                    UpdateActions();
                }
                if (!UiBusy && inventory.Pending) await ReadInventory();
            };
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1120, 760));
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open Easy Eject", null, delegate { Reveal(); });
            menu.Items.Add(storageMenu);
            menu.Items.Add("Import defaults…", null, delegate { Reveal(); OpenDefaults(); });
            menu.Items.Add("Saved profiles…", null, delegate { Reveal(); OpenProfiles(); });
            menu.Items.Add("Completed imports…", null, delegate { Reveal(); ReviewPendingImports(); });
            menu.Items.Add("Stop current operation", null, delegate { RequestOperationStop(); });
            menu.Items.Add("Quit preview", null, delegate { if (StorageBusy) { Reveal(); return; } quitting = true; Close(); });
            menu.Opening += delegate { RebuildStorageMenu(); };
            tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "Easy Eject interface preview", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += delegate { Reveal(); };
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs args) { if (args.Button == Forms.MouseButtons.Left) Reveal(); };
            tray.BalloonTipClicked += delegate { Reveal(); };
            AppWindow.Closing += delegate(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args) {
                if (UiBusy) { args.Cancel = true; return; }
                if (!quitting) { args.Cancel = true; AppWindow.Hide(); }
            };
            Closed += delegate {
                closed = true; pulse.Stop(); inventory.Dispose(); notifications.Dispose();
                DefaultsPanel.TryLeave(); ProfilesPanel.TryLeave(); tray.Dispose(); menu.Dispose();
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
            ProfilesPanel.Completed += delegate(string message) { ShowStorage(); Summary.Text = message; UpdateActions(); };
            Root.Loaded += async delegate {
                if (started) return; started = true;
                try { notifications.Start(); } catch (Exception ex) { Summary.Text = "Connection monitoring unavailable. Device actions are blocked. " + ex.Message; }
                pulse.Start(); await ReadInventory();
            };
        }
        internal void Reveal() { DispatcherQueue.TryEnqueue(delegate { if (!closed) { AppWindow.Show(); Activate(); } }); }
        async void OpenDefaults() {
            if (UiBusy || inventory.Scanning || DefaultsPanel.Visibility == Visibility.Visible || !ProfilesPanel.TryLeave()) return;
            returnToProfile = false; ProfilesPanel.Visibility = Visibility.Collapsed;
            Navigation.SelectedItem = DefaultsNavigation; StorageView.Visibility = Visibility.Collapsed; DefaultsPanel.Visibility = Visibility.Visible;
            UpdateActions(); await DefaultsPanel.StartEdit();
        }
        async void OpenProfiles() {
            if (UiBusy || inventory.Scanning || ProfilesPanel.Visibility == Visibility.Visible || !DefaultsPanel.TryLeave()) return;
            returnToProfile = false; ShowProfiles(); UpdateActions(); await ProfilesPanel.StartCatalog(); UpdateActions();
        }
        void ShowProfiles() { Navigation.SelectedItem = ProfilesNavigation; DefaultsPanel.Visibility = StorageView.Visibility = Visibility.Collapsed; ProfilesPanel.Visibility = Visibility.Visible; }
        void ShowStorage() { Navigation.SelectedItem = StorageNavigation; DefaultsPanel.Visibility = ProfilesPanel.Visibility = Visibility.Collapsed; StorageView.Visibility = Visibility.Visible; }
        void Navigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args) {
            var item = args.InvokedItemContainer as NavigationViewItem;
            if (item == null || DefaultsPanel == null || inventory == null) return;
            if (UiBusy) { Navigation.SelectedItem = DefaultsPanel.Visibility == Visibility.Visible ? DefaultsNavigation : ProfilesPanel.Visibility == Visibility.Visible ? ProfilesNavigation : StorageNavigation; return; }
            if ((string)item.Tag == "defaults") { OpenDefaults(); return; }
            if ((string)item.Tag == "profiles") { OpenProfiles(); return; }
            if (!DefaultsPanel.TryLeave() || !ProfilesPanel.TryLeave()) return;
            returnToProfile = false;
            ShowStorage(); UpdateActions();
        }
        void Storage_SelectionChanged(object sender, SelectionChangedEventArgs args) { if (inventory != null) UpdateActions(); }
        void UpdateActions() {
            var card = StorageCards.SelectedItem as StorageCard; var state = inventory.Current;
            bool fresh = state != null && state.Epoch == discovery.Epoch && notifications.Available;
            bool ready = fresh && !StorageBusy && !inventory.Scanning && settings != null && card != null;
            Eject.IsEnabled = ready && card.Choice.CanEject; CleanEject.IsEnabled = ready && card.Choice.CanClean;
            EditProfile.IsEnabled = ready && card.Connection != null;
            Import.IsEnabled = ready && card.Connection != null;
            Refresh.IsEnabled = !UiBusy && !inventory.Scanning;
            CancelOperation.Visibility = working && operationCancel != null && confirmation == null ? Visibility.Visible : Visibility.Collapsed;
            CancelOperation.Content = importOperationActive ? "Stop import" : "Stop cleanup";
            CancelOperation.IsEnabled = operationCancel != null && !operationCancel.IsCancellationRequested;
            SelectionStatus.Text = StorageBusy ? "Finish the current review or operation first." : !fresh ? "Refresh the storage list before reviewing a device." : card == null ? "Select a device to review safe removal." : card.Choice.Error ?? "Safe removal affects this physical device and all its volumes. Review before unplugging.";
        }
        void RebuildStorageMenu() {
            foreach (var item in storageMenu.DropDownItems.Cast<Forms.ToolStripItem>().ToArray()) item.Dispose();
            storageMenu.DropDownItems.Clear();
            storageMenu.DropDownItems.AddRange(TrayStorageMenu.Build(inventory.Current, discovery.Epoch,
                StorageBusy || inventory.Scanning, !notifications.Available || settings == null, settings != null && settings.CleanupCardsEnabled,
                async (choice, clean) => { Reveal(); ShowStorage(); await EjectDevice(clean, choice); }));
        }
        async void Refresh_Click(object sender, RoutedEventArgs args) { await ReadInventory(); }
        async Task ReadInventory() {
            if (closed || UiBusy || inventory.Scanning) return;
            var refresh = inventory.Refresh(); UpdateActions();
            if (settings == null) {
                try { settings = await Task.Run(() => AppSettings.Load()); }
                catch (Exception ex) { if (!closed) Summary.Text = "Saved app settings could not be read. Device actions are blocked. " + ex.Message; }
            }
            bool published = await refresh;
            if (closed) return;
            var state = inventory.Current;
            if (published && state != null && state.Epoch == discovery.Epoch) {
                StorageCards.SelectedItem = null;
                StorageCards.ItemsSource = state.Volumes.OrderBy(v => v.Group).ThenBy(v => v.Display).Select(v => new StorageCard {
                    Title = string.IsNullOrEmpty(v.Label) ? "Unnamed volume" : v.Label,
                    Detail = v.Classification + " · " + (v.Mounts.Count == 0 ? "No drive letter" : string.Join(", ", v.Mounts)),
                    State = v.System ? "Protected" : v.Error == "" ? "Connected" : "Needs review",
                    Choice = new TrayStorageChoice(state, v, discovery.Epoch), Connection = AutomaticConnection.Capture(state, v)
                }).ToArray();
                Summary.Text = settings == null ? "Saved app settings unavailable. Device actions are blocked." : state.Complete ? state.Volumes.Count + " volumes across " + state.Disks.Count + " physical disks" : "Storage cannot be safely acted on. " + string.Join(" ", state.Errors);
                tray.Text = "Easy Eject · " + state.Volumes.Count + " connected volumes";
                automatic.Observe(state);
                completions.Reconcile(state, WindowsDiscovery.VolumeId);
            } else if (inventory.Error != null) Summary.Text = "Storage could not be checked. " + inventory.Error;
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
        async Task EjectDevice(bool forceClean, TrayStorageChoice chosen = null) {
            if (closed || StorageBusy || inventory.Scanning || !notifications.Available || settings == null) return;
            var card = StorageCards.SelectedItem as StorageCard;
            var choice = chosen ?? (card == null ? null : card.Choice); if (choice == null) return;
            working = true; UpdateActions();
            EjectResult terminal = null;
            try {
                var reviewedSettings = await Task.Run(() => AppSettings.Load());
                activeReview = new ManualEjectReview(inventory.Current, choice, reviewedSettings, forceClean, discovery, operations,
                    validate => new WindowsEjectApi(validate), WindowsDiscovery.VolumeId);
                var owner = activeReview;
                confirmation = MessageDialog("Safe eject", owner.Description, "Request safe removal");
                UpdateActions(); var answer = await confirmation.ShowAsync(); confirmation = null;
                if (answer != ContentDialogResult.Primary) { owner.Cancel(); return; }
                owner.Confirm(); operationCancel = new CancellationTokenSource(); Summary.Text = owner.HasCleanup ? "Cleaning before safe removal…" : "Requesting safe removal…"; UpdateActions();
                var result = await Task.Run(() => owner.Execute(operationCancel.Token, message => DispatcherQueue.TryEnqueue(delegate { if (!closed && activeReview == owner) Summary.Text = message; })));
                terminal = result;
                Summary.Text = result.Success ? "Windows accepted safe removal." : "Device removal stopped. Review the explanation.";
                if (reviewedSettings.NotificationsEnabled) tray.ShowBalloonTip(5000, result.Success ? "Safe removal accepted" : "Device still connected", result.Message.Length > 255 ? result.Message.Substring(0, 255) : result.Message, result.Success ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning);
                confirmation = MessageDialog(result.Success ? "Safe removal accepted" : "Device still connected", result.Message, null);
                await confirmation.ShowAsync(); confirmation = null;
            } catch (OperationCanceledException ex) { Summary.Text = terminal == null ? ex.Message : terminal.Message; }
            catch (Exception ex) {
                if (terminal != null) Summary.Text = terminal.Message + " Completion message unavailable: " + ex.Message;
                else {
                    Summary.Text = "Removal stopped. Refresh and review the device again.";
                    try { confirmation = MessageDialog("Device still connected", ex.Message, null); await confirmation.ShowAsync(); }
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
        ContentDialog MessageDialog(string title, string message, string action) {
            return new ContentDialog { XamlRoot = Root.XamlRoot, Title = title, CloseButtonText = action == null ? "Close" : "Cancel",
                PrimaryButtonText = action ?? "", DefaultButton = ContentDialogButton.Close,
                Content = new ScrollViewer { MaxHeight = 360, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap } } };
        }
    }
}
