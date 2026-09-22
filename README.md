# Easy Eject for Photographers

**Import, verify, and eject camera media from your Mac menu bar.**

Version 1.5.2. Requires macOS 14 or later. Release downloads are signed with Developer ID and notarized by Apple.

Easy Eject is a lightweight macOS menu bar utility that intelligently detects camera memory cards and lets you safely eject them with a single click or keyboard shortcut. It also scrubs hidden macOS metadata that causes errors on cameras, emulators, and PCs.

## The Problem

CFexpress cards are PCIe/NVMe solid-state drives under the hood. Because of this, macOS reports them as permanent external SSDs rather than removable camera media. Depending on the card reader and editing app, this can make the usual eject-after-import option unavailable.

On top of that, macOS silently litters every drive with invisible metadata files (`.DS_Store`, `._` AppleDouble resource forks, `__MACOSX` folders). While hidden on your Mac, these files cause real problems on other systems:

- **Emulator consoles:** Phantom game entries from `._` files cluttering ROM libraries
- **Windows/Linux PCs:** Visible `.DS_Store` junk in every folder

## The Solution

### Smart Sorting

Instead of relying on flawed hardware flags, Easy Eject uses two detection methods:

1. **Hardware detection** via DiskArbitration: identifies SD, CFexpress, and XQD card reader protocols
2. **Folder structure scanning**: detects brand-specific camera directories:
   - Canon (`CANONMSC`)
   - Nikon (`NIKON`)
   - Fujifilm (`FUJI`)
   - GoPro (`GOPRO`)
   - Sony & Panasonic Pro Video (`SONY`, `PRIVATE`, `BPAV`, `XDROOT`)

Detected camera cards are grouped under **Camera Cards**. Classification uses hardware and folder clues, so check the listed drives before bulk ejecting. Backup drives containing camera folders can also match.

### Clean & Eject

The metadata scrubber removes hidden macOS files before ejecting:

- **`._*` AppleDouble files** that can appear as phantom entries on emulator consoles
- **`.DS_Store`**: Mac folder settings that clutter Windows and Linux
- **Empty `__MACOSX` folders** after known metadata files are removed. Unrelated contents are preserved

The scrubber safely skips macOS-managed directories (`.Spotlight-V100`, `.Trashes`, `.fseventsd`) to prevent filesystem issues.

## Air unit and camera imports

Open **Air Unit & Camera Imports** from the menu. Connect a device using USB mass storage or a card reader, select its media folder (such as DCIM or VIDEO), choose a destination on a different disk, and save a profile. Each profile recognizes a volume UUID, so similarly named drives do not inherit one another's settings. Re-enroll a device after formatting it.

- Automatic import is opt-in per device. **Import now** also works with automatic mode off.
- Originals are kept by default, which is recommended for client work and important photos or videos. Enabling permanent deletion requires confirming a warning that files bypass Trash and cannot be restored from it. Use deletion only for unimportant or replaceable footage and keep independent backups. Deletion occurs only after SHA-256 verification of a durable saved copy.
- Files are stored under the local import date (`YYYY-MM-DD`), retaining paths inside the selected media folder. Filename conflicts preserve both versions. Reimporting identical content reuses the saved copy.
- Menu bar text shows the active phase and percentage. The import window shows file counts, a progress bar, the current filename, and the latest result.
- **Stop import** preserves unverified originals. Completed, verified files may already have been removed if deletion was enabled.
- Optional Trash recovery saves files from the current user's Trash on that registered device into `Recovered Device Trash` before permanently removing them. This requires Full Disk Access.
- Optional automatic eject runs only after a successful scan/import and final device checks. Failed imports leave remaining originals in place and display a visible error.
- Manual eject, bulk eject, and the global shortcut cannot eject an active import's source or destination disk, including other partitions of those disks.
- Profiles wait for the chosen destination; connecting it while the source is still present retries setup. Mid-import failures require **Import now** or reconnecting the source.

The importer copies every regular file in your selected media folder, including video, camera RAW, proxy, and sidecar files. It skips hidden files and folders in normal media imports, and does not follow symbolic links. This avoids silently omitting recordings with unfamiliar extensions. Compatibility depends on the unit exposing readable mounted storage. Image Capture-only/PTP/MTP devices are not supported by this version. Testing one model does not establish support for every DJI or other air unit.

The native importer does not require Python, Xcode, Image Capture, a separate launch agent, or a background script on the user's Mac. Easy Eject must be running and the Mac awake. Enable Launch at Login if desired.

Profiles and the import audit log are stored in `~/Library/Application Support/Easy Eject/`. Logs identify saved paths, SHA-256 values, source removal, and errors. Removing a profile does not remove imported files.

## Key Features

- **Smart Sorting:** Automatically identifies CFexpress, XQD, and SD cards: including those connected via Mac Studio's front card reader.
- **One-Click Bulk Eject:** Safely unmount all camera cards simultaneously.
- **Clean & Eject:** Scrub hidden macOS metadata before ejecting. Set as the default for camera cards, or choose per-drive for other volumes.
- **Camera Card Eject Mode:** Toggle between "Eject" and "Clean & Eject" as the default for all camera card buttons and the keyboard shortcut.
- **Per-Drive Options:** Non-camera drives (SSDs, thumb drives, emulator cards) show a submenu with both Eject and Clean & Eject options.
- **Global Keyboard Shortcut:** Press **⌃⌥⌘ + letter** to instantly eject all camera cards from any app. Choose the letter in Settings and check for conflicts with shortcuts in your other apps.
- **Safety Warnings:** Alerts before ejecting non-camera drives to prevent accidental disconnection.
- **Launch at Login:** Optionally start with your Mac so it's always ready.
- **Debug Window:** Built-in diagnostic log with copy-to-clipboard for troubleshooting drive detection.
- **Native app:** SwiftUI menu bar utility with no ads or tracking. Memory use varies during imports.

## Validation

Run the isolated file-safety tests:

```sh
xcrun swiftc -parse-as-library Ejector/MediaImportEngine.swift Ejector/MetadataCleaner.swift Tests/ImportEngineTests.swift -o /tmp/easy-eject-tests
/tmp/easy-eject-tests
```

`Tests/MountedVolumeImportTest.swift` is an opt-in integration test. It requires a disposable volume specifically named **Easy Eject Test**, a fixture at `DCIM/DJI_001/TEST_IMPORT.MP4`, and `~/Easy Eject Test Imports` on a different volume. It copies/verifies/removes that fixture and ejects that test volume. Never point it at real media.

See `REVIEW.md` for review findings, fixes, evidence, and remaining release checks.

## Installation

1. Download the latest release from [Releases](https://github.com/rsmith4321/Ejector/releases/latest).
2. Move **Easy Eject** to your Applications folder.
3. Open the app and follow the macOS first-launch prompt. Use the signed, notarized release from this repository.

## Permissions

- **Accessibility** *(optional)*: Required only for the global keyboard shortcut to work inside other apps. The app walks you through enabling it in System Settings. Manual ejection from the menu works without this.
- **Full Disk Access** *(optional)*: Needed for protected locations such as device Trash, and when macOS denies access during cleanup. Normal file imports use the source and destination folders you select. The app guides you through setup with a one-click button that opens System Settings and reveals the app in Finder for easy drag-and-drop.

## Support

For tutorials, troubleshooting, and contact:
[Easy Eject setup and support](https://easyeject.com/)

---
*Created by Ryan Smith for the photography and videography community.*
