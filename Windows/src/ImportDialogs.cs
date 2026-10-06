using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace EasyEject {
    internal sealed class SelectionControls : UserControl {
        internal readonly CheckBox Videos = new CheckBox(), Previews = new CheckBox(), Clean = new CheckBox(), Automatic = new CheckBox(), AskEject = new CheckBox(), DeleteOriginals = new CheckBox(), RecoverTrash = new CheckBox();
        internal string RemovalScope = "This choice applies only to this device's custom profile.";
        internal Func<bool> ConfirmRemoval, ConfirmRecovery;
        bool setting, previousVideos;
        readonly Label explanation = new Label();
        internal SelectionControls() {
            AutoSize = true; Dock = DockStyle.Top;
            var layout = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            Videos.Text = "Videos only"; Previews.Text = "Include camera previews"; Clean.Text = "Clean folder layout";
            AskEject.Text = "Ask to eject after import";
            Automatic.Text = "Import automatically when connected";
            DeleteOriginals.Text = "Permanently remove verified imported originals";
            RecoverTrash.Text = "Recover and clear selected device Trash";
            ConfirmRemoval = () => OriginalRemovalChoice.Confirm(FindForm(), RemovalScope, Videos.Checked);
            ConfirmRecovery = () => DeviceTrashChoice.Confirm(FindForm(), RemovalScope);
            Videos.CheckedChanged += delegate { if (!setting && previousVideos && !Videos.Checked) { DeleteOriginals.Checked = false; RecoverTrash.Checked = false; } previousVideos = Videos.Checked; };
            DeleteOriginals.CheckedChanged += delegate { if (!setting && DeleteOriginals.Checked && !ConfirmRemoval()) DeleteOriginals.Checked = false; };
            RecoverTrash.CheckedChanged += delegate { if (!setting && RecoverTrash.Checked && !ConfirmRecovery()) RecoverTrash.Checked = false; };
            foreach (var box in new[] { Videos, Previews, Clean, Automatic, AskEject, DeleteOriginals, RecoverTrash }) { box.AutoSize = true; box.Margin = new Padding(0, 4, 0, 8); layout.Controls.Add(box); }
            explanation.AutoSize = true; explanation.MaximumSize = new Size(580, 0); explanation.Text = "Videos only leaves photos and unfamiliar files on the device. Required camera-package audio and metadata are saved with their recordings.\n\nTurning previews off skips optional proxies and thumbnails.\n\nClean layout puts ordinary media directly in the date folder. Recording packages and unfamiliar media keep their folder tree. Conflicting groups stay together with unchanged filenames.\n\nKeep originals is recommended. Permanent removal skips Trash after complete batch verification. Trash recovery is separate and uses the current Windows user plus any explicitly selected Mac owner. Changing Videos only to All media turns both removal and recovery off for review."; layout.Controls.Add(explanation);
            Controls.Add(layout);
        }
        internal void Set(MediaOptions options) { setting = true; try { Videos.Checked = options.VideosOnly; Previews.Checked = options.IncludePreviews; Clean.Checked = options.CleanLayout; AskEject.Checked = options.AskToEject; DeleteOriginals.Checked = options.DeleteOriginals; RecoverTrash.Checked = options.RecoverTrash; } finally { setting = false; previousVideos = Videos.Checked; } }
        internal MediaOptions Snapshot() { return new MediaOptions(Videos.Checked, Previews.Checked, Clean.Checked, AskEject.Checked, DeleteOriginals.Checked, RecoverTrash.Checked); }
        internal void SetTextWidth(int width) { explanation.MaximumSize = new Size(Math.Max(250, width - 12), 0); }
    }
    internal sealed class ImportDefaultsDialog : Form {
        internal readonly SelectionControls Selection = new SelectionControls();
        internal ImportDefaults Value { get { var options = Selection.Snapshot(); return new ImportDefaults { AutomaticImport = Selection.Automatic.Checked, VideosOnly = options.VideosOnly, IncludePreviews = options.IncludePreviews, CleanLayout = options.CleanLayout, AskToEject = options.AskToEject, DeleteOriginals = options.DeleteOriginals, RecoverTrash = options.RecoverTrash }; } }
        internal ImportDefaultsDialog(ImportDefaults defaults) {
            Text = "Import defaults"; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(650, 400); MinimumSize = new Size(600, 380); StartPosition = FormStartPosition.CenterParent;
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), AutoScroll = true };
            var content = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            var intro = new Label { AutoSize = true, MaximumSize = new Size(580, 0), Text = "New profiles use these choices. Profiles using defaults follow changes on their next import. Custom profiles keep their own choices. Saving never starts an import.", Margin = new Padding(0, 0, 0, 16) }; content.Controls.Add(intro);
            Selection.RemovalScope = "These shared defaults apply to EVERY device using defaults, including future profiles.";
            Selection.Set(defaults.Snapshot()); content.Controls.Add(Selection);
            Selection.Automatic.Checked = defaults.AutomaticEnabled;
            var retained = new Label { AutoSize = true, Text = "Saved full-quality copies keep their filenames. Verified existing copies can be reused.\n\nAutomatic import applies these same choices, including any acknowledged removal/recovery, on the next source connection and when the app launches. It never exports or ejects automatically. Saving choices starts no import on devices already connected.", Margin = new Padding(0, 20, 0, 0) }; content.Controls.Add(retained);
            body.Controls.Add(content); Controls.Add(body);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16) };
            var save = new Button { Text = "Save defaults", AutoSize = true, DialogResult = DialogResult.OK, MinimumSize = new Size(130, 36) };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(100, 36) };
            footer.Controls.Add(save); footer.Controls.Add(cancel); Controls.Add(footer); AcceptButton = save; CancelButton = cancel;
            body.SizeChanged += delegate { int width = Math.Max(300, body.ClientSize.Width - 60); intro.MaximumSize = retained.MaximumSize = new Size(width, 0); Selection.SetTextWidth(width); };
        }
    }
    internal sealed class ImportProfileDialog : Form {
        internal readonly ImportProfile Draft; internal readonly SelectionControls Selection = new SelectionControls();
        internal readonly RadioButton UseDefaults = new RadioButton(), UseCustom = new RadioButton();
        internal readonly TextBox DeviceName = new TextBox();
        internal readonly TextBox SourceFolder = new TextBox(), DestinationFolder = new TextBox();
        TextBox source { get { return SourceFolder; } } TextBox destination { get { return DestinationFolder; } }
        internal readonly Button ChooseSource = new Button(), ChooseOwner = new Button();
        internal bool DestinationWasChosen { get; private set; }
        internal readonly Label MacTrashOwner = new Label { AutoSize = true, MaximumSize = new Size(580, 0) };
        readonly string root; ImportDefaults defaults; bool updating;
        internal ImportProfileDialog(ImportProfile profile, string sourceRoot, string deviceName, ImportDefaults defaults, bool existing, Action settingsSaved = null) {
            Draft = profile.Copy(); root = sourceRoot; this.defaults = defaults;
            Text = existing ? "Edit import profile" : "Set up import profile"; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(720, 560); MinimumSize = new Size(680, 500); StartPosition = FormStartPosition.CenterParent;
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), AutoScroll = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var intro = new Label { AutoSize = true, MaximumSize = new Size(620, 0), Text = deviceName + (root == null ? "\nEdit saved choices while this device is disconnected. Connect the original device to change its media folder or select a Mac Trash owner. Import checks its identity and both disks again. Saving starts no import." : "\nChoose a complete media folder and a destination on a different physical disk. Saving a profile never starts an import."), Margin = new Padding(0, 0, 0, 12) };
            var header = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            header.Controls.Add(intro); header.Controls.Add(new Label { AutoSize = true, Text = "Device name (independent of the card's volume label)" });
            DeviceName.Text = Draft.Name ?? ProfileName.ForDevice(deviceName); DeviceName.MaxLength = 120; DeviceName.Width = 550; header.Controls.Add(DeviceName);
            layout.Controls.Add(header, 0, 0); layout.SetColumnSpan(header, 2);
            source.ReadOnly = true; destination.ReadOnly = true; source.Dock = destination.Dock = DockStyle.Fill;
            source.Text = root == null ? (String.IsNullOrEmpty(Draft.RelativeSource) ? "Whole device" : Draft.RelativeSource) : Path.Combine(root, Draft.RelativeSource ?? ""); destination.Text = Draft.Destination ?? "";
            layout.Controls.Add(new Label { AutoSize = true, Text = "Media folder", Margin = new Padding(0, 4, 0, 4) }, 0, 1);
            layout.Controls.Add(source, 0, 2); var chooseSource = ChooseSource; chooseSource.Text = "Choose…"; chooseSource.AutoSize = true; chooseSource.Enabled = root != null; layout.Controls.Add(chooseSource, 1, 2);
            layout.Controls.Add(new Label { AutoSize = true, Text = "Save copies under", Margin = new Padding(0, 12, 0, 4) }, 0, 3);
            layout.Controls.Add(destination, 0, 4); var chooseDestination = new Button { Text = "Choose…", AutoSize = true }; layout.Controls.Add(chooseDestination, 1, 4);
            chooseSource.Click += delegate { Choose(source, "Choose a complete media folder on " + deviceName); };
            chooseDestination.Click += delegate { if (Choose(destination, "Choose an existing destination on a different physical disk")) DestinationWasChosen = true; };
            var modes = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 16, 0, 8) };
            UseDefaults.Text = "Use defaults"; UseCustom.Text = "Use custom settings";
            UseDefaults.AutoSize = UseCustom.AutoSize = true; modes.Controls.Add(UseDefaults); modes.Controls.Add(UseCustom);
            var editDefaults = new Button { Text = "Import defaults…", AutoSize = true }; modes.Controls.Add(editDefaults);
            layout.Controls.Add(modes, 0, 5); layout.SetColumnSpan(modes, 2);
            layout.Controls.Add(Selection, 0, 6); layout.SetColumnSpan(Selection, 2);
            var retained = new Label { AutoSize = true, MaximumSize = new Size(620, 0), Text = "Saved full-quality copies keep their filenames. Complete camera packages keep their structure. Formatting or a changed card identity requires setup again.\n\nAutomatic import uses these same choices, including any acknowledged removal/recovery, on the next source connection and at app launch. Saving starts no work on an already connected device. Failures during copying/removal require manual retry or a new source connection; preflight waits can retry after a storage event. Eject and sharing always require a manual choice.", Margin = new Padding(0, 16, 0, 8) };
            layout.Controls.Add(retained, 0, 7); layout.SetColumnSpan(retained, 2);
            var trashOwner = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 12, 0, 8) };
            trashOwner.Controls.Add(new Label { AutoSize = true, Text = "Mac Trash owner (optional, only for this device)" }); trashOwner.Controls.Add(MacTrashOwner);
            var ownerButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var chooseOwner = ChooseOwner; chooseOwner.AutoSize = true; chooseOwner.Text = "Choose Mac Trash owner…"; chooseOwner.Enabled = root != null; var clearOwner = new Button { AutoSize = true, Text = "Windows Trash only" }; ownerButtons.Controls.Add(chooseOwner); ownerButtons.Controls.Add(clearOwner); trashOwner.Controls.Add(ownerButtons);
            chooseOwner.Click += delegate {
                using (var dialog = new FolderBrowserDialog { Description = "Choose YOUR numeric owner folder under this device's .Trashes folder. Other owners are never inferred or swept.", SelectedPath = Path.Combine(root, ".Trashes"), ShowNewFolderButton = false }) {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    try {
                        string owner = ValidateMacOwner(dialog.SelectedPath);
                        if (MessageBox.Show(this, "Confirm this is YOUR Mac Trash owner folder on this device:\n" + dialog.SelectedPath + "\n\nOnly this owner is added to the current Windows user's Trash scope when recovery is enabled. Choosing an owner does not enable recovery or start work.", "Select Mac Trash owner", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes) { Draft.MacTrashUid = owner; UpdateOwner(); }
                    } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Review Trash owner", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            };
            clearOwner.Click += delegate { Draft.MacTrashUid = null; UpdateOwner(); };
            layout.Controls.Add(trashOwner, 0, 8); layout.SetColumnSpan(trashOwner, 2); UpdateOwner();
            UseCustom.CheckedChanged += delegate { if (!updating && UseCustom.Checked) { Draft.SetCustom(true, this.defaults); UpdateSelection(); } };
            UseDefaults.CheckedChanged += delegate { if (!updating && UseDefaults.Checked) { SaveCustomSelection(); Draft.SetCustom(false, this.defaults); UpdateSelection(); } };
            editDefaults.Click += delegate {
                SaveCustomSelection();
                using (var dialog = new ImportDefaultsDialog(this.defaults)) {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    try { ImportDefaults.Save(dialog.Value); if (settingsSaved != null) settingsSaved(); this.defaults = dialog.Value; UpdateSelection(); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save defaults", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            };
            body.Controls.Add(layout); Controls.Add(body);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16) };
            var save = new Button { Text = "Save profile", AutoSize = true, MinimumSize = new Size(130, 36) };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(100, 36) };
            save.Click += delegate {
                try {
                    ApplyEdits(); DialogResult = DialogResult.OK;
                } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Review import folders", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            footer.Controls.Add(save); footer.Controls.Add(cancel);
            if (existing) { var forget = new Button { Text = "Forget profile", AutoSize = true, DialogResult = DialogResult.Retry, MinimumSize = new Size(130, 36) }; footer.Controls.Add(forget); }
            Controls.Add(footer); AcceptButton = save; CancelButton = cancel; UpdateSelection();
            body.SizeChanged += delegate { int width = Math.Max(300, body.ClientSize.Width - 60); intro.MaximumSize = retained.MaximumSize = new Size(width, 0); DeviceName.Width = width; Selection.SetTextWidth(width); };
        }
        internal void ApplyEdits() {
            string name = DeviceName.Text.Trim(); Profiles.ValidateName(name, true);
            string target = SafePaths.Normalize(destination.Text);
            if (root != null) {
                string selected = SafePaths.Normalize(source.Text);
                if (!SafePaths.Within(selected, root)) throw new SafetyException("Choose a media folder within the selected card.");
                Draft.RelativeSource = selected.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? "" : SafePaths.Relative(selected, root);
            }
            SaveCustomSelection(); Draft.Name = name; Draft.Destination = target;
            if (DestinationWasChosen) Draft.SavedDestination = null;
            if (root == null && Draft.AutomaticEnabled(defaults) && Draft.SavedDestination == null && !DestinationWasChosen) throw new SafetyException("Choose the destination folder again to bind its disk identity before enabling automatic import for this older profile.");
        }
        bool Choose(TextBox field, string description) {
            using (var dialog = new FolderBrowserDialog { Description = description, SelectedPath = field.Text, ShowNewFolderButton = false }) { if (dialog.ShowDialog(this) != DialogResult.OK) return false; field.Text = dialog.SelectedPath; return true; }
        }
        internal void SaveCustomSelection() {
            if (Draft.FollowsDefaults) return;
            var options = Selection.Snapshot(); Draft.SetVideosOnly(options.VideosOnly); Draft.IncludePreviews = options.IncludePreviews; Draft.CleanLayout = options.CleanLayout; Draft.AskToEject = options.AskToEject; Draft.DeleteOriginals = options.DeleteOriginals; Draft.RecoverTrash = options.RecoverTrash; Draft.AutomaticImport = Selection.Automatic.Checked;
        }
        internal string ValidateMacOwner(string input) {
            if (root == null) throw new SafetyException("Connect the original device before selecting a Mac Trash owner.");
            string path = SafePaths.Normalize(input), parent = SafePaths.Normalize(Path.Combine(root, ".Trashes"));
            if (!String.Equals(Path.GetDirectoryName(path), parent, StringComparison.OrdinalIgnoreCase) || !DeviceTrash.ValidMacOwner(Path.GetFileName(path))) throw new SafetyException("Choose one explicit numeric owner folder directly under this device's .Trashes folder.");
            return Path.GetFileName(path);
        }
        internal void UpdateOwner() { MacTrashOwner.Text = DeviceTrashChoice.Scope(Draft.MacTrashUid) + "\nSelecting an owner does not enable recovery. The recovery choice above still applies."; }
        internal void UpdateSelection() {
            updating = true;
            try { UseDefaults.Checked = Draft.FollowsDefaults; UseCustom.Checked = !Draft.FollowsDefaults; Selection.Set(Draft.Resolve(defaults)); Selection.Automatic.Checked = Draft.AutomaticEnabled(defaults); Selection.Enabled = !Draft.FollowsDefaults; }
            finally { updating = false; }
        }
    }
}
