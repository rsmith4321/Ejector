using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace EasyEject {
    internal sealed class ManualStorageDialog : Form {
        internal readonly TextBox Status = new TextBox(); internal readonly Button Stop = new Button(), Done = new Button();
        readonly CancellationTokenSource cancellation; bool running = true;
        internal ManualStorageDialog(string deviceName, CancellationTokenSource cancellation) {
            this.cancellation = cancellation; Text = "Manual storage action · " + deviceName; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(720, 440); MinimumSize = new Size(600, 360); StartPosition = FormStartPosition.CenterParent;
            Status.Multiline = true; Status.ReadOnly = true; Status.ScrollBars = ScrollBars.Vertical; Status.Dock = DockStyle.Fill; Status.Text = "Checking this device before safe removal. Other transfers keep their own progress and cancellation controls.";
            Stop.Text = "Cancel before removal"; Stop.AutoSize = true; Stop.Click += delegate { RequestStop(); };
            Done.Text = "Close"; Done.AutoSize = true; Done.Enabled = false; Done.Click += delegate { Close(); };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(16), FlowDirection = FlowDirection.RightToLeft }; footer.Controls.Add(Done); footer.Controls.Add(Stop);
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) }; body.Controls.Add(Status); Controls.Add(body); Controls.Add(footer);
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (running) { e.Cancel = true; RequestStop(); } };
        }
        internal void RequestStop() { if (!running || cancellation.IsCancellationRequested) return; cancellation.Cancel(); Stop.Enabled = false; Status.Text = "Stopping after the current check. A Windows removal request already in progress must finish."; }
        internal void PostProgress(string message) {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(() => { if (!IsDisposed && running && !cancellation.IsCancellationRequested) Status.Text = message; })); } catch (InvalidOperationException) {}
        }
        internal void Complete(string message) { running = false; Stop.Enabled = false; Done.Enabled = true; Status.Text = message; AcceptButton = CancelButton = Done; }
    }
}
