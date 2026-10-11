using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        static T WindowControl<T>(DeviceWindow window, string name) where T : Control { return (T)typeof(DeviceWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window); }
        static void StatusLayoutTests() {
            foreach (float scale in FixedTestScales) Test("minimum device window retains device list and action access with long operation results at fixed " + ScaleName(scale) + " scale", () => AtFixedScale(scale, () => {
                using (var window = new DeviceWindow(true)) {
                    window.Size = window.MinimumSize; window.PresentInventory(TrayState()); var status = WindowControl<TextBox>(window, "status");
                    string result = String.Join(Environment.NewLine, Enumerable.Range(1, 80).Select(i => "Synthetic saved copy " + i + " remains intact; retained work is available for manual review.")); status.Text = result;
                    CreateRenderHandles(window); window.PerformLayout(); var list = WindowControl<ListView>(window, "list"); var cancel = WindowControl<Button>(window, "cancel");
                    double factor = scale;
                    Check(list.Height >= Math.Max(100, window.Font.Height * 4) && status.Height <= 96 * factor && status.Text == result && status.ReadOnly && status.Multiline && status.ScrollBars == ScrollBars.Vertical, "Result text collapsed list, lost data or cannot scroll | list=" + list.Height + ", status=" + status.Height + ", scale=" + factor + ", window=" + window.ClientSize + ", rows=" + String.Join("/", ((TableLayoutPanel)list.Parent).GetRowHeights()) + ", exact text=" + (status.Text == result));
                    var actions = (FlowLayoutPanel)cancel.Parent; var cancelPoint = window.PointToClient(cancel.PointToScreen(System.Drawing.Point.Empty)); var actionsPoint = window.PointToClient(actions.PointToScreen(System.Drawing.Point.Empty));
                    Check(actionsPoint.Y >= 0 && actionsPoint.Y + actions.Height <= window.ClientSize.Height && actions.Height >= cancel.Height, "Action row is outside the window or shorter than one button | actions=" + actions.Bounds);
                    Check(actions.AutoScroll ? actions.DisplayRectangle.Contains(new System.Drawing.Rectangle(cancel.Left - actions.AutoScrollPosition.X, cancel.Top - actions.AutoScrollPosition.Y, cancel.Width, cancel.Height)) || cancel.Bounds.IntersectsWith(actions.DisplayRectangle) : cancelPoint.Y >= 0 && cancelPoint.Y + cancel.Height <= window.ClientSize.Height, "Long result pushed Cancel out of reach");
                }
            }));
            foreach (float scale in FixedTestScales) Test("device result viewport preserves full unbroken paths and stays bounded through resize and later progress at fixed " + ScaleName(scale) + " scale", () => AtFixedScale(scale, () => {
                using (var window = new DeviceWindow(true)) {
                    window.Size = window.MinimumSize; var status = WindowControl<TextBox>(window, "status"); string message = new string('x', 12000); status.Text = message; CreateRenderHandles(window); window.PerformLayout();
                    status.SelectAll(); Check(status.SelectedText == message && status.WordWrap && WindowControl<ListView>(window, "list").Height >= 100, "Long path text lost or collapsed devices | list=" + WindowControl<ListView>(window, "list").Height + ", window=" + window.ClientSize);
                    window.Size = new System.Drawing.Size((int)(1000 * scale), (int)(700 * scale)); window.PerformLayout(); Check(status.Height <= 96 * scale && status.Text == message, "Resize inflated or truncated result");
                    status.Text = "Verifying saved originals…"; window.Size = window.MinimumSize; window.PerformLayout(); Check(status.Text.StartsWith("Verifying") && WindowControl<ListView>(window, "list").Height >= 100, "Later progress broke layout");
                }
            }));
        }
    }
}
