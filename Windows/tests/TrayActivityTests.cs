using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        static T ActivityField<T>(DeviceWindow window, string name) { return (T)typeof(DeviceWindow).GetField(name, PrivateInstance).GetValue(window); }
        static void RefreshActivityWindow(DeviceWindow window) { typeof(DeviceWindow).GetMethod("UpdateActions", PrivateInstance).Invoke(window, null); }
        static List<ImportWorkProgress> MeasuredImport(Fixture f, out ImportResult result, Action<ImportWorkProgress> observe = null) {
            var values = new List<ImportWorkProgress>();
            result = new VerifiedImporter(new Operations(), path => UInt64.MaxValue, line => {}).Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, line => {}, new MediaOptions(false, true), value => { values.Add(value); if (observe != null) observe(value); });
            return values;
        }
        static void TrayActivityTests() {
            Test("large import reports intermediate checked copy progress and 100 only after receipt publication", delegate {
                using (var f = new Fixture()) {
                    f.Put("DCIM/VID_large.mp4", ""); File.WriteAllBytes(Path.Combine(f.Source, "DCIM", "VID_large.mp4"), new byte[8 * 1048576]); f.Put("DCIM/VID_small.mp4", "smaller fixture");
                    ImportResult result; var values = MeasuredImport(f, out result, value => {
                        if (value.Percent == 100) Check(Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 1, "Completion preceded durable receipt");
                    });
                    Check(values.Any(x => x.Phase == "Copying" && x.Percent > 20 && x.Percent < 60) && values.Any(x => x.Phase == "Verifying copy") && values.Any(x => x.Phase == "Checking published copy"), "Long-file intermediate work not measured");
                    Check(values.Last().Percent == 100 && values.Last().Phase == "Import complete" && File.Exists(result.ReceiptPath), "Missing verified completion");
                    for (int i = 1; i < values.Count; i++) Check(values[i].Fraction >= values[i - 1].Fraction, "Measured work regressed");
                    Check(values.Count < 250 && values.All(x => x.Fraction >= 0 && x.Fraction <= 1), "Progress flooding or invalid fraction");
                    Check(new FileInfo(Path.Combine(f.Source, "DCIM", "VID_large.mp4")).Length == 8 * 1048576, "Original changed");
                }
            });
            Test("reused verified imports report checks without pretending to copy files", delegate {
                using (var f = new Fixture()) {
                    f.Put("DCIM/VID_fixture.mp4", "kept fixture"); ImportResult first, second; MeasuredImport(f, out first); var values = MeasuredImport(f, out second);
                    Check(second.Reused == second.Files && second.Files > 0 && values.Any(x => x.Phase == "Checking saved copy") && !values.Any(x => x.Phase == "Copying"), "Reuse progress claimed new transfer");
                    Check(values.Last().Percent == 100 && File.ReadAllText(Saved(second, Path.Combine("DCIM", "VID_fixture.mp4"))) == "kept fixture", "Reuse not verified");
                }
            });
            Test("all-empty selected files have finite progress and verified receipts", delegate {
                using (var f = new Fixture()) {
                    f.Put("DCIM/VID_empty1.mp4", ""); f.Put("DCIM/VID_empty2.mp4", ""); ImportResult result; var values = MeasuredImport(f, out result);
                    Check(result.Files == 2 && values.All(x => !Double.IsNaN(x.Fraction) && !Double.IsInfinity(x.Fraction)) && values.Last().Percent == 100 && File.Exists(result.ReceiptPath), "Empty files broke progress or verification");
                }
            });
            Test("cancel during measured copying preserves original and never reports completion", delegate {
                using (var f = new Fixture()) using (var stop = new CancellationTokenSource()) {
                    f.Put("DCIM/VID_cancel.mp4", ""); string original = Path.Combine(f.Source, "DCIM", "VID_cancel.mp4"); File.WriteAllBytes(original, new byte[8 * 1048576]); var values = new List<ImportWorkProgress>(); var operations = new Operations();
                    Throws<OperationCanceledException>(() => new VerifiedImporter(operations, path => UInt64.MaxValue, line => {}).Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), stop.Token, line => {}, new MediaOptions(false, true), value => { values.Add(value); if (value.Phase == "Copying" && value.Percent > 25) stop.Cancel(); }));
                    Check(!values.Any(x => x.Percent == 100) && new FileInfo(original).Length == 8 * 1048576 && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(f.Destination, "*.partial", SearchOption.AllDirectories).Length == 0, "Cancellation lost source, completion state or partial cleanup");
                    using (operations.Reserve(new[] { 700, 701 })) {}
                }
            });
            Test("capacity refusal never emits successful import progress", delegate {
                using (var f = new Fixture()) {
                    f.Put("DCIM/VID_keep.mp4", "retained fixture"); var values = new List<ImportWorkProgress>();
                    Throws<SafetyException>(() => new VerifiedImporter(new Operations(), path => 0UL, line => {}).Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, line => {}, new MediaOptions(false, true), values.Add));
                    Check(!values.Any(x => x.Percent == 100) && File.ReadAllText(Path.Combine(f.Source, "DCIM", "VID_keep.mp4")) == "retained fixture", "Refusal looked successful");
                }
            });
            Test("a failed terminal display callback cannot misreport a committed import as failed", delegate {
                using (var f = new Fixture()) {
                    f.Put("DCIM/VID_keep.mp4", "retained fixture"); ImportResult result; MeasuredImport(f, out result, value => { if (value.Phase == "Import complete") throw new InvalidOperationException("fixture terminal display unavailable"); });
                    Check(File.Exists(result.ReceiptPath) && File.ReadAllText(Saved(result, Path.Combine("DCIM", "VID_keep.mp4"))) == "retained fixture", "Committed receipt lost to display callback");
                }
            });
            Test("empty filtered import is reported honestly without creating a completion receipt", delegate {
                using (var f = new Fixture()) {
                    f.Put("DCIM/photo.jpg", "photo retained"); var values = new List<ImportWorkProgress>();
                    var result = new VerifiedImporter(new Operations()).Run(f.Source, f.Source, f.Destination, "fixture-volume", new FakeGuard(), CancellationToken.None, line => {}, new MediaOptions(true, true), values.Add);
                    Check(result.Files == 0 && values.Last().Phase == "No matching media" && Directory.GetFiles(f.Destination, "verified-import.json", SearchOption.AllDirectories).Length == 0 && File.Exists(Path.Combine(f.Source, "DCIM", "photo.jpg")), "Filtered import produced a false batch");
                }
            });
            Test("tray reports measured import percentage and named device without changing issue attention", delegate {
                var state = new TrayActivity(); var token = state.Begin(TrayOperationKind.Import, "Fixture camera", "Preparing import"); state.Report(token, "Copying fixture.mp4", new ImportWorkProgress("Copying", 0.35));
                var view = TrayPresentation.Create(state, true, false, 2, true);
                Check(view.Busy && view.Attention && view.Tooltip.Contains("Importing 35%") && view.Tooltip.EndsWith("Needs attention", StringComparison.Ordinal) && view.Heading.Contains("Fixture camera") && view.Heading.Contains("Copying"), "Hidden import state or existing issue missing");
                view = TrayPresentation.Create(state, true, false, 2, false); Check(!view.Attention && view.Tooltip.Contains("Importing 35%"), "Acknowledgment erased work progress");
            });
            Test("tray scanning and planning phases do not fabricate a measured percentage", delegate {
                var state = new TrayActivity(); var token = state.Begin(TrayOperationKind.Import, "Fixture camera", "Preparing import"); state.Report(token, null, new ImportWorkProgress("Scanning", 0));
                Check(TrayPresentation.Create(state, true, false, 1, false).Tooltip.Contains("Scanning") && !TrayPresentation.Create(state, true, false, 1, false).Tooltip.Contains("%"), "Unmeasured scan has percentage");
                state.End("Import complete"); var view = TrayPresentation.Create(state, false, true, 1, false); Check(view.Tooltip.Contains("Reading connected storage"), "Inventory scan hidden"); view = TrayPresentation.Create(state, false, false, 1, false); Check(view.Heading.Contains("Import complete") && view.Tooltip.Contains("1 candidate card disks"), "Refresh erased final outcome or counts");
            });
            Test("Quick Share percentage explicitly describes current copy and validation remains busy", delegate {
                var state = new TrayActivity(); var token = state.Begin(TrayOperationKind.Share, "Fixture Luna", "Checking saved originals");
                state.Report(token, "Creating sharing copy: 40%"); Check(TrayPresentation.Create(state, true, false, 0, false).Tooltip.Contains("Sharing copy 40%"), "Copy scope missing");
                state.Report(token, "Creating sharing copy: 100%"); Check(TrayPresentation.Create(state, true, false, 0, false).Busy && String.IsNullOrEmpty(state.Outcome), "Encoded copy falsely completed entire action");
                state.Report(token, "Checking sharing copy VID_fixture.mp4"); Check(!state.Percent.HasValue && TrayPresentation.Create(state, true, false, 0, false).Tooltip.Contains("Checking sharing copy"), "Validation retained old encode percentage");
                state.Report(token, "Creating sharing copy: 0%"); Check(state.Percent == 0 && String.IsNullOrEmpty(state.Outcome), "Next copy could not start fresh");
            });
            Test("malformed share percentages remain unmeasured phases", delegate {
                var state = new TrayActivity(); var token = state.Begin(TrayOperationKind.Share, "Fixture Luna", "Checking saved originals");
                foreach (string text in new[] { "NaN%", "101%", "-1%", "50 %", "%", "50" }) {
                    state.Report(token, "Creating sharing copy: " + text); Check(!state.Percent.HasValue && TrayPresentation.Create(state, true, false, 0, false).Tooltip.EndsWith("Creating sharing copy", StringComparison.Ordinal), "Invalid progress accepted: " + text);
                }
            });
            Test("late callbacks cannot overwrite cancellation final result or a replacement operation", delegate {
                var state = new TrayActivity(); var old = state.Begin(TrayOperationKind.Import, "First fixture", "Preparing import"); state.RequestStop();
                Check(!state.Report(old, "Copying late", new ImportWorkProgress("Copying", 0.9)) && TrayPresentation.Create(state, true, false, 1, false).Tooltip.Contains("Stopping operation"), "Late progress erased stop request");
                state.End("Import cancelled; files kept"); Check(!state.Report(old, "Late completion") && state.Outcome.Contains("cancelled"), "Late completion erased cancellation");
                var next = state.Begin(TrayOperationKind.Verify, "Second fixture", "Checking saved files"); Check(!state.Report(old, "Old import") && state.Report(next, "Verifying saved fixture.mp4") && !state.Outcome.Contains("cancelled"), "Operation identity reused");
            });
            Test("sealed export callbacks stay invalid through separately recorded final outcome", delegate {
                var state = new TrayActivity(); var token = state.Begin(TrayOperationKind.Share, "Fixture Luna", "Checking saved originals"); state.End("");
                Check(state.AwaitingOutcome && !state.Report(token, "Creating sharing copy: 99%"), "Sealed process still reports progress"); state.End("Sharing copies ready. Full-quality originals kept.");
                Check(!state.AwaitingOutcome && TrayPresentation.Create(state, true, false, 0, false).Heading.Contains("Sharing copies ready") && !state.Report(token, "Late output"), "Final outcome lost while dialog busy");
            });
            Test("native tray tooltip is bounded and preserves attention with long unicode outcomes", delegate {
                var state = new TrayActivity(); state.End(String.Concat(Enumerable.Repeat("🌕&\r\n", 100))); var view = TrayPresentation.Create(state, false, false, 2, true);
                Check(view.Tooltip.Length <= 63 && view.Tooltip.EndsWith("Needs attention", StringComparison.Ordinal) && !view.Tooltip.Any(Char.IsControl) && view.Heading.Contains("&&") && !view.Heading.Any(Char.IsControl), "Unsafe native tooltip or menu mnemonic");
                for (int i = 0; i < view.Tooltip.Length; i++) if (Char.IsHighSurrogate(view.Tooltip[i])) { Check(i + 1 < view.Tooltip.Length && Char.IsLowSurrogate(view.Tooltip[i + 1]), "Truncated emoji surrogate"); i++; }
            });
            Test("hidden native window binds progress heading busy icon and existing warning state with notifications off", delegate {
                using (var window = new DeviceWindow(true)) {
                    var activity = ActivityField<TrayActivity>(window, "trayActivity"); var token = activity.Begin(TrayOperationKind.Import, "Fixture camera", "Preparing import");
                    typeof(DeviceWindow).GetField("working", PrivateInstance).SetValue(window, true); typeof(DeviceWindow).GetField("appSettings", PrivateInstance).SetValue(window, new AppSettings { ShowNotifications = false }); ActivityField<AppIssue>(window, "appIssue").Record("fixture issue");
                    window.ApplyActivityProgress(token, "Copying synthetic file", new ImportWorkProgress("Copying", 0.43)); RefreshActivityWindow(window);
                    var tray = ActivityField<NotifyIcon>(window, "tray"); var menu = ActivityField<ToolStripMenuItem>(window, "activityMenu"); var icons = ActivityField<TrayBusyIcons>(window, "trayBusyIcons");
                    Check(!window.Visible && tray.Text.Contains("43%") && tray.Text.EndsWith("Needs attention", StringComparison.Ordinal) && Object.ReferenceEquals(tray.Icon, icons.Attention) && !menu.Enabled && menu.Text.Contains("Fixture camera"), "Hidden active native state missing");
                    menu.PerformClick(); Check(activity.Current == token, "Status row triggered work"); activity.End("Import verified; originals kept"); typeof(DeviceWindow).GetField("working", PrivateInstance).SetValue(window, false); RefreshActivityWindow(window);
                    Check(menu.Text.Contains("Import verified") && Object.ReferenceEquals(tray.Icon, System.Drawing.SystemIcons.Warning), "Idle warning/outcome disappeared");
                }
            });
            Test("queued native callback after finish cannot replace the final window or tray result", delegate {
                using (var window = new DeviceWindow(true)) {
                    CreateRenderHandles(window); var activity = ActivityField<TrayActivity>(window, "trayActivity"); var old = activity.Begin(TrayOperationKind.Import, "Fixture camera", "Preparing import");
                    window.BeginInvoke(new Action(() => window.ApplyActivityProgress(old, "Late copying callback", new ImportWorkProgress("Copying", 0.9))));
                    activity.End("Import cancelled; copies kept"); var status = ActivityField<TextBox>(window, "status"); status.Text = "Final cancellation result"; RefreshActivityWindow(window); string tooltip = ActivityField<NotifyIcon>(window, "tray").Text;
                    Application.DoEvents(); Check(status.Text == "Final cancellation result" && ActivityField<NotifyIcon>(window, "tray").Text == tooltip, "Stale queued native callback overwrote final result");
                    var next = activity.Begin(TrayOperationKind.Share, "Fixture Luna", "Checking saved originals"); window.ApplyActivityProgress(old, "Late old import"); window.ApplyActivityProgress(next, "Creating sharing copy: 27%"); Check(status.Text == "Creating sharing copy: 27%" && ActivityField<NotifyIcon>(window, "tray").Text.Contains("Sharing copy 27%"), "Current operation callback suppressed or mixed");
                }
            });
            Test("Cancel operation keeps stopping state when an old measured report arrives", delegate {
                using (var window = new DeviceWindow(true)) {
                    CreateRenderHandles(window); var activity = ActivityField<TrayActivity>(window, "trayActivity"); var token = activity.Begin(TrayOperationKind.Import, "Fixture camera", "Preparing import"); var stop = new CancellationTokenSource();
                    typeof(DeviceWindow).GetField("cancellation", PrivateInstance).SetValue(window, stop); typeof(DeviceWindow).GetField("working", PrivateInstance).SetValue(window, true);
                    // A hidden WinForms button cannot be selected by PerformClick; exercise its shared handler directly.
                    var cancel = ActivityField<Button>(window, "cancel"); cancel.Enabled = true; window.RequestCancellation(); window.ApplyActivityProgress(token, "Queued progress", new ImportWorkProgress("Copying", 0.9));
                    Check(stop.IsCancellationRequested && ActivityField<TextBox>(window, "status").Text.Contains("Stopping after") && ActivityField<NotifyIcon>(window, "tray").Text.Contains("Stopping operation"), "Cancellation lost to late callback");
                    typeof(DeviceWindow).GetMethod("FinishOperation", PrivateInstance).Invoke(window, null); Check(!cancel.Enabled && activity.Current == null, "Cancel capability survived completion");
                }
            });
        }
    }
}
