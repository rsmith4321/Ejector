using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace EasyEject {
    internal sealed class SavedProfilesDialog : DpiForm {
        internal readonly ListView ProfilesList = new ListView();
        internal readonly Button Edit = new Button(), Forget = new Button();
        internal readonly TextBox Detail = new TextBox();
        readonly Func<SavedProfile[]> read;
        readonly Func<ImportDefaults> defaults;
        readonly Action<SavedProfile, ImportProfile, bool> save;
        readonly Action<SavedProfile> forget;
        readonly Action settingsSaved;
        internal bool Changed { get; private set; }
        SavedProfile Selected { get { return ProfilesList.SelectedItems.Count == 1 ? (SavedProfile)ProfilesList.SelectedItems[0].Tag : null; } }
        internal SavedProfilesDialog(Func<SavedProfile[]> read, Func<ImportDefaults> defaults, Action<SavedProfile, ImportProfile> save, Action<SavedProfile> forget)
            : this(read, defaults, (entry, draft, chosen) => save(entry, draft), forget, null) {}
        internal SavedProfilesDialog(Func<SavedProfile[]> read, Func<ImportDefaults> defaults, Action<SavedProfile, ImportProfile, bool> save, Action<SavedProfile> forget, Action settingsSaved) {
            this.read = read; this.defaults = defaults; this.save = save; this.forget = forget;
            this.settingsSaved = settingsSaved;
            Text = "Saved import profiles"; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(810, 570); MinimumSize = new Size(700, 490); StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            var intro = new Label { AutoSize = true, MaximumSize = new Size(740, 0), Text = "Review, rename, edit or forget profiles while devices are disconnected. To enroll a device or change its media folder, connect it and choose Import profile in the device window. Saving never starts an import.", Margin = new Padding(0, 0, 0, 12) };
            ProfilesList.Dock = DockStyle.Fill; ProfilesList.View = View.Details; ProfilesList.FullRowSelect = true; ProfilesList.MultiSelect = false; ProfilesList.HideSelection = false;
            ProfilesList.Columns.Add("Saved device", 270); ProfilesList.Columns.Add("Import choices", 150); ProfilesList.Columns.Add("Destination", 320);
            Detail.ReadOnly = true; Detail.Multiline = true; Detail.ScrollBars = ScrollBars.Vertical; Detail.Dock = DockStyle.Fill; Detail.Margin = new Padding(0, 12, 0, 0);
            layout.Controls.Add(intro, 0, 0); layout.Controls.Add(ProfilesList, 0, 1); layout.Controls.Add(Detail, 0, 2); Controls.Add(layout);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(14) };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(90, 36) };
            var refresh = new Button { Text = "Refresh profiles", AutoSize = true, MinimumSize = new Size(130, 36) };
            Edit.Text = "Edit profile…"; Forget.Text = "Forget profile…"; Edit.AutoSize = Forget.AutoSize = true; Edit.MinimumSize = Forget.MinimumSize = new Size(130, 36);
            footer.Controls.Add(close); footer.Controls.Add(refresh); footer.Controls.Add(Forget); footer.Controls.Add(Edit); Controls.Add(footer); CancelButton = close;
            ProfilesList.SelectedIndexChanged += delegate { UpdateSelection(); };
            ProfilesList.DoubleClick += delegate { if (Edit.Enabled) EditSelected(); };
            Edit.Click += delegate { EditSelected(); }; Forget.Click += delegate { ForgetSelected(); };
            refresh.Click += delegate { TryRefresh(); }; layout.SizeChanged += delegate { intro.MaximumSize = new Size(Math.Max(300, layout.ClientSize.Width - 40), 0); };
            RefreshProfiles();
        }
        internal void RefreshProfiles() {
            var entries = read(); ProfilesList.Items.Clear();
            foreach (var entry in entries) {
                var row = new ListViewItem(entry.Display) { Tag = entry };
                row.SubItems.Add(entry.CanEdit ? (entry.Profile.FollowsDefaults ? "Uses defaults" : "Custom") + " · " + (entry.Profile.AutomaticEnabled(defaults()) ? "Automatic" : "Manual") : "Needs review");
                row.SubItems.Add(entry.CanEdit ? entry.Profile.Destination : "Saved record could not be used"); ProfilesList.Items.Add(row);
            }
            UpdateSelection();
        }
        void TryRefresh() { try { RefreshProfiles(); } catch (Exception ex) { Edit.Enabled = Forget.Enabled = false; Detail.Text = "Saved profiles could not be refreshed. Close and try again.\r\n\r\n" + ex.Message; } }
        internal void UpdateSelection() {
            var selected = Selected; Edit.Enabled = Forget.Enabled = selected != null && selected.CanEdit;
            if (selected == null) { Detail.Text = ProfilesList.Items.Count == 0 ? "No saved devices. Connect a device to create an import profile." : "Select a saved profile to review its folders, owner scope and device identity. This list gives no permission to import or eject."; return; }
            if (!selected.CanEdit) { Detail.Text = "This record could not be used and was left intact:\r\n" + selected.FileName + "\r\n\r\n" + selected.Error + "\r\n\r\nConnect the original device and set up its profile again. No device identity is guessed."; return; }
            var p = selected.Profile; var choices = p.Resolve(defaults());
            Detail.Text = selected.Display + "\r\nMedia folder: " + (String.IsNullOrEmpty(p.RelativeSource) ? "Whole device" : p.RelativeSource) + "\r\nDestination: " + p.Destination + "\r\n\r\n" + (p.AutomaticEnabled(defaults()) ? "Automatic import on the next connection (and app launch)" : "Manual import only") + "\r\n" + (choices.VideosOnly ? "Videos only" : "All media") + " · " + (choices.IncludePreviews ? "Includes previews" : "Skips optional previews") + " · " + (choices.CleanLayout ? "Clean layout" : "Preserved folders") + "\r\n" + (choices.AskToEject ? "Asks to eject after import" : "No post-import eject prompt") + "\r\n" + OriginalRemovalChoice.BeforeImport(choices) + "\r\n" + (choices.RecoverTrash ? "Device Trash recovery is on." : "Device Trash recovery is off.") + "\r\n" + DeviceTrashChoice.Scope(p.MacTrashUid) + "\r\n\r\nSaved volume: " + p.VolumeId + "\r\nVolume serial: " + p.VolumeSerial + "\r\nPhysical disk: " + p.DiskInstance + "\r\nDestination identity: " + (p.SavedDestination == null ? "Older profile — choose the destination again before automatic import" : p.SavedDestination.VolumeId + " / " + p.SavedDestination.DiskInstance) + "\r\n\r\nThe original device and destination are checked again before each import. Formatting requires enrollment again.";
        }
        void EditSelected() {
            var entry = Selected; if (entry == null || !entry.CanEdit) return;
            try {
                using (var dialog = new ImportProfileDialog(entry.Profile, null, entry.Display, defaults(), true, settingsSaved)) {
                    var result = dialog.ShowDialog(this);
                    if (result == DialogResult.OK) { save(entry, dialog.Draft, dialog.DestinationWasChosen); Changed = true; if (settingsSaved != null) settingsSaved(); RefreshProfiles(); }
                    else if (result == DialogResult.Retry) ForgetEntry(entry);
                }
            } catch (Exception ex) { MessageBox.Show(this, ex.Message + "\n\nNo new import started. Refresh profiles before trying again.", "Saved profile needs review", MessageBoxButtons.OK, MessageBoxIcon.Warning); TryRefresh(); }
        }
        void ForgetSelected() { var entry = Selected; if (entry == null || !entry.CanEdit) return; try { ForgetEntry(entry); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Profile could not be forgotten", MessageBoxButtons.OK, MessageBoxIcon.Warning); TryRefresh(); } }
        void ForgetEntry(SavedProfile entry) {
            if (MessageBox.Show(this, "Forget " + entry.Display + " and its saved folders/import choices?\n\nOriginals, saved copies, import history and shared defaults stay intact. The device must be enrolled again to import.", "Forget saved profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            forget(entry); Changed = true; if (settingsSaved != null) settingsSaved(); RefreshProfiles();
        }
    }
    internal static class OfflineProfileDestination {
        internal static void SaveSelected(SavedProfile expected, ImportProfile draft, IDiscovery discovery, string root, Func<string, string> volumeId = null, Action<string> checkpoint = null) {
            if (draft == null) throw new SafetyException("Choose valid saved profile choices.");
            var value = draft.Copy(); string destination = SafePaths.Normalize(value.Destination);
            volumeId = volumeId ?? WindowsDiscovery.VolumeId;
            using (var directories = SafePaths.HoldDirectories(destination)) {
                directories.Validate(); string id = volumeId(destination); directories.Validate();
                var identity = DestinationIdentity.Capture(discovery.Scan(), id); directories.Validate(); Validate(value, identity);
                var guard = new WindowsDestinationGuard(discovery, destination, identity, volumeId); directories.Validate();
                Profiles.SaveOffline(expected, value, root, identity, stage => {
                    directories.Validate(); guard.Validate(); directories.Validate();
                    if (checkpoint != null) checkpoint(stage);
                    directories.Validate(); guard.Validate(); directories.Validate();
                });
            }
        }
        internal static void Validate(ImportProfile profile, DestinationIdentity target) {
            if (profile == null || target == null) throw new SafetyException("Destination identity is unavailable."); target.Validate();
            if (String.Equals(profile.VolumeId, target.VolumeId, StringComparison.OrdinalIgnoreCase) || String.Equals(profile.DiskInstance, target.DiskInstance, StringComparison.OrdinalIgnoreCase)) throw new SafetyException("Choose a destination on a different physical disk from this saved device.");
        }
    }
}
