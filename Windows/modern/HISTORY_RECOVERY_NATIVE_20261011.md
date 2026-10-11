# Native saved-history recovery and receipt refusal · October 11, 2026

Post-run shutdown audit: Windows Application Error records native crashes for PIDs **30196, 7624 and 29096** after these inspection sessions. Their PASS markers/disappearance do **not** establish normal shutdown; UI/file observations below retain their scoped evidence. The later [completion focus/shutdown investigation](COMPLETION_FOCUS_20261011.md) reproduces a passive engineering logger callback after disposal, fixes both probes and verifies a fresh native session exits 0/no exception. No equivalent exception stack was captured in these older sessions, so exact causation is not claimed.

Supported computer control now verifies saved-history behavior with an unreadable individual profile, a whole-catalog failure and changed receipt, followed by a fresh manual retry. All media is the existing generated UHD/audio fixture; no genuine camera footage or physical source device is involved. Production code, engineering host, default-test count and prepared Release ZIP are unchanged.

## Isolated scope

Source baseline: `05f22b89ebbdb9a47411f718541b3e5bf777bffa`. Unchanged engineering executable: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-flyout-final-20261011\bin\EasyEject.Modern\Debug\net10.0-windows10.0.26100.0\win-x64\EasyEject.Modern.exe`.

Fresh scope `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-history-errors-native-20261011` was initialized with explicit `review`, then normally quit before copying generated records. With its exclusive owner lock held, only the generated profile and completed-history record were copied from stopped `ui-uhd-review-20261011`. No live config, tool config or real media was copied. The profile remains manual, automatic import/eject/original removal/trash recovery off.

| Record | Bytes | SHA-256 |
| --- | ---: | --- |
| Generated profile `35B50F55…6B2.json` | 947 | `5231d25cd34f1c580792f4ec7dc3bbaf70c7b9241d6f8dd6cf3291a1cb3c8a3d` |
| `completed-imports.json` | 1483 | `a963c9d873eedc4d7e42e432b4848821130c9bc58b0e9b29d174100e878a3e19` |

The batch points to the existing disposable `Codex020-native-uhd-fixture-20261011/destination/2026-10-11`, two generated MP4 originals of **78,599,567 bytes each**, and receipt ID `34c23c83791347f6847c371c5bbd7426`. All 17 destination files were hash/length/write-time snapshotted before testing. The fixture's original `source` folder is absent throughout; this does not establish physical source eject.

## Native observations

1. **Malformed individual profile:** only the copied profile JSON is intentionally truncated. History remains readable, a selected batch enables **Verify saved originals**, and Quick Share stays hidden. Actual Verify reports **Verified all 2 exact saved files. Originals unchanged.** This exercises per-record catalog error isolation, not the whole-catalog warning. All 17 destination files remain exact.
2. **Whole-catalog failure:** after normal Quit, restore the exact generated profile and create 1,000 labelled disposable files whose names match the catalog shape. The resulting 1,001 entries trigger the existing safe catalog limit. Native history shows **Sharing choices could not be read. Saved-file verification remains available. Too many saved profiles to review safely.** Selecting the batch still permits actual successful verification of both files and hides sharing. Destination stays exact. The warning is durably recorded only in the isolated issue state.
3. **Catalog recovery:** after normal Quit and exclusive owner acquisition, the 1,000 exact fixture names are retained by non-overwriting rename to `.json.fixture` in the same checked isolated directory. No deletion or real record rename occurs. On restart history no longer shows that catalog warning, and selection offers manual Quick Share again. No export is invoked and no tools are configured in this scope.
4. **Malformed receipt refusal:** change only the generated receipt's first byte, retaining its **1025 bytes** and original write time. Native Verify stops with **JSON contains an invalid value at character 0.** No success dialog appears. This establishes parse refusal, not receipt-hash validation.
5. **Valid JSON exact-receipt refusal:** replace that injection with an otherwise exact receipt whose StartedUtc year is `2025` instead of `2026` (offset949, byte54→53). JSON parses successfully; file size and original write time remain exact. A new explicit history Verify stops with **The completed receipt changed. Saved originals were not changed.** All 16 other destination files remain exact; the receipt retains exactly the injected hash; no destination files are created. The isolated issue record retains the refusal.
6. **Restoration/manual retry:** restore the receipt's exact bytes and write time. The idle app still displays the refusal; no automatic retry starts. Open a new history review, select the batch and explicitly Verify. Both exact saved files pass. Close the success result, confirm idle and Quit through the actual tray callback. All 17 destination files, including receipt, now match the initial hashes/lengths/write times and count; copied history/profile hashes also match.

Source contracts inspected: `ProfileCatalog.Catalog` retains per-record errors but rejects >1000 profiles; `ManualQuickShareService.EligibleProfile` requires one valid opted-in profile; `MainWindow.ReviewHistory` catches whole-catalog errors while retaining verification; `ImportHistory.Verify` parses its held receipt then requires exact hash/identity before hashing every original. No production fix was needed.

## Evidence and limits

Evidence root: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-history-errors-native-20261011`. `native-final.json` SHA-256 **`54d9ef52d925bfc2315b7ca1e72ac4d00004382f05fe5d75f4184b9ee5409e49`** pins setup/process/module snapshots, intermediate results, exact backups and all passive review trace hashes. Final native module snapshot `native-loaded.json` SHA-256 **`be97f61dc9f8036ac8ce800ca07d24741ec9732f3868dff0533a0ea0c6454e82`**.

Loaded Modern DLL: **523264 bytes**, SHA-256 `69ca4cff36eca5c0d11f7ad7a04a1a45c6193d2055a744cc8f0992876ff12792`; Runtime: **1008128 bytes**, `d6bddcd4824a7b818b79d29238b24bac408863c321f98e45a008c82cf25c1070`. These match the prior final engineering build and are separate from packaged Release bytes. No rebuild or suite rerun in this milestone; prior warning-free native regression and selected/full suite evidence keeps its original scope/count.

- Exact receipt SHA-256 `827cb6d23e779884f39366980eb0ac35c700ec58041c9dadab4eda5b6672c5d2`.
- Invalid JSON injection SHA-256 `dbf2e19f6377697ad49c0cc0283aa8f99ff08746d0a62a0198e1952ffb025608`.
- Valid JSON injection SHA-256 `8c7bf38704a6288ac774aed8f8bd6244ac249185749f050c7438ff86710a9b0d`.
- Original and injected write time: `2026-10-11T15:59:40.9290605Z`.
- Catalog warning snapshot SHA-256 `18b7fb4c7064776ef3ba90dc0bd1aa0ef76c52cf5d28c7aac33c6e6b89319409`; exact-receipt refusal snapshot `b8ec17f05c577df0ff432d3832f567aba9ad0a4c8ec8cef9509737e3aa7620b0`.

Initialization PID27828, malformed-profile PID30196, overflow PID7624 and receipt/refusal/retry PID29096 all exit through actual idle tray callback Quit. No forced termination; final authoritative process checks show all four absent. The overflow session's first history click returned **coordinate input geometry is unavailable**; fresh selection/activation/screenshot recovered it and the one retry succeeded. This is retained automation recovery, not an app defect. Settled observations are used because immediate accessibility trees and post-Quit window lists can lag animations/process exit.

Both live profile files retain exact hashes/lengths/write times/count. Only owned fixtures/isolated issue state are modified, with original generated profile/receipt restored exactly. The 1,000 ignored test files and private backups remain for review. Existing Downloads Explorer and unrelated apps are untouched.

No actual import, export, device removal/format, original cleanup, tool/LUT install, settings/login/security/power/display change, push, signing or publication. Completion eject/cleanup behavior, genuine camera/LUT and physical destination/hardware proof, OS tray-icon/notifications, broad themes/DPI/travel and clean-machine/trust acceptance remain open. The current 960bd75 ZIP remains unpublished pending explicit OK. Full-port goal remains active.
