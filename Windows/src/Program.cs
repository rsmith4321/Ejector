using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyEject {
    internal static class Program {
        [STAThread] static void Main() {
            bool created;
            using (var mutex = new Mutex(true, @"Local\EasyEject.Windows.Prototype", out created)) {
                if (!created) { MessageBox.Show("Easy Eject is already running in this session."); return; }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new DeviceWindow());
            }
        }
    }
    internal sealed class DeviceWindow : Form {
        readonly WindowsDiscovery discovery = new WindowsDiscovery(); readonly Operations operations = new Operations();
        readonly ListView list = new ListView(); readonly Label status = new Label(); readonly Button refresh = new Button(), eject = new Button(), import = new Button(), cancel = new Button(), profile = new Button(), defaults = new Button();
        readonly NotifyIcon tray; readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer();
        Inventory inventory; bool scanning, working, quit; CancellationTokenSource cancellation; IntPtr notification;
        [StructLayout(LayoutKind.Sequential)] struct BroadcastInterface { public int Size, Type, Reserved; public Guid ClassGuid; public short Name; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr RegisterDeviceNotification(IntPtr recipient, ref BroadcastInterface filter, uint flags);
        [DllImport("user32.dll")] static extern bool UnregisterDeviceNotification(IntPtr handle);
        public DeviceWindow() {
            Text = "Easy Eject · Windows prototype"; ClientSize = new Size(1000, 540); MinimumSize = new Size(800, 420); StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(16) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var intro = new Label { AutoSize = true, MaximumSize = new Size(940, 0), Text = "Review connected storage before ejecting. Imports keep all originals and verify SHA-256.\nConnection events refresh the list. Import and eject start only when you choose them.", Margin = new Padding(0, 0, 0, 12) };
            list.Dock = DockStyle.Fill; list.View = View.Details; list.FullRowSelect = true; list.MultiSelect = false; list.HideSelection = false;
            list.Columns.Add("Volume / mount", 245); list.Columns.Add("Classification", 360); list.Columns.Add("Physical disk", 130); list.Columns.Add("Status", 190);
            list.SelectedIndexChanged += delegate { UpdateActions(); };
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 8) };
            refresh.Text = "Refresh"; eject.Text = "Safe eject…"; import.Text = "Verified import…"; cancel.Text = "Cancel import"; profile.Text = "Import profile…"; defaults.Text = "Import defaults…";
            foreach (var b in new[] { refresh, eject, import, profile, defaults, cancel }) { b.AutoSize = true; b.MinimumSize = new Size(135, 36); actions.Controls.Add(b); }
            cancel.Enabled = false;
            refresh.Click += async delegate { await RefreshDevices(); }; eject.Click += async delegate { await Eject(); }; import.Click += async delegate { await Import(); };
            profile.Click += async delegate { await EditProfile(); }; defaults.Click += delegate { EditDefaults(); };
            cancel.Click += delegate { if (cancellation != null) { cancellation.Cancel(); status.Text = "Stopping after the current file check. Originals kept."; } };
            status.AutoSize = true; status.MaximumSize = new Size(940, 0); status.Text = "Reading connected devices…";
            layout.Controls.Add(intro); layout.Controls.Add(list); layout.Controls.Add(actions); layout.Controls.Add(status); Controls.Add(layout);
            var menu = new ContextMenuStrip(); menu.Items.Add("Show devices", null, delegate { Show(); WindowState = FormWindowState.Normal; Activate(); });
            menu.Items.Add("Refresh", null, async delegate { await RefreshDevices(); });
            menu.Items.Add("Quit", null, delegate { if (working) { MessageBox.Show(this, "Wait for the operation to finish or cancel the import before quitting."); return; } quit = true; Close(); });
            tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "Easy Eject Windows prototype", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); };
            debounce.Interval = 800; debounce.Tick += async delegate { debounce.Stop(); await RefreshDevices(); };
            Shown += async delegate { await RefreshDevices(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (working) { e.Cancel = true; status.Text = "Wait for completion or cancel the import before closing."; return; }
                if (!quit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
            UpdateActions();
        }
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            var filter = new BroadcastInterface { Size = Marshal.SizeOf(typeof(BroadcastInterface)), Type = 5, ClassGuid = new Guid("53f56307-b6bf-11d0-94f2-00a0c91efb8b") };
            notification = RegisterDeviceNotification(Handle, ref filter, 0);
            discovery.NotificationsAvailable = notification != IntPtr.Zero;
            if (notification == IntPtr.Zero) { status.Text = "Disk notifications unavailable. Refresh before every action."; discovery.ConnectionChanged(); }
        }
        protected override void OnHandleDestroyed(EventArgs e) { discovery.NotificationsAvailable = false; discovery.ConnectionChanged(); if (notification != IntPtr.Zero) { UnregisterDeviceNotification(notification); notification = IntPtr.Zero; } base.OnHandleDestroyed(e); }
        protected override void WndProc(ref Message m) {
            if (m.Msg == 0x219 && (m.WParam.ToInt64() == 7 || m.WParam.ToInt64() == 0x8000 || m.WParam.ToInt64() == 0x8003 || m.WParam.ToInt64() == 0x8004)) {
                discovery.ConnectionChanged(); eject.Enabled = false; import.Enabled = false; debounce.Stop(); debounce.Start();
            }
            if (m.Msg == 0x11 && working) { m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
        }
        Volume Selected { get { return list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as Volume; } }
        void UpdateActions() {
            var v = Selected; bool ready = v != null && inventory != null && !working && !scanning && inventory.Epoch == discovery.Epoch;
            eject.Enabled = false; import.Enabled = false; profile.Enabled = false; defaults.Enabled = !working; refresh.Enabled = !working && !scanning;
            if (!ready) return;
            try { EjectService.Plan(inventory, v.Id); eject.Enabled = true; } catch (SafetyException) {}
            import.Enabled = inventory.Complete && v.Error == "" && !v.System && v.Disks.Count == 1 && v.Mounts.Any(x => x.Length >= 3 && x[1] == ':') && v.Classification != "Internal / fixed storage";
            profile.Enabled = import.Enabled;
        }
        async Task RefreshDevices() {
            if (scanning || working) return; scanning = true; UpdateActions();
            try {
                var next = await Task.Run(() => discovery.Scan()); inventory = next; list.Items.Clear();
                foreach (var v in next.Volumes.OrderBy(x => x.System).ThenBy(x => x.Classification).ThenBy(x => x.Display)) {
                    var row = new ListViewItem(v.Display) { Tag = v };
                    row.SubItems.Add(v.Classification); row.SubItems.Add(String.Join(", ", v.Disks)); row.SubItems.Add(v.System ? "System: protected" : v.Error == "" ? "Connected" : v.Error); list.Items.Add(row);
                }
                int cards = next.Volumes.Where(v => v.Classification.IndexOf("candidate", StringComparison.OrdinalIgnoreCase) >= 0).SelectMany(v => v.Disks).Distinct().Count();
                tray.Text = "Easy Eject: " + cards + " candidate card disks";
                status.Text = next.Complete ? "Read-only inventory: " + next.Volumes.Count + " volumes on " + next.Disks.Count + " disks. Closing this window keeps the tray app running." : "Incomplete inventory; actions blocked. " + String.Join(" ", next.Errors);
            } catch (Exception ex) { inventory = null; status.Text = ex.Message; }
            finally { scanning = false; UpdateActions(); }
        }
        async Task Eject() {
            var v = Selected; if (v == null || working) return;
            try {
                var plan = EjectService.Plan(inventory, v.Id);
                string text = "Windows will remove this device and all affected volumes:\n\n" + String.Join("\n", plan.Affected) + "\n\nFinish all transfers first. Request safe removal?";
                if (MessageBox.Show(this, text, "Safe eject", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                working = true; UpdateActions(); status.Text = "Requesting safe removal…";
                var result = await Task.Run(() => new EjectService(discovery, new WindowsEjectApi(discovery, plan.Epoch), operations).Execute(plan));
                status.Text = result.Message;
                MessageBox.Show(this, result.Message, result.Success ? "Safe removal accepted" : "Device still connected", MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            } catch (Exception ex) { ShowError(ex); }
            finally { working = false; UpdateActions(); debounce.Start(); }
            await RefreshDevices();
        }
        async Task Import() {
            var v = Selected; if (v == null || working) return;
            try {
                var disk = inventory.Disks.Single(d => d.Number == v.Disks.Single());
                long selectionEpoch = inventory.Epoch; working = true; UpdateActions();
                string root = v.Mounts.First(x => x.Length >= 3 && x[1] == ':');
                ImportDefaults choices = ReadDefaults(); ImportProfile stored = ReadProfile(v, disk);
                if (stored == null) {
                    stored = await SetUpProfile(v, disk, root, selectionEpoch, choices, null);
                    if (stored == null) return;
                    // Setup may have edited shared defaults. Freeze the current choices once here.
                    choices = ReadDefaults();
                }
                MediaOptions options = stored.Resolve(choices);
                string source = SafePaths.Normalize(Path.Combine(root, stored.RelativeSource)), destination = SafePaths.Normalize(stored.Destination);
                if (!SafePaths.Within(source, root)) throw new SafetyException("Choose a folder within the selected device volume.");
                var guard = await Task.Run(() => new VolumeImportGuard(discovery, source, destination, v, disk, selectionEpoch));
                string text = (options.VideosOnly ? "Copy selected videos and their required companions under:\n" : "Copy all media and sidecars under:\n") + source + "\n\nTo a new dated batch under:\n" + destination + "\n\n" + (options.IncludePreviews ? "Camera previews included." : "Optional previews stay on the device; required package files are included.") + "\nRelative paths and complete camera packages are retained. SHA-256 verifies each copy. Originals and the device stay connected.\n\nStart this manual import?";
                if (MessageBox.Show(this, text, "Verified import", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                working = true; cancellation = new CancellationTokenSource(); cancel.Enabled = true; UpdateActions();
                var result = await Task.Run(() => new VerifiedImporter(operations).Run(root, source, destination, v.Id, guard, cancellation.Token, message => {
                    if (!IsDisposed) BeginInvoke(new Action(() => status.Text = message));
                }, options));
                MessageBox.Show(this, result.Files == 0 ? "No matching media or device indexes were found. No batch was created. Device remains connected." : (result.HasSelectedMedia ? "Import complete. " : "No matching media. Device index backup only. ") + result.Files + " files verified (" + result.Bytes + " bytes).\n" + result.Folder + "\n\nOriginals kept. Device remains connected.", result.HasSelectedMedia ? "Import complete" : "No matching media");
            } catch (OperationCanceledException) { status.Text = "Import cancelled. All originals and completed copies kept. No completion receipt was written."; MessageBox.Show(this, status.Text, "Import cancelled"); }
            catch (Exception ex) { ShowError(ex); }
            finally { working = false; cancel.Enabled = false; if (cancellation != null) { cancellation.Dispose(); cancellation = null; } UpdateActions(); debounce.Start(); }
            await RefreshDevices();
        }
        static bool ConfigurationError(Exception ex) {
            return ex is SafetyException || ex is System.Runtime.Serialization.SerializationException || ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception;
        }
        ImportDefaults ReadDefaults() {
            try { return ImportDefaults.Load(); }
            catch (Exception ex) {
                if (!ConfigurationError(ex)) throw;
                MessageBox.Show(this, "Saved defaults could not be read. Videos only with previews will be used until you save defaults again.\n\n" + ex.Message, "Import defaults", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return new ImportDefaults();
            }
        }
        ImportProfile ReadProfile(Volume volume, Disk disk) {
            try { return Profiles.Load(volume, disk); }
            catch (Exception ex) {
                if (!ConfigurationError(ex)) throw;
                MessageBox.Show(this, "Saved folder choices could not be used. Set up this device again; saving replaces its old profile.\n\n" + ex.Message, "Import profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }
        }
        void EditDefaults() {
            if (working) return;
            try {
                working = true; UpdateActions();
                using (var dialog = new ImportDefaultsDialog(ReadDefaults())) {
                    if (dialog.ShowDialog(this) == DialogResult.OK) { ImportDefaults.Save(dialog.Value); status.Text = "Import defaults saved. They apply on the next import; no import started."; }
                }
            } catch (Exception ex) { ShowError(ex); }
            finally { working = false; UpdateActions(); debounce.Start(); }
        }
        async Task EditProfile() {
            var volume = Selected; if (volume == null || working) return;
            try {
                var disk = inventory.Disks.Single(d => d.Number == volume.Disks.Single()); long epoch = inventory.Epoch;
                working = true; UpdateActions();
                string root = volume.Mounts.First(x => x.Length >= 3 && x[1] == ':');
                await SetUpProfile(volume, disk, root, epoch, ReadDefaults(), ReadProfile(volume, disk));
            } catch (Exception ex) { ShowError(ex); }
            finally { working = false; UpdateActions(); debounce.Start(); }
        }
        async Task<ImportProfile> SetUpProfile(Volume volume, Disk disk, string root, long epoch, ImportDefaults choices, ImportProfile stored) {
            using (var dialog = new ImportProfileDialog(stored ?? choices.NewProfile(volume, disk), root, volume.Display, choices, stored != null)) {
                DialogResult result = dialog.ShowDialog(this);
                if (result == DialogResult.Retry) {
                    if (MessageBox.Show(this, "Forget this device's saved folders and import choices? Files and originals stay intact.", "Forget import profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes) {
                        Profiles.Remove(volume, disk); status.Text = "Import profile forgotten. No files were imported or removed.";
                    }
                    return null;
                }
                if (result != DialogResult.OK) return null;
                ImportProfile draft = dialog.Draft;
                string source = SafePaths.Normalize(Path.Combine(root, draft.RelativeSource));
                await Task.Run(() => { using (SafePaths.LockDirectories(source, draft.Destination)) { var guard = new VolumeImportGuard(discovery, source, draft.Destination, volume, disk, epoch); guard.Validate(); } });
                Profiles.Save(draft); status.Text = "Import profile saved. No import started. Choose Verified import when ready.";
                return draft;
            }
        }
        void ShowError(Exception ex) { status.Text = ex.Message; MessageBox.Show(this, ex.Message, "Easy Eject", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        protected override void Dispose(bool disposing) { if (disposing) { tray.Dispose(); debounce.Dispose(); } base.Dispose(disposing); }
    }
}
