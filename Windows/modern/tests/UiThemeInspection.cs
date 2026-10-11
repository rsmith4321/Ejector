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
        async Task TestNativeFlyoutTextLayout() {
            var flyout = new TrayFlyoutWindow(() => {}, () => {}, () => {}, (_, kind) => Task.CompletedTask, () => {});
            try {
                var area = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark }) {
                    flyout.SetInspectionTheme(theme);
                    flyout.ShowAt(new System.Drawing.Point(area.Right - 140, area.Bottom - 20), FixtureFlyout());
                    await Task.Delay(200);
                    flyout.WriteInspectionLayout();
                    await flyout.TestWrappedInspectionStatus();
                }
            } finally { flyout.Close(); }
        }
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
        internal async Task TestWrappedInspectionStatus() {
            string prior = Status.Text;
            try {
                Status.Text = "Generated wrapping check: keep every device connected while reviewing this longer status message. No device action, import, deletion or settings change is connected to this fixture.";
                Surface.UpdateLayout(); await Task.Delay(100);
                WriteInspectionLayout();
                if (Status.ActualHeight < Status.FontSize * 2)
                    throw new InvalidOperationException("Generated long status did not exercise native multiline wrapping.");
            } finally { Status.Text = prior; Surface.UpdateLayout(); }
        }
        internal void WriteInspectionTextLayout() {
            var header = (StackPanel)Status.Parent;
            var grid = (Grid)header.Parent;
            var origin = Status.TransformToVisual(Surface).TransformPoint(new Windows.Foundation.Point());
            var viewport = DeviceScroll.TransformToVisual(Surface).TransformPoint(new Windows.Foundation.Point());
            var characters = new System.Collections.Generic.List<object>();
            double top = Double.PositiveInfinity, bottom = 0, right = 0;
            for (int offset = 0; offset < Status.ContentEnd.Offset; offset++) {
                var pointer = Status.ContentStart.GetPositionAtOffset(offset, Microsoft.UI.Xaml.Documents.LogicalDirection.Forward);
                if (pointer == null) continue;
                var rectangle = pointer.GetCharacterRect(Microsoft.UI.Xaml.Documents.LogicalDirection.Forward);
                if (rectangle.Height > 0) {
                    top = Math.Min(top, rectangle.Top); bottom = Math.Max(bottom, rectangle.Bottom);
                    right = Math.Max(right, rectangle.Right);
                }
                characters.Add(new { offset, x = rectangle.X, y = rectangle.Y, width = rectangle.Width, height = rectangle.Height });
            }
            File.AppendAllText(Path.Combine(UiTestHost.Root, "flyout-text-layout.jsonl"),
                System.Text.Json.JsonSerializer.Serialize(new { theme = Surface.ActualTheme.ToString(),
                    text = Status.Text, font = Status.FontFamily.Source, fontSize = Status.FontSize,
                    lineHeight = Status.LineHeight, stacking = Status.LineStackingStrategy.ToString(),
                    width = Status.ActualWidth, height = Status.ActualHeight, desiredHeight = Status.DesiredSize.Height,
                    trimmed = Status.IsTextTrimmed, x = origin.X, y = origin.Y,
                    headerHeight = header.ActualHeight, headerDesiredHeight = header.DesiredSize.Height,
                    rowHeight = grid.RowDefinitions[0].ActualHeight, viewportTop = viewport.Y,
                    scale = Surface.XamlRoot.RasterizationScale, characters }) + "\r\n");
            // Native text layout rounds fractional line metrics. Allow one physical
            // pixel, but refuse trimmed/overlapping or unavailable text bounds.
            double tolerance = 1 / Surface.XamlRoot.RasterizationScale;
            if (Double.IsPositiveInfinity(top) || top < -tolerance || bottom > Status.DesiredSize.Height + tolerance ||
                right > Status.ActualWidth + tolerance || Status.IsTextTrimmed ||
                origin.Y + Math.Max(Status.ActualHeight, Status.DesiredSize.Height) > viewport.Y + tolerance ||
                header.ActualHeight + tolerance < header.DesiredSize.Height || grid.RowDefinitions[0].ActualHeight + tolerance < header.ActualHeight)
                throw new InvalidOperationException("Native flyout status bounds are unavailable, trimmed or overlap the device viewport.");
        }
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
