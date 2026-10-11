# Compact storage layout correction · October 11

Native review at a requested **680 × 600 logical size**, using the laptop's existing **150% rasterization scale**, reproduced a real defect: five volumes were reported but the device list had no visible area. The unbounded wrapped action/results footer consumed its star row. A new native layout regression reproduces **device-list height=0** before the production fix.

`MainWindow.xaml` now places results/actions in an independent vertical scroller within a footer capped at 45% of the storage view's height (minimum cap 120). `StorageView_SizeChanged` updates that cap on layout changes. **Stop stays in a fixed row outside the scroller**, so scrolling utilities/results cannot hide it. Device list scrolling and operation/confirmation/identity/cancellation authority are unchanged. Other editor layouts are unchanged.

The explicit `UI_TESTS`-only `review-small` launch uses the existing fresh-root/ordinary-ancestor/exclusive-owner/isolation gates and normal read-only inventory. It requests only this owned window size, records geometry and starts no scripted hardware action. It is absent from normal builds. Initial supported border/system-menu resize input left the original window unchanged; that attempt is not claimed as native resize acceptance. The launch-size fixture supplies the real WinUI layout for subsequent supported input checks.

## Evidence

Before native host: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-small-before-20261011`; scope `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-small-before-20261011`, PID **29296**. Initial unsuccessful sizing attempt used PID **28908** in `ui-small-native-20261011`. Both quit normally.

Fixed engineering host/evidence: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-small-fixed-20261011`; scope `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-small-fixed-20261011`, PID **28876**. Loaded UI **513024 bytes**, SHA-256 `3A763A63524C2B9B2F7CAABC160CCC5522586B85ED7930C05E4BA775985498AB`; runtime **1008128 bytes**, SHA-256 `1288ACD5DD77DF401BA468E09751C94B5FAA234D3AE7CF65723858DD119E35D8`.

Fixed geometry: physical window **1020 × 900**, rasterization **1.5**, client **665.33 × 562.67**, device list **150**, footer **252.67** logical units. The captured screenshot is **668 × 594**; screenshot frame size and XAML client size are distinct.

Supported native input confirms:

- The device list shows cards and independently scrolls to the final protected C: card.
- The action/results viewport independently scrolls to About; clicking it opens the correct 0.2.0-dev dialog, and Escape closes it.
- Settings and Import defaults reach their final controls/notes while Save/Cancel remain visible; Cancel publishes no settings/defaults.
- A copy of the existing generated UHD fixture profile opens offline. Its final identity/Quick Share fields are reachable and Save/Cancel remain visible. Cancel retains the exact profile, SHA-256 `5231D25CD34F1C580792F4EC7DC3BBAF70C7B9241D6F8DD6CF3291A1CB3C8A3D`. Its source/destination refer only to existing owned fixtures; automatic import/removal/recovery are off. No import/export/profile save occurs.
- Actual tray callback Quit exits the fixed review normally. All three owned native review processes are absent. Both live configuration files retain exact hashes, lengths and write times. The isolated Profiles folder contains only the deliberately copied generated profile.

`native-final.json` SHA-256 **`732FC6DF54A8A001DCD3CE8C9ABE41F376C43AAC2005A2CF70656D79ECF27892`** retains geometry, module/exclusion checks, live comparison and outcomes. Before/fixed module receipts and geometry are in the fixed evidence folder. Screenshots remain in the supported tool transcript.

## Tests and distribution consequence

`build-ui-tests.ps1` builds with zero warnings/errors. Before fix, `Codex020-small-regression-before-20261011.log` fails **height=0**. After fix, `Codex020-small-fixed-20261011.log` passes device-list minimum, independently scrollable footer, reachable last utility, fixed visible Stop after scrolling, existing minimize/maximized retention and actual tray Quit draft/busy/editor/idle/Stop checks. The retained passing automated fixture is `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-45ef20153e6347658cd6aa77bbbbe908`.

Normal `build.ps1 -Test -BuildRoot C:/Users/rsmit/.codex/EasyEjectBuildTools/Codex020-small-normal-20261011` builds without warnings/errors and passes **246/0 selected contracts**, including theme/per-monitor and existing UI workflow contracts. Its normal assembly contains no `UiTestHost`/`InitializeSmallReview`; normal executable was not launched. No engine/classic source changes or new full-default count are claimed.

The previous prepared **6de85d2** review/ZIP omits this production correction and is superseded. Retain it as historical evidence; rebuild the review/package before any 0.2 publication. No publish approval has been received. Physical themes/monitors/DPI range, real busy-operation Stop input, camera/media/LUT/filesystem/travel, OS tray/notification and trusted distribution remain open. No OS settings, real media/device action, install, signing, push or publication occurred; full goal remains active.
