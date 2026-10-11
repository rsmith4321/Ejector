# Windows tray flyout · 0.2 preview work

Left-click opens a compact WinUI window beside the clicked tray icon. Double-click opens the main window; right-click retains the existing menu. Camera cards, emulator cards and other external storage use the existing inventory and physical-device choices. Internal, protected and system volumes stay out of this list.

The flyout shows capacity, free space and transfer protection. Import opens the existing manual import review. Quick Share appears only for an exact opted-in device profile and opens that device's saved batches; the existing manual resolution/LUT/verification flow remains authoritative. Preferences are re-read on each opening. No flyout action enables automatic import or sharing.

Eject uses the same manual removal review, immutable device choice, epoch checks, reservations and native removal implementation as the main window. Its confirmation belongs to the flyout's XAML root, with **Keep Connected** as the Return/Escape default. Results stay in the flyout and use the existing notification delivery. An unrelated device can be reviewed during a transfer only through the existing separately proven storage path. Protected source and destination disks remain unavailable.

Escape and deactivation dismiss an idle flyout. Its own confirmation/removal request keeps it open. The footer opens the window, opens Settings when idle, or requests Quit through the same editor/work policy as the tray menu. Theme resources and Desktop Acrylic follow Windows appearance. Placement uses the clicked monitor's physical work area and DPI; a bounded device list scrolls on short screens. Fixed-scale placement tests cover every taskbar edge, negative monitor coordinates, auto-hide and small work areas. These tests do not establish appearance on every physical monitor or theme.

## Tray Quit investigation

Ryan reported the 0.1 review exiting with an unsaved Import defaults change. An isolated WinUI test invoking the actual tray `ToolStripItem.PerformClick` callback did **not reproduce that exit on the original code**. The editor retained its changed checkbox and busy/editor Quit requests were refused. The cause of the reported exit remains unconfirmed.

The hardened window now treats an active defaults/profile/settings editor as open even if its panel is hidden. Refused Quit restores the active editor and explains the refusal. The regression also hides an active draft before invoking the real tray callback and verifies that the draft returns unchanged. An idle Quit must actually close the window.

Run the native UI regression with PowerShell 7 and the existing SDK:

```powershell
./Windows/modern/build-ui-tests.ps1 -BuildRoot C:\Users\rsmit\.codex\EasyEjectBuildTools\UiTray-fresh
```

This explicitly builds `UI_TESTS`, changes the entry point, and redirects all preferences to a fresh owned `ui-*` fixture folder. Discovery, automatic work and release checks do not start. It invokes the real tray callback and real WinUI controls, then exits. A separate `inspect` argument supports computer-controlled visual checks with a generated device and a confirmation whose callback has no device API. The test host is excluded from every normal review/public build. These checks do not import camera footage or eject hardware.

The explicit `review` argument instead opens normal discovery and manual workflows, titled **Easy Eject · isolated review**, with fresh isolated preferences/history. This mode accesses connected storage and requires the separately authorized device scope. It performs no scripted device action, disables the automatic-import pump and never copies live profiles into the fixture. `review-restart` reuses only an exact marked review fixture with an exclusive session lock. Test-only footer buttons open the flyout and invoke the actual tray Quit callback so supported computer control can check these paths when the Windows taskbar is not exposed. These buttons are absent from every normal build; this is an engineering test build, not the distributable review copy.

## Retained evidence · October 11

- Baseline actual-tray callback: `EasyEjectFixtures/ui-tray-quit-baseline-20261011/result.txt`, PASS.
- Hidden-active-draft protection: `EasyEjectFixtures/ui-tray-quit-hardened-20261011/result.txt`, PASS.
- Reproducible script: `EasyEjectBuildTools/CodexUiQuit-script-20261011.log`, warning-free build and PASS; fixture `ui-b789f988d59a4133aa92e49a295fcc27`.
- The initial visual fixture exposed a wrong-XamlRoot exception when opening its confirmation. Dialog construction now receives the correct root immediately. The fixture and production removal paths use that corrected factory.
- The original 0.1 review was opened for the unsaved-checkbox observation, then its draft was cancelled. Its normal startup persisted an issue-state file; no defaults/profile/settings were saved. After confirming idle, that owned process was stopped because the supported window inventory did not expose its tray menu. This is not evidence of normal idle tray Quit in that original process.
- Native flyout `ui-flyout-v3-20261011`: actual Return and Escape each recorded `None` and **Keep Connected** in the generated confirmation. Escape outside that dialog hid the flyout while its process remained alive. These are computer-controlled WinUI observations; no device API was connected.
- Actual camera review identified a shared removal target containing internal E: and card F:. Escape cancelled the review; both stayed connected. The card's empty DCIM import backed up its zero-byte index to the owned NTFS destination, retaining originals and producing no media history.
- That import exposed a regression in the first editor-hardening draft: a hidden profile catalog was treated as an active editor, preventing its completion review. `ProfilesView.Editing` now requires an actual draft. The native regression checks that a hidden catalog permits handoff, while a hidden real profile draft still refuses Quit. `CodexUiQuit-final-20261011.log` compiled without warnings and passed, fixture `ui-67b2b96bffb642f9a217138ccf670491`. The earlier device-review process was confirmed idle before replacement; that replacement stop is not counted as normal Quit acceptance.
- The corrected engineering review restarted with the exact isolated profile. Repeating the empty-DCIM import reused the one index backup, created no media history, and visibly displayed **No matching media. Eject now?** with E:/F: listed and Keep Connected as the highlighted default. Actual Return selected Keep Connected. This closes that limited native no-media/default observation, not physical removal acceptance.
- The actual connected-device flyout showed both E:/F: with capacity/free space and no Quick Share while opted out. The real SD-card Eject review opened in that flyout, listing E:/F: together; actual Return dismissed it and a read-only volume query confirmed both GUIDs remained mounted. No removal request was made.

The [later generated-file card review](CARD_REVIEW_20261011.md) adds native copy/reuse, true Quit/restart verification, a visible Stop action after fixing its collapsed-child layout dependency, cancellation/partial cleanup and a fresh successful manual retry. Physical eject/veto/reconnect, genuine camera Quick Share and official LUT, clean-computer startup and trusted signing remain separate acceptance gates. A review build or pre-release does not complete them.
