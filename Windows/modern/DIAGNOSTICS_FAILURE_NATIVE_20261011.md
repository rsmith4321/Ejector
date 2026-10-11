# Native diagnostic read/Refresh failure and recovery · October 11, 2026

Actual Windows input exercised the production debug viewer through an isolated engineering host built at **a80c429**, with no production source or package change. This supplements [snapshot/Clipboard/restart evidence](DIAGNOSTICS_NATIVE_20261011.md); it does not claim native Clear or overlapping-worker closure acceptance.

## Scope and provenance

- Executable: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-debug-final-ui-20261011\bin\EasyEject.Modern\Debug\net10.0-windows10.0.26100.0\win-x64\EasyEject.Modern.exe`.
- Fresh owned fixture: `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-debug-failure-20261011`. The exact owned review marker, isolated debug-only settings and generated 416-byte log were copied from the preceding diagnostic fixture. No live configuration was copied or edited. Explicit `review-restart` admits only the exact marker; no automatic import pump runs in this mode.
- Evidence: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-debug-failure-20261011`. `launch.ps1`, `lock.ps1`, accessibility observations, raw Clipboard comparison flags, loaded-module hashes, exit record and `finalize.ps1` remain there.
- Native PID **30408** loaded the recorded engineering Modern/Runtime DLLs and installed framework-dependent .NET **10.0.0** coreclr. This is not the self-contained Release UI or a clean-machine loader test.
- Read-only app inventory showed five internal/protected volumes across one physical disk, no external camera/card. No format, media import, cleanup, original deletion or physical removal occurred.

## Actual checks

| Check | Observed result |
| --- | --- |
| Initial read failure | An owned shell process holds only the generated `debug.log` with `FileMode.Open`, `FileAccess.Read`, `FileShare.None`. Clicking the production main **Debug log…** button reports “Debug log could not be reviewed. Existing diagnostics were retained.” The underlying source-open explanation is “Cannot open source without following links”; a sharing conflict triggers that generic guard wording here, not a proved link. No viewer appears; ordinary debug/Refresh/information controls recover. An isolated unacknowledged issue is saved. |
| Read recovery | Release the owned lock, then click engineering **Test debug log**, which captures the known snapshot and invokes the real tray debug callback. The production viewer opens with the original four-event, 416-character log. |
| Refresh failure | Append one literal generated fixture event to the owned file, making it 497 bytes, then hold the same exclusive read lock. Actual viewer **Refresh** reports the source-open error, retains the original displayed four events, and re-enables Refresh/Copy. The later event is absent from the displayed document. No clear action is invoked. |
| Copy after failure | Actual **Copy to Clipboard** succeeds while the current file remains locked. Close the viewer, release the lock, open the generated-text comparison and use Ctrl+V. `clipboard-after-failure.json` records raw Clipboard exactly equal to the original **416-character snapshot**, unequal to the current **497-character file**. Normalized pasted lines match the original snapshot, with 412 textbox characters. No raw Clipboard contents are recorded by that comparison probe. |
| Fresh-view recovery | Reopen using production main **Debug log…** after releasing the lock. All five generated events appear. Actual **Refresh** succeeds and reports “Current debug log displayed.” This is recovery in a fresh view, not a claim that the earlier failed view retried successfully in place. |
| Retained explanation | Production **View issue…** still shows “Needs attention” with the initial diagnostic-read failure. **Cancel** closes the review; `issue-state.json` remains `Dismissed=false`. No acknowledgement or operation retry is submitted. |
| Normal exit | After the modal closes and ordinary controls settle, engineering **Test tray Quit** invokes the production tray Quit callback. The process exits **0**, with no `unhandled.txt`. The immediate post-click state capture reports “window is not a usable app window” because the process has exited; no repeated Quit input is sent. |

One initial input reports unavailable coordinate geometry. Fresh window selection/activation/screenshot followed by one retry succeeds. All other input follows fresh observations, with settled-state reads after asynchronous dialog transitions. Screenshot payloads are not saved or reprocessed.

The file lock causes an immediate open failure, not a long-running blocked worker. **Overlapping worker/Close retention is not proved by this check.** Native clear, clear-failure/identity-conflict handling and worker-overlap acceptance remain open; existing backend disposable-file assertions retain their own scope. The generated Clipboard snapshot is left on Windows Clipboard; no previous user Clipboard is read/restored and nothing is sent externally.

## Preservation and evidence

`finalize.ps1` passes exact observations and raw/pasted comparisons, module-byte stability and normal exit, released read-only locks, unchanged isolated settings, unchanged 497-byte final log after the owned append, and exactly three isolated profile files (settings, generated log, unacknowledged issue). Both live profile files retain exact bytes/hash/write time/count. The preceding diagnostic fixture and aggregate, engineering source, normal build assembly, login-registration absence, Mac baseline and current unsigned ZIP remain unchanged.

Aggregate `Codex020-debug-failure-20261011\final.json` SHA-256: **9227ccc2f9566972dfdb753dd53f6c8f2d65a7346f8bdae0ff514c07409a7c6b**. It pins the retained scripts/observations and flags the unproved gates explicitly. This milestone reuses the preceding warning-free engineering build and normal exclusion evidence; **no new compile, default-suite or supplied-tool count** is claimed.

Current production/package source remains **0040e58**. Unsigned ZIP remains **107459831 bytes**, SHA-256 **e941e2c49a9bb88d65d6d24234e6cd2fbcd711350ef2c7cf43105a7e56198bb4**. No push, publication, install, autostart, OS setting or device action. The full goal and genuine-media/official-LUT/physical/travel/display/normal-loader/trusted-signing/publishing gates remain active/open.
