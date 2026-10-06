using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace EasyEject {
    internal sealed class ImportCompletionDialog : Form {
        internal readonly Button Keep = new Button(), Eject = new Button(), OpenFolder = new Button(), Share = new Button();
        internal readonly Label Explanation = new Label();
        bool expired;
        internal ImportCompletionDialog(ImportCompletion entry, bool canEject, string cleanupText = "", bool canShare = false, string shareMessage = "") {
            Text = entry.HasMedia ? "Import complete. Eject now?" : "No matching media. Eject now?";
            Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi; Size = new Size(740, 500); MinimumSize = new Size(680, 440); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24) };
            var plan = entry.Origin.Plan;
            Explanation.AutoSize = true; Explanation.MaximumSize = new Size(630, 0); Explanation.Location = new Point(24, 24);
            Explanation.Text = entry.DeviceName + Environment.NewLine + Environment.NewLine + entry.Message
                + (entry.HasMedia ? "" : "\n\nPhotos, skipped previews or other files may still be on the device.")
                + "\n\nChoose Keep Connected for Lightroom or other transfers. Eject Now requests Windows safe removal of this device and all affected partitions/storage sources."
                + (plan == null ? "\n\n" + entry.Origin.BlockedReason : "\n\nAffected storage:\n" + String.Join("\n", plan.Affected))
                + (cleanupText == "" ? "" : "\n\n" + cleanupText)
                + (canShare ? "\n\nQuick Share creates validated copies from this exact saved import. It asks size and LUT choice and never ejects automatically." : "")
                + (shareMessage == "" ? "" : "\n\n" + shareMessage)
                + "\n\nSaved folder:\n" + entry.Folder;
            body.Controls.Add(Explanation); body.SizeChanged += delegate { Explanation.MaximumSize = new Size(Math.Max(250, body.ClientSize.Width - 60), 0); };
            Keep.Text = "Keep Connected"; Keep.DialogResult = DialogResult.Cancel;
            Eject.Text = cleanupText == "" ? plan != null && plan.Affected.Length > 1 ? "Eject Device" : "Eject Now" : "Clean && Eject"; Eject.DialogResult = DialogResult.Yes; Eject.Enabled = canEject;
            OpenFolder.Text = "Open Import Folder"; OpenFolder.DialogResult = DialogResult.Ignore;
            Share.Text = "Quick Share…"; Share.DialogResult = DialogResult.Retry; Share.Visible = Share.Enabled = canShare && entry.Batch != null;
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(16), FlowDirection = FlowDirection.RightToLeft };
            foreach (var button in new[] { Keep, Eject, OpenFolder, Share }) { button.AutoSize = true; button.MinimumSize = new Size(150, 36); footer.Controls.Add(button); }
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
        internal readonly CheckBox Notifications = new CheckBox(), Debug = new CheckBox(), Shortcut = new CheckBox(), Cleanup = new CheckBox(), WarnOther = new CheckBox();
        internal readonly ComboBox ShortcutLetter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
        internal readonly CheckBox Login = new CheckBox(); internal readonly Button UseThisCopy = new Button();
        internal readonly Label LoginStatus = new Label(); readonly StartupSnapshot startup; bool retarget;
        internal readonly Button ShareTools = new Button();
        internal bool? LoginChange { get { if (startup == null || !startup.Available || startup.Conflict) return null; if (retarget && Login.Checked) return true; return Login.Checked == startup.Owned ? (bool?)null : Login.Checked; } }
        internal AppSettings Value { get { return new AppSettings { ShowNotifications = Notifications.Checked, DebugLogging = Debug.Checked, EnableShortcut = Shortcut.Checked, ShortcutLetter = ShortcutLetter.SelectedItem as string, CleanCardsOnEject = Cleanup.Checked, WarnOtherStorage = WarnOther.Checked }; } }
        internal AppSettingsDialog(AppSettings settings, string shortcutStatus = null, StartupSnapshot startup = null, Action reviewStartup = null, Action configureShareTools = null) {
            this.startup = startup;
            Text = "Easy Eject settings"; Font = new Font("Segoe UI", 10); Size = new Size(650, 400); MinimumSize = new Size(600, 360); AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var body = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24) };
            Login.Text = "Launch at login (register for this Windows user)"; Login.AutoSize = true; Login.Checked = startup != null && startup.Owned; Login.Enabled = startup != null && startup.Available && !startup.Conflict; Login.Margin = new Padding(0, 4, 0, 10); body.Controls.Add(Login);
            LoginStatus.AutoSize = true; LoginStatus.MaximumSize = new Size(520, 0); LoginStatus.Text = startup == null ? "Login registration is not checked in this read-only/test window." : startup.Message; LoginStatus.Margin = new Padding(0, 0, 0, 10); body.Controls.Add(LoginStatus);
            var loginActions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 16) };
            var review = new Button { Text = "Windows Startup apps…", AutoSize = true, Enabled = reviewStartup != null }; review.Click += delegate { try { if (reviewStartup != null) reviewStartup(); } catch (Exception ex) { LoginStatus.Text = "Windows Startup apps could not be opened. " + ex.Message; } }; loginActions.Controls.Add(review);
            UseThisCopy.Text = "Use this copy at login"; UseThisCopy.AutoSize = true; UseThisCopy.Visible = startup != null && startup.Owned && !startup.Here; UseThisCopy.Click += delegate { RetargetLogin(); }; loginActions.Controls.Add(UseThisCopy); body.Controls.Add(loginActions);
            Notifications.Text = "Show operation notifications"; Notifications.Checked = settings.NotificationsEnabled;
            Debug.Text = "Enable debug logging"; Debug.Checked = settings.DebugEnabled;
            Shortcut.Text = "Enable global card-removal shortcut"; Shortcut.Checked = settings.ShortcutEnabled;
            Cleanup.Text = "Clean macOS metadata from cards before ejecting"; Cleanup.Checked = settings.CleanupCardsEnabled;
            WarnOther.Text = "Warn when ejecting other external storage"; WarnOther.Checked = settings.WarnOtherEnabled;
            foreach (var box in new[] { Notifications, Cleanup, WarnOther, Debug, Shortcut }) { box.AutoSize = true; box.Margin = new Padding(0, 4, 0, 14); body.Controls.Add(box); }
            var shortcutRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            shortcutRow.Controls.Add(new Label { Text = "Ctrl + Alt + Shift +", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            ShortcutLetter.Items.AddRange(GlobalHotkey.Letters); ShortcutLetter.SelectedItem = settings.ShortcutKey; ShortcutLetter.Enabled = Shortcut.Checked;
            Shortcut.CheckedChanged += delegate { ShortcutLetter.Enabled = Shortcut.Checked; }; shortcutRow.Controls.Add(ShortcutLetter); body.Controls.Add(shortcutRow);
            if (shortcutStatus != null) body.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(500, 0), Text = shortcutStatus, Margin = new Padding(0, 8, 0, 8) });
            var info = new Label { AutoSize = true, MaximumSize = new Size(540, 0), Text = "Login launches Easy Eject in the tray, without opening its device window. It does not enable automatic import: that remains a separate profile/default choice. An enabled import may run when the app launches. Saving login registration starts no app or import. If you move this copy, explicitly update its registration.\n\nThe shortcut opens the same confirmation as Eject cards. It stays off until enabled. A shortcut already used by another app is reported unavailable; no other key is registered.\n\nWindows notification and quiet-hour settings may hide alerts; completed imports remain available in the app. Debug logs can contain device names and file paths. They stay in local app-data and are never sent automatically." }; body.Controls.Add(info);
            ShareTools.Text = "Quick Share tools…"; ShareTools.AutoSize = true; ShareTools.Enabled = configureShareTools != null; ShareTools.Margin = new Padding(0, 16, 0, 8); ShareTools.Click += delegate { if (configureShareTools != null) configureShareTools(); }; body.Controls.Add(ShareTools);
            body.SizeChanged += delegate { info.MaximumSize = LoginStatus.MaximumSize = new Size(Math.Max(250, body.ClientSize.Width - 60), 0); };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(16), FlowDirection = FlowDirection.RightToLeft };
            var save = new Button { Text = "Save settings", AutoSize = true, MinimumSize = new Size(130, 36), DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(100, 36), DialogResult = DialogResult.Cancel }; footer.Controls.Add(save); footer.Controls.Add(cancel);
            Controls.Add(body); Controls.Add(footer); AcceptButton = save; CancelButton = cancel;
        }
        internal void RetargetLogin() { if (startup == null || !startup.Owned || startup.Here) return; retarget = true; Login.Checked = true; LoginStatus.Text = "Save settings to register this copy. The other copy's registration is replaced only after a fresh status check."; }
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
        readonly TextBox text;
        readonly Label outcome;
        readonly Button clear, copy, refresh;
        readonly Func<DebugLogSnapshot> load;
        readonly Action<DebugLogSnapshot> clearLog;
        readonly Action<string> copyText;
        DebugLogSnapshot snapshot;
        internal string DisplayedText { get { return text.Text; } }
        internal string ActionStatus { get { return outcome.Text; } }
        internal DiagnosticsDialog(string content) : this(null, null, null, null) { text.Text = content; }
        internal DiagnosticsDialog(DebugLogSnapshot initial, Func<DebugLogSnapshot> load, Action<DebugLogSnapshot> clearLog, Action<string> copyText) {
            snapshot = initial; this.load = load; this.clearLog = clearLog; this.copyText = copyText;
            Text = "Easy Eject debug log"; Font = new Font("Segoe UI", 10); Size = new Size(850, 580); MinimumSize = new Size(650, 420); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            text = new TextBox { Name = "logText", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Text = initial == null ? "" : initial.Text };
            outcome = new Label { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(12, 4, 12, 4) };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12), FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Name = "close", Text = "Close", AutoSize = true, MinimumSize = new Size(95, 36), DialogResult = DialogResult.Cancel };
            copy = new Button { Name = "copy", Text = "Copy to Clipboard", AutoSize = true, MinimumSize = new Size(150, 36) };
            refresh = new Button { Name = "refresh", Text = "Refresh", AutoSize = true, MinimumSize = new Size(95, 36) };
            clear = new Button { Name = "clear", Text = "Clear log", AutoSize = true, MinimumSize = new Size(95, 36) };
            footer.Controls.AddRange(new Control[] { close, copy, refresh, clear }); Controls.Add(text); Controls.Add(outcome); Controls.Add(footer);
            copy.Click += delegate { RequestCopy(); }; refresh.Click += delegate { RequestRefresh(); }; clear.Click += delegate { RequestClear(); };
            AcceptButton = close; CancelButton = close; UpdateChoices();
        }
        void UpdateChoices() { copy.Enabled = copyText != null && snapshot != null && snapshot.Exists; refresh.Enabled = load != null; clear.Enabled = load != null && clearLog != null && snapshot != null && snapshot.Exists; }
        void Reload() { var current = load(); if (current == null) throw new SafetyException("Debug log could not be refreshed."); snapshot = current; text.Text = current.Text; UpdateChoices(); }
        internal void RequestRefresh() {
            if (!refresh.Enabled) return;
            try { Reload(); outcome.Text = "Current debug log displayed."; } catch (Exception ex) { outcome.Text = ex.Message; }
        }
        internal void RequestCopy() {
            if (!copy.Enabled) return;
            try { copyText(snapshot.Exists ? snapshot.Text : ""); outcome.Text = "Displayed debug log copied to Clipboard."; } catch (Exception ex) { outcome.Text = "Clipboard copy failed: " + ex.Message; }
        }
        internal void RequestClear() {
            if (!clear.Enabled) return;
            try { clearLog(snapshot); Reload(); outcome.Text = "Debug log cleared."; } catch (Exception ex) { outcome.Text = ex.Message; }
        }
    }
}
