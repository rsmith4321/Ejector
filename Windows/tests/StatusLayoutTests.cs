using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        static T WindowControl<T>(DeviceWindow window, string name) where T : Control { return (T)typeof(DeviceWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window); }
        static void StatusLayoutTests() {
            Test("minimum device window retains device list and action access with long operation results", delegate {
                using (var window = new DeviceWindow(true)) {
                    window.Size = window.MinimumSize; window.PresentInventory(TrayState()); var status = WindowControl<TextBox>(window, "status");
                    string result = String.Join(Environment.NewLine, Enumerable.Range(1, 80).Select(i => "Synthetic saved copy " + i + " remains intact; retained work is available for manual review.")); status.Text = result;
                    CreateRenderHandles(window); window.PerformLayout(); var list = WindowControl<ListView>(window, "list"); var cancel = WindowControl<Button>(window, "cancel");
                    double factor = window.CurrentAutoScaleDimensions.Height / 96.0;
                    Check(list.Height >= Math.Max(100, window.Font.Height * 4) && status.Height <= 96 * factor && status.Text == result && status.ReadOnly && status.Multiline && status.ScrollBars == ScrollBars.Vertical, "Result text collapsed list, lost data or cannot scroll | list=" + list.Height + ", status=" + status.Height + ", DPI factor=" + factor + ", exact text=" + (status.Text == result));
                    var cancelPoint = window.PointToClient(cancel.PointToScreen(System.Drawing.Point.Empty)); Check(cancelPoint.Y >= 0 && cancelPoint.Y + cancel.Height <= window.ClientSize.Height, "Long result pushed actions outside window");
                }
            });
            Test("device result viewport preserves full unbroken paths and stays bounded through resize and later progress", delegate {
                using (var window = new DeviceWindow(true)) {
                    window.Size = window.MinimumSize; var status = WindowControl<TextBox>(window, "status"); string message = new string('x', 12000); status.Text = message; CreateRenderHandles(window); window.PerformLayout();
                    status.SelectAll(); Check(status.SelectedText == message && status.WordWrap && WindowControl<ListView>(window, "list").Height >= 100, "Long path text lost or collapsed devices");
                    window.Size = new System.Drawing.Size(1000, 700); window.PerformLayout(); Check(status.Height <= 96 * window.CurrentAutoScaleDimensions.Height / 96.0 && status.Text == message, "Resize inflated or truncated result");
                    status.Text = "Verifying saved originals…"; window.Size = window.MinimumSize; window.PerformLayout(); Check(status.Text.StartsWith("Verifying") && WindowControl<ListView>(window, "list").Height >= 100, "Later progress broke layout");
                }
            });
        }
    }
}
