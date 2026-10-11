# Native minimize, maximized retention and close-to-tray · October 11

The current production reveal behavior passes the previously unrun minimize/maximized paths. No production fix was required. New `UI_TESTS` regression checks exercise the real native OverlappedPresenter and production `Reveal`/`EnsureVisible`: minimize then Reveal restores, Reveal while maximized retains maximization, and a minimized formerly maximized window returns maximized for a visible operation review. These checks precede the existing actual tray Quit draft/busy/editor/idle and Stop-layout checks.

## Supported native input

Fresh isolated scope: `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-lifecycle-native-20261011`.
Engineering executable/evidence: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-reveal-before-20261011`.
Owned PID **7588**, start **2026-10-11 12:35:45 EDT**, native window **4066056**.

1. Settings initially shows shortcut off and login unregistered. Only **Enable global card-removal shortcut** is enabled and saved. Native status confirms **Ctrl + Alt + Shift + E**; isolated JSON records `EnableShortcut:true`, with all six untouched fields still null. No import, cleanup, removal or login registration starts.
2. Click the native title-bar Minimize button. The same process remains responsive. Select the existing Downloads Explorer window and send the actual global chord there. Capture Easy Eject without calling its activation API: it has restored and reports **No camera or emulator cards are connected**. No device request is made.
3. Click native Maximize; a settled screenshot fills the display and the title-bar accessibility action becomes **Restore**. Click Minimize, select Explorer and send the chord again. Without Easy Eject activation assistance, a settled screenshot again fills the display and retains **Restore**.
4. Click the maximized title-bar Close. Supported window enumeration reports no Easy Eject window; read-only process evidence retains the same responsive PID/start with `MainWindowHandle:0`. Send the chord from Explorer. The same window **4066056** returns maximized, still reporting no cards. Capture again uses no Easy Eject activation assistance.
5. Reopen Settings, disable the shortcut and save. Status confirms **Global eject shortcut is off**. Select Explorer and send the same chord; the passive app activation count stays **9 → 9** after a one-second observation interval. No shortcut activation occurs. Explorer receives no file action and remains open.
6. Return to Easy Eject and choose the actual tray Quit callback through the engineering footer. The first accessibility-only input reports unavailable coordinate geometry; fresh selection/activation/screenshot recovers once, and the callback exits PID7588 normally. No termination workaround is used.

The native UI run uses the **before** engineering host, which already contains the added native presenter checks. The later final test build updates the PASS description and adds presenter/visibility fields to the passive review trace; those new fields are not claimed as evidence from this earlier native session. Tool-driven activation during final Quit recovery is separate from the unassisted shortcut-return screenshots above.

## Pins and retained results

Loaded native-run UI DLL: **504832 bytes**, SHA-256 `8CB214D2EDD20D24DC3025AC4A44034C6817D3F9C249876B6415B3EC1B031CB5`.
Loaded native-run runtime DLL: **1008128 bytes**, SHA-256 `E31D7FFDFE029347A57B7A75E44C3464C76D8D9BAA39C63523CDAB049D45C552`.
Module receipt: `lifecycle-module-pins.json` in the before-build folder.

Final isolated `app-settings.json`: **169 bytes**, SHA-256 `61B55F89832463BF40920DEE58058A193306AEB9A3585262550A7A3A93F54CC5`, shortcut false, six other raw fields null. Both live configuration files retain exact hashes, lengths and write times; the login Run value `EasyEject.Windows` remains absent.

Final evidence: `lifecycle-final.json`, SHA-256 `68493A5CC883AB57EDA5505D021BFFDA1BA10CAB396A90BC40F296C128B389E0`.
Native trace: `keyboard-probe-402d0c8a2d4f4fa4bfef167245ab3fe4.jsonl` in the isolated scope, SHA-256 `C3576206F77AA216E39437BFC1AD50743DDFF2F9F6865A2079648FA122718602`.
`disabled-before.json` and `disabled-after.json` retain the equal activation counts.

The first final registry check used `Get-ItemPropertyValue`, which throws when the value is absent. A corrected read-only `RegistryKey.GetValue` check confirms absence; the correction is recorded in the final evidence. This was an evidence-script issue, not an app failure.

Both commands below complete with **0 warnings / 0 errors** and native presenter/tray Quit/Stop regression **PASS**:

```powershell
pwsh -NoProfile -File Windows/modern/build-ui-tests.ps1 -BuildRoot C:/Users/rsmit/.codex/EasyEjectBuildTools/Codex020-reveal-before-20261011
pwsh -NoProfile -File Windows/modern/build-ui-tests.ps1 -BuildRoot C:/Users/rsmit/.codex/EasyEjectBuildTools/Codex020-reveal-final-20261011
```

Logs are sibling `*.log` files. First automated fixture: `ui-a2a18bafc62049dbb2388d7fec4b38fc`; final automated fixture: `ui-2a8b2a957f874aafaef64e0ed8d7ec0d`, both direct children of `C:\Users\rsmit\.codex\EasyEjectFixtures`, with retained `result.txt`.

No production inputs, package or full-default counts changed. No camera was present; this establishes native window/shortcut behavior without physical removal acceptance. OS tray-icon clicks, notification delivery/clicks, silent login delivery, all physical themes/DPI/monitors, genuine media/LUT, hardware/filesystem/travel and trusted distribution remain open. No OS settings, autostart entry, real media, eject, format, cleanup, installation, push or publication actions occurred. Full port goal remains active.
