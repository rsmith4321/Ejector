# Native notifications, pending choices and no-media completion · October 11, 2026

This milestone adds an explicit **UI_TESTS-only** `inspect-notifications` mode. It exercises the production `NotifyImport`/tray notification delivery, pending-choice picker and completion dialog using generated text and an existing generated history fixture. Settings are isolated and changed only in memory. There is no runtime pump, import, worker, removal plan, physical device action or settings save. Production completion and notification behavior is unchanged from **34125fc**.

Mac `Ejector/ImportCompletionNotification.swift` is unchanged at baseline `384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c`, Git blob `581b325b8cc92756f79265a9494fa517960cdc8b`. The source contracts require dismissal to retain the pending choice and a separate manual review before eject. These native checks advance that contract without claiming the complete import/notification timing harness.

## Native observations

All paths below are beneath `%USERPROFILE%/.codex/EasyEjectBuildTools` or its sibling `EasyEjectFixtures`. Computer input uses the supported Windows computer-use skill.

| Scope | Observed result |
| --- | --- |
| `ui-notification-native-20261011`, PID11368 | Actual blocked completion → Return → Keep Connected. With one pending choice, notifications off invokes the real delivery route and records zero shown/clicked/closed callbacks. The next enabled request follows about **21.5 seconds** later; no disabled-request callback intervenes. |
| Same scope, enabled request | Window presenter is minimized; actual shell **Shown** at `18:00:42.3577832Z`, then **Closed** at `18:00:42.3711046Z` (about **13.3ms**). Pending count remains one, clicked count zero, working false and no confirmation open. `AppWindow.IsVisible` remains true while the presenter is Minimized; it is not proof of an onscreen window. |
| Same scope, shell inspection | Fresh supported window/app inventories expose no notification surface to inspect or click. **Visible delivery and click-to-reveal remain unverified.** A shell callback alone does not establish a visible toast, its duration or its content. No notification/OS/security setting was changed to force delivery. |
| `ui-notification-pending-native-20261011`, PID31532 | Empty pending review reports “No current import eject choices. Exact saved batches remain in Import history.” A generated no-media completion then shows the actual no-matching-media title, warning that photos/skipped previews/other files may remain, physical-device/all-volumes warning, disabled Eject and Keep Connected default. Quick Share is absent because the entry has no batch. Actual Return keeps it connected. |
| Same scope, pending picker | The retained single entry appears with Review import initially disabled and Close highlighted. **Return selects the first row rather than closing**; it enables Review import but takes no review/eject action. An explicit click on Review import opens a fresh real no-media completion. Actual Escape keeps it connected. A subsequent notifications-off callback records pending=1, working=false, confirmationOpen=false and zero shell callbacks. |
| Both owned scopes | Finish gates are written only after settled idle observations. Actual idle tray Quit callback exits **0**, result PASS, no `unhandled.txt`. No original/media was copied, deleted, formatted or ejected. |

The no-media entry is seeded in process only, with an absent synthetic source, no removal authority and **no batch**. The isolated history contains one preexisting generated UHD batch solely to identify/validate the owned fixture. It remains byte/time exact; these checks do not fabricate a no-media history record. Pending review invokes production registry and dialog code, rather than a replacement dialog.

## Builds and retained evidence

- `Codex020-notification-native-20261011.log` and `Codex020-notification-pending-20261011.log`: warning-free engineering builds; native completion-focus/presenter/compact footer/Stop/tray draft/editor/busy/idle regression PASS. Automated fixtures `ui-44a3747b34b84106bb752339b56d4948` and `ui-75a91042d6bf4e39803ec1d07afc36ec` are retained separately from the two input sessions.
- `Codex020-notification-pending-normal-20261011.log`: final warning-free normal build. Metadata contains KeepConnectedDialog/TrayFlyoutWindow and excludes UiTestHost, notification/completion inspection, keyboard probes and focus regression/diagnostics. Normal executable never launched. No new default-suite count is claimed; prior selected **246/0** and full **1454/0 each runtime** retain their separate source scope.
- Aggregate `Codex020-notification-pending-inspection-20261011/native-final.json`, SHA-256 **`c55633fcc560ad2c7ca188a81a55545430b9ba1c17856582546e6295381e9dfa`**, pins loaded modules, exits, source, event/keyboard logs, before snapshots and normal metadata exclusion. Both scopes exit0/no exception. All **21** generated destination files and **2** live records retain exact length/hash/write time and counts, with **zero destination additions**; each isolated copied history remains exact with no new configuration files.

Commands: `Windows/modern/build-ui-tests.ps1 -BuildRoot <fresh-root>`; owned engineering executable `<fresh-ui-root> inspect-notifications`; `Windows/modern/build.ps1 -BuildRoot <fresh-root>`. `finalize-evidence.ps1` in the aggregate directory performs exact file/count, event, shutdown, metadata and current ZIP assertions. It preserves JSON timestamps as strings.

The [current unsigned 0.2 ZIP](REVIEW_020_20261011.md) from production **34125fc** remains exact: **107459543 bytes / 650 entries**, SHA-256 **`12bf37369652345f44ab005569063953eb62fba6026d25a01156119132145137`**. This milestone changes test-only source and documentation, so it does not supersede that production payload. No push/publication occurred; Ryan's publishing OK remains pending.

Actual shell visual delivery/click-to-reveal, full import sequencing/busy timing, formerly valid removal-plan expiry, physical eject/veto/cleanup, genuine camera footage/official LUT, broader monitor/theme/DPI and travel/trusted-distribution acceptance remain open. Full goal remains active.
