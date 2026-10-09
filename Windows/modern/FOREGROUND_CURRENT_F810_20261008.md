# Current F810 foreground checkpoint · October 8, 2026

Actual Computer Use checks ran on the Windows laptop after the Gyro worker explicitly returned its read-only metadata scope with no CPU lease/job. The coordinator granted the prepared short foreground sequence. No build or suite rerun belongs to this checkpoint.

The app is the existing `C:/Users/rsmit/.codex/EasyEjectBuildTools/PowerConnectionAtomic-20261008/review/app/EasyEject.Modern.exe`, PID **21224**, start **2026-10-09T02:46:46.1826671Z**, window **1967754**. The backend remains **F810DFB734CF47AE09FA8A9558DA5F819056FE059692CE31857ABE2AF1F35FAD**. October 9 UTC observations below occurred October 8 in America/New_York. The complete build/module bindings remain in [current review acceptance](CURRENT_REVIEW_ACCEPTANCE_20261008.md).

## Native observations

- The existing Easy Eject glyph/red background renders in the title and sidebar. Selecting internal MYASUS storage displays “No safe removable device node was identified”; eject, card eject, cleanup/eject, verified import, resume and profile actions remain disabled. Only the internal row was selected; protected OS storage was listed but not separately selected.
- Empty verified history shows its recorded-only/last-20 explanation, no completed batches, disabled Verify saved originals, no Quick Share action and highlighted Close default. Alt+F4 hides the window with that dialog open. Immediate enumeration races the asynchronous hide; fresh selection then reports that window absent. Read-only metadata at **03:08:19.6814572Z** confirms the same PID/path/start survives with MainWindowHandle zero.
- A normal launch of the same existing executable reveals window1967754 with the same history dialog, still empty and verification disabled. Escape closes the dialog; a fresh observation confirms idle storage and enabled Refresh. The final process census finds only one persistent modern instance, the original PID21224. This is actual native duplicate-launch/reveal and modal retention, not cold restart or active file-operation acceptance.
- Settings initially reports login unregistered, notifications/warn-other/update choices on, cleanup/shortcut off and shortcut letter E disabled. Scrolling reaches the keyboard section while Save/Cancel remain fixed and visible. Toggling the unsaved shortcut draft checks its box/enables the letter while the runtime status remains off. Cancel returns to storage with the draft-discarded message; reopening Settings shows shortcut off and the letter disabled again.
- Opening cleanup enablement shows its permanent-metadata warning with Cancel highlighted. Escape dismisses the warning and cleanup remains unchecked. Page Cancel returns to idle storage. No warning acceptance or Settings Save occurred.
- Final native state has no modal/editor, enabled Refresh and disabled storage mutation actions. Supported window enumeration exposes Easy Eject, G-Helper, Codex and Steam, but no targetable tray/taskbar. **Normal Quit, tray icon/menu interaction, busy-Quit refusal and full cold restart remain open.** No alternate automation or engineering termination was used during these checks.

Some immediate accessibility trees lagged the screenshots during asynchronous navigation/dialog animation. Fresh observations were used before relying on changed modal/index state; the transient tree is not treated as a product failure. The initially occluded screenshot showed another window, so the selected Easy Eject window was explicitly activated and reobserved before input.

## Preserved evidence and postcheck correction

External packet: `C:/Users/rsmit/.codex/EasyEjectBuildTools/ForegroundCurrentChecks-20261009T0307095425211Z`.

`before.json` retains fresh process/configuration/manifest and all340 tracked Windows file pins. `hidden-process.json` records the hidden live process. `final-native-observation.txt` retains the final native accessibility/document text and returned window list; the preceding native screenshots/actions remain in the tool transcript. `after.json`, SHA-256 **A00BE0A84C5776DFC77506B8E08113C7B5311290BEE582D2BE29C7EE2BE24B9B**, records:

- All **649** current payload files still match manifest lengths/hashes/file count.
- Exactly two configuration files, unchanged: 161-byte issue SHA **2DA0274519483D90BBC19964F254B4A2A4E651A40F24726267A27FC6C3C85BC0**, and 486-byte tools SHA **C366C5017700613C17F734FFC01C8AEACC0F386B61EF19558FD8DE57EE94728D**. No settings/default/profile/history record was created or restored.
- Same PID/path/start, window1967754, one persistent modern instance and eight selected loaded module hashes, including actual F810. The prior peer module receipt remains unchanged.
- The first postcheck refused its all-tracked-files guard because peer commit **743cf396d178ae98770a81cdd61b6e425f08d814** added eight documentation lines to exactly PARITY, PROGRESS and CURRENT_REVIEW_ACCEPTANCE during the foreground slot. The original before records were preserved. The corrected receipt records all three before/after document hashes, separately verifies zero production-file changes and retains clean Git state at that peer checkpoint. It does not claim all340 tracked files were byte-identical. This checkpoint's subsequent documentation commit is separate from that before/after receipt.

No Save, startup install/retarget, shortcut registration, live history seed, import/export, real-device action, deletion, system-setting change or process termination occurred. Internal refusal, empty history and unsaved drafts do not establish connected profiles/default persistence, real operations, notifications, physical sleep/wake, official Luna LUT, camera/filesystem/travel or trusted distribution. Full Windows acceptance remains open.
