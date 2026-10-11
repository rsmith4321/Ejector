# Exact packaged backend regression · October 11, 2026

The current unsigned **0040e58** review backend now has a fresh full-suite, selected-contract and supplied-tool replay against its **exact Release DLL bytes**, under the same bundled .NET10.0.12 core runtime bytes. This strengthens backend evidence; it does not launch or certify the normal WinUI executable, trusted distribution or physical travel setup.

## Bound target and method

Target: `C:\Users\rsmit\.codex\EasyEjectBuildTools\CodexReview-0040e58\review\app\EasyEject.Runtime.dll`, **952832 bytes**, SHA-256 **`771d34714946c7c2a90dd8a85ce637b42e7946d03b1756e3d94a5685f4bf09ed`**. Source is exact Windows archive **0040e581768b349be702d2dc09ff666112082da6**.

Existing FullContracts and ModernContracts sources from that archive are published Release/self-contained into separate fresh test folders under `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-packaged-regression-20261011`. Compilation is warning-free. Their compiled-reference Runtime initially has different bytes. Only those stopped, owned runner copies are replaced with the exact packaged target DLL before execution; the review folder is never changed. Full-suite protocol/package fixtures use freshly compiled unsigned Framework binaries from the same archived source, with warnings as errors. They do not establish a new full Framework-suite result.

Live OS module observations for every parent runner bind **EasyEject.Runtime.dll, coreclr.dll, hostpolicy.dll and System.Private.CoreLib.dll** to the exact corresponding packaged hashes/lengths. Core runtime file version is **10.0.1226.42308**. Runner hashes, loaded paths, process IDs, start/exit records and logs are retained. This is loaded-path/on-disk-byte evidence, not an in-memory integrity or complete dependency-closure claim.

## Results

| Run | Process | Result |
| --- | --- | --- |
| Full default contracts | PID20204 | **1454 passed / 0 failed**, exit0; no real device eject API used. |
| Selected modern contracts | PID31644 | **246 passed / 0 failed**, exit0. Static WinUI source/theme checks and service/controller contracts are included; this runner does not render WinUI. |
| Existing supplied tools on generated clips | PID31244 | **16 passed / 0 failed**, exit0; positive official LUT conversion explicitly **NOT RUN**. |

Counts overlap and are **not summed**. No test source, expected outcome or default registration is changed. All stderr logs are empty. Actual-tool cases encode/probe/full-decode 1080p/4K, landscape/portrait/rotation, audio/silence, smaller clips/frame rate, HDR refusal, exact receipts/reuse/conflicts, changed-original/destination refusal, cancellation/partial retry, separate-process history and abrupt owner/encoder loss. Luna identity is injected into generated MP4 metadata. These are not genuine-camera/I-Log/color/compression acceptance, physical source eject, complete WinUI restart or another destination filesystem.

Existing supplied executables remain exact before/after: FFmpeg SHA-256 `6c03ed874bf9c219d3ba865339b48a106eef11fac07e8e53bb599ac59214469d`; FFprobe `fb41be6f4386df4625d13eec932ec926be4744853759c7c100947edc7b917096`. Paths come from the existing tool record; no tools or LUT are downloaded/installed/copied into the product, and no LUT is silently substituted.

Independent finalization verifies all **386** extracted archive files against their decompressed archive entries; all **649** review-manifest files and both live configuration files preserve exact hash/length/write time/count. The current **650-entry** distribution ZIP remains exact at **107459831 bytes**, SHA-256 **`e941e2c49a9bb88d65d6d24234e6cd2fbcd711350ef2c7cf43105a7e56198bb4`**. Mac `Ejector`, `Tests` and `Ejector.xcodeproj` have no Git differences from baseline **384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c**. No new review/config files, normal app launch, physical device action, system setting change, signing, push or publication occurs.

Aggregate `Codex020-packaged-regression-20261011/final.json` SHA-256 **`54e07f0de8c54a7475e87ab393788b60e56379f080952d72cfadbd124b80e4dc`** retains these assertions and pins. `prepare.ps1`, `run-owned.ps1 -Run selected|real` and `finalize.ps1` remain there. Publish commands use existing SDK10.0.401, `-c Release -r win-x64 --self-contained true -p:EasyEjectBuildRoot=<fresh-root>/build`, with the unchanged archived test projects. The full runner is launched with no arguments; the real run uses `--share-real-tools <existing-ffmpeg> <existing-ffprobe>` only.

Read-only inventory during this scope reports **one internal disk / five volumes / no external source**, with operations blocked without notification registration. The attached Insta360 from the earlier card review is no longer present; no drive letter or removal permission is inferred. See the [current full-goal gate audit](../COMPLETION_GATES_20261011.md) for remaining acceptance. Full goal stays active and the expired heartbeat stays paused. Current 0.2 publishing OK remains pending.
