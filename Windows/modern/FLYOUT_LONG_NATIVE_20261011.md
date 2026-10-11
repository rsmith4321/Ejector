# Native long flyout and focus evidence · October 11, 2026

The isolated engineering host now accepts explicit `inspect-long`: 18 generated devices, six in each camera/emulator/external group, with long labels and opted-in fixture sharing. It uses no discovery, worker or device API. Fresh `ui-*` root and ordinary-ancestor guards are unchanged. No production behavior changes in this milestone; the 960bd75 review/ZIP remains current and publication-held.

## Native results

At this laptop's existing **150% scaling**, the flyout is 400×1021.33 logical pixels, physical `(1942,50) 600×1532`, inside the `(0,0) 2560×1600` work area. Device viewport is **862.67** logical pixels, with **1177.33** scroll extent. Footer top/bottom **972/1004** stay within the surface. The final inspection host records these values and asserts positive viewport, long-list scroll extent, footer bounds and physical work-area containment before declaring ready. This checks the current monitor; no Windows display setting was changed.

Supported computer input proves:

- Initial Open window focus → Tab focuses Settings → Shift+Tab returns to Open → Shift+Tab focuses device 18 Quick Share and scrolls it fully into view. Header/footer remain visible and labels wrap.
- The final device's generated Eject confirmation highlights **Keep Connected**. Return records `None`, displays **Keep Connected. No device action.**, and retains the last-device scroll position.
- Activating the pre-existing Downloads Explorer window while confirmation is open keeps the flyout and confirmation visible. With no confirmation, the same click-away activation hides the flyout while its process remains alive.
- In the final host, two actual wheel-scroll inputs reach device 18 while the footer remains visible. Idle Escape is received/handled and records `visible=false`; the immediate window inventory is stale and still lists the window, so it is not counted as settled inventory confirmation. The earlier probe session separately returned an empty app-window list after idle Escape, before normal Quit.

## Unresolved Explorer-return Escape delivery

In the first long session, two Escape inputs after Explorer activation/return leave the generated confirmation open. Clicking its body explicitly, then Escape, safely cancels. The separate passive probe reproduces this distinction:

- Roots are attached before input: surface Border, popup ContentDialog and overlay Rectangle.
- Explorer deactivation is logged at `17:06:05.83094Z`; returning through supported `press_key` logs `CodeActivated` at `17:06:14.0716345Z`.
- Neither of two failed Escape attempts produces PreviewEscape or Escape at those observed roots.
- After an explicit body click, `17:06:31.0626234Z` records PreviewEscape from TextBlock, followed by handled Escape and one `None` fixture result. The routed event is observed at multiple attached roots; these are not multiple input actions.
- Idle Escape subsequently arrives from Button and hides the flyout.

The helper's accessibility focus reports the same dialog TextBlock before and after the body click; it does not establish actual keyboard delivery. The evidence narrows the failure to delivery/focus before the observed routed handlers, but does not prove an application, OS or automation-helper cause. No production keyboard workaround is introduced. Human keyboard/focus acceptance and broader keyboard coverage remain open. Earlier successful guide-return checks retain their separate scope.

The passive probe exists only in `UI_TESTS`, only runs in explicit inspection, logs activation/Escape/source **type** and request/visibility state, records no text or other keys and never handles input. Final normal assembly metadata excludes `UiTestHost`, `InitializeInspectionKeyboardProbe` and `WriteInspectionLayout`, and includes `TrayFlyoutWindow`.

## Builds, sessions and evidence

All paths below are under `C:\Users\rsmit\.codex\EasyEjectBuildTools` unless stated otherwise:

| Evidence | Scope |
| --- | --- |
| `Codex020-flyout-long-20261011.log` | Retained initial fixture compile failure: arithmetic assigned to uint DevInst fields; corrected with explicit casts. Includes cascading XAML compiler warning/error; no app launched. |
| `Codex020-flyout-long-fixed-20261011.log` | Corrected engineering build: zero warnings/errors, native presenter/compact/tray editor/busy/idle regression PASS. Interactive PID30796, fixture `ui-flyout-long-native-20261011`: keyboard, final-device Return, retained scroll, confirmation retention and idle click-away. No passive trace yet. |
| `Codex020-flyout-probe-20261011.log` | Passive-probe engineering build: zero warnings/errors and regression PASS. Interactive PID14564, fixture `ui-flyout-probe-native-20261011`: instrumented focus distinction and idle Escape. |
| `Codex020-flyout-final-20261011.log` | Final engineering build: zero warnings/errors and regression PASS (`ui-bccac9e7c4e348a79f51d9b0c0c6d47c`). Interactive PID28144, fixture `ui-flyout-final-native-20261011`: explicit native geometry assertions PASS and wheel scrolling. |
| `Codex020-flyout-normal-20261011.log` | Normal build: zero warnings/errors, never launched. `Codex020-flyout-final-20261011/normal-exclusion.json` pins metadata exclusion; normal DLL SHA-256 `c9ecf73c79dd3b0916cc178b3da42f19c007f3bbb0bcb1b534213bba09db60b6`. |

All three interactive fixtures are direct children of `C:\Users\rsmit\.codex\EasyEjectFixtures`. Each finishes through the existing `continue.txt` gate and actual idle tray callback Quit; all `result.txt` files PASS and all owned processes exit. No forced termination. Inspection actions never invoke a real device API. No default-test count change; earlier 246/0 selected and 1454/0 each full baseline remain separate evidence.

Probe loaded modules: Modern DLL **522240 bytes**, SHA-256 `5b00b36be384c8e79139f12e3acef03dae63cc2c6e34ec01966580160320e2b8`; Runtime **1008128 bytes**, `6171e6a4fa55e9d37b5de15e461641c5300263e323eb2b1e64f0ce879a5ff749`. Final assertion host: Modern DLL **523264 bytes**, `69ca4cff36eca5c0d11f7ad7a04a1a45c6193d2055a744cc8f0992876ff12792`; Runtime **1008128 bytes**, `d6bddcd4824a7b818b79d29238b24bac408863c321f98e45a008c82cf25c1070`. These different builds are distinguished, not presented as identical payloads. Each `native-loaded.json` records actual module paths, including installed .NET 10.0.0 for these Debug engineering sessions; the unsigned Release review separately includes .NET 10.0.12.

Aggregate `Codex020-flyout-final-20261011/native-final.json` SHA-256 **`fcb4b55a817bbcf40e51b0d730bdab655c1856cd32bcc4c0cc54363f81387f12`** pins each fixture trace/layout/result and source hash. Probe trace SHA-256 **`f0a9a6294c0d6119831a3ca5f552e0aa657de674d04bc621b116865de4af731d`**; final trace **`88d8d22f2bee52f41dda21751efdef31ab69537f84e44b0471b39b36f334d36e`**.

All **332** pre-existing files under local EasyEject, including the two live profile files, retain exact hashes/lengths/write times and count. An initial comparison accidentally deserialized ISO timestamps as DateTime and reported a mismatch; explicit `ConvertFrom-Json -DateKind String` compares the stored representations exactly and passes. No actual file difference was found. The pre-existing Explorer window remains open; unrelated apps are untouched.

No camera/media, original removal, format, physical eject, tool/LUT installation, settings/login change, push, signing or publication. The release ZIP hash remains `7d07e88a56d5bd73ff68b2e07970d37e314b8f0eb77e1ecc71decd8230c2c997`. Physical hardware/media/LUT, OS tray-icon/notification, broad themes/DPI/monitors/travel, clean-machine/trust and explicit publishing OK remain open. Full goal remains active.
