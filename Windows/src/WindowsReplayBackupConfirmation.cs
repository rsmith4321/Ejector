using System.Drawing;
using System.Windows.Forms;

namespace EasyEject {
    internal sealed class WindowsReplayBackupConfirmation : DpiForm {
        internal readonly TextBox Content;
        internal readonly Button Restore, Keep;
        internal WindowsReplayBackupConfirmation(string description) {
            Text = "Restore reviewed update history?"; Font = new Font("Segoe UI", 10); Size = new Size(780, 540); MinimumSize = new Size(660, 420); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            Content = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, Text = description };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(10), FlowDirection = FlowDirection.RightToLeft };
            Keep = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(100, 36) };
            Restore = new Button { Text = "Restore update history", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(190, 36) };
            footer.Controls.AddRange(new Control[] { Keep, Restore }); Controls.Add(Content); Controls.Add(footer); AcceptButton = Keep; CancelButton = Keep;
        }
    }
}
