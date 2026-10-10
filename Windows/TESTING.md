# Windows device testing

**Newest review copy (October 10, Mac parity through `dc2c3ba`/`07de127`):** `C:\Users\rsmit\.codex\EasyEjectBuildTools\ClaudeReview-3a704bd\review\app\EasyEject.Modern.exe` (601 files, unsigned, built from committed `3a704bd`; `review-build.json` SHA256 `29D7EFD0AB2DF1DFA19FBD58348895B16A192CDF07C9DDA615E67B9564FCE3D7`). It adds DJI O4 Pro Gyroflow-first Quick Share and the import-history Open Folder action. This laptop has no PowerShell 7, so it was built with a PowerShell 5.1 equivalent of `modern/build-review.ps1`. The same publish arguments and payload checks apply, but the vendor-notice and native-import reports were skipped. Quit the running preview from its tray menu before opening this copy. The older copy below stays as it is.

The [October9 native generated-history pass](modern/VISIBLE_REVIEW_WORKFLOW.md) verified two exact saved clips with source absent, sharing hidden while off, explicit1080p/Standard opt-in, manual export of2 new copies and a separate manual reuse of both. Exact reviewed recovery retained temporary profile/history in quarantine; live history reopened empty. Original/output bytes and first-export write times are preserved. Normal successful-export issue clearing is recorded separately. Generated fixtures do not establish real camera/device, native4K, cold app restart, cancellation/changed-record refusal, positive official Luna LUT or trusted distribution acceptance. Existing1415/0 each-runtime counts are unchanged.

The latest local review is:

`C:\Users\rsmit\.codex\EasyEjectBuildTools\PowerConnectionAtomic-20261008\review\app\EasyEject.Modern.exe`

Keep the complete app folder together. Finish any active work, then use **Quit preview** in the running preview's tray menu before opening this copy. Closing the window keeps that copy running; launching another version reveals it rather than switching versions. This unsigned review uses the normal Windows settings and history folders.

The Easy Eject artwork is included in the executable, window, header and tray. The shared default suite now passes **1415/0 in each runtime**, with separate [Framework and modern evidence](POWER_OPERATION_ACCEPTANCE.md); **242/0 selected modern** and **15/0 owned synthetic power checks** remain separate overlapping scopes. The current review payload is unchanged. Limited [current-copy foreground checks](modern/FOREGROUND_CURRENT_F810_20261008.md) have passed. After the October9 Codex restart, [supported input and normal minimized reveal](modern/VISIBLE_REVIEW_WORKFLOW.md) work; maximized duplicate launch and empty-history modal retention/Escape also pass on the original instance with settings preserved. The generated nonempty-history/manual1080p pass above is complete. Tray-menu reveal, Quit/full app restart, drafts, remaining active workflows and actual sleep/device acceptance remain open. See [power review evidence](POWER_CONNECTION_ACCEPTANCE.md), [review build history](modern/REVIEW_BUILD.md) and [icon observations](modern/ICON_ACCEPTANCE.md).

For generated sleep/wake interruption checks, the existing test runner accepts `--power-operations`. The [test-only operation milestone](POWER_OPERATION_ACCEPTANCE.md) records **27/0 each runtime**, covering manual sharing/publication, cleanup/fake eject and interrupted-removal confirmation/retry on retained fixtures. It does not request physical sleep or affect an attached device; the review app above is unchanged.

## First pass: detection and interface

Identify the spare USB drive, reader/card or camera and whether its contents are disposable before testing. Device operations need an agreed scope. Begin with detection only.

1. Open the latest preview. Check the header/window icon, complete action labels and internal storage's disabled removal actions.
2. Confirm automatic import is off for the test device. Connect it and compare its model, capacity, volumes and drive letters with Windows Disk Management. A multi-volume device must show the entire physical group.
3. Open the device's removal review and choose **Cancel**. Check that Enter/Escape use the safe default and nothing is removed.
4. Close the main window and reopen it from the Easy Eject tray icon. Check the icon/menu, double-click and **Quit preview** while idle. Restart and confirm saved choices remain.

Record the build, device/reader model, source filesystem, destination filesystem, steps and observed result. Do not call an unperformed check a pass. A camera exposing only MTP/PTP storage is outside the mounted-volume importer contract.

## Second pass: copying and safe removal

After Ryan identifies disposable files and an existing destination on another physical disk, agree to the following operations individually:

1. Save an import profile with automatic import, original removal, Trash recovery and cleanup off. Saving must start no transfer.
2. Review **Videos only**, previews and clean/preserved layout, then explicitly start copying. Check copied names, hashes, selection, package companions and that every original remains unchanged. Repeating the import must reuse only verified matching copies.
3. During a sufficiently long test copy, use **Stop**. Check that no partial file appears as a completed copy, originals remain intact, reservations release and an explicit retry works. Test disconnect/reconnect separately after agreeing to interrupt only this disposable transfer; stale choices must expire.
4. Check completion, saved history after restart and opening the exact destination. Eject must remain a separate choice unless the profile explicitly enables an eject prompt; **Keep Connected** remains its safe default.
5. Explicitly test plain eject on the identified spare device. Confirm success before unplugging. Separately test a held-file veto and reconnection; report any refusal honestly and never force removal.

Deletion, metadata cleanup and Trash recovery require a separate disposable-files scope. Do not enable them for the basic copy/eject pass.

## Remaining full-port checks

The foreground pass also covers defaults/profile editing, narrow layout/DPI/theme, notifications, hotkey registration, silent login/single-instance behavior, busy close/reveal/Quit, interruption/resume and unrelated-device operation during a transfer. See [foreground acceptance](FOREGROUND_ACCEPTANCE.md) for the detailed unrun cases.

Manual Quick Share needs separately selected existing FFmpeg/FFprobe and an explicitly chosen official LUT for a positive Luna test. It remains opt-in per device and manual per action. Validate 1080p/4K, LUT choice, real probe/full decode, cancellation/retry and exact last-20 history reuse after source removal/restart without changing full-quality originals.

Full completion also requires genuine camera/package and destination-filesystem acceptance, clean-computer startup and trusted direct-download delivery. A local unsigned build and fake/disposable-file tests do not establish those results.
