# Windows foreground acceptance

This records actual foreground observations separately from hidden renders and fixture tests. The full scope remains [PARITY.md](PARITY.md), against Mac baseline `384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c`. The Windows port is not complete.

## Modern WinUI preview · October 7, 09:20–09:25 EDT

Source `4ab69f06268c3a7884a567a8c9f9bd51243ff1d1` is the new read-only WinUI 3 preview, not the complete modern port. Two brief input slots were explicitly coordinated with the access task and released afterward. Only the preview's own returned window received input.

The first preview launched as PID **5452**, started **09:20:09.234769 EDT**, HWND **15927262**. Actual screenshot/accessibility showed the five internal volumes and protected OS C:, dark Fluent cards, Refresh and the explicit interface-preview/no-device-change label. The summary correctly retained incomplete inventory status. Alt+F4 hid the window; a fresh subsequent enumeration found no preview window and the same process/start time remained alive. Ordinary second launch restored the same HWND/PID/start time, without follow-up activation or an extra persistent instance. This proves close-hide/ordinary reopen for the read-only preview, not active-dialog/operation/startup behavior.

Ryan approved the appearance and requested smaller headings. The separately built font revision launched as PID **31824**, started **09:25:03.023725 EDT**, HWND **7406064**. Its actual screenshot shows the 22/24 headings and 16 drive names, Connected storage fitting one line, and the complete tray instruction wrapping over two lines. No Windows display/font/scale setting changed. It remains visible and idle. Replacing the exact old isolated read-only process after path/start-time/apphost-hash checks was engineering cleanup; it is not tray Quit evidence.

| Preview artifact | SHA-256 |
| --- | --- |
| Apphost `EasyEject.Modern.exe`, both builds | `45f0a4efe101a956d6d2a7d5679224c5b81fce7cd1dd7ac1eb195e608cb3f564` |
| Initial managed payload `ModernBuild/.../EasyEject.Modern.dll` | `471cd34bff92e59dbb3b153ba18104ba722a30c89c8c8f58c3ac907ce51de780` |
| Inspected font payload `ModernBuildFontReview/.../EasyEject.Modern.dll` | `35d78947317eaae15fb23ecce65ca7f9d4c1e861426d35441fddd1c4c544023a` |

Outputs are under `%USERPROFILE%\.codex\EasyEjectBuildTools`, outside OneDrive. Apphost hashes alone do not identify the changed managed/XAML payload. `modern-font-build.log` has 0 warnings/0 errors; the final reproducible script run `modern-build-script-test.log`, terminal 10652 exit zero, has both builds warning-free and 13/0 reused fixture reopen tests on .NET 10. The retained full 1214/0 classic suite is not a new full modern-runtime run.

Tray single-click/context menu/Quit were implemented but not actually exercised; the supported helper did not return a targetable taskbar window. Full modern operation/dialog integration, restart/login, keyboard/notifications/hotkey, other sizes/themes/monitor DPI, supplied tools/LUT, identified disposable hardware and trusted distribution remain open. No real import/eject/cleanup/deletion/profile save/startup installation, other-app input, security/power/display change or publication occurred during these checks.

## October 7 observation

Checkout was clean at `94a0047`; latest source is `e1598951d657749b310080190a9c1dcfe01f8bfb`. The existing build was launched without rebuilding or changing source. No source/test file was newer than the executable. The retained full-test log still ends **1201 passed; 0 failed**, `manual-import-review-worker-final-test.log`; that suite was not rerun for this observation.

| Artifact | SHA-256 |
| --- | --- |
| `EasyEject.Windows.exe` | `c0026e94df8bedd9f864d0abdb962de6b50d3911d43c66825d766f9f7635a9eb` |
| `EasyEject.Tests.exe` | `c85697f64895ef6e47cbf3de1bb41e99cb5f2e49c25adb9b9d13bd773d71f2f3` |

Read-only native inventory returned one internal disk/five volumes, with OS C: protected and MYASUS, RESTORE, SYSTEM and an unnamed internal volume. No disposable external storage was identified. The inventory command lacks connection notifications and blocks operations; its result does not prove the running application's notification delivery or hardware compatibility.

The pre-existing executable at `%LOCALAPPDATA%\EasyEject\PrototypeBuild\EasyEject.Windows.exe` was launched through the computer-use skill. Its launch helper initially reported no targetable window; a fresh window enumeration then returned the running app under the packaged host's canonical `LocalCache\Local\EasyEject\PrototypeBuild` path. The application did not need another launch. Native process inspection found PID 16400, started at 06:50:41 local time. A unique returned Easy Eject window was selected and brought forward; no other application's controls were operated.

| Actual observation | Evidence scope |
| --- | --- |
| Ordinary application launch | `Easy Eject · Windows prototype` appeared and remained running. No Easy Eject approval dialog was observed in this launch. This is under Ryan's current Windows policy, not proof of trusted signing or compatibility with default security policy. |
| Main device window | Foreground screenshot and accessibility tree showed all five internal volumes, separate Internal storage/Windows storage groups, protected OS C:, and the inventory/status text. Safe eject, Clean & eject, Eject cards, Verified import, Resume and Import profile were disabled with no eligible selection. |
| Settings entry | Clicking the actual Settings button opened `Easy Eject settings` and disabled the owner window while the modal was open. |
| Initial Settings presentation | Foreground screenshots showed launch-at-login unchecked and not registered, notifications checked, card cleanup unchecked, other-storage warnings checked, and visible Save settings/Cancel buttons outside the scrolling content. |
| Remaining Settings contents | The modal accessibility tree contained debug, shortcut/letter, Quick Share tools and automatic-release controls plus their explanatory text. Their presence in the tree does not prove scrolling, keyboard/dropdown operation or visibility at other sizes. |

No settings were saved, startup entry installed, media tool run, import started, cleanup performed or device ejected. Foreground input stopped when the access task requested focus for Ryan's Mac password interaction. Settings was left open; no Cancel, further scrolling, keyboard operation or app shutdown/restart was performed in this observation. This focus sequencing is not a paused goal or a verified background test job.

## Coordinated mouse and lifecycle follow-up

Ryan explicitly requested coordination with the other task for keyboard/mouse control. The recovery task confirmed the access task was idle with no active Mac viewer/password window and granted a short Easy Eject slot. This is direct user-authorized coordination, not permission inferred from another task's message alone.

On the same build/process, actual scrolling brought the debug/shortcut controls and explanation into view while Save/Cancel stayed fixed. A screenshot-based click checked the shortcut only in the unsaved draft and enabled its letter control. The dropdown opened and visibly listed **D, E, F, G, K, M, R, T, X**. The current shortcut-status text remained off. This does not prove native hotkey registration/delivery or saving a changed letter.

Modal keyboard/letter selection remain **unproved**. The focus-based checkbox click changed scroll/focus but did not check it; a subsequent Space attempt produced a snapshot focused on the disabled owner and did not toggle the checkbox. Screenshot-based checkbox input succeeded. A dropdown selection attempt closed the list but the selected value remained E. These observations are insufficient to establish an app defect or a keyboard/selection pass; the helper's owner/modal targeting needs resolution or direct foreground verification. No alternate UI automation or security bypass was used.

Screenshot-based **Cancel** closed Settings and restored the active main window. Both known `app-settings.json` paths remained absent, and the own `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value `EasyEject.Windows` remained absent. Before/after evidence is retained outside OneDrive in `%USERPROFILE%\.codex\EasyEjectWindowsPreview\foreground-settings-before.json` and `foreground-settings-after.json`; the latter records `SettingsUnchanged`, `StartupUnchanged` and `OriginalProcessStillRunning` as true at **06:59:59 local time**. No settings were saved, startup installed or media/device action performed.

With no modal left, **Alt+F4** closed the device window. Fresh native window enumeration returned no Easy Eject window, while the same PID 16400/start time remained alive with an empty main-window title. This proves close/hide keeps that process running; tray re-opening, tray icon input, Quit, restart and busy/modal shutdown protection remain unrun. Keyboard/mouse control was then explicitly released to the access task and recovery task; subsequent work in this slot is background documentation only. The retained full-suite result stays 1201/0, not a new test run/count.

## Remaining foreground checks

### Second-launch contract and current control failure

The baseline `Ejector/AppLifecycle.swift:162` reveals Imports when the app is opened again. On the old Windows build, a second ordinary launch actually showed “Easy Eject is already running in this session.”; dismissing that notice left only the original hidden PID 16400. Source `9b38cc69f1714d43bb473566bc4c106fffaf39de` now sends a current-user/session window-only request and preserves the visible owned dialog. Duplicate login launches remain quiet. This fixes the software contract; actual new-build foreground activation is not yet observed.

The isolated warnings-as-errors build at `%USERPROFILE%\.codex\EasyEjectFixtures\run-c880afad0ee94331\new-build` passes focused **13/0** and full **1214/0**, terminal session **4501** exit **0**. Native event/child-process/nonactivating-form fixtures do not signal the production listener or establish actual app foreground activation. Logs are `window-reopen-targeted.log` and `window-reopen-final-test.log` in `%USERPROFILE%\.codex\EasyEjectWindowsPreview`; the original source/tests/binaries are retained in the parent fixture directory.

| Tested isolated artifact | SHA-256 |
| --- | --- |
| `EasyEject.Windows.exe` | `7063e4e169d7c7a4c5975e093d4148c37bd97e065d7443bbb9a76ee5758e6a98` |
| `EasyEject.Tests.exe` | `6c3e6d0cdb1d5f174cf41c25a9dbbf6bacac3f6f0f8f9b7828de2830ca020aea` |

After verification, the exact hidden old development preview was stopped with its PID/path/start time/hash checked; fresh read-only inventory still showed only one internal disk/five volumes. This was engineering process cleanup, **not graceful tray Quit proof**. Supported `sky.launch_app` failed before starting the new app: `GetCursorPos failed: Access is denied. (0x80070005)`. One retry after fresh window enumeration returned the same error. No new Easy Eject process was present. Enumeration still returned the Mac Studio viewer, Gyroflow, WireGuard, Settings, Steam and Codex, without an authentication dialog; this does not establish whether the interactive desktop is locked. Input stopped and the slot was released to the access task under Ryan's coordination authorization. No security setting or alternative UI automation was used.

### Desktop recovered and real second-launch reveal observed · 08:48–08:52 EDT

Ryan replied **ready** after being asked to wake/unlock to the normal Windows desktop. The access task granted a fresh quiet input slot. The exact previously tested isolated executable/hash above launched through the supported computer-use API: PID **18784**, started **08:48:58.475**, returned window **3605682**. Initial capture contained occluding content from another app; an explicit activation of the selected Easy Eject window then produced the actual visible main window. No other application's controls were operated.

Actual **Alt+F4** hid the main window. Fresh window enumeration returned no Easy Eject window, while PID/path/start time remained unchanged with empty title. An ordinary second launch of that same executable restored **the same window ID, process and start time**, with main-window screenshot/tree and focus on its device list, without a duplicate notice or manual follow-up activation. A second ordinary launch while Settings was open retained the same Settings dialog/control identities and disabled owner, with only the original app process present. This proves the tested build's ordinary reopen and modal-preservation behavior under Ryan's current policy. It is not tray-icon input, graceful Quit, startup/login or full restart proof.

Settings scrolling brought the Quick Share tools and automatic-release controls/explanations into view, with Save/Cancel fixed. The end of the release explanation remained partly clipped in the captured viewport; further scrolling did not visibly change the final capture. Full-tail reachability is therefore still unproved; this observation alone does not distinguish app layout from helper/modal input behavior. Screenshot-based **Cancel** closed the modal. Both known app-settings files and the own login Run value remained absent. No preference was saved or login/hotkey installed. App was left visible, with no modal or active operation, and input control explicitly released to the access task at **08:52 EDT**.

No source/test/build change or regression rerun; retained **1214/0**, terminal **4501** exit zero. No media import, conversion, cleanup, device eject, security/power action or other-app input. Ryan then reported the interface looks ancient and asked about a modern Windows UI API. WinUI 3 was recommended; Gyroflow's Qt/QML implementation was identified from official source. Modern presentation expectations remain open; no UI framework migration has been implemented or claimed.

Use fresh returned window/state objects before each input. Do not automate authentication/security prompts or compete with another task's password/installer interaction. Check the actual Mac `SettingsView.swift`, `AppLifecycle.swift`, completion and manual-sharing contracts before treating an outcome as parity evidence.

| Requirement | Next evidence | Current status |
| --- | --- | --- |
| Settings accessibility and cancellation | Scroll all content; inspect the letter dropdown, keyboard focus and Escape/Cancel; confirm temporary edits do not save or register a shortcut/login entry. | Partial: mouse scrolling/draft enable/dropdown display/Cancel with unchanged storage observed; full content, keyboard/Escape and changed-letter selection unproved |
| Defaults and saved profiles | Exercise defaults/custom selection, broadening confirmations, cancel, migration and stale edits through actual dialogs with owned fixtures. | Not run |
| History and completion | Inspect exact retained batches, explicit verification, Keep Connected default, return from manual sharing and loss of eject authority after restart. | Not run |
| Tray and lifecycle | Close hides; manual second launch/tray reopens; Quit exits; fresh launch and active/modal quit protection are observed. | Actual tested-build hide, ordinary second-launch reveal of the same window/process and Settings preservation observed; tray/Quit/login/restart/busy protection remain pending |
| Notifications and hotkey | Actual delivery/suppression, fresh review on balloon click, selected-key registration/collision and no repeated/stale removal. Do not install login registration during development. | Not run |
| Scaling and controls | Minimum-size scrolling, fixed action visibility, actual dropdowns/pickers, larger layout and applicable monitor/DPI behavior. | Main/Settings render, partial Settings scrolling/fixed actions/dropdown display observed at current size; remaining sizes/inputs unproved |
| Quick Share | Manual opt-in/size/LUT/tool choice, actual validation, cancellation/retry and unchanged exact imported originals/history across restart. | [Actual-tool rehearsal](QUICK_SHARE_ACCEPTANCE.md) prepared, not run; tool/LUT paths still needed |
| Hardware and optional removal | Explicitly identified disposable camera/card/reader/destination and authorized operations; physical import, long transfer, reconnect, grouping/veto/eject and any separately permitted cleanup/removal/recovery. | Identification/authorization still needed |
| Direct distribution | Trusted publisher/artifact, manual installation/update/rollback and current-policy proof after runtime/hardware acceptance. | Signing/runtime proof still needed; no public release authorized |

Do not turn these observations into a whole foreground, hardware, codec/LUT or full-port pass. Record further actions and their actual outcomes as they occur.
