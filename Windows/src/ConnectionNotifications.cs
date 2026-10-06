using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace EasyEject {
    internal sealed class ConnectionVersions {
        readonly object gate = new object(); readonly Dictionary<string, long> versions = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long era; bool available, faulted;
        internal bool Available { get { lock (gate) return available; } }
        internal void Begin() { lock (gate) { versions.Clear(); era++; available = false; faulted = false; } }
        internal void Enable() { lock (gate) available = !faulted; }
        internal void Disable() { lock (gate) { available = false; faulted = true; era++; } }
        internal void Note(string instance) {
            lock (gate) {
                if (String.IsNullOrWhiteSpace(instance) || instance.Length >= 200 || (!versions.ContainsKey(instance) && versions.Count >= 8192)) { available = false; faulted = true; era++; return; }
                long current; versions.TryGetValue(instance, out current); versions[instance] = current + 1;
            }
        }
        internal string Stamp(IEnumerable<string> instances) {
            lock (gate) {
                if (!available) return "";
                var text = new StringBuilder(era.ToString(System.Globalization.CultureInfo.InvariantCulture));
                foreach (string id in instances.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) { long value; versions.TryGetValue(id, out value); text.Append('\n').Append(id.ToUpperInvariant()).Append('=').Append(value); }
                using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
            }
        }
    }
    internal sealed class InstanceNotifications : IDisposable {
        internal readonly ConnectionVersions Versions = new ConnectionVersions();
        readonly Action changed; readonly Callback callback; IntPtr registration; volatile bool stopped = true;
        // Retain a callback until native unregistration succeeds, even if its owner closes.
        static readonly List<InstanceNotifications> pendingUnregister = new List<InstanceNotifications>();
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint Callback(IntPtr registration, IntPtr context, uint action, IntPtr data, uint size);
        [DllImport("cfgmgr32.dll")] static extern uint CM_Register_Notification(IntPtr filter, IntPtr context, Callback callback, out IntPtr registration);
        [DllImport("cfgmgr32.dll")] static extern uint CM_Unregister_Notification(IntPtr registration);
        internal string Error = "";
        internal InstanceNotifications(Action changed) { this.changed = changed; callback = Receive; }
        internal void Start() {
            if (!Stop()) return;
            Versions.Begin(); stopped = false;
            // x64 CM_NOTIFY_FILTER: four DWORDs + union containing WCHAR InstanceId[200].
            IntPtr filter = Marshal.AllocHGlobal(416);
            try {
                Marshal.Copy(new byte[416], 0, filter, 416); Marshal.WriteInt32(filter, 0, 416); Marshal.WriteInt32(filter, 4, 2); Marshal.WriteInt32(filter, 8, 2);
                uint result = CM_Register_Notification(filter, IntPtr.Zero, callback, out registration);
                if (result == 0 && registration != IntPtr.Zero) { Versions.Enable(); Error = ""; }
                else { Versions.Disable(); Error = "Device-instance notifications unavailable (CONFIGRET 0x" + result.ToString("X") + ")."; }
            } catch (Exception ex) { Versions.Disable(); Error = "Device-instance notifications unavailable: " + ex.Message; }
            finally { Marshal.FreeHGlobal(filter); }
        }
        uint Receive(IntPtr handle, IntPtr context, uint action, IntPtr data, uint size) {
            if (stopped) return 0;
            try {
                string id = Decode(action, data, size);
                if (id == null) Versions.Disable(); else Versions.Note(id);
                changed(); // Counter update only: no UI, scans, blocking I/O or removal in this callback.
            } catch { Versions.Disable(); try { changed(); } catch {} }
            return 0;
        }
        internal static string Decode(uint action, IntPtr data, uint size) {
            if (action < 7 || action > 9 || data == IntPtr.Zero || size < 10 || size > 65536 || Marshal.ReadInt32(data, 0) != 2 || Marshal.ReadInt32(data, 4) != 0) return null;
            int count = Math.Min(200, (int)(size - 8) / 2); string text = Marshal.PtrToStringUni(IntPtr.Add(data, 8), count); int end = text.IndexOf('\0');
            if (end < 1 || end >= 200) return null;
            string id = text.Substring(0, end); return id.Contains("\\") ? id : null;
        }
        internal bool Stop() {
            stopped = true; Versions.Disable();
            if (registration == IntPtr.Zero) return true;
            bool released = false;
            try { released = CM_Unregister_Notification(registration) == 0; } catch {}
            lock (pendingUnregister) {
                if (!released) { if (!pendingUnregister.Contains(this)) pendingUnregister.Add(this); Error = "Device-instance notification unregistration failed. Grouped removal stays unavailable until restart."; return false; }
                registration = IntPtr.Zero; pendingUnregister.Remove(this); return true;
            }
        }
        public void Dispose() { Stop(); GC.KeepAlive(callback); }
    }
}
