using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyEject {
    internal static class Program {
        [STAThread] static void Main(string[] args) {
            bool startup; try { startup = StartupLaunch.Parse(args); } catch (Exception ex) { MessageBox.Show(ex.Message, "Easy Eject"); return; }
            bool created;
            using (var mutex = new Mutex(true, @"Local\EasyEject.Windows.Prototype", out created)) {
                if (!created) { if (!startup) MessageBox.Show("Easy Eject is already running in this session."); return; }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new DeviceWindow(false, startup));
            }
        }
    }
    internal sealed class DeviceWindow : Form {
        readonly WindowsDiscovery discovery = new WindowsDiscovery(); readonly Operations operations = new Operations();
        readonly ImportCompletionRegistry completions = new ImportCompletionRegistry();
        readonly AutomaticImportScheduler automatic = new AutomaticImportScheduler();
        bool automaticPumping, createdBefore; long automaticConfigIssueEpoch = -1;
        readonly StartupPresentation startupPresentation; bool monitoringStarted;
        AppSettings appSettings = new AppSettings(); AppIssue appIssue = new AppIssue(); ImportCompletionDialog activeCompletion;
        ToolStripMenuItem pendingMenu, issueMenu, settingsMenu, debugMenu, bulkMenu, savedProfilesMenu;
        readonly GlobalHotkey hotkey = new GlobalHotkey(new WindowsHotkeyApi()); readonly bool readOnly;
        int cardCount;
        readonly Button resume = new Button();
        readonly Button savedProfiles = new Button();
        readonly ListView list = new ListView(); readonly Label status = new Label(); readonly Button refresh = new Button(), eject = new Button(), cleanEject = new Button(), bulk = new Button(), import = new Button(), cancel = new Button(), profile = new Button(), defaults = new Button(), history = new Button(), pending = new Button(), settings = new Button(), issue = new Button();
        readonly NotifyIcon tray; readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer();
        Inventory inventory; bool scanning, working, quit; CancellationTokenSource cancellation; IntPtr notification;
        [StructLayout(LayoutKind.Sequential)] struct BroadcastInterface { public int Size, Type, Reserved; public Guid ClassGuid; public short Name; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr RegisterDeviceNotification(IntPtr recipient, ref BroadcastInterface filter, uint flags);
        [DllImport("user32.dll")] static extern bool UnregisterDeviceNotification(IntPtr handle);
        public DeviceWindow() : this(false) {}
        internal DeviceWindow(bool readOnly) : this(readOnly, false) {}
        internal DeviceWindow(bool readOnly, bool startInTray) {
            this.readOnly = readOnly;
            startupPresentation = new StartupPresentation(startInTray);
            try { appSettings = AppSettings.Load(); appIssue = AppIssue.Load(); } catch (Exception ex) { appIssue.Record("Saved application state could not be read: " + ex.Message); }
            if (readOnly) appSettings = new AppSettings();
            Text = "Easy Eject · Windows prototype"; ClientSize = new Size(1000, 600); MinimumSize = new Size(800, 500); StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(16) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var intro = new Label { AutoSize = true, MaximumSize = new Size(940, 0), Text = "Review connected storage before ejecting. Imports retain originals by default and verify SHA-256.\nAutomatic import starts off. Enable it in a profile or shared defaults. Eject and sharing require a manual choice.", Margin = new Padding(0, 0, 0, 12) };
            list.Dock = DockStyle.Fill; list.View = View.Details; list.FullRowSelect = true; list.MultiSelect = false; list.HideSelection = false;
            list.Columns.Add("Volume / mount", 245); list.Columns.Add("Classification", 360); list.Columns.Add("Physical disk", 130); list.Columns.Add("Status", 190);
            foreach (StorageGroup group in Enum.GetValues(typeof(StorageGroup))) list.Groups.Add(new ListViewGroup(group.ToString(), GroupLabel(group)));
            list.SelectedIndexChanged += delegate { UpdateActions(); };
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 8) };
            refresh.Text = "Refresh"; eject.Text = "Safe eject…"; import.Text = "Verified import…"; cancel.Text = "Cancel operation"; profile.Text = "Import profile…"; defaults.Text = "Import defaults…"; history.Text = "Import history…";
            pending.Text = "Completed imports…"; settings.Text = "Settings…"; issue.Text = "View issue…"; bulk.Text = "Eject cards…"; cleanEject.Text = "Clean && eject…";
            resume.Text = "Resume interrupted import…";
            savedProfiles.Text = "Saved profiles…";
            foreach (var b in new[] { refresh, eject, cleanEject, bulk, import, resume, profile, savedProfiles, defaults, history, pending, settings, issue, cancel }) { b.AutoSize = true; b.MinimumSize = new Size(135, 36); actions.Controls.Add(b); }
            cancel.Enabled = false;
            refresh.Click += async delegate { await RefreshDevices(); }; eject.Click += async delegate { await Eject(); }; import.Click += async delegate { await Import(); };
            bulk.Click += async delegate { await EjectCards(); };
            cleanEject.Click += async delegate { await Eject(true); };
            profile.Click += async delegate { await EditProfile(); }; defaults.Click += delegate { EditDefaults(); };
            savedProfiles.Click += delegate { EditSavedProfiles(); };
            history.Click += async delegate { await ReviewHistory(); };
            resume.Click += async delegate { await ResumeImport(); };
            pending.Click += async delegate { await ReviewCompletions(); }; settings.Click += delegate { EditSettings(); }; issue.Click += delegate { ViewIssue(); };
            cancel.Click += delegate { if (cancellation != null) { cancellation.Cancel(); status.Text = "Stopping after the current check. Verified copies and remaining source data are kept."; } };
            status.AutoSize = true; status.MaximumSize = new Size(940, 0); status.Text = "Reading connected devices…";
            layout.Controls.Add(intro); layout.Controls.Add(list); layout.Controls.Add(actions); layout.Controls.Add(status); Controls.Add(layout);
            layout.SizeChanged += delegate { int width = Math.Max(350, layout.ClientSize.Width - layout.Padding.Horizontal - 8); intro.MaximumSize = status.MaximumSize = new Size(width, 0); };
            var menu = new ContextMenuStrip(); menu.Items.Add("Show devices", null, delegate { Show(); WindowState = FormWindowState.Normal; Activate(); });
            menu.Items.Add("Refresh", null, async delegate { await RefreshDevices(); });
            bulkMenu = new ToolStripMenuItem("Eject cards…", null, async delegate { await EjectCards(); }); menu.Items.Add(bulkMenu);
            pendingMenu = new ToolStripMenuItem("Completed imports…", null, async delegate { await ReviewCompletions(); }); menu.Items.Add(pendingMenu);
            settingsMenu = new ToolStripMenuItem("Settings…", null, delegate { EditSettings(); }); menu.Items.Add(settingsMenu);
            savedProfilesMenu = new ToolStripMenuItem("Saved profiles…", null, delegate { EditSavedProfiles(); }); menu.Items.Add(savedProfilesMenu);
            issueMenu = new ToolStripMenuItem("View issue…", null, delegate { ViewIssue(); }); menu.Items.Add(issueMenu);
            debugMenu = new ToolStripMenuItem("View debug log…", null, delegate { ViewDebugLog(); }); menu.Items.Add(debugMenu);
            menu.Items.Add("Quit", null, delegate { if (working) { MessageBox.Show(this, "Wait for the operation to finish or cancel the import before quitting."); return; } quit = true; Close(); });
            tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "Easy Eject Windows prototype", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); };
            // Balloon clicks carry no device token. They only reveal the app for a fresh review.
            tray.BalloonTipClicked += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); };
            menu.Opening += delegate { UpdateActions(); };
            debounce.Interval = 800; debounce.Tick += async delegate { debounce.Stop(); await RefreshDevices(); };
            Shown += delegate { StartMonitoring(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (!quit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
                if (working) { e.Cancel = true; status.Text = "Wait for completion or cancel the import before closing."; return; }
            };
            UpdateActions();
        }
        async void StartMonitoring() { if (monitoringStarted) return; monitoringStarted = true; await RefreshDevices(); }
        protected override void SetVisibleCore(bool value) {
            if (value && startupPresentation != null && !startupPresentation.AllowShow(() => { IntPtr handle = Handle; }, () => BeginInvoke(new Action(StartMonitoring)))) { base.SetVisibleCore(false); return; }
            base.SetVisibleCore(value);
        }
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            var filter = new BroadcastInterface { Size = Marshal.SizeOf(typeof(BroadcastInterface)), Type = 5, ClassGuid = new Guid("53f56307-b6bf-11d0-94f2-00a0c91efb8b") };
            notification = RegisterDeviceNotification(Handle, ref filter, 0);
            discovery.NotificationsAvailable = notification != IntPtr.Zero;
            discovery.Instances.Start();
            if (createdBefore) automatic.SettingsSaved(); createdBefore = true;
            if (!readOnly && !hotkey.Configure(Handle, appSettings.ShortcutEnabled, appSettings.ShortcutKey)) appIssue.Record(hotkey.Status);
            if (notification == IntPtr.Zero) { status.Text = "Disk notifications unavailable. Refresh before every action."; discovery.ConnectionChanged(); }
        }
        protected override void OnHandleDestroyed(EventArgs e) { hotkey.Stop(); hotkey.WindowDestroyed(); discovery.Instances.Stop(); discovery.NotificationsAvailable = false; discovery.ConnectionChanged(); ExpireCompletions(); if (notification != IntPtr.Zero) { UnregisterDeviceNotification(notification); notification = IntPtr.Zero; } base.OnHandleDestroyed(e); }
        protected override void WndProc(ref Message m) {
            if (m.Msg == 0x312 && hotkey.Matches(m.WParam, m.LParam, working || scanning || readOnly)) {
                IntPtr triggerId = m.WParam, triggerKeys = m.LParam;
                BeginInvoke(new Action(async delegate { if (hotkey.Matches(triggerId, triggerKeys, working || scanning || readOnly)) await EjectCards(); })); m.Result = IntPtr.Zero; return;
            }
            if (m.Msg == 0x219 && (m.WParam.ToInt64() == 7 || m.WParam.ToInt64() == 0x8000 || m.WParam.ToInt64() == 0x8003 || m.WParam.ToInt64() == 0x8004)) {
                discovery.ConnectionChanged(); eject.Enabled = false; import.Enabled = false; debounce.Stop(); debounce.Start();
                ExpireCompletions();
                UpdateActions();
            }
            if (m.Msg == 0x11 && working) { m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
        }
        Volume Selected { get { return list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as Volume; } }
        static string GroupLabel(StorageGroup group) { switch (group) { case StorageGroup.Camera: return "Camera cards (candidates)"; case StorageGroup.Emulator: return "Emulator cards (candidates)"; case StorageGroup.External: return "Other external volumes"; case StorageGroup.Internal: return "Internal storage"; case StorageGroup.Protected: return "Windows storage (protected)"; default: return "Unknown mapping"; } }
        void UpdateActions() {
            var v = Selected; bool ready = v != null && inventory != null && !working && !scanning && inventory.Epoch == discovery.Epoch;
            eject.Enabled = cleanEject.Enabled = false; import.Enabled = false; profile.Enabled = false; defaults.Enabled = !working; history.Enabled = !working && !scanning; refresh.Enabled = !working && !scanning;
            resume.Enabled = false;
            savedProfiles.Enabled = savedProfilesMenu.Enabled = !working && !scanning && !readOnly;
            settings.Enabled = pending.Enabled = issue.Enabled = !working && !scanning;
            pending.Enabled &= completions.Entries.Length > 0; issue.Enabled &= appIssue.HasIssue;
            pendingMenu.Enabled = pending.Enabled; issueMenu.Enabled = issue.Enabled; settingsMenu.Enabled = settings.Enabled; debugMenu.Visible = appSettings.DebugEnabled; debugMenu.Enabled = !working;
            tray.Icon = appIssue.NeedsAttention ? SystemIcons.Warning : SystemIcons.Information;
            tray.Text = appIssue.NeedsAttention ? "Easy Eject: needs attention. Choose View issue." : "Easy Eject: " + cardCount + " candidate card disks";
            bulk.Enabled = !readOnly && !working && !scanning && inventory != null && inventory.Epoch == discovery.Epoch && inventory.Volumes.Any(x => x.IsCard);
            bulkMenu.Enabled = bulk.Enabled;
            if (!ready) return;
            try { EjectService.Plan(inventory, v.Id); eject.Enabled = true; } catch (SafetyException) {}
            if (eject.Enabled) try { MetadataCleanupPlan.Single(inventory, EjectService.Plan(inventory, v.Id), v.Id); cleanEject.Enabled = !readOnly; } catch (SafetyException) {}
            import.Enabled = !readOnly && inventory.Complete && v.Error == "" && !v.System && v.Disks.Count == 1 && v.Mounts.Any(x => x.Length >= 3 && x[1] == ':') && v.Classification != "Internal / fixed storage";
            profile.Enabled = import.Enabled;
            resume.Enabled = import.Enabled && !readOnly;
        }
        async Task RefreshDevices() {
            if (scanning || working) return; scanning = true; UpdateActions();
            try {
                var next = await Task.Run(() => discovery.Scan()); PresentInventory(next); automatic.Observe(next);
                status.Text = next.Complete ? "Read-only inventory: " + next.Volumes.Count + " volumes on " + next.Disks.Count + " disks. Closing this window keeps the tray app running." : "Incomplete inventory; actions blocked. " + String.Join(" ", next.Errors);
            } catch (Exception ex) { inventory = null; status.Text = ex.Message; }
            finally { scanning = false; UpdateActions(); }
            await PumpAutomaticImports();
        }
        async Task PumpAutomaticImports() {
            if (automaticPumping || readOnly || working || scanning || inventory == null || inventory.Epoch != discovery.Epoch) return;
            automaticPumping = true;
            try {
                while (!readOnly && !working && !scanning && inventory != null && inventory.Complete && inventory.Epoch == discovery.Epoch) {
                    ImportDefaults choices; SavedProfile[] entries;
                    try { choices = ImportDefaults.Load(); entries = Profiles.Catalog(); }
                    catch (Exception ex) {
                        if (automaticConfigIssueEpoch != inventory.Epoch) { automaticConfigIssueEpoch = inventory.Epoch; ShowError(new SafetyException("Automatic import could not read saved choices. No import started. " + ex.Message)); }
                        break;
                    }
                    if (entries.Any(e => !e.CanEdit && inventory.Volumes.Any(v => Profiles.FileName(v.Id) == e.FileName)) && automaticConfigIssueEpoch != inventory.Epoch) {
                        automaticConfigIssueEpoch = inventory.Epoch; ShowError(new SafetyException("A connected device's saved profile could not be read and was left intact. Review Saved profiles or set up the connected device again. No automatic import starts from that invalid record."));
                    }
                    var request = automatic.Take(inventory, entries.Where(x => x.CanEdit).Select(x => x.Profile), choices, working || scanning);
                    if (request == null) break;
                    await RunAutomaticImport(request);
                    await RefreshDevices();
                }
            } finally { automaticPumping = false; }
        }
        async Task RunAutomaticImport(AutomaticImportRequest request) {
            bool engineStarted = false;
            try {
                working = true; UpdateActions();
                var connection = request.Connection; var stored = request.Profile;
                request.Connection.RequireCurrent(inventory);
                if (stored.VolumeId != connection.Volume.Id || stored.VolumeSerial != connection.Volume.Serial || stored.DiskInstance != connection.Disk.InstanceId) throw new SafetyException("The automatic profile no longer matches this card. Connect the original card or enroll the formatted device again. No import started.");
                Profiles.ValidateStoredFolders(stored);
                string source = SafePaths.Normalize(Path.Combine(connection.Root, stored.RelativeSource)), destination = SafePaths.Normalize(stored.Destination);
                if (!SafePaths.Within(source, connection.Root)) throw new SafetyException("The saved automatic media folder is outside this device.");
                cancellation = new CancellationTokenSource(); cancel.Enabled = true; status.Text = "Preparing automatic import for " + ProfileName.Display(stored) + "…";
                var guard = await Task.Run(() => new VolumeImportGuard(discovery, source, destination, connection.Volume, connection.Disk, connection.Epoch));
                request.ValidateAdmission(inventory, guard.DestinationIdentity); guard.Validate();
                completions.InvalidateSource(connection.Root); Log("Automatic connection import | " + ProfileName.Display(stored) + " | " + source + " -> " + destination);
                engineStarted = true;
                var result = await Task.Run(() => new VerifiedImporter(operations).Run(connection.Root, source, destination, stored.VolumeId, guard, cancellation.Token, text => { if (!IsDisposed) BeginInvoke(new Action(() => status.Text = text)); }, request.Options));
                await PresentImportResult(result, stored, connection.Volume, guard, connection.Root, destination, request.Options);
            } catch (OperationCanceledException) {
                status.Text = "Automatic import cancelled. Remaining originals/Trash data and verified copies are kept. No automatic retry occurs on this connection."; Log(status.Text); Notify("Automatic import cancelled", status.Text, ToolTipIcon.Info);
            } catch (Exception ex) {
                if (!engineStarted) automatic.PreflightWaiting(request);
                ShowError(ex);
            } finally { FinishOperation(); }
        }
        internal void PresentInventory(Inventory next) {
            inventory = next; list.Items.Clear();
            foreach (var v in next.Volumes.OrderBy(x => x.Group).ThenBy(x => x.Display)) {
                var row = new ListViewItem(v.Display) { Tag = v, Group = list.Groups[v.Group.ToString()] };
                row.SubItems.Add(v.Classification); row.SubItems.Add(String.Join(", ", v.Disks.Select(n => { var disk = next.Disks.SingleOrDefault(d => d.Number == n); return n + (disk == null || disk.Model == "" ? "" : " · " + disk.Model); }))); row.SubItems.Add(v.System ? "System: protected" : v.Error == "" ? "Connected" : v.Error); list.Items.Add(row);
            }
            cardCount = next.Disks.Where(d => next.Volumes.Any(v => v.IsCard && v.Disks.Contains(d.Number))).Select(d => d.TargetId == "" ? "disk:" + d.Number : d.TargetId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        }
        async Task EjectCards() {
            if (working || scanning || readOnly) return;
            working = true; UpdateActions();
            try {
                var state = await Task.Run(() => discovery.Scan()); var plan = BulkEjectPolicy.Plan(state);
                if (plan.Targets.Length == 0) { status.Text = "No camera or emulator cards are connected."; return; }
                bool clean = appSettings.CleanupCardsEnabled; var cleanPlans = clean ? plan.Targets.Select(t => MetadataCleanupPlan.Bulk(state, t)).ToArray() : new MetadataCleanupPlan[0];
                using (var dialog = new BulkEjectDialog(plan, String.Join("\r\n\r\n", cleanPlans.Select(p => p.Description)))) if (dialog.ShowDialog(this) != DialogResult.Yes) return;
                completions.InvalidateAll(); status.Text = "Requesting and verifying grouped card removal…"; Log("User confirmed grouped card removal | " + plan.Description);
                var service = new BulkEjectService(discovery, new WindowsBulkConnections(discovery), operations, validate => new WindowsEjectApi(validate));
                var counts = new CleanupCounts(); if (clean) { cancellation = new CancellationTokenSource(); cancel.Enabled = true; }
                var result = await Task.Run(() => service.Execute(plan, initial => { foreach (var selection in cleanPlans) selection.Preflight(initial, WindowsDiscovery.VolumeId); },
                    (target, validate) => { if (clean) cleanPlans.Single(p => p.TargetId == target.Id).Clean(validate, WindowsDiscovery.VolumeId, cancellation.Token, counts, ReportCleanup); }, clean ? cancellation.Token : CancellationToken.None));
                if (clean) result.CleanupMessage = counts.Message;
                if (result.Success) ClearIssue(); else RecordIssue(result.Message);
                status.Text = result.Summary; Notify(result.Success ? "Cards safely removed" : "Card removal stopped", result.Summary + " Open Easy Eject to review connected devices.", result.Success ? ToolTipIcon.Info : ToolTipIcon.Warning);
                if (ForegroundWindow) using (var dialog = new BulkEjectResultDialog(result)) dialog.ShowDialog(this);
            } catch (Exception ex) { ShowError(ex); }
            finally { FinishOperation(); }
            await RefreshDevices();
        }
        void ReportCleanup(string message) { BeginInvoke(new Action(() => status.Text = message)); }
        async Task Eject(bool forceClean = false) {
            var v = Selected; if (v == null || working || scanning || readOnly) return;
            try {
                working = true; UpdateActions();
                var plan = EjectService.Plan(inventory, v.Id);
                bool clean = forceClean || (v.IsCard && appSettings.CleanupCardsEnabled); var cleanup = clean ? MetadataCleanupPlan.Single(inventory, plan, v.Id) : null;
                string warning = !v.IsCard && appSettings.WarnOtherEnabled ? "\n\nThis storage is not recognized as a camera or emulator card. Review other data and every affected partition before ejecting." : "";
                string text = "Windows will remove this device and all affected volumes:\n\n" + String.Join("\n", plan.Affected) + warning + (cleanup == null ? "" : "\n\n" + cleanup.Description) + "\n\nFinish all transfers first. Request safe removal?";
                if (MessageBox.Show(this, text, "Safe eject", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                completions.InvalidateAll(); Log("User requested manual safe removal | " + String.Join(", ", plan.Affected));
                working = true; UpdateActions(); status.Text = "Requesting safe removal…";
                var counts = new CleanupCounts(); if (clean) { cancellation = new CancellationTokenSource(); cancel.Enabled = true; }
                var result = await Task.Run(() => new EjectService(discovery, new WindowsEjectApi(discovery, plan.Epoch, clean ? cancellation.Token : CancellationToken.None), operations).Execute(plan, initial => {
                    if (cleanup == null) return;
                    cleanup.Preflight(initial, WindowsDiscovery.VolumeId);
                    cleanup.Clean(() => { var fresh = discovery.Scan(); EjectService.Validate(discovery, fresh, plan); return fresh; }, WindowsDiscovery.VolumeId, cancellation.Token, counts, ReportCleanup);
                }, clean ? cancellation.Token : CancellationToken.None));
                if (clean) result.CleanupMessage = counts.Message;
                status.Text = result.Message;
                if (result.Success) ClearIssue(); else RecordIssue(result.Message);
                Notify(result.Success ? "Safe removal accepted" : "Device still connected", result.Message, result.Success ? ToolTipIcon.Info : ToolTipIcon.Warning);
                if (ForegroundWindow) MessageBox.Show(this, result.Message, result.Success ? "Safe removal accepted" : "Device still connected", MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            } catch (Exception ex) { ShowError(ex); }
            finally { FinishOperation(); }
            await RefreshDevices();
        }
        async Task Import() {
            var v = Selected; if (v == null || working || scanning || readOnly) return;
            automatic.MarkManual(inventory, v);
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
                ProfileDestination.Require(stored, guard.DestinationIdentity, false);
                string text = (options.VideosOnly ? "Copy selected videos and their required companions under:\n" : "Copy all media and sidecars under:\n") + source + "\n\nTo a date folder under:\n" + destination + "\n\n" + (options.IncludePreviews ? "Camera previews included." : "Optional previews stay on the device; required package files are included.") + "\n" + (options.CleanLayout ? "Ordinary media uses a clean layout; packages and unfamiliar media keep their tree." : "Media folder paths are retained.") + "\nOriginal filenames are kept. SHA-256 verifies new and reused copies.\n\n" + OriginalRemovalChoice.BeforeImport(options) + "\n\n" + DeviceTrashChoice.BeforeImport(options) + "\n\nThe device remains connected. Start this manual import?";
                using (var review = new VerifiedImportConfirmationDialog(text)) if (review.ShowDialog(this) != DialogResult.Yes) return;
                if (stored.SavedDestination == null) { guard.Validate(); stored.SavedDestination = guard.DestinationIdentity; Profiles.Save(stored); }
                completions.InvalidateSource(root); Log("User started import | " + source + " -> " + destination);
                working = true; cancellation = new CancellationTokenSource(); cancel.Enabled = true; UpdateActions();
                var result = await Task.Run(() => new VerifiedImporter(operations).Run(root, source, destination, v.Id, guard, cancellation.Token, progressText => {
                    if (!IsDisposed) BeginInvoke(new Action(() => status.Text = progressText));
                }, options));
                await PresentImportResult(result, stored, v, guard, root, destination, options);
            } catch (OperationCanceledException) { status.Text = "Import cancelled. All originals and completed copies kept. No completion receipt was written."; Log(status.Text); Notify("Import cancelled", status.Text, ToolTipIcon.Info); if (ForegroundWindow) MessageBox.Show(this, status.Text, "Import cancelled"); }
            catch (Exception ex) { ShowError(ex); }
            finally { FinishOperation(); }
            await RefreshDevices();
        }
        async Task ResumeImport() {
            var volume = Selected; if (volume == null || working || scanning || readOnly) return;
            try {
                var disk = inventory.Disks.Single(x => x.Number == volume.Disks.Single()); long epoch = inventory.Epoch;
                working = true; UpdateActions();
                string root = SafePaths.Normalize(volume.Mounts.First(x => x.Length >= 3 && x[1] == ':'));
                var stored = ReadProfile(volume, disk);
                if (stored == null) throw new SafetyException("Set up this device with its original media folder and destination first. Saving a profile starts no import. Then choose Resume interrupted import.");
                string source = SafePaths.Normalize(Path.Combine(root, stored.RelativeSource)), destination = SafePaths.Normalize(stored.Destination), intentPath;
                if (!SafePaths.Within(source, root)) throw new SafetyException("The saved media folder is outside this source volume.");
                var options = stored.Resolve(ReadDefaults());
                using (var picker = new OpenFileDialog { Title = "Choose the interrupted batch's removal-intent.json beside its import audit", InitialDirectory = destination, FileName = "removal-intent.json", Filter = "Removal intent (removal-intent.json)|removal-intent.json", CheckFileExists = true, Multiselect = false, RestoreDirectory = true }) {
                    if (picker.ShowDialog(this) != DialogResult.OK) return; intentPath = picker.FileName;
                }
                string sid; using (var identity = WindowsIdentity.GetCurrent()) { if (identity.User == null) throw new SafetyException("The current Windows Trash owner is unavailable."); sid = identity.User.Value; }
                cancellation = new CancellationTokenSource(); cancel.Enabled = true; status.Text = "Reviewing the exact interrupted batch, owners, devices and complete saved copies…";
                var guard = await Task.Run(() => new VolumeImportGuard(discovery, source, destination, volume, disk, epoch));
                ProfileDestination.Require(stored, guard.DestinationIdentity, false); automatic.MarkManual(inventory, volume);
                var reviewed = await Task.Run(() => RemovalResumeEvidence.Review(intentPath, root, source, destination, sid, guard, operations, cancellation.Token));
                using (var dialog = new RemovalResumeConfirmationDialog(RemovalResumeChoice.Explanation(reviewed, volume.Display, source, destination))) if (dialog.ShowDialog(this) != DialogResult.Yes) return;
                cancellation.Token.ThrowIfCancellationRequested();
                guard = await Task.Run(() => new VolumeImportGuard(discovery, source, destination, volume, disk, epoch));
                completions.InvalidateSource(root); Log("User explicitly resumed verified import | " + reviewed.ImportId);
                var result = await Task.Run(() => RemovalResumeEvidence.Resume(reviewed, intentPath, root, source, destination, sid, guard, operations, cancellation.Token, text => {
                    if (!IsDisposed) BeginInvoke(new Action(() => status.Text = text));
                }));
                await PresentImportResult(result, stored, volume, guard, root, destination, options);
            } catch (OperationCanceledException) {
                status.Text = "Resume cancelled before any new permanent removal. Previously absent files remain absent; remaining source data and complete saved copies are kept. No completion receipt was written."; Log(status.Text); Notify("Resume cancelled", status.Text, ToolTipIcon.Info);
                if (ForegroundWindow) ShowMessage(status.Text, "Resume cancelled", MessageBoxIcon.Information);
            } catch (Exception ex) { ShowError(ex); }
            finally { FinishOperation(); }
            await RefreshDevices();
        }
        async Task PresentImportResult(ImportResult result, ImportProfile stored, Volume v, VolumeImportGuard guard, string root, string destination, MediaOptions options) {
            string deviceName = String.IsNullOrWhiteSpace(stored.Name) ? v.Display : stored.Name;
            string removalMessage = result.Receipt != null && !String.IsNullOrEmpty(result.Receipt.ResumedUtc) ? RemovalResumeChoice.Summary(result) : OriginalRemovalChoice.Summary(result.RemovedOriginals) + " " + DeviceTrashChoice.Summary(result.RecoveredTrashFiles, result.ClearedTrashFiles, result.ClearedTrashRecords);
            string historyNote = ""; bool historyFailed = false;
            if (result.HasSelectedMedia) {
                try { await Task.Run(() => ImportHistory.Add(ImportHistory.Create(result, stored, deviceName, guard.DestinationIdentity))); historyNote = "\nExact verified files saved in Import history."; }
                catch (Exception ex) { historyFailed = true; historyNote = "\nImport verified, but history could not be saved: " + ex.Message + "\nCopies and the completion receipt remain available."; RecordIssue(historyNote.Trim()); }
            }
            string message = result.Files == 0 ? "No matching media or device indexes were found. No batch was created." : (result.HasSelectedMedia ? "Import complete. " : "No matching media. Device index backup only. ") + result.Files + " files verified (" + result.Bytes + " bytes; " + result.Reused + " reused).\n" + result.Folder;
            bool sourceUnchanged = guard.CompletionOrigin.Epoch == discovery.Epoch;
            message += historyNote + "\n\n" + removalMessage + " " + (sourceUnchanged ? "Device remains connected." : "The source connection changed after copying. Refresh and review the device."); status.Text = message; Log(message);
            if (!historyFailed) ClearIssue();
            ImportCompletion completion = null;
            if (options.AskToEject && sourceUnchanged) {
                completion = new ImportCompletion(guard.CompletionOrigin, deviceName, result.Files > 0 ? result.Folder : destination, destination, guard.DestinationIdentity, result.HasSelectedMedia, message);
                completions.Insert(completion);
            }
            bool showChoice = CompletionPresentation.ShowChoice(options.AskToEject, ForegroundWindow, appSettings.NotificationsEnabled);
            FinishOperation();
            if (showChoice && completion != null) await ReviewCompletion(completion.Id);
            else {
                Notify(historyFailed ? "Import verified; history needs attention" : result.HasSelectedMedia ? "Import complete" : "No matching media", historyFailed ? historyNote.Trim() : sourceUnchanged ? CompletionPresentation.Notification(result.HasSelectedMedia, result.RemovedOriginals, result.ClearedTrashFiles) : "The import finished, then the source connection changed. Open Easy Eject to review connected devices. " + removalMessage, historyFailed ? ToolTipIcon.Warning : ToolTipIcon.Info);
                if (ForegroundWindow) ShowMessage(message, result.HasSelectedMedia ? "Import complete" : "No matching media", historyFailed ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
        }
        static bool ConfigurationError(Exception ex) {
            return ex is SafetyException || ex is System.Runtime.Serialization.SerializationException || ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception;
        }
        bool ForegroundWindow { get { return Visible && WindowState != FormWindowState.Minimized && (ContainsFocus || Form.ActiveForm == this); } }
        void ShowMessage(string message, string title, MessageBoxIcon icon) {
            bool previous = working; working = true; UpdateActions();
            try { MessageBox.Show(this, message, title, MessageBoxButtons.OK, icon); }
            finally { working = previous; UpdateActions(); }
        }
        void FinishOperation() { working = false; cancel.Enabled = false; if (cancellation != null) { cancellation.Dispose(); cancellation = null; } UpdateActions(); debounce.Start(); }
        void ExpireCompletions() { completions.InvalidateAll(); if (activeCompletion != null && !activeCompletion.IsDisposed) activeCompletion.ExpireEject(); }
        void ShowDevices() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        void Notify(string title, string text, ToolTipIcon icon) {
            if (!appSettings.NotificationsEnabled) return;
            try { tray.ShowBalloonTip(5000, title.Length > 63 ? title.Substring(0, 63) : title, text.Length > 255 ? text.Substring(0, 255) : text, icon); }
            catch (Exception ex) { status.Text += " Notification unavailable: " + ex.Message + " Review completed imports or View issue in the app."; }
        }
        void Log(string text) {
            if (!appSettings.DebugEnabled) return;
            try { DebugLog.Append(Profiles.Root, text); }
            catch (Exception ex) { status.Text += " Debug logging unavailable: " + ex.Message; }
        }
        void SaveIssue() { try { AppIssue.Save(appIssue); } catch (Exception ex) { status.Text += " Issue could not be saved: " + ex.Message; } }
        void RecordIssue(string message) { appIssue.Record(message); status.Text = message; SaveIssue(); Log("Needs attention | " + message); UpdateActions(); }
        void ClearIssue() { if (!appIssue.HasIssue) return; appIssue.Clear(); SaveIssue(); UpdateActions(); }
        void ViewIssue() {
            if (working || !appIssue.HasIssue) return;
            working = true; UpdateActions();
            try { using (var dialog = new IssueDialog(appIssue)) if (dialog.ShowDialog(this) == DialogResult.Yes) { appIssue.Dismiss(); SaveIssue(); status.Text = "Issue acknowledged. No operation was retried."; } }
            finally { FinishOperation(); }
        }
        void ViewDebugLog() {
            if (working || !appSettings.DebugEnabled) return;
            working = true; UpdateActions();
            try { using (var dialog = new DiagnosticsDialog(DebugLog.Read(Profiles.Root))) dialog.ShowDialog(this); }
            catch (Exception ex) { ShowError(ex); }
            finally { FinishOperation(); }
        }
        void EditSettings() {
            if (working || scanning) return;
            working = true; UpdateActions();
            try {
                var registration = readOnly ? null : new StartupRegistration(new WindowsStartupStore(), Application.ExecutablePath);
                var snapshot = registration == null ? null : registration.Inspect();
                using (var dialog = new AppSettingsDialog(appSettings, hotkey.Status, snapshot, readOnly ? (Action)null : () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }))) if (dialog.ShowDialog(this) == DialogResult.OK) {
                if (AppSettings.NeedsCleanupConfirmation(appSettings, dialog.Value) && MessageBox.Show(this,
                    "Enable permanent macOS metadata cleanup before manual camera/emulator card ejection?\n\nOnly ._*, .DS_Store, .apdisk and empty __MACOSX folders are removed. Photos, recordings and device Trash are kept. Each eject confirmation lists the cleanup roots. Saving never starts cleanup.",
                    "Enable metadata cleanup", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                AppSettings.Save(dialog.Value); appSettings = dialog.Value;
                bool registered = readOnly || !IsHandleCreated || hotkey.Configure(Handle, appSettings.ShortcutEnabled, appSettings.ShortcutKey);
                string loginStatus = "";
                if (dialog.LoginChange.HasValue && registration != null) {
                    try { loginStatus = registration.Apply(snapshot, dialog.LoginChange.Value).Message; }
                    catch (Exception ex) { throw new SafetyException("Other application settings were saved, but login registration needs review. " + ex.Message); }
                }
                status.Text = "Application settings saved. " + hotkey.Status + (loginStatus == "" ? "" : "\n" + loginStatus); if (!registered) RecordIssue(hotkey.Status); Log(status.Text);
            } }
            catch (Exception ex) { ShowError(ex); }
            finally { FinishOperation(); }
        }
        async Task ReviewCompletions() {
            if (working || scanning) return;
            string id = null; working = true; UpdateActions();
            try { using (var dialog = new PendingCompletionsDialog(completions.Entries)) if (dialog.ShowDialog(this) == DialogResult.OK) id = dialog.SelectedId; }
            finally { FinishOperation(); }
            if (id != null) await ReviewCompletion(id);
        }
        async Task ReviewCompletion(string id) {
            if (working || scanning) return;
            var entry = completions.Get(id); if (entry == null) { ShowError(new SafetyException("This import choice expired. Refresh and review the connected device.")); return; }
            working = true; UpdateActions(); ShowDevices();
            try {
                while (true) {
                    bool canEject = false;
                    MetadataCleanupPlan cleanup = null;
                    if (entry.Origin.Plan != null) {
                        canEject = await Task.Run(() => {
                            try {
                                var state = discovery.Scan(); bool matches = state.Epoch == discovery.Epoch && completions.Matches(id, state, WindowsDiscovery.VolumeId(entry.Origin.SourceRoot), entry.Origin.SourceRoot);
                                if (matches && entry.Origin.SourceIsCard && appSettings.CleanupCardsEnabled) cleanup = MetadataCleanupPlan.Single(state, entry.Origin.Plan, entry.Origin.VolumeId);
                                return matches;
                            }
                            catch (Exception ex) { if (!ConfigurationError(ex)) throw; return false; }
                        });
                    }
                    DialogResult choice;
                    using (var dialog = new ImportCompletionDialog(entry, canEject, cleanup == null ? "" : cleanup.Description)) { activeCompletion = dialog; choice = dialog.ShowDialog(this); activeCompletion = null; }
                    if (choice == DialogResult.Ignore) {
                        await Task.Run(() => {
                            using (SafePaths.LockDirectories(entry.DestinationRoot)) {
                                var guard = new WindowsDestinationGuard(discovery, entry.DestinationRoot, entry.Destination);
                                CompletionFolder.Open(entry, entry.Destination, guard, operations, folder => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + folder + "\"", UseShellExecute = false }));
                            }
                        });
                        continue;
                    }
                    if (choice != DialogResult.Yes) { status.Text = "Device kept connected. Original files unchanged."; break; }
                    Log("User requested post-import eject | " + entry.DeviceName);
                    var counts = new CleanupCounts(); if (cleanup != null) { cancellation = new CancellationTokenSource(); cancel.Enabled = true; }
                    var result = await Task.Run(() => new CompletedEjectService(completions, discovery, new WindowsEjectApi(discovery, entry.Origin.Epoch, cleanup == null ? CancellationToken.None : cancellation.Token), operations, WindowsDiscovery.VolumeId).Execute(id, initial => {
                        if (cleanup == null) return; cleanup.Preflight(initial, WindowsDiscovery.VolumeId);
                        cleanup.Clean(() => { var fresh = discovery.Scan(); EjectService.Validate(discovery, fresh, entry.Origin.Plan); return fresh; }, WindowsDiscovery.VolumeId, cancellation.Token, counts, ReportCleanup);
                    }, cleanup == null ? CancellationToken.None : cancellation.Token));
                    if (cleanup != null) result.CleanupMessage = counts.Message;
                    status.Text = result.Message; Log(result.Message);
                    if (result.Success) { completions.InvalidateAll(); ClearIssue(); } else RecordIssue(result.Message);
                    Notify(result.Success ? "Safe removal accepted" : "Device still connected", result.Message, result.Success ? ToolTipIcon.Info : ToolTipIcon.Warning);
                    MessageBox.Show(this, result.Message, result.Success ? "Safe removal accepted" : "Device still connected", MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                    break;
                }
            } catch (Exception ex) { ShowError(ex); }
            finally { activeCompletion = null; FinishOperation(); }
        }
        async Task ReviewHistory() {
            if (working || scanning) return;
            try {
                working = true; UpdateActions();
                var values = await Task.Run(() => ImportHistory.Load());
                CompletedBatch batch;
                using (var dialog = new ImportHistoryDialog(values)) { if (dialog.ShowDialog(this) != DialogResult.OK) return; batch = dialog.SelectedBatch; }
                cancellation = new CancellationTokenSource(); cancel.Enabled = true;
                await Task.Run(() => {
                    using (SafePaths.LockDirectories(batch.DestinationRoot)) {
                        var guard = new WindowsDestinationGuard(discovery, batch.DestinationRoot, batch.Destination);
                        ImportHistory.Verify(batch, batch.Destination, guard, operations, cancellation.Token, message => { if (!IsDisposed) BeginInvoke(new Action(() => status.Text = message)); });
                    }
                });
                status.Text = "Verified all " + batch.Originals.Count + " exact saved files. Originals unchanged.";
                MessageBox.Show(this, status.Text + "\n" + batch.Folder, "Saved batch verified", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } catch (OperationCanceledException) { status.Text = "Saved-file verification cancelled. Files unchanged."; }
            catch (Exception ex) { ShowError(ex); }
            finally { working = false; cancel.Enabled = false; if (cancellation != null) { cancellation.Dispose(); cancellation = null; } UpdateActions(); debounce.Start(); }
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
                    if (dialog.ShowDialog(this) == DialogResult.OK) { ImportDefaults.Save(dialog.Value); automatic.SettingsSaved(); status.Text = "Import defaults saved. They apply on the next manual import or source connection; no import started."; }
                }
            } catch (Exception ex) { ShowError(ex); }
            finally { working = false; UpdateActions(); debounce.Start(); }
        }
        void EditSavedProfiles() {
            if (working || scanning || readOnly) return;
            try {
                working = true; UpdateActions();
                using (var dialog = new SavedProfilesDialog(() => Profiles.Catalog(), ReadDefaults, delegate(SavedProfile expected, ImportProfile draft, bool chosen) {
                    if (chosen || !String.Equals(expected.Profile.Destination, draft.Destination, StringComparison.OrdinalIgnoreCase)) {
                        using (SafePaths.LockDirectories(draft.Destination)) {
                            string id = WindowsDiscovery.VolumeId(draft.Destination);
                            var identity = DestinationIdentity.Capture(discovery.Scan(), id); OfflineProfileDestination.Validate(draft, identity);
                            Profiles.SaveOffline(expected, draft, Profiles.Root, identity);
                        }
                    } else Profiles.SaveOffline(expected, draft);
                }, entry => Profiles.ForgetOffline(entry), automatic.SettingsSaved)) {
                    dialog.ShowDialog(this);
                    if (dialog.Changed) status.Text = "Saved profiles updated. No import started. Device and destination validation still apply to each import.";
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
            using (var dialog = new ImportProfileDialog(stored ?? choices.NewProfile(volume, disk), root, volume.Display, choices, stored != null, automatic.SettingsSaved)) {
                DialogResult result = dialog.ShowDialog(this);
                if (result == DialogResult.Retry) {
                    if (MessageBox.Show(this, "Forget this device's saved folders and import choices? Files and originals stay intact.", "Forget import profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes) {
                        Profiles.Remove(volume, disk); automatic.SettingsSaved(); status.Text = "Import profile forgotten. No files were imported or removed.";
                    }
                    return null;
                }
                if (result != DialogResult.OK) return null;
                ImportProfile draft = dialog.Draft;
                string source = SafePaths.Normalize(Path.Combine(root, draft.RelativeSource));
                await Task.Run(() => { using (SafePaths.LockDirectories(source, draft.Destination)) { var guard = new VolumeImportGuard(discovery, source, draft.Destination, volume, disk, epoch); if (!dialog.DestinationWasChosen) ProfileDestination.Require(draft, guard.DestinationIdentity, false); guard.Validate(); draft.SavedDestination = guard.DestinationIdentity; } });
                Profiles.Save(draft); automatic.SettingsSaved(); status.Text = "Import profile saved. No import started. Choose Verified import or reconnect for enabled automatic import.";
                return draft;
            }
        }
        void ShowError(Exception ex) { RecordIssue(ex.Message); Notify("Easy Eject needs attention", ex.Message, ToolTipIcon.Warning); if (ForegroundWindow) ShowMessage(ex.Message, "Easy Eject", MessageBoxIcon.Warning); }
        protected override void Dispose(bool disposing) { if (disposing) { hotkey.Dispose(); discovery.Instances.Dispose(); tray.Dispose(); debounce.Dispose(); } base.Dispose(disposing); }
    }
}
