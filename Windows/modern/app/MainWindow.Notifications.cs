using Forms = System.Windows.Forms;

namespace EasyEject.Modern {
    public sealed partial class MainWindow {
        string TryModernNotification(string title, string message, bool warning, bool enabled) {
            return TrayNotificationDelivery.TryShow(enabled, closed, title, message, warning,
                (timeout, heading, body, attention) => tray.ShowBalloonTip(timeout, heading, body,
                    attention ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info));
        }
    }
}
