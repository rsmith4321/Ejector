using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Forms = System.Windows.Forms;

namespace EasyEject.Modern {
    public sealed class StorageCard {
        public string Title { get; set; }
        public string Detail { get; set; }
        public string State { get; set; }
    }
    public sealed partial class MainWindow : Window {
        readonly WindowsDiscovery discovery = new WindowsDiscovery();
        readonly Forms.NotifyIcon tray;
        bool quitting, scanning;
        public MainWindow() {
            InitializeComponent(); SystemBackdrop = new MicaBackdrop();
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1120, 760));
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open Easy Eject", null, delegate { Reveal(); });
            menu.Items.Add("Import defaults…", null, delegate { Reveal(); OpenDefaults(); });
            menu.Items.Add("Quit preview", null, delegate { if (DefaultsPanel.Busy) { Reveal(); return; } quitting = true; Close(); });
            tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "Easy Eject interface preview", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += delegate { Reveal(); };
            tray.MouseClick += delegate(object sender, Forms.MouseEventArgs args) { if (args.Button == Forms.MouseButtons.Left) Reveal(); };
            AppWindow.Closing += delegate(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args) {
                if (DefaultsPanel.Busy) { args.Cancel = true; return; }
                if (!quitting) { args.Cancel = true; AppWindow.Hide(); }
            };
            Closed += delegate { DefaultsPanel.TryLeave(); tray.Dispose(); menu.Dispose(); };
            DefaultsPanel.Completed += delegate(string message) { ShowStorage(); Summary.Text = message; };
            Root.Loaded += async delegate { await ReadInventory(); };
        }
        internal void Reveal() { DispatcherQueue.TryEnqueue(delegate { AppWindow.Show(); Activate(); }); }
        async void OpenDefaults() {
            if (DefaultsPanel.Busy || DefaultsPanel.Visibility == Visibility.Visible) return;
            Navigation.SelectedItem = DefaultsNavigation; StorageView.Visibility = Visibility.Collapsed; DefaultsPanel.Visibility = Visibility.Visible;
            await DefaultsPanel.StartEdit();
        }
        void ShowStorage() { Navigation.SelectedItem = StorageNavigation; DefaultsPanel.Visibility = Visibility.Collapsed; StorageView.Visibility = Visibility.Visible; }
        void Navigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args) {
            var item = args.InvokedItemContainer as NavigationViewItem;
            if (item == null || DefaultsPanel == null) return;
            if ((string)item.Tag == "defaults") { OpenDefaults(); return; }
            if (!DefaultsPanel.TryLeave()) { Navigation.SelectedItem = DefaultsNavigation; return; }
            ShowStorage();
        }
        async void Refresh_Click(object sender, RoutedEventArgs args) { await ReadInventory(); }
        async Task ReadInventory() {
            if (scanning) return;
            scanning = true; Refresh.IsEnabled = false;
            try {
                var state = await Task.Run(() => discovery.Scan());
                StorageCards.ItemsSource = state.Volumes.OrderBy(v => v.System).ThenBy(v => v.Display).Select(v => new StorageCard {
                    Title = string.IsNullOrEmpty(v.Label) ? "Unnamed volume" : v.Label,
                    Detail = v.Classification + " · " + (v.Mounts.Count == 0 ? "No drive letter" : string.Join(", ", v.Mounts)),
                    State = v.System ? "Protected" : "Connected"
                }).ToArray();
                Summary.Text = state.Complete ? state.Volumes.Count + " volumes across " + state.Disks.Count + " physical disks" : "Some storage details are unavailable. Refresh to check again.";
            } catch (Exception ex) { Summary.Text = "Storage could not be checked. " + ex.Message; }
            finally { scanning = false; Refresh.IsEnabled = true; }
        }
    }
}
