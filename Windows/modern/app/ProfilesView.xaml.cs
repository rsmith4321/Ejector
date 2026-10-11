using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EasyEject.Modern {
    public sealed class ProfileCard {
        public string DisplayName { get; set; } public string ChoiceSummary { get; set; } public string TargetPath { get; set; }
        internal SavedProfile Entry;
    }
    public sealed partial class ProfilesView : UserControl {
        ImportProfileEdit editor; ImportDefaults defaults; bool busy, updating, active; int lifetime;
        Func<string, Task<string>> choose; IDiscovery discovery;
        internal bool Busy { get { return busy; } }
        internal bool Editing { get { return active && editor != null; } }
        internal void QuitRefused(string message) { Status.Text = message; }
        internal event Action<string> Changed;
        internal event Action<string> Completed;
        internal event Action RequestDefaults;
        internal event Func<SavedProfile, Action, Task> RequestImport;
        internal event Action ActivityChanged;
        public ProfilesView() { InitializeComponent(); }
        internal void Initialize(IDiscovery discovery, Func<string, Task<string>> choose) { this.discovery = discovery; this.choose = choose; }
        internal bool TryLeave() { if (busy || editor != null && !editor.Cancel()) return false; active = false; lifetime++; editor = null; return true; }
        internal async Task ReloadDefaults() {
            if (busy || editor == null || editor.Closed) return; busy = true; Render();
            try { var loaded = await Task.Run(() => ImportDefaults.Load()); editor.UpdateDefaults(loaded); defaults = loaded; Status.Text = "Profile draft retained. Shared defaults were reloaded; custom settings and sharing choices are independent."; }
            catch (Exception ex) { editor.Cancel(); Status.Text = "Shared defaults could not be reloaded. Cancel and reopen the profile before saving. " + ex.Message; }
            finally { busy = false; Render(); }
        }
        internal async Task StartCatalog(string selectedVolume = null) {
            if (busy) return; if (editor != null) editor.Cancel(); editor = null; active = true;
            int generation = ++lifetime; busy = true; Render(); Status.Text = "Reading saved profiles…";
            try {
                var loaded = await Task.Run(() => Tuple.Create(Profiles.Catalog(), ImportDefaults.Load()));
                if (!active || lifetime != generation) return; defaults = loaded.Item2;
                Records.SelectedItem = null; Records.ItemsSource = loaded.Item1.Select(entry => new ProfileCard { Entry = entry, DisplayName = entry.Display,
                    ChoiceSummary = entry.CanEdit ? (entry.Profile.FollowsDefaults ? "Uses defaults" : "Custom") + " · " + (entry.Profile.AutomaticEnabled(defaults) ? "Automatic" : "Manual") : "Needs review",
                    TargetPath = entry.CanEdit ? entry.Profile.Destination : "Saved record retained" }).ToArray();
                if (selectedVolume != null) Records.SelectedItem = Records.Items.Cast<ProfileCard>().SingleOrDefault(card => card.Entry.CanEdit && card.Entry.Profile.VolumeId == selectedVolume);
                Status.Text = selectedVolume == null ? "Saving or forgetting preferences never starts an import. Import now opens a separate review of the original connected device."
                    : "Profile saved. Review its saved choices below; Import now opens a separate manual import review."; UpdateDetail();
            } catch (Exception ex) { if (active && generation == lifetime) { Records.ItemsSource = null; Detail.Text = ex.Message; Status.Text = "Saved profiles could not be read. Existing records were retained."; } }
            finally { if (generation == lifetime) { busy = false; Render(); } }
        }
        internal async Task StartConnected(AutomaticConnection connection) {
            if (busy) return; if (editor != null) editor.Cancel(); editor = null; active = true;
            int generation = ++lifetime; busy = true; Render(); Status.Text = "Reviewing this connected device's saved profile…";
            try {
                var loaded = await Task.Run(() => {
                    connection.RequireCurrent(discovery.Scan());
                    var review = Profiles.ReviewConnected(connection.Volume, connection.Disk);
                    var choices = ImportDefaults.Load(); connection.RequireCurrent(discovery.Scan()); return Tuple.Create(review, choices);
                });
                if (!active || generation != lifetime) return; defaults = loaded.Item2; var reviewed = loaded.Item1;
                var initial = reviewed.Value ?? defaults.NewProfile(connection.Volume, connection.Disk);
                editor = new ImportProfileEdit(initial, defaults, connection.Root,
                    (value, chosen) => Profiles.SaveConnectedSelection(reviewed, value, connection.Volume, connection.Disk, connection.Epoch, connection.Root, discovery, chosen),
                    reviewed.Value == null ? (Action)null : () => { connection.RequireCurrent(discovery.Scan()); Profiles.ForgetConnected(reviewed); });
                Status.Text = reviewed.Error == null ? "Review folders and choices, then explicitly save. No import starts." : "Existing profile needs setup again and is retained until an explicit save. " + reviewed.Error;
            } catch (Exception ex) { if (active && generation == lifetime) Status.Text = "Device profile could not be reviewed. Refresh before retrying. " + ex.Message; }
            finally { if (generation == lifetime) { busy = false; Render(); } }
        }
        async Task EditSelected() {
            var card = Records.SelectedItem as ProfileCard; if (busy || card == null || !card.Entry.CanEdit) return;
            busy = true; Render(); var entry = card.Entry;
            try {
                defaults = await Task.Run(() => ImportDefaults.Load());
                editor = new ImportProfileEdit(entry.Profile, defaults, null, (value, chosen) => {
                    if (chosen) OfflineProfileDestination.SaveSelected(entry, value, discovery, Profiles.Root); else Profiles.SaveOffline(entry, value);
                }, () => Profiles.ForgetOffline(entry));
                Status.Text = "Edit saved choices. Connect the original device to change its media folder or Mac owner.";
            } catch (Exception ex) { Status.Text = "Profile could not be opened. Refresh before retrying. " + ex.Message; }
            finally { busy = false; Render(); }
        }
        void Render() {
            updating = true;
            try {
                bool editing = editor != null; Catalog.Visibility = CatalogButtons.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
                EditorBody.Visibility = EditButtons.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
                Heading.Text = editing ? "Import profile" : "Saved profiles"; EditorBody.IsEnabled = editing && !busy && !editor.Closed;
                Records.IsEnabled = Refresh.IsEnabled = active && !busy; var selected = Records.SelectedItem as ProfileCard;
                Edit.IsEnabled = Forget.IsEnabled = !busy && selected != null && selected.Entry.CanEdit;
                ImportNow.IsEnabled = active && !busy && !editing && selected != null && selected.Entry.CanEdit && RequestImport != null;
                Save.IsEnabled = editing && !busy && !editor.Closed; Cancel.IsEnabled = !busy; ForgetDraft.IsEnabled = Save.IsEnabled && editor.CanForget;
                if (!editing) return;
                var p = editor.Snapshot(); var options = editor.Options;
                DeviceName.Text = p.Name ?? ""; Source.Text = editor.SourceDisplay; Destination.Text = p.Destination ?? "";
                ChooseSource.IsEnabled = ChooseOwner.IsEnabled = editor.Connected; Mode.SelectedIndex = p.FollowsDefaults ? 0 : 1; CustomChoices.IsEnabled = !p.FollowsDefaults;
                Videos.IsChecked = options.VideosOnly; Previews.IsChecked = options.IncludePreviews; Clean.IsChecked = options.CleanLayout;
                Automatic.IsChecked = editor.Automatic; AskEject.IsChecked = options.AskToEject; Remove.IsChecked = options.DeleteOriginals; Recover.IsChecked = options.RecoverTrash;
                OwnerScope.Text = DeviceTrashChoice.Scope(p.MacTrashUid); Offer.IsChecked = QuickShareProfile.Enabled(p);
                var camera = QuickShareProfile.Camera(p); ShareCamera.SelectedIndex = camera == QuickShareCamera.DjiO4Pro ? 1 : 0;
                ShareSize.SelectedIndex = QuickShareProfile.Preset(p) == QuickSharePreset.Uhd ? 1 : 0; Lut.IsChecked = QuickShareProfile.Color(p) != QuickShareColor.Standard; Lut.Content = "Prefer official " + QuickShareMedia.LutLabel(camera) + " LUT in the export prompt";
                SharingCameraNote.Text = camera == QuickShareCamera.DjiO4Pro ? "DJI O4 Pro originals are stabilized in Gyroflow first. " + QuickShareProfile.GyroflowGuidance + " Only _stabilized.mp4 exports beside verified DJI O4 Pro originals in a batch are eligible." : "Only supported Insta360 Luna VID MP4 recordings are eligible.";
                SharingChoices.IsEnabled = Offer.IsChecked == true;
                Identity.Text = "Saved volume: " + p.VolumeId + "\nVolume serial: " + p.VolumeSerial + "\nPhysical disk: " + p.DiskInstance + "\nDestination identity: " + (p.SavedDestination == null ? "Choose the destination again to verify its disk before automatic import." : p.SavedDestination.VolumeId + " / " + p.SavedDestination.DiskInstance) + "\nFormatting requires setup again. Every import rechecks the original device and destination.";
            } finally { updating = false; }
        }
        void UpdateDetail() {
            var card = Records.SelectedItem as ProfileCard;
            if (card == null) { Detail.Text = Records.Items.Count == 0 ? "No saved devices. Connect a device to create its import profile." : "Select a profile to inspect saved folders, choices and device identity. This list grants no import or eject authority."; return; }
            var entry = card.Entry;
            if (!entry.CanEdit) { Detail.Text = "Record retained: " + entry.FileName + "\n\n" + entry.Error + "\n\nConnect the original device and set up its profile again. Identity is never guessed."; return; }
            var p = entry.Profile; var choices = p.Resolve(defaults);
            Detail.Text = entry.Display + "\nMedia folder: " + (String.IsNullOrEmpty(p.RelativeSource) ? "Whole device" : p.RelativeSource) + "\nDestination: " + p.Destination + "\n\n" + (choices.VideosOnly ? "Videos only" : "All media") + " · " + (choices.IncludePreviews ? "Includes previews" : "Skips optional previews") + " · " + (choices.CleanLayout ? "Clean layout" : "Preserved folders") + "\n" + (p.AutomaticEnabled(defaults) ? "Automatic on next connection and launch" : "Manual import only") + "\n" + (choices.AskToEject ? "Asks to eject after import" : "No post-import eject prompt") + "\n" + OriginalRemovalChoice.BeforeImport(choices) + "\nTrash recovery: " + (choices.RecoverTrash ? "On" : "Off") + "\n" + DeviceTrashChoice.Scope(p.MacTrashUid) + "\nManual Quick Share: " + (QuickShareProfile.Enabled(p) ? QuickShareMedia.CameraName(QuickShareProfile.Camera(p)) + " / " + p.SharingPreset + " / " + QuickShareProfile.Color(p) : "Off") + "\n\nSaved volume: " + p.VolumeId + "\nVolume serial: " + p.VolumeSerial + "\nPhysical disk: " + p.DiskInstance;
        }
        void Records_SelectionChanged(object sender, SelectionChangedEventArgs args) { if (Records == null || busy) return; UpdateDetail(); Render(); }
        async void Refresh_Click(object sender, RoutedEventArgs args) { await StartCatalog(); }
        async void Edit_Click(object sender, RoutedEventArgs args) { await EditSelected(); }
        async void ImportNow_Click(object sender, RoutedEventArgs args) {
            var selected = Records.SelectedItem as ProfileCard;
            if (!active || busy || editor != null || selected == null || !selected.Entry.CanEdit || RequestImport == null) return;
            bool transferred = false; int generation = lifetime;
            busy = true; Render(); if (ActivityChanged != null) ActivityChanged();
            try { await RequestImport(selected.Entry, () => {
                if (transferred || !active || generation != lifetime || !busy || editor != null) throw new SafetyException("The saved-profile import handoff expired. Choose Import now again.");
                // MainWindow already owns work/Stop/shutdown. Release this page's
                // busy scope so the existing completion/eject/share flow can run.
                transferred = true; busy = false; Render(); if (ActivityChanged != null) ActivityChanged();
            }); }
            catch (Exception ex) { if (!transferred && active && generation == lifetime) Status.Text = "Import review could not open. No import started. " + ex.Message; }
            finally { if (!transferred) { busy = false; Render(); if (ActivityChanged != null) ActivityChanged(); } }
        }
        void Defaults_Click(object sender, RoutedEventArgs args) { if (!busy && editor != null && !editor.Closed && RequestDefaults != null) RequestDefaults(); }
        void Name_Changed(object sender, TextChangedEventArgs args) { if (!updating && !busy && editor != null) editor.SetName(DeviceName.Text.Trim()); }
        void Mode_Changed(object sender, SelectionChangedEventArgs args) { if (!updating && !busy && editor != null) { editor.SetFollowing(Mode.SelectedIndex == 0); Render(); } }
        void Choice_Changed(object sender, RoutedEventArgs args) { if (!updating && !busy && editor != null) { var box = (CheckBox)sender; editor.SetChoice((ImportDefaultChoice)Enum.Parse(typeof(ImportDefaultChoice), (string)box.Tag), box.IsChecked == true); Render(); } }
        void Sharing_Changed(object sender, RoutedEventArgs args) { SaveSharing(); }
        void ShareSize_Changed(object sender, SelectionChangedEventArgs args) { SaveSharing(); }
        void SaveSharing() { if (!updating && !busy && editor != null) { editor.SetSharing(Offer.IsChecked == true, ShareSize.SelectedIndex == 1, Lut.IsChecked == true, ShareCamera.SelectedIndex == 1 ? QuickShareCamera.DjiO4Pro : QuickShareCamera.Luna); Render(); } }
        void ClearOwner_Click(object sender, RoutedEventArgs args) { if (!busy && editor != null) { editor.ClearMacOwner(); Render(); } }
        async Task<bool> Confirm(string title, string message, string action) {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, PrimaryButtonText = action, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
                Content = new ScrollViewer { MaxHeight = 360, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap } } };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        async void Removal_Changed(object sender, RoutedEventArgs args) {
            if (updating || busy || editor == null) return; var box = (CheckBox)sender; bool recovery = (string)box.Tag == "recover";
            if (box.IsChecked != true) { editor.DisableRemoval(recovery); Render(); return; }
            var owner = editor; var review = owner.BeginReview(recovery ? ProfileReviewKind.RecoverTrash : ProfileReviewKind.RemoveOriginals); busy = true; Render();
            try { bool accepted = await Confirm(recovery ? "Enable device Trash recovery?" : "Enable permanent original removal?",
                    "This choice applies only to this device's custom profile.\n\n" + (recovery ? DeviceTrashChoice.Scope(owner.Snapshot().MacTrashUid) + "\n\n" + DeviceTrashChoice.Warning : (review.VideosOnly ? "Videos only is selected; photos and excluded companions are retained." : "All media is selected; recognized imported photos and audio are eligible too.") + "\n\n" + OriginalRemovalChoice.Warning) + "\n\nSaving starts no work.", recovery ? "Enable verified recovery" : "Enable permanent removal");
                if (active && owner == editor) owner.Accept(review, accepted);
            } catch (Exception ex) { owner.Accept(review, false); Status.Text = "Choice was not enabled. " + ex.Message; }
            finally { busy = false; Render(); }
        }
        async void Choose_Click(object sender, RoutedEventArgs args) {
            if (busy || editor == null || choose == null) return; var owner = editor; string kind = (string)((Button)sender).Tag; busy = true; Render();
            try {
                string path = await choose(kind); if (path == null || !active || owner != editor) return;
                if (kind == "source") owner.SetSource(path); else if (kind == "destination") owner.SetDestination(path);
                else { var review = owner.BeginReview(ProfileReviewKind.MacOwner, path); bool accepted = await Confirm("Select Mac Trash owner", "Confirm this is YOUR numeric owner folder on this device:\n" + path + "\n\nOnly this owner is added to the current Windows user's scope when recovery is enabled. This choice does not enable recovery or start work.", "Use this owner"); owner.Accept(review, accepted); }
            } catch (Exception ex) { Status.Text = "Folder choice was not applied. " + ex.Message; }
            finally { busy = false; Render(); }
        }
        async void Save_Click(object sender, RoutedEventArgs args) {
            if (busy || editor == null || editor.Closed) return; var owner = editor; string savedVolume = owner.Snapshot().VolumeId; busy = true; Render(); bool saved = false;
            try { await Task.Run(() => owner.Save()); saved = true; }
            catch (Exception ex) { owner.Cancel(); Status.Text = "Saving could not finish. Cancel and reopen the profile to review its saved state before retrying. " + ex.Message; }
            finally { busy = false; Render(); }
            if (saved) { if (Changed != null) Changed("Import profile saved. No import started."); await StartCatalog(savedVolume); }
        }
        async void Forget_Click(object sender, RoutedEventArgs args) {
            if (busy) return; if (editor == null) await EditSelected(); if (editor == null || editor.Closed || !editor.CanForget) return;
            var owner = editor; var review = owner.BeginReview(ProfileReviewKind.Forget); busy = true; Render(); bool forgotten = false;
            try {
                bool accepted = await Confirm("Forget saved profile", "Forget " + owner.Snapshot().Name + " and its saved folders/import choices?\n\nOriginals, saved copies, import history and shared defaults stay intact. The original device must be enrolled again to import.", "Forget profile");
                forgotten = await Task.Run(() => owner.Forget(review, accepted));
            } catch (Exception ex) { owner.Cancel(); Status.Text = "Profile could not be forgotten. Cancel and refresh before retrying. " + ex.Message; }
            finally { busy = false; Render(); }
            if (forgotten) { if (Changed != null) Changed("Import profile forgotten. Media and history were retained."); await StartCatalog(); }
        }
        void Cancel_Click(object sender, RoutedEventArgs args) { if (TryLeave() && Completed != null) Completed("Profile draft discarded. No import started."); }
    }
}
