#if UI_TESTS
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace EasyEject.Modern {
    // Compiled only into an explicit UI-test build. Never shipped in a review/release.
    // Automated/inspect modes have no discovery/workers/device APIs. Explicit review
    // mode uses normal manual workflows with isolated preferences and no automatic pump.
    internal static class UiTestHost {
        internal static string Root;
        internal static bool Inspect;
        internal static bool Review;
        const string ReviewScope = "Isolated preferences; normal device discovery and manual actions. No scripted hardware action.\r\n";
        static FileStream reviewOwner;
        [STAThread] static void Main(string[] args) {
            if (args.Length < 1 || args.Length > 2 || args.Length == 2 && args[1] != "inspect" && args[1] != "review" && args[1] != "review-restart") throw new ArgumentException("An owned isolated fixture root is required.");
            Inspect = args.Length == 2 && args[1] == "inspect";
            Review = args.Length == 2 && (args[1] == "review" || args[1] == "review-restart");
            Root = Path.GetFullPath(args[0]);
            string parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectFixtures");
            if (!String.Equals(Path.GetDirectoryName(Root), parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(Root).StartsWith("ui-", StringComparison.Ordinal)) throw new ArgumentException("UI test root must be an owned ui- fixture child.");
            for (var ancestor = new DirectoryInfo(Root); ancestor != null; ancestor = ancestor.Parent)
                if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("UI fixtures require ordinary parent directories.");
            bool restart = args.Length == 2 && args[1] == "review-restart";
            if (restart) {
                string marker = Path.Combine(Root, "review-scope.txt");
                if (!File.Exists(marker) || (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0 || File.ReadAllText(marker) != ReviewScope) throw new ArgumentException("Restart requires the exact owned review fixture marker.");
            } else if (Directory.Exists(Root) || File.Exists(Root)) throw new ArgumentException("Choose a fresh UI fixture root.");
            Directory.CreateDirectory(Root); Profiles.IsolatedAppDataForTests = Root;
            if (Review) {
                string owner = Path.Combine(Root, "review-owner.lock");
                if (File.Exists(owner) && (File.GetAttributes(owner) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Redirected review owner refused.");
                reviewOwner = new FileStream(owner, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ => {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                new App(new TrayStartupGate(), false, () => {});
            });
        }
        internal static async void Run(MainWindow window) {
            if (Review) {
                // Explicit foreground review: normal inventory and manual workflows,
                // but fresh preferences/history owned solely by this fixture.
                window.Title = "Easy Eject · isolated review";
                window.Closed += (_, args) => { reviewOwner?.Dispose(); Application.Current.Exit(); };
                File.WriteAllText(Path.Combine(Root, "review-scope.txt"), ReviewScope);
                window.InitializeReviewControls();
                return;
            }
            try { await window.TestTrayQuit(); File.WriteAllText(Path.Combine(Root, "result.txt"), "PASS actual tray Quit preserves defaults draft and refuses busy/editor Quit; idle Quit closes.\r\n"); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(Root, "result.txt"), "FAIL " + ex); Environment.ExitCode = 1; }
            finally { Application.Current.Exit(); }
        }
    }
    public sealed partial class MainWindow {
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        internal void InitializeReviewControls() {
            var footer = (Microsoft.UI.Xaml.Controls.StackPanel)Navigation.PaneFooter;
            var flyout = new Microsoft.UI.Xaml.Controls.Button { Content = "Test tray flyout" };
            flyout.Click += (_, args) => { var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition).Bounds; ToggleTrayFlyout(new System.Drawing.Point(area.Right - 140, area.Bottom - 20)); };
            var quit = new Microsoft.UI.Xaml.Controls.Button { Content = "Test tray Quit" };
            quit.Click += (_, args) => tray.ContextMenuStrip.Items.Cast<System.Windows.Forms.ToolStripItem>().Single(item => item.Text == "Quit preview").PerformClick();
            footer.Children.Add(flyout); footer.Children.Add(quit);
        }
        internal async Task TestTrayQuit() {
            await EnsureVisible();
            var quitItem = tray.ContextMenuStrip.Items.Cast<System.Windows.Forms.ToolStripItem>().Single(item => item.Text == "Quit preview");
            DefaultsPanel.Visibility = Visibility.Visible; StorageView.Visibility = Visibility.Collapsed;
            await DefaultsPanel.StartEdit();
            var box = (Microsoft.UI.Xaml.Controls.CheckBox)DefaultsPanel.FindName("Previews");
            bool before = box.IsChecked == true; box.IsChecked = !before;
            quitItem.PerformClick(); await Task.Yield();
            Require(!closed && !quitting && DefaultsPanel.Visibility == Visibility.Visible && box.IsChecked == !before, "Tray Quit discarded the unsaved defaults draft.");
            DefaultsPanel.Visibility = Visibility.Collapsed;
            quitItem.PerformClick(); await Task.Yield();
            Require(!closed && !quitting && DefaultsPanel.Visibility == Visibility.Visible && box.IsChecked == !before, "Tray Quit discarded a hidden active defaults draft.");
            Require(DefaultsPanel.TryLeave(), "Could not cancel test draft."); ShowStorage();
            working = true; quitItem.PerformClick(); await Task.Yield();
            Require(!closed && !quitting, "Tray Quit interrupted work."); working = false;
            ProfilesPanel.Visibility = Visibility.Visible; quitItem.PerformClick(); await Task.Yield();
            Require(!closed && !quitting, "Tray Quit abandoned profile editor."); ShowStorage();
            SettingsPanel.Visibility = Visibility.Visible; quitItem.PerformClick(); await Task.Yield();
            Require(!closed && !quitting, "Tray Quit abandoned settings editor."); ShowStorage();
            await ProfilesPanel.StartCatalog(); ShowStorage();
            Require(!EditorOpen && !StorageBusy, "A hidden profile catalog blocked the saved-profile import handoff and completion review.");
            ProfilesPanel.TestDraft(); ProfilesPanel.Visibility = Visibility.Collapsed;
            quitItem.PerformClick(); await Task.Yield();
            Require(!closed && !quitting && ProfilesPanel.Visibility == Visibility.Visible && ProfilesPanel.Editing, "Tray Quit abandoned a hidden active profile draft.");
            Require(ProfilesPanel.TryLeave(), "Could not cancel profile test draft."); ShowStorage();
            var prompt = MessageDialog(ManualEjectPrompt.Title, "Generated fixture only", ManualEjectPrompt.Confirm); prompt.CloseButtonText = ManualEjectPrompt.Keep;
            Require(prompt.DefaultButton == Microsoft.UI.Xaml.Controls.ContentDialogButton.Close && prompt.CloseButtonText == "Keep Connected", "Native eject prompt has an unsafe default.");
            if (UiTestHost.Inspect) {
                trayFlyout = new TrayFlyoutWindow(Reveal, () => { Reveal(); OpenSettings(); }, RequestQuit, async (device, kind) => {
                    if (kind != "eject") { Reveal(); return; }
                    working = true;
                    try {
                        var fixturePrompt = MessageDialog(ManualEjectPrompt.Title, "Generated fixture only. No real device API is connected.", ManualEjectPrompt.Confirm, trayFlyout.DialogRoot);
                        fixturePrompt.CloseButtonText = ManualEjectPrompt.Keep;
                        var answer = await fixturePrompt.ShowAsync();
                        File.AppendAllText(Path.Combine(UiTestHost.Root, "fixture-actions.txt"), "Fixture eject choice: " + answer + " (no device API)\r\n");
                        trayFlyout.Report(answer == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary ? "Fixture request only. No device was removed." : "Keep Connected. No device action.");
                    } finally { working = false; }
                }, RefreshTrayFlyout);
                AppWindow.Hide();
                var area = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                trayFlyout.ShowAt(new System.Drawing.Point(area.Right - 140, area.Bottom - 20), FlyoutView());
                File.WriteAllText(Path.Combine(UiTestHost.Root, "ready.txt"), "UI fixture ready; write continue.txt to finish.");
                while (!File.Exists(Path.Combine(UiTestHost.Root, "continue.txt"))) await Task.Delay(250);
                Require(!closed, "UI fixture unexpectedly closed.");
                DefaultsPanel.TryLeave(); ProfilesPanel.TryLeave(); SettingsPanel.TryLeave(); ShowStorage();
            }
            quitItem.PerformClick(); Require(closed, "Idle tray Quit did not close.");
        }
        internal TrayFlyoutView FixtureFlyout() {
            var state = new Inventory { Epoch = 1 };
            state.Disks.Add(new Disk { Number = 700, DevInst = 700, TargetDevInst = 701, InstanceId = "fixture-disk", TargetId = "fixture-usb", Serial = "fixture-serial", Ancestors = new System.Collections.Generic.List<string> { "fixture-disk", "fixture-usb" } });
            state.Volumes.Add(new Volume { Id = "fixture-camera", Label = "Camera · generated fixture", Serial = "fixture", Disks = new System.Collections.Generic.List<int> { 700 }, Mounts = new System.Collections.Generic.List<string> { "Z:\\" }, Group = StorageGroup.Camera });
            return TrayFlyoutModel.Build(new TrayFlyoutInput { State = state, Epoch = 1, MonitoringAvailable = true, SettingsLoaded = true, EditorOpen = EditorOpen, Work = UiBusy, SharingOptedIn = _ => true, Space = _ => new TrayFlyoutSpace(128000000000, 64000000000) });
        }
    }
    public sealed partial class ProfilesView {
        internal void TestDraft() {
            active = true;
            editor = new ImportProfileEdit(new ImportProfile { Name = "Generated profile draft", VolumeId = "fixture-volume", VolumeSerial = "fixture-serial", DiskInstance = "fixture-disk", RelativeSource = "", Destination = UiTestHost.Root }, new ImportDefaults(), null, (_, chosen) => {}, null);
            Render();
        }
    }
}
#endif
