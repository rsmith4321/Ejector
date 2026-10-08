using System;

namespace EasyEject {
    // A balloon is optional presentation, never the terminal operation result.
    // Returning a delivery warning keeps the caller's result and teardown intact.
    internal static class TrayNotificationDelivery {
        internal static string TryShow(bool enabled, bool closed, string title, string message, bool warning,
            Action<int, string, string, bool> show) {
            if (!enabled || closed) return null;
            try {
                if (show == null) throw new ArgumentNullException("show");
                show(5000, Limit(title, 63), Limit(message, 255), warning);
                return null;
            } catch (Exception ex) {
                return "Notification unavailable: " + ex.Message + " Review the result in Easy Eject.";
            }
        }
        static string Limit(string text, int maximum) {
            if (text == null) return "";
            if (text.Length <= maximum) return text;
            int length = maximum;
            if (Char.IsHighSurrogate(text[length - 1]) && Char.IsLowSurrogate(text[length])) length--;
            return text.Substring(0, length);
        }
    }
}
