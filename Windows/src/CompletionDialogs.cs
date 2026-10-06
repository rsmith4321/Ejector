using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace EasyEject {
    internal sealed class ImportCompletionDialog : Form {
        internal readonly Button Keep = new Button(), Eject = new Button(), OpenFolder = new Button();
        internal readonly Label Explanation = new Label();
        bool expired;
        internal ImportCompletionDialog(ImportCompletion entry, bool canEject) {
            Text = entry.HasMedia ? "Import complete. Eject now?" : "No matching media. Eject now?";
            Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi; Size = new Size(740, 500); MinimumSize = new Size(680, 440); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24) };
            var plan = entry.Origin.Plan;
            Explanation.AutoSize = true; Explanation.MaximumSize = new Size(630, 0); Explanation.Location = new Point(24, 24);
            Explanation.Text = entry.DeviceName + Environment.NewLine + Environment.NewLine + entry.Message
                + (entry.HasMedia ? "" : "\n\nPhotos, skipped previews or other files may still be on the device.")
                + "\n\nChoose Keep Connected for Lightroom or other transfers. Eject Now requests Windows safe removal of this device and all affected partitions/storage sources."
                + (plan == null ? "\n\n" + entry.Origin.BlockedReason : "\n\nAffected storage:\n" + String.Join("\n", plan.Affected))
                + "\n\nSaved folder:\n" + entry.Folder;
            body.Controls.Add(Explanation); body.SizeChanged += delegate { Explanation.MaximumSize = new Size(Math.Max(250, body.ClientSize.Width - 60), 0); };
            Keep.Text = "Keep Connected"; Keep.DialogResult = DialogResult.Cancel;
            Eject.Text = plan != null && plan.Affected.Length > 1 ? "Eject Device" : "Eject Now"; Eject.DialogResult = DialogResult.Yes; Eject.Enabled = canEject;
            OpenFolder.Text = "Open Import Folder"; OpenFolder.DialogResult = DialogResult.Ignore;
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(16), FlowDirection = FlowDirection.RightToLeft };
            foreach (var button in new[] { Keep, Eject, OpenFolder }) { button.AutoSize = true; button.MinimumSize = new Size(150, 36); footer.Controls.Add(button); }
            Controls.Add(body); Controls.Add(footer); AcceptButton = Keep; CancelButton = Keep;
            if (!canEject && plan != null) ExpireEject();
        }
        internal void ExpireEject() { Eject.Enabled = false; if (expired) return; expired = true; Explanation.Text += "\n\nThe device connection changed. Ejection from this choice is disabled; refresh and review the device."; }
    }
    internal sealed class PendingCompletionsDialog : Form {
        internal readonly ListBox Entries = new ListBox();
        internal string SelectedId { get { return Entries.SelectedItem == null ? null : ((ImportCompletion)Entries.SelectedItem).Id; } }
        internal PendingCompletionsDialog(ImportCompletion[] values) {
            Text = "Completed imports"; Font = new Font("Segoe UI", 10); Size = new Size(680, 420); MinimumSize = new Size(600, 360); AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var intro = new Label { AutoSize = true, Dock = DockStyle.Fill, Text = "Review an import to keep connected, open its folder, or explicitly request safe removal. Choices expire after connection changes, another import on that source, or app restart.", Margin = new Padding(0, 0, 0, 12) };
            layout.SizeChanged += delegate { intro.MaximumSize = new Size(Math.Max(200, layout.ClientSize.Width - 40), 0); };
            Entries.Dock = DockStyle.Fill; Entries.Format += delegate(object sender, ListControlConvertEventArgs e) { e.Value = ((ImportCompletion)e.ListItem).DeviceName; }; Entries.FormattingEnabled = true;
            foreach (var value in values) Entries.Items.Add(value);
            var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 0, 0) };
            var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(100, 36), DialogResult = DialogResult.Cancel };
            var review = new Button { Text = "Review import…", AutoSize = true, MinimumSize = new Size(150, 36), Enabled = values.Length > 0 };
            review.Click += delegate { if (SelectedId != null) DialogResult = DialogResult.OK; }; footer.Controls.Add(close); footer.Controls.Add(review);
            if (values.Length > 0) Entries.SelectedIndex = 0;
            layout.Controls.Add(intro); layout.Controls.Add(Entries); layout.Controls.Add(footer); Controls.Add(layout); CancelButton = close;
        }
    }
    internal sealed class AppSettingsDialog : Form {
        internal readonly CheckBox Notifications = new CheckBox(), Debug = new CheckBox();
        internal AppSettings Value { get { return new AppSettings { ShowNotifications = Notifications.Checked, DebugLogging = Debug.Checked }; } }
        internal AppSettingsDialog(AppSettings settings) {
            Text = "Easy Eject settings"; Font = new Font("Segoe UI", 10); Size = new Size(650, 400); MinimumSize = new Size(600, 360); AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var body = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24) };
            Notifications.Text = "Show operation notifications"; Notifications.Checked = settings.NotificationsEnabled;
            Debug.Text = "Enable debug logging"; Debug.Checked = settings.DebugEnabled;
            foreach (var box in new[] { Notifications, Debug }) { box.AutoSize = true; box.Margin = new Padding(0, 4, 0, 14); body.Controls.Add(box); }
            var info = new Label { AutoSize = true, MaximumSize = new Size(540, 0), Text = "Notifications keep devices connected. Open Easy Eject to review completion choices. Windows notification and quiet-hour settings may hide alerts; completed imports remain available in the app.\n\nDebug logs can contain device names and file paths. They stay in local app-data and are never sent automatically." }; body.Controls.Add(info);
            body.SizeChanged += delegate { info.MaximumSize = new Size(Math.Max(250, body.ClientSize.Width - 60), 0); };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(16), FlowDirection = FlowDirection.RightToLeft };
            var save = new Button { Text = "Save settings", AutoSize = true, MinimumSize = new Size(130, 36), DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(100, 36), DialogResult = DialogResult.Cancel }; footer.Controls.Add(save); footer.Controls.Add(cancel);
            Controls.Add(body); Controls.Add(footer); AcceptButton = save; CancelButton = cancel;
        }
    }
    internal sealed class IssueDialog : Form {
        internal IssueDialog(AppIssue issue) {
            Text = "Easy Eject issue"; Font = new Font("Segoe UI", 10); Size = new Size(700, 450); MinimumSize = new Size(600, 380); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = issue.Message + Environment.NewLine + Environment.NewLine + "Dismiss acknowledges this issue. It never retries an import or eject. A later failure will restore attention." };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(16), FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(100, 36), DialogResult = DialogResult.Cancel };
            var dismiss = new Button { Text = "Dismiss issue", AutoSize = true, MinimumSize = new Size(140, 36), DialogResult = DialogResult.Yes }; footer.Controls.Add(close); footer.Controls.Add(dismiss);
            Controls.Add(text); Controls.Add(footer); AcceptButton = close; CancelButton = close;
        }
    }
    internal sealed class DiagnosticsDialog : Form {
        internal DiagnosticsDialog(string content) {
            Text = "Easy Eject debug log"; Font = new Font("Segoe UI", 10); Size = new Size(850, 580); MinimumSize = new Size(650, 420); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Text = content };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel, Dock = DockStyle.Bottom, Height = 36 }; Controls.Add(text); Controls.Add(close); CancelButton = close;
        }
    }
}
