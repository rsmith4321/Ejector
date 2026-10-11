# Native terminal release result revocation · October 11, 2026

The real release dialog now has controlled native evidence for **activity revocation after worker completion but before result consumption**, fresh manual retry, and **closure preventing an old handler from changing a newly opened dialog**. No production behavior is changed. The generated backend does not authenticate, restore or install an update.

## Engineering boundary

An entirely `#if UI_TESTS` helper pauses the real `finish` handler immediately after its actual `await operation`, before the existing dialog-ownership and `TryPresent` checks. It is armed only by an owned fixture marker, runs once for the generated backend, and records action/accepted/Open/Busy/CanClose before waiting. The worker has already terminated and the real presenter is idle. The asynchronous pause leaves UI input and activity pulses responsive; it does not simulate a frozen dispatcher or physical slow I/O. A five-minute timeout refuses rather than waiting indefinitely. Normal metadata excludes the method, generated state machine and flag.

Both sessions run the same engineering executable under `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-release-delivery-ui-20261011\bin\EasyEject.Modern\Debug\net10.0-windows10.0.26100.0\win-x64`. Fresh fixtures are `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-release-delivery-activity-20261011` and `ui-release-delivery-close-20261011`. Seeded settings/log are generated and owned; live configuration is not copied. Loaded Modern/Runtime/installed .NET10.0.0 coreclr paths and hashes are recorded. This is not the normal self-contained Release UI. Read-only inventory shows only five internal/protected volumes on one disk, no external camera/card.

## Actual native results

| Session / input | Evidence |
| --- | --- |
| PID27316: Test release workflows → Check release metadata | Backend Check terminal at **19:28:02.0184765Z**, zero presentations. Pause records accepted=true, Open=true, Busy=false, CanClose=true at **19:28:02.0204092Z**. |
| While consumption is paused, owned unavailable marker then renamed ended | Actual pulses observe unavailable **19:28:02.9310145Z** and available **19:28:14.1129229Z**, both nested=false/Busy=false/CanClose=true. Native Check control disables then recovers; result remains unpresented. |
| Resume consumption at **19:28:24.6327931Z** | Existing `TryPresent` refuses with “Review ended after activity or ownership changed.” No backend presentation. Returning to idle does not revive the old result. |
| Fresh manual Check | Exactly one new Check and one presentation (runs=2); real dialog shows generated metadata/result text. Close disposes cleanly; actual tray Quit exits0. |
| PID13980: independent generated Check | Worker terminal at **19:29:36.5692404Z**; consumption pause records accepted=true/idle/CanClose=true. |
| Actual Close while consumption is paused | Dialog closes and original backend disposes at **19:29:47.1881987Z**, zero presentations; main controls recover before any resume marker exists. |
| Open ordinary Updates & installation, then resume old handler | New dialog reports the actual unconfigured production channel, with remote actions disabled. Resume at **19:30:08.6016154Z** records old Open=false/Busy=false/CanClose=false. New dialog's accessibility tree is **exactly unchanged** before/after resume; disposed backend trace is also byte-for-byte unchanged. Existing dialog-identity guard prevents old UI delivery. |
| Close new dialog, settle main controls, actual tray Quit | Exit0; neither session has `unhandled.txt`. No repeat input after post-Quit unusable-window capture. |

One initial indexed input reports unavailable coordinate geometry before opening the activity session's dialog. Fresh returned-window selection, activation and screenshot establish the unchanged main view; one retry succeeds. Immediate captures after asynchronous input sometimes show pre-transition text, so settled observations are taken before further input. No screenshot payload is saved or reprocessed.

## Verification and preserved state

- Engineering build `Codex020-release-delivery-ui-20261011.log`: **0 warnings/0 errors**, existing automated native root/theme/text/completion/pending/presenter/Stop/tray/editor/busy/idle regressions PASS, fixture `ui-969b924fb6b5476eb732aaeb74bfdcbc`, no exception.
- Normal build `Codex020-release-delivery-normal-20261011.log`: **0 warnings/0 errors**, selected **246 passed/0 failed**. Normal PE metadata retains the production release view and excludes the delivery pause/helper state plus prior review hooks. No new full-suite/supplied-tool count or broad binary-identity claim.
- Each native trace contains only Check actions, no paths/files selected or Backup/Restore/ZIP/folder action. Activity session: two starts/terminals, one fresh presentation. Close session: one start/terminal, zero presentations and no backend access after disposal.
- Both live files and each fixture's two profile files retain exact hash/length/write time/count. Login registration remains absent, Mac baseline384cb767 exact. No media/device, OS setting, install, autostart, push or publication action.
- Current production/package source remains **0040e58**. Unsigned ZIP remains **107459831 bytes**, SHA-256 **e941e2c49a9bb88d65d6d24234e6cd2fbcd711350ef2c7cf43105a7e56198bb4**. Normal Release app is not launched.

Evidence root: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-release-delivery-native-20261011`. Launch/finalizer scripts and both sessions' actual accessibility/process/module/exit records are retained; owned transition/backend traces and wait/resume records are pinned. Aggregate `final.json` SHA-256 **ed9d4e5a7a29a9dc53896852b6d19e4aa8213edc17a47a3447f63efdcb678cbc** verifies these assertions.

The controlled terminal-result activity and closed-old-handler cases now have native evidence. Same-dialog old delivery while a newer worker remains active, native ZIP/folder selection, XAML presentation failures, accepted restore/publication/uncertain recovery and configured CMS/HTTP/revocation/publisher/package/install/rollback remain open. Genuine media/official LUT/physical/travel/display/normal-loader/signing/publishing gates are unchanged. Full goal active; a generated result and controlled consumption pause do not prove trusted update acceptance.
