# Native completion review with no removal plan · October 11, 2026

The real WinUI completion dialog now has isolated native evidence for a source with no removal plan, safe Return/default behavior, destination refusal without notifications and successful guarded folder opening after read-only notification registration. Only the explicit `UI_TESTS` inspection host changes; production behavior and the prepared Release ZIP are unchanged.

## Scope and native observations

Baseline source: `e28c071baa4efced18fecd13736c670f09582d3d`, plus this test-only host. New `inspect-completion` requires a fresh ordinary `ui-` fixture child, one generated-history batch under the owned BuildTools fixture naming/path restrictions and an absent synthetic source. Its fake disk has no removal target; `ImportCompletionOrigin.Plan == null` is asserted before calling the actual `ReviewImportCompletion`. It never starts the runtime pump, release checks or import workers. The final mode registers real storage notifications solely for the unchanged read-only destination guard. No removal plan is granted or eject API invoked.

Only `completed-imports.json` (1483 bytes, SHA-256 `a963c9d873eedc4d7e42e432b4848821130c9bc58b0e9b29d174100e878a3e19`) was copied into each ready isolated scope, using non-overwriting copy. No live config, profile or tool config was copied. The batch points to the existing two generated UHD originals and receipt in `Codex020-native-uhd-fixture-20261011/destination/2026-10-11`. The generated original source folder remains absent.

Supported computer control observed:

1. **Initial unregistered setup:** actual completion shows **Eject Now disabled**, the missing safe removable-node explanation, physical-device/all-volumes warning and **Keep Connected** highlighted/default. Actual Return closes safely and records **Device kept connected**. A new manual review's **Open Import Folder** refuses with **Saved destination is missing or has an unknown physical mapping.** No new Explorer window opens. Inspection had no registered notifications, so `WindowsDiscovery.Scan()` marks the inventory incomplete; the production guard correctly refuses. This is retained test-setup evidence, not a production defect.
2. **Final monitored setup:** register connection notifications without starting normal runtime work. The real dialog still disables Eject and defaults to Keep Connected. Actual **Open Import Folder** passes the unchanged destination guard and opens Explorer at the generated batch. Explorer breadcrumbs and both generated file names are observed. The completion loop creates a new review with Eject still disabled and Keep Connected highlighted. Close only the new generated-folder Explorer window, reactivate Easy Eject and press actual Return: **Device kept connected** is recorded and the app returns idle.
3. Both sessions finish through the existing `continue.txt` inspection gate and actual idle tray Quit callback. Both `result.txt` files PASS; PIDs **29272/31044** exit normally. The pre-existing Downloads Explorer window is preserved. One refresh used the flyout after Open window had hidden it; fresh selection recovered the main window without repeating the click. One Explorer input geometry failure recovered with fresh selection/activation/screenshot and a single successful retry. Neither is presented as an app defect.

All **17 generated destination files** and **2 live profile records** retain exact hashes, lengths, last-write times and counts against the snapshots taken before the initial session. No genuine media/import/export, format, original cleanup, physical eject, settings/login change, tool/LUT installation, push, signing or publication.

## Builds and retained evidence

All build paths below are under `C:\Users\rsmit\.codex\EasyEjectBuildTools`; isolated scopes are direct children of `C:\Users\rsmit\.codex\EasyEjectFixtures`.

| Evidence | Scope |
| --- | --- |
| `Codex020-completion-native-20261011.log` | Initial engineering build: zero warnings/errors, native presenter/compact/tray editor/busy/idle regression PASS; automated fixture `ui-1d517aa9510c417c95cd7d07919f7a35`. Interactive `ui-completion-native-20261011`: unregistered safe refusal and Return. |
| `Codex020-completion-monitored-20261011.log` | Final engineering build: zero warnings/errors, same native regression PASS; automated fixture `ui-cb4f51becd274a14a3ce4eeffc2d171f`. Interactive `ui-completion-monitored-native-20261011`: real guarded Explorer launch, completion return and safe Return. |
| `Codex020-completion-normal-20261011.log` | Normal build: zero warnings/errors, never launched. Metadata excludes `UiTestHost`, `InitializeCompletionInspection` and `InitializeInspectionKeyboardProbe`, while retaining `TrayFlyoutWindow`. Normal DLL SHA-256 `3ceb6137491908eb60b3a0945d3c35811fc03f4840d6a792d9a0189e27ff1d16`. |

Build commands: `Windows/modern/build-ui-tests.ps1 -BuildRoot <fresh-outside-OneDrive-root>` for each engineering root; pinned SDK `dotnet-10.0.401/dotnet.exe build Windows/modern/app/EasyEject.Modern.csproj -p:EasyEjectBuildRoot=<normal-root> --verbosity minimal` for normal exclusion. Explicit interactive launches use the engineering executable with `<fresh-ui-fixture-root> inspect-completion`. Existing SDK/cache only; nothing installed.

Final evidence `Codex020-completion-monitored-20261011/native-final.json` SHA-256 **`8f12c7e2a2b2e2819336c1e7637c9fcbf167201c082dd96d071105d03370c1bc`** pins process/module snapshots, monitoring marker, completion results, passive traces, normal exclusion and copied history hashes. Loaded Modern DLL: **526848 bytes**, SHA-256 `7b845d98640e443325e491012fb5ef42a42bfa1f47aa0467eda4b98b4429b937`; Runtime: **1008128 bytes**, `dae8a939643c7de0a473b7d030b63e5f032a36ea98c4b2998b27f3b50d2a435a`. Engineering uses installed .NET **10.0.0**; these are separate from the packaged Release payload/.NET10.0.12.

No new default-suite count or full-suite rerun is claimed. Earlier selected **246/0** and full shared-engine **1454/0 each** keep their original scope. Current production ZIP remains SHA-256 `7d07e88a56d5bd73ff68b2e07970d37e314b8f0eb77e1ecc71decd8230c2c997` from exact source `960bd75`; publishing still requires Ryan's OK.

This proves the **no-removal-plan** completion path. It does not prove expiration of a previously valid physical removal plan, actual completion eject/cleanup/veto, notifications delivery or genuine camera/LUT acceptance. Those and broad display/travel/clean-machine/trust gates remain open. The full Windows goal stays active.
