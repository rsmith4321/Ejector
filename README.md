# Easy Eject

A native macOS menu bar utility for ejecting camera cards, optional hidden-file cleaning and verified media imports. **One shared codebase, two distribution builds:**

| | Website download | Mac App Store |
| --- | --- | --- |
| Target / bundle | `Ejector` / `com.ryansmithphotography.Ejector` | `EjectorStore` / `com.ryansmithphotography.EasyEject.store` |
| Current source | Website 1.7.1 (local candidate) | Store 1.1 (9) submitted; public Store 1.0 (8) |
| Distribution | Developer ID signed and notarized | App Sandbox and App Store review |
| Folder detection and cleaning | No Easy Eject per-card authorization; macOS Files and Folders permissions still apply | Select the whole card with Authorize a Card; repeat if formatting invalidates access |
| Updates | Sparkle automatic checks; signed GitHub updates installed with approval | App Store |

Both use the same native menu, classifier, settings, metadata cleaner, import engine, shortcuts and eject policies. Keep differences limited to permissions, distribution and the direct build's welcome screen. The September 22 decision to restore the website build supersedes earlier Store-only retirement notes.

## Eject your card

Finish transfers in Lightroom or your usual photo app. Open Easy Eject's menu and choose the card. Plain Eject needs no import profile or folder authorization in either build. Unplug only after success. Ejection is not forced; ejecting a physical disk also unmounts its partitions.

The menu groups Camera Cards, Emulator Cards and Other External Volumes. Hardware that identifies SD/CFexpress/XQD can be recognized without folder access. Readers presenting generic PCI/NVMe storage may need camera folders to identify CFexpress. The Store build can lose that folder-based classification after camera formatting until authorized again; the disk is still available under Other External Volumes. The website build can inspect those folders without per-card grants, subject to normal macOS permissions. Classification is a heuristic, not a guarantee for every reader or an empty card.

The count deduplicates physical card disks. Review the list before Eject All Cards. The optional Control-Option-Command plus selected letter shortcut starts off and needs no Accessibility permission. Issues show a compact warning icon; View Issue opens the explanation, and Dismiss acknowledges it without retrying. Later failures restore attention.

## Optional cleaning

Cleaning removes regular `._*`, `.DS_Store`, `.apdisk` files and empty `__MACOSX` folders. It preserves ordinary media, ignores symbolic links and skips protected system folders and device Trash.

For the Store build, choose Authorize a Card and select the **whole card** under Locations, not DCIM. Authorize again if formatting invalidates access; Forget removes the saved grant. Granting access never starts importing or enables deletion. Full Disk Access is not a sandbox bypass.

For the website build, there is no Authorize a Card step. macOS can still request removable/network-volume access; review Privacy & Security → Files and Folders if denied. Enable Clean Cards Before Ejecting only if desired. Other volumes have a separate Clean & Eject action.

## Optional imports

In Device Media Imports, select the device, its media folder (such as DCIM, or a complete camera package/whole card) and a destination on another physical disk. The Store build saves scoped source/destination permissions. Profiles match the volume UUID, not its name or DJI folder alone. Formatting can change the UUID and require setup again **in either build**.

Copies go into dated folders and are verified with SHA-256 and durability checks. Originals are kept by default. Automatic import and the post-import eject prompt start off unless changed in import defaults. Original deletion and device Trash recovery always start off for each new profile. Saving does not start an import. Permanent original deletion requires explicit confirmation and saved-copy verification; keep it off for important media. Device Trash recovery verifies recovered copies before removal, and in Store also needs whole-card access.

Website 1.6.0 adds **Videos only**, **Include camera previews**, and **Clean folder layout**. Media selection and preview selection are independent. New profiles follow the user’s import defaults unless Customize for this device is enabled; older profiles retain their saved choices as custom settings. Previews remain included unless disabled. Skipped previews, unknown files and device indexes are never deleted by the importer. Required camera-package files remain included even when optional previews are off.

Ordinary media can be saved directly in the dated folder with unchanged names. Conflicting groups go in Additional media; known structured or unfamiliar media retains its tree under Camera originals. Complete Sony/P2/RED and other recognized packages include supporting audio/metadata; partial CLIP/STREAM selections stop with guidance. This is a verified file copier, not a codec converter, stitching engine, clip joiner, camera database editor or universal device certification. Same-name still/movie pairs are conservatively kept for photo workflows; renamed Live Photo pairs and unfamiliar video sequences need manual review.

For Lightroom: select Videos only, optionally exclude previews, and choose whether to keep or permanently delete verified imported videos. Enable Ask to eject after import for a native dialog with Keep Connected as the default. A camera exposing multiple mounted storage sources gets one explicit grouped confirmation. Eject actions validate the exact source topology and identities before sequential unmounts. No matching media describes the configured selection, not the whole camera. Source media is kept unless permanent deletion was explicitly enabled for that device.

[Import and Lightroom guide](https://easyeject.com/help/video-imports-with-lightroom) · [Camera compatibility and official sources](https://easyeject.com/help/camera-media-compatibility)

Public Store 1.0 (8) has the earlier importer. Store 1.1 (9) was submitted before import defaults were added. Shared source compiles for both targets; compiling this change does not submit or publish an App Store update.

Source/destination disks are protected during imports. Completion dialogs explain what will be unmounted. Import reservations are released before waiting for the eject choice. Eject actions check the source volume, original physical disk and connection; a changed or unknown disk is not ejected. Disconnect/reconnect, a new import on the device, and app restart invalidate old actions. Keep independent backups. Devices must appear as mounted storage in Finder; PTP/MTP-only devices are unsupported.

## Install and develop

Requires macOS 14+. Universal arm64/x86_64 builds; Intel and older macOS runtime coverage is limited. Quit the other edition before switching, and do not run two automatic importers for the same device. Each bundle keeps its own preferences/profiles; they are not migrated automatically. A returning website installation retains its existing settings.

Archive `Ejector` or `EjectorStore` with the preserved release toolchain. Direct releases require Developer ID signing, notarization, stapling, signature/Gatekeeper verification and a tested download. Store releases require current App Store Connect checks and one reviewed submission. Build/upload/submission are not approval or live availability. See `Store/RELEASE-8.md`, `Store/README.md` and `RELEASE-1.6.0.md` for evidence and limits.

[Download and comparison](https://easyeject.com/editions/) · [Support](https://easyeject.com/support/) · [Privacy](https://easyeject.com/privacy/)


## New-device defaults and website updates

Settings and Device Media Imports → Import defaults let users choose media selection, camera previews, clean layout, automatic import and the post-import eject prompt. New cards visibly use defaults, with their option controls disabled until Customize for this device is selected. Edit defaults is available directly in the profile editor. Cards using defaults follow changes on their next import; each running import freezes its options. Older profiles retain saved settings as custom profiles. Returning to defaults disables original deletion and device Trash recovery; these options require custom settings and their per-device confirmations.

Website 1.7.0 uses Sparkle 2.10.0, linked only into the Ejector target. Checks are automatic by default and can be disabled in Settings. Users approve installation. Silent downloading/installing and system-profile reporting are disabled. Both the appcast and update archive require Ed25519 signatures; the app and DMG also use Developer ID signing and notarization. Updates wait for imports, ejection and open dialogs. Final termination refuses to interrupt active work. See [update publishing](Updates/README.md).
