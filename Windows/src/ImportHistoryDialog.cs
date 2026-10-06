using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace EasyEject {
    internal sealed class ImportHistoryDialog : Form {
        internal readonly ListView Batches = new ListView();
        internal readonly TextBox Details = new TextBox();
        readonly Button verify = new Button();
        internal readonly Button Share = new Button();
        internal CompletedBatch SelectedBatch { get { return Batches.SelectedItems.Count == 0 ? null : ImportHistory.Clone((CompletedBatch)Batches.SelectedItems[0].Tag); } }
        internal ImportHistoryDialog(IEnumerable<CompletedBatch> values, SavedProfile[] profiles = null) {
            Text = "Verified import history"; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(860, 620); MinimumSize = new Size(700, 500); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 52)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var intro = new Label { AutoSize = true, Dock = DockStyle.Fill, Text = "The last 20 verified batches survive source eject and restart. Select a batch to review its exact saved files. Verification checks the destination and recorded SHA-256 hashes.", Margin = new Padding(0, 0, 0, 12) };
            layout.SizeChanged += delegate { intro.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - layout.Padding.Horizontal), 0); };
            Batches.Dock = DockStyle.Fill; Batches.View = View.Details; Batches.FullRowSelect = true; Batches.MultiSelect = false; Batches.HideSelection = false;
            Batches.Columns.Add("Completed", 165); Batches.Columns.Add("Device", 280); Batches.Columns.Add("Saved files", 105);
            foreach (var value in values) {
                var batch = ImportHistory.Clone(value);
                var row = new ListViewItem(DateTime.Parse(batch.CompletedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime().ToString("g")) { Tag = batch };
                row.SubItems.Add(batch.DeviceName); row.SubItems.Add(batch.Originals.Count.ToString()); Batches.Items.Add(row);
            }
            Details.Dock = DockStyle.Fill; Details.Multiline = true; Details.ReadOnly = true; Details.ScrollBars = ScrollBars.Both; Details.WordWrap = false; Details.Margin = new Padding(0, 12, 0, 12);
            Details.Text = Batches.Items.Count == 0 ? "No completed media batches are saved yet. Index-only and failed imports do not create sharing history." : "Select a batch. The source device is not needed to verify these saved originals.";
            var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(100, 36), DialogResult = DialogResult.Cancel };
            verify.Text = "Verify saved originals…"; verify.AutoSize = true; verify.MinimumSize = new Size(190, 36); verify.Enabled = false;
            verify.Click += delegate { if (SelectedBatch != null) { DialogResult = DialogResult.OK; Close(); } };
            Share.Text = "Quick Share…"; Share.AutoSize = true; Share.MinimumSize = new Size(150, 36); Share.Visible = false; Share.Enabled = false;
            Share.Click += delegate { if (SelectedBatch != null && profiles != null && ManualQuickShareService.EligibleProfile(SelectedBatch, profiles) != null) { DialogResult = DialogResult.Retry; Close(); } };
            Batches.SelectedIndexChanged += delegate {
                var batch = SelectedBatch; verify.Enabled = batch != null;
                Share.Visible = Share.Enabled = profiles != null && ManualQuickShareService.EligibleProfile(batch, profiles) != null;
                if (batch == null) return;
                Details.Text = batch.DeviceName + Environment.NewLine + batch.Folder + Environment.NewLine + "Receipt: " + batch.ReceiptPath + Environment.NewLine + Environment.NewLine
                    + String.Join(Environment.NewLine, batch.Originals.Take(100).Select(x => SafePaths.Relative(x.Path, batch.Folder) + " (" + x.Bytes + " bytes)"))
                    + (batch.Originals.Count > 100 ? Environment.NewLine + "… " + (batch.Originals.Count - 100) + " more recorded files; verification checks all." : "");
            };
            footer.Controls.Add(close); footer.Controls.Add(verify); footer.Controls.Add(Share); CancelButton = close;
            layout.Controls.Add(intro); layout.Controls.Add(Batches); layout.Controls.Add(Details); layout.Controls.Add(footer); Controls.Add(layout);
        }
    }
}
