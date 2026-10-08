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

Remaining gates include positive official LUT selection/Save and interrupted/failed setup validation, nonempty/offline/connected profiles and history, actual import/resume/completion/Stop, active-transfer close/reveal/Quit, tray click/Quit, cold and silent startup, notifications/hotkey behavior, modern release review, other theme/DPI/accessibility layouts, full vendor notice/catalog/loader and trusted direct distribution acceptance. Positive user-supplied official Luna LUT and genuine camera footage remain absent. Hardware acceptance still requires Ryan to identify the disposable device and destination before any real import/eject/cleanup action. The later existing-tool Save follow-up below closes only its explicitly observed gates; this checkpoint does not satisfy the remaining gates or authorize public release.

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

## Native FFmpeg selection and populated-draft Cancel

The same candidate's FFmpeg picker accepts the existing supplied `ffmpeg.exe` at `C:/Users/rsmit/Documents/Gyroflow-DJI-LUT/ext/ffmpeg-n9.0.2-22-g46d8f462ee-win64-gpl-shared-9.0/bin/ffmpeg.exe`. Its exact path appears only in the FFmpeg field; FFprobe and LUT remain empty, Save and draft controls are restored, and the status says selection changed and Save validates it. Escape discards the populated draft. Reopening shows all three paths empty; a final Escape returns to idle with Refresh/Help enabled.

The first accessibility click returns **“coordinate input geometry is unavailable”**; fresh screenshot coordinate input recovers. The picker focused-element report remains Search Box after clicking File name, so no blind typing is used. Explicit `set_value` on the observed File name edit succeeds and the exact path is checked before Choose. These are retained helper limitations, not successful focus verification or application failures.

`native-ffmpeg-selection-cancel.json`, observed **18:17:45.6197251Z**, binds original PID/start/window, all **601/601** unchanged candidate files, empty configuration/profiles, absent login registration and the unchanged supplied tool SHA-256 **6C03ED874BF9C219D3BA865339B48A106EEF11FAC07E8E53BB599AC59214469D**. This proves positive FFmpeg selection and whole-draft Cancel only. No Save, tool execution, export, LUT copy, new software-suite run, newer vendor-layout approval or physical-device action occurs. Positive FFprobe/LUT selection, setup validation/Save and the other acceptance gates remain open.

## Native supplied-tool Save and persisted-draft Cancel

Both native FFmpeg and FFprobe pickers select the existing supplied executables from the exact tool folder above. Save completes and returns to idle with **“Quick Share setup saved. No export started.”** Reopening restores both exact paths and an empty LUT field. Clear tool selections empties both draft fields; Escape discards that draft, leaves the saved record byte-identical, and a second reopen restores both paths. Final Escape leaves the original instance idle.

This is the first positive setup Save in this native packet. It intentionally leaves one new **486-byte** `C:/Users/rsmit/AppData/Local/EasyEject/Profiles/quick-share-tools.json`, version1, with FFmpeg **540,160 bytes / SHA-256 6C03ED874BF9C219D3BA865339B48A106EEF11FAC07E8E53BB599AC59214469D**, FFprobe **229,888 bytes / SHA-256 FB41BE6F4386DF4625D13EEC932EC926BE4744853759C7C100947EDC7B917096**, and null LUT. Each saved binding independently matches its unchanged supplied executable. Record SHA-256 is **C366C5017700613C17F734FFC01C8AEACC0F386B61EF19558FD8DE57EE94728D** before and after draft clearing/Cancel; `native-saved-quick-share-tools.json` retains an exclusive exact evidence copy. Earlier zero-configuration observations retain their earlier time scope. Direct application settings remain absent; no device profile/defaults/application-settings/history Save or login registration occurs. Older `PrototypeBuild` files are outside these configuration counts and are not edited.

`native-tool-setup-save.json`, observed **18:24:10.1704717Z**, binds the original PID/start/window, **601/601** unchanged candidate files and the native/reopened/persisted-record observations. `set_value` on picker element1662 fails twice with **“element 1662 is not available in cached app state for EasyEject.Modern.exe”** despite fresh observation. Direct screenshot selection of the observed tool rows and Choose succeeds; no blind typing or tool execution is used. The short completed Save does not exercise interrupted/failed validation, Stop or active-worker close behavior. No export, LUT copy, media/device action, new software-suite run or newer vendor-layout acceptance occurs. Positive official LUT/genuine footage and remaining lifecycle/hardware/distribution gates stay open; full goal active.

## Later refused-Save correction and current preview

An actual empty-tool refusal exposes a failure-scroll bug in this original candidate. [Correction evidence](QUICK_SHARE_SETUP_SCROLL.md) records the reproduction, one owned UI overlay, warning-free242/0 selected, native readable refusal/manual retry/issue acknowledgement and retained failed attempts. Original PID6148 is stopped only after idle verification to launch that corrected copy; it is no longer the current running preview. Corrected **PID25172/window7472742** remains idle with the existing saved tool setup and an acknowledged test issue. The later packet's source/bytes differ from this original unmodified e1f307c candidate; earlier counts/preservation/native observations retain their time and source scope. No new full-suite, combined vendor integration, tray Quit, active Stop, real media/device/LUT or trusted distribution acceptance is claimed.

## Corrected-preview settings keyboard and cleanup cancellation

On the same corrected PID25172/start **18:38:52.3954229Z**/window7472742, Settings opens the metadata-cleanup confirmation with **Cancel** visibly the default. Pressing Return dismisses it, leaves cleanup off and restores editing. No enable action or Save occurs. The confirmation describes only regular macOS metadata and empty __MACOSX folders, keeps photos/recordings/device Trash and states that saving starts no cleanup.

Scrolling exposes the shortcut controls while Save/Cancel remain fixed. Enabling the shortcut only in the unsaved draft enables its letter list, showing all nine choices **D/E/F/G/K/M/R/T/X**. Down moves the visible selection from E to F; Return closes the list with F in the draft. Runtime status remains **“Global eject shortcut is off.”** Cancel returns to storage with the draft-discarded explanation; reopening restores shortcut off, disabled letter E and cleanup off. The reopened draft is also cancelled, leaving the app idle.

External packet `C:/Users/rsmit/.codex/EasyEjectBuildTools/NativeSettingsKeyboard-20261008` retains `before.json` and `native-settings-keyboard.json`, observed **18:58:43.1652089Z**. Both configuration files retain exact lengths/hashes, app-settings.json remains absent and the EasyEject.Windows login value remains absent. All **601/601** corrected candidate files still match their manifest. No code/build change or new suite count is claimed. This adds actual keyboard selection/default-cancellation/draft restoration evidence, not live global shortcut registration/delivery, Settings Save/login, busy work, physical storage, other-layout or distribution proof.

Capture limits are retained: an initial screenshot shows another app despite Easy Eject accessibility text; no input follows until explicit target activation and a fresh correct capture. The structured focused_element remains Settings status during the popup, while displayed selection/key outcomes are observable. No text is typed, and immediate transitional snapshots are followed by settled observations before judging results.

## Native Settings Save and live shortcut lifecycle

On corrected PID25172/window7472742, a fresh Settings draft enables only the default E shortcut, leaving login/cleanup/debug off and other choices at their defaults. Explicit Save returns to storage with **“Ctrl + Alt + Shift + E opens the card-removal confirmation.”** Reopening reads the enabled E choice and successful runtime registration status. The real chord while Settings is open leaves that review in place; after Cancel, the same chord reaches **“No camera or emulator cards are connected.”** The displayed inventory contains only protected/internal storage. This exercises the read-only empty-card branch, not a physical-device request or connected-card confirmation.

A fresh Settings draft disables the shortcut and explicitly saves. Runtime status becomes **“Global eject shortcut is off.”** The same chord then leaves the idle summary unchanged. Before the test, app-settings.json is absent. The native Saves create a 168-byte enabled record, SHA-256 **30911C90A9D2D60FD29A821D9ACDA98E0A7FCA7E8E35ADE8B0CD9A9B1037AAED**, then a 169-byte disabled record, SHA-256 **61B55F89832463BF40920DEE58058A193306AEB9A3585262550A7A3A93F54CC5**. Both contain only the seven known properties; every property besides EnableShortcut remains null, preserving its effective default.

After the native disable Save and idle observation, scope/ordinary-ancestor/length/hash/creation-time guards and a read hold denying writes move only that test-created disabled record into recoverable external evidence storage. Reopening reads the original absent-store defaults with shortcut off/disabled E and cleanup off; Cancel leaves the app idle. Existing quick-share-tools.json and issue-state.json retain exact lengths/hashes, and the EasyEject.Windows login value remains absent. All601 candidate files still match their manifest.

External packet `C:/Users/rsmit/.codex/EasyEjectBuildTools/NativeSettingsSave-20261008` retains before/enabled/disabled records, exact saved copies, `recovered-app-settings.json`, `recovery.json` and `native-settings-save.json`, observed **19:04:09.3121009Z**. No production edit/rebuild/new suite count, autostart, cleanup/eject/import/export, system-setting change or publication occurs. This adds actual Settings Save/read-back, shortcut registration, open-editor refusal, idle empty-card delivery and release acceptance. It does not prove connected-card confirmation/removal, hidden/cold-start shortcut delivery, transfer/Quit protection, notification delivery, other layouts, physical hardware/LUT or trusted distribution. Full goal remains active.
