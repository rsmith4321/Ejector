using System;

namespace EasyEject {
    // Capture normal wakeups even before UI initialization; duplicate login does
    // not reveal. Initialization/monitoring are owned once and shutdown revokes UI.
    internal sealed class TrayStartupGate : IDisposable {
        readonly object sync = new object(); bool starting, ready, pending, disposed;
        WindowReopenRequests reopen;
        internal bool Request(bool startup) {
            WindowReopenRequests target;
            lock (sync) {
                if (disposed) return false; if (startup) return true;
                if (!ready) { pending = true; return true; } target = reopen;
            }
            return target.Request();
        }
        internal void Start(bool startup, Action initialize, Action monitor, Func<Action, bool> dispatch, Action reveal) {
            lock (sync) { if (disposed || starting || ready) throw new SafetyException("This tray launch already started or closed."); starting = true; }
            try {
                initialize(); monitor(); bool show; WindowReopenRequests target;
                lock (sync) {
                    if (disposed) throw new ObjectDisposedException("Tray launch");
                    reopen = target = new WindowReopenRequests(dispatch, reveal); ready = true; show = !startup || pending; pending = false;
                }
                if (show) target.Request();
            } catch { Dispose(); throw; }
        }
        public void Dispose() { WindowReopenRequests target; lock (sync) { if (disposed) return; disposed = true; pending = false; target = reopen; } if (target != null) target.Dispose(); }
    }
}
