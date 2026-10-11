using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Foundation;

namespace EasyEject.Modern {
    public sealed partial class TrayFlyoutWindow : Window {
        readonly Action open, settings, quit;
        readonly Action refresh;
        readonly Func<TrayFlyoutDevice, string, Task> action;
        System.Drawing.Point anchor; string signature; bool visible, requestRunning;
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(System.Drawing.Point point, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint x, out uint y);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        internal bool IsFlyoutVisible { get { return visible; } }
        internal bool RequestRunning { get { return requestRunning; } }
        internal XamlRoot DialogRoot { get { return Surface.XamlRoot; } }
        internal TrayFlyoutWindow(Action open, Action settings, Action quit, Func<TrayFlyoutDevice, string, Task> action, Action refresh) {
            InitializeComponent(); this.open = open; this.settings = settings; this.quit = quit; this.action = action; this.refresh = refresh;
            SystemBackdrop = new DesktopAcrylicBackdrop();
            AppWindow.IsShownInSwitchers = false;
            var presenter = (OverlappedPresenter)AppWindow.Presenter;
            presenter.SetBorderAndTitleBar(false, false); presenter.IsResizable = presenter.IsMinimizable = presenter.IsMaximizable = false;
            presenter.IsAlwaysOnTop = true;
            int rounded = 2; DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(this), 33, ref rounded, sizeof(int));
            Activated += (_, args) => { if (args.WindowActivationState == WindowActivationState.Deactivated) Dismiss(TrayFlyoutDismissReason.Deactivated); };
            Surface.ActualThemeChanged += (_, args) => { if (visible) Position(); };
            Surface.Loaded += (_, args) => { if (visible) { Position(); Open.Focus(FocusState.Programmatic); } };
            AppWindow.Changed += (_, args) => { if (visible && args.DidPresenterChange) Position(); };
        }
        internal void ShowAt(System.Drawing.Point point, TrayFlyoutView view) {
            anchor = point; Result.Text = ""; Render(view); Position(); visible = true; AppWindow.Show(); Activate();
            Open.Focus(FocusState.Programmatic);
        }
        internal void Dismiss(TrayFlyoutDismissReason reason) {
            if (!TrayFlyoutDismiss.ShouldClose(reason, false, requestRunning)) return;
            visible = false; AppWindow.Hide();
        }
        internal void Report(string text) { Result.Text = text; }
        internal void Render(TrayFlyoutView view) {
            if (requestRunning) return;
            string next = view.Status + "|" + view.EmptyText + "|" + view.CanOpenSettings + "|" + view.QuitAction + "|" + String.Join("|", System.Linq.Enumerable.Select(view.Groups, g => g.Heading)) + "|" + String.Join("|", System.Linq.Enumerable.Select(view.Devices, d => d.VolumeId + d.Title + d.Capacity + d.StateText + d.Reason + d.CanEject + d.CanImport + d.ShowQuickShare + d.CanQuickShare));
            if (signature == next) return; signature = next;
            Status.Text = view.Status ?? "Choose a device action. Removal always asks first.";
            Settings.IsEnabled = view.CanOpenSettings;
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(Quit, view.QuitRefusal ?? "Quit Easy Eject");
            Devices.Children.Clear();
            if (view.EmptyText != null) Devices.Children.Add(Text(view.EmptyText, 14));
            foreach (var group in view.Groups) {
                Devices.Children.Add(new TextBlock { Text = group.Heading, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                foreach (var device in group.Devices) {
                    var body = new StackPanel { Spacing = 6 };
                    body.Children.Add(new TextBlock { Text = device.Title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                    body.Children.Add(Text(device.Capacity + " · " + device.StateText, 12));
                    if (device.Reason != null) body.Children.Add(Text(device.Reason, 12));
                    var buttons = new ActionWrapPanel();
                    buttons.Children.Add(DeviceButton("Eject…", device.CanEject, device, "eject"));
                    buttons.Children.Add(DeviceButton("Import…", device.CanImport, device, "import"));
                    if (device.ShowQuickShare) buttons.Children.Add(DeviceButton("Quick Share…", device.CanQuickShare, device, "share"));
                    body.Children.Add(buttons); Devices.Children.Add(body);
                }
            }
            if (visible) Position();
        }
        static TextBlock Text(string text, double size) { return new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap }; }
        Button DeviceButton(string caption, bool enabled, TrayFlyoutDevice device, string kind) {
            var button = new Button { Content = caption, IsEnabled = enabled };
            button.Click += async (_, args) => {
                if (requestRunning) return;
                if (kind != "eject") { Dismiss(TrayFlyoutDismissReason.ActionStarted); await action(device, kind); return; }
                requestRunning = true; Devices.IsHitTestVisible = false; Open.IsEnabled = Settings.IsEnabled = Quit.IsEnabled = false;
                try { await action(device, kind); }
                catch (Exception ex) { Report("Device still connected. " + ex.Message); }
                finally { requestRunning = false; Devices.IsHitTestVisible = true; Open.IsEnabled = Quit.IsEnabled = true; signature = null; refresh(); Position(); }
            }; return button;
        }
        void Position() {
            var screen = System.Windows.Forms.Screen.FromPoint(anchor);
            uint x, y; double scale = GetDpiForMonitor(MonitorFromPoint(anchor, 2), 0, out x, out y) == 0 && x > 0 ? x / 96.0 : 1;
            double width = Math.Min(400, Math.Max(1, screen.WorkingArea.Width / scale - 24));
            double maximum = Math.Max(1, screen.WorkingArea.Height / scale - 24);
            // Measure content without giving the device list an infinite visible viewport.
            Surface.Width = width; Surface.Height = Double.NaN; DeviceScroll.MaxHeight = Math.Max(40, maximum - 180);
            Surface.Measure(new Windows.Foundation.Size(width, maximum));
            var bounds = TrayFlyoutPlacement.Place(screen.Bounds, screen.WorkingArea, anchor, new SizeF((float)width, (float)Math.Min(maximum, Math.Max(180, Surface.DesiredSize.Height))), scale);
            Surface.Width = bounds.Width / scale; Surface.Height = bounds.Height / scale;
            AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }
        void Surface_KeyDown(object sender, KeyRoutedEventArgs args) { if (args.Key == Windows.System.VirtualKey.Escape) { Dismiss(TrayFlyoutDismissReason.Escape); args.Handled = !requestRunning; } }
        void Open_Click(object sender, RoutedEventArgs args) { Dismiss(TrayFlyoutDismissReason.OpenWindow); open(); }
        void Settings_Click(object sender, RoutedEventArgs args) { Dismiss(TrayFlyoutDismissReason.ActionStarted); settings(); }
        void Quit_Click(object sender, RoutedEventArgs args) { Dismiss(TrayFlyoutDismissReason.ActionStarted); quit(); }
    }
}
