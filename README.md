# Easy Ejector for Photographers

**The missing "Eject" button for macOS photographers, videographers, and content creators.**

Easy Ejector is a lightweight macOS menu bar utility that intelligently detects camera memory cards and lets you safely eject them with a single click or keyboard shortcut. It also scrubs hidden macOS metadata that causes errors on cameras, emulators, and PCs.

## The Problem

CFexpress cards are PCIe/NVMe solid-state drives under the hood. Because of this, macOS reports them as permanent external SSDs rather than removable camera media. This breaks auto-eject in Lightroom, Premiere Pro, DaVinci Resolve, and other editing apps — forcing you to open Finder every time you want to safely remove a card.

On top of that, macOS silently litters every drive with invisible metadata files (`.DS_Store`, `._` AppleDouble resource forks, `__MACOSX` folders). While hidden on your Mac, these files cause real problems on other systems:

- **Cameras:** Database errors and corrupted thumbnails when cards are reinserted
- **Emulator consoles:** Phantom game entries from `._` files cluttering ROM libraries
- **Windows/Linux PCs:** Visible `.DS_Store` junk in every folder

## The Solution

### Smart Sorting

Instead of relying on flawed hardware flags, Easy Ejector uses two detection methods:

1. **Hardware detection** via DiskArbitration — identifies SD, CFexpress, and XQD card reader protocols
2. **Folder structure scanning** — detects brand-specific camera directories:
   - Canon (`CANONMSC`)
   - Nikon (`NIKON`)
   - Fujifilm (`FUJI`)
   - GoPro (`GOPRO`)
   - Sony & Panasonic Pro Video (`SONY`, `PRIVATE`, `M4ROOT`, `BPAV`, `XDROOT`)

Detected camera cards are grouped under a dedicated **Camera Cards** section, cleanly separated from your permanent drives.

### Clean & Eject

The metadata scrubber removes hidden macOS files before ejecting:

- **`._*` AppleDouble files** — the #1 cause of phantom entries on emulator consoles
- **`.DS_Store`** — Mac folder settings that clutter Windows and Linux
- **`__MACOSX` folders** — junk created when extracting zip files

The scrubber safely skips macOS-managed directories (`.Spotlight-V100`, `.Trashes`, `.fseventsd`) to prevent filesystem issues.

## Key Features

- **Smart Sorting:** Automatically identifies CFexpress, XQD, and SD cards — including those connected via Mac Studio's front card reader.
- **One-Click Bulk Eject:** Safely unmount all camera cards simultaneously.
- **Clean & Eject:** Scrub hidden macOS metadata before ejecting. Set as the default for camera cards, or choose per-drive for other volumes.
- **Camera Card Eject Mode:** Toggle between "Eject" and "Clean & Eject" as the default for all camera card buttons and the keyboard shortcut.
- **Per-Drive Options:** Non-camera drives (SSDs, thumb drives, emulator cards) show a submenu with both Eject and Clean & Eject options.
- **Global Keyboard Shortcut:** Press **⌃⌥⌘ + letter** to instantly eject all camera cards from any app. Fully customizable and designed to never conflict with Adobe or editing shortcuts.
- **Safety Warnings:** Alerts before ejecting non-camera drives to prevent accidental disconnection.
- **Launch at Login:** Optionally start with your Mac so it's always ready.
- **Debug Window:** Built-in diagnostic log with copy-to-clipboard for troubleshooting drive detection.
- **Zero Bloat:** Native SwiftUI app, under 30MB memory footprint, no ads, no cost.

## Installation

1. Download the latest release from [Releases](https://github.com/rsmith4321/Ejector/releases/latest).
2. Move **Easy Ejector** to your Applications folder.
3. **First launch:** Right-click the app and select **Open**, then click **Open** in the dialog. You only need to do this once (required because the app is distributed outside the Mac App Store).

## Permissions

- **Accessibility** *(optional)*: Required only for the global keyboard shortcut to work inside other apps. The app walks you through enabling it in System Settings. Manual ejection from the menu works without this.
- **Full Disk Access** *(optional)*: Required for the Clean & Eject feature to scan and delete hidden metadata files. The app guides you through setup with a one-click button that opens System Settings and reveals the app in Finder for easy drag-and-drop.

## Support

For tutorials, troubleshooting, and contact:
[ryansmithphotography.com/easyejector](https://www.ryansmithphotography.com/easyejector)

---
*Created by Ryan Smith for the photography and videography community.*
