using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Forms = System.Windows.Forms;

namespace EasyEject.Modern {
    public sealed partial class MainWindow {
        AppIssue appIssue = new AppIssue(); bool issueLoaded, issuePersistenceAvailable;
        long? discoveryIssueEpoch; string discoveryIssueText;
        readonly Forms.ToolStripMenuItem issueMenu = new Forms.ToolStripMenuItem("View issue…");
        readonly Forms.ToolStripMenuItem debugMenu = new Forms.ToolStripMenuItem("View debug log…");
        readonly Forms.ToolStripMenuItem helpMenu = new Forms.ToolStripMenuItem("Help & instructions…");
        readonly Forms.ToolStripMenuItem aboutMenu = new Forms.ToolStripMenuItem("About Easy Eject…");
        string RecordModernIssue(string message) {
            if (closed) return null;
            appIssue.Record(message); string warning = null;
            try { SaveModernIssue(IssueReview.Copy(appIssue)); }
            catch (Exception ex) { warning = "Issue remains available here, but could not be saved. " + ex.Message; }
            if (warning != null) Summary.Text += "\n" + warning;
            UpdateImportTray(); return warning;
        }
        void SaveModernIssue(AppIssue value) {
            if (!issuePersistenceAvailable) throw new SafetyException("Saved issue state is unavailable; its existing record was retained.");
            AppIssue.Save(value);
        }
        void ClearModernIssue() {
            if (closed || !appIssue.HasIssue) return;
            var cleared = IssueReview.Copy(appIssue); cleared.Clear();
            try { SaveModernIssue(cleared); appIssue.Clear(); }
            catch (Exception ex) { Summary.Text += "\nEarlier issue remains available; clearing its saved status failed. " + ex.Message; }
            UpdateImportTray();
        }
        void RecordDiscoveryIssue(string message) {
            if (discoveryIssueEpoch == discovery.Epoch && discoveryIssueText == message) return;
            discoveryIssueEpoch = discovery.Epoch; discoveryIssueText = message; RecordModernIssue(message);
        }
        void Issue_Click(object sender, RoutedEventArgs args) { ViewModernIssue(); }
        void Debug_Click(object sender, RoutedEventArgs args) { ViewModernDebugLog(); }
        void Help_Click(object sender, RoutedEventArgs args) { ViewModernInformation(false); }
        void About_Click(object sender, RoutedEventArgs args) { ViewModernInformation(true); }
        async void ViewModernIssue() {
            if (!appIssue.HasIssue || !BeginSavedReview()) return;
            IssueReview owner = null;
            try {
                await EnsureVisible(); owner = new IssueReview(appIssue, SaveModernIssue);
                confirmation = MessageDialog(appIssue.NeedsAttention ? "Needs attention" : "Last issue (acknowledged)", owner.Description, owner.CanDismiss ? "Dismiss issue" : null);
                if (await confirmation.ShowAsync() == ContentDialogResult.Primary) { owner.Dismiss(); Summary.Text = "Issue acknowledged. No import, export or removal was retried."; }
            } catch (Exception ex) { Summary.Text = "Issue acknowledgement did not finish. " + ex.Message; }
            finally { if (owner != null) owner.Cancel(); FinishSavedReview(); }
        }
        async void ViewModernInformation(bool about) {
            if (!BeginSavedReview()) return;
            try { await EnsureVisible(); confirmation = MessageDialog(about ? "About Easy Eject" : "Help & instructions", about ? ApplicationInformation.ModernAbout : ApplicationInformation.ModernHelp, null); await confirmation.ShowAsync(); }
            catch (Exception ex) { Summary.Text = "Information could not be displayed. " + ex.Message; }
            finally { FinishSavedReview(); }
        }
        async void ViewModernDebugLog() {
            if (settings == null || !settings.DebugEnabled || !BeginSavedReview()) return;
            DiagnosticsReview owner = null; Task pendingWork = null;
            try {
                await EnsureVisible(); string root = Profiles.Root; owner = await Task.Run(() => new DiagnosticsReview(root));
                var logText = new TextBox { Text = owner.Text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono"), MinHeight = 180, MaxHeight = 320 };
                ScrollViewer.SetVerticalScrollBarVisibility(logText, ScrollBarVisibility.Auto); ScrollViewer.SetHorizontalScrollBarVisibility(logText, ScrollBarVisibility.Auto);
                var status = Wrapped("Only the displayed log is copied or cleared. Logs may contain device names and file paths; nothing is sent automatically.");
                var refresh = new Button { Content = "Refresh" }; var copy = new Button { Content = "Copy to Clipboard" }; var clear = new Button { Content = "Clear displayed log" };
                var buttons = new ActionWrapPanel(); buttons.Children.Add(refresh); buttons.Children.Add(copy); buttons.Children.Add(clear);
                var body = new StackPanel { Spacing = 12 }; body.Children.Add(logText); body.Children.Add(status); body.Children.Add(buttons);
                var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Debug log", Content = body, CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close };
                confirmation = dialog; bool worker = false;
                Action render = () => { if (closed || confirmation != dialog) return; logText.Text = owner.Text; refresh.IsEnabled = !worker && owner.Open; copy.IsEnabled = !worker && owner.CanCopy; clear.IsEnabled = !worker && owner.CanClear; };
                Func<Action, string, Task> run = async (action, success) => {
                    if (closed || confirmation != dialog || worker || !owner.Open) return;
                    worker = true; render(); status.Text = "Working on the reviewed log…";
                    try { pendingWork = Task.Run(action); await pendingWork; if (!closed && confirmation == dialog) status.Text = success; }
                    catch (Exception ex) { if (!closed && confirmation == dialog) status.Text = ex.Message; }
                    finally { worker = false; render(); }
                };
                refresh.Click += async delegate { await run(owner.Refresh, "Current debug log displayed."); };
                clear.Click += async delegate { await run(owner.Clear, "Displayed log cleared. Refresh reads any later events."); };
                copy.Click += delegate {
                    if (closed || confirmation != dialog || worker || !owner.CanCopy) return;
                    worker = true; render();
                    try { owner.Copy(value => Forms.Clipboard.SetText(value)); status.Text = "Displayed debug log copied to Clipboard."; }
                    catch (Exception ex) { status.Text = "Clipboard copy failed: " + ex.Message; }
                    finally { worker = false; render(); }
                };
                dialog.Closing += delegate(ContentDialog sender, ContentDialogClosingEventArgs args) { if (worker || owner.Busy) { args.Cancel = true; status.Text = "Wait for the current log operation before closing."; } };
                render(); await dialog.ShowAsync();
            } catch (Exception ex) { Summary.Text = "Debug log could not be reviewed. Existing diagnostics were retained. " + ex.Message; RecordModernIssue(Summary.Text); }
            finally {
                // Even if XAML presentation fails, the exact log worker retains its
                // view until the file operation ends; no second action is admitted.
                if (pendingWork != null) try { await pendingWork; } catch { }
                if (owner != null) owner.Dispose(); FinishSavedReview();
            }
        }
    }
}
