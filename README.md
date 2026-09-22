# Easy Eject

A native macOS menu bar utility for ejecting camera cards, optional hidden-file cleaning and verified media imports. **One shared codebase, two distribution builds:**

| | Website download | Mac App Store |
| --- | --- | --- |
| Target / bundle | `Ejector` / `com.ryansmithphotography.Ejector` | `EjectorStore` / `com.ryansmithphotography.EasyEject.store` |
| Current version | 1.5.3 | 1.0 (5), submitted; verify current Apple status |
| Distribution | Developer ID signed and notarized | App Sandbox and App Store review |
| Folder detection and cleaning | No Easy Eject per-card authorization; macOS Files and Folders permissions still apply | Select the whole card with Authorize a Card; repeat if formatting invalidates access |
| Updates | GitHub release checker | App Store |

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

In Air Unit & Camera Imports, select the device, its media folder (such as DCIM) and a destination on another physical disk. The Store build saves scoped source/destination permissions. Profiles match the volume UUID, not its name or DJI folder alone. Formatting can change the UUID and require setup again **in either build**.

Copies go into dated folders and are verified with SHA-256 and durability checks. Originals are kept by default. Automatic import, eject after verified import, original deletion and device Trash recovery are separate options, off for new profiles. Saving does not start an import. Permanent original deletion requires explicit confirmation and saved-copy verification; keep it off for important media. Device Trash recovery verifies recovered copies before removal, and in Store also needs whole-card access.

Source/destination disks are protected during imports. Multiple enrolled partitions or unknown disk identity hold automatic eject; finish imports and eject manually. Keep independent backups. Devices must appear as mounted storage in Finder; PTP/MTP-only devices are unsupported.

## Install and develop

Requires macOS 14+. Universal arm64/x86_64 builds; Intel and older macOS runtime coverage is limited. Quit the other edition before switching, and do not run two automatic importers for the same device. Each bundle keeps its own preferences/profiles; they are not migrated automatically. A returning website installation retains its existing settings.

Archive `Ejector` or `EjectorStore` with the preserved release toolchain. Direct releases require Developer ID signing, notarization, stapling, signature/Gatekeeper verification and a tested download. Store releases require current App Store Connect checks and one reviewed submission. Build/upload/submission are not approval or live availability. See `Store/RELEASE-5.md`, `Store/README.md` and `RELEASE-1.5.3.md` for evidence and limits.

[Download and comparison](https://easyeject.com/editions/) · [Support](https://easyeject.com/support/) · [Privacy](https://easyeject.com/privacy/)
