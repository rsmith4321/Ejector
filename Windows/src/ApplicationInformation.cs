using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Easy Eject for Windows")]
[assembly: AssemblyProduct("Easy Eject")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: AssemblyInformationalVersion("0.1.0-dev")]

namespace EasyEject {
    internal static class ApplicationInformation {
        internal static string Version {
            get { return ((AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(typeof(ApplicationInformation).Assembly, typeof(AssemblyInformationalVersionAttribute))).InformationalVersion; }
        }
        internal static string About { get { return AboutFor(".NET Framework 4.8"); } }
        internal static string ModernAbout { get { return AboutFor(".NET 10 / WinUI 3"); } }
        static string AboutFor(string platform) { return "Easy Eject for Windows\r\nDevelopment version " + Version + "\r\n\r\nWindows 11 x64 / " + platform + "\r\n\r\nThis local development build is part of the full Windows port. Real camera/card compatibility, foreground behavior and trusted signing still need acceptance testing. No Windows release or update channel is configured yet.\r\n\r\nSettings, saved profiles, exact import history and debug logs are stored separately from the executable in local application data. Closing the device window hides it in the tray; Quit exits after active work finishes or is cancelled."; }
        internal static string Help { get { return HelpFor(false); } }
        internal static string ModernHelp { get { return HelpFor(true); } }
        static string HelpFor(bool modern) { return
            "Easy Eject Help & Instructions\r\n\r\n" +
            "Connected storage\r\nRefresh reads connected volumes. Camera and emulator cards are candidates identified from their folders. Choose a volume in the window or Connected storage in the tray to review an action. Internal/Windows storage and incomplete or ambiguous mappings are protected.\r\n\r\n" +
            "Verified imports\r\nCreate an Import profile for a device and choose a destination on a different physical disk. Import defaults control media, previews and folder layout; a custom profile can override them. Files are copied and checked with SHA-256 before completion. Full-quality originals remain unchanged by default.\r\n\r\n" +
            "Saved choices\r\nSaved profiles can be edited even after the device is disconnected. Automatic import, asking whether to eject after import, original removal and Trash recovery start off. Enabling removal/recovery requires the separate acknowledgement in the profile or defaults. Interrupted removal is resumed only through its reviewed, recorded import.\r\n\r\n" +
            "Completion and history\r\nCompleted imports offers Keep connected, Open destination and a fresh eject review when the device is still eligible. Import history retains the last 20 exact verified batches for saved-copy verification and manual Quick Share after source eject or app restart. History never restores source-removal or eject permission.\r\n\r\n" +
            "Manual Quick Share\r\nEnable Quick Share per device, then choose it explicitly from a completed import or history. Each action asks for 1080p or 4K and whether to apply the official Luna I-Log LUT. Supported Luna videos get separate sharing copies; full-quality imported originals are kept. " + (modern ? "Quick Share tools" : "Settings") + " lets you select your local FFmpeg/FFprobe executables and official LUT. Nothing is downloaded or installed automatically. Export does not automatically eject.\r\n\r\n" +
            "Safe eject and cleanup\r\nSafe eject reviews the selected storage's removal group, including sibling volumes. Clean & eject is a separate reviewed metadata cleanup action. The clean-before-eject setting requires acknowledgement and starts off. If Windows reports a veto, close the application using the device and review a new request. A failure never forces removal.\r\n\r\n" +
            "Stopping and leaving the window\r\nCancel operation stops at the next safe check and keeps verified copies and remaining originals. Closing this window hides Easy Eject in the tray while work continues. Open it from the tray to review results. Quit is unavailable until work finishes or cancellation completes.\r\n\r\n" +
            "Settings and diagnostics\r\nNotifications start on; the eject shortcut, debug logging and launch at sign-in start off. Choose a shortcut explicitly in Settings. View issue shows a failure; Dismiss acknowledges it without retrying. Enable debug logging to open its viewer. Refresh reads the current private log, Copy to Clipboard copies the displayed text, and Clear log clears only that displayed log when it has not changed.\r\n\r\n" +
            (modern ? "Login\r\nLaunch at login is a separate Settings choice. Save applies the reviewed choice; Cancel does not. Login opens Easy Eject in the tray without enabling automatic imports. Moving the app requires explicitly selecting the new copy. Windows Startup apps can still disable or delay it.\r\n\r\nWindows updates\r\nOpen Windows updates & installation for the separate app/settings folders and manual update/rollback instructions. Updates are unavailable in this development version. No modern Windows release channel or signing publisher is approved; this app cannot determine whether a newer release is available. Metadata/file/backup review stays unavailable until its modern package policy is approved. Installation is always manual." :
            "Windows updates and installation\r\nOpen Windows updates & installation from the tray for the separate app/settings folders and manual install, update and rollback instructions. No Windows release channel or signing publisher is approved in this development build. It cannot determine whether newer Windows releases are available. Instructions never download, install or restart the app.");
        }
    }
    internal sealed class InformationDialog : DpiForm {
        internal readonly TextBox Content;
        internal InformationDialog(bool about) {
            Text = about ? "About Easy Eject" : "Easy Eject Help & Instructions";
            Font = new Font("Segoe UI", 10); Size = about ? new Size(700, 510) : new Size(850, 640);
            MinimumSize = new Size(650, 420); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            Content = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Text = about ? ApplicationInformation.About : ApplicationInformation.Help };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12), FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(100, 36), DialogResult = DialogResult.Cancel }; footer.Controls.Add(close);
            Controls.Add(Content); Controls.Add(footer); AcceptButton = close; CancelButton = close;
        }
    }
}
