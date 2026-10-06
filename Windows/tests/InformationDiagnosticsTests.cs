using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        static void InformationDiagnosticsTests() {
            Test("clearing the displayed private log leaves preferences history and media untouched", delegate {
                using (var f = new Fixture()) {
                    string log = Path.Combine(f.Destination, "debug.log");
                    foreach (string name in new[] { "profiles.json", "issue-state.json", "import-history.json", "original.mp4" }) File.WriteAllText(Path.Combine(f.Destination, name), "keep " + name);
                    DebugLog.Append(f.Destination, "first event"); var snapshot = DebugLog.Load(f.Destination);
                    Check(snapshot.Exists && snapshot.Text.Contains("first event"), "Displayed snapshot missing"); DebugLog.Clear(snapshot, f.Destination);
                    Check(!File.Exists(log) && !DebugLog.Load(f.Destination).Exists, "Exact log did not clear");
                    foreach (string name in new[] { "profiles.json", "issue-state.json", "import-history.json", "original.mp4" }) Check(File.ReadAllText(Path.Combine(f.Destination, name)) == "keep " + name, "Unrelated file changed");
                    DebugLog.Append(f.Destination, "new event"); Check(DebugLog.Read(f.Destination).Contains("new event"), "Logging did not resume after clear");
                }
            });
            Test("clear refuses same-size changed log even with restored file modification time", delegate {
                using (var f = new Fixture()) {
                    string path = Path.Combine(f.Destination, "debug.log"); File.WriteAllText(path, "old text"); DateTime date = File.GetLastWriteTimeUtc(path); var before = DebugLog.Load(f.Destination);
                    File.WriteAllText(path, "new text"); File.SetLastWriteTimeUtc(path, date);
                    Check(Throws<SafetyException>(() => DebugLog.Clear(before, f.Destination)).Message.Contains("Refresh"), "Missing stale choice guidance"); Check(File.ReadAllText(path) == "new text", "Changed log cleared");
                }
            });
            Test("clear refuses replacement identity even when displayed bytes and time match", delegate {
                using (var f = new Fixture()) {
                    string path = Path.Combine(f.Destination, "debug.log"); File.WriteAllText(path, "same text"); DateTime date = File.GetLastWriteTimeUtc(path); var before = DebugLog.Load(f.Destination);
                    File.Move(path, Path.Combine(f.Destination, "old-log.txt")); File.WriteAllText(path, "same text"); File.SetLastWriteTimeUtc(path, date);
                    Throws<SafetyException>(() => DebugLog.Clear(before, f.Destination)); Check(File.ReadAllText(path) == "same text", "Replacement cleared");
                }
            });
            Test("missing snapshot cannot clear a log created after the viewer opened", delegate {
                using (var f = new Fixture()) {
                    var missing = DebugLog.Load(f.Destination); DebugLog.Clear(missing, f.Destination); DebugLog.Append(f.Destination, "later");
                    Throws<SafetyException>(() => DebugLog.Clear(missing, f.Destination)); Check(DebugLog.Read(f.Destination).Contains("later"), "New unseen log removed");
                    Throws<SafetyException>(() => DebugLog.Clear(null, f.Destination));
                }
            });
            Test("hard-linked private log cannot be read appended or cleared through an alias", delegate {
                using (var f = new Fixture()) {
                    string path = Path.Combine(f.Destination, "debug.log"), alias = Path.Combine(f.Source, "alias.txt"); File.WriteAllText(path, "private fixture"); var before = DebugLog.Load(f.Destination);
                    Check(CreateHardLink(alias, path, IntPtr.Zero), "Could not create isolated hard-link fixture");
                    Throws<SafetyException>(() => DebugLog.Read(f.Destination)); Throws<SafetyException>(() => DebugLog.Append(f.Destination, "must refuse")); Throws<SafetyException>(() => DebugLog.Clear(before, f.Destination));
                    Check(File.ReadAllText(alias) == "private fixture" && File.ReadAllText(path) == "private fixture", "Alias contents changed");
                }
            });
            Test("read-only debug log stays readable but cannot be appended or cleared", delegate {
                using (var f = new Fixture()) {
                    string path = Path.Combine(f.Destination, "debug.log"); File.WriteAllText(path, "protected fixture"); var before = DebugLog.Load(f.Destination); File.SetAttributes(path, FileAttributes.ReadOnly);
                    try { Check(DebugLog.Read(f.Destination) == before.Text, "Read-only log unavailable for review"); Throws<Exception>(() => DebugLog.Append(f.Destination, "refuse")); Throws<Exception>(() => DebugLog.Clear(before, f.Destination)); Check(File.ReadAllText(path) == "protected fixture" && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0, "Protection changed"); }
                    finally { File.SetAttributes(path, FileAttributes.Normal); }
                }
            });
            Test("system debug log is refused without changing its protection", delegate {
                using (var f = new Fixture()) {
                    string path = Path.Combine(f.Destination, "debug.log"); File.WriteAllText(path, "system fixture"); var before = DebugLog.Load(f.Destination); File.SetAttributes(path, FileAttributes.System);
                    try { Throws<SafetyException>(() => DebugLog.Load(f.Destination)); Throws<SafetyException>(() => DebugLog.Append(f.Destination, "refuse")); Throws<SafetyException>(() => DebugLog.Clear(before, f.Destination)); Check(File.ReadAllText(path) == "system fixture" && (File.GetAttributes(path) & FileAttributes.System) != 0, "Protection changed"); }
                    finally { File.SetAttributes(path, FileAttributes.Normal); }
                }
            });
            Test("a directory named debug.log is never swept or mistaken for a missing log", delegate {
                using (var f = new Fixture()) {
                    string path = Path.Combine(f.Destination, "debug.log"); var missing = DebugLog.Load(f.Destination); Directory.CreateDirectory(path); string retained = Path.Combine(path, "retain.mp4"); File.WriteAllText(retained, "keep");
                    Throws<SafetyException>(() => DebugLog.Load(f.Destination)); Throws<SafetyException>(() => DebugLog.Append(f.Destination, "refuse")); Throws<SafetyException>(() => DebugLog.Clear(missing, f.Destination)); Check(File.ReadAllText(retained) == "keep", "Directory contents swept");
                }
            });
            Test("debug log actions refuse a redirected private directory", delegate {
                using (var f = new Fixture()) {
                    File.WriteAllText(Path.Combine(f.Source, "debug.log"), "keep"); var snapshot = DebugLog.Load(f.Source); string link = Path.Combine(f.Root, "linked-logs"); CreateJunction(f, link, f.Source);
                    Throws<SafetyException>(() => DebugLog.Load(link)); Throws<SafetyException>(() => DebugLog.Append(link, "refuse")); Throws<SafetyException>(() => DebugLog.Clear(snapshot, link)); Check(File.ReadAllText(Path.Combine(f.Source, "debug.log")) == "keep", "Redirected target changed");
                }
            });
            Test("oversized debug log is retained when read append and clear refuse", delegate {
                using (var f = new Fixture()) {
                    string path = Path.Combine(f.Destination, "debug.log"); File.WriteAllText(path, "small"); var before = DebugLog.Load(f.Destination); File.WriteAllBytes(path, new byte[1048577]);
                    Throws<SafetyException>(() => DebugLog.Load(f.Destination)); Throws<SafetyException>(() => DebugLog.Append(f.Destination, "refuse")); Throws<SafetyException>(() => DebugLog.Clear(before, f.Destination)); Check(new FileInfo(path).Length == 1048577, "Oversized diagnostics lost");
                }
            });
            Test("a concurrent reader prevents log clear and keeps the exact file intact", delegate {
                using (var f = new Fixture()) {
                    string path = Path.Combine(f.Destination, "debug.log"); File.WriteAllText(path, "held"); var before = DebugLog.Load(f.Destination);
                    using (var held = SafePaths.ReadFile(path)) Throws<Exception>(() => DebugLog.Clear(before, f.Destination));
                    Check(File.ReadAllText(path) == "held", "Concurrent file removed"); DebugLog.Clear(before, f.Destination); Check(!File.Exists(path), "Released clear did not complete");
                }
            });
            Test("debug viewer has no automatic copy clear or refresh and copies displayed snapshot", delegate {
                using (var f = new Fixture()) {
                    DebugLog.Append(f.Destination, "displayed"); var before = DebugLog.Load(f.Destination); int loads = 0, clears = 0, copies = 0; string copied = null;
                    using (var dialog = new DiagnosticsDialog(before, () => { loads++; return DebugLog.Load(f.Destination); }, expected => { clears++; DebugLog.Clear(expected, f.Destination); }, value => { copies++; copied = value; })) {
                        Check(loads + clears + copies == 0, "Viewer performed an action on opening"); DebugLog.Append(f.Destination, "unseen event"); dialog.RequestCopy();
                        Check(copies == 1 && copied == before.Text && !copied.Contains("unseen event") && clears + loads == 0, "Copy used unseen log or caused mutation");
                        dialog.RequestClear(); Check(clears == 1 && loads == 0 && dialog.DisplayedText == before.Text && dialog.ActionStatus.Contains("Refresh"), "Stale clear was silently retried");
                        dialog.RequestRefresh(); Check(loads == 1 && dialog.DisplayedText.Contains("unseen event"), "Explicit refresh missing"); dialog.RequestCopy(); Check(copied == dialog.DisplayedText && copies == 2, "Refreshed snapshot not copied");
                        dialog.RequestClear(); Check(clears == 2 && loads == 2 && !DebugLog.Load(f.Destination).Exists, "Refreshed clear incomplete"); dialog.RequestClear(); dialog.RequestCopy(); Check(clears == 2 && copies == 2, "Missing log actions still enabled");
                    }
                }
            });
            Test("debug viewer action failures preserve displayed diagnostics and remain explicit", delegate {
                using (var f = new Fixture()) {
                    DebugLog.Append(f.Destination, "keep"); var before = DebugLog.Load(f.Destination); int copies = 0, clears = 0, loads = 0;
                    using (var dialog = new DiagnosticsDialog(before, () => { loads++; throw new SafetyException("fixture refresh refused"); }, expected => { clears++; throw new SafetyException("fixture clear refused"); }, value => { copies++; throw new InvalidOperationException("fixture clipboard busy"); })) {
                        dialog.RequestCopy(); Check(dialog.ActionStatus.Contains("clipboard busy"), "Copy error hidden"); dialog.RequestClear(); Check(dialog.ActionStatus.Contains("clear refused") && loads == 0, "Clear error retried"); dialog.RequestRefresh();
                        Check(dialog.ActionStatus.Contains("refresh refused") && dialog.DisplayedText == before.Text && DebugLog.Read(f.Destination) == before.Text && copies == 1 && clears == 1 && loads == 1, "Failure lost text or retried");
                    }
                }
            });
            Test("text-only diagnostics renderer cannot invoke private file or clipboard actions", delegate {
                using (var dialog = new DiagnosticsDialog("synthetic diagnostics")) {
                    dialog.RequestCopy(); dialog.RequestClear(); dialog.RequestRefresh(); Check(dialog.DisplayedText == "synthetic diagnostics" && dialog.ActionStatus == "", "Text-only viewer action ran");
                    Check(dialog.Controls.Find("copy", true).Single().Enabled == false && dialog.Controls.Find("clear", true).Single().Enabled == false, "Absent capabilities enabled");
                }
            });
            Test("minimum debug viewer retains fixed action footer and Close keyboard defaults", delegate {
                using (var dialog = new DiagnosticsDialog(new string('x', 12000))) {
                    dialog.Size = dialog.MinimumSize; CreateRenderHandles(dialog); dialog.PerformLayout(); var text = (TextBox)dialog.Controls.Find("logText", true).Single();
                    Check(text.ReadOnly && text.ScrollBars == ScrollBars.Both && text.Height > 150 && text.TextLength == 12000, "Diagnostic viewport lost");
                    Check(Object.ReferenceEquals(dialog.AcceptButton, dialog.CancelButton) && ((Button)dialog.AcceptButton).Text == "Close", "Enter or Escape can mutate logs");
                    foreach (string name in new[] { "copy", "clear", "refresh", "close" }) { var button = dialog.Controls.Find(name, true).Single(); Check(button.Right <= button.Parent.ClientSize.Width && button.Bottom <= button.Parent.ClientSize.Height, "Footer action clipped: " + name); }
                }
            });
            Test("help and about are scrollable read-only views with Close keyboard defaults", delegate {
                foreach (bool about in new[] { false, true }) using (var dialog = new InformationDialog(about)) {
                    dialog.Size = dialog.MinimumSize; CreateRenderHandles(dialog); dialog.PerformLayout();
                    Check(dialog.Content.ReadOnly && dialog.Content.ScrollBars == ScrollBars.Vertical && dialog.Content.Height > 200, "Instructions unavailable at minimum size");
                    Check(Object.ReferenceEquals(dialog.AcceptButton, dialog.CancelButton) && ((Button)dialog.AcceptButton).Text == "Close", "Unexpected default information action");
                    Check(dialog.Content.Text.Contains(about ? ApplicationInformation.Version : "Full-quality originals remain unchanged by default."), "Missing version or original-retention guidance");
                }
                Check(ApplicationInformation.About.Contains("Development version") && ApplicationInformation.About.Contains("No Windows release or update channel is configured yet"), "Prototype described as a final release");
                Check(typeof(Program).Assembly.GetName().Version != new Version(0, 0, 0, 0), "Executable has no version identity");
            });
            Test("tray Help About and debug actions are unavailable during work and scan", delegate {
                using (var window = new DeviceWindow(true)) {
                    var type = typeof(DeviceWindow); var help = (ToolStripMenuItem)type.GetField("helpMenu", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window); var about = (ToolStripMenuItem)type.GetField("aboutMenu", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                    type.GetField("appSettings", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, new AppSettings { DebugLogging = true }); type.GetMethod("UpdateActions", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
                    var debug = (ToolStripMenuItem)type.GetField("debugMenu", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                    Check(help.Enabled && about.Enabled, "Idle information actions missing");
                    foreach (string flag in new[] { "working", "scanning" }) {
                        type.GetField(flag, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, true); type.GetMethod("UpdateActions", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
                        Check(!help.Enabled && !about.Enabled && !debug.Enabled, "Information action interrupts " + flag); type.GetMethod("ViewInformation", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { false }); type.GetMethod("ViewInformation", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { true }); type.GetMethod("ViewDebugLog", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
                        Check((bool)type.GetField(flag, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window), "Information action changed active state"); type.GetField(flag, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, false);
                    }
                }
            });
        }
    }
}
