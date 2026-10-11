# Native release presentation cancellation retains operations · October 11, 2026

Canceling the actual WinUI `ContentDialog.ShowAsync` operation raises `TaskCanceledException` and removes the release dialog. Existing production finally retains its exact worker or native picker and global review ownership until that operation actually returns. Two fresh isolated sessions prove these cases; neither authenticates or applies an update.

## Engineering boundary

Only `#if UI_TESTS` adds the explicit marker-gated wrapper and cleanup telemetry. It uses the real `ShowAsync` operation and calls its `Cancel()` while the real presenter is busy. Original catch/finally, saved-review admission, `ReleaseView`, native picker and tray Quit remain in use. Normal builds directly await the original dialog and exclude the inspection methods/backend/state. No production behavior changed.

Both sessions run `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-release-presentation-ui-20261011\bin\EasyEject.Modern\Debug\net10.0-windows10.0.26100.0\win-x64\EasyEject.Modern.exe`, with only prior generated review marker/settings/log copied into separate fixtures. Modern/Runtime/coreclr loaded paths and hashes are retained. Inventory shows five internal/protected volumes on one disk, no external camera/card. Normal Release app is not launched.

| Case | Actual native result |
| --- | --- |
| Worker: PID27216, fixture `ui-release-presentation-worker-20261011` | Actual Check starts the deliberately noncancelling generated worker at **19:52:18.9258439Z**. Selector/Check disabled; Stop enabled. |
| Cancel actual presentation at **19:52:26.0858708Z** | Real TaskCanceledException, Busy=true/Nested=false. Modal disappears. Finally enters **19:52:26.0958372Z** with WorkerCompleted=false, Working=true, Ending=true, retained owner/confirmation, CanClose=false. Main update/debug/refresh controls remain disabled. |
| Actual Test tray Quit during wait | Process stays alive; native document/screenshot show “Wait for the operation to finish or cancel it before quitting. Easy Eject stays in the tray.” Before-release traces contain no terminal/disposal/publication. |
| Release exact worker marker | Worker becomes terminal at **19:52:36.023128Z**. Late generated result never presents. Finally records owned operations terminal, then disposal/Working=false/no confirmation at **19:52:36.0281066Z**. Controls recover; actual tray Quit exits **0**, no unhandled exception. |
| Picker: PID28816, fixture `ui-release-presentation-picker-20261011` | Actual Update history → Review history backup opens the real Windows Open dialog. No file/path is selected. |
| Cancel parent presentation at **19:53:18.9056628Z** | Real TaskCanceledException, Busy=true/Nested=true. Parent XAML modal disappears, but same native Open dialog remains and disables parent window. Finally enters **19:53:18.9131715Z**, PickerCompleted=false/Working=true/Ending=true/CanClose=false. Zero backend Started/read/restore actions; no early disposal. |
| Click native picker Cancel | Actual task returns. Only then cleanup finishes the revoked selection, records PickerCompleted=true/Nested=false at **19:53:28.3705304Z**, and disposes/releases ownership at **19:53:28.3731228Z**. Controls recover; zero backend work/presentation. |
| Ordinary Updates & installation afterward | Remains unconfigured with Check/ZIP/folder disabled despite existing inspection marker. Close succeeds; actual tray Quit exits **0**, no unhandled exception. |

Immediate accessibility trees can lag screenshot/document rendering. Settled observations and transition ordering determine subsequent input. Immediate worker Quit tree retains prior Summary, while document/screenshot and recovered tree contain refusal. Retained text after recovery is not ongoing work. Initial helper geometry error was followed by fresh selection/state and one retry; first call had opened the modal, so retry was refused by ordinary review admission. No backend action duplicated. Screenshots inspected directly, not saved/reprocessed.

## Verification

Builds outside OneDrive:

```powershell
Windows/modern/build-ui-tests.ps1 -BuildRoot C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-release-presentation-ui-20261011
Windows/modern/build.ps1 -Test -BuildRoot C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-release-presentation-normal-20261011
```

Both logs: **0 warnings/0 errors**. Existing automatic native lifecycle/tray/editor/busy/idle regressions PASS in `ui-65c6500ec73e4292a8b9c8024aa2f809`, no unhandled exception. Normal selected suite: **246 passed/0 failed**, separate from previous full/tool counts. Normal PE excludes `ShowReviewReleasePresentation`, `RecordReviewReleaseLifetime` and other engineering hooks; production release view remains.

Finalizer verifies cancellation/hold/terminal/disposal ordering, worker Quit refusal before terminal, zero late publication, picker zero work, ordinary entry isolation, exits0, exact live file count/hash/length/write time. Both isolated Profiles contain only exact168-byte settings and416-byte generated log. No issue/profile/history/settings write, payload inspection, network authentication, restore/install, Clipboard/clear/deletion, media/device, OS/login setting, push or publication action.

Evidence root: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-release-presentation-native-20261011`. Launch/finalizer scripts, per-case process/module/exit, native observations, pre-release traces, lifetime/exception records and pins remain. Aggregate `final.json` SHA-256 **24fbbbe17837f017a48585d953c5365c071fd9d8a27929efcd2e964aae9d8ea5**.

Current unsigned ZIP remains **107459831 bytes**, SHA-256 **e941e2c49a9bb88d65d6d24234e6cd2fbcd711350ef2c7cf43105a7e56198bb4**, production/package source **0040e58**. Mac baseline384cb767 exact; login registration absent. Native operation cancellation and owned cleanup are proved, not general XAML rendering failure, configured update trust or accepted restore/install. Full physical/genuine-media/official-LUT/travel/display/normal-loader/signing/publishing gates remain open; full goal active.
