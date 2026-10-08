# Modern navigation and dialog foreground checkpoint · October 8, 2026

Ryan explicitly confirmed the desktop was unlocked and authorized resuming UI checks. The supported Computer Use skill was used for app selection, launch, input, accessibility and screenshots. No Windows lock/security/power/display settings were changed. The existing resource-probe process was left untouched.

The tested app is the existing **ModernSelectionBuild-20261007** Debug candidate: archive **51e052c** plus the two dialog files committed as **6582d52**. Earlier build verification is **0 warnings/0 errors and 236/0 selected**, terminal 74167 exit zero. Later release-package/channel/review source is not in this app. Native observations below therefore do not approve the current dirty worktree or a self-contained release payload.

## Observed in the actual app

- Supported launch created primary **PID8124**, start **12:03:39 EDT**, window **67996**, with one protected internal disk/five volumes. Device import, cleanup/removal and eject actions remain disabled; no real device action was invoked.
- Saved profiles opens its empty-state page. Import now, Edit and Forget are disabled when no records exist.
- Import defaults displays Videos only/previews/clean layout enabled, with automatic import/eject prompt/removal off. A harmless previews draft change followed by Cancel returns to storage with **“Import defaults draft discarded. Saved choices were not changed.”**
- Empty verified history opens a native dialog, with verification disabled and Close available. Alt+F4 while it is open leaves both dialog/window visible; Escape dismisses it and releases the busy state. This tests the empty dialog only, not nonempty selection callbacks or saved-batch verification/export.
- Settings opens with login unregistered and global shortcut off. Scrolling exposes shortcut/update controls while Save and Cancel stay reachable. Enabling the shortcut only in the draft enables the nine-letter dropdown; keyboard Down/Return chooses F while the status still says the live shortcut is off. Cancel returns to storage with **“Settings draft discarded. Saved settings were not changed.”** No Save, login registration or native hotkey registration was performed.
- Alt+F4 on the idle window removes it from the supported visible-window inventory while the same PID/start/path remains running.
- A second normal supported launch reveals the original window **67996** and retains primary **PID8124** with no retained secondary process. The window preserves the cancellation explanation. This proves idle close-hide and normal secondary reveal; it does not prove cold app restart, silent login, busy-transfer reveal or explicit tray Quit.

The window is hidden again for the requested human tray-icon click. **Tray click result remains pending** in this checkpoint: the automation surface lists Explorer windows but exposes no taskbar/tray window, so no fabricated target or alternative input path was used.

## Exact evidence and limits

`%USERPROFILE%/.codex/EasyEjectWindowsPreview/native-selection-ui-evidence-20261008.json` records the process/start/path/window, observed cases, module paths/hashes and configuration metadata. Supported-tool screenshots/accessibility observations are in this conversation; no independent screenshot archive is claimed.

Managed app SHA **45EF88CC7E91407C8C64683B9B23565A5A2211F32252154755432C53509AC92A**, runtime SHA **734DCF6366B3484DD42015FD02ABA1CEB3A438A4AEE172AE734819A6CF4113E7** match the compiled candidate. The live process loads installed **.NET10.0.0** `coreclr.dll` (SHA **5201734DB1EF225D23D8EFCA6198B47CC70EDDD5A12601E16D76A7DA68C5907D**) and its own `Microsoft.UI.Xaml.dll` (SHA **AAD12524765E6FB63F0AE26A45A9BA3104F24FDE66413D8A3036FBED74A1E990**).

Both conventional and Codex-redirected EasyEject roots have zero direct JSON files and zero files in their direct Profiles folder, matching the prelaunch metadata check. The owned login Run value is absent. Historical build directories are outside that count. Normal settings/history/profiles, startup registration, real media/devices and system settings were not changed.

Remaining native gates include nonempty/offline/connected profile editing and enrollment, exact history selection/verification/sharing, import/resume/completion, progress/Stop/independent other-device review, notifications, actual shortcut registration and one-use queued reviews, tray click/Quit, cold/silent startup and busy lifetime, modern release-review integration, other theme/DPI/accessibility layouts and hardware/filesystem/travel behavior. Positive official Luna LUT/genuine camera and trusted direct distribution remain open. Full goal active; expired overnight schedule paused.
