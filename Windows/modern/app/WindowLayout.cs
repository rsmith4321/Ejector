using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace EasyEject.Modern {
    internal static class WindowLayout {
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
        internal static void InitialSize(Window window) {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            uint dpi = GetDpiForWindow(handle);
            double scale = dpi == 0 ? 1 : dpi / 96.0;
            var screen = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            int width = Math.Min((int)Math.Round(1120 * scale), screen.Width);
            int height = Math.Min((int)Math.Round(760 * scale), screen.Height);
            window.AppWindow.Resize(new SizeInt32(width, height));
        }
    }
}
