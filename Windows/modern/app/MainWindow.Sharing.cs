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
        static ScrollViewer Scrolling(StackPanel body) { return new ScrollViewer { Content = body, MaxHeight = 420, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; }
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
            string verified = null;
            try {
                var batches = await Task.Run(() => ImportHistory.Load()); SavedProfile[] profiles = null; string profileWarning = "";
                try { profiles = await Task.Run(() => Profiles.Catalog()); }
                catch (Exception ex) { profileWarning = "Sharing choices could not be read. Saved-file verification remains available. " + ex.Message; }
                var list = new ListView { MaxHeight = 180, SelectionMode = ListViewSelectionMode.Single,
                    ItemsSource = batches.Select(b => DateTime.Parse(b.CompletedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime().ToString("g") + " · " + b.DeviceName + " · " + b.Originals.Count + " files").ToArray() };
                var details = Wrapped(batches.Count == 0 ? "No completed media batches yet. Index-only and failed imports create no sharing history." : "Select a batch. The source device is not needed to verify these saved originals.");
                var body = new StackPanel { Spacing = 12 };
                body.Children.Add(Wrapped("The last 20 exact verified batches survive source eject and restart. Verification checks the saved destination, receipt and every recorded file hash; earlier folders are never scanned."));
                if (profileWarning.Length != 0) body.Children.Add(Wrapped(profileWarning)); body.Children.Add(list); body.Children.Add(details);
                confirmation = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Verified import history", Content = Scrolling(body),
                    PrimaryButtonText = "Verify saved originals", SecondaryButtonText = "", CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false };
                var dialog = confirmation;
                list.SelectionChanged += delegate {
                    var batch = list.SelectedIndex < 0 ? null : batches[list.SelectedIndex]; dialog.IsPrimaryButtonEnabled = batch != null;
                    bool share = profiles != null && ManualQuickShareService.EligibleProfile(batch, profiles) != null;
                    dialog.SecondaryButtonText = share ? "Quick Share…" : ""; dialog.IsSecondaryButtonEnabled = share;
                    if (batch != null) details.Text = batch.DeviceName + "\n" + batch.Folder + "\nReceipt: " + batch.ReceiptPath + "\n\n"
                        + String.Join("\n", batch.Originals.Take(100).Select(file => SafePaths.Relative(file.Path, batch.Folder) + " (" + file.Bytes + " bytes)"))
                        + (batch.Originals.Count > 100 ? "\n… " + (batch.Originals.Count - 100) + " more recorded files; verification checks all." : "");
                };
                var answer = await dialog.ShowAsync(); confirmation = null;
                if (list.SelectedIndex < 0 || answer == ContentDialogResult.None) return;
                var selected = ImportHistory.Clone(batches[list.SelectedIndex]);
                if (answer == ContentDialogResult.Secondary) { await PerformQuickShare(selected); return; }
                operationCancel = new CancellationTokenSource(); ImportProgress.Visibility = Visibility.Visible; ImportProgress.IsIndeterminate = true;
                var token = activity.Begin(TrayOperationKind.Verify, selected.DeviceName, "Checking saved files"); UpdateActions();
                await Task.Run(() => ImportHistory.Verify(selected, selected.Destination,
                    batch => new WindowsDestinationGuard(discovery, batch.DestinationRoot, batch.Destination), operations, operationCancel.Token, message => QueueImportProgress(token, message)));
                verified = "Verified all " + selected.Originals.Count + " exact saved files. Originals unchanged.\n" + selected.Folder; Summary.Text = verified;
                operationCancel.Dispose(); operationCancel = null; activity.End(Summary.Text); UpdateActions();
                confirmation = MessageDialog("Saved batch verified", Summary.Text, null); await confirmation.ShowAsync(); confirmation = null;
            } catch (OperationCanceledException) { Summary.Text = "Saved-file verification cancelled. Files unchanged."; }
            catch (Exception ex) { Summary.Text = verified == null ? "History review stopped. Saved originals remain unchanged. " + ex.Message : verified + "\nCompletion display unavailable: " + ex.Message; }
            finally { FinishSavedReview(); }
        }
        async Task<bool> PerformQuickShare(CompletedBatch expected) {
            QuickShareResult terminal = null; ManualQuickShareRequest request = null; ManualQuickShareReview review = null; string success = null;
            try {
                var token = activity.Begin(TrayOperationKind.Share, expected.DeviceName, "Reviewing saved batch and sharing choices");
                var offer = await Task.Run(() => SharingService.Prepare(expected)); review = new ManualQuickShareReview(offer);
                var size = new ComboBox { ItemsSource = new[] { "1080p · smaller files", "4K · more detail" }, SelectedIndex = review.Preset == QuickSharePreset.Uhd ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
                var lut = new CheckBox { Content = "Apply official Luna I-Log LUT", IsChecked = review.Color == QuickShareColor.LunaILog };
                var body = new StackPanel { Spacing = 16 };
                body.Children.Add(Wrapped(offer.Batch.DeviceName + "\n\nCreate sharing copies from this exact verified batch:\n" + offer.Batch.Folder));
                body.Children.Add(Wrapped("Choose size for this export")); body.Children.Add(size); body.Children.Add(lut);
                body.Children.Add(Wrapped("Use the LUT only for Luna I-Log recordings. Leave it off for normal-color footage; color tags do not identify the recording mode.\n\nFraming and audio are preserved without enlarging the video, at most 30 fps. Approximate sizes: 60 MB/min at 1080p; 180 MB/min at 4K.\n\nOutputs are probed and fully decoded before publication under Sharing Copies. Existing verified exports can be reused. Full-quality originals stay unchanged. This action never ejects. Tools and the official LUT must be supplied in Quick Share tools."));
                confirmation = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Create sharing copies", Content = Scrolling(body), PrimaryButtonText = "Create sharing copies", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
                UpdateActions(); var answer = await confirmation.ShowAsync(); confirmation = null;
                if (answer != ContentDialogResult.Primary) { review.Cancel(); Summary.Text = "Quick Share cancelled. No export started; full-quality imported originals are kept."; return false; }
                review.Set(size.SelectedIndex == 1 ? QuickSharePreset.Uhd : QuickSharePreset.Hd, lut.IsChecked == true ? QuickShareColor.LunaILog : QuickShareColor.Standard); request = review.Confirm();
                operationCancel = new CancellationTokenSource(); ImportProgress.Visibility = Visibility.Visible; ImportProgress.IsIndeterminate = true;
                Summary.Text = "Creating sharing copies from exact verified saved originals…"; UpdateActions();
                terminal = await Task.Run(() => SharingService.Run(request, operationCancel.Token, message => QueueImportProgress(token, message)));
                success = QuickSharePresentation.Success(request.Batch, request.Preset, terminal); Summary.Text = success; StopSavedCancellation();
                NotifyImport(terminal.CleanupWarnings.Count == 0 ? "Sharing copies ready" : "Sharing copies ready; needs attention", success, terminal.CleanupWarnings.Count != 0);
                if (AppWindow.IsVisible) { confirmation = MessageDialog("Quick Share", success, null); await confirmation.ShowAsync(); confirmation = null; }
                return true;
            } catch (QuickShareFailure ex) {
                StopSavedCancellation(); Summary.Text = ex.Message;
                NotifyImport(ex.Cancelled ? "Quick Share cancelled" : "Quick Share needs attention", ex.Message, !ex.Cancelled || QuickSharePresentation.CancelNeedsAttention(ex));
            } catch (OperationCanceledException) {
                StopSavedCancellation(); Summary.Text = terminal == null ? "Quick Share cancelled before export. Saved originals and earlier sharing copies are kept. Retry is manual." : success ?? QuickSharePresentation.Success(request.Batch, request.Preset, terminal);
            } catch (Exception ex) {
                StopSavedCancellation(); Summary.Text = terminal == null ? "Quick Share stopped. Saved originals remain unchanged. Retry is manual. " + ex.Message
                    : (success ?? QuickSharePresentation.Success(request.Batch, request.Preset, terminal)) + "\nCompletion display unavailable: " + ex.Message;
            } finally { if (review != null) review.Cancel(); StopSavedCancellation(); activity.End(Summary.Text); UpdateImportTray(); ImportStatus.Text = Summary.Text; ImportProgress.Visibility = Visibility.Collapsed; UpdateActions(); }
            return terminal != null;
        }
        void StopSavedCancellation() {
            if (operationCancel != null) { operationCancel.Dispose(); operationCancel = null; }
            activity.End(Summary.Text); UpdateImportTray(); UpdateActions();
        }
        async void OpenQuickShareTools() {
            if (!BeginSavedReview()) return;
            try {
                var snapshot = await Task.Run(() => QuickShareConfiguration.Load()); var value = snapshot.Value;
                var body = new StackPanel { Spacing = 12 };
                body.Children.Add(Wrapped("Choose FFmpeg and FFprobe executables you supplied. Saving checks their paths and hashes without running them. Changed tools require a fresh selection. Easy Eject does not download or install tools; export is a separate manual action."));
                var ffmpeg = new TextBox { Text = value.Ffmpeg == null ? "" : value.Ffmpeg.Path, IsReadOnly = true };
                var ffprobe = new TextBox { Text = value.Ffprobe == null ? "" : value.Ffprobe.Path, IsReadOnly = true };
                var lut = new TextBox { Text = value.Lut == null ? "" : value.Lut.Path, IsReadOnly = true };
                var status = Wrapped("Cancel keeps previous setup. No export starts here."); bool picking = false, failed = false; string saved = null;
                var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Quick Share tools", PrimaryButtonText = "Save setup", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
                var scroll = Scrolling(body);
                Action<string, TextBox, string> addFile = (label, field, extension) => {
                    body.Children.Add(Wrapped(label)); body.Children.Add(field);
                    var choose = new Button { Content = "Choose…" }; body.Children.Add(choose);
                    choose.Click += async delegate {
                        if (picking || quickShareSetupSaving) return; picking = true; scroll.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false;
                        try {
                            var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id) { CommitButtonText = "Choose " + label };
                            picker.FileTypeFilter.Add(extension); var result = await picker.PickSingleFileAsync();
                            if (result != null) { field.Text = SafePaths.Normalize(result.Path); status.Text = "Selection changed. Save validates it; Cancel keeps the previous setup."; }
                        } catch (Exception ex) { status.Text = "File selection unavailable. " + ex.Message; }
                        finally { picking = false; scroll.IsEnabled = !failed; dialog.IsPrimaryButtonEnabled = !failed; }
                    };
                };
                addFile("FFmpeg executable", ffmpeg, ".exe"); addFile("FFprobe executable", ffprobe, ".exe");
                body.Children.Add(Wrapped("Official Luna I-Log LUT is optional for normal-color exports. Choose " + QuickShareMedia.OfficialLutName + " from your official Insta360 download. Save checks the exact official hash and retains a local copy. Your downloaded original stays intact; clearing the choice deletes neither copy."));
                addFile("Official LUT", lut, ".cube");
                var clearTools = new Button { Content = "Clear tool selections" }; clearTools.Click += delegate { ffmpeg.Text = ffprobe.Text = ""; };
                var clearLut = new Button { Content = "Clear LUT selection" }; clearLut.Click += delegate { lut.Text = ""; };
                body.Children.Add(clearTools); body.Children.Add(clearLut); body.Children.Add(status); dialog.Content = scroll;
                dialog.CloseButtonClick += delegate(ContentDialog sender, ContentDialogButtonClickEventArgs args) {
                    if (picking) { args.Cancel = true; return; }
                    if (quickShareSetupSaving) { args.Cancel = true; RequestOperationStop(); status.Text = "Stopping validation. Waiting for the current file check/write…"; }
                };
                dialog.PrimaryButtonClick += async delegate(ContentDialog sender, ContentDialogButtonClickEventArgs args) {
                    if (picking || quickShareSetupSaving || failed) { args.Cancel = true; return; }
                    var deferral = args.GetDeferral(); args.Cancel = true;
                    string selectedFfmpeg = ffmpeg.Text, selectedFfprobe = ffprobe.Text, selectedLut = lut.Text;
                    quickShareSetupSaving = true; scroll.IsEnabled = false; dialog.IsPrimaryButtonEnabled = false; dialog.CloseButtonText = "Stop validation";
                    operationCancel = new CancellationTokenSource(); status.Text = "Validating supplied files and saving setup. No tools are running…"; UpdateActions();
                    try {
                        await Task.Run(() => QuickShareConfiguration.Save(snapshot, selectedFfmpeg, selectedFfprobe, selectedLut, Profiles.Root, operationCancel.Token));
                        saved = "Quick Share setup saved. No export started."; args.Cancel = false;
                    } catch (OperationCanceledException) { failed = true; status.Text = "Setup validation cancelled. Close and reopen to review saved choices. An already copied local LUT is retained."; }
                    catch (Exception ex) { failed = true; status.Text = "Setup was not confirmed. Close and reopen to review saved choices before retrying. " + ex.Message; }
                    finally {
                        operationCancel.Dispose(); operationCancel = null; quickShareSetupSaving = false; scroll.IsEnabled = !failed; dialog.CloseButtonText = "Close"; UpdateActions(); deferral.Complete();
                    }
                };
                confirmation = dialog; UpdateActions(); await dialog.ShowAsync(); confirmation = null;
                Summary.Text = saved ?? (failed ? status.Text : "Quick Share setup draft discarded. Previous selections kept.");
            } catch (Exception ex) { Summary.Text = "Quick Share setup unavailable. " + ex.Message; }
            finally { FinishSavedReview(); }
        }
    }
}
