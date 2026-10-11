# Native release ZIP and folder choices · October 11, 2026

Actual Windows ZIP and extracted-folder pickers now have evidence for cancellation with zero backend work, exact selected-path handoff, honest backend refusal and recovered controls. This does not verify an archive/application package or grant production update trust.

## Boundary

`UiReleaseInspection.cs` remains entirely `#if UI_TESTS`. An explicit owned `release-picker-inspection.txt` marker admits Archive/Extracted choice controls only in the generated backend. Its `Run` still throws for those actions before opening selected payload contents; no authentication, network, replay, restore, installation or executable capability is added. Normal unconfigured backend admission is unchanged. An ordinary entry in the same session proves that the fixture marker cannot enable its remote actions.

Engineering PID **30372** runs `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-release-paths-ui-20261011\bin\EasyEject.Modern\Debug\net10.0-windows10.0.26100.0\win-x64\EasyEject.Modern.exe`. Fresh owned fixture: `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-release-delivery-paths-20261011`. Only owned generated debug settings/log/review marker seed preferences. Targets are an empty generated ZIP and a generated folder containing one text marker, not application payloads. Live configuration is not copied or changed. Modern/Runtime/installed .NET10.0.0 coreclr module paths/hashes are recorded; normal self-contained Release UI is not launched. Inventory shows five internal/protected volumes on one disk, no external camera/card.

| Actual native input | Observed result |
| --- | --- |
| Verify selected ZIP → Open dialog → Cancel | Parent disabled while native picker owns selection; return says “Choice cancelled. No new action started.” Both choice controls recover. Backend runs/presentations remain0. |
| Verify extracted folder → Select Folder → Cancel | Same recovery, still zero backend runs/presentations after both cancellations. |
| Fresh ZIP picker → exact absolute generated-empty.zip path → Verify ZIP | Native filename value is verified before submission. Backend receives that exact normalized owned path, one Archive start/terminal, then refuses. Real UI shows “Release action did not finish…” with the engineering capability refusal, and actions recover. |
| Fresh folder picker → generated-extracted-folder under verified owned parent → Review extracted folder | Native Folder value and parent address are verified before submission. Backend receives the exact owned absolute directory, one Extracted start/terminal, then refuses. No acceptance is presented; controls recover. |
| Close inspection, ordinary Updates & installation | Actual unconfigured channel/publisher message appears. Check, ZIP and extracted-folder actions all disabled despite the fixture marker still existing. |
| Close, settle main controls, actual tray Quit | Exit **0**, no `unhandled.txt`; no repeated input after post-Quit unusable-window capture. |

Native child pickers appear within the parent capture. ZIP focus is established by visible-field click/native Alt+N and the filename caret; provider focus still reports Search Box, so it is qualified rather than treated as proof. The complete entered filename value is verified before submission. Folder selection uses its observed row under the exact parent. No search/shell command is submitted and no user file is selected. Native type text reports “All Files”; this session does not claim a visible ZIP-only filter despite the source `FileTypeFilter.Add(".zip")`. Screenshots are inspected directly, not saved or reprocessed. Settled observations follow asynchronous transitions before further input.

## Verification and preservation

- `Codex020-release-paths-ui-20261011.log`: **0 warnings/0 errors**, existing automated native root/theme/text/completion/pending/presenter/Stop/tray/editor/busy/idle regressions PASS; fixture `ui-72cff86f8bbc462795bdc21b883c3b87`, no exception.
- `Codex020-release-paths-normal-20261011.log`: **0 warnings/0 errors**, selected **246 passed/0 failed**. PE metadata retains the production release view but excludes the generated backend, new picker flag and all prior engineering pause/activity/diagnostic hooks. No full-suite/tool-count or broad binary-identity claim.
- Trace has only Archive then Extracted, exact selected paths, two terminal records, zero accepted backend presentations and clean disposal. Failure text is displayed by the real presenter; backend presentation counts do not count that error UI as a successful verification.
- Generated ZIP/text retain exact hash/length/write time. Both live records and two isolated profile files retain exact hash/length/write time/count. Login registration absent, Mac baseline384cb767 exact.
- Current production/package source remains **0040e58**. Unsigned ZIP remains **107459831 bytes**, SHA-256 **e941e2c49a9bb88d65d6d24234e6cd2fbcd711350ef2c7cf43105a7e56198bb4**. No device/media, OS setting, install, autostart, push or publication action.

Evidence root: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-release-paths-native-20261011`. Launch/finalizer scripts, actual accessibility/path/cancellation/refusal observations, traces, process/modules/exit and preservation pins remain. Aggregate `final.json` SHA-256 **f2ec781a02ee3c20fd511a6fa2ccd15380065aa737aeaa7222c51d5b42d0c72c** verifies these scoped assertions.

Native picker cancellation/path/error handling is now proved for these generated targets. Genuine configured metadata/CMS/HTTP/revocation/publisher/package/ZIP/folder acceptance, XAML presentation-failure lifetime, accepted restore/publication/uncertain recovery and installation/rollback remain open. Physical/genuine-media/official-LUT/travel/display/normal-loader/signing/publishing gates are unchanged. Full goal active; an engineering-refused choice is not package verification.
