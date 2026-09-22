# Easy Eject

A native macOS menu bar utility for ejecting camera cards, optional hidden-file cleaning and verified media imports. The maintained distribution is the **Mac App Store app** (`EjectorStore`, `com.ryansmithphotography.EasyEject.store`). The former direct website download is retired. Historical source and release artifacts remain available for rollback; do not ship the legacy `Ejector` target as a second current edition.

## Eject your card

Finish transfers in Lightroom or your usual photo app. Open the eject icon in the Mac menu bar and choose the card. Plain Eject needs no import profile or folder authorization. Unplug only after ejection succeeds. Ejection is not forced, and ejecting a physical disk unmounts its other partitions.

The menu groups Camera Cards, Emulator Cards and Other External Volumes. The count deduplicates physical card disks. Card detection uses hardware and, when authorized, familiar camera/emulator folder structures; it is a heuristic. Review the list before Eject All Cards. The optional Control-Option-Command plus selected letter shortcut starts off and needs no Accessibility permission.

## Authorize a card

1. Choose **Authorize a Card** in the menu or Settings.
2. In the chooser, select the card's name under **Locations**, such as **Untitled**. Select the card itself, not DCIM or a folder inside it.
3. Click **Authorize**. Access is remembered for that volume. After formatting the card, authorize it again. **Forget** in Settings removes saved access.
4. If desired, enable **Clean Cards Before Ejecting**. Other volumes have a separate **Clean & Eject** action.

Cleaning removes regular `._*`, `.DS_Store`, `.apdisk` files and empty `__MACOSX` folders. It preserves ordinary media, ignores symbolic links and skips protected system folders and device Trash. Permission does not start an import or enable deletion. Full Disk Access is not required or used as a sandbox bypass. Instructions appear on first launch and remain in Help and Settings.

## Optional imports

Open **Air Unit & Camera Imports**, select a device and choose **Set up import**. Authorize its media folder (such as DCIM) and a destination folder on a different physical disk. These are separate grants from whole-card authorization.

Copies go into dated folders. SHA-256 and durability checks verify saved files. Originals are retained by default. Automatic import, eject after verified import, original deletion and device Trash recovery are separate per-profile options and start off for new profiles. Saving a profile does not start an import; use Import now or the next connection.

Optional original deletion is permanent, bypasses Trash and requires a warning. Keep it off for important media. Optional device Trash recovery also needs whole-card authorization; recovered files are copied and verified before being removed from that device's Trash. Other drives are untouched. Keep independent backups.

Source and destination disks are protected during imports. Multiple enrolled partitions or unavailable physical identity hold automatic eject; finish the desired imports and eject manually. Formatting requires new authorization and a new import profile. No profile or permission is migrated automatically from the retired direct app.

## Development and release

Requires macOS 14 or later. Archive the **EjectorStore** scheme with an Apple-accepted release toolchain. Store version 1.0, build 3. `Store/README.md`, `Store/VERIFICATION.md` and `Store/RELEASE-3.md` distinguish local, uploaded, submitted and approved state. Never infer public availability from a successful build or upload.

Shared app code contains historical direct-target compatibility branches, but only the Store scheme is maintained for distribution. Store permissions use App Sandbox, user-selected read/write and app-scoped bookmarks. Updates go through the App Store.

[Support and authorization guide](https://easyeject.com/support/) · [Privacy](https://easyeject.com/privacy/)
