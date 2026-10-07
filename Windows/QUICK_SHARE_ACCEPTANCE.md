# Real Quick Share tool rehearsal

Status: command prepared; actual FFmpeg/FFprobe and positive official LUT runs are **not yet performed**. This is one part of [the full parity checklist](PARITY.md), not a complete-port certificate.

Preparation verification: test-source `290c4719ff1485cff7792ff870403a85964c4daa` compiles with warnings as errors. The existing default suite passes **1184/0**, exact terminal session **59605** exit zero, `real-share-rehearsal-final-test.log`. Four startup refusals pass separately (missing arguments, identical tools, absent tool, wrong LUT before launch), `real-share-rehearsal-gates.log`; inert selected files remain unchanged in retained owned fixture `run-bb8ece2aee9446d6`. This proves compilation/default regression and those refusal gates, not execution of the prepared actual-tool cases.

`--share-real-tools` is an opt-in test-executable mode. It is excluded from `build.ps1 -Test`. Supply separate, already installed FFmpeg and FFprobe executables. Nothing is downloaded or installed. The command executes those selected binaries, checks their version responses, and records their SHA-256 hashes. It does not configure the normal app.

The command generates one-second test-pattern clips and a sine-wave audio track using FFmpeg. It adds an ignorable MP4 `free` box containing an injected Luna identity, imports those generated files into owned fixture storage, saves opt-in profile/tool/history records only inside that fixture, and runs the production manual sharing service with fake destination/device guards. It models source absence by renaming only its owned source folder. Every generated fixture stays under `%USERPROFILE%\.codex\EasyEjectFixtures\run-<id>` outside OneDrive and is cleaned on completion. Supplied executables/LUT remain read-only. No user footage, native discovery, eject, source cleanup, login registration, power/security/graphics settings, or network operation is requested.

After the tool paths are supplied, use the installed compiler and record the exact checkout/build, log and terminal exit code:

```powershell
./Windows/build.ps1
# Replace these values with the explicitly selected existing files.
$chosenFfmpeg = 'C:\chosen-tool-folder\ffmpeg.exe'
$chosenFfprobe = 'C:\chosen-tool-folder\ffprobe.exe'
$chosenLut = 'C:\chosen-LUT-folder\Luna_I-Log_to_Rec709_BT1886_s33_v2.cube'
$rehearsalLog = Join-Path $env:USERPROFILE '.codex/EasyEjectWindowsPreview/real-share-tools.log'
New-Item -ItemType Directory -Force -Path (Split-Path $rehearsalLog) | Out-Null
git rev-parse HEAD | Set-Content -LiteralPath $rehearsalLog
& "$env:LOCALAPPDATA/EasyEject/PrototypeBuild/EasyEject.Tests.exe" --share-real-tools $chosenFfmpeg $chosenFfprobe $chosenLut 2>&1 | Tee-Object -FilePath $rehearsalLog -Append
$rehearsalExit = $LASTEXITCODE
"Terminal exit: $rehearsalExit" | Tee-Object -FilePath $rehearsalLog -Append
if ($rehearsalExit -ne 0) { throw 'Real sharing rehearsal did not pass; retain and review the log.' }
```

More than 3 GiB free is required at the fixture location. Native job ownership, bounded tool output and per-process deadlines apply. Exit `2` means preflight did not finish, `1` means a case failed, and `0` means only the cases actually run passed. With no fourth argument, seven standard-color cases run and the log explicitly reports positive LUT conversion **NOT RUN**. A full LUT run requires the user-supplied official asset with SHA-256 `e3e5c3ab4ca7c166f3ebb740b45ed8cbce649b0c18dca9d5661754c53b9ee17d`; a supplied incorrect LUT refuses before launching tools. Do not substitute or bundle a fabricated LUT.

| Prepared case | Required evidence from an actual run |
| --- | --- |
| Landscape 3840×2160 at 60 fps with audio → 1080p | 1920×1080, 30 fps, H.264/yuv420p/limited BT.709, stereo AAC, input/output probes and full decode, source/output/receipt hashes, fresh-service exact reuse |
| Landscape → 4K | 3840×2160 with independent dimension/rate/duration/audio checks and exact reuse |
| Portrait 2160×3840 → 1080p | 1080×1920, retained orientation/aspect dimensions, stereo AAC and exact reuse |
| Silent 640×360 at 24 fps → 4K option | 640×360 without enlargement or added audio, 24 fps and exact reuse |
| Changed existing output | Foreign conflicting bytes retained; distinct fresh output and stable verified reuse |
| Cancellation on actual encoder progress | Cancellation observed before publication, zero published output/receipts, private-work cleanup, reservation release and successful fresh manual retry; this does not prove the exact point within frame encoding |
| Changed saved original/current destination | Refusal before any media process, changed bytes retained, restored fresh review retries; destination identity remains a fake |
| Optional official LUT at 1080p and 4K | Actual transform/probe/full decode, bound official LUT hash and output receipt, unchanged generated input and reuse |

This prepares stronger evidence than the synthetic protocol helpers, but the actual runs are still pending. Generated SDR patterns with injected identity cannot certify genuine Luna metadata, I-Log exposure or visual LUT color accuracy, camera bit depth/codec/rotation variants, or whether compressed copies are smaller than a genuine source. These remain acceptance items alongside real mixed-camera/package footage, recipe-change preservation, retained last-20 history across actual app restart, foreground manual size/LUT/cancellation/return-to-choice behavior, and the complete named [Mac sharing inventory](SHARE_REGRESSION_MAP.md).

Physical import/removal, filesystems, grouping/veto/reconnect and travel rehearsal still require Ryan to identify disposable camera/card/reader/destination hardware and the authorized test scope. Signing and direct-download proof remain separate gates. A successful standard-only or full LUT rehearsal closes only the properties it actually demonstrates.
