using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyEject {
    public static partial class Tests {
        sealed class UpdateCoordinatorFixture : IDisposable {
            internal readonly WindowsChannelFixture Channel = new WindowsChannelFixture();
            internal WindowsUpdateCheckScope Scope; internal int Calls; internal Func<WindowsUpdateCheckScope, Task<WindowsChannelOffer>> Operation;
            internal WindowsUpdateCoordinator Create(bool enabled = true) {
                return new WindowsUpdateCoordinator(true, scope => { Interlocked.Increment(ref Calls); Scope = scope; return Operation == null ? Task.FromResult(Channel.Offer()) : Operation(scope); }, () => Channel.Now, TimeSpan.FromDays(1), enabled);
            }
            public void Dispose() { Channel.Dispose(); }
        }
        static WindowsUpdateCheckResult UpdatePulse(WindowsUpdateCoordinator c) { return ChannelAwait(() => c.PulseAsync()); }
        static WindowsUpdateCheckResult UpdateManual(WindowsUpdateCoordinator c, CancellationToken cancel = default(CancellationToken)) { return ChannelAwait(() => c.CheckManualAsync(cancel)); }
        static void WindowsUpdateCoordinatorTests() {
            Test("Windows update defaults and legacy settings enable metadata checks without other automatic actions", () => {
                using (var f = new Fixture()) { File.WriteAllText(Path.Combine(f.Destination, "app-settings.json"), "{\"ShowNotifications\":false,\"DebugLogging\":true,\"EnableShortcut\":false,\"ShortcutLetter\":\"R\",\"CleanCardsOnEject\":false,\"WarnOtherStorage\":false}"); var value = AppSettings.Load(f.Destination);
                    Check(new AppSettings().AutomaticUpdatesEnabled && value.AutomaticUpdatesEnabled && !value.AutomaticUpdateChecks.HasValue && !value.NotificationsEnabled && value.DebugEnabled && value.ShortcutKey == "R" && !value.CleanupCardsEnabled && !new ImportDefaults().AutomaticEnabled, "Legacy/default update migration changed independent choices"); }
            });
            Test("Windows update explicit off survives configuration save reload and unrelated settings edits", () => {
                using (var f = new Fixture()) { var before = new AppSettings { AutomaticUpdateChecks = false, ShowNotifications = false, DebugLogging = true, EnableShortcut = true, ShortcutLetter = "M", WarnOtherStorage = false }; AppSettings.Save(before, f.Destination); var current = AppSettings.Load(f.Destination);
                    using (var dialog = new AppSettingsDialog(current)) { dialog.Cleanup.Checked = true; var value = dialog.Value; Check(!dialog.AutomaticUpdates.Checked && !value.AutomaticUpdatesEnabled && !value.NotificationsEnabled && value.DebugEnabled && value.ShortcutEnabled && value.ShortcutKey == "M" && !value.WarnOtherEnabled, "Settings edit lost explicit off or other flags"); AppSettings.Save(value, f.Destination); }
                    Check(!AppSettings.Load(f.Destination).AutomaticUpdatesEnabled && AppSettings.Load(f.Destination).CleanupCardsEnabled, "Reload lost independent choices"); }
            });
            Test("Windows update settings cancellation writes nothing and explains unavailable channel and manual installation", () => {
                using (var f = new Fixture()) { AppSettings.Save(new AppSettings(), f.Destination); string path = Path.Combine(f.Destination, "app-settings.json"), hash = WindowsSignerEvidence.Hash(File.ReadAllBytes(path));
                    using (var dialog = new AppSettingsDialog(AppSettings.Load(f.Destination))) { dialog.AutomaticUpdates.Checked = false; Check(dialog.UpdateStatus.Text.Contains("no approved Windows release channel") && dialog.UpdateStatus.Text.Contains("installation remain manual") && dialog.UpdateStatus.Text.Contains("No system profile"), "Missing honest channel/privacy/manual explanation"); dialog.DialogResult = DialogResult.Cancel; }
                    Check(WindowsSignerEvidence.Hash(File.ReadAllBytes(path)) == hash && AppSettings.Load(f.Destination).AutomaticUpdatesEnabled, "Cancelled preference changed configuration"); }
            });
            Test("Windows update settings control is reachable at minimum size with save cancel outside scroll area", () => {
                using (var dialog = new AppSettingsDialog(new AppSettings())) { dialog.Size = dialog.MinimumSize; CreateRenderHandles(dialog); dialog.PerformLayout(); var body = dialog.AutomaticUpdates.Parent as FlowLayoutPanel; Check(body != null && body.AutoScroll && !body.WrapContents && dialog.UpdateStatus.Parent == body && dialog.UpdateStatus.MaximumSize.Width <= body.ClientSize.Width && dialog.AcceptButton != null && dialog.CancelButton != null, "Update controls lack bounded scrolling/footer"); body.ScrollControlIntoView(dialog.AutomaticUpdates); Check(dialog.AutomaticUpdates.Bounds.IntersectsWith(body.ClientRectangle), "Automatic preference is unreachable"); }
            });
            Test("Windows update unconfigured and read-only runtime creates no storage or network and never claims current", () => {
                using (var f = new WindowsChannelFixture()) { int roots = 0; var transport = ChannelTransport(f); using (var c = WindowsReleaseChecks.Create(null, () => { roots++; return f.Archive.Package.Files.Destination; }, transport, new WindowsManifestTrust(f.Archive.Package.CertificateTrust), () => f.Now, true)) {
                    c.PublishIdle(true); c.Start(); Check(!c.Configured && UpdatePulse(c) == null && c.Status.Contains("cannot determine"), "Unconfigured check claimed availability/current"); Throws<SafetyException>(() => UpdateManual(c)); Check(roots == 0 && transport.Metadata == 0 && transport.Signature == 0, "Missing channel touched storage or transport"); }
                    using (var c = WindowsReleaseChecks.Create(true, true)) { c.Start(); c.PublishIdle(true); Check(UpdatePulse(c) == null && !c.Configured && WindowsReleaseChecks.ProductionChannel == null, "Read-only/default runtime enabled an unapproved channel"); }
                }
            });
            Test("Windows update startup waits for foreground idle and repeated starts do not duplicate checks", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create()) { Check(UpdatePulse(c) == null && f.Calls == 0, "Unstarted coordinator fetched"); c.Start(); c.Start(); Check(UpdatePulse(c) == null && f.Calls == 0, "Busy startup fetched"); c.PublishIdle(true); var result = UpdatePulse(c); Check(result.Outcome == WindowsUpdateCheckOutcome.Accepted && f.Calls == 1 && UpdatePulse(c) == null && !c.Checking, "Startup duplicated or never admitted check"); }
            });
            Test("Windows update due check waits through import eject scan and dialog then resumes only once", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create()) { c.Start(); foreach (string state in new[] { "import", "eject", "scan", "modal", "disabled owner" }) { c.PublishIdle(false); for (int i = 0; i < 3; i++) Check(UpdatePulse(c) == null, "Busy due check fetched during " + state); } c.PublishIdle(true); Check(UpdatePulse(c).Outcome == WindowsUpdateCheckOutcome.Accepted && f.Calls == 1 && UpdatePulse(c) == null, "Idle resume looped or duplicated"); }
            });
            Test("Windows update daily cadence consumes admitted success and permits one later fresh check", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create()) { c.Start(); c.PublishIdle(true); Check(UpdatePulse(c).Outcome == WindowsUpdateCheckOutcome.Accepted, "Initial check failed"); f.Channel.Now = f.Channel.Now.AddHours(23); Check(UpdatePulse(c) == null, "Cadence checked early"); f.Channel.Now = f.Channel.Now.AddHours(1); Check(UpdatePulse(c).Outcome == WindowsUpdateCheckOutcome.Accepted && f.Calls == 2 && UpdatePulse(c) == null, "Daily check missing/duplicated"); }
            });
            Test("Windows update automatic off blocks pulses but preserves manual checks", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create(false)) { c.Start(); c.PublishIdle(true); Check(UpdatePulse(c) == null && f.Calls == 0 && c.Status.Contains("are off"), "Explicit off fetched"); var result = UpdateManual(c); Check(result.Origin == WindowsUpdateCheckOrigin.Manual && result.Outcome == WindowsUpdateCheckOutcome.Accepted && c.TryPresent(result) && f.Calls == 1, "Off preference disabled manual check"); }
            });
            Test("Windows update enabling automatic checks explicitly schedules once without equal-save resets", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create(false)) { c.Start(); c.PublishIdle(true); c.SetAutomatic(true); Check(UpdatePulse(c).Outcome == WindowsUpdateCheckOutcome.Accepted && f.Calls == 1, "Enabling did not admit a due check"); c.SetAutomatic(true); Check(UpdatePulse(c) == null, "Saving unchanged enabled reset cadence"); c.SetAutomatic(false); c.SetAutomatic(true); Check(UpdatePulse(c).Outcome == WindowsUpdateCheckOutcome.Accepted && f.Calls == 2, "Explicit re-enable did not schedule"); }
            });
            Test("Windows update rejected manual admission and precancellation consume no work or due attempt", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create()) using (var cancel = new CancellationTokenSource()) { c.Start(); Throws<SafetyException>(() => UpdateManual(c)); c.PublishIdle(true); cancel.Cancel(); Throws<OperationCanceledException>(() => UpdateManual(c, cancel.Token)); Check(f.Calls == 0 && UpdatePulse(c).Outcome == WindowsUpdateCheckOutcome.Accepted && f.Calls == 1, "Denied manual consumed work/cadence"); }
            });
            Test("Windows update active request blocks another manual check and suppresses duplicate timer pulses", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create()) { var pending = new TaskCompletionSource<WindowsChannelOffer>(); f.Operation = scope => pending.Task; c.Start(); c.PublishIdle(true); var task = c.PulseAsync(); Check(c.Checking && f.Scope.Allowed && UpdatePulse(c) == null, "Pending check lost ownership or pulse duplicated"); Check(Throws<SafetyException>(() => UpdateManual(c)).Message.Contains("already in progress") && f.Calls == 1, "Duplicate manual started"); pending.SetResult(f.Channel.Offer()); Check(ChannelAwait(() => task).Outcome == WindowsUpdateCheckOutcome.Accepted && !c.Checking, "Terminal request retained slot"); }
            });
            Test("Windows update busy then idle permanently revokes token-ignoring late results without immediate retry", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create()) { var pending = new TaskCompletionSource<WindowsChannelOffer>(); f.Operation = scope => pending.Task; c.Start(); c.PublishIdle(true); var task = c.PulseAsync(); var captured = f.Scope; c.PublishIdle(false); c.PublishIdle(true); Check(!captured.Allowed && captured.Token.IsCancellationRequested && c.Checking && !task.IsCompleted && UpdatePulse(c) == null, "Transient activity revived scope or claimed ignored request stopped"); pending.SetResult(f.Channel.Offer()); var result = ChannelAwait(() => task); Check(result.Outcome == WindowsUpdateCheckOutcome.Cancelled && result.Offer == null && !c.TryPresent(result) && UpdatePulse(c) == null && f.Calls == 1, "Late result was presented/retried"); }
            });
            Test("Windows update disable then re-enable cannot revive an already revoked automatic check", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create()) { var pending = new TaskCompletionSource<WindowsChannelOffer>(); f.Operation = scope => pending.Task; c.Start(); c.PublishIdle(true); var task = c.PulseAsync(); var captured = f.Scope; c.SetAutomatic(false); c.SetAutomatic(true); Check(!captured.Allowed && c.Checking && UpdatePulse(c) == null, "Preference re-enabled old authority or overlapping request"); pending.SetResult(f.Channel.Offer()); Check(ChannelAwait(() => task).Outcome == WindowsUpdateCheckOutcome.Cancelled, "Revoked automatic result accepted"); f.Operation = null; Check(UpdatePulse(c).Outcome == WindowsUpdateCheckOutcome.Accepted && f.Calls == 2, "Explicit re-enable lost its later attempt"); }
            });
            Test("Windows update automatic preference changes preserve an explicit manual request", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create()) { var pending = new TaskCompletionSource<WindowsChannelOffer>(); f.Operation = scope => pending.Task; c.PublishIdle(true); var task = c.CheckManualAsync(CancellationToken.None); c.SetAutomatic(false); Check(f.Scope.Allowed && !f.Scope.Token.IsCancellationRequested, "Automatic off cancelled manual action"); pending.SetResult(f.Channel.Offer()); var result = ChannelAwait(() => task); Check(result.Outcome == WindowsUpdateCheckOutcome.Accepted && c.TryPresent(result), "Manual result invalidated by automatic choice"); }
            });
            Test("Windows update review caller cancellation releases only after terminal and allows manual retry", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create()) using (var cancel = new CancellationTokenSource()) { var pending = new TaskCompletionSource<WindowsChannelOffer>(); f.Operation = scope => pending.Task; c.PublishIdle(true); var task = c.CheckManualAsync(cancel.Token); cancel.Cancel(); Check(!f.Scope.Allowed && c.Checking && !task.IsCompleted, "Review close falsely completed ignored operation"); pending.SetResult(f.Channel.Offer()); Check(ChannelAwait(() => task).Outcome == WindowsUpdateCheckOutcome.Cancelled && !c.Checking, "Review lifetime cancellation returned offer or kept slot"); f.Operation = null; Check(UpdateManual(c).Outcome == WindowsUpdateCheckOutcome.Accepted && f.Calls == 2, "Manual retry unavailable"); }
            });
            Test("Windows update failure consumes cadence while manual retry remains explicit", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create()) { f.Operation = scope => { throw new SafetyException("fixture trust refusal"); }; c.Start(); c.PublishIdle(true); var failed = UpdatePulse(c); Check(failed.Outcome == WindowsUpdateCheckOutcome.Failed && failed.Offer == null && failed.Message.Contains("trust refusal") && c.TryPresent(failed), "Failure became accepted or lost diagnosis"); for (int i = 0; i < 4; i++) Check(UpdatePulse(c) == null, "Failure automatically retried early"); f.Operation = null; Check(UpdateManual(c).Outcome == WindowsUpdateCheckOutcome.Accepted && f.Calls == 2, "Failure blocked manual retry"); }
            });
            Test("Windows update asynchronous faults and missing callback operations are observed and release the slot", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create()) { c.PublishIdle(true); f.Operation = scope => null; Check(UpdateManual(c).Outcome == WindowsUpdateCheckOutcome.Failed && !c.Checking, "Null task retained slot/accepted"); f.Operation = scope => Task.FromResult<WindowsChannelOffer>(null); Check(UpdateManual(c).Outcome == WindowsUpdateCheckOutcome.Failed, "Null offer accepted"); var fault = new TaskCompletionSource<WindowsChannelOffer>(); fault.SetException(new InvalidOperationException("owned async failure")); f.Operation = scope => fault.Task; Check(UpdateManual(c).Message.Contains("owned async failure") && !c.Checking, "Async fault escaped or retained ownership"); }
            });
            Test("Windows update expiry after callback prevents acceptance before foreground delivery", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create()) { var offer = f.Channel.Offer(f.Channel.Text(expires: f.Channel.Seconds + 30)); c.PublishIdle(true); f.Operation = scope => { f.Channel.Now = f.Channel.Now.AddMinutes(1); return Task.FromResult(offer); }; var result = UpdateManual(c); Check(result.Outcome == WindowsUpdateCheckOutcome.Failed && result.Offer == null && result.Message.Contains("expired"), "Expired callback became accepted"); }
            });
            Test("Windows update queued delivery refuses another host and presents the current result only once", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var first = f.Create()) using (var other = f.Create()) { first.PublishIdle(true); other.PublishIdle(true); var result = UpdateManual(first); Check(!other.TryPresent(result) && first.TryPresent(result) && !first.TryPresent(result), "Result retargeted host or presented twice"); }
            });
            Test("Windows update queued delivery refuses intervening activity even after idle returns", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create()) { c.PublishIdle(true); var result = UpdateManual(c); c.PublishIdle(false); c.PublishIdle(true); Check(!c.TryPresent(result), "Queued result survived activity transition"); var fresh = UpdateManual(c); Check(c.TryPresent(fresh), "Fresh manual result refused"); }
            });
            Test("Windows update queued automatic delivery refuses disabled or re-enabled preference epochs", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create()) { c.Start(); c.PublishIdle(true); var result = UpdatePulse(c); c.SetAutomatic(false); c.SetAutomatic(true); Check(!c.TryPresent(result), "Old automatic result revived after preference transition"); Check(c.TryPresent(UpdatePulse(c)), "New explicit enabled check refused"); }
            });
            Test("Windows update newer explicit request invalidates an older queued delivery", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create()) { c.PublishIdle(true); WindowsUpdateCheckResult older = UpdateManual(c), newer = UpdateManual(c); Check(!c.TryPresent(older) && c.TryPresent(newer), "Older queued check replaced newer result"); }
            });
            Test("Windows update host close revokes work retains live ownership until terminal and refuses stale presentation", () => {
                using (var f = new UpdateCoordinatorFixture()) { var c = f.Create(); var pending = new TaskCompletionSource<WindowsChannelOffer>(); f.Operation = scope => pending.Task; c.Start(); c.PublishIdle(true); var task = c.PulseAsync(); c.Dispose(); c.Dispose(); Check(!f.Scope.Allowed && f.Scope.Token.IsCancellationRequested && c.Checking && !task.IsCompleted, "Host close falsely completed or revived request"); pending.SetResult(f.Channel.Offer()); var result = ChannelAwait(() => task); Check(result.Outcome == WindowsUpdateCheckOutcome.Cancelled && !c.Checking && !c.TryPresent(result), "Closed host accepted result/retained terminal slot"); Throws<ObjectDisposedException>(() => UpdateManual(c)); }
            });
            Test("Windows update cancellation callback faults do not restore authority or crash foreground activity", () => {
                using (var f = new UpdateCoordinatorFixture()) using (var c = f.Create()) { var pending = new TaskCompletionSource<WindowsChannelOffer>(); CancellationTokenRegistration registration = default(CancellationTokenRegistration); f.Operation = scope => { registration = scope.Token.Register(() => { throw new InvalidOperationException("fixture cancellation callback fault"); }); return pending.Task; }; c.Start(); c.PublishIdle(true); var task = c.PulseAsync(); c.PublishIdle(false); c.PublishIdle(true); pending.SetResult(f.Channel.Offer()); Check(ChannelAwait(() => task).Outcome == WindowsUpdateCheckOutcome.Cancelled && !c.Checking, "Faulting cancellation restored scope or retained slot"); registration.Dispose(); }
            });
            Test("Windows update scheduling rejects invalid UTC clock and cadence without invoking work", () => {
                int calls = 0; foreach (var date in new[] { DateTime.Now, new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc) }) using (var c = new WindowsUpdateCoordinator(true, scope => { calls++; return null; }, () => date, TimeSpan.FromDays(1), true)) { c.Start(); c.PublishIdle(true); Throws<SafetyException>(() => UpdatePulse(c)); Check(!c.Checking, "Invalid clock captured a scope"); }
                foreach (var cadence in new[] { TimeSpan.Zero, TimeSpan.FromDays(8) }) Throws<ArgumentException>(() => new WindowsUpdateCoordinator(false, null, () => DateTime.UtcNow, cadence, true)); Check(calls == 0, "Invalid schedule invoked work");
            });
            Test("Windows update runtime factory lazily shares durable replay across admitted checks", () => {
                using (var f = new WindowsChannelFixture()) { int roots = 0; string root = Path.Combine(f.Archive.Package.Files.Root, "controller-replay"); var transport = ChannelTransport(f, f.Text("0.3.0.0", f.Seconds - 20)); using (var c = WindowsReleaseChecks.Create(f.Channel, () => { roots++; return root; }, transport, new WindowsManifestTrust(f.Archive.Package.CertificateTrust), () => f.Now, true)) {
                    c.Start(); Check(UpdatePulse(c) == null && roots == 0 && !Directory.Exists(root), "Busy factory created storage"); c.PublishIdle(true); Check(UpdatePulse(c).Outcome == WindowsUpdateCheckOutcome.Accepted && roots == 1 && File.Exists(Path.Combine(root, WindowsChannelReplayStore.FileName)), "Factory omitted durable acceptance");
                    f.Now = f.Now.AddDays(1); transport.Fetch = ChannelTransport(f, f.Text("0.2.0.0", f.Seconds - 20)).Fetch; var refused = UpdateManual(c); Check(refused.Outcome == WindowsUpdateCheckOutcome.Failed && refused.Message.Contains("rolled back") && roots == 1 && transport.Metadata == 2 && transport.Signature == 2, "Factory lost/recreated replay owner or accepted lower version"); }
                }
            });
            Test("Windows update actual read-only window has unconfigured monitoring and preserves operation status", () => {
                using (var window = new DeviceWindow(true)) { var flags = BindingFlags.Instance | BindingFlags.NonPublic; var c = (WindowsUpdateCoordinator)typeof(DeviceWindow).GetField("releaseChecks", flags).GetValue(window); var timer = (System.Windows.Forms.Timer)typeof(DeviceWindow).GetField("releaseTimer", flags).GetValue(window); var menu = (ToolStripMenuItem)typeof(DeviceWindow).GetField("releaseStatusMenu", flags).GetValue(window); var status = (TextBox)typeof(DeviceWindow).GetField("status", flags).GetValue(window); status.Text = "preserve operation status";
                    window.Enabled = false; window.Enabled = true; foreach (string field in new[] { "working", "scanning" }) { typeof(DeviceWindow).GetField(field, flags).SetValue(window, true); typeof(DeviceWindow).GetMethod("UpdateActions", flags).Invoke(window, null); typeof(DeviceWindow).GetField(field, flags).SetValue(window, false); }
                    Check(!c.Configured && c.AutomaticEnabled && !timer.Enabled && !menu.Enabled && menu.Text == "Windows releases: channel unavailable" && status.Text == "preserve operation status", "Read-only integration enabled checks/timer or rewrote operation status"); }
            });
        }
    }
}
