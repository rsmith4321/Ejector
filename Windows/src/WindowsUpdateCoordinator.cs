using System;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEject {
    internal enum WindowsUpdateCheckOrigin { Automatic, Manual }
    internal enum WindowsUpdateCheckOutcome { Accepted, Cancelled, Failed }
    internal sealed class WindowsUpdateCheckScope : IDisposable {
        readonly CancellationTokenSource cancellation; readonly object cancellationSync = new object(); int valid = 1; bool disposed;
        internal readonly CancellationToken Token;
        internal readonly long Id, ActivityEpoch, PreferenceEpoch;
        internal readonly WindowsUpdateCheckOrigin Origin;
        internal WindowsUpdateCheckScope(long id, long activity, long preference, WindowsUpdateCheckOrigin origin, CancellationToken caller) {
            Id = id; ActivityEpoch = activity; PreferenceEpoch = preference; Origin = origin; cancellation = CancellationTokenSource.CreateLinkedTokenSource(caller); Token = cancellation.Token;
        }
        internal bool Allowed { get { return Volatile.Read(ref valid) != 0 && !Token.IsCancellationRequested; } }
        internal void Invalidate() { Interlocked.Exchange(ref valid, 0); }
        internal void Cancel() { lock (cancellationSync) { if (disposed) return; try { cancellation.Cancel(); } catch (AggregateException) { /* Every callback fault is observed; revoked authority remains invalid. */ } } }
        public void Dispose() { Invalidate(); lock (cancellationSync) { if (disposed) return; disposed = true; cancellation.Dispose(); } }
    }
    internal sealed class WindowsUpdateCheckResult {
        internal readonly WindowsUpdateCheckOutcome Outcome; internal readonly WindowsUpdateCheckOrigin Origin;
        internal readonly WindowsChannelOffer Offer; internal readonly string Message;
        internal readonly WindowsUpdateCoordinator Owner; internal readonly long Id, ActivityEpoch, PreferenceEpoch;
        internal int Presented;
        internal WindowsUpdateCheckResult(WindowsUpdateCoordinator owner, WindowsUpdateCheckScope scope, WindowsUpdateCheckOutcome outcome, WindowsChannelOffer offer, string message) {
            Owner = owner; Id = scope.Id; ActivityEpoch = scope.ActivityEpoch; PreferenceEpoch = scope.PreferenceEpoch; Origin = scope.Origin; Outcome = outcome; Offer = offer; Message = message;
        }
    }
    // No Form access on workers. The foreground publishes activity, and each captured
    // authority is permanently revoked by any intervening busy/dialog/scan transition.
    internal sealed class WindowsUpdateCoordinator : IDisposable {
        readonly object sync = new object(); readonly bool configured;
        readonly Func<WindowsUpdateCheckScope, Task<WindowsChannelOffer>> check; readonly Func<DateTime> clock; readonly TimeSpan interval;
        readonly Func<WindowsChannelReplayStore> replay;
        bool enabled, started, idle, disposed, reviewOpen; long id, activityEpoch, preferenceEpoch;
        DateTime nextAutomatic = new DateTime(0, DateTimeKind.Utc); WindowsUpdateCheckScope active;
        internal WindowsUpdateCoordinator(bool configured, Func<WindowsUpdateCheckScope, Task<WindowsChannelOffer>> check, Func<DateTime> clock, TimeSpan interval, bool automatic, Func<WindowsChannelReplayStore> replay = null) {
            if (configured && check == null || clock == null || interval < TimeSpan.FromMinutes(1) || interval > TimeSpan.FromDays(7)) throw new ArgumentException("Windows release checks require an owned callback, UTC clock and bounded cadence.");
            this.configured = configured; this.check = check; this.clock = clock; this.interval = interval; enabled = automatic; this.replay = replay;
        }
        internal bool ReplayRecoveryConfigured { get { lock (sync) return !disposed && configured && replay != null; } }
        internal WindowsChannelReplayStore ReplayStore() {
            lock (sync) { if (disposed || !ReplayRecoveryConfigured) throw new SafetyException("Windows update-history recovery is not configured or is closed."); }
            return replay();
        }
        internal bool Configured { get { return configured; } }
        internal bool Checking { get { lock (sync) return active != null; } }
        internal bool AutomaticEnabled { get { lock (sync) return enabled; } }
        internal string Status {
            get { lock (sync) {
                if (!configured) return WindowsUpdateSafety.ChannelStatus;
                if (disposed) return "Windows release monitoring is closed.";
                if (active != null) return active.Allowed ? "Checking signed Windows release metadata. No archive is downloaded or installed." : "Release check cancelled; waiting for its owned request to stop.";
                if (!idle) return "Windows release checks wait for imports, ejection, scans and open dialogs to finish.";
                return enabled ? "Automatic signed Windows metadata checks are enabled. Installation remains manual." : "Automatic Windows release checks are off. A manual check remains available.";
            } }
        }
        internal void Start() { lock (sync) { if (!disposed) started = true; } }
        internal void SetReviewOpen(bool value) {
            WindowsUpdateCheckScope revoked = null;
            lock (sync) {
                if (disposed || reviewOpen == value) return; reviewOpen = value; activityEpoch++;
                if (active != null) { revoked = active; revoked.Invalidate(); }
            }
            if (revoked != null) revoked.Cancel();
        }
        internal void PublishIdle(bool value) {
            WindowsUpdateCheckScope revoked = null;
            lock (sync) { if (disposed || idle == value) return; idle = value; activityEpoch++; if (!value && active != null) { revoked = active; revoked.Invalidate(); } }
            if (revoked != null) revoked.Cancel();
        }
        internal void SetAutomatic(bool value) {
            WindowsUpdateCheckScope revoked = null;
            lock (sync) {
                if (disposed || enabled == value) return; enabled = value; preferenceEpoch++;
                if (value) nextAutomatic = new DateTime(0, DateTimeKind.Utc);
                else if (active != null && active.Origin == WindowsUpdateCheckOrigin.Automatic) { revoked = active; revoked.Invalidate(); }
            }
            if (revoked != null) revoked.Cancel();
        }
        DateTime Now() { var now = clock(); if (now.Kind != DateTimeKind.Utc || now < new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc) || now > DateTime.MaxValue.Subtract(interval)) throw new SafetyException("Windows release scheduling requires a valid bounded UTC clock."); return now; }
        WindowsUpdateCheckScope Capture(WindowsUpdateCheckOrigin origin, CancellationToken caller) {
            lock (sync) {
                if (disposed) throw new ObjectDisposedException("WindowsUpdateCoordinator"); caller.ThrowIfCancellationRequested();
                if (!configured) { if (origin == WindowsUpdateCheckOrigin.Automatic) return null; throw new SafetyException(WindowsUpdateSafety.ChannelStatus); }
                if (!idle || active != null) { if (origin == WindowsUpdateCheckOrigin.Automatic) return null; throw new SafetyException(active != null ? "A Windows release check is already in progress." : "Finish the active import, eject, scan or dialog before checking Windows releases."); }
                if (origin == WindowsUpdateCheckOrigin.Automatic && (!started || !enabled || reviewOpen)) return null;
                var now = Now(); if (origin == WindowsUpdateCheckOrigin.Automatic && now < nextAutomatic) return null;
                // Consume an admitted attempt, including cancellation/failure. An idle
                // timer must not silently retry every second after a stopped request.
                nextAutomatic = now.Add(interval); active = new WindowsUpdateCheckScope(++id, activityEpoch, preferenceEpoch, origin, caller); return active;
            }
        }
        internal Task<WindowsUpdateCheckResult> PulseAsync() { var scope = Capture(WindowsUpdateCheckOrigin.Automatic, CancellationToken.None); return scope == null ? Task.FromResult<WindowsUpdateCheckResult>(null) : Run(scope); }
        internal Task<WindowsUpdateCheckResult> CheckManualAsync(CancellationToken caller) { return Run(Capture(WindowsUpdateCheckOrigin.Manual, caller)); }
        async Task<WindowsUpdateCheckResult> Run(WindowsUpdateCheckScope scope) {
            WindowsChannelOffer offer = null; Exception error = null;
            try { if (!scope.Allowed) throw new OperationCanceledException(scope.Token); var operation = check(scope); if (operation == null) throw new SafetyException("Windows release callback returned no owned operation."); offer = await operation.ConfigureAwait(false); if (offer == null) throw new SafetyException("Windows release check returned no authenticated offer."); offer.RequireTime(); }
            catch (Exception ex) { error = ex; }
            WindowsUpdateCheckResult result;
            lock (sync) {
                bool current = !disposed && idle && activityEpoch == scope.ActivityEpoch && Object.ReferenceEquals(active, scope) && scope.Allowed && (scope.Origin != WindowsUpdateCheckOrigin.Automatic || enabled && preferenceEpoch == scope.PreferenceEpoch);
                var outcome = !current || error is OperationCanceledException ? WindowsUpdateCheckOutcome.Cancelled : error == null ? WindowsUpdateCheckOutcome.Accepted : WindowsUpdateCheckOutcome.Failed;
                result = new WindowsUpdateCheckResult(this, scope, outcome, outcome == WindowsUpdateCheckOutcome.Accepted ? offer : null, outcome == WindowsUpdateCheckOutcome.Cancelled ? "Windows release check cancelled. No installation occurred; another attempt is manual or follows the configured cadence." : error == null ? "Authenticated Windows release " + offer.Release.Version + ". Review it before any manual installation." : "Windows release check failed: " + error.Message);
                if (Object.ReferenceEquals(active, scope)) active = null;
            }
            scope.Dispose(); return result;
        }
        // Foreground delivery is separate from worker completion. A queued callback
        // must still belong to this host/epoch/request and must never show twice.
        internal bool TryPresent(WindowsUpdateCheckResult result) {
            lock (sync) {
                if (result == null || result.Owner != this || disposed || !idle || result.ActivityEpoch != activityEpoch || result.Id != id || result.Outcome == WindowsUpdateCheckOutcome.Cancelled || result.Origin == WindowsUpdateCheckOrigin.Automatic && (!enabled || result.PreferenceEpoch != preferenceEpoch)) return false;
                return Interlocked.CompareExchange(ref result.Presented, 1, 0) == 0;
            }
        }
        public void Dispose() {
            WindowsUpdateCheckScope revoked;
            lock (sync) { if (disposed) return; disposed = true; idle = false; activityEpoch++; revoked = active; if (revoked != null) revoked.Invalidate(); }
            // The executing request retains its slot/resources until terminal. Closing
            // a host revokes results immediately without claiming the request finished.
            if (revoked != null) revoked.Cancel();
        }
    }
    internal static class WindowsReleaseChecks {
        // Explicit application-code approval is still pending hardware/signing proof.
        internal static ApprovedWindowsChannel ProductionChannel { get { return null; } }
        // A modern multi-file artifact needs its own approved package/publisher policy.
        // It must never silently inherit the classic six-file Framework channel.
        internal static ApprovedWindowsChannel ModernProductionChannel { get { return null; } }
        internal static ApprovedModernWindowsComponents ModernProductionComponents { get { return null; } }
        internal const string ModernHistoryDirectory = "modern-update-history";
        internal static WindowsUpdateCoordinator CreateModern(bool automatic) { return CreateModern(ModernProductionChannel, () => Profiles.Root, new WindowsChannelHttpTransport(), new WindowsManifestTrust(), () => DateTime.UtcNow, automatic); }
        internal static WindowsUpdateCoordinator CreateModern(ApprovedWindowsChannel channel, Func<string> root, IWindowsChannelTransport transport, IWindowsManifestEnvelopeTrust trust, Func<DateTime> clock, bool automatic) {
            if (root == null) throw new ArgumentNullException("root");
            if (channel != null && !channel.IsModern) throw new SafetyException("Modern update checks require an independently approved modern channel policy.");
            return Create(channel, () => System.IO.Path.Combine(root(), ModernHistoryDirectory), transport, trust, clock, automatic);
        }
        internal static WindowsUpdateCoordinator Create(bool readOnly, bool automatic) { return Create(readOnly ? null : ProductionChannel, () => Profiles.Root, new WindowsChannelHttpTransport(), new WindowsManifestTrust(), () => DateTime.UtcNow, automatic); }
        internal static WindowsUpdateCoordinator Create(ApprovedWindowsChannel channel, Func<string> root, IWindowsChannelTransport transport, IWindowsManifestEnvelopeTrust trust, Func<DateTime> clock, bool automatic) {
            if (root == null || transport == null || trust == null) throw new ArgumentNullException("Windows release dependencies");
            var replay = new Lazy<WindowsChannelReplayStore>(() => new WindowsChannelReplayStore(root()), LazyThreadSafetyMode.ExecutionAndPublication);
            Func<WindowsUpdateCheckScope, Task<WindowsChannelOffer>> action = channel == null ? null : (Func<WindowsUpdateCheckScope, Task<WindowsChannelOffer>>)(scope => Task.Run(async () => {
                scope.Token.ThrowIfCancellationRequested(); if (!scope.Allowed) throw new SafetyException("Windows release scope was revoked before configuration access.");
                var discovery = new WindowsChannelDiscovery(channel, transport, trust, clock, () => scope.Allowed, replay.Value); return await discovery.CheckAsync(scope.Token).ConfigureAwait(false);
            }));
            return new WindowsUpdateCoordinator(channel != null, action, clock, TimeSpan.FromDays(1), automatic, channel == null ? null : (Func<WindowsChannelReplayStore>)(() => replay.Value));
        }
    }
}
