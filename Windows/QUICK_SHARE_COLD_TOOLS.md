# Manual sharing with real tools across process restart · October 8, 2026

## Partial cancellation and fresh manual retry · source 35c9f62

The subsequent opt-in group generates two owned clips, publishes and fully validates the first, then cancels from the second actual encoder's progress callback. The cancelling child proves one published copy/receipt and unchanged originals, with current private work cleared and reservations released. A new test process explicitly retries: it reuses the first exact output/receipt and creates/probes/fully decodes only the remaining copy. A third fresh process reuses both without encoding or decoding, preserving both output/receipt hashes and exact history. No eject token is created; no automatic retry occurs.

Final isolated **45c9189** plus only the two owned test files passes **13/0 on Framework and modern**, sessions **40010/33148 terminal exit zero**, modern build **0 warnings/0 errors**. There are eleven child outcomes per run (twenty-two combined) and zero final owned processes. Helpers are `C:/Users/rsmit/.codex/EasyEjectBuildTools/compile-real-share-partial-framework-20261008.ps1` and `compile-real-share-partial-modern-20261008.ps1`; snapshot is `RealSharePartialSource-20261008`. Evidence in `%USERPROFILE%/.codex/EasyEjectWindowsPreview`: `real-share-partial-framework-20261008.log`, `real-share-partial-modern-20261008.log`, `real-share-partial-source-hashes-20261008.json`, `real-share-partial-terminal-evidence-20261008.json`. Both modern runtime copies match SHA **3E745DB965F29F9240C80BED47069B4334EDDD22BA425CDDBDBBBEC9696CF429**; Framework runner **69689C837FEFD1198FCA838CDFB35CD6900DA36B5DC78243007CB9E17F2C7047**, modern managed harness **060989DE02BEAE11DDCBB73FF5D50E7A6CB03FF433039CF35FD56D8D85E23AB7**. Later release-review integration is excluded/preserved; no combined current-tree/WinUI/default-suite pass is asserted. Counts overlap the earlier twelve groups below. Positive official LUT and all actual app/hardware/travel/trust gates remain open.

Test source **0a61c4b** bridges two earlier evidence scopes: [cold-process synthetic sharing](QUICK_SHARE_RESTART.md) and [actual-tool generated-clip conversion](QUICK_SHARE_ACCEPTANCE.md). It extends the existing explicit `--share-real-tools` rehearsal with one group that launches eight separate test processes. Default tests never enter this mode. Production import/sharing/UI behavior is unchanged.

The Mac baseline `384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c`, `QuickShareImport.swift`, persists exact destination copies separately from connection-bound completion/eject authority. The Windows production manual sharing service now has actual-tool evidence for these persisted choices across test-process boundaries:

1. Prepare rereads exact saved history, device opt-in and tools without running media tools or publishing an export. An incorrect parent challenge refuses before manual work.
2. A fresh process explicitly chooses 1080p, probes/encodes/fully decodes the generated clip, and publishes its copy and receipt. Another process reuses their exact hashes without encoding or decoding again.
3. Separate processes explicitly create and reuse the 4K recipe while retaining the earlier 1080p bytes. The 640×360 generated source is not enlarged by either choice.
4. A changed generated saved original refuses before any media call and remains changed. All prior outputs/receipts are retained byte-for-byte. The parent restores only its owned fixture bytes; a new manual process then reuses the earlier export. Saved history remains unchanged and the generated source path remains absent.

Each child requires the parent-issued nonce, SHA-bound bounded batch snapshot, exact history/tool-configuration hashes, contained `EasyEjectFixtures/run-…` storage, and a fresh `WindowsDestinationGuard` with fake discovery. The actual selected tools are read from the challenged fixture configuration and validated by the production service. Every child starts and ends with zero completion/eject tokens; operation reservations are available after return. `--owned-cold` is an internal submode of the explicit real-tool command, not normal app launch behavior. The parent starts only its own compiled test executable, without a window, drains its output, and enforces a deadline on that exact child.

## Final verification

An isolated Git archive **933ea37** plus only `tests/RealQuickShareAcceptance.cs` and `tests/RealQuickShareColdAcceptance.cs` is retained outside OneDrive at `C:/Users/rsmit/.codex/EasyEjectBuildTools/RealShareColdSource-20261007`. It excludes subsequently evolving managed-application/archive/channel work. Both owned worktree/snapshot hashes match. Final runs on October 8:

| Runner | Result | Terminal evidence |
| --- | --- | --- |
| Installed Framework x64 compiler, warnings as errors | **12 passed /0 failed**, eight child outcomes | session **32361**, exit **0** |
| Pinned SDK 10.0.401, modern `EasyEject.FullContracts.csproj` | **0 warnings/0 errors**, **12 passed /0 failed**, eight child outcomes | session **22279**, exit **0** |

These twelve groups are the eleven existing standard-color actual-tool groups plus the new cold-process group. The official LUT argument was omitted, so both positive LUT groups explicitly remain **NOT RUN**. Counts overlap with earlier rehearsals; they are not default/full-suite counts. The modern helper builds the runtime/test project only, with the maintained build properties and private process-only SDK/cache environment; it does not rebuild or launch WinUI. No new live CoreCLR module capture was made for these runs.

Reproduction helpers, retained outside the repository:

```powershell
& C:/Users/rsmit/.codex/EasyEjectBuildTools/compile-real-share-cold-framework-20261007.ps1
& C:/Users/rsmit/.codex/EasyEjectBuildTools/compile-real-share-cold-modern-20261007.ps1
```

Both execute the explicitly selected, already installed FFmpeg/FFprobe under `C:/Users/rsmit/Documents/Gyroflow-DJI-LUT/ext/ffmpeg-n9.0.2-22-g46d8f462ee-win64-gpl-shared-9.0/bin` on generated fixtures only. Version output remains `n9.0.2-22-g46d8f462ee-20261006`; executable SHA-256 values are **6C03ED874BF9C219D3BA865339B48A106EEF11FAC07E8E53BB599AC59214469D** and **FB41BE6F4386DF4625D13EEC932EC926BE4744853759C7C100947EDC7B917096**. Nothing was downloaded, installed or configured in the normal app.

Final evidence under `%USERPROFILE%/.codex/EasyEjectWindowsPreview`:

- `real-share-cold-framework-20261008.log` and `real-share-cold-modern-20261008.log`: actual tools, all cases and sixteen child outcomes across both runs.
- `real-share-cold-source-hashes-20261007.json`: exact tested owned files.
- `real-share-cold-terminal-evidence-20261008.json`: terminal sessions, counts, final zero owned processes and binary/source hashes. Both modern runtime copies match **79CF919C736AF9BB66B78707E14B6827AD744C830F9E81C86D1DFC8158AF4473**; modern managed test runner **69B458D2BF379CF9FD8E5D6562F5879DDEE7F95A556324218AABCE94AEBD7428**; Framework runner **EBD95210C910190CDAE37B43CE715CEB52BE3D6DA6BC09D458B2780BBB00A9A1**. These do not assert identity with an earlier WinUI payload.

Initial attempts remain recorded: `real-share-cold-framework-20261007.log` refuses noncanonical helper tool-path arguments before execution; the helper was corrected without changing app policy/assertions. `real-share-cold-framework-corrected-20261007.log` reports a preflight tool timeout; `real-share-cold-modern-20261007.log` ends after runtime compilation without a produced test runner. Their handles were missing and actual owned processes absent before retrying the unchanged snapshot. They are not successful evidence.

## Remaining full-port acceptance

Generated Luna identity is injected into an MP4 free box; fake destination discovery and a source-folder rename do not prove camera identity, recording-mode/color accuracy, physical eject, reader/filesystem/travel behavior, or actual WinUI restart/selection/tray/notifications/Stop. Positive official Luna LUT and trusted direct distribution remain open. No real camera/user media, device action, normal settings/history, autostart, security/power/display, signing, installation or publication was changed. Full goal remains active; expired overnight schedule remains paused.
