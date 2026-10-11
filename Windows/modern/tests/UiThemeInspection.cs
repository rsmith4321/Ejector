#if UI_TESTS
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace EasyEject.Modern {
    // Explicit test-only per-element overrides. No Windows theme/settings write,
    // saved app preference, runtime pump, source path or device callback.
    public sealed partial class MainWindow {
        internal static string InspectionBrush(Brush brush) {
            if (brush is not SolidColorBrush solid) throw new InvalidOperationException("Expected a resolved solid theme brush.");
            var value = solid.Color;
            return String.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", value.A, value.R, value.G, value.B);
        }
        async Task TestNativeThemeRendering() {
            var prior = Root.RequestedTheme;
            string lightBackground = null, lightText = null;
            try {
                foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark }) {
                    Root.RequestedTheme = theme;
                    await Task.Delay(200); Root.UpdateLayout();
                    Require(Root.ActualTheme == theme, "Native root did not resolve requested inspection theme.");
                    string background = InspectionBrush(Root.Background), text = InspectionBrush(Summary.Foreground);
                    if (theme == ElementTheme.Light) { lightBackground = background; lightText = text; }
                    else Require(background != lightBackground && text != lightText, "Native light/dark theme brushes did not change.");
                    File.AppendAllText(Path.Combine(UiTestHost.Root, "theme-regression.jsonl"),
                        System.Text.Json.JsonSerializer.Serialize(new { requested = theme.ToString(), actual = Root.ActualTheme.ToString(), background, text }) + "\r\n");
                }
            } finally { Root.RequestedTheme = prior; }
        }
        void InitializeThemeInspection() {
            var footer = (StackPanel)Navigation.PaneFooter;
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark, ElementTheme.Default }) {
                var button = new Button { Content = "Inspect " + (theme == ElementTheme.Default ? "Windows" : theme.ToString()) + " theme" };
                button.Click += async (_, args) => {
                    if (StorageBusy || closed) return;
                    Root.RequestedTheme = theme; trayFlyout.SetInspectionTheme(theme);
                    await Task.Delay(200);
                    if (closed) return;
                    var area = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                    trayFlyout.ShowAt(new System.Drawing.Point(area.Right - 140, area.Bottom - 20), FixtureFlyout());
                    await Task.Delay(200);
                    if (closed) return;
                    trayFlyout.WriteInspectionLayout();
                    File.AppendAllText(Path.Combine(UiTestHost.Root, "theme-inspection.jsonl"),
                        System.Text.Json.JsonSerializer.Serialize(new { utc = DateTime.UtcNow, requested = theme.ToString(),
                            main = Root.ActualTheme.ToString(), background = InspectionBrush(Root.Background),
                            text = InspectionBrush(Summary.Foreground), flyout = trayFlyout.InspectionThemeSnapshot(),
                            working, confirmationOpen = confirmation != null }) + "\r\n");
                };
                footer.Children.Add(button);
            }
        }
    }
    public sealed partial class TrayFlyoutWindow {
        internal void SetInspectionTheme(ElementTheme theme) { Surface.RequestedTheme = theme; }
        internal object InspectionThemeSnapshot() {
            return new { actual = Surface.ActualTheme.ToString(), background = MainWindow.InspectionBrush(Surface.Background),
                text = MainWindow.InspectionBrush(Status.Foreground), scale = Surface.XamlRoot.RasterizationScale,
                width = Surface.ActualWidth, height = Surface.ActualHeight, viewport = DeviceScroll.ActualHeight,
                footerTop = Open.TransformToVisual(Surface).TransformPoint(new Windows.Foundation.Point()).Y };
        }
    }
}
#endif
