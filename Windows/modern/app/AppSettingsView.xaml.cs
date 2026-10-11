using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EasyEject.Modern {
    public sealed partial class AppSettingsView : UserControl {
        AppSettingsEdit editor; StartupEdit login; string loginNote; bool active, loading, saving, confirming, updating;
        internal event Action<AppSettings> Saved;
        internal event Action<string> Completed;
        internal Func<string> ShortcutDescription;
        internal Func<string> ReleaseDescription;
        internal bool Busy { get { return loading || saving || confirming; } }
        internal bool Editing { get { return active; } }
        internal void QuitRefused(string message) { Status.Text = message; }
        public AppSettingsView() { InitializeComponent(); Letter.ItemsSource = GlobalHotkey.Letters; }
        internal async Task StartEdit() {
            if (Busy) return; loading = true; active = true; Render();
            try {
                var values = await Task.Run(() => {
                    var settings = AppSettings.Load(); StartupEdit draft = null; string note = null;
                    try { draft = new StartupEdit(new StartupRegistration(new WindowsStartupStore(), Environment.ProcessPath, modern: true)); }
                    catch (Exception ex) { note = "Login registration could not be checked. " + ex.Message; }
                    return Tuple.Create(settings, draft, note);
                });
                if (active) { editor = new AppSettingsEdit(values.Item1, AppSettings.Save); login = values.Item2; loginNote = values.Item3; }
                Status.Text = "Changes stay in this draft until Save. Cancel keeps saved settings and login registration.";
            }
            catch (Exception ex) { editor = null; Status.Text = "Settings could not be read. Existing records were retained. " + ex.Message; }
            finally { loading = false; Render(); }
        }
        internal bool TryLeave() { if (Busy) return false; active = false; if (editor != null) editor.Cancel(); if (login != null) login.Cancel(); editor = null; login = null; return true; }
        void Render() {
            updating = true;
            try {
                var p = editor == null ? new AppSettings() : editor.Snapshot();
                Notifications.IsChecked = p.NotificationsEnabled; Cleanup.IsChecked = p.CleanupCardsEnabled; WarnOther.IsChecked = p.WarnOtherEnabled;
                Debug.IsChecked = p.DebugEnabled; Shortcut.IsChecked = p.ShortcutEnabled; Letter.SelectedItem = p.ShortcutKey; Updates.IsChecked = p.AutomaticUpdatesEnabled;
                Login.IsChecked = login != null && login.Enabled; Login.IsEnabled = login != null && login.Available && !Busy;
                UseThisCopy.Visibility = login != null && login.CanRetarget ? Visibility.Visible : Visibility.Collapsed;
                UseThisCopy.IsEnabled = !Busy;
                LoginStatus.Text = (loginNote ?? (login == null ? "Login registration is not checked." : login.Snapshot.Message)) + "\n\nThis copy:\n" + Environment.ProcessPath;
                Scroll.IsEnabled = editor != null && editor.Open && !Busy; Letter.IsEnabled = p.ShortcutEnabled;
                Save.IsEnabled = editor != null && editor.Open && !Busy; Cancel.IsEnabled = !Busy;
                ShortcutStatus.Text = ShortcutDescription == null ? "Shortcut is off until saved and enabled." : ShortcutDescription();
                UpdateStatus.Text = (ReleaseDescription == null ? WindowsUpdateSafety.ChannelStatus : ReleaseDescription()) + " Open Windows updates & installation for manual review and instructions. Downloads and installation stay manual.";
            } finally { updating = false; }
        }
        void Choice_Changed(object sender, RoutedEventArgs args) {
            if (updating || Busy || editor == null || !editor.Open) return;
            var box = (CheckBox)sender; editor.Set((AppSettingChoice)Enum.Parse(typeof(AppSettingChoice), (string)box.Tag), box.IsChecked == true); Render();
        }
        void Letter_Changed(object sender, SelectionChangedEventArgs args) {
            if (updating || Busy || editor == null || !editor.Open || Letter.SelectedItem == null) return; editor.SetLetter((string)Letter.SelectedItem); Render();
        }
        async void Cleanup_Changed(object sender, RoutedEventArgs args) {
            if (updating || Busy || editor == null || !editor.Open) return;
            if (Cleanup.IsChecked != true) { editor.DisableCleanup(); Render(); return; }
            var owner = editor; var review = owner.ReviewCleanup(); confirming = true; Render();
            try {
                var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Enable metadata cleanup?", DefaultButton = ContentDialogButton.Close,
                    PrimaryButtonText = "Enable metadata cleanup", CloseButtonText = "Cancel", Content = new TextBlock { TextWrapping = TextWrapping.Wrap,
                        Text = "Enable permanent macOS metadata cleanup before manual camera/emulator card ejection?\n\nOnly ._*, .DS_Store, .apdisk and empty __MACOSX folders are removed. Photos, recordings and device Trash are kept. Each eject confirmation lists the cleanup roots. Keep backups. Saving never starts cleanup." } };
                var answer = await dialog.ShowAsync(); if (active && editor == owner) owner.AcceptCleanup(review, answer == ContentDialogResult.Primary);
            } catch (Exception ex) { owner.AcceptCleanup(review, false); Status.Text = "Cleanup was not enabled. " + ex.Message; }
            finally { confirming = false; Render(); }
        }
        async void Save_Click(object sender, RoutedEventArgs args) {
            if (Busy || editor == null || !editor.Open) return; var owner = editor; saving = true; Render(); Status.Text = "Saving reviewed settings…";
            AppSettings result = null; var loginOwner = login; string loginMessage = null; bool? loginChoice = loginOwner == null ? null : loginOwner.Change;
            try {
                var saved = await Task.Run(() => {
                    var settings = owner.Save(); string message = null;
                    if (loginOwner != null) {
                        try { message = loginOwner.SaveReviewed(loginChoice).Message; }
                        catch (Exception ex) { message = "Other application settings were saved, but login registration needs review. " + ex.Message; }
                    }
                    return Tuple.Create(settings, message);
                });
                result = saved.Item1; loginMessage = saved.Item2;
            }
            catch (Exception ex) { Status.Text = "Could not finish saving. Close and reopen Settings to review saved choices before retrying. " + ex.Message; }
            finally { saving = false; Render(); }
            if (result != null) {
                active = false; string message = "Application settings saved. No import, cleanup or device removal started." + (loginMessage == null ? "" : "\n" + loginMessage);
                try { if (Saved != null) Saved(result); }
                catch (Exception ex) { message += "\nSettings saved; runtime application needs review. " + ex.Message; }
                if (Completed != null) Completed(message);
            }
        }
        void Cancel_Click(object sender, RoutedEventArgs args) { if (TryLeave() && Completed != null) Completed("Settings draft discarded. Saved settings were not changed."); }
        void Login_Changed(object sender, RoutedEventArgs args) { if (updating || Busy || login == null || !login.Available) return; login.Set(Login.IsChecked == true); loginNote = "Login choice will be applied only when you Save settings."; Render(); }
        void UseThisCopy_Click(object sender, RoutedEventArgs args) { if (Busy || login == null || !login.CanRetarget) return; login.UseThisCopy(); loginNote = "This copy will replace the reviewed Easy Eject login command only when you Save settings."; Render(); }
        void Startup_Click(object sender, RoutedEventArgs args) {
            if (Busy) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
            catch (Exception ex) { Status.Text = "Windows Startup apps could not be opened. " + ex.Message; }
        }
    }
}
