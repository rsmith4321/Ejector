# Composed modern application package review

`ModernWindowsApplicationPackageReview` owns complete authenticated/held contents and requires native apphost, managed application, runtime configuration and dependency declarations before returning application metadata. Revalidation repeats these checks while retaining ownership. Constructor failure/cancellation releases acquired resources; disposal is terminal. No package code is loaded or executed.

This joins the previously separate application checks. Component code/catalog identity, modern ZIP/channel/replay integration, approved publisher and trusted delivery remain separate requirements. It does not enable ModernProductionChannel, replace classic policy or approve installation. Baseline Mac384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c UpdateChannel.swift was inspected; Windows retains its established explicit manual-install approach and busy/modal lifetime.

## Verification

Isolated archive **e8266792942ee9fb1df7e0721512a4dc5cae5517** plus only the new review class, new tests and test registration; foreign dirty sharing tests excluded/preserved. Run:

```powershell
& C:/Users/rsmit/.codex/EasyEjectBuildTools/verify-modern-application-package-v2-20261008.ps1
```

`ModernApplicationPackageV2-20261008` output: Framework and self-contained Release runners each pass **3/0** admission/resource groups, **5/0** explicit actual-application-copy groups and adjacent **20/0 content /11/0 runtime /5/0 managed /3/0 native host /13/0 dependency**. Counts overlap with earlier suites. Separate modern Debug app/selected **236/0**, zero warnings/errors. No full-default rerun for this composition is claimed.

Positive tests change stable-version metadata only in disposable copies of the earlier local apphost/managed app, using fake publisher/chain, ephemeral fixture CMS, synthetic runtime/dependency declarations and inert component bytes. This proves composed application policy and ownership/resource release, not component integrity, real signing or a complete release. Development managed metadata, conflicting runtime and dependency versions refuse after the prior application layers succeed. Late extra files refuse revalidation and remain intact. Cancellation preserves a valid owner's holds; explicit disposal releases them and subsequent revalidation refuses.

Terminal **58740** and durable parent **14132** exit zero, **14:35:46Z–14:37:38Z October8**. `modern-application-package-v2-result-20261008.json`, output `source-scope.json` and `final-evidence.json` retain exact evidence. Three owned source hashes match; all **601 original review files** remain unchanged; owned verifier count zero. Debug app/selected backend SHA256 **DB9CB9C668CAFF8223CF6A7217E02FEE431A8CC394835FF01F367E7110CD7FFB**; distinct Release runner **ECDE8CAAC14A2AD727873855D23579D5DBBDCD9A8EB0232600A3C9CE8A5933B3**. Release configuration includes Core/Desktop10.0.12; loaded modules were not observed. The Debug result is not an exact Release-app-byte claim.

Initial output/result under `ModernApplicationPackage-20261008` retains a fixture compiler failure: fake executable callback requires `(path, stream)` arguments. Corrected v2 uses a fresh directory without weakened assertions.

User review remains `ReviewCloseTrayVerified-20261008/app`. No new app launch, desktop input, media/device/settings/install/publication action. Foreground, identified disposable hardware/filesystems, official positive Luna LUT/genuine camera and remaining trusted delivery remain open; full goal active.
