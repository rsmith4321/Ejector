# Current power-aware backend · generated Quick Share acceptance

October 8, 2026 local checkpoint: **16 passed /0 failed**, existing self-contained `FullContracts` runner on the current F810 backend. This closes the gap between the historical a7afd34 actual-tool run and the newer power-aware review. Production source was unchanged; no build or installation was needed.

## Exact run

- Wrapper: `C:/Users/rsmit/.codex/EasyEjectBuildTools/run-power-real-tools-20261008.ps1 -LeaseReady`, after explicit coordinated CPU grant.
- Runner: `PowerConnectionAtomic-20261008/FullContracts/EasyEject.FullContracts.exe --share-real-tools <native absolute ffmpeg.exe> <native absolute ffprobe.exe>`.
- Runner SHA-256: `CE34E52FAF28B966EC83DD04803ECB6988FE9D5347E66DBE920DEE45F647D56A`.
- Backend SHA-256: `F810DFB734CF47AE09FA8A9558DA5F819056FE059692CE31857ABE2AF1F35FAD`, matching the current review and implementation `3ccf740856c853abe361f1472e0183b106546172`.
- Shell session **82508** terminal **exit0**; wrapper PID18448, **2026-10-09T02:58:56.3500024Z–03:00:28.1705617Z** (October 8 local). No remaining FullContracts/FFmpeg/FFprobe process was observed at handback.
- Evidence directory: `C:/Users/rsmit/.codex/EasyEjectBuildTools/PowerRealTools-F810-20261008`, retaining `result.json`, `real-tools.log` and `protected-before.json` / `protected-after.json`.
- Log SHA-256: `4EA778C6570A5B8CD4E8ED9F212CFB7030F9049C135354CF8F5998C4B0118927`.

The six recorded current app/runner dependency pairs match before and after. All four `RealQuickShare*.cs` Git blobs match current committed tests (`power-real-tools-source-bindings-20261008.json`). The isolated runner's `Tests.cs` predates the later power-operation registrations; its actual-tool dispatcher is unchanged. This run adds **16 actual-tool cases**, and makes no new default-suite count claim.

## Passed cases and preserved state

Actual supplied FFmpeg/FFprobe encode, probe and decode generated clips: 1080p/4K, landscape/portrait, silent/small clips and frame-rate preservation; exclusive receipts/reuse and foreign-output preservation; cancellation and changed-original/destination refusal; both display-matrix rotations and HDR refusal; separate-process saved-batch creation/reuse/refusal, partial cancellation/retry, abrupt owner loss and live encoder containment; corrupt-frame decode refusal followed by fresh retry.

Tools were already supplied at `C:/Users/rsmit/Documents/Gyroflow-DJI-LUT/ext/ffmpeg-n9.0.2-22-g46d8f462ee-win64-gpl-shared-9.0/bin`. Before/after executable hashes match: FFmpeg `6C03ED874BF9C219D3BA865339B48A106EEF11FAC07E8E53BB599AC59214469D`; FFprobe `FB41BE6F4386DF4625D13EEC932EC926BE4744853759C7C100947EDC7B917096`.

Before/after checks verify all **649** manifest-listed review files against manifest `76F6D2C0109ED12C7D6042D6DFA504AC32B1AE3F4A7B4538856C5B551169A7A2`. The existing foreground app **PID21224**, start **2026-10-09T02:46:46.1826671Z**, remained the same process/path. Both normal profile-directory files remain unchanged: the fresh **161-byte** issue-state hash `2DA0274519483D90BBC19964F254B4A2A4E651A40F24726267A27FC6C3C85BC0` and **486-byte** tool configuration hash `C366C5017700613C17F734FFC01C8AEACC0F386B61EF19558FD8DE57EE94728D`. Historical issue pins were not substituted or restored.

## Remaining acceptance

Positive official LUT conversion is explicitly **unrun** because no user-supplied official LUT was provided. Luna identity is injected into generated MP4 metadata; fake device guards and same-host temporary storage do not prove genuine camera identity/color, physical removal, destination filesystem compatibility, foreground interaction or complete application restart. Physical sleep/wake delivery, identified disposable hardware, travel and trusted direct-distribution acceptance remain open. No user media, real device action, UI input, settings/security/power changes or publishing occurred. The CPU slot was returned to the coordinator after terminal cleanup; the full goal remains active and the expired heartbeat remains paused.
