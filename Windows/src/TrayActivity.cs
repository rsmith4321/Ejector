using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;

namespace EasyEject {
    internal enum TrayOperationKind { Import, Resume, Share, Verify, Eject }
    internal sealed class TrayOperation {
        internal readonly TrayOperationKind Kind;
        internal readonly string Name;
        internal TrayOperation(TrayOperationKind kind, string name) { Kind = kind; Name = name; }
    }
    internal sealed class TrayActivity {
        internal TrayOperation Current { get; private set; }
        internal string Phase { get; private set; }
        internal string Detail { get; private set; }
        internal int? Percent { get; private set; }
        internal bool Stopping { get; private set; }
        internal string Outcome { get; private set; }
        internal bool AwaitingOutcome { get; private set; }
        internal TrayOperation Begin(TrayOperationKind kind, string name, string phase) {
            Current = new TrayOperation(kind, TrayPresentation.Clean(name, 40)); Phase = phase; Detail = ""; Percent = null; Stopping = false; Outcome = ""; AwaitingOutcome = false; return Current;
        }
        internal bool Report(TrayOperation token, string detail, ImportWorkProgress measured = null) {
            if (token == null || !Object.ReferenceEquals(token, Current) || Stopping) return false;
            if (detail != null) Detail = detail;
            if (measured != null) { Phase = measured.Phase; Percent = measured.Phase == "Scanning" || measured.Phase == "Preparing import" || measured.Phase == "No matching media" ? (int?)null : measured.Percent; }
            else if (token.Kind == TrayOperationKind.Share) {
                const string prefix = "Creating sharing copy: "; int value;
                Percent = null;
                if (detail != null && detail.StartsWith(prefix, StringComparison.Ordinal)) {
                    Phase = "Creating sharing copy";
                    if (detail.EndsWith("%", StringComparison.Ordinal) && Int32.TryParse(detail.Substring(prefix.Length, detail.Length - prefix.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0 && value <= 100) Percent = value;
                }
                else Phase = TrayPresentation.Clean(detail, 100);
            } else if (token.Kind != TrayOperationKind.Import) Phase = TrayPresentation.Clean(detail, 100);
            return true;
        }
        internal void RequestStop() { if (Current != null) { Stopping = true; Percent = null; } }
        internal void End(string outcome) {
            if (String.IsNullOrWhiteSpace(outcome)) { if (Current != null) AwaitingOutcome = true; }
            else { Outcome = TrayPresentation.Clean(outcome, 220); AwaitingOutcome = false; }
            Current = null; Phase = Detail = ""; Percent = null; Stopping = false;
        }
    }
    internal sealed class TrayPresentation {
        internal readonly string Tooltip, Heading;
        internal readonly bool Busy, Attention;
        TrayPresentation(string tooltip, string heading, bool busy, bool attention) { Tooltip = tooltip; Heading = heading; Busy = busy; Attention = attention; }
        internal static TrayPresentation Create(TrayActivity activity, bool working, bool scanning, int cards, bool attention) {
            string caption, heading;
            if (activity.Current != null) {
                caption = activity.Stopping ? "Stopping operation" : activity.Percent.HasValue
                    ? (activity.Current.Kind == TrayOperationKind.Share ? "Sharing copy " : "Importing ") + activity.Percent.Value + "%" : activity.Phase;
                heading = activity.Current.Name + ": " + caption + (activity.Percent.HasValue && !activity.Stopping ? " | " + activity.Phase : "");
            } else if (scanning) caption = heading = "Reading connected storage";
            else if (working) { caption = "Reviewing choices"; heading = caption + (String.IsNullOrWhiteSpace(activity.Outcome) ? "" : " | Last operation: " + activity.Outcome); }
            else {
                caption = cards + " candidate card disks";
                heading = String.IsNullOrWhiteSpace(activity.Outcome) ? caption : "Last operation: " + activity.Outcome;
                if (!String.IsNullOrWhiteSpace(activity.Outcome)) caption += " | " + activity.Outcome;
            }
            string suffix = attention ? " | Needs attention" : "";
            // .NET Framework NotifyIcon rejects text longer than 63 UTF-16 code units.
            return new TrayPresentation(Clean("Easy Eject: " + caption, 63 - suffix.Length) + suffix, Clean(heading, 100).Replace("&", "&&"), working || scanning || activity.Current != null, attention);
        }
        internal static string Clean(string value, int limit) {
            value = value ?? ""; var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++) if (Char.IsControl(chars[i])) chars[i] = ' ';
            string result = new string(chars).Trim();
            if (result.Length <= limit) return result;
            int count = Math.Max(0, limit - 1); if (count > 0 && Char.IsHighSurrogate(result[count - 1])) count--;
            return result.Substring(0, count) + "…";
        }
    }
    internal sealed class TrayBusyIcons : IDisposable {
        internal readonly Icon Busy, Attention;
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        internal TrayBusyIcons() { Busy = Draw(false); try { Attention = Draw(true); } catch { Busy.Dispose(); throw; } }
        static Icon Draw(bool attention) {
            using (var bitmap = new Bitmap(16, 16)) {
                using (var graphics = Graphics.FromImage(bitmap)) using (var pen = new Pen(Color.FromArgb(35, 102, 180), 2)) {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias; graphics.Clear(Color.Transparent); graphics.DrawEllipse(pen, 1, 1, 13, 13);
                    graphics.DrawLine(pen, 8, 3, 8, 11); graphics.DrawLine(pen, 4, 7, 8, 11); graphics.DrawLine(pen, 8, 11, 12, 7);
                    if (attention) using (var brush = new SolidBrush(Color.Orange)) { graphics.FillEllipse(brush, 10, 0, 6, 6); }
                }
                IntPtr handle = bitmap.GetHicon(); try { using (var borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); } finally { DestroyIcon(handle); }
            }
        }
        public void Dispose() { Busy.Dispose(); Attention.Dispose(); }
    }
}
