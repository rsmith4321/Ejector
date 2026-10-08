# Manual Quick Share across process restart · October 7, 2026 · source 8abd2a9

October 8 follow-up **0a61c4b** also runs the explicitly selected existing media tools across separate test processes: **12/0 on both Framework and modern**, including one new eight-child generated-clip group. [Actual-tool cold-process evidence](QUICK_SHARE_COLD_TOOLS.md) records the distinct source/bytes/logs and scope. The nine synthetic default groups below retain their original evidence; native app restart, real camera/device and positive official LUT remain unproved.

The Mac baseline `384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c`, `QuickShareImport.swift`, retains the first twenty saved batches and keeps destination history separate from connection-bound eject permission. Existing Windows cold-history tests already verify saved copies in separate processes. This milestone extends that evidence through the production **manual Quick Share service**, persistent profile/tool/history reads, a fresh destination guard, synthetic media processing, publication/receipts and exact reuse.

`tests/ColdQuickShareTests.cs` adds nine default regression groups and a focused `--cold-share` command. Parent fixtures create verified generated copies, persist opt-in configuration only under their own root, and rename their own generated source folder to model absence. Each action launches a separate test process, without a window, with an exact nonce marker and SHA-bound expected batch/history. The child admits only the existing `EasyEjectFixtures/run-…` namespace, bounded ordinary snapshot files and contained fixture destinations/tools. An invalid challenge refuses before configuration/media work. It obtains a new `WindowsDestinationGuard` using fake discovery; no native device discovery/removal API or real media tool is called. Every child starts with an empty completion registry and checks that sharing restores no eject token.

| Separate-process property | Checked outcome |
| --- | --- |
| Prepare a retained batch after source absence | No probe, encode, decode, export folder or eject authority |
| Manually choose 1080p and 4K in successive fresh processes | One synthetic validated publication per size, then exact receipt-bound reuse without another encode/decode; prior export and receipt hashes unchanged |
| Twenty newer verified batches replace the earliest history entry | Exactly twenty retained records; the old request refuses while its saved files remain; a current batch can still be prepared |
| Change a saved original | Refuse before media processing, preserve changed bytes; restoring only owned fixture bytes permits a new manually prepared action |
| Change the verified import receipt | Refuse without repairing/deleting it; fresh retry after restoring owned fixture bytes |
| Disable or forget the device profile | Sharing unavailable while exact history remains; explicit fixture re-enrollment permits a new manual action |
| Fresh destination identity differs | Refuse before processing/publication; a separately prepared matching destination can proceed |
| Invalid ownership challenge | Child exits with refusal, unchanged history/originals and no export; a fresh valid challenge works |
| Manually choose Luna LUT when none was supplied | Refuse before media work without installing an asset; a separate explicit Standard choice succeeds despite a saved LUT preference, retaining that preference |

The media process is deliberately **synthetic**: it writes generated text output and supplies fake probe/decode responses. These cases establish persisted orchestration and validation requests, not actual codec/color/LUT conversion. Existing [actual-tool generated-clip evidence](QUICK_SHARE_ACCEPTANCE.md) and [rotation/HDR evidence](QUICK_SHARE_ROTATION_HDR.md) retain their separate source/byte scopes. The source-folder rename is not physical eject, and the test-process restart is not a foreground WinUI restart.

## Source and verification

The isolated snapshot `C:/Users/rsmit/.codex/EasyEjectBuildTools/ColdShareSource-20261007` is archive **c026805** plus only the owned new test file and cold-sharing dispatch/default-suite additions to `tests/Tests.cs`. It excludes separately evolving modern runtime-configuration work. Production source and modern presentation are unchanged by this milestone.

Installed Framework compilation treats warnings as errors. Initial `cold-share-framework-20261007.log` records **0/7**: an unnecessary fixture rename attempted to move the existing configuration directory onto itself, before any child action. Removing that fixture-only rename preserves all assertions. The corrected seven-case run passes **7/0**, terminal **52688**, exit zero. The final nine-case run, adding challenge/LUT checks and export-receipt hash retention, passes **9/0**, terminal **50810**, exit zero, in `cold-share-framework-final-20261007.log`. The maintained compiler helper is `C:/Users/rsmit/.codex/EasyEjectBuildTools/compile-cold-share-framework-20261007.ps1`; all builds/fixtures are outside OneDrive.

Modern maintained verification command:

```powershell
& C:/Users/rsmit/.codex/EasyEjectBuildTools/ColdShareSource-20261007/Windows/modern/build.ps1 -Test -FullTest -BuildRoot C:/Users/rsmit/.codex/EasyEjectBuildTools/ColdShareModern-20261007
```

Final modern terminal **5713**, exit zero, passes **236/0 selected and 1257/0 full**, including all nine new cold-sharing groups and twenty separately launched sharing children. App/runtime/runners and unsigned Framework fixtures compile with **0 warnings/0 errors**. Log: `C:/Users/rsmit/.codex/EasyEjectWindowsPreview/cold-share-modern-full-20261007.log`. Selected/full/Framework counts overlap, are not additive, and exclude the separately committed runtime-configuration layer; this is not a combined current-worktree pass. No optional real-media/large-file/device suites are implied.

Actual full parent PID **34540**, start **21:34:30 EDT**, uses installed .NET **10.0.0** CoreCLR, recorded in `cold-share-modern-modules-20261007.json`; child PIDs/outcomes are recorded in the logs. After terminal, all four runtime project/app/selected/full copies match SHA-256 **BE68966D645D6AC5DB042F0707FFDFE49B5DD09DF60B792FA99A0102EB883C7F**. Managed app SHA is **2A82CDF7561FA82A9ECF32542267AC4F579B8D61DAA58F20BC370A9E15E6CE85**; modern full harness SHA is **04A560353E1F0C2B53A1ED1A4E1128AB3CB4F784722CF5B5C77519DA76CFF4AC**; final Framework test executable SHA is **8EF2FA2350122B58CFAC84396CF1EA2959C6179C93A561D167B5C82BF7791B4A**. `cold-share-final-hashes-20261007.json` retains paths/hashes. The owned new test source matches the isolated snapshot, SHA **12113380C3A932B8B368912E9C4F57DA090976B1AE4876D7EAB725BAB37B523E**, in `cold-share-owned-source-hashes-20261007.json`. Both owned build roots have zero live app/runners after terminal. All manifests/logs above are under `C:/Users/rsmit/.codex/EasyEjectWindowsPreview`.

No real camera/user media, physical eject/cleanup, actual media tools/LUT, foreground input, normal settings/startup/security/power/display change, installation, signing/trust-store change or publication occurs in these new cases. The existing full-suite nonce-profile roundtrip remains separately scoped. Full modern foreground/restart/tray/notifications, genuine camera/official positive LUT, identified disposable hardware/filesystems/travel and complete trusted direct-distribution acceptance remain open. The full goal stays active and the expired overnight schedule stays paused.
