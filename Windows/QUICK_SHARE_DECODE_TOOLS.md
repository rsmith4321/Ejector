# Actual damaged-frame validation · October 8, 2026

Test source **3b9b1cd** adds one opt-in generated-media case to `--share-real-tools`. Production behavior and the default test dispatcher are unchanged. It tests the full-output decode contract in Mac baseline **384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c**, `Ejector/EasyShare.swift`, and Windows `QuickShareEngine`: stream metadata alone cannot authorize publication.

The fixture imports its own generated silent 640×360/30 fps clip through the existing fake-device fixture, hides that owned source, and prepares a manual sharing request from exact persisted originals/history. The actual selected FFmpeg encodes normally. A test-only wrapper then zeros the final quarter of the single owned private MP4 media box, retaining early frames, container/sample tables and file length. Mutation is restricted to that batch's private `.easy-share-…/output.mp4`, with bounded file/box sizes; no original, published output or user media is touched.

The production output probe accepts the retained metadata, then the actual FFmpeg full decoder fails. Assertions require one encode, two probes and one decode, a non-cancellation failure, zero created/published copies/receipts/outputs, removed private work, unchanged original hashes and unchanged verified history, and released disk reservations. A fresh explicit manual service/request encodes, probes, fully decodes and publishes one valid copy/receipt. Another fresh service reuses their exact hashes without another encode/decode. No automatic retry is added.

## Verified artifacts

The isolated source is archive **a95adca** plus only `Windows/tests/RealQuickShareAcceptance.cs`, outside OneDrive at `%USERPROFILE%/.codex/EasyEjectBuildTools/RealShareDecodeSource-20261008`. Final worktree/snapshot test SHA matches **19E5F79189D7E228559530DD22F79DFB11A831CE7897B4397C5BCB961C38A46F**. Concurrent modern release-policy integration is excluded and preserved.

- Framework: **14 passed/0 failed**, warnings-as-errors compilation, terminal session **41809 exit zero**.
- Modern installed .NET: **14 passed/0 failed**, **0 warnings/0 errors**, terminal session **85768 exit zero**.
- Both runtime DLL copies: **1FFE95F47A717A82F57FA14D03497B0F88827F091E3ED5BAAF51A7EE14D0B37A**.
- Framework harness: **6D7463AFB63DD2158FB073775BCCB7EAEAD960CFA6FCFA2A5723690BE65834CC**.
- Modern managed harness: **6EE1D098B22A628FF2AD15DEEE5900941EED822ED40EE6C650622B2B712331B1**.
- A live initial modern test parent was observed loading installed `.NET10.0.0/coreclr.dll`, SHA **5201734DB1EF225D23D8EFCA6198B47CC70EDDD5A12601E16D76A7DA68C5907D**. This is test-parent evidence, not foreground app or release-runtime approval. Final owned test-process count is zero.

Helpers in `%USERPROFILE%/.codex/EasyEjectBuildTools`:

```powershell
& ./compile-real-share-decode-framework-20261008.ps1
& ./compile-real-share-decode-modern-20261008.ps1
```

They use existing installed tools at `Documents/Gyroflow-DJI-LUT/ext/ffmpeg-n9.0.2-22-g46d8f462ee-win64-gpl-shared-9.0/bin`, canonicalize executable paths and invoke the explicit real-tool command. FFmpeg SHA **6C03ED874BF9C219D3BA865339B48A106EEF11FAC07E8E53BB599AC59214469D**, FFprobe SHA **FB41BE6F4386DF4625D13EEC932EC926BE4744853759C7C100947EDC7B917096**. No tool or LUT is installed.

Evidence in `%USERPROFILE%/.codex/EasyEjectWindowsPreview`: `real-share-decode-framework-final-20261008.log`, `real-share-decode-modern-final-20261008.log`, `real-share-decode-source-20261008.json`, `real-share-decode-live-runtime-20261008.json`, and `real-share-decode-terminal-20261008.json`. Initial logs without `-final` retain **13/1**, terminal sessions **42276/54145 exit one**: zeroing the entire media payload failed the intended validation-stage assertion. The fixture was changed to damage only later frames; required probe/decode/publication assertions were retained. Those initial runs are not passes.

Counts overlap the previous thirteen groups; no default/full-suite rerun, current dirty-tree integration or WinUI acceptance is claimed. Injected Luna identity in generated media, fake guards and same-host storage do not prove genuine camera media, positive official LUT/color accuracy, abrupt application crash, real device removal, alternate filesystem/travel behavior or trusted distribution. Those gates and full foreground/tray acceptance remain open. The full goal stays active; the expired overnight heartbeat stays paused.
