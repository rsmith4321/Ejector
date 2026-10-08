# Fresh bundled native acceptance · October 8, 2026

This checkpoint builds and tests the exact committed Windows archive **e1f307c5a67300e039986038cfac59b3f916b7c8**, with no working-tree overlays. Ryan authorized native Easy Eject testing and subsequently resumed the desktop after reboot. The supported Computer Use skill supplied every application launch, input, screenshot and accessibility observation. All builds and fixtures are outside OneDrive. No device import, cleanup, removal or eject was invoked.

The later vendor provenance/notice commits, including **dd13bd2**, are outside this candidate. In particular, these results do not approve the newer 649-file review folder. The full Easy Eject goal remains active.

## Exact source, build and software results

The external packet is `C:/Users/rsmit/.codex/EasyEjectBuildTools/NativeAcceptance-e1f307c-20261008`. `source-scope.json` binds Windows tree **531676e572e50d454a222497ec7e393064351e22** and archive SHA-256 **82ABC3AA5B84E95D53B2B6062134ACD796207CE85C059449D79BC1442700E87B**. `source/Windows` is the extracted archive; dirty concurrent files were excluded and preserved.

The existing cached SDK 10.0.401 prepares the unsigned self-contained x64 review folder with .NET 10.0.12 and Windows App SDK 2.5.1. The exact command is retained in `verify-native-source.ps1`:

```powershell
& C:/Users/rsmit/.codex/EasyEjectBuildTools/NativeAcceptance-e1f307c-20261008/verify-native-source.ps1
```

It invokes the archived `modern/build-review.ps1`, publishes selected and full self-contained contract runners and binds each owned runner's backend to the candidate's exact bytes. Replacement, when necessary, affects only that disposable runner. Framework app/protocol fixtures are compiled with warnings as errors for the full runner; this is not a separate Framework full-suite execution.

Terminal **79639** finished with exit **0**. Durable `verify/result.json` records **17:41:56.2067291Z–17:46:51.9383500Z**, status passed, selected/full exits zero, **242/0 selected** and **1357/0 full default**. The full log explicitly states that no real device eject API was used. Counts overlap and are existing contract checks. Compilation is warning-free. This invocation does not execute the opt-in actual FFmpeg sharing suite.

The review manifest contains **601 files / 284,338,642 bytes**. All files remain exact after software and native checks. Key SHA-256 bindings:

| File | SHA-256 |
| --- | --- |
| EasyEject.Modern.exe | `786AD10735F65BBC544B76E1F6EA5DC6E1E8D34153EDA6165B048B245C434E2E` |
| EasyEject.Modern.dll | `96DA932964FD9EBA297AD8D29B55E0E18B1146BCB19D3324876D10F4D84539A3` |
| EasyEject.Runtime.dll | `59CDC6B4BD0FCB7B82C1A16821888699BE5134CE33FDE4E1BCE03CC802CF3694` |
| coreclr.dll | `128AEE8C62A673D64739E585E3876B61133571CEC83A46240A62581D5465639B` |
| EasyEject.Modern.pri | `FADDBC964E528275BC521B9A86F7443E081607D13FCD599E454BC6BE8E0477BD` |

`verify/full-runtime-observed.json` independently records the live full runner **PID9236** loading its own bundled CLR, matching the candidate CLR hash. `verify/result.json` records its eventual terminal completion; no observation timeout was treated as a reason to restart it.

## Observed native behavior

Supported launch of `review/app/EasyEject.Modern.exe` creates **PID6148**, start **17:46:11.3696838Z**, window **788100**. The app is left running idle on connected storage. No installation or startup registration was performed.

- The Easy Eject title/header icon is visible. The inventory lists five volumes on the internal disk, with the Windows OS volume protected. Import, resume, cleanup and removal actions are disabled. This checks the present internal inventory, not reader/card compatibility.
- Settings opens with notifications on, login unregistered and cleanup/global shortcut off. A notifications-only draft is changed to off. Closing hides the window while the same process survives. Normal second launch retains the original window and draft. The first recapture returns **“no monitor found for window”**; fresh returned-window selection plus activation and recapture succeeds. Automatic foreground activation for that attempt remains unproven.
- Cancel reports **“Settings draft discarded. Saved settings were not changed.”** Reopening shows notifications on again. Scrolling exposes shortcut/update/debug controls while Save and Cancel remain accessible. No settings Save or Windows Startup apps action occurs.
- Import defaults shows Videos only, previews and clean layout on, automatic import/eject prompt/original removal off. Changing only the previews draft and choosing Cancel reports **“Import defaults draft discarded. Saved choices were not changed.”** Reopening restores previews on. No removal/recovery or automatic action is enabled.
- Saved profiles displays its empty state; Import now, Edit and Forget are disabled.
- Quick Share tools opens an owned Cancel-default modal with empty tool paths and accessible, distinctly named FFmpeg/FFprobe/LUT controls. No picker, Save, tool execution or export occurs. Alt+F4 hides the app after the queued close settles; normal second launch reveals the same window with the same modal. Escape dismisses it, reports **“Quick Share setup draft discarded. Previous selections kept.”**, and restores Refresh/information controls and the ordinary selection explanation. This checks modal lifetime, not an active encoder/import or pending Save worker.

One earlier navigation input returned **“window is not a usable app window.”** Fresh discovery found the app hidden while PID6148 remained alive; supported second launch recovered the original window. Its cause is not established. Coordinate inputs from fresh screenshots were used for subsequent navigation. Immediate snapshots sometimes preceded queued input or animations, so settled state was reobserved before judging the result or issuing the next input.

The supported app inventory exposes Easy Eject and Codex windows, but no Explorer/taskbar window. Tray icon click/context-menu Quit remains unrun. No fabricated taskbar target, alternate input path or forced process termination was used.

## Preservation and remaining acceptance

`native-startup.json` records **64 candidate-owned loaded modules**, all matching the review manifest, including the managed app/backend, bundled CLR and WinUI runtime. This is an observed subset, not complete dependency loading, a clean-computer test or publisher trust. Supported screenshots/accessibility trees are in the conversation; no independent screenshot archive is claimed.

`native-hidden-draft.json`, `native-cancel-instance.json`, `native-tools-hidden.json` and `native-final-checkpoint.json` retain process and bounded workflow observations. The final checkpoint at **17:57:15.1195723Z** confirms only the original instance remains, all **601/601** files are exact, conventional EasyEject direct settings and Profiles files are zero, and HKCU's `EasyEject.Windows` Run value remains absent. Configuration absence was checked before and after launch; no profile, defaults, application-settings or sharing-setup Save was invoked. Windows security, power, display and lock settings were untouched.

Remaining gates include positive native picker selection/Save/failure behavior, nonempty/offline/connected profiles and history, actual import/resume/completion/Stop, active-transfer close/reveal/Quit, tray click/Quit, cold and silent startup, notifications/hotkey behavior, modern release review, other theme/DPI/accessibility layouts, full vendor notice/catalog/loader and trusted direct distribution acceptance. Positive user-supplied official Luna LUT and genuine camera footage remain absent. Hardware acceptance still requires Ryan to identify the disposable device and destination before any real import/eject/cleanup action. This checkpoint does not satisfy those gates or authorize public release.

## Native picker, empty history and Help follow-up

The same unmodified **e1f307c** app, **PID6148/window788100**, is checked further without a rebuild or software-suite rerun. Each separately labeled FFmpeg, FFprobe and Official LUT control opens its owned Windows file picker. While a picker is active, the setup content and Save are disabled. Button Cancel for FFmpeg and Escape for FFprobe/LUT return to the existing setup modal, restore its controls and leave all paths empty. Escape then discards the whole setup draft and restores the normal storage controls. No file is selected, tool executed, LUT copied or configuration saved.

The picker initially selects **All Files**. Inspecting the dropdown confirms that **`*.exe`** for FFmpeg and **`*.cube`** for Official LUT are available beside All Files. This is observed UI, not an enforced file-format or supplied-tool trust check. No source change is needed merely because the initial label is All Files. One accessibility input fails **“element 619 is not available in cached app state for EasyEject.Modern.exe”**; fresh screenshot observation and coordinate Cancel succeed. The failed helper input is retained and not described as an application failure or a successful accessibility click.

Empty verified history displays its explanation, disables Verify saved originals, offers no sharing action and dismisses with Escape. Help opens its native dialog; scrolling reaches the final login/update instructions while Close stays fixed and reachable. Escape dismisses Help. Settled accessibility confirms Refresh and Help enabled and the ordinary storage-selection explanation restored. This does not prove nonempty history verification, actual saved-copy sharing or the described import/eject behaviors.

`native-picker-history-help.json` records the final **18:04:35.4100896Z** preservation check: the original process/start/window, **601/601** exact files, zero direct configuration/profile files and absent login registration. The app remains idle on connected storage. This is additional native evidence for the already tested candidate; the later vendor-notice/source/layout work is excluded and preserved. Positive selection/Save and all remaining physical/worker/tray/trust gates above stay open.

## Native defaults acknowledgement cancellation

The same **e1f307c/PID6148/window788100** candidate now has Cancel-only acknowledgement evidence. Opening original-removal review with Videos only selected explains that photos and excluded companions are retained, verified removal skips Trash, and shared defaults affect devices following them, including future devices. Escape dismisses the review and leaves removal off. Changing Videos only to off in the unsaved draft opens a fresh removal review that explicitly includes recognized photos/audio. Its visible Cancel button dismisses the review and leaves removal off.

Device Trash recovery review states the current Windows user plus any explicitly configured Mac owner scope, with no guessed owner, verified recovery before permanent clearing, and independence from ordinary original removal. Escape dismisses it and leaves recovery off. Each acknowledgement displays Cancel as the default and blocks the outer Save while open; Enter-key behavior was not exercised. Neither Enable action was invoked.

Cancel then discards the entire defaults draft. Reopening restores Videos only, previews and clean layout on, with automatic import, eject prompt, original removal and recovery off. A final Cancel returns to idle connected storage; settled accessibility confirms the discard summary, enabled Refresh/Help and disabled device actions. This does not prove positive acknowledgement/Save/persistence, resetting previously enabled removal flags when broadening selection, actual recovery/removal or any real-device workflow.

`native-default-acknowledgements.json`, observed **18:10:33.7783447Z**, binds the original process/start/window, all **601/601** unchanged candidate files, zero direct configuration/profile files and absent login registration. Source comparison reads Mac defaults and Windows defaults/editor tests; no new Mac or defaults-suite execution is claimed. No enabling, Save, import, cleanup, recovery, removal or eject occurred. Concurrent vendor-layout work is preserved and excluded. The full goal and remaining acceptance gates stay open.
