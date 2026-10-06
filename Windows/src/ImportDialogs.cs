using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace EasyEject {
    internal sealed class SelectionControls : UserControl {
        internal readonly CheckBox Videos = new CheckBox(), Previews = new CheckBox(), Clean = new CheckBox(), AskEject = new CheckBox(), DeleteOriginals = new CheckBox();
        internal string RemovalScope = "This choice applies only to this device's custom profile.";
        internal Func<bool> ConfirmRemoval;
        bool setting, previousVideos;
        readonly Label explanation = new Label();
        internal SelectionControls() {
            AutoSize = true; Dock = DockStyle.Top;
            var layout = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            Videos.Text = "Videos only"; Previews.Text = "Include camera previews"; Clean.Text = "Clean folder layout";
            AskEject.Text = "Ask to eject after import";
            DeleteOriginals.Text = "Permanently remove verified imported originals";
            ConfirmRemoval = () => OriginalRemovalChoice.Confirm(FindForm(), RemovalScope, Videos.Checked);
            Videos.CheckedChanged += delegate { if (!setting && previousVideos && !Videos.Checked) DeleteOriginals.Checked = false; previousVideos = Videos.Checked; };
            DeleteOriginals.CheckedChanged += delegate { if (!setting && DeleteOriginals.Checked && !ConfirmRemoval()) DeleteOriginals.Checked = false; };
            foreach (var box in new[] { Videos, Previews, Clean, AskEject, DeleteOriginals }) { box.AutoSize = true; box.Margin = new Padding(0, 4, 0, 8); layout.Controls.Add(box); }
            explanation.AutoSize = true; explanation.MaximumSize = new Size(580, 0); explanation.Text = "Videos only leaves photos and unfamiliar files on the device. Required camera-package audio and metadata are saved with their recordings.\n\nTurning previews off skips optional proxies and thumbnails.\n\nClean layout puts ordinary media directly in the date folder. Recording packages and unfamiliar media keep their folder tree. Conflicting groups stay together with unchanged filenames.\n\nKeep originals is recommended. Permanent removal skips Trash after complete batch verification. Changing Videos only to All media turns removal off for review."; layout.Controls.Add(explanation);
            Controls.Add(layout);
        }
        internal void Set(MediaOptions options) { setting = true; try { Videos.Checked = options.VideosOnly; Previews.Checked = options.IncludePreviews; Clean.Checked = options.CleanLayout; AskEject.Checked = options.AskToEject; DeleteOriginals.Checked = options.DeleteOriginals; } finally { setting = false; previousVideos = Videos.Checked; } }
        internal MediaOptions Snapshot() { return new MediaOptions(Videos.Checked, Previews.Checked, Clean.Checked, AskEject.Checked, DeleteOriginals.Checked); }
        internal void SetTextWidth(int width) { explanation.MaximumSize = new Size(Math.Max(250, width - 12), 0); }
    }
    internal sealed class ImportDefaultsDialog : Form {
        internal readonly SelectionControls Selection = new SelectionControls();
        internal ImportDefaults Value { get { var options = Selection.Snapshot(); return new ImportDefaults { VideosOnly = options.VideosOnly, IncludePreviews = options.IncludePreviews, CleanLayout = options.CleanLayout, AskToEject = options.AskToEject, DeleteOriginals = options.DeleteOriginals }; } }
        internal ImportDefaultsDialog(ImportDefaults defaults) {
            Text = "Import defaults"; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(650, 400); MinimumSize = new Size(600, 380); StartPosition = FormStartPosition.CenterParent;
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), AutoScroll = true };
            var content = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            var intro = new Label { AutoSize = true, MaximumSize = new Size(580, 0), Text = "New profiles use these choices. Profiles using defaults follow changes on their next import. Custom profiles keep their own choices. Saving never starts an import.", Margin = new Padding(0, 0, 0, 16) }; content.Controls.Add(intro);
            Selection.RemovalScope = "These shared defaults apply to EVERY device using defaults, including future profiles.";
            Selection.Set(defaults.Snapshot()); content.Controls.Add(Selection);
            var retained = new Label { AutoSize = true, Text = "Saved full-quality copies keep their filenames. Verified existing copies can be reused.", Margin = new Padding(0, 20, 0, 0) }; content.Controls.Add(retained);
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
        readonly TextBox source = new TextBox(), destination = new TextBox();
        readonly string root; ImportDefaults defaults; bool updating;
        internal ImportProfileDialog(ImportProfile profile, string sourceRoot, string deviceName, ImportDefaults defaults, bool existing) {
            Draft = profile.Copy(); root = sourceRoot; this.defaults = defaults;
            Text = existing ? "Edit import profile" : "Set up import profile"; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(720, 560); MinimumSize = new Size(680, 500); StartPosition = FormStartPosition.CenterParent;
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), AutoScroll = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var intro = new Label { AutoSize = true, MaximumSize = new Size(620, 0), Text = deviceName + "\nChoose a complete media folder and a destination on a different physical disk. Saving a profile never starts an import.", Margin = new Padding(0, 0, 0, 16) };
            layout.Controls.Add(intro, 0, 0); layout.SetColumnSpan(intro, 2);
            source.ReadOnly = true; destination.ReadOnly = true; source.Dock = destination.Dock = DockStyle.Fill;
            source.Text = Path.Combine(root, Draft.RelativeSource ?? ""); destination.Text = Draft.Destination ?? "";
            layout.Controls.Add(new Label { AutoSize = true, Text = "Media folder", Margin = new Padding(0, 4, 0, 4) }, 0, 1);
            layout.Controls.Add(source, 0, 2); var chooseSource = new Button { Text = "Choose…", AutoSize = true }; layout.Controls.Add(chooseSource, 1, 2);
            layout.Controls.Add(new Label { AutoSize = true, Text = "Save copies under", Margin = new Padding(0, 12, 0, 4) }, 0, 3);
            layout.Controls.Add(destination, 0, 4); var chooseDestination = new Button { Text = "Choose…", AutoSize = true }; layout.Controls.Add(chooseDestination, 1, 4);
            chooseSource.Click += delegate { Choose(source, "Choose a complete media folder on " + deviceName); };
            chooseDestination.Click += delegate { Choose(destination, "Choose an existing destination on a different physical disk"); };
            var modes = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 16, 0, 8) };
            UseDefaults.Text = "Use defaults"; UseCustom.Text = "Use custom settings";
            UseDefaults.AutoSize = UseCustom.AutoSize = true; modes.Controls.Add(UseDefaults); modes.Controls.Add(UseCustom);
            var editDefaults = new Button { Text = "Import defaults…", AutoSize = true }; modes.Controls.Add(editDefaults);
            layout.Controls.Add(modes, 0, 5); layout.SetColumnSpan(modes, 2);
            layout.Controls.Add(Selection, 0, 6); layout.SetColumnSpan(Selection, 2);
            var retained = new Label { AutoSize = true, MaximumSize = new Size(620, 0), Text = "Saved full-quality copies keep their filenames. Complete camera packages keep their structure. Formatting or a changed card identity requires setup again.", Margin = new Padding(0, 16, 0, 8) };
            layout.Controls.Add(retained, 0, 7); layout.SetColumnSpan(retained, 2);
            UseCustom.CheckedChanged += delegate { if (!updating && UseCustom.Checked) { Draft.SetCustom(true, this.defaults); UpdateSelection(); } };
            UseDefaults.CheckedChanged += delegate { if (!updating && UseDefaults.Checked) { SaveCustomSelection(); Draft.SetCustom(false, this.defaults); UpdateSelection(); } };
            editDefaults.Click += delegate {
                SaveCustomSelection();
                using (var dialog = new ImportDefaultsDialog(this.defaults)) {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    try { ImportDefaults.Save(dialog.Value); this.defaults = dialog.Value; UpdateSelection(); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save defaults", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            };
            body.Controls.Add(layout); Controls.Add(body);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16) };
            var save = new Button { Text = "Save profile", AutoSize = true, MinimumSize = new Size(130, 36) };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(100, 36) };
            save.Click += delegate {
                try {
                    string selected = SafePaths.Normalize(source.Text), target = SafePaths.Normalize(destination.Text);
                    if (!SafePaths.Within(selected, root)) throw new SafetyException("Choose a media folder within the selected card.");
                    SaveCustomSelection(); Draft.RelativeSource = selected.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? "" : SafePaths.Relative(selected, root);
                    Draft.Destination = target; DialogResult = DialogResult.OK;
                } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Review import folders", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            footer.Controls.Add(save); footer.Controls.Add(cancel);
            if (existing) { var forget = new Button { Text = "Forget profile", AutoSize = true, DialogResult = DialogResult.Retry, MinimumSize = new Size(130, 36) }; footer.Controls.Add(forget); }
            Controls.Add(footer); AcceptButton = save; CancelButton = cancel; UpdateSelection();
            body.SizeChanged += delegate { int width = Math.Max(300, body.ClientSize.Width - 60); intro.MaximumSize = retained.MaximumSize = new Size(width, 0); Selection.SetTextWidth(width); };
        }
        void Choose(TextBox field, string description) {
            using (var dialog = new FolderBrowserDialog { Description = description, SelectedPath = field.Text, ShowNewFolderButton = false }) { if (dialog.ShowDialog(this) == DialogResult.OK) field.Text = dialog.SelectedPath; }
        }
        internal void SaveCustomSelection() {
            if (Draft.FollowsDefaults) return;
            var options = Selection.Snapshot(); Draft.SetVideosOnly(options.VideosOnly); Draft.IncludePreviews = options.IncludePreviews; Draft.CleanLayout = options.CleanLayout; Draft.AskToEject = options.AskToEject; Draft.DeleteOriginals = options.DeleteOriginals;
        }
        internal void UpdateSelection() {
            updating = true;
            try { UseDefaults.Checked = Draft.FollowsDefaults; UseCustom.Checked = !Draft.FollowsDefaults; Selection.Set(Draft.Resolve(defaults)); Selection.Enabled = !Draft.FollowsDefaults; }
            finally { updating = false; }
        }
    }
}
