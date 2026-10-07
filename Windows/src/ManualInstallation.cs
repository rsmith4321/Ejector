using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace EasyEject {
    internal static class WindowsUpdateSafety {
        internal static bool CanReview(bool busy, bool scanning, bool openDialog) { return !busy && !scanning && !openDialog; }
        internal const string ChannelStatus = "This development build has no approved Windows release channel or signing publisher. It cannot determine whether a newer Windows release is available.";
    }
    // These are folder-view permissions, never installation, executable-launch or deletion authority.
    internal sealed class InstallationFolders : IDisposable {
        internal sealed class Folder : IDisposable {
            internal readonly string Path, Error; readonly SafePaths.DirectoryLease lease;
            internal bool Available { get { return lease != null; } }
            internal Folder(string path) {
                Path = path ?? "(unavailable)";
                try { Path = SafePaths.Normalize(path); lease = SafePaths.HoldDirectories(Path); lease.Validate(); }
                catch (Exception ex) { if (lease != null) lease.Dispose(); lease = null; Error = ex.Message; }
            }
            internal void Validate() { if (lease == null) throw new SafetyException("This folder could not be verified: " + Error); lease.Validate(); }
            public void Dispose() { if (lease != null) lease.Dispose(); }
        }
        internal readonly Folder Application, Settings;
        readonly Action<string> open; bool disposed;
        internal InstallationFolders(string application, string settings, Action<string> open = null) {
            this.open = open ?? (path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }));
            Application = new Folder(application); Settings = new Folder(settings);
        }
        internal void Open(bool settings) {
            if (disposed) throw new ObjectDisposedException("InstallationFolders");
            var folder = settings ? Settings : Application; folder.Validate(); open(folder.Path);
        }
        public void Dispose() { disposed = true; Settings.Dispose(); Application.Dispose(); }
    }
    internal static class ManualInstallation {
        internal static string Instructions(string version, InstallationFolders folders, string channelStatus = null) {
            return "Easy Eject for Windows\r\nCurrent version: " + version + "\r\n\r\n" + (channelStatus ?? WindowsUpdateSafety.ChannelStatus) + "\r\n\r\n" +
                "Application folder\r\n" + folders.Application.Path + FolderIssue(folders.Application) + "\r\n\r\n" +
                "Separate settings folder\r\n" + folders.Settings.Path + FolderIssue(folders.Settings) + "\r\nProfiles, import defaults, exact batch history, tool selections, the supplied LUT and diagnostics stay here when you switch app folders.\r\n\r\n" +
                "First installation\r\nOnce an approved Windows download is available, use its Windows x64 ZIP and published verification instructions. Check the archive SHA-256 and the executable's trusted Windows signature against the approved publisher. The current Mac DMG, PKG, app and Sparkle feed are not Windows updates. Extract the complete Windows folder into a versioned folder you own, then start EasyEject.Windows.exe. Keep the accompanying .exe.config file. No administrator or startup installation is needed.\r\n\r\n" +
                "Manual update\r\nFinish or safely cancel active transfers and removal, then close open dialogs. Close these instructions and choose Quit from the tray; closing the device window only hides it. With the app stopped, make a backup of the separate settings folder before upgrading. Extract the verified new release into a new versioned folder, retain the previous app folder, and start only the new copy. Check its version and saved choices. If you previously enabled launch at sign-in, explicitly review its moved-copy registration from the new app.\r\n\r\n" +
                "Manual rollback\r\nFinish work, close dialogs and Quit the new copy. Start the retained previous app folder. It uses the same separate settings. An older app may refuse a newer settings format; review the saved backup before restoring anything. Rollback does not import, clean, eject or delete camera originals.\r\n\r\n" +
                "Remove the app\r\nIf enabled, explicitly turn off this app's launch-at-sign-in entry in Settings. Finish work and Quit, then remove only the versioned application folder. Separate settings and imported media are retained. Clearing saved settings is a separate deliberate choice.\r\n\r\n" +
                "These instructions and folder buttons perform no download, extraction, installation, restart, registration change or cleanup.";
        }
        static string FolderIssue(InstallationFolders.Folder folder) { return folder.Available ? "" : "\r\nFolder access unavailable: " + folder.Error; }
    }
    internal sealed class ManualInstallationDialog : DpiForm {
        internal readonly TextBox Content;
        internal readonly Button OpenApplication, OpenSettings, CloseGuide;
        readonly InstallationFolders folders; readonly Action<string> showError;
        internal ManualInstallationDialog(string version, InstallationFolders folders, Action<string> showError = null) {
            this.folders = folders; this.showError = showError ?? (message => MessageBox.Show(this, message, "Folder unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning));
            Text = "Windows updates and installation"; Font = new Font("Segoe UI", 10); Size = new Size(850, 650); MinimumSize = new Size(650, 420);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            Content = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Text = ManualInstallation.Instructions(version, folders) };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12), FlowDirection = FlowDirection.RightToLeft };
            CloseGuide = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(100, 36), DialogResult = DialogResult.Cancel };
            OpenSettings = new Button { Text = "Open settings folder", AutoSize = true, MinimumSize = new Size(160, 36), Enabled = folders.Settings.Available };
            OpenApplication = new Button { Text = "Open app folder", AutoSize = true, MinimumSize = new Size(150, 36), Enabled = folders.Application.Available };
            OpenSettings.Click += delegate { OpenFolder(true); }; OpenApplication.Click += delegate { OpenFolder(false); };
            footer.Controls.Add(CloseGuide); footer.Controls.Add(OpenSettings); footer.Controls.Add(OpenApplication);
            Controls.Add(Content); Controls.Add(footer); AcceptButton = CloseGuide; CancelButton = CloseGuide;
        }
        internal void OpenFolder(bool settings) { if (IsDisposed || Disposing) return; try { folders.Open(settings); } catch (Exception ex) { showError(ex.Message); } }
        protected override void Dispose(bool disposing) { if (disposing) folders.Dispose(); base.Dispose(disposing); }
    }
}
