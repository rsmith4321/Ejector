using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EasyEject.Modern {
    public sealed partial class ImportDefaultsView : UserControl {
        ImportDefaultsEdit editor;
        bool updating, loading, saving, confirming, active;
        int loadRevision;
        internal bool Busy { get { return loading || saving || confirming; } }
        internal event Action<string> Completed;
        internal event Action Saved;
        public ImportDefaultsView() { InitializeComponent(); }
        internal async Task StartEdit() {
            if (Busy) return;
            if (editor != null) editor.Cancel();
            editor = null; active = true; loading = true; int revision = ++loadRevision; Render();
            Status.Text = "Reading saved choices…";
            try {
                var initial = await Task.Run(() => ImportDefaults.Load());
                if (active && revision == loadRevision) {
                    editor = new ImportDefaultsEdit(initial, ImportDefaults.Save);
                    Status.Text = "Changes stay in this draft until you choose Save defaults.";
                }
            } catch (Exception ex) { if (active && revision == loadRevision) Status.Text = "Saved choices could not be read. Nothing was changed. " + ex.Message; }
            finally { if (revision == loadRevision) { loading = false; Render(); } }
        }
        internal bool TryLeave() {
            if (Busy || editor != null && !editor.Cancel()) return false;
            active = false; loadRevision++; return true;
        }
        void Render() {
            updating = true;
            try {
                Options.IsEnabled = active && editor != null && !editor.Closed && !Busy;
                Save.IsEnabled = Options.IsEnabled; Cancel.IsEnabled = active && !Busy;
                if (editor == null) return;
                var view = editor.Snapshot(); var options = view.Snapshot();
                Videos.IsChecked = options.VideosOnly; Previews.IsChecked = options.IncludePreviews; Clean.IsChecked = options.CleanLayout;
                Automatic.IsChecked = view.AutomaticEnabled; AskEject.IsChecked = options.AskToEject;
                Remove.IsChecked = options.DeleteOriginals; Recover.IsChecked = options.RecoverTrash;
                OriginalsNote.Text = options.DeleteOriginals ? "Permanent removal is on. Recognized imported originals are removed only after complete saved-copy verification; this skips Trash." : "Keep originals is on (recommended). Verified full-quality copies are saved and originals stay on the device.";
            } finally { updating = false; }
        }
        void Choice_Changed(object sender, RoutedEventArgs args) {
            if (updating || editor == null || Busy) return;
            var box = (CheckBox)sender;
            editor.Set((ImportDefaultChoice)Enum.Parse(typeof(ImportDefaultChoice), (string)box.Tag), box.IsChecked == true);
            Render();
        }
        async void Removal_Changed(object sender, RoutedEventArgs args) {
            if (updating || editor == null || Busy) return;
            var box = (CheckBox)sender; bool recovery = (string)box.Tag == "recover";
            if (box.IsChecked != true) { editor.DisableRemoval(recovery); Render(); return; }
            var owner = editor; var review = owner.BeginRemovalReview(recovery); confirming = true; Render();
            string scope = "These shared defaults apply to EVERY device using defaults, including future profiles. Custom profiles keep their own choices.";
            string message = recovery
                ? scope + "\n\nRecovery uses the current Windows user only, plus any explicit Mac Trash owner saved in each device profile. Mac owners are never guessed.\n\n" + DeviceTrashChoice.Warning
                : scope + "\n\n" + (review.VideosOnly ? "Videos only is selected. Photos and excluded companions are retained." : "All media is selected. Recognized imported photos and audio are eligible too.") + "\n\n" + OriginalRemovalChoice.Warning + "\n\nSaving enables this choice for future imports; saving never starts an import or removes files.";
            try {
                var dialog = new ContentDialog {
                    XamlRoot = XamlRoot,
                    Title = recovery ? "Enable verified device Trash recovery?" : "Enable permanent original removal?",
                    PrimaryButtonText = recovery ? "Enable verified recovery" : "Enable permanent removal",
                    CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
                    Content = new ScrollViewer { MaxHeight = 360, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap } }
                };
                var result = await dialog.ShowAsync();
                if (active && owner == editor) owner.AcceptRemoval(review, result == ContentDialogResult.Primary);
            } catch (Exception ex) { Status.Text = "The choice was not enabled. " + ex.Message; }
            finally { confirming = false; Render(); }
        }
        async void Save_Click(object sender, RoutedEventArgs args) {
            if (Busy || editor == null || editor.Closed) return;
            var owner = editor; saving = true; Render(); Status.Text = "Saving reviewed defaults…";
            bool saved = false;
            try { await Task.Run(() => owner.Save()); saved = true; }
            catch (Exception ex) { owner.Cancel(); Status.Text = "Could not finish saving. Close and reopen Import defaults to review the saved choices before retrying. " + ex.Message; }
            finally { saving = false; Render(); }
            if (saved) { active = false; if (Saved != null) Saved(); if (Completed != null) Completed("Import defaults saved. No import started."); }
        }
        void Cancel_Click(object sender, RoutedEventArgs args) {
            if (TryLeave() && Completed != null) Completed("Import defaults draft discarded. Saved choices were not changed.");
        }
    }
}
