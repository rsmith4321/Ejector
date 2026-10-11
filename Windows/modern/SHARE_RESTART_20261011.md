# Native generated-video sharing and restart · October 11, 2026

The isolated 0.2 engineering host now passes manual 4K-preset creation and exact reuse across actual idle tray Quit/restart, with the generated source folder absent throughout. This is native UI and supplied-tool evidence on generated clips, not genuine Insta360 footage, a physical source eject, official LUT/color acceptance or a complete-port claim. The normal 0.2 distributable remains unchanged and unpublished pending Ryan's OK.

## Scope and preparation

Production payload is `6de85d2`; documentation baseline is `7532678`. The warning-free engineering host is `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-ui-layout-20261011\bin\EasyEject.Modern\Debug\net10.0-windows10.0.26100.0\win-x64\EasyEject.Modern.exe`. Its backend SHA-256 is `707E55B543E5424D464D94DAF4997652061E1D205B4BBA2D8E0C5C7AB9B3BB16`. Preferences/history are confined to `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-share-review-20261011`; normal device discovery is read-only until an independently chosen manual operation. Automatic import is disabled by the engineering review host.

`NativeHistoryFixture` adds a separate eight-argument `--prepare-owned-isolated-native-video-history` mode. The configuration argument is an initialized, stopped direct `ui-` child of the owned fixtures directory. The helper requires ordinary ancestry, the exact review marker and an exclusive owner lock, then selects the backend's explicit isolated app-data override. No video seed mode exists. Existing ordinary-root modes retain their previous admission rules.

Helper compilation uses the existing SDK 10.0.401/cache, `NativeHistoryFixture.csproj`, `-c Release -r win-x64 --self-contained true`, `-p:EasyEjectBuildRoot=...\Codex020-isolated-video-helper-20261011\build`, the exact backend above and output `...\Codex020-isolated-video-helper-20261011\runner`. Logs: `Codex020-isolated-video-helper-20261011.log` and the final usage-text rebuild `Codex020-isolated-video-helper-usage-v2-20261011.log`; warnings as errors, exit 0. An intervening rebuild omitted the repository's `EasyEjectBuildRoot` property and failed before compilation with missing assets; its log is retained separately. Final helper assembly SHA-256: `166CE9A536C8655B075B2484E4DDDEAF5E071805149BACEF4DE7C2322D658255`.

Eight opt-in admission controls refuse: missing mode, wrong backend, outside review root, uninitialized review, active review owner, wrong tool hash, wrong marker and nested review root. The first six preceded preparation; the last two pass again after the final rebuild and explicitly leave their requested preparation roots absent. Logs and `additional-admission.json` are in the helper directory. These are opt-in checks; the default suite count remains **1454/0** per runtime, **246/0** selected modern from the earlier candidate validation.

Generated fixture: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-native-share-fixture-20261011`. `preparation-arguments.json` records the exact command arguments. Existing user-supplied executables are under `C:\Users\rsmit\Documents\Gyroflow-DJI-LUT\ext\ffmpeg-n9.0.2-22-g46d8f462ee-win64-gpl-shared-9.0\bin`:

- FFmpeg SHA-256 `6C03ED874BF9C219D3BA865339B48A106EEF11FAC07E8E53BB599AC59214469D`.
- FFprobe SHA-256 `FB41BE6F4386DF4625D13EEC932EC926BE4744853759C7C100947EDC7B917096`.

The helper creates two playable one-second 640×360 silent 30fps H.264 clips, with synthetic Luna selection metadata, production import receipt and native read-only C: destination identity. It moves its source to `retained-source`, verifies only the two exact recorded originals, refuses changed destination/original and pre-cancellation, and retries successfully. `Seeded=false`, `IsolatedReview=true`. The prepared records were subsequently copied exclusively to the stopped isolated review, with its owner lock held, initially absent targets and exact ledger hashes. `isolated-records.json` retains that separate operation. No live preferences/history, real footage or existing camera files were published or changed.

## Actual native UI observations

1. PID14680 starts with retained generated history and absent source. Verify saved originals reports **all 2 exact saved files, originals unchanged**. Quick Share is hidden while the profile is opted out.
2. Quick Share tools selects the two existing executables through native file pickers. Save reports **setup saved, no export started**. Both LUT selections remain empty. No download or installation occurs.
3. Saved profiles edits the disconnected generated device through the normal offline editor. Manual Quick Share is explicitly enabled, preferred size **4K**, color **Standard / LUT off**. Save starts no import/export. Automatic import, post-import eject prompt, original removal and trash recovery remain off.
4. Import history selects the exact two-file batch. Quick Share now appears. Its separate confirmation shows **4K**, LUT unchecked, and Cancel as the safe default. The explicit Create sharing copies action reports **2 new; 0 reused**, preserves full-quality originals and ejects no device.
5. Close the result, then invoke the actual tray-menu Quit callback through the engineering host's Test tray Quit action. PID14680 exits. Launch the same host and isolated root normally in review-restart mode: PID20696. The batch, tools and manual profile choices persist, while the source remains absent.
6. A new explicit history → Quick Share → Create action reports **0 new; 2 reused**. All ten pre-existing destination files retain exact SHA-256, bytes and last-write timestamps, including originals, output files, both output receipts, import receipt and first audit. Only a separate reuse audit is added. Idle tray Quit exits PID20696 normally.

The 4K preset is a maximum size: these 640×360 fixtures remain 640×360 rather than being enlarged. Both outputs are independently probed as H.264 640×360 and fully decoded with the pinned existing tools. They are each **423670 bytes**, SHA-256 `39441D7AFD453C6F31AC9DC3D6C6E41748EC7F89C9CF604015039794EFB5BCA4`. Each original remains **169551 bytes**, SHA-256 `E8DCD8BB1916DB8933332A811C98931973A4D3293CA49BD563A0DD9D449AA03B`. This does not prove genuine 3840×2160 camera output or audio/color fidelity.

The first independent timestamp comparison used PowerShell's default JSON date conversion and falsely reported mismatches. Re-reading with `ConvertFrom-Json -DateKind String` preserves exact round-trip timestamp precision and confirms all ten unchanged timestamps, hashes and sizes. The corrected evidence explicitly records the comparison error.

| Evidence in generated fixture | SHA-256 |
| --- | --- |
| `fixture-receipt.json` | `55349562F1973521EF6E29D3B454E118B4671A589A9FA546C1FDB77A70DE131D` |
| `native-reuse-after-restart.json` | `E9C878ABC74C00A0E130736CF111E0ECD54BCFC54DFB01A152A59807E93806AC` |
| `independent-output-validation.json` | `9A5C89D01F71783BBD85FB3116108F3E128EE9F6D8E97BEBA4C77F5EC5D5FA30` |

`native-export-before-restart.json` retains all ten initial file identities. Export audits record `Uhd | Standard`, source/output hashes, exclusive publication and the separate `new 2/reused 0` and `new 0/reused 2` results.

## Additional native settings/flyout observations

On the separate isolated `ui-device-review-20261011` host, flyout Settings opens the main settings editor. Save enables **Ctrl+Alt+Shift+E** only in isolated preferences. The actual key chord opens the grouped E:/F: removal review; Return keeps both connected. Editing the shortcut back to Off without saving, then choosing flyout Quit, refuses exit and restores the editor with the draft preserved. Saving Off unregisters the chord; pressing it no longer opens a review. Idle flyout Quit then exits PID31392. Autostart remains unregistered and no system settings change.

A transient inventory issue at `2026-10-11T15:39:27.6181740Z` reported connection changes while scanning; later fresh inventory and read-only volume identity checks succeeded. This proves retained fail-closed issue reporting, not an intentional unplug/reconnect, physical eject or OS sleep/wake test.

The engineering controls invoke real tray callbacks but do not establish actual OS notification-area left/right/double-click interaction. Real camera model, whole-camera eject/veto/reconnect, genuine footage and official LUT, physical theme/DPI, power/travel and clean-machine/trusted-distribution acceptance remain open. The full goal stays active. The prepared 0.2 ZIP is unchanged; publication still requires Ryan's explicit OK.
