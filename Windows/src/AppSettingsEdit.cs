using System;
using System.Threading;

namespace EasyEject {
    internal enum AppSettingChoice { Notifications, Debug, Shortcut, WarnOther, AutomaticUpdates }
    internal sealed class AppSettingsCleanupReview {
        internal readonly AppSettingsEdit Owner; internal readonly long Revision; internal int Used;
        internal AppSettingsCleanupReview(AppSettingsEdit owner, long revision) { Owner = owner; Revision = revision; }
    }
    // An owned draft; cancelling or editing cannot grant cleanup or publish settings.
    internal sealed class AppSettingsEdit {
        readonly Action<AppSettings> save; readonly AppSettings original;
        AppSettings value; long revision; int phase;
        internal bool Saving { get { return Volatile.Read(ref phase) == 1; } }
        internal bool Open { get { return Volatile.Read(ref phase) == 0; } }
        internal AppSettingsEdit(AppSettings before, Action<AppSettings> save) {
            if (before == null || save == null) throw new ArgumentNullException("Settings dependencies"); before.Validate();
            original = Copy(before); value = Copy(before); this.save = save;
        }
        internal static AppSettings Copy(AppSettings p) {
            return new AppSettings { ShowNotifications = p.ShowNotifications, DebugLogging = p.DebugLogging, EnableShortcut = p.EnableShortcut,
                ShortcutLetter = p.ShortcutLetter, CleanCardsOnEject = p.CleanCardsOnEject, WarnOtherStorage = p.WarnOtherStorage, AutomaticUpdateChecks = p.AutomaticUpdateChecks };
        }
        internal AppSettings Snapshot() { return Copy(value); }
        void RequireOpen() { if (!Open) throw new SafetyException("This settings draft closed or is being saved. Open a fresh review."); }
        internal void Set(AppSettingChoice choice, bool enabled) {
            RequireOpen();
            switch (choice) {
                case AppSettingChoice.Notifications: value.ShowNotifications = enabled; break;
                case AppSettingChoice.Debug: value.DebugLogging = enabled; break;
                case AppSettingChoice.Shortcut: value.EnableShortcut = enabled; break;
                case AppSettingChoice.WarnOther: value.WarnOtherStorage = enabled; break;
                case AppSettingChoice.AutomaticUpdates: value.AutomaticUpdateChecks = enabled; break;
                default: throw new ArgumentOutOfRangeException("choice");
            }
            revision++;
        }
        internal void SetLetter(string letter) { RequireOpen(); if (!GlobalHotkey.Supported(letter)) throw new SafetyException("Choose a supported shortcut letter."); value.ShortcutLetter = letter; revision++; }
        internal void DisableCleanup() { RequireOpen(); value.CleanCardsOnEject = false; revision++; }
        internal AppSettingsCleanupReview ReviewCleanup() { RequireOpen(); return new AppSettingsCleanupReview(this, revision); }
        internal bool AcceptCleanup(AppSettingsCleanupReview review, bool confirm) {
            if (review == null || review.Owner != this || Interlocked.Exchange(ref review.Used, 1) != 0 || !Open || review.Revision != revision || !confirm) return false;
            value.CleanCardsOnEject = true; revision++; return true;
        }
        internal bool CleanupNeedsReview { get { return AppSettings.NeedsCleanupConfirmation(original, value); } }
        internal AppSettings Save() {
            if (Interlocked.CompareExchange(ref phase, 1, 0) != 0) throw new SafetyException("This settings draft closed or is being saved. Open a fresh review.");
            try { var frozen = Snapshot(); frozen.Validate(); save(Copy(frozen)); return Copy(frozen); }
            finally { Interlocked.Exchange(ref phase, 2); }
        }
        internal void Cancel() { if (Interlocked.CompareExchange(ref phase, 2, 0) == 1) throw new InvalidOperationException("The settings worker still owns this draft."); }
    }
}
