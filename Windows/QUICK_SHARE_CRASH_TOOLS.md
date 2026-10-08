# Actual sharing tools after abrupt owned process loss

The opt-in generated-media rehearsal now terminates its exact owned sharing test process after the second real encode completes and **before** that output's probe, full decode or publication. The first clip has already passed all validation and has an exclusively published copy/receipt. A parent nonce and bounded fixture/history/tool hashes bind the child, which announces its exact checkpoint and waits. The parent uses the retained `Process` handle to terminate only that child, observes a nonzero terminal exit and never admits an automatic retry.

The parent then checks the complete saved-original hashes, unchanged history and absent generated source, exact first copy/receipt, and one abandoned private `output.mp4`. A fresh process explicitly retries: production sharing ignores that unverified private output, reuses the validated first copy, and encodes/probes/fully decodes/publishes only the remainder. Another fresh process reuses both exact published copies and receipts without encoding or decoding again. The private output's path, bytes and SHA-256 stay unchanged through both actions. No completion/eject tokens are restored. Normal operation reservations are available after each returning child.

Only the existing explicit `--share-real-tools` entry can invoke these cases. `--restart-only` restricts that opt-in command to the existing cold restart/graceful cancellation controls and the new abrupt-loss group. Default tests and production UI dispatch never enter it. The expanded cold fixture modes retain the original challenge boundaries. Production sharing code is unchanged.

## Verified source and commands · October 8, 2026

Snapshot is committed **8b778264179a61b3c516be657f0ec05cd62ffc5f** plus only three owned test files. Concurrent backend-identity work is excluded and preserved. `C:/Users/rsmit/.codex/EasyEjectBuildTools/SharingCrash-20261008/source-scope.json` binds all overlays; `final-evidence.json` confirms their current hashes, runner/backend hashes, every child outcome and zero final owned test processes.

```powershell
& C:/Users/rsmit/.codex/EasyEjectBuildTools/verify-sharing-crash-20261008.ps1
```

The retained helper requires a fresh external root, compiles the Framework runner with warnings as errors and the modern full-contract project with zero warnings/errors, and runs each as:

```text
<owned-runner.exe> --share-real-tools --restart-only <existing-ffmpeg.exe> <existing-ffprobe.exe>
```

Both return **3 passed /0 failed**, fourteen child outcomes per run (twenty-eight combined). Counts overlap earlier restart/cancellation evidence; they are not a full-default or full actual-tool suite count. Terminal **11140 exit zero**, durable passed with no failure, **17:06:40Z–17:07:04Z**. `framework-compile.log`, `framework-test.log`, `modern-compile.log`, `modern-test.log`, `result.json` and `final-evidence.json` are under the snapshot's external root. No failed initial attempt or production fix was required.

The already supplied FFmpeg/FFprobe are in `Documents/Gyroflow-DJI-LUT/ext/ffmpeg-n9.0.2-22-g46d8f462ee-win64-gpl-shared-9.0/bin`. Preflight requires their previous SHA-256 values **6C03ED874BF9C219D3BA865339B48A106EEF11FAC07E8E53BB599AC59214469D** / **FB41BE6F4386DF4625D13EEC932EC926BE4744853759C7C100947EDC7B917096** and verifies their actual version replies. No new tool or LUT was acquired or installed.

## Scope and remaining gates

This proves abrupt **test-process** loss at this particular checkpoint, with generated 640×360 standard-color media, injected Luna identity and fake same-host destination discovery. The encoder has already exited when termination occurs. It does not prove interruption of a running encoder/job, Windows crash/power loss, durable survival across storage failure, actual WinUI restart/lifecycle, genuine camera identity/recording-mode/color correctness, official positive Luna LUT conversion, physical eject, other filesystems or trusted distribution. The abandoned private output is deliberately retained throughout production retry/reuse; only the existing `Fixture.Dispose` removes the bounded owned fixture after all checks finish. No automatic production orphan deletion was added or tested.

No original user media, real device, saved normal configuration, app review payload, startup/security/power/display setting, Mac/website source, signing or public distribution changed. Native foreground testing still waits for desktop access; expressly identified disposable hardware, official LUT/genuine camera and trusted delivery gates remain. The full Easy Eject goal stays active.
