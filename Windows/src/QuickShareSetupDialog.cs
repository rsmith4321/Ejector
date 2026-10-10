using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyEject {
    internal sealed class QuickShareSetupDialog : DpiForm {
        internal readonly TextBox Ffmpeg = new TextBox(), Ffprobe = new TextBox(), Lut = new TextBox(), DjiLut = new TextBox();
        internal readonly Button SaveSelection = new Button(), CancelSelection = new Button(), ClearLut = new Button(), ClearDjiLut = new Button(), ClearTools = new Button();
        readonly Button chooseFfmpeg = new Button(), chooseFfprobe = new Button(), chooseLut = new Button(), chooseDjiLut = new Button();
        readonly Label status = new Label(); readonly string root;
        readonly Func<string, string, string, string, CancellationToken, QuickShareConfigurationSnapshot> save;
        CancellationTokenSource cancellation; bool saving, closeAfterStop;
        internal QuickShareConfigurationSnapshot Saved { get; private set; }
        internal QuickShareSetupDialog(QuickShareConfigurationSnapshot snapshot, string configurationRoot, Func<string, string, string, CancellationToken, QuickShareConfigurationSnapshot> save = null) {
            if (snapshot == null) throw new ArgumentNullException("snapshot"); root = SafePaths.Normalize(configurationRoot); var value = snapshot.Value;
            // A supplied test save callback predates the optional DJI LUT and receives the other choices.
            if (save != null) this.save = (ffmpeg, ffprobe, lut, dji, cancel) => save(ffmpeg, ffprobe, lut, cancel);
            else this.save = (ffmpeg, ffprobe, lut, dji, cancel) => QuickShareConfiguration.Save(snapshot, ffmpeg, ffprobe, lut, root, cancel, null, dji);
            Text = "Quick Share tools"; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(720, 540); MinimumSize = new Size(660, 460); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), AutoScroll = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var intro = new Label { AutoSize = true, Text = "Select FFmpeg and FFprobe executables you supplied. Easy Eject does not download or install tools. Saving records their paths and hashes without running them. Changed tools require a new selection. Export always requires a separate manual choice.", Margin = new Padding(0, 0, 0, 16) };
            layout.Controls.Add(intro, 0, 0); layout.SetColumnSpan(intro, 2);
            Ffmpeg.Text = value.Ffmpeg == null ? "" : value.Ffmpeg.Path; Ffprobe.Text = value.Ffprobe == null ? "" : value.Ffprobe.Path; Lut.Text = value.Lut == null ? "" : value.Lut.Path; DjiLut.Text = value.DjiLut == null ? "" : value.DjiLut.Path;
            AddFileRow(layout, 1, "FFmpeg executable", Ffmpeg, chooseFfmpeg, "Choose ffmpeg.exe", "Executables (*.exe)|*.exe");
            AddFileRow(layout, 3, "FFprobe executable", Ffprobe, chooseFfprobe, "Choose ffprobe.exe", "Executables (*.exe)|*.exe");
            var lutInfo = new Label { AutoSize = true, Text = "Luna I-Log LUT (optional for normal-color exports)\n\nChoose " + QuickShareMedia.OfficialLutName + " from Insta360's official Luna LUT download. Save checks the exact official SHA-256 and copies this supplied asset into Easy Eject's local app data. Keep your downloaded original. A copied LUT remains available if the downloaded file is moved. Clearing the selection does not delete either copy.", Margin = new Padding(0, 16, 0, 8) };
            layout.Controls.Add(lutInfo, 0, 5); layout.SetColumnSpan(lutInfo, 2);
            AddFileRow(layout, 6, "Official LUT selection", Lut, chooseLut, "Choose the official Luna LUT", "LUT files (*.cube)|*.cube");
            var clearRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 8) };
            ClearLut.Text = "Clear LUT selection"; ClearLut.AutoSize = true; ClearLut.Click += delegate { Lut.Text = ""; }; clearRow.Controls.Add(ClearLut);
            ClearTools.Text = "Clear tool selections"; ClearTools.AutoSize = true; ClearTools.Click += delegate { Ffmpeg.Text = Ffprobe.Text = ""; }; clearRow.Controls.Add(ClearTools);
            var djiInfo = new Label { AutoSize = true, Text = "DJI O4 Pro D-Log M LUT (optional)\n\nChoose the O4 Air Unit Series D-Log M to Rec.709 .cube from DJI's O4 download page. Save checks its exact official SHA-256 and copies it into local app data, as above. Gyroflow exports that already carry Gyroflow's LUT marker do not need it.", Margin = new Padding(0, 16, 0, 8) };
            layout.Controls.Add(djiInfo, 0, 8); layout.SetColumnSpan(djiInfo, 2);
            AddFileRow(layout, 9, "Official DJI O4 LUT selection", DjiLut, chooseDjiLut, "Choose the official DJI O4 LUT", "LUT files (*.cube)|*.cube");
            ClearDjiLut.Text = "Clear DJI LUT selection"; ClearDjiLut.AutoSize = true; ClearDjiLut.Click += delegate { DjiLut.Text = ""; }; clearRow.Controls.Add(ClearDjiLut);
            layout.Controls.Add(clearRow, 0, 11); layout.SetColumnSpan(clearRow, 2);
            status.AutoSize = true; status.Text = "No export starts from this window. Save setup separately from the other application settings."; status.Margin = new Padding(0, 8, 0, 0); layout.Controls.Add(status, 0, 12); layout.SetColumnSpan(status, 2);
            body.Controls.Add(layout); Controls.Add(body);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16) };
            SaveSelection.Text = "Save Quick Share setup"; SaveSelection.AutoSize = true; SaveSelection.MinimumSize = new Size(180, 36);
            CancelSelection.Text = "Cancel"; CancelSelection.AutoSize = true; CancelSelection.MinimumSize = new Size(100, 36); footer.Controls.Add(SaveSelection); footer.Controls.Add(CancelSelection); Controls.Add(footer);
            SaveSelection.Click += async delegate { await SaveAsync(); };
            CancelSelection.Click += delegate { if (saving) { RequestStop(); return; } DialogResult = DialogResult.Cancel; };
            AcceptButton = CancelSelection; CancelButton = CancelSelection; CancelSelection.DialogResult = DialogResult.None;
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (saving) { e.Cancel = true; RequestStop(); } };
            body.SizeChanged += delegate { int width = Math.Max(300, body.ClientSize.Width - 60); intro.MaximumSize = lutInfo.MaximumSize = djiInfo.MaximumSize = status.MaximumSize = new Size(width, 0); };
            intro.MaximumSize = lutInfo.MaximumSize = djiInfo.MaximumSize = status.MaximumSize = new Size(590, 0);
        }
        void AddFileRow(TableLayoutPanel layout, int row, string title, TextBox field, Button choose, string pickerTitle, string filter) {
            layout.Controls.Add(new Label { AutoSize = true, Text = title, Margin = new Padding(0, 8, 0, 4) }, 0, row);
            field.ReadOnly = true; field.Dock = DockStyle.Fill; layout.Controls.Add(field, 0, row + 1);
            choose.Text = "Choose…"; choose.AutoSize = true; layout.Controls.Add(choose, 1, row + 1);
            choose.Click += delegate {
                using (var dialog = new OpenFileDialog { Title = pickerTitle, Filter = filter, CheckFileExists = true, CheckPathExists = true, Multiselect = false, RestoreDirectory = true, DereferenceLinks = false }) {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    try { field.Text = SafePaths.Normalize(dialog.FileName); status.Text = "Selection changed. Save to validate and store it; Cancel keeps the previous setup."; }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Review Quick Share file", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            };
        }
        internal void RequestStop() { closeAfterStop = true; CancelSelection.Enabled = false; status.Text = "Stopping setup validation. Waiting for the current file read/write to finish…"; if (cancellation != null) cancellation.Cancel(); }
        internal async Task SaveAsync() {
            if (saving) return; string ffmpeg = Ffmpeg.Text, ffprobe = Ffprobe.Text, lut = Lut.Text, dji = DjiLut.Text;
            saving = true; cancellation = new CancellationTokenSource(); SetEditable(false); status.Text = "Validating selected files and saving setup. No tools are running…";
            try {
                Saved = await Task.Run(() => save(ffmpeg, ffprobe, lut, dji, cancellation.Token));
                saving = false; DialogResult = DialogResult.OK; Close();
            } catch (OperationCanceledException) { status.Text = "Setup validation cancelled. Previous preferences were kept. An already copied official local LUT is retained."; }
            catch (Exception ex) { status.Text = "Setup was not confirmed. " + ex.Message; MessageBox.Show(this, status.Text, "Review Quick Share setup", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally {
                saving = false; cancellation.Dispose(); cancellation = null; SetEditable(true);
                if (closeAfterStop && Saved == null) { DialogResult = DialogResult.Cancel; Close(); }
            }
        }
        void SetEditable(bool enabled) { foreach (var button in new[] { SaveSelection, chooseFfmpeg, chooseFfprobe, chooseLut, chooseDjiLut, ClearLut, ClearDjiLut, ClearTools }) button.Enabled = enabled; CancelSelection.Enabled = true; }
    }
}
