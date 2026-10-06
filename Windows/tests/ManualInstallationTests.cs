using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        static void ManualInstallationTests() {
            Test("Windows manual update review refuses every busy scan or dialog combination", delegate {
                foreach (bool busy in new[] { false, true }) foreach (bool scan in new[] { false, true }) foreach (bool dialog in new[] { false, true })
                    Check(WindowsUpdateSafety.CanReview(busy, scan, dialog) == (!busy && !scan && !dialog), "Review crossed active work/scan/dialog protection");
            });
            Test("Windows installation guide identifies development version unavailable channel and Windows-only manual delivery", delegate {
                using (var f = new Fixture()) using (var folders = new InstallationFolders(f.Source, f.Destination, _ => { throw new Exception("Unrequested folder launch"); })) {
                    string text = ManualInstallation.Instructions(ApplicationInformation.Version, folders);
                    Check(text.Contains(ApplicationInformation.Version) && text.Contains(WindowsUpdateSafety.ChannelStatus) && !text.Contains("up to date") && text.Contains("Windows x64 ZIP") && text.Contains("Mac DMG") && text.Contains("SHA-256") && text.Contains("trusted Windows signature"), "Guide implies release/channel proof or offers Mac artifacts");
                    Check(text.Contains("Manual rollback") && text.Contains("close open dialogs") && text.Contains("choose Quit") && text.Contains("closing the device window only hides") && text.Contains("Separate settings and imported media are retained"), "Manual exit/rollback/state retention omitted");
                }
            });
            Test("Windows installation folder actions are explicit exact-path views that preserve files", delegate {
                using (var f = new Fixture()) {
                    f.Put("original.mp4", "keep original"); File.WriteAllText(Path.Combine(f.Destination, "settings.json"), "keep settings"); var opened = new List<string>();
                    using (var folders = new InstallationFolders(f.Source, f.Destination, opened.Add)) using (var dialog = new ManualInstallationDialog("fixture-version", folders, _ => { throw new Exception("Unexpected folder error"); })) {
                        Check(opened.Count == 0 && dialog.Content.ReadOnly && dialog.OpenApplication.Enabled && dialog.OpenSettings.Enabled && dialog.AcceptButton == dialog.CloseGuide && dialog.CancelButton == dialog.CloseGuide, "Guide starts work or has an unsafe default");
                        dialog.OpenFolder(false); dialog.OpenFolder(true); Check(opened.SequenceEqual(new[] { f.Source, f.Destination }), "Folder action retargeted a captured path");
                        Check(File.ReadAllText(Path.Combine(f.Source, "original.mp4")) == "keep original" && File.ReadAllText(Path.Combine(f.Destination, "settings.json")) == "keep settings" && Directory.GetFiles(f.Source).Length == 1 && Directory.GetFiles(f.Destination).Length == 1, "Folder view changed media/settings or created private work");
                    }
                }
            });
            Test("Windows installation guide disables missing folders without creating them", delegate {
                using (var f = new Fixture()) {
                    string missing = Path.Combine(f.Root, "missing-settings"); var opened = new List<string>(); var errors = new List<string>();
                    using (var folders = new InstallationFolders(f.Source, missing, opened.Add)) using (var dialog = new ManualInstallationDialog("fixture", folders, errors.Add)) {
                        Check(dialog.OpenApplication.Enabled && !dialog.OpenSettings.Enabled && dialog.Content.Text.Contains("Folder access unavailable"), "Missing location became enabled");
                        dialog.OpenFolder(true); dialog.OpenFolder(false); Check(errors.Count == 1 && opened.SequenceEqual(new[] { f.Source }) && !Directory.Exists(missing), "Missing location recreated or blocked available location");
                    }
                }
            });
            Test("Windows installation guide refuses redirected folder aliases before opening", delegate {
                using (var f = new Fixture()) {
                    string link = Path.Combine(f.Root, "settings-link"); CreateJunction(f, link, f.Destination); int calls = 0; var errors = new List<string>();
                    using (var folders = new InstallationFolders(f.Source, link, _ => calls++)) using (var dialog = new ManualInstallationDialog("fixture", folders, errors.Add)) {
                        Check(!dialog.OpenSettings.Enabled && dialog.OpenApplication.Enabled, "Redirected location became enabled"); dialog.OpenFolder(true);
                        Check(calls == 0 && errors.Count == 1 && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0 && Directory.GetFileSystemEntries(f.Destination).Length == 0, "Alias followed or changed");
                    }
                }
            });
            Test("Windows installation folder identity rejects empty-directory replacement at click", delegate {
                using (var f = new Fixture()) {
                    int calls = 0; var errors = new List<string>();
                    using (var folders = new InstallationFolders(f.Source, f.Destination, _ => calls++)) using (var dialog = new ManualInstallationDialog("fixture", folders, errors.Add)) {
                        string moved = f.Destination + "-moved"; Directory.Move(f.Destination, moved); Directory.CreateDirectory(f.Destination); File.WriteAllText(Path.Combine(f.Destination, "foreign.keep"), "replacement folder contents");
                        dialog.OpenFolder(true); Check(calls == 0 && errors.Count == 1 && Directory.Exists(moved) && File.ReadAllText(Path.Combine(f.Destination, "foreign.keep")) == "replacement folder contents", "Changed folder opened or cleaned");
                    }
                }
            });
            Test("Windows installation opener failure retains instructions without automatic retry", delegate {
                using (var f = new Fixture()) {
                    int calls = 0; var errors = new List<string>();
                    using (var folders = new InstallationFolders(f.Source, f.Destination, _ => { calls++; throw new InvalidOperationException("fixture viewer unavailable"); })) using (var dialog = new ManualInstallationDialog("fixture", folders, errors.Add)) {
                        string before = dialog.Content.Text; dialog.OpenFolder(false); Check(calls == 1 && errors.Count == 1 && errors[0].Contains("fixture viewer unavailable") && dialog.Content.Text == before && dialog.CancelButton == dialog.CloseGuide, "Failure retried or changed guide/default");
                    }
                }
            });
            Test("Windows installation closed guide releases leases and refuses stale actions", delegate {
                using (var f = new Fixture()) {
                    int calls = 0; var folders = new InstallationFolders(f.Source, f.Destination, _ => calls++); var dialog = new ManualInstallationDialog("fixture", folders, _ => { throw new Exception("Stale error callback"); }); dialog.Dispose();
                    dialog.OpenFolder(true); Throws<ObjectDisposedException>(() => folders.Open(false)); Check(calls == 0, "Closed view launched a stale action");
                    Directory.Move(f.Destination, f.Destination + "-released"); Check(Directory.Exists(f.Destination + "-released"), "Guide retained a directory handle"); folders.Dispose();
                }
            });
            Test("Windows installation tray callback refuses work scan and disabled-dialog owner while preserving status", delegate {
                using (var window = new DeviceWindow(true)) {
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic; var type = typeof(DeviceWindow); var menu = (ToolStripMenuItem)type.GetField("installationMenu", flags).GetValue(window);
                    var status = (TextBox)type.GetField("status", flags).GetValue(window); status.Text = "keep prior issue/progress";
                    foreach (string field in new[] { "working", "scanning" }) {
                        type.GetField(field, flags).SetValue(window, true); type.GetMethod("UpdateActions", flags).Invoke(window, null); Check(!menu.Enabled, "Review enabled during " + field);
                        type.GetMethod("ViewInstallation", flags).Invoke(window, null); Check(status.Text == "keep prior issue/progress", "Rejected review changed status"); type.GetField(field, flags).SetValue(window, false);
                    }
                    window.Enabled = false; type.GetMethod("UpdateActions", flags).Invoke(window, null); Check(!menu.Enabled, "Disabled dialog owner allowed review"); type.GetMethod("ViewInstallation", flags).Invoke(window, null);
                    window.Enabled = true; type.GetMethod("UpdateActions", flags).Invoke(window, null); Check(menu.Enabled && status.Text == "keep prior issue/progress" && ApplicationInformation.Help.Contains("Windows updates and installation"), "Idle review failed or lost context/help");
                }
            });
        }
    }
}
