# Shared-code implementation verification, September 22, 2026

## Implemented locally

Checkout: `worktrees/store-feature-parity`, branch `codex/store-feature-parity`, based on `925ce7779e2d9943c5cf5bdfb883e00c16def43d`. Store candidate 1.0 (3). Both targets use one native AppKit menu, settings/help, hardware/folder classifier, metadata cleaner, verified import engine and eject coordination. There is no separate reduced Store app implementation.

Restored: Camera/Emulator/Other grouping, individual and bulk card eject, Clean & Eject, SSD confirmation, registered configurable global shortcut, logs/help/settings, explicit Trash recovery after saved-copy verification. Original media is retained by default. Bulk operations plan physical disks and permissions before cleanup, protect active import disks, deduplicate partitions, and stop on failure.

Manual Eject remains the primary workflow. Eject after verified import is an optional per-device setting, now off for new profiles. It does not eject a card merely because it was connected unless that card has an explicitly enabled automatic import profile. Existing profile choices are preserved. Multiple enrolled partitions or unknown coordination identity hold automatic eject for explicit menu action.

## Observed and independently checked

Signed local sandbox preview: `com.ryansmithphotography.EasyEject.paritypreview`, separate container and preferences, same three Store entitlements. Tests used only generated files on an 80 MB HFS+ disk image named Easy Eject Parity Test, with an APFS destination on this SSD.

- Native menu separately displayed the connected CFexpress card under Camera Cards and ordinary external disks in their own group. No real media was ejected or cleaned.
- Manual Eject from the preview unmounted the generated card. A second run without any card grant showed one non-card confirmation with Cancel focused, ejected successfully and displayed safe to unplug. See `manual-eject-verification.json`.
- Clean & Eject from the preview removed generated `.DS_Store` and `.apdisk` files and unmounted the card. Remount verified original media and a user file within `__MACOSX` were intact. See `clean-eject-verification.json`.
- A normal Open panel grant allowed shared metadata cleanup in the signed sandbox. Stored permission survived relaunch. Before authorization the generated media read was denied. See `probe/first-access-results.json`.
- Shared Trash recovery ran in the signed probe and actual preview. Destination copies matched expected bytes/hashes before generated Trash sources were removed; ordinary source media remained. See `probe/recovery-results.json` and `ui-import-verification.json`.
- Enabling only the disposable profile's automatic import and eject options, then detaching/reconnecting, triggered import without pressing Import now, verified copies, unmounted the image and displayed “Safe to unplug.” A read-only remount confirmed original files retained. See `automatic-eject-verification.json`.
- Cleanup confirmation defaults to Cancel. Settings, debug window and import profile editor were exercised. The Settings Done action now targets its own window and was observed closing it. Menu tracking is cancelled before non-card confirmation dialogs; the final preview showed one visible prompt. Preview test profile and card grant were removed; cleanup, debug, global shortcut and login left off. Fixture image is unmounted.
- Both full and Store Release targets compile with the preserved Xcode 26.6 RC2 release toolchain. The local signed preview uses installed Xcode 27.1. These are local builds, not a Store-distributed package.
- 14 shared import/cleanup cases and 16 signed sandbox cases passed; 9 bulk physical-disk planning and 9 automatic-eject partition policy cases passed. Logs are in `tests/`. Signature verified with the sandbox, user-selected read/write and app-scoped bookmark entitlements.

## Actual sandbox and distribution differences

No tested core eject, metadata-cleaning, import or Trash-recovery feature required removal. Store file access requires explicit folder grants: card root for folder classification/cleaning/Trash, media and destination for import. Formatting or lost permissions requires authorization again. Hardware-based CFexpress/SD detection and plain ejection work without card folder access.

Store updates use App Store; the direct target retains its existing GitHub update check and launch license screen. No Accessibility permission or broad filesystem exception was added. This permits one shared product implementation; the two bundle IDs and distribution targets remain until a separate distribution decision is made. It is not evidence of Apple approval for the new candidate.

## Limits and remaining release work

Actual notification delivery was not established: the preview identity reported notifications not allowed. Physical global-shortcut activation, launch-at-login/reboot, Intel/older macOS, real CFexpress cleanup and long transfers, and native multi-profile/multi-disk operation are not newly verified. Disk-policy tests are not substitutes for hardware runs. The latest cleanup/recovery proof is HFS+ on this Mac, not a filesystem-wide compatibility claim.

No installed release, production import profile, website, Apple metadata or submission was changed. Existing Store build 2 submission remains a separate artifact; recheck live Apple state before replacing it. Review this code, then archive/sign the final release identity, rerun release QA, capture final screenshots and coordinate the existing submission. Do not reuse prototype screenshots as evidence of this candidate.
