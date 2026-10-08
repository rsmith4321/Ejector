# Manual sharing: owner loss during a live encode · October 8, 2026

This extends the explicit generated-file restart rehearsal. The production backend, encoder recipe, default test dispatch and modern UI are unchanged. Two generated four-second 640×360 clips use an injected Luna selection marker, fake destination guards and a renamed absent source; this does not certify camera media or physical removal.

The test wrapper adds `-readrate 1` only to its second encode. At an early positive frame-progress callback, the challenged sharing child proves that the first copy/receipt has been validated and published, with one decode, three probes and no eject tokens. A read-only ToolHelp snapshot selects the exact encoder by creator PID and executable basename, then verifies its full selected image path and retains its process handle. The parent independently repeats ancestry/image/start-time checks, requires the encoder to be alive less than 2.5 seconds into the paced four-second clip, and kills only its own challenged sharing child.

The exact retained encoder must stop within two seconds without a direct encoder kill. Its exit code is recorded rather than assumed: both observed job-close exits were **0**. The abandoned private MP4 must fail the actual FFprobe command, distinguishing the unfinished encode from a naturally completed file. Failure-only cleanup through a retained owned handle cannot turn a failed containment assertion into a pass.

Fresh manual processes then create only the remaining copy and reuse both exact verified outputs. Original hashes, saved history, first publication/receipt and the abandoned private file remain exact through retry and reuse. No action acquires source-eject authority; no automatic retry or abandoned-work cleanup was added.

## Verified scope

Snapshot: committed **5dcc6b58fd26b98aeae22fabc8eb3e7b6a0a732c** plus only four owned test overlays:

- `tests/RealQuickShareAcceptance.cs`
- `tests/RealQuickShareColdAcceptance.cs`
- `tests/RealQuickShareCrashAcceptance.cs`
- `tests/RealQuickShareBusyCrashAcceptance.cs`

The source hashes in the final packet match the worktree. Concurrent component/version/release work is excluded and preserved. Framework `/warn:4 /warnaserror+` compilation and .NET 10.0.401 FullContracts compilation passed without warnings/errors. The explicit restart-only run passed **4 groups / 0 failures on each runtime**, including the earlier cold restart, graceful partial cancellation and post-encode crash controls. There were **17 challenged child outcomes per runtime**, 34 combined; these are overlapping control/acceptance scopes, not 34 unique default tests.

| Runtime | Owned sharing child | Exact live encoder | Progress frame | Encoder stop | Exit |
| --- | --- | --- | --- | --- | --- |
| Framework | 22516 | 17656 | 6 | 20 ms | 0 |
| .NET 10 | 22168 | 13524 | 7 | 21 ms | 0 |

Terminal session **80142**, exit **0**. Durable result: **17:35:11.852–17:35:48.606 UTC**, passed. Final owned runner count: **0**. Packet:

`C:/Users/rsmit/.codex/EasyEjectBuildTools/SharingBusyCrash-20261008-Final`

It retains exact source scope/hashes, build/test logs, runner/backend hashes, child checkpoint identities, result and final evidence. Framework runner SHA256: **E20BCF6C141B571210613A0E470A6258CF1983BBC97DB1B3875B0340C18847E1**. Modern runner: **CE34E52FAF28B966EC83DD04803ECB6988FE9D5347E66DBE920DEE45F647D56A**. Modern `EasyEject.Runtime.dll`: **15F369B82DE2F2065A093E721B7FDE28FCFFEAEC234043C60ECEC135F3B9DA0B**. This is the isolated test backend, not a freshly published WinUI package.

The previously supplied tools were hash-checked before execution: FFmpeg **6C03ED874BF9C219D3BA865339B48A106EEF11FAC07E8E53BB599AC59214469D**, FFprobe **FB41BE6F4386DF4625D13EEC932EC926BE4744853759C7C100947EDC7B917096**. No tools or LUT were downloaded/installed.

```powershell
& C:/Users/rsmit/.codex/EasyEjectBuildTools/verify-sharing-busy-crash-final-20261008.ps1
# Helper requires a fresh external output directory; retained results are not overwritten.
# Exact runner action on both runtimes:
# <owned runner> --share-real-tools --restart-only <selected ffmpeg.exe> <selected ffprobe.exe>
```

## Retained interruptions and corrections

The first packet, `SharingBusyCrash-20261008`, was interrupted by the human laptop restart before a final result; its partial logs are preserved and are not a complete run. A compile-only checkpoint also remains. Subsequent Framework **3/1** packets retain the missing child diagnostics, an overbroad all-child count, and an incorrect expectation that job closure must produce a nonzero tool exit. The final test selects only the exact tool and requires early live progress, prompt termination and actual rejection of the unfinished private MP4. A separate evidence-record filename error was corrected to the actual `EasyEject.Runtime.dll`; the failed record was preserved, with no test rerun for that read-only correction.

This closes the generated same-host live-encoder/owner-loss rehearsal, not Windows shutdown, power loss, full app/WinUI restart, the full default/current-tree suite, positive official Luna LUT conversion, genuine-camera identity, native tray/dialog/notification/lifetime behavior, identified disposable hardware/filesystems or trusted distribution. The full port goal remains active. No real media, device, security/power/display setting, installation or public release was affected by this test milestone.

Microsoft's [PROCESSENTRY32W contract](https://learn.microsoft.com/en-us/windows/win32/api/tlhelp32/ns-tlhelp32-processentry32w) specifies snapshot sizing and creator PID. The existing production job uses [KILL_ON_JOB_CLOSE](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_limit_information); the test provides direct owned-process observations in addition to that API contract.
