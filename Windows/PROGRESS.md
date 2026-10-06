# Windows progress · October 6, 2026

## Full-port scope and latest software milestone

### Guarded optional macOS metadata cleanup · goal continuation

Implementation commit: `334bf71a43dc7df158854c994266c4fcc62776ef`.

Read `MetadataCleaner.swift`, its cleanup preservation fixture, `DriveManager.eject`/bulk flow, `AppLifecycle.swift` and the cleanup acknowledgment in `SettingsView.swift`. Windows now ports optional removal of regular `._*`, `.DS_Store` and `.apdisk` files, plus empty `__MACOSX` directories. It traverses other directories without deleting arbitrary contents and preserves Mac/Windows Trash, Spotlight/events/temporary/system folders, all reparse points, system/offline items and hard-linked metadata aliases. Photos, recordings and other filenames remain intact. File disposition uses a held exclusive DELETE/attribute handle; ancestors stay pinned, canonical paths/types/volume serials are checked, and source identity is revalidated before deletion. Empty-folder removal compares the captured file identity and relies on Windows' atomic nonempty-directory refusal if new contents arrive. No read-only flag is cleared, permission changed, or recursive folder deletion used.

**Clean & eject…** explicitly reviews cleanup of the selected volume and all storage affected by removal. **Clean macOS metadata from cards before ejecting** starts off in new/legacy settings; enabling it requires a separate No-default acknowledgment, and saving never starts cleanup. Future card, bulk/shortcut and post-import eject confirmations include cleanup roots. Other external storage keeps plain eject unless Clean & eject is explicitly chosen; its warning starts on. Windows retains grouped removal confirmation even when the extra other-storage warning is disabled. Single-device cleanup covers the selected volume; bulk covers all confirmed card-volume roots, preserving the Mac scope distinction.

All group disks are reserved before cleanup. Bulk preflights every selected root before any deletion/request, keeps continuously verified peers across its own removals, and stops on cleanup failure while retaining earlier verified removal names/counts. Cancellation, changed roots/connections/topology, inaccessible or protected metadata stop the current removal request. Completed cleanup stays applied and its count is reported; no implicit retry follows. Post-import authority is consumed once before cleanup, so a failure cannot reuse that old action. The normal eject service now rescans after preparation, and cancellation is checked again at the API boundary. Debug/issue state carries the cleanup result/failure locally.

Validation: `./Windows/build.ps1 -Test` **143 passed; zero failed**, warnings as errors; `EasyEject.Tests.exe --ui-smoke` passes read-only notification/system-protection checks with real shortcuts off; `--ui-render` and `git diff --check` pass. Sixteen new semantic fixture cases cover supported names/empty folders, arbitrary and newly arriving user contents, Mac/Windows protected trees, links/ancestor roots, hard links/system attributes, cancellation/partial counts, changed identity/root/serial, reservations and fake-eject ordering, read-only failure without bypass/retry, scan-to-request cancellation, all-root preflight, later cleanup failure/earlier successes, defaults/drafts/enable acknowledgment and one-use completion authority. An initial fixture failure exposed directory enumeration conflicting with its own exclusive DELETE handle; enumeration now occurs while pinned, followed by identity-checked empty-only disposition. Final tests exercise actual handle deletion on disposable files, with all removal APIs faked.

Minimum-size renders in `%USERPROFILE%\.codex\EasyEjectWindowsPreview\run-02b8e5c636584ab0` were inspected for settings, grouped/cleanup confirmation, completion cleanup and the window. Footers remain usable while long bodies scroll. Foreground/DPI, actual card-filesystem permissions, cleanup/removal/cancellation on an explicitly identified disposable device, native notifications/hotkeys and signing remain unverified. No real media/device cleanup, import or eject occurred; no security/power setting, startup registration, Mac target, website, push or publication changed.

The full-port goal remains active. Remaining independent work includes shared original-removal/Trash-recovery options and confirmations, offline profiles/automatic import/startup semantics, debug controls, and the complete manual Quick Share pipeline/UI. Physical proof and trusted signing still gate completion/release.

### Card groups, connection-aware bulk removal and optional shortcut · goal continuation

Implementation commit: `2571e98b508168f4d3fd20b8dc25010f9965e1ed`.

Read the baseline Mac classifier in `EjectorApp.swift`, `BulkEjectPolicy.swift`/its nine cases, `GlobalHotkeyManager.swift`, and grouped-unmount tests before implementation. Windows now separates camera candidates, emulator candidates, other external volumes, internal storage and protected Windows volumes. All eleven camera root names count case-insensitively from regular directories. Emulator classification needs two distinct supported root folders plus SD hardware evidence and takes precedence over camera evidence. Links/files do not supply evidence; fixed/system storage is never promoted. CFexpress/XQD remain explicit hardware candidates; generic NVMe plus DCIM does not prove CFexpress. Model/physical disk and mount information are shown, and card counts deduplicate removal devices across partitions/LUNs.

**Eject cards…** in the window/tray freezes all card groups in a scrollable confirmation; **Keep connected** is the Enter/Escape default. Mixed/unclassified siblings, hidden LUNs without classified volumes, protected/unknown/spanning mappings and unavailable connection proof refuse before removal. All confirmed disks are reserved together before any API. Requests run sequentially, revalidate all remaining groups, and verify each removed group's disks/volumes absent before continuing. A veto, changed peer or unverified disappearance stops later requests. The result preserves earlier verified-safe names; notifications lead with stopped/complete status, and failures persist in View issue. Newly connected cards are never added to an existing confirmation.

Read-only `CM_Register_Notification` all-device-instance events track target/disk/recorded-ancestor versions. Global epochs still guard ordinary import/eject/completion; bulk alone tolerates its own earlier device removal with continuously registered, unchanged proof for each remaining confirmed group immediately before native removal. Identical reconnects/restarts and shared-ancestor changes invalidate proof. Malformed events/registration gaps fault it until fresh registration. Callbacks only update counters; failed unregistration retains callback lifetime and suppresses actions.

The optional **Ctrl + Alt + Shift + D/E/F/G/K/M/R/T/X** shortcut starts off, including legacy settings, defaults to E, and opens the same fresh confirmation. Windows-key combinations are OS-reserved, hence different modifiers from Mac. Only the selected key is registered with `MOD_NOREPEAT`, releasing the previous registration first. Collisions/unsupported choices/release failures are explained without fallback keys. Busy/modal/scanning work and queued stale messages suppress activation. Saving settings installs no startup behavior.

Validation commands:

```powershell
./Windows/build.ps1 -Test
& "$env:LOCALAPPDATA/EasyEject/PrototypeBuild/EasyEject.Tests.exe" --ui-smoke
& "$env:LOCALAPPDATA/EasyEject/PrototypeBuild/EasyEject.Tests.exe" --ui-render
git diff --check
```

**127 passed; zero failed**, warnings as errors. Twenty-seven new tests cover folder/hardware evidence, partition/LUN grouping/counts, mixed/hidden/protected/unknown refusal, atomic reservations, own-removal events, absence verification/transient scans, veto/partial outcomes, identical reconnects, changed peer serial/classification/topology, unconfirmed new cards, live request guards, proof faults, immutable confirmation/default cancellation, shared ancestors/event decoding, shortcut defaults/reconfiguration/conflicts/release failure and settings migration/drafts. Removal/hotkey adapters are fakes; default tests register no real key and eject no device. Hidden native smoke registers disk/instance notifications, maps the same one internal disk/five volumes, blocks system eject, and confirms no shortcut registered. Test windows explicitly suppress production shortcut configuration even if saved settings later enable it.

Minimum-size hidden renders inspected: `%USERPROFILE%\.codex\EasyEjectWindowsPreview\run-2ce4788ce55a44ba` (grouped window 800×500, confirmation/partial result 600×400, settings 600×360). Common-controls styles match the app; scrollable bodies preserve fixed footers. Hidden `DrawToBitmap` does not reliably paint native dropdown text; E/R selection/persistence/enabled states are fixture-verified, not foreground proof. Actual notification event delivery, native hotkey delivery/collisions, DPI/keyboard/tray interaction and disposable-hardware success/veto/reconnect remain unverified. No hardware-affecting action, security/power change, startup installation, push or publication occurred.

The full-port goal remains active. Next: remaining shared/app options and offline profiles; safe optional metadata cleanup, verified original removal/device Trash recovery; complete opt-in manual Quick Share. Physical proof and trusted signing still gate completion/release.

### Connection-bound completion, notifications and issues · goal continuation

Implementation commit: `9191d525463248a761b3b82f216c07842488e62d`.

Profiles/shared defaults now expose **Ask to eject after import**, off initially and in older custom profiles. The choice freezes with media options for each operation. A completed scan can offer **Keep Connected** (Enter/Escape default), **Eject Now / Eject Device**, and **Open Import Folder**. No matching media explicitly warns that photos/skipped previews may remain. The grouped explanation names every affected partition/LUN. Import reservations are released before the choice. Completion authority captures the original source root, volume/disk identities, connection epoch and removal topology before copying; it stays in memory and is never restored from history/notifications. A new import invalidates its source action, device events invalidate all stale choices, and the removal request consumes its token once after reservation/revalidation. Busy disks retain the choice; vetoes do not retry.

**Completed imports…** in the window/tray preserves background choices when Windows notifications are suppressed. Notifications start on and debug logging off. Native NotifyIcon balloons reveal the app for review; clicking never requests removal or supplies device authority. When notifications are disabled, an opted-in post-import choice still uses the native dialog. Settings save only app preferences. Debug logging is local, explicitly enabled, flushed and capped at 1 MiB without deleting earlier diagnostics. **View issue…** persists the explanation and acknowledgment; Dismiss never retries, and even an identical later failure restores attention. Close hides the device window during a copy; quitting remains blocked while operations/dialogs are active. No startup registration is installed.

Validation: `./Windows/build.ps1 -Test` **100 passed; zero failed**, warnings as errors. Twenty new tests cover completion identity/root/topology/epoch, new-import/restart invalidation, immutable capture, one-use explicit eject, busy and veto behavior, grouped sibling reservations, safe destination opening, media/no-match dialog defaults, notification routing, prompting defaults/custom/frozen migration, app/issue persistence, debug size refusal, draft settings and close-during-work behavior. Removal/folder-opening callbacks are fakes; no real device eject or Explorer launch is used by default tests. `EasyEject.Tests.exe --ui-smoke` passes with read-only native inventory. `--ui-render` outputs in `%USERPROFILE%\.codex\EasyEjectWindowsPreview\run-28e9d96721a84e65` were inspected for completion/expired/no-media, pending imports, settings, issue and device windows at minimum sizes. Foreground interaction/DPI, actual notification delivery/suppression, Explorer integration and hardware removal remain unverified.

The full-port goal remains active. Next: classifier/card grouping and optional global shortcut, remaining settings/shared options and offline profiles, safe optional metadata cleanup/original removal/Trash recovery, and full manual Quick Share. Physical disposable-device validation and trusted signing remain required. No real-device action, security/power change, startup installation, push, publication or paid service occurred.

### Persistent exact batch history · goal continuation

Implementation commit: `01933cebfb27db2276a6bedad81105ba93c3825d`. Prior layout/reuse/audit milestone: `5f03d41`.

Completed media imports now save the newest 20 batches to `completed-imports.json` in the app's configuration folder. Each record includes the device/profile, completion date, exact verified saved paths/bytes/SHA-256, exact immutable receipt and its hash, destination root, volume GUID/filesystem serial and disk PnP/serial identity. Retention trims only history entries; it never deletes copies or receipts. Metadata-only/failed/cancelled imports do not enter history. A history persistence failure is reported separately while the verified import and its receipt remain available.

**Import history…** lists these batches even when the source is absent. Its explicit **Verify saved originals…** action obtains a fresh destination connection guard and shared disk reservations, pins paths, verifies receipt membership and hashes only the exact recorded files. It never scans the shared date folder for candidates or restores eject authority. Changed destination, receipt, saved original, fabricated membership or path redirection fails safely. Disk numbers/devnodes are not persisted as destination identity; new connections receive new guards. Quick Share itself is still pending.

Validation commands: `./Windows/build.ps1 -Test`, then `EasyEject.Tests.exe --ui-smoke` and `EasyEject.Tests.exe --ui-render` from `%LOCALAPPDATA%\EasyEject\PrototypeBuild`. **80 passed; zero failed**, warnings as errors. Twelve added tests cover source absence/exact membership, newest-20 retention, destination substitution and transient renumbering, receipt/copy tampering, index-only/cancelled exclusion, corrupt/oversized/duplicate/escaping history, snapshot isolation, redirected paths, cancellation/disconnect/reservation release and native history selection. A receipt changed after engine completion is refused before history capture. Hidden read-only smoke passes. Minimum-size selected/empty history and device-window renderings were inspected under `%USERPROFILE%\.codex\EasyEjectWindowsPreview\run-63d804c272f646f8`; source/destination profile TextBox painting remains a hidden-renderer limitation, and foreground/DPI acceptance remains pending.

The full-port goal remains active. Next: completion actions/notifications/settings/hotkey, remaining shared options and offline profiles, safe optional cleanup/removal and full manual Quick Share. Physical disposable-device and trusted-signing evidence are still required. No real-device import/removal/cleanup, security/power changes, startup registration, push or publishing occurred. Builds and fixtures stay outside OneDrive.

### Date layouts, verified reuse and durable audit · goal continuation

Imports now share a `YYYY-MM-DD` destination folder. Clean layout is a third frozen profile/default choice, enabled for new/default profiles; earlier custom profiles retain their original tree behavior. Ordinary media groups flatten with filenames unchanged. Colliding filenames or recording associations move the whole group into a stable `Additional media/<fingerprint>` folder. Structured packages and unfamiliar files retain their selected folder/tree under `Camera originals/<fingerprint>`. Device indexes remain separate. Unicode/case aliases are rejected before destination creation.

Matching saved files are SHA-256 verified against the frozen source plan and reused, with a writable held handle and successful flush required. New copies preserve camera modification dates and require caller-available space greater than file size plus 1 GiB. Every import has an exclusive `.easy-eject-receipts/<id>` record containing a flushed `import-audit.log` and its own `verified-import.json`; previous receipts are never overwritten. Failed/cancelled runs retain their audit and published copies, keep originals, and write no completion receipt. These flush checks do not establish physical power-loss durability.

`./Windows/build.ps1 -Test`: **68 passed; zero failed**, warnings as errors. Thirteen added cases cover matching reuse, lens-pair and cross-camera conflicts, preserved unknown trees, proxy/master paths, late copy/audit changes, capacity refusal, restored-timestamp source tampering, Unicode aliases, reuse without another space reservation, and read-only reuse refusal. Existing fixtures now assert exact receipt membership inside shared date folders. `--ui-smoke` passes without device operations; `--ui-render` minimum-size profile/default PNGs were inspected in `%USERPROFILE%\.codex\EasyEjectWindowsPreview\run-cdec8e9f70564a52`. Scrollable bodies and fixed footer controls remain usable in the renderer. Native hidden TextBox painting and foreground/DPI acceptance remain limitations.

Persistent last-20 batch history, completion actions, the other shared options, settings/notifications/hotkey, optional cleanup/removal and full manual Quick Share remain pending. The full-port goal is active. Hardware and trusted-signing proof remain required. No real-device import/removal/cleanup, security/power change, push or publication occurred. Earlier checkpoint counts below are historical.

### Selection/default/profile UI · overnight continuation

The production window now exposes **Import defaults…** and **Import profile…**, and imports use the saved profile's frozen media/preview selection. Native scrollable dialogs keep Save/Cancel in a fixed footer; profiles show Use defaults / Use custom settings and disable inherited controls. New profiles initially use Videos only with previews included. Shared edits apply on the next import; older prototype profiles remain custom all-media/include-previews until explicitly switched. Profile setup/edit validates source/destination paths, physical identities and the original selection connection before saving, and never starts import. Forget removes only its app-data profile after a separate confirmation; defaults and media remain intact. Import confirmation still defaults to No. Invalid/unreadable saved choices give an explanation and allow setup again.

`./Windows/build.ps1 -Test`: **55 passed; zero failed**, warnings as errors. Six added tests cover inherited/custom settings, immutable in-flight choices, actual older JSON missing the new fields, persistence/replacement without damaging profiles, bounded/corrupt defaults refusal, forgetting without media loss, editor draft isolation, and defaults changed during a real fixture copy. `--ui-smoke` remains the hidden read-only integration check. `--ui-render` renders fixture windows at their minimum sizes without showing a foreground window; wrapping was corrected and fixed footer buttons inspected. Hidden native TextBox controls do not paint their contents in this renderer; their nonzero bounds and populated values were checked. This does not replace foreground interaction/DPI acceptance. Preview PNGs are outside the checkout under `%USERPROFILE%\.codex\EasyEjectWindowsPreview`.

This milestone implements the media/preview portion of defaults and profiles. Clean layout/reuse, the other shared options, offline/disconnected profile management, completion actions, audit/history, notifications/settings/hotkey, optional cleanup/removal and full Quick Share remain pending. No real-device import, removal or cleanup was used. No power/security settings changed. Earlier checkpoints below are historical.

### Earlier engine checkpoint

Ryan's latest scope supersedes an Insta360-only/MVP interpretation: **port the full current project** at `384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c`, including Luna/manual Quick Share, over the roughly one-month runway. Insta360 travel testing leads the order, without removing other features. Direct download only; no Microsoft Store work. [PARITY.md](PARITY.md) inventories source/test contracts, full feature gaps, Quick Share semantics, staged milestones and supported signing/direct-download options. Camera model/reader/destination will be confirmed later. No complete-port or hardware-compatibility claim is made.

Earlier `./Windows/build.ps1 -Test` checkpoint: **49 passed, zero failed**, with warnings treated as errors. Two interrupted-transfer fixtures stop after exactly 1,048,576 bytes were written: cancellation and simulated disconnection retain byte-identical originals, remove private partials, publish no completed media/receipt and release disk reservations so a retry succeeds. Receipt finalization now marks completion only after the final flush succeeds; an I/O fault after rename is not directly injected by these tests.

`MediaSelection.cs` ports the baseline extension/association/package rules into the verified-copy engine: immutable videos/all-media and preview choices, still/movie protection, same-folder companions and master/proxy association, required Sony/P2/RED structure, device-index separation, partial/unsupported package refusal and selected-media versus metadata-only results. Seven new fixtures exercise these paths through the real file-copy engine. Source manifests are reselected with the same frozen options at completion. The production window still uses all-media/include-previews; videos/preview controls, shared defaults, profile editor, clean layout and verified reuse are pending next milestones. Completed device index backups are stored under `.easy-eject-metadata`; no originals are removed. This is partial engine parity, not a complete import port.

Build outputs remain outside OneDrive. No real card import, cleanup/deletion, eject, public download, security/power change or startup installation was performed. The earlier runtime milestone below records the preceding 40-test checkpoint.

## Earlier runtime milestone

Ryan reported execution ready after changing Smart App Control himself; readback `VerifiedAndReputablePolicyState=0`. Normal builds/test execution now succeed. **40 tests pass, zero fail.** Native read-only inventory maps **one internal disk and five volumes**, matching the OS-provider observations below. The hidden Windows Forms smoke check creates the window, registers disk notifications, reads complete native inventory, and confirms system-volume ejection is blocked. No real card/media import or device removal was performed. Visual UI acceptance and physical hardware compatibility remain unverified.

Executed successfully:

```powershell
./Windows/build.ps1 -Test
& "$env:LOCALAPPDATA/EasyEject/PrototypeBuild/EasyEject.Tests.exe" --inventory
& "$env:LOCALAPPDATA/EasyEject/PrototypeBuild/EasyEject.Tests.exe" --ui-smoke
```

Initial runtime failures exposed two real defects: alternate-stream syntax reached Framework path parsing before the safety rejection, and `FILE_RENAME_INFO` lacked the terminating UTF-16 NUL required by the Win32 path-normalization wrapper. Both are fixed. Fixture files now use the ordinary `%USERPROFILE%\.codex\EasyEjectFixtures` directory outside OneDrive, avoiding the packaged host's app-data virtualization. Generic import-path checks remain strict. Production profile storage resolves only its own redirected configuration folder within local app-data and is covered by a passing save/load test. Tests now assert the intended failure stage for disconnection, tampering and junction rejection, instead of accepting an unrelated early path error. Large binary files, empty sidecars, byte totals, receipt hashes, profile replacement and post-format invalidation pass.

Ryan asked for independent overnight continuation. The app heartbeat `easy-eject-overnight-windows-port` is active every 30 minutes, bounded in its instructions to October 6 at 08:00 America/New_York. It may continue safe implementation/testing in this Windows checkout and update this report. No push, real-device operation, startup installation, or additional power/security change is authorized overnight. The Windows app and laptop must remain running/awake for local continuation.

The sections below preserve the initial source milestone and readiness evidence as historical records; their original execution blocker and zero-executed-tests statements have been superseded by this runtime milestone.

## Initial source milestone and readiness record

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
