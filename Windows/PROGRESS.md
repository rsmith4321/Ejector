# Windows progress · October 6, 2026

Workspace: `C:\Users\rsmit\OneDrive\Documents\EasyEject`.
Branch: `windows/native-tray-milestone`, based on `384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c`.
Remote: `https://github.com/rsmith4321/Ejector.git`. No push, release, deployment, or Store submission.

This is a locally compiled source milestone, **not runtime or device compatibility proof**. Source milestone commit: `2297165c8fd4731d1c7f47079027f9cec7932302`. The subsequent documentation commit adds this report; the current checkout revision is available with `git rev-parse HEAD`.

## Delivered source

- Native Windows Forms tray/device UI; manual import/eject confirmations default to No. Close hides to tray; active operations prevent app exit; session mutex prevents duplicates.
- Read-only SetupAPI disk interfaces, volume GUID enumeration, volume-to-disk extents, storage descriptors, hotplug properties, PnP ancestry, and device-change generation.
- Conservative camera/card candidates. A generic SSD/NVMe device with camera folders is not certified as CFexpress. Internal drives remain protected.
- Manual `CM_Request_Device_EjectW` adapter with immediate devnode/instance revalidation and veto/error reporting. Plans include sibling partitions/LUNs and reject incomplete, spanning, changed or protected topology. No force fallback or real eject test.
- Shared source/destination disk reservations; verified folder copying with preserved relative paths, source stamps, SHA-256 rereads, exclusive handle-based publication, flushed writes, completion receipts, cancellation, and reparse/path-escape rejection. No source original deletion, cleanup, or auto-actions.
- Optional identity-bound import folder profiles. Windows first slice backs up the selected folder's regular files; Mac filters, clean layout, deduplication and other parity remain later work.
- 37 compiled test cases covering safety policy and disposable file fixtures, including same physical device, stale actions, changed topology, vetoes, path boundaries, conflicts, cancellation, source/destination tampering and junctions. **Zero cases executed on this laptop.**

## Checks and exact results

```powershell
git status --short --branch
git remote -v
git rev-parse HEAD
dotnet --info
(Get-ItemProperty 'HKLM:/SOFTWARE/Microsoft/NET Framework Setup/NDP/v4/Full' -Name Release).Release
./Windows/build.ps1
./Windows/inspect-readonly.ps1
git diff --check
```

Git works and the project folder is writable; checkout started clean. Edits are confined to `Windows/`. Mac source/project and website artifacts were not modified. No repository AGENTS.md or applicable ancestor AGENTS.md was found.

`dotnet --info`: installed .NET runtimes 8.0.21 and 10.0.0, Windows Desktop runtime 10.0.0, **no .NET SDK**. Installed x64 Framework compiler version `4.8.9221.0`; Framework Release registry value `533509`. The implementation uses Framework 4.8 APIs with that installed compiler, without installing a toolchain.

`./Windows/build.ps1`: exit 0; app and test executable compiled using `/warn:4 /warnaserror+ /platform:x64`. Outputs and runtime configuration are under `C:\Users\rsmit\AppData\Local\EasyEject\PrototypeBuild` as reported by the shell, outside OneDrive. Codex's packaged host can redirect this logical local-app-data path into its package's LocalCache; build script reports its effective path and no binary is checked into Git.

`./Windows/inspect-readonly.ps1`: exit 0, `ReadOnly=true`, **1 physical disk, 5 volumes, 5 partitions, 5 disk/partition associations, 1 logical-disk association**. This used Windows CIM providers. It did not execute the prototype's P/Invoke inventory code or call an eject API, and it did not touch card/SSD/camera media.

Network/GitHub fetch and official Microsoft documentation reads succeeded. The API references and Windows/Mac behavior differences are in [README.md](README.md).

Whitespace validation passed. Compilation was repeated only following source changes; the final build was clean. Test binaries exist, but that is not a passing test result. Git initially lacked an author identity; the local source commit used the identity consistently present in the repository's five most recent commits, through per-command `git -c` options. Global/repository identity settings were not changed.

## Runtime blocker: Windows Application Control

A tiny disposable program under the external build directory compiled successfully with the installed compiler. Its ordinary executable launch was rejected with:

> An Application Control policy has blocked this file.

Read-only `Microsoft-Windows-CodeIntegrity/Operational` logs contained event **3077** for the compiled `CompilerProbe.exe`, event **3033** describing the signing-level failure, and event **3118** labeled Smart App Control block details. Policy ID in the event: `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`. Registry `HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState` is **1** (enabled). Windows reports Windows 11 Home build **26300**.

`CiTool.exe -lp -json` returned `OperationResult=-2147024891` (access denied); full policy provenance/possible additional organization policy could not be enumerated at the current privilege level. No elevation was requested. No allowlist, App Control, signing credentials, trust roots, or OS permissions were changed. No alternate assembly loader/interpreter was used to execute the blocked code. App and test launch are deferred rather than treated as successful.

Microsoft's [supported signing guidance](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control) calls for trusted-provider code signing, with Artifact Signing as a supported route. An authorized development-signing workflow or a separately approved test environment is required to run these binaries. No available trusted signing workflow was established here; no production credentials were inspected or used.

## Overnight readiness and OLED power settings

Ryan's latest clarification is simply to avoid a static image damaging the OLED, with actual panel power-off preferred when available. The one-minute AC display-off setting is retained. No further display/lock investigation is needed for this source milestone; the following records the checks already performed.

This Windows chat continued shell source/build work locally after the one-minute display timeout was configured; it does not depend on the Mac checkout being open. This proves current Windows-side activity, not guaranteed unattended scheduling after the chat stops or survival of power/network/app interruption. Foreground UI automation was deferred.

Windows' signed `System.Windows.Forms.SystemInformation.PowerStatus` reports **Online** (AC power). Active plan is **Turbo**, GUID `6fecc5ae-f350-48a5-b669-b472cb895ccf`.

| Setting | Original AC | Applied AC | Battery/DC |
| --- | --- | --- | --- |
| Actual display-off timeout (`VIDEOIDLE`) | 300 seconds | **60 seconds** (`0x3c`) | 180 seconds, unchanged |
| Idle sleep timeout (`STANDBYIDLE`) | 300 seconds | **0 / never** | 180 seconds, unchanged |
| Hibernate timeout (`HIBERNATEIDLE`) | 0 / never | unchanged | unchanged |
| Lid close (`LIDACTION`) | 1 / sleep | unchanged | 1 / sleep, unchanged |
| Sign-in on wake (`CONSOLELOCK`) | 1 / yes | unchanged | 1 / yes, unchanged |

Exact mutations, read back successfully:

```powershell
powercfg /setacvalueindex SCHEME_CURRENT SUB_VIDEO VIDEOIDLE 60
powercfg /setacvalueindex SCHEME_CURRENT SUB_SLEEP STANDBYIDLE 0
powercfg /setactive SCHEME_CURRENT
powercfg /query SCHEME_CURRENT SUB_VIDEO VIDEOIDLE
powercfg /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE
powercfg /qh SCHEME_CURRENT SUB_BUTTONS LIDACTION
powercfg /qh SCHEME_CURRENT SUB_NONE CONSOLELOCK
```

Original settings were saved outside the repo at `%LOCALAPPDATA%\EasyEject\PrototypeBuild\power-before-2026-10-06.json`. To restore just the changed AC values on the same plan:

```powershell
powercfg /setacvalueindex 6fecc5ae-f350-48a5-b669-b472cb895ccf SUB_VIDEO VIDEOIDLE 300
powercfg /setacvalueindex 6fecc5ae-f350-48a5-b669-b472cb895ccf SUB_SLEEP STANDBYIDLE 300
powercfg /setactive 6fecc5ae-f350-48a5-b669-b472cb895ccf
```

Screen saver readback: `ScreenSaveActive=1`, `ScreenSaveTimeOut=300`, `ScreenSaverIsSecure=0`, `SCRNSAVE.EXE=C:\WINDOWS\system32\Ribbons.scr`, all unchanged. It has no on-resume logon requirement. The actual display timeout is independent and earlier than the saver; a black/animated saver was not substituted for display power-off.

Dynamic Lock `EnableGoodbye` was absent in HKCU Winlogon. `InactivityTimeoutSecs`, checked policy screen-saver lock values, MDM DeviceLock `MaxInactivityTimeDeviceLock`, and checked DynamicLock/HumanPresence policy values were not configured at the queried locations. User-presence prediction power setting was disabled. Read-only supported WinRT `HumanPresenceSettings.GetCurrentSettings()` reports **IsLockOnLeaveEnabled=false**, **IsWakeOnApproachEnabled=false**, **IsAdaptiveDimmingEnabled=false**. No enabled ordinary idle-lock control was found to switch off; authentication and sign-in-on-wake were preserved. These checks do not rule out every vendor or organization policy.

**Physical OLED panel-off and an unlocked interactive desktop were not observed.** `powercfg /requests` required administrator privileges, so a display power request that overrides idle-off could not be excluded. A LogonUI process existed in the current shell session; that alone is insufficient to determine the desktop's current lock state. No attempt was made to unlock the session or bypass security. Lid-open idle-unlocked behavior still needs an observed desktop check; closing the lid will sleep and require sign-in as before.

## Next safe acceptance steps

1. Establish an authorized runtime environment compatible with App Control; keep the current laptop's security policy intact. Run `./Windows/build.ps1 -Test` and record actual case results. Do not interpret static compilation as passing tests.
2. Run `EasyEject.Tests.exe --inventory` for native read-only mapping and compare with Windows Disk Management/CIM. Validate ABI layouts, notifications, descriptor failures, empty readers and hidden volumes. No removal requests in this check.
3. Inspect tray/UI at common DPI settings and observe AC idle panel power-off with lid open, session lock state, then protected lid-close sleep/sign-in. Shell checks can continue while the display is off or the desktop is locked.
4. Have Ryan explicitly identify a disposable card/reader before **any device-affecting test**. On that fixture, verify import hashes/original retention, conflicting paths, cancellation, multi-partition/LUN reservations, reconnect/format identity changes, reparse rejection, and open-file veto reporting. No real production card or SSD was qualified during this milestone.
5. Only after those checks should supported reader/card compatibility be claimed or further Mac parity/distribution work begin. Current code remains an unsigned prototype with no installer, auto-start, public release or Store proof.
