using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EasyEject.Modern {
    public sealed partial class MainWindow {
        ManualQuickShareService manualSharing;
        bool quickShareSetupSaving;
        void History_Click(object sender, RoutedEventArgs args) { ReviewHistory(); }
        void QuickShareTools_Click(object sender, RoutedEventArgs args) { OpenQuickShareTools(); }
        ManualQuickShareService SharingService {
            get { return manualSharing ?? (manualSharing = new ManualQuickShareService(Profiles.Root, operations, new WindowsQuickShareProcess(), batch => new WindowsDestinationGuard(discovery, batch.DestinationRoot, batch.Destination))); }
        }
        static TextBlock Wrapped(string value) { return new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }; }
        static ScrollViewer Scrolling(UIElement body) { return new ScrollViewer { Content = body, MaxHeight = 420, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; }
        bool BeginSavedReview() {
            if (closed || UiBusy || inventory.Scanning || !DefaultsPanel.TryLeave() || !ProfilesPanel.TryLeave() || !SettingsPanel.TryLeave()) return false;
            ShowStorage(); working = true; UpdateActions(); return true;
        }
        void FinishSavedReview() {
            if (operationCancel != null) { operationCancel.Dispose(); operationCancel = null; }
            quickShareSetupSaving = false; confirmation = null; working = false;
            ImportStatus.Text = Summary.Text; ImportProgress.Visibility = Visibility.Collapsed; activity.End(Summary.Text); UpdateImportTray(); UpdateActions();
        }
        async void ReviewHistory() {
            if (!BeginSavedReview()) return;
            string verified = null; TrayOperation token = null;
            try {
                await EnsureVisible();
                var batches = await Task.Run(() => ImportHistory.Load()); SavedProfile[] profiles = null; string profileWarning = "";
                try { profiles = await Task.Run(() => Profiles.Catalog()); }
                catch (Exception ex) { profileWarning = "Sharing choices could not be read. Saved-file verification remains available. " + ex.Message; RecordModernIssue(profileWarning); }
                var list = new ListView { MaxHeight = 180, SelectionMode = ListViewSelectionMode.Single,
                    ItemsSource = batches.Select(b => DateTime.Parse(b.CompletedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime().ToString("g") + " · " + b.DeviceName + " · " + b.Originals.Count + " files").ToArray() };
                var details = Wrapped(batches.Count == 0 ? "No completed media batches yet. Index-only and failed imports create no sharing history." : "Select a batch. The source device is not needed to verify these saved originals.");
                var body = new StackPanel { Spacing = 12 };
                body.Children.Add(Wrapped("The last 20 exact verified batches survive source eject and restart. Verification checks the saved destination, receipt and every recorded file hash; earlier folders are never scanned."));
                var openFolder = new Button { Content = "Open Folder", Visibility = Visibility.Collapsed }; bool openRequested = false;
                if (profileWarning.Length != 0) body.Children.Add(Wrapped(profileWarning)); body.Children.Add(list); body.Children.Add(openFolder); body.Children.Add(details);
                confirmation = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Verified import history", Content = Scrolling(body),
                    PrimaryButtonText = "Verify saved originals", SecondaryButtonText = "", CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false };
                var dialog = confirmation;
                bool selecting = true;
                SelectionChangedEventHandler selection = delegate {
                    if (!selecting || closed || !Object.ReferenceEquals(confirmation, dialog)) return;
                    var batch = list.SelectedIndex < 0 || list.SelectedIndex >= batches.Count ? null : batches[list.SelectedIndex]; dialog.IsPrimaryButtonEnabled = batch != null;
                    bool share = profiles != null && ManualQuickShareService.EligibleProfile(batch, profiles) != null;
                    dialog.SecondaryButtonText = share ? "Quick Share…" : ""; dialog.IsSecondaryButtonEnabled = share;
                    openFolder.Visibility = share && QuickShareProfile.GyroflowFirst(ManualQuickShareService.EligibleProfile(batch, profiles)) ? Visibility.Visible : Visibility.Collapsed;
                    if (batch != null) details.Text = batch.DeviceName + "\n" + batch.Folder + "\nReceipt: " + batch.ReceiptPath + "\n\n"
                        + String.Join("\n", batch.Originals.Take(100).Select(file => SafePaths.Relative(file.Path, batch.Folder) + " (" + file.Bytes + " bytes)"))
                        + (batch.Originals.Count > 100 ? "\n… " + (batch.Originals.Count - 100) + " more recorded files; verification checks all." : "");
                };
                RoutedEventHandler openChoice = delegate { if (!selecting || closed || !Object.ReferenceEquals(confirmation, dialog) || openFolder.Visibility != Visibility.Visible) return; openRequested = true; dialog.Hide(); };
                openFolder.Click += openChoice; list.SelectionChanged += selection; ContentDialogResult answer; int index;
                try { answer = await dialog.ShowAsync(); index = list.SelectedIndex; }
                finally { selecting = false; list.SelectionChanged -= selection; openFolder.Click -= openChoice; }
                confirmation = null;
                if (openRequested && index >= 0 && index < batches.Count) {
                    var folderBatch = ImportHistory.Clone(batches[index]);
                    await Task.Run(() => CompletionFolder.Open(folderBatch, batch => new WindowsDestinationGuard(discovery, batch.DestinationRoot, batch.Destination), operations,
                        folder => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + folder + "\"", UseShellExecute = false })));
                    Summary.Text = "Import folder opened. Stabilize O4 Pro clips in Gyroflow before using Quick Share. Apply the LUT in Gyroflow or Quick Share; recognized Gyroflow LUT exports keep their existing colors."; return;
                }
                if (index < 0 || index >= batches.Count || answer == ContentDialogResult.None) return;
                var selected = ImportHistory.Clone(batches[index]);
                if (answer == ContentDialogResult.Secondary) { await PerformQuickShare(selected); return; }
                operationCancel = new CancellationTokenSource(); ImportProgress.Visibility = Visibility.Visible; ImportProgress.IsIndeterminate = true;
                token = activity.Begin(TrayOperationKind.Verify, selected.DeviceName, "Checking saved files"); UpdateActions();
                await StartStorageTask(token, () => Task.Run(() => { ImportHistory.Verify(selected, selected.Destination,
                    batch => new WindowsDestinationGuard(discovery, batch.DestinationRoot, batch.Destination), operations, operationCancel.Token, message => QueueImportProgress(token, message), guard => QueueStorageProof(token, guard)); return true; }));
                verified = "Verified all " + selected.Originals.Count + " exact saved files. Originals unchanged.\n" + selected.Folder; Summary.Text = verified;
                await DrainStorageTask(token);
                operationCancel.Dispose(); operationCancel = null; activity.End(Summary.Text); UpdateActions();
                confirmation = MessageDialog("Saved batch verified", Summary.Text, null); await confirmation.ShowAsync(); confirmation = null;
            } catch (OperationCanceledException) { Summary.Text = "Saved-file verification cancelled. Files unchanged."; }
            catch (Exception ex) { Summary.Text = verified == null ? "History review stopped. Saved originals remain unchanged. " + ex.Message : verified + "\nCompletion display unavailable: " + ex.Message; RecordModernIssue(Summary.Text); }
            finally { await DrainStorageTask(token); FinishSavedReview(); }
        }
        async Task<bool> PerformQuickShare(CompletedBatch expected) {
            QuickShareResult terminal = null; ManualQuickShareRequest request = null; ManualQuickShareReview review = null; string success = null; TrayOperation token = null;
            try {
                await EnsureVisible();
                token = activity.Begin(TrayOperationKind.Share, expected.DeviceName, "Reviewing saved batch and sharing choices");
                var offer = await Task.Run(() => SharingService.Prepare(expected)); var camera = offer.Camera;
                string gyroflow = camera == QuickShareCamera.DjiO4Pro ? await Task.Run(() => QuickSharePresentation.GyroflowPending(offer.Batch)) : null;
                if (gyroflow != null) { Summary.Text = gyroflow + " No export started; full-quality imported originals are kept."; if (AppWindow.IsVisible) { confirmation = MessageDialog("Quick Share", Summary.Text, null); await confirmation.ShowAsync(); confirmation = null; } return false; }
                review = new ManualQuickShareReview(offer);
                var size = new ComboBox { ItemsSource = new[] { "1080p · smaller files", "4K · more detail" }, SelectedIndex = review.Preset == QuickSharePreset.Uhd ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
                var lut = new CheckBox { Content = "Apply official " + QuickShareMedia.LutLabel(camera) + " LUT", IsChecked = review.Color != QuickShareColor.Standard };
                var body = new StackPanel { Spacing = 16 };
                body.Children.Add(Wrapped(offer.Batch.DeviceName + "\n\nCreate sharing copies from this exact verified batch:\n" + offer.Batch.Folder));
                body.Children.Add(Wrapped("Choose size for this export")); body.Children.Add(size); body.Children.Add(lut);
                body.Children.Add(Wrapped((camera == QuickShareCamera.DjiO4Pro ? "Shares the Gyroflow _stabilized.mp4 exports beside the DJI O4 Pro originals. Use the LUT only for D-Log M footage. Gyroflow exports marked as having a LUT applied keep their existing colors; Quick Share only makes them smaller. For other already-colored exports, turn the LUT off."
                    : "Use the LUT only for Luna I-Log recordings. Leave it off for normal-color footage; color tags do not identify the recording mode.") + "\n\nFraming and audio are preserved without enlarging the video, at most 30 fps. Approximate sizes: 60 MB/min at 1080p; 180 MB/min at 4K.\n\nOutputs are probed and fully decoded before publication under Sharing Copies. Existing verified exports can be reused. Full-quality originals stay unchanged. This action never ejects. Tools and the official LUT must be supplied in Quick Share tools."));
                confirmation = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Create sharing copies", Content = Scrolling(body), PrimaryButtonText = "Create sharing copies", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
                UpdateActions(); var answer = await confirmation.ShowAsync(); confirmation = null;
                if (answer != ContentDialogResult.Primary) { review.Cancel(); Summary.Text = "Quick Share cancelled. No export started; full-quality imported originals are kept."; return false; }
                review.Set(size.SelectedIndex == 1 ? QuickSharePreset.Uhd : QuickSharePreset.Hd, lut.IsChecked == true ? QuickShareMedia.LogColor(camera) : QuickShareColor.Standard); request = review.Confirm();
                operationCancel = new CancellationTokenSource(); ImportProgress.Visibility = Visibility.Visible; ImportProgress.IsIndeterminate = true;
                Summary.Text = "Creating sharing copies from exact verified saved originals…"; UpdateActions();
                terminal = await StartStorageTask(token, () => Task.Run(() => SharingService.Run(request, operationCancel.Token, message => QueueImportProgress(token, message), guard => QueueStorageProof(token, guard))));
                success = QuickSharePresentation.Success(request.Batch, request.Preset, terminal, request.Camera); Summary.Text = success; await DrainStorageTask(token); StopSavedCancellation();
                NotifyImport(terminal.CleanupWarnings.Count == 0 ? "Sharing copies ready" : "Sharing copies ready; needs attention", success, terminal.CleanupWarnings.Count != 0, terminal.CleanupWarnings.Count == 0);
                if (AppWindow.IsVisible) { confirmation = MessageDialog("Quick Share", success, null); await confirmation.ShowAsync(); confirmation = null; }
                return true;
            } catch (QuickShareFailure ex) {
                StopSavedCancellation(); Summary.Text = ex.Message;
                NotifyImport(ex.Cancelled ? "Quick Share cancelled" : "Quick Share needs attention", ex.Message, !ex.Cancelled || QuickSharePresentation.CancelNeedsAttention(ex));
            } catch (OperationCanceledException) {
                StopSavedCancellation(); Summary.Text = terminal == null ? "Quick Share cancelled before export. Saved originals and earlier sharing copies are kept. Retry is manual." : success ?? QuickSharePresentation.Success(request.Batch, request.Preset, terminal, request.Camera);
            } catch (Exception ex) {
                StopSavedCancellation(); Summary.Text = terminal == null ? "Quick Share stopped. Saved originals remain unchanged. Retry is manual. " + ex.Message
                    : (success ?? QuickSharePresentation.Success(request.Batch, request.Preset, terminal, request.Camera)) + "\nCompletion display unavailable: " + ex.Message;
            } finally { await DrainStorageTask(token); if (review != null) review.Cancel(); StopSavedCancellation(); activity.End(Summary.Text); UpdateImportTray(); ImportStatus.Text = Summary.Text; ImportProgress.Visibility = Visibility.Collapsed; UpdateActions(); }
            return terminal != null;
        }
        void StopSavedCancellation() {
            if (operationCancel != null) { operationCancel.Dispose(); operationCancel = null; }
            activity.End(Summary.Text); UpdateImportTray(); UpdateActions();
        }
        async void OpenQuickShareTools() {
            if (!BeginSavedReview()) return;
            Task pendingPicker = null, pendingSave = null; bool reviewing = false;
            try {
                await EnsureVisible();
                var snapshot = await Task.Run(() => QuickShareConfiguration.Load()); var value = snapshot.Value;
                var body = new StackPanel { Spacing = 12 };
                body.Children.Add(Wrapped("Choose FFmpeg and FFprobe executables you supplied. Saving checks their paths and hashes without running them. Changed tools require a fresh selection. Easy Eject does not download or install tools; export is a separate manual action."));
                var ffmpeg = new TextBox { Text = value.Ffmpeg == null ? "" : value.Ffmpeg.Path, IsReadOnly = true };
                var ffprobe = new TextBox { Text = value.Ffprobe == null ? "" : value.Ffprobe.Path, IsReadOnly = true };
                var lut = new TextBox { Text = value.Lut == null ? "" : value.Lut.Path, IsReadOnly = true };
                var djiLut = new TextBox { Text = value.DjiLut == null ? "" : value.DjiLut.Path, IsReadOnly = true };
                var status = Wrapped("Cancel keeps previous setup. No export starts here."); bool picking = false, failed = false; string saved = null;
                var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Quick Share tools", PrimaryButtonText = "Save setup", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
                var fields = new ContentControl { Content = body, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                var scroll = Scrolling(fields);
                Func<bool> owns = () => reviewing && !closed && Object.ReferenceEquals(confirmation, dialog);
                Func<string, TextBox, string, Task> pickFile = async (label, field, extension) => {
                    picking = true; fields.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false;
                    try {
                        var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id) { CommitButtonText = "Choose " + label };
                        picker.FileTypeFilter.Add(extension); var result = await picker.PickSingleFileAsync();
                        if (owns() && result != null) { field.Text = SafePaths.Normalize(result.Path); status.Text = "Selection changed. Save validates it; Cancel keeps the previous setup."; }
                    } catch (Exception ex) { if (owns()) status.Text = "File selection unavailable. " + ex.Message; }
                    finally { picking = false; if (owns()) { fields.IsEnabled = !failed; dialog.IsPrimaryButtonEnabled = !failed; } }
                };
                Action<string, TextBox, string> addFile = (label, field, extension) => {
                    body.Children.Add(Wrapped(label)); body.Children.Add(field);
                    var choose = new Button { Content = "Choose…" }; body.Children.Add(choose);
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(field, label + " path");
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choose, "Choose " + label);
                    choose.Click += async delegate {
                        if (!owns() || picking || quickShareSetupSaving || failed) return;
                        pendingPicker = pickFile(label, field, extension); await pendingPicker;
                    };
                };
                addFile("FFmpeg executable", ffmpeg, ".exe"); addFile("FFprobe executable", ffprobe, ".exe");
                body.Children.Add(Wrapped("Official Luna I-Log LUT is optional for normal-color exports. Choose " + QuickShareMedia.OfficialLutName + " from your official Insta360 download. Save checks the exact official hash and retains a local copy. Your downloaded original stays intact; clearing the choice deletes neither copy."));
                addFile("Official LUT", lut, ".cube");
                body.Children.Add(Wrapped("Official DJI O4 Pro D-Log M LUT is optional. Choose the O4 Air Unit Series D-Log M to Rec.709 .cube from DJI's O4 download page. Save checks its exact official hash and retains a local copy, as above. Gyroflow exports that already carry Gyroflow's LUT marker do not need it."));
                addFile("Official DJI O4 LUT", djiLut, ".cube");
                var clearTools = new Button { Content = "Clear tool selections" }; clearTools.Click += delegate { if (owns() && !picking && !quickShareSetupSaving && !failed) ffmpeg.Text = ffprobe.Text = ""; };
                var clearLut = new Button { Content = "Clear LUT selection" }; clearLut.Click += delegate { if (owns() && !picking && !quickShareSetupSaving && !failed) lut.Text = ""; };
                var clearDjiLut = new Button { Content = "Clear DJI LUT selection" }; clearDjiLut.Click += delegate { if (owns() && !picking && !quickShareSetupSaving && !failed) djiLut.Text = ""; };
                body.Children.Add(clearTools); body.Children.Add(clearLut); body.Children.Add(clearDjiLut); body.Children.Add(status); dialog.Content = scroll;
                dialog.CloseButtonClick += delegate(ContentDialog sender, ContentDialogButtonClickEventArgs args) {
                    if (!owns()) { args.Cancel = true; return; }
                    if (picking) { args.Cancel = true; return; }
                    if (quickShareSetupSaving) { args.Cancel = true; RequestOperationStop(); status.Text = "Stopping validation. Waiting for the current file check/write…"; }
                };
                dialog.Closing += delegate(ContentDialog sender, ContentDialogClosingEventArgs args) {
                    if (owns() && (picking || quickShareSetupSaving)) args.Cancel = true;
                };
                dialog.PrimaryButtonClick += async delegate(ContentDialog sender, ContentDialogButtonClickEventArgs args) {
                    if (!owns() || picking || quickShareSetupSaving || failed) { args.Cancel = true; return; }
                    var deferral = args.GetDeferral(); args.Cancel = true;
                    string selectedFfmpeg = ffmpeg.Text, selectedFfprobe = ffprobe.Text, selectedLut = lut.Text, selectedDjiLut = djiLut.Text;
                    quickShareSetupSaving = true; fields.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false; dialog.CloseButtonText = "Stop validation";
                    var cancellation = new CancellationTokenSource(); operationCancel = cancellation;
                    status.Text = "Validating supplied files and saving setup. No tools are running…"; UpdateActions();
                    Func<Task> saveSetup = async () => {
                        try {
                            await Task.Run(() => QuickShareConfiguration.Save(snapshot, selectedFfmpeg, selectedFfprobe, selectedLut, Profiles.Root, cancellation.Token, null, selectedDjiLut));
                            saved = "Quick Share setup saved. No export started."; if (owns()) args.Cancel = false;
                        } catch (OperationCanceledException) { failed = true; if (owns()) status.Text = "Setup validation cancelled. Close and reopen to review saved choices. An already copied local LUT is retained."; }
                        catch (Exception ex) { failed = true; if (owns()) status.Text = "Setup was not confirmed. Close and reopen to review saved choices before retrying. " + ex.Message; }
                        finally {
                            if (Object.ReferenceEquals(operationCancel, cancellation)) operationCancel = null;
                            cancellation.Dispose(); quickShareSetupSaving = false;
                            // Keep the viewer scrollable so a failed or stopped Save's
                            // explanation remains readable while choices stay disabled.
                            if (owns()) { fields.IsEnabled = !failed; dialog.CloseButtonText = "Close"; UpdateActions(); }
                        }
                    };
                    try { pendingSave = saveSetup(); await pendingSave; }
                    finally { deferral.Complete(); }
                };
                confirmation = dialog; reviewing = true; UpdateActions(); await dialog.ShowAsync(); reviewing = false; confirmation = null;
                Summary.Text = saved ?? (failed ? status.Text : "Quick Share setup draft discarded. Previous selections kept.");
                if (failed) RecordModernIssue(Summary.Text);
            } catch (Exception ex) { Summary.Text = "Quick Share setup view unavailable. Reopen it to review saved choices before retrying. " + ex.Message; RecordModernIssue(Summary.Text); }
            finally {
                reviewing = false;
                // A failed XAML view cannot dispose cancellation or release busy/tray
                // ownership while its exact picker or file-validation worker remains.
                if (pendingPicker != null) try { await pendingPicker; } catch { }
                if (pendingSave != null) try { await pendingSave; } catch { }
                FinishSavedReview();
            }
        }
    }
}
