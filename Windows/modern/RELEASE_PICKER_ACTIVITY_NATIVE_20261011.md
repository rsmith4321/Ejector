# Native release picker activity revocation · October 11, 2026

The real Windows history picker now has native evidence that a choice is refused after release activity becomes unavailable and then returns to idle while the picker remains open. A fresh manual choice succeeds. The activity change is controlled by an engineering-only fixture; it does not represent a physical scan, trusted update, history restore or installation.

## Boundary and actual session

`UiReleaseInspection.cs` remains entirely `#if UI_TESTS`. An owned `release-activity-busy.txt` marker makes the existing release activity pulse report unavailable to the real presenter. The hook records actual observed transitions, including nested/busy/CanClose state. It applies only to the labeled generated backend. The ordinary backend factory clears the helper reference so a later ordinary entry cannot inherit its label or availability hook. The normal availability expression is factored into a local variable with the same production conditions; no production behavior change or binary identity is asserted.

The prior read-only backend permits only an exact generated JSON read, with no network, authentication, replay, restore, installation or executable capability. Its fake reviewed ID9001 is in memory; any actual Restore invocation would throw before acting. ZIP/folder actions remain disabled.

Actual engineering PID **24520** runs `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-release-picker-ui-20261011\bin\EasyEject.Modern\Debug\net10.0-windows10.0.26100.0\win-x64\EasyEject.Modern.exe`, with fresh owned fixture `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-release-picker-20261011`. Only prior generated debug settings/log and the review marker seed this fixture; live configuration is not copied or changed. Loaded Modern/Runtime/coreclr paths and hashes are retained; this uses installed .NET10.0.0, not the normal self-contained Release app. Read-only inventory shows five internal/protected volumes on one disk, no external camera/card.

| Actual native input/state | Observed outcome |
| --- | --- |
| Test release workflows → Update history → Review history backup | Native Open dialog appears with its parent disabled. |
| Create owned unavailable marker while picker stays open | Actual pulse logs unavailable at **19:18:53.6422323Z**, nested=true, busy=true, CanClose=false. |
| Rename marker to ended while same picker stays open | Actual pulse logs available at **19:19:10.3508426Z**, still nested=true, busy=true, CanClose=false. Both transitions fall between saved native picker observations. |
| Submit exact generated filename through that original picker | “Selection was not applied. Release activity changed during this choice. Open a fresh manual review.” The backend has **zero runs and zero presentations**. |
| Open a fresh picker, select generated-history.json and submit | Exactly one guarded Backup read and one foreground presentation, generated ID9001. No other action runs. |
| Cancel backup review | Reviewed ID cleared; Restore disabled and new review required. Closing disposes the backend with no held worker and backup=0. |
| Ordinary Updates & installation entry | Real unconfigured backend reports no approved channel/signing publisher; metadata/ZIP/folder actions disabled, no generated authority or engineering label. |
| Close ordinary dialog, settle main controls, actual tray Quit callback | Process exits **0**, no `unhandled.txt`. No repeated input after Quit. |

Native dialog children are captured within the parent. The stale filename is verified as the exact owned absolute path before submission. Provider focus reports Search Box despite the visible filename caret/value, so it is not treated as reliable focus proof; no search or shell command is submitted. The fresh picker address and selected generated filename are verified before submission. Immediate post-input captures sometimes retain old pixels/text while asynchronous rendering completes; settled observations are taken before further input. Screenshot payloads are not saved or reprocessed.

## Verification

- `Codex020-release-picker-ui-20261011.log`: **0 warnings/0 errors**, existing automatic native root/theme/text/completion/pending/presenter/Stop/tray/editor/busy/idle checks PASS; retained automatic fixture `ui-d6b8e490f205480faf1eaadb50cdbf8a` has no exception.
- `Codex020-release-picker-normal-20261011.log`: **0 warnings/0 errors**, selected **246 passed/0 failed**. Normal PE metadata includes the production release view but excludes the test host/backend, availability method/logging field, factory/initializers/flags and earlier diagnostic hooks. No full-suite, supplied-tool or whole-assembly equivalence claim is added.
- Exact generated JSON remains `{"generated":true,"authenticUpdateHistory":false}`. Isolated settings/log remain exact with two profile files. Both live files retain hash/length/write time/count; login registration remains absent and Mac source matches baseline384cb767.
- Current production/package source remains **0040e58**. Unsigned ZIP stays **107459831 bytes**, SHA-256 **e941e2c49a9bb88d65d6d24234e6cd2fbcd711350ef2c7cf43105a7e56198bb4**. Normal Release UI is not launched. No device/media, OS setting, autostart, install, push or publication action.

Evidence root: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-release-picker-native-20261011`. Launch/finalizer scripts, actual accessibility observations, process/modules/exit, transition/backend traces and preservation pins remain there or in the pinned fixture. Aggregate `final.json` SHA-256 **51ecf8e95a5df41782f909953444c7eb34820918bc882a7d2f853d6774e8e5bc** verifies these scoped assertions.

Native ZIP/folder selection, queued worker completion before UI consumption, presentation-failure lifetime and accepted restore/publication/recovery remain open. Configured CMS/HTTP/revocation/publisher/package/install/rollback proof and physical/genuine-media/official-LUT/travel/display/signing/publishing acceptance remain necessary. This closes the controlled outstanding-history-picker activity case only; the full goal remains active.
