using System;
using System.Runtime.InteropServices;

namespace EasyEject {
    internal interface IHotkeyApi { bool Register(IntPtr window, int id, uint modifiers, uint key); bool Unregister(IntPtr window, int id); }
    internal sealed class WindowsHotkeyApi : IHotkeyApi {
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] static extern bool UnregisterHotKey(IntPtr window, int id);
        public bool Register(IntPtr window, int id, uint modifiers, uint key) { return RegisterHotKey(window, id, modifiers, key); }
        public bool Unregister(IntPtr window, int id) { return UnregisterHotKey(window, id); }
    }
    internal sealed class GlobalHotkey : IDisposable {
        internal static readonly string[] Letters = { "D", "E", "F", "G", "K", "M", "R", "T", "X" };
        internal const int Id = 0x4545; internal const uint Modifiers = 0x4007; // Ctrl + Alt + Shift, no repeat.
        readonly IHotkeyApi api; IntPtr window; uint key; bool accepting;
        internal bool Registered { get; private set; }
        internal string Status { get; private set; }
        internal GlobalHotkey(IHotkeyApi api) { this.api = api; Status = "Global eject shortcut is off."; }
        internal static bool Supported(string letter) { return Array.IndexOf(Letters, letter) >= 0; }
        internal bool Configure(IntPtr handle, bool enabled, string letter) {
            if (!Stop()) return false;
            if (!enabled) return true;
            if (handle == IntPtr.Zero || !Supported(letter)) { Status = "Choose a supported shortcut letter."; return false; }
            window = handle; key = letter[0];
            Registered = api.Register(window, Id, Modifiers, key);
            accepting = Registered;
            Status = Registered ? "Ctrl + Alt + Shift + " + letter + " opens the card-removal confirmation." : "Shortcut unavailable. Choose another letter or use Eject cards in the menu.";
            if (!Registered) { window = IntPtr.Zero; key = 0; }
            return Registered;
        }
        internal bool Matches(IntPtr id, IntPtr payload, bool busy) {
            long packed = payload.ToInt64();
            return Registered && accepting && !busy && id.ToInt64() == Id && (packed & 0xffff) == (Modifiers & 0x3fff) && ((packed >> 16) & 0xffff) == key;
        }
        internal bool Stop() {
            accepting = false;
            if (Registered && !api.Unregister(window, Id)) { Status = "Shortcut could not be released. Restart Easy Eject before changing it."; return false; }
            Registered = false; window = IntPtr.Zero; key = 0; Status = "Global eject shortcut is off."; return true;
        }
        internal void WindowDestroyed() { Registered = false; accepting = false; window = IntPtr.Zero; key = 0; Status = "Global eject shortcut is off."; }
        public void Dispose() { Stop(); }
    }
}
