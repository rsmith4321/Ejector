using System;
using System.Drawing;
using System.Windows.Forms;

namespace EasyEject {
    internal static class OriginalRemovalChoice {
        internal const string Warning = "After the complete selected batch and its saved dependencies pass SHA-256 verification, only recognized imported originals and companions are permanently removed. This skips Trash and cannot be undone. Unknown files, device indexes, excluded photos and skipped previews stay on the device. Required package support files are saved; Videos only retains non-video support files on the device.\n\nKeep originals for important or irreplaceable media and keep independent backups. Sony and some other cameras maintain media databases; deleting outside the camera can leave those databases inconsistent. Use the camera to erase or format after backup if unsure.";
        internal static string Summary(int removed) { return removed == 0 ? "Originals kept." : "Permanently removed " + removed + " verified originals. Unselected/unknown files and device indexes are kept."; }
        internal static string BeforeImport(MediaOptions options) {
            return options.DeleteOriginals ? "PERMANENT ORIGINAL REMOVAL IS ON.\n" + Warning : "Originals are kept.";
        }
        internal static bool Confirm(IWin32Window owner, string scope, bool videosOnly) {
            using (var dialog = new OriginalRemovalConfirmationDialog(scope, videosOnly)) return dialog.ShowDialog(owner) == DialogResult.Yes;
        }
    }
    internal sealed class OriginalRemovalConfirmationDialog : Form {
        internal readonly Button Cancel = new Button(), EnableRemoval = new Button();
        internal readonly Label Explanation = new Label();
        internal OriginalRemovalConfirmationDialog(string scope, bool videosOnly) {
            Text = "Enable permanent original removal?"; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(700, 560); MinimumSize = new Size(640, 440); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24) };
            Explanation.AutoSize = true; Explanation.MaximumSize = new Size(590, 0); Explanation.Location = new Point(24, 24);
            Explanation.Text = scope + "\n\n" + (videosOnly ? "Videos only is selected. Photos and their excluded companions are retained." : "All media is selected. Recognized imported photos and audio are eligible too.")
                + "\n\n" + OriginalRemovalChoice.Warning + "\n\nSaving enables this choice for future imports; saving never starts an import or removes files.";
            body.Controls.Add(Explanation); body.SizeChanged += delegate { Explanation.MaximumSize = new Size(Math.Max(250, body.ClientSize.Width - 60), 0); };
            Cancel.Text = "Cancel"; Cancel.DialogResult = DialogResult.Cancel;
            EnableRemoval.Text = "Enable permanent removal"; EnableRemoval.DialogResult = DialogResult.Yes;
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16) };
            foreach (var button in new[] { Cancel, EnableRemoval }) { button.AutoSize = true; button.MinimumSize = new Size(110, 36); footer.Controls.Add(button); }
            Controls.Add(body); Controls.Add(footer); AcceptButton = Cancel; CancelButton = Cancel;
        }
    }
    internal sealed class VerifiedImportConfirmationDialog : Form {
        internal readonly Button Cancel = new Button(), StartImport = new Button();
        internal readonly Label Explanation = new Label();
        internal VerifiedImportConfirmationDialog(string description) {
            Text = "Review verified import"; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(740, 580); MinimumSize = new Size(660, 440); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24) };
            Explanation.AutoSize = true; Explanation.MaximumSize = new Size(630, 0); Explanation.Location = new Point(24, 24); Explanation.Text = description;
            body.Controls.Add(Explanation); body.SizeChanged += delegate { Explanation.MaximumSize = new Size(Math.Max(250, body.ClientSize.Width - 60), 0); };
            Cancel.Text = "Cancel"; Cancel.DialogResult = DialogResult.Cancel; StartImport.Text = "Start manual import"; StartImport.DialogResult = DialogResult.Yes;
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16) };
            foreach (var button in new[] { Cancel, StartImport }) { button.AutoSize = true; button.MinimumSize = new Size(110, 36); footer.Controls.Add(button); }
            Controls.Add(body); Controls.Add(footer); AcceptButton = Cancel; CancelButton = Cancel;
        }
    }
}
