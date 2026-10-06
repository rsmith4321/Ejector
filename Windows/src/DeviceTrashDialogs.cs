using System;
using System.Drawing;
using System.Windows.Forms;

namespace EasyEject {
    internal static class DeviceTrashChoice {
        internal const string Warning = "Files matching your media and preview choices are copied to Recovered Device Trash and verified with the complete selected import batch. Only recognized recovered files and companions are then permanently cleared from this device's selected Trash. This skips recovery from Trash. Unknown files, excluded photos, skipped previews and device indexes remain. Windows restoration records are saved and cleared only after their corresponding verified file. Empty folders and records for retained contents remain.\n\nRecovery is independent of ordinary-original removal. Keep independent backups. Unsupported or ambiguous Trash records stop recovery for review, with remaining data retained. Saving never starts recovery.";
        internal static string Scope(string macUid) { return "Windows: only the current Windows user's Recycle Bin on this device.\nMac: " + (macUid == null ? "no owner selected; Mac Trash is left on the device." : "only the explicitly selected .Trashes\\" + macUid + " folder on this device."); }
        internal static string BeforeImport(MediaOptions options) { return options.RecoverTrash ? "RECOVER AND CLEAR SELECTED DEVICE TRASH IS ON.\n" + Scope(options.MacTrashUid) + "\n\n" + Warning : "Device Trash recovery is off; Trash stays on the device."; }
        internal static string Summary(int recovered, int cleared, int records) { return recovered == 0 ? "" : "Recovered " + recovered + " Trash files; permanently cleared " + cleared + " verified files and " + records + " restoration records."; }
        internal static bool Confirm(IWin32Window owner, string scope) { using (var dialog = new DeviceTrashRecoveryConfirmationDialog(scope)) return dialog.ShowDialog(owner) == DialogResult.Yes; }
    }
    internal sealed class DeviceTrashRecoveryConfirmationDialog : Form {
        internal readonly Button Cancel = new Button(), EnableRecovery = new Button(); internal readonly Label Explanation = new Label();
        internal DeviceTrashRecoveryConfirmationDialog(string scope) {
            Text = "Enable verified device Trash recovery?"; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(700, 540); MinimumSize = new Size(640, 440); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24) };
            Explanation.AutoSize = true; Explanation.MaximumSize = new Size(590, 0); Explanation.Location = new Point(24, 24);
            Explanation.Text = scope + "\n\nRecovery uses the current Windows user only, plus any explicit Mac Trash owner saved in each device's profile. Mac owners are never guessed.\n\n" + DeviceTrashChoice.Warning;
            body.Controls.Add(Explanation); body.SizeChanged += delegate { Explanation.MaximumSize = new Size(Math.Max(250, body.ClientSize.Width - 60), 0); };
            Cancel.Text = "Cancel"; Cancel.DialogResult = DialogResult.Cancel; EnableRecovery.Text = "Enable verified recovery"; EnableRecovery.DialogResult = DialogResult.Yes;
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16) };
            foreach (var button in new[] { Cancel, EnableRecovery }) { button.AutoSize = true; button.MinimumSize = new Size(110, 36); footer.Controls.Add(button); }
            Controls.Add(body); Controls.Add(footer); AcceptButton = Cancel; CancelButton = Cancel;
        }
    }
}
