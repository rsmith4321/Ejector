# Native defaults persistence and profile inheritance · October 11, 2026

The isolated WinUI review now has computer-controlled evidence for defaults Save/restart, draft cancellation, safe removal/recovery warning defaults and new-card profile inheritance. This adds foreground evidence without changing the production app or default test count. It does not complete hardware, media/LUT, physical display, notification or distribution acceptance.

## Exact scope

Owned fixture: `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-defaults-native-20261011`.

Engineering executable: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-ui-layout-20261011\bin\EasyEject.Modern\Debug\net10.0-windows10.0.26100.0\win-x64\EasyEject.Modern.exe`. The first launch used a fresh fixture plus `review`; the second used the exact marked fixture plus `review-restart`. The test-only Quit button invokes the actual tray `ContextMenuStrip` item's callback. It is not proof of a click on the OS tray icon. The supported window inventory exposed no taskbar; OS tray-icon and notification interaction remain untested here.

Current loaded modules were independently read from the restarted process: UI SHA-256 `46F2E9E5C1A522F8100F481CCFB6956E34D82882B8A315D3929FF1A689DF387B`, Debug backend `6F38BE334769E31A918D23CACB1E8E5CDA04D40AEE087FC6AB27C7ED9181359F`. These differ from the normal Release/helper backend as recorded in the earlier evidence.

Read source contracts before testing: `ImportDefaultsView.xaml` / `.xaml.cs`, `ImportDefaults.cs`, `ImportDefaultsEdit.cs`, `UiTestHost.cs` and the normal tray callbacks. All input used supported Computer Use. Accessibility sometimes showed the previous frame immediately after input; a fresh observation confirmed each settled result before the next action.

## Native observations

1. Fresh PID **31260** showed Videos only, previews and clean layout on; automatic import, eject prompt and permanent removal off. No saved defaults file existed. Toggling previews off and invoking actual tray Quit retained the editor, unchecked preview choice and same process. The settled status said: “Save or cancel the open editor before quitting. Unsaved choices are kept until you decide.”
2. Clean layout was turned off in the draft. Opening permanent-removal review displayed its full scope and highlighted **Cancel**. Actual Return dismissed it; the removal checkbox remained off. No removal was enabled or executed.
3. Explicit Save displayed **Import defaults saved. No import started.** The isolated `EasyEject\Profiles\import-defaults.json` was 151 bytes with SHA-256 **63B3A66420DC26DB87455F90579832E068B06067431479D5F8970FF3703ACF85**, write time **2026-10-11T16:11:31.2478328Z**. Parsed values: `VideosOnly=true`; `IncludePreviews`, `CleanLayout`, `AutomaticImport`, `AskToEject`, `DeleteOriginals`, `RecoverTrash` all false.
4. Actual idle tray callback Quit exited PID31260. The immediate window inventory briefly retained its old window, but an independent process query proved exit before relaunch. Restart PID **30528** reopened those exact native checkbox states. The saved file retained its hash, bytes and write time.
5. A new All media draft was cancelled. Status confirmed the draft was discarded. Reopening restored video-only choices, and independent checks again found the saved file exact.
6. Scrolling the native defaults pane exposed Trash recovery. Its warning named the current Windows user and explicit Mac owner scope, with **Cancel** highlighted. Actual Return cancelled it and left the recovery checkbox off. Cancelling the whole editor preserved the saved file.
7. Selecting the connected SD card **F:** and opening Import profile showed **Use defaults**. Its disabled options inherited video-only on, previews/clean layout/automatic/eject off. Switching to **Use custom settings** enabled the options with the same starting values. Toggling only previews on and requesting tray Quit preserved the custom draft and refusal message. Cancel discarded it; no profile was enrolled or saved.
8. Idle actual tray callback Quit exited PID30528 normally. Final independent census found neither owned process. The isolated configuration directory contained only `import-defaults.json`, still exact after both draft cancellations and profile review.

No source or destination picker was used, no import/export started, and no card format, source write, eject, original removal or recovery occurred. Normal live preferences were not selected by this host; no before/after live-config preservation claim is inferred without a separate baseline.

## Retained evidence

| File under the owned fixture | SHA-256 |
| --- | --- |
| `defaults-after-save.json` | `4324C6C5A94A50A0A3C679B0B6AA1B25765EA5CED8011742D24D00166E3F83AC` |
| `loaded-review-module-pins.json` | `A8D5CCB603816DAB7132CB1D0051CB51C1F5BEDE623D76807D62CD1D832237A0` |
| `defaults-native-final.json` | `778EB0FAB1568957E57EF97CD863B22D4A97B0237740F8F1E11CAEA0EEF0DE92` |

The final JSON distinguishes computer-control observations from independently measured file/process checks. These are opt-in foreground checks; existing default counts remain **1454/0** each runtime and **246/0** selected modern. No production payload change or full parity-row promotion follows. The full goal remains active and 0.2 publication still requires Ryan's OK.
