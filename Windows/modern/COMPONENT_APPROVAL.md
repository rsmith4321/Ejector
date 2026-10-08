# Independent component catalog requirement · October 8, 2026

Source **95cb4f8c845e1eadfb9eb0e42df52baadcac16f1** closes the remaining software boundary between a publisher-authenticated inventory and independently approved vendor bytes. Final modern application-folder review now requires a separate `ApprovedModernWindowsComponents` object. Missing approval refuses before opening candidate folders or invoking trust; a cancelled request still reports cancellation first. Signed ZIP inspection remains independently available and retains no extraction/install authority.

The catalog is supplied only by trusted application code, with an independent source-evidence SHA256, exact .NET/Windows App SDK/minimum-Windows identifiers and exact component names, lengths and hashes. Constructor validation freezes the supplied entries, requires canonical bounded paths, rejects aliases/duplicates/file-directory overlap/application-owned entries, and requires the runtime's mandatory components. A canonical catalog hash includes all approval identifiers and sorted entries.

Every non-application payload must match the exact approved set, including DLLs, native executables, WinMD/PRI/MUI, symbols, data and notices; checking only dependency-declared DLLs is insufficient. A fixed application-owned set remains under publisher/content/application identity checks. Application code is not authenticated by the vendor catalog. Approval is checked again against held contents on revalidation, with existing fresh publisher trust, complete hashes, cancellation and ownership retained. A release inventory cannot create approval for its own new or changed vendor bytes.

The catalog is threaded through the modern package policy, review session, backend and WinUI release entry point. Production channel **and production component approval remain null**. There is no parser or UI action that loads downloaded data into production approval. Final successful fixture messages now disclose the independent catalog check while still requiring native-loader/trusted-distribution acceptance and retaining no launch/install permission. No review worker downloads, extracts, installs, launches or switches app versions.

## Tested scope

Isolated **28a6cb9** plus eleven owned files, outside OneDrive. Subsequent foreign busy-crash work is excluded and preserved. Before-fix production remains untouched in its snapshot; only the new absent-approval sample case is inserted, reproducing **9/1**. Earlier behavior was an explicitly partial read-only review, not proof of vendor approval. Final application sample is **11/0**, including pre-access absence refusal and authenticated changed-component refusal followed by a fresh exact restoration/retry.

- App/Framework fixtures and modern selected/full runners compile with warnings as errors. Selected **242/0**; Framework/modern full defaults **1357/0 each**, reporting no real eject API used. Terminals **67953/42600 exit zero**. Counts overlap.
- Seven focused approval groups pass on both runtimes: frozen ordering/membership/hash/length/runtime identity, authenticated substitution/extra-file refusal, malformed/alias/overlarge/omitted approval rejection, application/vendor partition, refreshed trust/cancellation/disposal, pre-access absence and signed-ZIP/default-folder policy separation.
- Adjacent component-version **5/0**, dependency **13/0**, backend **5/0** and read-only258-asset version sample **1/0** pass on Framework. Final main/backend/runtime/package focused chain passes on both; application **11/0**, archive/channel/release samples **2/0 each**. Framework focused terminal **67281**, modern **20943**, both exit zero; initial final Framework terminal **82420 exit zero**.
- App, selected and full backend bytes match **2CC40D33203713CF9B4873C35B3E2CF126D55EFDE8F6773CC880A5B3B662D490**. These are Debug/installed-runtime checks, not refreshed self-contained publication or native-loader proof. No candidate backend replacement is needed.
- Original601 review payloads remain exact and unlaunched. Final owned catalog app/test process count is zero. No real media/eject/device/settings/system/installation/publishing action.

All positive catalogs are explicitly **fake fixture approval**, using generated/disposable records or stable-metadata-patched owned binaries with fake publisher/chain evidence around real CMS cryptography. No production vendor, source dossier or release is approved. A retained Framework compile failure was a test-local/lambda name collision; renaming that local corrected it without changing production policy.

## Reproduction and evidence

Evidence root `%USERPROFILE%/.codex/EasyEjectBuildTools`: before/after/final source snapshots `CatalogBeforeSource-20261008`, `CatalogAfterSource-20261008`, `CatalogFinalSource-20261008`, with JSON hashes/overlay scopes. Final helper `verify-catalog-framework-final-20261008.ps1` requires fresh output directories.

```powershell
& ./Windows/modern/build.ps1 -Test -FullTest -BuildRoot <fresh-outside-OneDrive-directory>
& <runner.exe> --modern-windows-component-approval
& <runner.exe> --modern-windows-application-package-sample <read-only-app-folder>
```

Before proof: `CatalogBeforeFramework-20261008/application-sample.log`. Final Framework evidence: `CatalogFinalFramework-20261008` compile/default/component/sample logs and `CatalogFinalFrameworkFocused-20261008`. Modern evidence: `catalog-final-modern-20261008.log`, `catalog-modern-approval-20261008.log`, `CatalogFinalModernFocused-20261008` and `catalog-final-bindings-20261008.json`. Source hashes remain bound to the exact final snapshot; foreign source/default/tool work is not folded into these counts.

Genuine independent vendor/package/catalog provenance and actual production pins remain required before configuration, followed by clean-computer/native-loader/publisher/trusted direct-distribution proof. Modern foreground/tray/lifetime/notification, supplied official positive Luna LUT/genuine camera footage and explicitly identified disposable hardware/filesystem/travel acceptance remain open. The full goal stays active; the expired heartbeat stays paused. No public-release approval or complete-port claim.
