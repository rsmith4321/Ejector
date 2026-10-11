#if UI_TESTS
using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;

namespace EasyEject.Modern {
    public sealed partial class MainWindow {
        // Explicit inspection only. Real NotifyImport/tray delivery and reveal;
        // generated text, isolated settings, no runtime pump or device action.
        void InitializeNotificationInspection() {
            settings = new AppSettings { ShowNotifications = true, EnableShortcut = false, AutomaticUpdateChecks = false };
            int sequence = 0, shown = 0, clicked = 0, dismissed = 0;
            Action<string> record = kind => {
                if (closed) return;
                File.AppendAllText(Path.Combine(UiTestHost.Root, "notification-events.jsonl"),
                    System.Text.Json.JsonSerializer.Serialize(new { utc = DateTime.UtcNow, kind, sequence,
                        shown, clicked, dismissed, enabled = settings.NotificationsEnabled,
                        pending = completions.Entries.Length, visible = AppWindow.IsVisible,
                        presenter = (AppWindow.Presenter as OverlappedPresenter)?.State.ToString(),
                        working, confirmationOpen = confirmation != null }) + "\r\n");
            };
            tray.BalloonTipShown += (_, args) => { shown++; record("ShellShown"); };
            tray.BalloonTipClosed += (_, args) => { dismissed++; record("ShellClosed"); };
            tray.BalloonTipClicked += (_, args) => {
                clicked++; record("ShellClicked");
                // The production handler performs Reveal. Observe its queued result.
                DispatcherQueue.TryEnqueue(() => record("AfterClickReveal"));
            };
            var footer = (StackPanel)Navigation.PaneFooter;
            var pending = new Button { Content = "Review pending choices" };
            pending.Click += (_, args) => { record("ReviewPendingRequested"); ReviewPendingImports(); };
            footer.Children.Add(pending);
            foreach (bool enabled in new[] { false, true }) {
                var button = new Button { Content = enabled ? "Send generated notification" : "Check notifications off" };
                button.Click += (_, args) => {
                    if (StorageBusy) return;
                    sequence++; settings.ShowNotifications = enabled;
                    Summary.Text = "Generated notification check only. No files copied or device removed. Pending choices are retained.";
                    NotifyImport("Easy Eject generated check " + sequence, Summary.Text, false);
                    if (enabled) ((OverlappedPresenter)AppWindow.Presenter).Minimize();
                    record(enabled ? "EnabledRequestedAndMinimized" : "DisabledRequested");
                };
                footer.Children.Add(button);
            }
            record("Ready");
        }
    }
}
#endif
