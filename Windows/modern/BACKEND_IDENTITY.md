# Actual managed backend identity · October 8, 2026

Source **eac04e25854d5544b68f1133f4d684ce80ceae57** makes composed modern application review inspect the held `EasyEject.Runtime.dll`, rather than accepting its version only from the dependency declaration. Authenticated complete inventory and a valid main application are insufficient when the backend bytes are malformed or retain development identity.

The bounded metadata reader requires an x64 PE DLL, supported managed library flags, zero CLR method entry point, neutral assembly metadata and the existing .NET10 reference contract. Actual assembly name, product, title and assembly/file/informational versions must match the authenticated stable release. The main GUI executable and classic policies remain separate. Inspection never loads or executes candidate code; held content is revalidated around the operation, preserving cancellation, ownership and refreshed trust.

## Evidence and exact scope

Tests use isolated **e45d416** plus ten owned source/test overlays. Concurrent notification source **8b77826** is excluded from this checkpoint; its separate proof is preserved. Nothing in the previously prepared 601-file `IntegratedReview-20261008/app` is modified or launched.

- Before the production fix, composed sample **5/2**, terminal exit one: authenticated malformed and development backends were incorrectly admitted. Retained log: `EasyEjectWindowsPreview/backend-before-clean-reproduction-20261008.log`.
- Framework app/test fixtures and modern app/selected/full runners compile with warnings as errors. Modern selected **236/0**, full default **1345/0**; Framework full default **1345/0**. Full suites report no real eject API used. Terminals **15683/21383 exit zero**; counts overlap and are not summed.
- Final focused checks on both runtimes: backend basic **5/0**, actual backend sample **5/0**, main managed basic **5/0**, main sample **15/0**, runtime configuration **11/0**, composed basic **3/0**, composed sample **7/0**, archive/channel/release samples **2/0 each**. Framework terminal **47654**, modern exact-backend terminal **42264**, both exit zero.
- Modern app, selected runner and default full runner share backend SHA **B899F74D86B9CFB4C38770586E126FE439B5BBC05BE062C658A7A038F053FB6C**. The separate final focused runner initially differed (**9EE9478C…**); only its owned runtime copy was replaced with the app's exact backend before the final focused run. No new bundled-CLR/module observation is claimed for these Debug tests.
- The initial modern opt-in sample **4/1** exposed an overly broad harness assertion against any newly loaded assembly. Final diagnostic identifies framework dependencies `System.Memory` and `System.Text.Encoding.Extensions`; the check now refuses any newly loaded candidate backend and verifies original bytes, matching the existing main-app test. This correction affects only the explicit sample method; default tests and production code are unchanged. Both final focused runs include it.
- All 601 original review payload lengths/hashes remain unchanged. Final owned app/test process count is zero. No real media, network release, hardware removal, normal configuration or system setting action occurred.

Positive release fixtures patch version attributes only in disposable owned copies and use fake positive trust. They do not grant publisher, installation or launch approval. The actual development sample is read-only and explicitly refused as a stable release.

## Reproduction

All directories below are under `%USERPROFILE%/.codex/EasyEjectBuildTools`, outside OneDrive. `BackendAfterSource-20261008` holds the snapshot; `backend-after-source-20261008.json` records initial overlays and `backend-after-source-final-20261008.json` records the sample-only harness correction. `backend-after-bindings-20261008.json` retains initial runner hashes.

```powershell
& ./Windows/modern/build.ps1 -Test -FullTest -BuildRoot <fresh-outside-OneDrive-directory>
& <runner.exe> --modern-windows-backend
& <runner.exe> --modern-windows-backend-sample <read-only-EasyEject.Runtime.dll>
& <runner.exe> --modern-windows-application-package-sample <read-only-app-folder>
```

Retained helpers: `verify-backend-after-20261008.ps1` (Framework compilation/fixtures), `backend-focused-20261008.ps1` (fresh log directory, all ten focused modes). Evidence: `BackendAfterFramework-20261008/full-default.log`, `backend-after-modern-20261008.log`, `BackendAfterFrameworkFinal-20261008/compile.log`, `backend-focused-build-20261008.log`, and the per-mode logs in `BackendAfterFrameworkFocusedFinal-20261008` and `BackendAfterModernFocusedExact-20261008`. Earlier path/harness failures remain retained and are not counted as product passes.

## Remaining acceptance

Complete vendor component/catalog identity, approved publisher/endpoints, clean-computer loader behavior and trusted direct distribution remain open. This development build is unlaunched; native UI/tray/lifetime/notification acceptance, official user-supplied Luna LUT, genuine camera footage and explicitly identified disposable hardware/filesystem/travel tests remain required. Full goal stays active; the expired overnight heartbeat stays paused.
