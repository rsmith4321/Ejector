using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EasyEject {
    // An invisible owned top-level HWND receives the same storage notifications
    // as the classic window. It neither reveals UI nor requests device removal.
    internal sealed class StorageNotificationWindow : NativeWindow, IDisposable {
        readonly WindowsDiscovery discovery; readonly Func<bool> busy;
        readonly Action powerBoundary; readonly PowerNotifications power = new PowerNotifications();
        IntPtr registration; bool started, disposed;
        [StructLayout(LayoutKind.Sequential)] struct BroadcastInterface { public int Size, Type, Reserved; public Guid ClassGuid; public short Name; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr RegisterDeviceNotification(IntPtr recipient, ref BroadcastInterface filter, uint flags);
        [DllImport("user32.dll")] static extern bool UnregisterDeviceNotification(IntPtr handle);
        internal StorageNotificationWindow(WindowsDiscovery discovery, Func<bool> busy) { this.discovery = discovery; this.busy = busy; }
        internal StorageNotificationWindow(WindowsDiscovery discovery, Func<bool> busy, Action powerBoundary) : this(discovery, busy) { this.powerBoundary = powerBoundary; }
        internal bool Available { get { return !disposed && Handle != IntPtr.Zero && registration != IntPtr.Zero && power.Available && discovery.Instances.Versions.Available; } }
        internal void Start() {
            if (disposed) throw new ObjectDisposedException("Storage notifications");
            if (started) return; started = true;
            try {
                CreateHandle(new CreateParams { Caption = "Easy Eject storage notifications", Width = 0, Height = 0 });
                var filter = new BroadcastInterface { Size = Marshal.SizeOf(typeof(BroadcastInterface)), Type = 5, ClassGuid = new Guid("53f56307-b6bf-11d0-94f2-00a0c91efb8b") };
                registration = RegisterDeviceNotification(Handle, ref filter, 0);
                power.Start(Handle);
                discovery.Instances.Start(); discovery.NotificationsAvailable = Available;
            } catch { discovery.NotificationsAvailable = false; throw; }
            finally { discovery.ConnectionChanged(); }
        }
        protected override void WndProc(ref Message message) {
            long change = message.WParam.ToInt64();
            if (message.Msg == 0x218) {
                long before = discovery.Epoch;
                if (discovery.HandlePowerEvent(change)) {
                    if (discovery.Epoch != before && powerBoundary != null) powerBoundary();
                    message.Result = new IntPtr(1); return;
                }
            }
            if (message.Msg == 0x219 && (change == 7 || change == 0x8000 || change == 0x8003 || change == 0x8004)) discovery.ConnectionChanged();
            if (message.Msg == 0x11) {
                bool block;
                try { block = busy(); } catch { block = true; }
                if (block) { message.Result = IntPtr.Zero; return; }
            }
            base.WndProc(ref message);
        }
        protected override void OnHandleChange() {
            base.OnHandleChange();
            if (started && !disposed && Handle == IntPtr.Zero) { power.Stop(); discovery.NotificationsAvailable = false; discovery.ConnectionChanged(); }
        }
        public void Dispose() {
            if (disposed) return; disposed = true;
            power.Dispose();
            discovery.NotificationsAvailable = false; discovery.ConnectionChanged(); discovery.Instances.Dispose();
            if (registration != IntPtr.Zero) { UnregisterDeviceNotification(registration); registration = IntPtr.Zero; }
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}
