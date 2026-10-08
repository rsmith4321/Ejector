# Pinned vendor notice preservation · October 8, 2026

Source **dd13bd23795ea1954f3a0e7576f6b22397bf184a** makes local modern review preparation preserve existing package notices. The prior601-file development folder had no vendor license/notice text. `build-review.ps1` now calls `preserve-vendor-notices.ps1` before complete inventory capture, retaining **48** exact metadata/license/notice files totaling **1,758,946 bytes** under `ThirdPartyNotices`, plus a source-binding report outside the app payload.

The code-owned `vendor-notice-pins.json` records exact SHA256 identities for the twenty packages independently observed in [VENDOR_PROVENANCE.md](VENDOR_PROVENANCE.md), their exact notice/metadata entry names, lengths and hashes. It is packaging source, not downloaded approval data or a production runtime catalog. No production channel/component approval is configured. NuGet metadata is preserved verbatim as `PACKAGE-METADATA.txt`; extensionless `LICENSE` is preserved verbatim as `LICENSE.txt`. Other texts retain their paths/case, including `tools/NOTICE.txt`. No license text is invented, edited or fetched.

Dependency selection must exactly match the sixteen package/four download-dependency source set. The helper refuses changed selections/versions and changed complete package hashes. It holds existing package files read-only without write/delete sharing and validates every exact bounded archive entry before creating output. Copies use fresh-file creation and durability flushes, then exact SHA256 rechecks. Held packages release on success/failure. Bounds retain at most64 files/4MiB overall and1MiB per entry; graph input is bounded4MiB. Existing output/report, reports inside the app, outputs in the checkout/OneDrive/cache, and redirected/offline paths refuse. Failed preparation retains its fixture rather than overwriting or cleaning it.

The report records source graph/pin/observation hashes, selected package identities and roles, exact source entry→destination bindings and missing embedded-license text. Preserving a graph superset does not assert that every package's executable content ships. **Microsoft.Windows.SDK.BuildTools.MSIX**, **Microsoft.Windows.SDK.BuildTools** and **Microsoft.Windows.SDK.NET.Ref** lack an embedded license text in these packages. Their authentic metadata, including existing license references where supplied, remains visible. Final license/notice/redistribution acceptance is still required; these metadata files do not fill that gap. Release README/LICENSE naming, vendor catalog approval, publisher trust and native acceptance remain separate.

The provenance note also corrects its earlier unsupported notice claim: ASP.NET's only byte overlap is `System.Diagnostics.EventLog.Messages.dll`, also supplied by Windows Desktop. The old601 payloads stay unchanged.

## Verification

Isolated **2eb4b1a** plus the four owned source/doc overlays, outside OneDrive. No application/runtime behavior or default-test dispatcher changes.

- Positive exact preservation:20 packages,48 entries and1,758,946 bytes; command chunk **3675c3 exit0**.
- Six refusal controls: retained output, retained report, changed selection, changed package bytes, report inside payload and output inside package cache. Prior files stay exact; exclusive reopening of the owned failed-package fixture confirms its held handle released. Chunk **5355af exit0**, durable passed result.
- Three NTFS junction controls: payload, report parent and package cache redirects refuse before mutation; the wholly owned target remains empty. Junctions/fixtures are retained. Chunk **6f8aac exit0**, durable passed result. This is path handling with disposable local directories, not physical removable-device/filesystem acceptance.
- Fresh WinUI self-contained Release preparation passes with warnings as errors and no warning/error diagnostics, terminal **92859 exit0**. **649 files /286,097,668 bytes** include all48 notice files with matching complete inventory entries. App stays unlaunched and owned review process count is zero. Static inspection observes357 PEs/1500 imports/zero ordinary missing-local names; this remains weaker than actual loader/clean-computer proof.
- Final collection binds all649 recorded hashes/lengths, exact source overlays and prior601 hashes. Inventory SHA256 **538B4DA6479A1307AD86FC7651FAB38BDCD23E6EADEB68F4A2DD200822E92CA6**, notice report **3995CB1D4CD55C48F815DE956976C11AC60C7DD19267B15AD7EEA74F697F7D48**. No refreshed selected/default/media-tool/native-UI pass is claimed for this candidate.

Retained initial helper failure was an overly restrictive package-ID pattern that rejected `win-x64`; allowing standard hyphens corrected it before any notice output. `NoticeSource-20261008`/`NoticePreservation-20261008` remain. A malformed explicit source-revision argument was separately refused before build creation (chunk **c2b7be**); the corrected build omits the field because the source is an isolated base plus overlays, and its precise source binding resides in the snapshot record.

## Reproduction and evidence

```powershell
& ./Windows/modern/build-review.ps1 -BuildRoot <fresh-directory-outside-OneDrive>
```

Private evidence root `%USERPROFILE%/.codex/EasyEjectBuildTools`: `NoticeFinalSource-20261008`, `notice-final-source-20261008.json`, `notice-source-20261008.zip`; `NoticeFinalPreservation-20261008/vendor-notices.json`; `NoticeControls-20261008/result.json` and `verify-notice-controls-20261008.ps1`; `NoticeRedirectControls-20261008/result.json`; `NoticeReview-20261008` complete unlaunched app/build/static/notice/inventory evidence; `notice-review-result-20261008.json` durable final collection. Pin-file SHA256 **761690C5BA61431E20173997165DD90F58AC9259BFD16EA27A88C9BD5DB3F6B2**.

No real media/device/eject/cleanup, settings/security/power/display change, auto-start, signing account, spending, push or publication. Complete independently approved vendor paths/catalog and final notices, clean-computer/native loader/trusted direct distribution, modern foreground/tray/lifetime/notification, supplied official positive Luna LUT/genuine camera footage and identified disposable hardware/filesystem/travel gates remain. Full goal active; expired heartbeat paused.
