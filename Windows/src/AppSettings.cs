using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace EasyEject {
    [DataContract] internal sealed class AppSettings {
        [DataMember] internal bool? ShowNotifications, DebugLogging;
        [DataMember] internal bool? EnableShortcut;
        [DataMember] internal string ShortcutLetter;
        [DataMember] internal bool? CleanCardsOnEject, WarnOtherStorage;
        [DataMember] internal bool? AutomaticUpdateChecks;
        internal bool NotificationsEnabled { get { return ShowNotifications ?? true; } }
        internal bool DebugEnabled { get { return DebugLogging ?? false; } }
        internal bool ShortcutEnabled { get { return EnableShortcut ?? false; } }
        internal string ShortcutKey { get { return ShortcutLetter ?? "E"; } }
        internal bool CleanupCardsEnabled { get { return CleanCardsOnEject ?? false; } }
        internal bool WarnOtherEnabled { get { return WarnOtherStorage ?? true; } }
        internal bool AutomaticUpdatesEnabled { get { return AutomaticUpdateChecks ?? true; } }
        internal static bool NeedsCleanupConfirmation(AppSettings before, AppSettings after) { return !before.CleanupCardsEnabled && after.CleanupCardsEnabled; }
        internal static AppSettings Load() { return Load(Profiles.Root); }
        internal static AppSettings Load(string root, Action<string> checkpoint = null) { var value = Configuration.Read<AppSettings>(root, "app-settings.json", checkpoint) ?? new AppSettings(); value.Validate(); return value; }
        internal static void Save(AppSettings settings) { Save(settings, Profiles.Root); }
        internal static void Save(AppSettings settings, string root) { if (settings == null) throw new ArgumentNullException("settings"); settings.Validate(); Profiles.WriteJson(settings, typeof(AppSettings), root, "app-settings.json"); }
        internal void Validate() { if (!GlobalHotkey.Supported(ShortcutKey)) throw new SafetyException("Saved shortcut letter is unsupported. Choose a supported letter in Settings."); }
    }
    internal static class Configuration {
        internal static T Read<T>(string root, string name, Action<string> checkpoint = null, int maximumBytes = 16384) where T : class {
            if (maximumBytes <= 0 || maximumBytes > 65536) throw new ArgumentOutOfRangeException("maximumBytes");
            using (var directories = SafePaths.HoldDirectories(root)) {
                if (checkpoint != null) checkpoint("Before configuration read"); directories.Validate();
                string path = Path.Combine(root, name); T value = null; bool exists = true;
                try { if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) throw new SafetyException("Saved application state is a directory, not an ordinary record. Existing data is retained."); }
                catch (FileNotFoundException) { exists = false; }
                if (exists) {
                    using (var stream = SafePaths.ReadFile(path)) {
                        CheckRecord(stream, maximumBytes);
                        value = JsonDocuments.Read<T>(stream, maximumBytes);
                        if (value == null) throw new SafetyException("Saved application settings are invalid.");
                        if (checkpoint != null) checkpoint("After record decode");
                        CheckRecord(stream, maximumBytes);
                    }
                }
                if (checkpoint != null) checkpoint("After configuration read"); directories.Validate(); return value;
            }
        }
        internal static void CheckRecord(FileStream stream, int maximumBytes) {
            var identity = Native.Info(stream.SafeFileHandle);
            if (identity.Links != 1 || (identity.Attributes & 0x1000) != 0) throw new SafetyException("Saved application state is offline or has another file alias. Existing data is retained.");
            if (stream.Length > maximumBytes) throw new SafetyException("Saved application settings are too large to read.");
        }
    }
    [DataContract] internal sealed class AppIssue {
        [DataMember] internal string Message = "", ObservedUtc = "";
        [DataMember] internal bool Dismissed;
        internal bool HasIssue { get { return !String.IsNullOrEmpty(Message); } }
        internal bool NeedsAttention { get { return HasIssue && !Dismissed; } }
        internal void Record(string message) { Message = message.Length > 4096 ? message.Substring(0, 4096) : message; ObservedUtc = DateTime.UtcNow.ToString("o"); Dismissed = false; }
        internal void Dismiss() { Dismissed = true; }
        internal void Clear() { Message = ""; ObservedUtc = ""; Dismissed = false; }
        internal static AppIssue Load() { return Load(Profiles.Root); }
        internal static AppIssue Load(string root, Action<string> checkpoint = null) {
            var value = Configuration.Read<AppIssue>(root, "issue-state.json", checkpoint) ?? new AppIssue();
            DateTime date;
            if (value.Message == null || value.Message.Length > 4096 || (value.HasIssue && !DateTime.TryParse(value.ObservedUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out date))) throw new SafetyException("Saved issue state is invalid.");
            return value;
        }
        internal static void Save(AppIssue issue) { Save(issue, Profiles.Root); }
        internal static void Save(AppIssue issue, string root) { Profiles.WriteJson(issue, typeof(AppIssue), root, "issue-state.json"); }
    }
    internal static class CompletionPresentation {
        internal static string HistoryFailure(Exception failure) {
            return "Import verified; saving its history did not finish: " + failure.Message + Environment.NewLine + "The history action did not change saved media. Review the destination and Import history before retrying.";
        }
        internal static bool ShowChoice(bool askToEject, bool foreground, bool notifications) { return askToEject && (foreground || !notifications); }
        internal static string Notification(bool hasMedia, int removed = 0, int clearedTrash = 0) {
            return (hasMedia ? "Selected files imported and verified." : "No media matches your import options. Photos or skipped files may remain.") + (removed > 0 ? " Permanently removed " + removed + " verified originals." : "") + (clearedTrash > 0 ? " Cleared " + clearedTrash + " verified Trash files." : "") + " Device remains connected. Open Easy Eject to review the import and choose whether to eject.";
        }
    }
}
