using System;
using System.Threading;
using System.Windows.Forms;

namespace EasyEject {
    // Recheck exact registration and UI ownership on delivery; a queued shortcut
    // cannot outlive reconfiguration/shutdown or execute while a review is busy.
    internal sealed class HotkeyDispatch : IDisposable {
        readonly GlobalHotkey hotkey; readonly Func<bool> busy; readonly Func<Action, bool> queue; readonly Action request;
        long generation; bool queued, disposed;
        internal HotkeyDispatch(IHotkeyApi api, Func<bool> busy, Func<Action, bool> queue, Action request) {
            hotkey = new GlobalHotkey(api); this.busy = busy; this.queue = queue; this.request = request;
        }
        internal string Status { get { return hotkey.Status; } }
        internal bool Configure(IntPtr handle, bool enabled, string letter) {
            if (disposed) throw new ObjectDisposedException("Shortcut"); generation++; queued = false; return hotkey.Configure(handle, enabled, letter);
        }
        bool Blocked { get { try { return disposed || busy(); } catch { return true; } } }
        internal bool Receive(IntPtr id, IntPtr payload) {
            if (queued || !hotkey.Matches(id, payload, Blocked)) return false;
            long current = generation; queued = true; int delivered = 0, valid = 1;
            try {
                if (queue(() => {
                    if (Interlocked.Exchange(ref delivered, 1) != 0 || Volatile.Read(ref valid) == 0 || disposed || current != generation) return;
                    queued = false; if (hotkey.Matches(id, payload, Blocked)) request();
                })) return true;
                Volatile.Write(ref valid, 0); queued = false; return false;
            } catch { Volatile.Write(ref valid, 0); queued = false; throw; }
        }
        internal void WindowDestroyed() { generation++; queued = false; hotkey.WindowDestroyed(); }
        public void Dispose() { if (disposed) return; disposed = true; generation++; queued = false; hotkey.Dispose(); }
    }
    internal sealed class HotkeyMessageWindow : NativeWindow, IDisposable {
        readonly HotkeyDispatch dispatch; bool disposed;
        internal HotkeyMessageWindow(Func<bool> busy, Func<Action, bool> queue, Action request) { dispatch = new HotkeyDispatch(new WindowsHotkeyApi(), busy, queue, request); }
        internal string Status { get { return dispatch.Status; } }
        internal bool Configure(bool enabled, string letter) {
            if (disposed) throw new ObjectDisposedException("Shortcut window");
            if (enabled && Handle == IntPtr.Zero) CreateHandle(new CreateParams { Caption = "Easy Eject shortcut", Width = 0, Height = 0 });
            return dispatch.Configure(Handle, enabled, letter);
        }
        protected override void WndProc(ref Message message) {
            if (message.Msg == 0x312) { dispatch.Receive(message.WParam, message.LParam); message.Result = IntPtr.Zero; return; }
            base.WndProc(ref message);
        }
        protected override void OnHandleChange() { base.OnHandleChange(); if (Handle == IntPtr.Zero && dispatch != null) dispatch.WindowDestroyed(); }
        public void Dispose() { if (disposed) return; disposed = true; dispatch.Dispose(); if (Handle != IntPtr.Zero) DestroyHandle(); }
    }
}
