# Easy Ejector for Photographers

**The missing "Eject" button for macOS photographers, videographers, and content creators.**

Easy Ejector was born out of a daily workflow headache. I created this application because of a frustration I was having on macOS: after upgrading to my Sony A7RV and switching to CFexpress cards, Adobe Lightroom could no longer automatically eject the card after importing images. This app was built specifically for this workflow, ensuring you don't have to dig through Finder just to safely unmount a card.

## The Problem: The CFexpress Hardware Quirk

* **Hardware Identity:** Under the hood, a CFexpress card is literally a PCIe/NVMe solid-state drive.
* **OS Misidentification:** Because of this high-speed architecture, macOS and Windows both report its hardware profile as a standard external hard drive.
* **Broken Automation:** This is precisely why standard system flags like Apple's `volumeIsRemovableKey` return false, causing photo and video editing apps like Lightroom or Premiere Pro to fail to recognize them as camera media for automatic ejection.

## The Solution: Smart Folder Detection

Instead of relying on flawed hardware flags, Easy Ejector uses a **"Smart Sorting" heuristic**. It quietly peeks at the file structure of your connected drives and actively scans for brand-specific root folders used by major camera systems.

The app instantly recognizes cards formatted by:
* **Canon** (CANONMSC)
* **Nikon** (NIKON)
* **Fujifilm** (FUJI)
* **GoPro** (GOPRO)
* **Sony & Panasonic Pro Video** (SONY, PRIVATE, M4ROOT, BPAV, XDROOT)

If these folders are found, the app intelligently groups the drive under a dedicated "Camera Cards" section in your menu bar, separate from your permanent RAID arrays and working SSDs.

## Key Features

* **🚀 Smart Sorting:** Automatically identifies CFexpress, XQD, and SD cards—including those connected via the high-speed PCIe card reader on the front of a Mac Studio.
* **🖱️ One-Click Bulk Eject:** Safely unmount all detected camera media simultaneously without accidentally disconnecting your working drives.
* **⌨️ Safe Global Hotkey:** Press **⌃⌥⌘ + [Letter]** (Control + Option + Command) to instantly eject cards from anywhere on your Mac. The default is **E**, but it is fully customizable via a dropdown menu.
* **🍃 Zero Bloat:** A native SwiftUI background agent that consumes virtually no memory and lives quietly as a UIElement in your menu bar.
* **🛠️ Debug Mode:** Includes a built-in diagnostic window with a "Copy to Clipboard" button for frictionless support requests.

## Installation & Usage

1. **Download:** Grab the latest `Easy_Ejector_Installer.dmg` from the [Releases](https://github.com/YOUR_USERNAME/Ejector/releases/latest) section.
2. **Install:** Open the DMG and drag **Easy Ejector** into your Applications folder.
3. **Permissions:** To use the global keyboard shortcut while in other apps (like Photoshop), macOS requires **Accessibility** permission. This is entirely optional; the app remains fully functional for manual ejection without it.
4. **Gatekeeper Note:** If you encounter a security warning, hold the **Control** key while clicking the app and select **Open** the first time you run it.

## Support & Updates

For tutorials, troubleshooting, and contact information, visit:
[ryansmithphotography.com/easyejector](https://www.ryansmithphotography.com/easyejector)

---
*Created by Ryan Smith for the photography and videography community.*
