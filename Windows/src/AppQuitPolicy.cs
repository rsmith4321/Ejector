namespace EasyEject {
    internal enum AppCloseAction { HideToTray, Exit, StayOpen }

    // Shared tray lifecycle (Mac AppLifecycle/EjectorApp parity) for both interfaces.
    // Closing the window only hides it to the tray, even during work, so workers, dialogs
    // and drafts stay alive. Only an explicit tray Quit exits, and Quit is refused while an
    // import, eject, sharing or other work, a modal review or an unsaved editor is active.
    // A refused Quit is forgotten, so a later ordinary close hides again instead of exiting.
    internal static class AppQuitPolicy {
        internal const string BusyMessage = "Wait for the operation to finish or cancel it before quitting. Easy Eject stays in the tray.";
        internal const string EditorMessage = "Save or cancel the open editor before quitting. Unsaved choices are kept until you decide.";

        internal static AppCloseAction QuitRequested(bool work, bool openEditor) {
            return work || openEditor ? AppCloseAction.StayOpen : AppCloseAction.Exit;
        }

        internal static AppCloseAction Closing(bool quitRequested, bool userClose, bool work) {
            if (!quitRequested && userClose) return AppCloseAction.HideToTray;
            return work ? AppCloseAction.StayOpen : AppCloseAction.Exit;
        }

        internal static string Refusal(bool work, bool openEditor) {
            return work ? BusyMessage : openEditor ? EditorMessage : null;
        }
    }
}
