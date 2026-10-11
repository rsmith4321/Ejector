# Native generated-video sharing and restart · October 11, 2026

The isolated 0.2 engineering host now passes manual 4K-preset creation and exact reuse across actual idle tray Quit/restart, with the generated source folder absent throughout. This is native UI and supplied-tool evidence on generated clips, not genuine Insta360 footage, a physical source eject, official LUT/color acceptance or a complete-port claim. The normal 0.2 distributable remains unchanged and unpublished pending Ryan's OK.

## Scope and preparation

Production payload is `6de85d2`; documentation baseline is `7532678`. The warning-free engineering host is `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-ui-layout-20261011\bin\EasyEject.Modern\Debug\net10.0-windows10.0.26100.0\win-x64\EasyEject.Modern.exe`. Its Debug backend SHA-256 is `6F38BE334769E31A918D23CACB1E8E5CDA04D40AEE087FC6AB27C7ED9181359F`; its UI assembly is `46F2E9E5C1A522F8100F481CCFB6956E34D82882B8A315D3929FF1A689DF387B`. The helper and normal distributable instead use the Release backend `707E55B543E5424D464D94DAF4997652061E1D205B4BBA2D8E0C5C7AB9B3BB16`. The original notes conflated these pins. Fresh pre-launch readback and retained `engineering-host-pins.json` correct that documentation error; no backend replacement was performed. Preferences/history are confined to `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-share-review-20261011`; normal device discovery is read-only until an independently chosen manual operation. Automatic import is disabled by the engineering review host.

`NativeHistoryFixture` adds a separate eight-argument `--prepare-owned-isolated-native-video-history` mode. The configuration argument is an initialized, stopped direct `ui-` child of the owned fixtures directory. The helper requires ordinary ancestry, the exact review marker and an exclusive owner lock, then selects the backend's explicit isolated app-data override. No video seed mode exists. Existing ordinary-root modes retain their previous admission rules.

Helper compilation uses the existing SDK 10.0.401/cache, `NativeHistoryFixture.csproj`, `-c Release -r win-x64 --self-contained true`, `-p:EasyEjectBuildRoot=...\Codex020-isolated-video-helper-20261011\build`, `-p:EasyEjectFixtureBackend=C:\Users\rsmit\.codex\EasyEjectBuildTools\ClaudeReview-6de85d2\review\app\EasyEject.Runtime.dll` and output `...\Codex020-isolated-video-helper-20261011\runner`. The final explicit Release-reference rebuild passes warning-free, exit 0, in `Codex020-isolated-video-helper-release-pin-20261011.log`. Helper assembly SHA-256: `2EA76250B32154CD99AB2AAA32B1347C29C78C474EC6B60F03FD10A6EE2BAAF6`; loaded backend remains the exact Release pin above. Earlier preparation and usage-rebuild logs are retained. The intermediate usage rebuild referenced the Debug backend but retained the newer existing Release backend in output; it is superseded by this explicit matching reference/output rebuild. An intervening rebuild omitted the repository's `EasyEjectBuildRoot` property and failed before compilation with missing assets; its log is retained separately.

Eight opt-in admission controls refuse: missing mode, wrong backend, outside review root, uninitialized review, active review owner, wrong tool hash, wrong marker and nested review root. All eight are rerun on the final matching Release-reference helper; every requested preparation root remains absent. `release-pin-*.log` and `release-pin-admission.json` retain exact exit 1/reasons in the helper directory. Earlier preparation, logs and `additional-admission.json` are also retained. These are opt-in checks; the default suite count remains **1454/0** per runtime, **246/0** selected modern from the earlier candidate validation.

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

## Changed-original refusal, exact restore and manual retry

An additional hash-checked engineering session, PID24640, uses the same isolated preferences and generated batch. With the app stopped and review owner lock held, an exclusive backup retained the first generated original. Only byte100 changed from 0 to 1, with its original length **169551** and last-write timestamp preserved. Its changed SHA-256 is `5DAC698B24AC9A22EBA34D1A85E146FC592D73404759E019288175C2ED57542B`. No real footage is involved.

The normal history → Quick Share → explicit Create action refuses with **“A saved original changed. Quick Share is refused.”** The UI reports **0 new / 0 reused, 0/0 published files/receipts**, and manual retry. Independent checking finds all eleven existing destination files at their expected hashes, sizes and timestamps, with the deliberately injected file still changed and **zero new files**. The application did not repair or accept the changed original.

The fixture driver holds the generated file exclusively, requires its exact injected hash and the backup's original hash, restores only the injected byte and original timestamp, then confirms the original full-file hash. The app remains on its refusal message; restoration starts no automatic action. A separate fresh native history/share/Create action succeeds with **0 new / 2 reused**. All eleven prior files are now exact again; only a separate successful-retry audit is added. This establishes released reservations and a fresh manual retry on the same session after refusal.

A further fresh sharing review tests pre-export cancellation. Return opens the focused resolution combo rather than confirming export. Escape dismisses the combo, then a second Escape cancels the dialog. The UI reports **“Quick Share cancelled. No export started; full-quality imported originals are kept.”** Independent checking confirms all twelve destination files' hashes, sizes and timestamps, with no added or removed file. This is pre-export cancellation, not Stop during encoding. PID24640 then exits through the actual idle tray Quit callback.

| Additional generated-fixture evidence | SHA-256 |
| --- | --- |
| `changed-original-injection.json` | `D5BBF957E7F96EF986DEB0B01DDAC59D5199D598AABD5E60F5878C07300D4585` |
| `changed-original-refusal-files.json` | `0C721C425EAA7EC47AC6BF6B6F6DF7B98F6A57DAB53A4E9A40D5BDDCFFC493EA` |
| `changed-original-retry-files.json` | `9072FAA5B3EB350AE72A2EDCA18F186ADC7304CB8335DDB3C5F4D051D9E7739E` |
| `cancel-files-unchanged.json` | `2BA122374003319A27454704C11175B9EB8DAE1DBD83203D3A40605BAD001989` |

`changed-original-backup.bin`, `changed-original-restored.json` and all earlier receipts remain retained. No device action, real-media modification, new tool, live configuration write or production payload change occurs. Subsequent [generated UHD/audio checks](UHD_CANCEL_RETRY_20261011.md) cover native encoding Stop/partial-result preservation/manual retry; physical destination replacement remains pending.

## Additional native settings/flyout observations

On the separate isolated `ui-device-review-20261011` host, flyout Settings opens the main settings editor. Save enables **Ctrl+Alt+Shift+E** only in isolated preferences. The actual key chord opens the grouped E:/F: removal review; Return keeps both connected. Editing the shortcut back to Off without saving, then choosing flyout Quit, refuses exit and restores the editor with the draft preserved. Saving Off unregisters the chord; pressing it no longer opens a review. Idle flyout Quit then exits PID31392. Autostart remains unregistered and no system settings change.

A transient inventory issue at `2026-10-11T15:39:27.6181740Z` reported connection changes while scanning; later fresh inventory and read-only volume identity checks succeeded. This proves retained fail-closed issue reporting, not an intentional unplug/reconnect, physical eject or OS sleep/wake test.

The engineering controls invoke real tray callbacks but do not establish actual OS notification-area left/right/double-click interaction. Real camera model, whole-camera eject/veto/reconnect, genuine footage and official LUT, physical theme/DPI, power/travel and clean-machine/trusted-distribution acceptance remain open. The full goal stays active. The prepared 0.2 ZIP is unchanged; publication still requires Ryan's explicit OK.
