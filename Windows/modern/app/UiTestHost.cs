#if UI_TESTS
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace EasyEject.Modern {
    // Compiled only into an explicit UI-test build. Never shipped in a review/release.
    // Automated/basic inspect modes have no discovery/workers/device APIs.
    // Completion inspection registers read-only monitoring for real destination guards.
    // Explicit review
    // mode uses normal manual workflows with isolated preferences and no automatic pump.
    internal static class UiTestHost {
        internal static string Root;
        internal static bool Inspect;
        internal static bool InspectLong;
        internal static bool InspectCompletion;
        internal static bool InspectNotifications;
        internal static bool InspectThemes;
        internal static bool Review;
        static bool smallReview;
        const string ReviewScope = "Isolated preferences; normal device discovery and manual actions. No scripted hardware action.\r\n";
        static FileStream reviewOwner;
        [STAThread] static void Main(string[] args) {
            if (args.Length < 1 || args.Length > 2 || args.Length == 2 && args[1] != "inspect" && args[1] != "inspect-long" && args[1] != "inspect-completion" && args[1] != "inspect-notifications" && args[1] != "inspect-themes" && args[1] != "review" && args[1] != "review-restart" && args[1] != "review-small") throw new ArgumentException("An owned isolated fixture root is required.");
            InspectThemes = args.Length == 2 && args[1] == "inspect-themes";
            InspectLong = InspectThemes || args.Length == 2 && args[1] == "inspect-long";
            InspectNotifications = args.Length == 2 && args[1] == "inspect-notifications";
            InspectCompletion = InspectNotifications || args.Length == 2 && args[1] == "inspect-completion";
            Inspect = InspectCompletion || InspectLong || args.Length == 2 && args[1] == "inspect";
            smallReview = args.Length == 2 && args[1] == "review-small";
            Review = smallReview || args.Length == 2 && (args[1] == "review" || args[1] == "review-restart");
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
                var app = new App(new TrayStartupGate(), false, () => {});
                // Preserve engineering-host exceptions without handling/suppressing them.
                app.UnhandledException += (_, args) => File.AppendAllText(Path.Combine(Root, "unhandled.txt"), args.Exception + "\r\n");
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
                if (smallReview) await window.InitializeSmallReview();
                return;
            }
            try { await window.TestTrayQuit(); File.WriteAllText(Path.Combine(Root, "result.txt"), "PASS native minimized reveal/maximized retention; actual tray Quit preserves defaults draft and refuses busy/editor Quit; idle Quit closes.\r\n"); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(Root, "result.txt"), "FAIL " + ex); Environment.ExitCode = 1; }
            finally { Application.Current.Exit(); }
        }
    }
    public sealed partial class MainWindow {
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        internal async Task InitializeSmallReview() {
            await EnsureVisible();
            double scale = Root.XamlRoot.RasterizationScale;
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Ceiling(680 * scale), (int)Math.Ceiling(600 * scale)));
            await Task.Delay(150); Root.UpdateLayout();
            File.WriteAllText(Path.Combine(UiTestHost.Root, "small-window.json"), System.Text.Json.JsonSerializer.Serialize(new {
                requestedWidth = 680, requestedHeight = 600, rasterizationScale = scale,
                clientWidth = Root.ActualWidth, clientHeight = Root.ActualHeight,
                cardsHeight = StorageCards.ActualHeight, footerHeight = StorageFooter.ActualHeight,
                physicalWidth = AppWindow.Size.Width, physicalHeight = AppWindow.Size.Height }));
        }
        internal void InitializeReviewControls() {
            InitializeReviewKeyboardProbe();
            var footer = (Microsoft.UI.Xaml.Controls.StackPanel)Navigation.PaneFooter;
            InitializeReleaseReviewProbe(footer);
            var flyout = new Microsoft.UI.Xaml.Controls.Button { Content = "Test tray flyout" };
            flyout.Click += (_, args) => { var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition).Bounds; ToggleTrayFlyout(new System.Drawing.Point(area.Right - 140, area.Bottom - 20)); };
            var quit = new Microsoft.UI.Xaml.Controls.Button { Content = "Test tray Quit" };
            quit.Click += (_, args) => tray.ContextMenuStrip.Items.Cast<System.Windows.Forms.ToolStripItem>().Single(item => item.Text == "Quit preview").PerformClick();
            string firstReviewedDebugText = null;
            var debug = new Microsoft.UI.Xaml.Controls.Button { Content = "Test debug log" };
            debug.Click += (_, args) => {
                if (StorageBusy || settings == null || !settings.DebugEnabled) return;
                firstReviewedDebugText ??= DebugLog.Read(Profiles.Root);
                debugMenu.PerformClick();
            };
            var clipboard = new Microsoft.UI.Xaml.Controls.Button { Content = "Check pasted debug text" };
            clipboard.Click += async (_, args) => {
                if (firstReviewedDebugText == null || !BeginSavedReview()) return;
                try {
                    await EnsureVisible();
                    string current = DebugLog.Read(Profiles.Root);
                    string rawClipboard = System.Windows.Forms.Clipboard.GetText();
                    Func<string, string> lines = value => value.Replace("\r\n", "\n").Replace("\r", "\n");
                    var paste = new Microsoft.UI.Xaml.Controls.TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 160 };
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(paste, "Paste generated debug log here");
                    paste.TextChanged += (_, changed) => File.WriteAllText(Path.Combine(UiTestHost.Root, "clipboard-check.json"),
                        System.Text.Json.JsonSerializer.Serialize(new { utc = DateTime.UtcNow,
                            rawMatchesFirstReviewedLog = rawClipboard == firstReviewedDebugText, rawMatchesCurrentLog = rawClipboard == current,
                            pastedLinesMatchFirstReviewedLog = lines(paste.Text) == lines(firstReviewedDebugText),
                            pastedLinesMatchCurrentLog = lines(paste.Text) == lines(current),
                            pastedCharacters = paste.Text.Length, rawClipboardCharacters = rawClipboard.Length,
                            firstReviewedCharacters = firstReviewedDebugText.Length, currentCharacters = current.Length }));
                    confirmation = MessageDialog("Generated diagnostic clipboard check", "", null);
                    confirmation.Content = paste; await confirmation.ShowAsync();
                } catch (Exception ex) { Summary.Text = "Generated diagnostic clipboard comparison unavailable. " + ex.Message; }
                finally { FinishSavedReview(); }
            };
            var pauseDebug = new Microsoft.UI.Xaml.Controls.Button { Content = "Pause next debug Refresh" };
            pauseDebug.Click += (_, args) => {
                if (StorageBusy || settings == null || !settings.DebugEnabled) return;
                if (File.Exists(Path.Combine(UiTestHost.Root, "debug-worker-started.json")) ||
                    File.Exists(Path.Combine(UiTestHost.Root, "debug-worker-release.txt"))) {
                    Summary.Text = "Choose a fresh owned fixture for another paused debug review."; return;
                }
                Interlocked.Exchange(ref pauseNextDebugRefresh, 1);
                Summary.Text = "Engineering-only pause armed for the next debug view's first Refresh; no file action started.";
            };
            footer.Children.Add(flyout); footer.Children.Add(quit); footer.Children.Add(debug); footer.Children.Add(clipboard); footer.Children.Add(pauseDebug);
        }
        int pauseNextDebugRefresh;
        DiagnosticsReview CreateReviewDiagnostics(string root) {
            if (!UiTestHost.Review || Interlocked.Exchange(ref pauseNextDebugRefresh, 0) == 0)
                return new DiagnosticsReview(root);
            // Hold a real review's load callback before its real read. This makes
            // native Closing/disabled controls observable without slow hardware,
            // deleting a log, or replacing the production dialog/controller.
            DiagnosticsReview review = null; int paused = 0;
            review = new DiagnosticsReview(DebugLog.Load(root), () => {
                if (Interlocked.Exchange(ref paused, 1) == 0) {
                    string release = Path.Combine(UiTestHost.Root, "debug-worker-release.txt");
                    File.WriteAllText(Path.Combine(UiTestHost.Root, "debug-worker-started.json"),
                        System.Text.Json.JsonSerializer.Serialize(new { utc = DateTime.UtcNow, busy = review.Busy, root }));
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    while (!File.Exists(release)) {
                        if (timer.Elapsed > TimeSpan.FromMinutes(3)) throw new TimeoutException("Owned diagnostic worker pause timed out; displayed log retained.");
                        Thread.Sleep(50);
                    }
                }
                var snapshot = DebugLog.Load(root);
                File.WriteAllText(Path.Combine(UiTestHost.Root, "debug-worker-read.json"),
                    System.Text.Json.JsonSerializer.Serialize(new { utc = DateTime.UtcNow, busy = review.Busy, characters = snapshot.Text.Length }));
                return snapshot;
            }, expected => DebugLog.Clear(expected, root));
            return review;
        }
        // Diagnostic evidence only: UI_TESTS review builds record activation and
        // Escape routing. Never record text or other keys, and never handle a key.
        void InitializeReviewKeyboardProbe() {
            var trace = new StreamWriter(new FileStream(Path.Combine(UiTestHost.Root,
                "keyboard-probe-" + Guid.NewGuid().ToString("N") + ".jsonl"),
                FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
            var observed = new HashSet<UIElement>();
            bool stopped = false;
            Action<string, bool?, string> record = (kind, handled, source) => {
                if (stopped) return;
                trace.WriteLine(
                System.Text.Json.JsonSerializer.Serialize(new { utc = DateTime.UtcNow, kind, handled, source,
                    presenterState = (AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter)?.State.ToString(), visible = AppWindow.IsVisible }));
            };
            Action<UIElement> attach = element => {
                if (!observed.Add(element)) return;
                element.AddHandler(UIElement.PreviewKeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((sender, args) => {
                    if (args.Key == Windows.System.VirtualKey.Escape)
                        record("PreviewEscape", args.Handled, args.OriginalSource?.GetType().Name);
                }), true);
                element.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((sender, args) => {
                    if (args.Key == Windows.System.VirtualKey.Escape)
                        record("Escape", args.Handled, args.OriginalSource?.GetType().Name);
                }), true);
                record("Attached", null, element.GetType().Name);
            };
            attach(Root);
            var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(250);
            timer.Tick += (_, args) => {
                if (stopped || Root.XamlRoot == null) return;
                foreach (var popup in Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot))
                    if (popup.Child is UIElement child) attach(child);
            };
            Activated += (_, args) => record("Activation", null, args.WindowActivationState.ToString());
            Closed += (_, args) => { stopped = true; timer.Stop(); trace.Dispose(); };
            timer.Start();
        }
        internal async Task TestTrayQuit() {
            await EnsureVisible();
            await TestNativeThemeRendering();
            await TestCompletionSafeFocus();
            await TestPendingPickerFocus();
            var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)AppWindow.Presenter;
            presenter.Minimize();
            Require(presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized, "Native presenter did not minimize.");
            Reveal(); await Task.Delay(100);
            Require(presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Restored, "Reveal left the native window minimized.");
            presenter.Maximize();
            Reveal(); await Task.Delay(100);
            Require(presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized, "Reveal discarded maximized window state.");
            presenter.Minimize(); await EnsureVisible();
            Require(presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized, "Visible operation review did not restore the prior maximized window state.");
            presenter.Restore();
            var originalSize = AppWindow.Size;
            double compactScale = Root.XamlRoot.RasterizationScale;
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Ceiling(680 * compactScale), (int)Math.Ceiling(600 * compactScale)));
            await Task.Delay(100); Root.UpdateLayout();
            Require(StorageCards.ActualHeight >= 96, "Compact storage window has no usable device list; height=" + StorageCards.ActualHeight);
            CancelOperation.Visibility = Visibility.Visible; Root.UpdateLayout();
            StorageActions.ChangeView(null, Double.MaxValue, null, true); await Task.Delay(100); Root.UpdateLayout();
            var stopBounds = CancelOperation.TransformToVisual(Root).TransformBounds(new Windows.Foundation.Rect(0, 0, CancelOperation.ActualWidth, CancelOperation.ActualHeight));
            var actionsBounds = StorageActions.TransformToVisual(Root).TransformBounds(new Windows.Foundation.Rect(0, 0, StorageActions.ActualWidth, StorageActions.ActualHeight));
            var aboutBounds = AboutButton.TransformToVisual(Root).TransformBounds(new Windows.Foundation.Rect(0, 0, AboutButton.ActualWidth, AboutButton.ActualHeight));
            Require(StorageCards.ActualHeight >= 96 && StorageActions.ScrollableHeight > 0, "Compact action viewport displaced the device list or did not scroll.");
            Require(stopBounds.Height > 0 && stopBounds.Top >= 0 && stopBounds.Bottom <= actionsBounds.Top && actionsBounds.Bottom <= Root.ActualHeight, "Compact Stop is outside the visible fixed footer.");
            Require(aboutBounds.Top >= actionsBounds.Top && aboutBounds.Bottom <= actionsBounds.Bottom, "Last utility action is not reachable through the compact footer scroll.");
            CancelOperation.Visibility = Visibility.Collapsed; StorageActions.ChangeView(null, 0, null, true);
            AppWindow.Resize(originalSize); await Task.Delay(100); Root.UpdateLayout();
            CancelOperation.Visibility = Visibility.Collapsed; Root.UpdateLayout();
            CancelOperation.Content = "Stop import"; CancelOperation.Visibility = Visibility.Visible; Root.UpdateLayout();
            Require(CancelOperation.ActualWidth > 0 && CancelOperation.ActualHeight > 0, "The newly visible Stop action has no clickable layout bounds.");
            CancelOperation.Visibility = Visibility.Collapsed; Root.UpdateLayout();
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
            await TestNativeFlyoutTextLayout();
            if (UiTestHost.Inspect) {
                if (UiTestHost.InspectCompletion) InitializeCompletionInspection();
                if (UiTestHost.InspectNotifications) InitializeNotificationInspection();
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
                trayFlyout.InitializeInspectionKeyboardProbe();
                if (UiTestHost.InspectThemes) InitializeThemeInspection();
                AppWindow.Hide();
                var area = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                trayFlyout.ShowAt(new System.Drawing.Point(area.Right - 140, area.Bottom - 20), FlyoutView());
                await Task.Delay(150);
                trayFlyout.WriteInspectionLayout();
                File.WriteAllText(Path.Combine(UiTestHost.Root, "ready.txt"), "UI fixture ready; write continue.txt to finish.");
                while (!File.Exists(Path.Combine(UiTestHost.Root, "continue.txt"))) await Task.Delay(250);
                Require(!closed, "UI fixture unexpectedly closed.");
                DefaultsPanel.TryLeave(); ProfilesPanel.TryLeave(); SettingsPanel.TryLeave(); ShowStorage();
            }
            quitItem.PerformClick(); Require(closed, "Idle tray Quit did not close.");
        }
        async Task TestCompletionSafeFocus() {
            foreach (bool sharing in new[] { false, true }) {
                var dialog = new KeepConnectedDialog { XamlRoot = Root.XamlRoot, Title = "Completion focus fixture",
                    Content = Scrolling(Wrapped("Generated fixture only. No device action.")), PrimaryButtonText = "Eject Now", IsPrimaryButtonEnabled = false };
                var share = new Microsoft.UI.Xaml.Controls.Button { Content = "Quick Share…" };
                if (sharing) {
                    var body = new Microsoft.UI.Xaml.Controls.StackPanel(); body.Children.Add(Wrapped("Generated fixture only.")); body.Children.Add(share);
                    dialog.Content = Scrolling(body);
                }
                var opened = new TaskCompletionSource<bool>(); dialog.Opened += (_, args) => opened.TrySetResult(true);
                var shown = dialog.ShowAsync();
                await Task.WhenAny(opened.Task, Task.Delay(5000));
                if (!opened.Task.IsCompleted) { dialog.Hide(); await shown; throw new InvalidOperationException("Native completion focus fixture did not open."); }
                await Task.Yield();
                await Task.Delay(500);
                var focus = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot) as Microsoft.UI.Xaml.Controls.Button;
                bool safe = dialog.SafeInitialFocus && focus != null && focus.Name == "CloseButton";
                if (sharing && safe) {
                    // A late framework/activation pass stays safe before navigation.
                    share.Focus(FocusState.Programmatic);
                    safe = (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot) as Microsoft.UI.Xaml.Controls.Button)?.Name == "CloseButton";
                    Require(share.Focus(FocusState.Keyboard), "Completion sharing is not keyboard focusable.");
                    Require(object.ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot), share), "Safe initial focus trapped deliberate keyboard navigation.");
                }
                dialog.Hide(); await shown;
                Require(safe, "Completion initial focus did not choose Keep Connected; sharing=" + sharing + "; focusReady=" + dialog.SafeInitialFocus + "; focus=" + (focus?.Content?.ToString() ?? "not a button") + "; " + dialog.FocusDiagnostics);
                Require(!sharing || share.IsEnabled && share.IsTabStop, "Safe initial focus removed keyboard access to Quick Share.");
            }
        }
        async Task TestPendingPickerFocus() {
            var list = new Microsoft.UI.Xaml.Controls.ListView {
                ItemsSource = new[] { "Generated pending choice. No device action." },
                SelectionMode = Microsoft.UI.Xaml.Controls.ListViewSelectionMode.Single, MaxHeight = 300 };
            var dialog = PendingImportsDialog(list);
            var opened = new TaskCompletionSource<bool>(); dialog.Opened += (_, args) => opened.TrySetResult(true);
            var shown = dialog.ShowAsync();
            await Task.WhenAny(opened.Task, Task.Delay(5000));
            if (!opened.Task.IsCompleted) { dialog.Hide(); await shown; throw new InvalidOperationException("Native pending picker did not open."); }
            await Task.Delay(500);
            var focus = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot) as Microsoft.UI.Xaml.Controls.Button;
            bool safe = focus != null && focus.Name == "CloseButton" && list.SelectedIndex == -1 && !dialog.IsPrimaryButtonEnabled;
            bool reachable = false;
            if (safe) {
                list.Focus(FocusState.Programmatic);
                safe = (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot) as Microsoft.UI.Xaml.Controls.Button)?.Name == "CloseButton" && list.SelectedIndex == -1;
                reachable = list.Focus(FocusState.Keyboard);
                list.SelectedIndex = 0;
                reachable &= list.SelectedIndex == 0;
            }
            dialog.Hide(); await shown;
            Require(safe, "Pending picker initial focus did not choose Close without selecting an import; focus=" + (focus?.Content?.ToString() ?? "not a button"));
            Require(reachable, "Pending picker prevented deliberate keyboard list selection.");
        }
        internal TrayFlyoutView FixtureFlyout() {
            var state = new Inventory { Epoch = 1 };
            for (int i = 0; i < (UiTestHost.InspectLong ? 18 : 1); i++) {
                string disk = "fixture-disk-" + i, target = "fixture-usb-" + i;
                state.Disks.Add(new Disk { Number = 700 + i, DevInst = (uint)(700 + i * 2), TargetDevInst = (uint)(701 + i * 2), InstanceId = disk, TargetId = target, Serial = "fixture-serial-" + i, Ancestors = new System.Collections.Generic.List<string> { disk, target } });
                state.Volumes.Add(new Volume { Id = "fixture-volume-" + i, Label = UiTestHost.InspectLong ? "Generated device " + (i + 1).ToString("D2") + " · long label for native wrapping and scrolling review" : "Camera · generated fixture", Serial = "fixture-" + i, Disks = new System.Collections.Generic.List<int> { 700 + i }, Mounts = new System.Collections.Generic.List<string> { "Z:\\" }, Group = TrayFlyoutModel.GroupOrder[i / 6] });
            }
            return TrayFlyoutModel.Build(new TrayFlyoutInput { State = state, Epoch = 1, MonitoringAvailable = true, SettingsLoaded = true, EditorOpen = EditorOpen, Work = UiBusy, SharingOptedIn = _ => true, Space = _ => new TrayFlyoutSpace(128000000000, 64000000000) });
        }
        // Explicit inspection only. Seed only a generated batch in this isolated
        // history, then manually open the production completion dialog. The source
        // has no native removal plan; destination folder opening retains real guards.
        void InitializeCompletionInspection() {
            InitializeReviewKeyboardProbe();
            // Register connection notifications only: no runtime pump, release
            // checks, workers, import, or removal plan. Never bypass destination guards.
            notifications.Start();
            Require(notifications.Available, "Completion inspection requires real storage notifications.");
            File.WriteAllText(Path.Combine(UiTestHost.Root, "completion-monitoring.txt"),
                "Read-only storage notifications registered. No runtime pump or hardware removal plan.\r\n");
            Func<bool, Task> reviewCompletion = async hasMedia => {
                if (StorageBusy) return;
                try {
                    var batches = await Task.Run(() => ImportHistory.Load());
                    string owned = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "EasyEjectBuildTools");
                    Require(batches.Count == 1 && batches[0].ProfileId.StartsWith("EasyEject-native-history-fixture-", StringComparison.Ordinal) &&
                        Path.GetFileName(Path.GetDirectoryName(batches[0].DestinationRoot)).StartsWith("Codex020-native-", StringComparison.Ordinal) &&
                        SafePaths.Within(batches[0].DestinationRoot, owned), "Completion inspection requires one owned generated history batch.");
                    var batch = batches[0]; string source = Path.Combine(UiTestHost.Root, "absent-completion-source");
                    Require(!Directory.Exists(source) && !File.Exists(source), "Completion inspection source must stay absent.");
                    var state = new Inventory { Epoch = 1 };
                    state.Disks.Add(new Disk { Number = 700, InstanceId = "generated-no-removal-plan" });
                    state.Volumes.Add(new Volume { Id = batch.ProfileId, Label = "Generated absent source", Serial = "fixture-only", Mounts = new List<string> { source }, Disks = new List<int> { 700 }, Group = StorageGroup.Camera });
                    var origin = new ImportCompletionOrigin(state, batch.ProfileId, source);
                    Require(origin.Plan == null, "Completion inspection must never grant a hardware removal plan.");
                    var entry = new ImportCompletion(origin, batch.DeviceName, batch.Folder, batch.DestinationRoot, batch.Destination,
                        hasMedia, hasMedia ? "Generated completion fixture only. No hardware removal plan. Two saved generated originals remain verified."
                            : "Generated no-media completion fixture only. No files imported and no hardware removal plan.", hasMedia ? batch : null);
                    completions.Insert(entry);
                    await ReviewImportCompletion(entry.Id);
                    File.AppendAllText(Path.Combine(UiTestHost.Root, "completion-inspection.txt"), Summary.Text + "\r\n");
                } catch (Exception ex) { Summary.Text = "Completion fixture stopped. " + ex.Message; }
            };
            var review = new Microsoft.UI.Xaml.Controls.Button { Content = "Test blocked completion" };
            review.Click += async (_, args) => await reviewCompletion(true);
            ((Microsoft.UI.Xaml.Controls.StackPanel)Navigation.PaneFooter).Children.Add(review);
            if (UiTestHost.InspectNotifications) {
                var empty = new Microsoft.UI.Xaml.Controls.Button { Content = "Test no-media completion" };
                empty.Click += async (_, args) => await reviewCompletion(false);
                ((Microsoft.UI.Xaml.Controls.StackPanel)Navigation.PaneFooter).Children.Add(empty);
            }
        }
    }
    public sealed partial class TrayFlyoutWindow {
        // Passive inspection-only evidence. Do not record text/other keys or handle input.
        internal void InitializeInspectionKeyboardProbe() {
            var trace = new StreamWriter(new FileStream(Path.Combine(UiTestHost.Root,
                "flyout-keyboard-" + Guid.NewGuid().ToString("N") + ".jsonl"),
                FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
            var observed = new HashSet<UIElement>();
            bool stopped = false;
            Action<string, bool?, string> record = (kind, handled, source) => {
                if (stopped) return;
                trace.WriteLine(
                System.Text.Json.JsonSerializer.Serialize(new { utc = DateTime.UtcNow, kind, handled, source,
                    visible = AppWindow.IsVisible, requestRunning }));
            };
            Action<UIElement> attach = element => {
                if (!observed.Add(element)) return;
                element.AddHandler(UIElement.PreviewKeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((sender, args) => {
                    if (args.Key == Windows.System.VirtualKey.Escape) record("PreviewEscape", args.Handled, args.OriginalSource?.GetType().Name);
                }), true);
                element.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((sender, args) => {
                    if (args.Key == Windows.System.VirtualKey.Escape) record("Escape", args.Handled, args.OriginalSource?.GetType().Name);
                }), true);
                record("Attached", null, element.GetType().Name);
            };
            attach(Surface);
            var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(250);
            timer.Tick += (_, args) => {
                if (stopped || Surface.XamlRoot == null) return;
                foreach (var popup in Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(Surface.XamlRoot))
                    if (popup.Child is UIElement child) attach(child);
            };
            Activated += (_, args) => record("Activation", null, args.WindowActivationState.ToString());
            Closed += (_, args) => { stopped = true; timer.Stop(); trace.Dispose(); };
            timer.Start();
        }
        internal void WriteInspectionLayout() {
            Surface.UpdateLayout();
            WriteInspectionTextLayout();
            var work = System.Windows.Forms.Screen.FromPoint(anchor).WorkingArea;
            var location = AppWindow.Position; var size = AppWindow.Size;
            File.WriteAllText(Path.Combine(UiTestHost.Root, "flyout-layout.json"), System.Text.Json.JsonSerializer.Serialize(new {
                generatedDevices = UiTestHost.InspectLong ? 18 : 1,
                scale = Surface.XamlRoot.RasterizationScale,
                width = Surface.ActualWidth, height = Surface.ActualHeight,
                scrollViewport = DeviceScroll.ViewportHeight, scrollableHeight = DeviceScroll.ScrollableHeight,
                footerTop = Open.TransformToVisual(Surface).TransformPoint(new Windows.Foundation.Point()).Y,
                footerBottom = Quit.TransformToVisual(Surface).TransformPoint(new Windows.Foundation.Point()).Y + Quit.ActualHeight,
                physicalX = location.X, physicalY = location.Y, physicalWidth = size.Width, physicalHeight = size.Height,
                workX = work.X, workY = work.Y, workWidth = work.Width, workHeight = work.Height }));
            double footerTop = Open.TransformToVisual(Surface).TransformPoint(new Windows.Foundation.Point()).Y;
            double footerBottom = Quit.TransformToVisual(Surface).TransformPoint(new Windows.Foundation.Point()).Y + Quit.ActualHeight;
            if (location.X < work.Left || location.Y < work.Top || location.X + size.Width > work.Right || location.Y + size.Height > work.Bottom ||
                DeviceScroll.ViewportHeight <= 0 || footerTop < 0 || footerBottom > Surface.ActualHeight + 1 ||
                UiTestHost.InspectLong && DeviceScroll.ScrollableHeight <= 0)
                throw new InvalidOperationException("Inspection flyout does not retain its device viewport/footer inside the monitor work area.");
            File.WriteAllText(Path.Combine(UiTestHost.Root, "flyout-layout-pass.txt"), "PASS native flyout device viewport/footer/work-area bounds; long-list scroll extent when requested.\r\n");
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
