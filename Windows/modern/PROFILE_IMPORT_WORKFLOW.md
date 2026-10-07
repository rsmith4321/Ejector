# Saved profile to manual import · October 7, 2026

## Baseline and source contract

Mac baseline `384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c`, `DroneImportView.swift`, offers **Import now**, Edit and Remove profile beside saved choices. Its editor saves separately. `DroneImportManager.importNow` requires the original connected volume identity and freezes resolved defaults when starting. README explicitly states that saving never starts an import. The Windows handoff retains those distinctions and adds the existing Windows manual folder/device review.

In modern Saved profiles, an explicit successful save rereads the catalog and selects that volume's current valid record. **Import now…** is a separate action; it opens the existing Cancel-default verified import review. Offline, invalid, changed or ambiguous devices/records refuse with an explanation. Catalog and editor action buttons wrap using the established layout control. No new defaults, automatic admission, export or eject are enabled by saving.

`SavedProfileImportChoice` captures the selected immutable catalog entry and one complete current inventory connection, matching volume ID, volume serial and physical device instance. Labels and saved names do not identify a source. Preparation is consumed once, including failure. It holds the configuration directory, reads the connected record, requires exact filename/SHA/native stamp equality, freezes resolved choices in `ImportOperationReview`, and rereads the exact record at handoff. Fresh source connection/epoch and source/destination guards remain mandatory; neither a replacement record with identical bytes nor a replacement destination is adopted silently. Source, destination and media-folder leases remain owned by the operation through review/worker.

The window marks this connection's automatic attempt as manually consumed, selects its matching storage card and takes work/Stop/shutdown ownership before the profile page releases its busy scope. The page performs no late busy reset after transfer, so it cannot suppress the existing completion/eject/share review or release another later profile worker. Untransferred failures remain on the profile page; later status failures retain an attention explanation without claiming that no import occurred. The same current operation path handles confirmation, import, optional acknowledged removal, durable exact history, completion and separate manual sharing/eject actions.

`ValidateForPresentation` rechecks the held selection after the worker/visibility awaits and before displaying a manual import review. Confirmation remains one-use; the engine validates again. New saved-profile selection does not change the established rule that an already displayed operation uses its frozen profile/defaults. Legacy destination binding still occurs only during confirmed non-cancelled execution against the exact reviewed record. Cancelled setup/review writes no binding, media or receipt. Failure/cancellation never automatically retries this source connection.

## Verification scope

The maintained selected suite adds 14 generated-file/fake-discovery groups: explicit manual admission despite automatic/sharing/removal preferences; absent/duplicate/formatted/replaced/system sources; invalid/stale inventory; label independence; changed/missing/malformed/identical-byte-replaced profile records; one-use failures with fresh manual retry; source reconnect/late storage events; destination replacement/shared disk refusal; late record replacement and folder ownership release; queued display revocation; immutable following-defaults/media selection through actual fixture copying; and cancelled/confirmed legacy binding. Generated originals and prior records are checked for retention. The existing import, completion, sharing, scheduler and full regression suites retain their separate scope. No default test requests real eject or selects real media/tools/LUTs.

Native WinUI profile selection/reconnection, wrapping/keyboard/default Cancel/Stop/hide/quit, completion prompts after transfer and restart remain unrun. Software cases do not constitute that foreground proof.

## Verified checkpoint · source e1546be

All builds used the maintained script and private cache, with outputs outside OneDrive and no app launch:

```powershell
./Windows/modern/build.ps1 -Test -BuildRoot C:/Users/rsmit/.codex/EasyEjectBuildTools/ModernSavedProfileFirst
./Windows/modern/build.ps1 -Test -FullTest -BuildRoot C:/Users/rsmit/.codex/EasyEjectBuildTools/ModernSavedProfileVerified
./Windows/modern/build.ps1 -Test -BuildRoot C:/Users/rsmit/.codex/EasyEjectBuildTools/ModernSavedProfileVerified
```

First terminal **53534**, exit one, builds the app warning-free but refuses test compilation: the new fixture referred to `ImportReceipt.DeleteOriginals` instead of its actual `OriginalRemovalRequested` field. The assertion was corrected to the actual persisted contract. Combined terminal **48689**, exit zero, passes **219/0** selected and **1214/0** full existing default; app/runtime/runners and unsigned Framework package/protocol fixture builds are warnings-as-errors, with zero warnings/errors. It precedes the final UI-only explicit busy-ownership transfer and late-status handling. Final terminal **66615**, exit zero, rebuilds that app warning-free and passes **219/0** selected. Counts overlap and are not additive. The 14 new groups execute on the final app's runtime; the full suite remains broader shared-source regression evidence.

Logs are `%USERPROFILE%/.codex/EasyEjectWindowsPreview/modern-saved-profile-first.log`, `modern-saved-profile-verified.log` and `modern-saved-profile-final.log`. Actual full PID **27112**, start **13:02:51 EDT**, loaded installed **.NET 10.0.0** CoreCLR and the full-output runtime; `modern-saved-profile-runtime-modules.json` captures the live modules. Terminal success and absence of this runner were observed; final process inventory found no app/runner in this output root.

`modern-saved-profile-final-hashes.json` records SHA-256 after final terminal success:

| Payload | SHA-256 |
| --- | --- |
| Runtime project / final app copy / selected runner copy | `EEC09D41EB1AEFEAE715C56A540A3149753A9676E0DC31DB85B0B8F669236599` |
| Full runner runtime copy | `FE9B9128536170AF9A34CEA333BFE4610C5D1B3D45C5CA9FE039B08B562918F7` |
| Final managed modern app | `A495DB808E3713EF9CF7F596986AB9141EEB4536361E4426D85FCB098AA40079` |

The full copy differs from final app/selected bytes; this checkpoint does **not** claim an exact final-app-byte full-suite pass. Final known normal-store direct JSON and direct Profiles JSON were empty in both conventional and Codex-redirected app-data paths, recorded in `modern-saved-profile-configuration-final.json`; this is an observation after tests, not a pre-run baseline or proof that no nonce fixture ever wrote there. Existing full-suite nonce-only profile roundtrip remains separately scoped. Historical build/snapshot folders were excluded from that store observation and preserved. New cases use fake discovery, generated originals/configuration and owned rename/replacement fixtures. No real user/camera media, real device action, supplied tools/LUT execution/installation, native input, normal settings/issue/history/log changes, login/security/power/display changes or publication occurred in this milestone. Concurrent integrated-review evidence and in-progress modern release-manifest source are separate; the latter is not included or claimed verified here.

## Remaining full-port proof

Actual profile/import/completion/tray/lifecycle/DPI acceptance, unrelated/concurrent storage UI, full modern artifact/dependency/publisher/channel policy and trusted direct distribution remain open. Official supplied Luna LUT, genuine camera footage, explicitly identified disposable hardware/filesystems and travel acceptance remain separate gates. Nothing here authorizes real source import/removal/eject, tools/LUT installation, login/security/power/display changes or publication. The full finish goal remains active and the expired overnight heartbeat remains paused.
