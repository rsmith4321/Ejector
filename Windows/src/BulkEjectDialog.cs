using System.Drawing;
using System.Windows.Forms;

namespace EasyEject {
    internal sealed class BulkEjectDialog : Form {
        internal BulkEjectDialog(BulkEjectPlan plan) {
            Text = "Review card removal"; Font = new Font("Segoe UI", 10); Size = new Size(720, 520); MinimumSize = new Size(600, 400); AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = "Request Windows safe removal for these " + plan.Targets.Length + " camera/emulator devices and all listed volumes?\r\n\r\n" + plan.Description + "\r\n\r\nFinish all transfers first. Requests run one device at a time and stop on a veto or identity change. Wait for verified removal before unplugging." };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(16), FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "Keep connected", AutoSize = true, MinimumSize = new Size(150, 36), DialogResult = DialogResult.Cancel };
            var request = new Button { Text = "Request safe removal", AutoSize = true, MinimumSize = new Size(170, 36), DialogResult = DialogResult.Yes }; footer.Controls.Add(cancel); footer.Controls.Add(request);
            Controls.Add(details); Controls.Add(footer); AcceptButton = cancel; CancelButton = cancel;
        }
    }
    internal sealed class BulkEjectResultDialog : Form {
        internal BulkEjectResultDialog(BulkEjectResult result) {
            Text = result.Success ? "Cards safely removed" : "Card removal stopped"; Font = new Font("Segoe UI", 10); Size = new Size(720, 520); MinimumSize = new Size(600, 400); AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = result.Message };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(16), FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(100, 36), DialogResult = DialogResult.Cancel }; footer.Controls.Add(close);
            Controls.Add(details); Controls.Add(footer); AcceptButton = close; CancelButton = close;
        }
    }
}
