using System;
using System.Runtime.InteropServices;

namespace EasyEject {
    internal interface IPowerNotificationApi {
        IntPtr Register(IntPtr window);
        bool Unregister(IntPtr registration);
    }
    internal sealed class WindowsPowerNotificationApi : IPowerNotificationApi {
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr RegisterSuspendResumeNotification(IntPtr recipient, uint flags);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool UnregisterSuspendResumeNotification(IntPtr registration);
        public IntPtr Register(IntPtr window) { return RegisterSuspendResumeNotification(window, 0); }
        public bool Unregister(IntPtr registration) { return UnregisterSuspendResumeNotification(registration); }
    }
    // Own the window subscription without retaining a managed native callback.
    // Failed unregister blocks re-registration; a later Stop may release it.
    internal sealed class PowerNotifications : IDisposable {
        readonly IPowerNotificationApi api; IntPtr registration; bool available, disposed;
        internal PowerNotifications() : this(new WindowsPowerNotificationApi()) {}
        internal PowerNotifications(IPowerNotificationApi api) { this.api = api; }
        internal bool Available { get { return !disposed && available; } }
        internal bool Start(IntPtr window) {
            if (disposed) throw new ObjectDisposedException("Power notifications");
            if (!Stop() || window == IntPtr.Zero) return false;
            try { registration = api.Register(window); available = registration != IntPtr.Zero; }
            catch { registration = IntPtr.Zero; available = false; }
            return Available;
        }
        internal bool Stop() {
            available = false;
            if (registration == IntPtr.Zero) return true;
            try { if (!api.Unregister(registration)) return false; }
            catch { return false; }
            registration = IntPtr.Zero; return true;
        }
        public void Dispose() { if (disposed) return; disposed = true; Stop(); }
    }
}
