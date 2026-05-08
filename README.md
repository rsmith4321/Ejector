# Easy Ejector for Photographers

[cite_start]**The missing "Eject" button for macOS photographers, videographers, and content creators.** [cite: 1571]

[cite_start]Easy Ejector was born out of a daily workflow headache. [cite: 1521, 1572] [cite_start]I created this application because of a frustration I was having on macOS: after upgrading to my Sony A7RV and switching to CFexpress cards, Adobe Lightroom could no longer automatically eject the card after importing images. [cite: 1473, 1474, 1484, 1522] [cite_start]This app was built specifically for this workflow, ensuring you don't have to dig through Finder just to safely unmount a card. [cite: 1260, 1283, 1487]

## The Problem: The CFexpress Hardware Quirk

* [cite_start]**Hardware Identity**: Under the hood, a CFexpress card is literally a PCIe/NVMe solid-state drive. [cite: 139, 1499, 1531, 1576]
* [cite_start]**OS Misidentification**: Because of this high-speed architecture, macOS and Windows both report its hardware profile as a standard external hard drive. [cite: 140, 1500, 1532, 1577]
* [cite_start]**Broken Automation**: This is precisely why standard system flags like Apple's `volumeIsRemovableKey` return false, causing photo and video editing apps like Lightroom or Premiere Pro to fail to recognize them as camera media for automatic ejection. [cite: 141, 1501, 1533, 1574, 1578]

## The Solution: Smart Folder Detection

[cite_start]Instead of relying on flawed hardware flags, Easy Ejector uses a **"Smart Sorting" heuristic**. [cite: 143, 1506, 1537, 1579] [cite_start]It quietly peeks at the file structure of your connected drives and actively scans for brand-specific root folders used by major camera systems. [cite: 1294, 1295, 1540, 1580]

[cite_start]The app instantly recognizes cards formatted by: [cite: 1510, 1542]
* [cite_start]**Canon** (CANONMSC) [cite: 698, 1509, 1541]
* [cite_start]**Nikon** (NIKON) [cite: 698, 1509, 1541]
* [cite_start]**Fujifilm** (FUJI) [cite: 698, 1509, 1541]
* [cite_start]**GoPro** (GOPRO) [cite: 698, 1509, 1541]
* [cite_start]**Sony & Panasonic Pro Video** (SONY, PRIVATE, M4ROOT, BPAV, XDROOT) [cite: 503, 1296, 1509, 1541]

[cite_start]If these folders are found, the app intelligently groups the drive under a dedicated "Camera Cards" section in your menu bar, separate from your permanent RAID arrays and working SSDs. [cite: 1297, 1510, 1542, 1581]

## Key Features

* [cite_start]**🚀 Smart Sorting**: Automatically identifies CFexpress, XQD, and SD cards—including those connected via the high-speed PCIe card reader on the front of a Mac Studio. [cite: 664, 667, 1511, 1543, 1582]
* [cite_start]**🖱️ One-Click Bulk Eject**: Safely unmount all detected camera media simultaneously without accidentally disconnecting your working drives. [cite: 1512, 1544, 1583]
* [cite_start]**⌨️ Safe Global Hotkey**: Press **⌃⌥⌘ + [Letter]** (Control + Option + Command) to instantly eject cards from anywhere on your Mac. [cite: 701, 1513, 1545, 1584] [cite_start]The default is `E`, but it is fully customizable via a dropdown menu. [cite: 1231, 1236, 1585]
* [cite_start]**🍃 Zero Bloat**: A native SwiftUI background agent that consumes virtually no memory and lives quietly as a UIElement in your menu bar. [cite: 2, 1302, 1515, 1547, 1586]
* [cite_start]**🛠️ Debug Mode**: Includes a built-in diagnostic window with a "Copy to Clipboard" button for frictionless support requests. [cite: 721, 725, 1587]

## Installation & Usage

1. [cite_start]**Download**: Grab the latest `Easy_Ejector_Installer.dmg` from the [Releases](https://github.com/YOUR_USERNAME/Ejector/releases/latest) section. [cite: 1555, 1558, 1588]
2. [cite_start]**Install**: Open the DMG and drag **Easy Ejector** into your Applications folder. [cite: 1344, 1465, 1589]
3. [cite_start]**Permissions**: To use the global keyboard shortcut while in other apps (like Photoshop), macOS requires **Accessibility** permission. [cite: 969, 970, 1028, 1590] [cite_start]This is entirely optional; the app remains fully functional for manual ejection without it. [cite: 987, 1029, 1031, 1591]
4. [cite_start]**Gatekeeper Note**: Because this app is built for the photography community by an independent developer, you may need to hold the **Control** key while clicking the app and select **Open** the first time you run it. [cite: 867, 923, 957, 1469, 1592]

## Distribution & Development

[cite_start]To formally notarize or publish this app, a paid **Apple Developer Program** account is required ($99/year). [cite: 1222, 1223, 1598] [cite_start]Once an app is submitted, Apple's manual review process typically takes **24 to 48 hours**. [cite: 1217, 1221, 1599]

## Support & Updates

For tutorials, troubleshooting, and contact information, visit:
[cite_start]**[ryansmithphotography.com/easyejector](https://www.ryansmithphotography.com/easyejector)** [cite: 1271, 1274, 1593]

---
[cite_start]*Created by Ryan Smith for the professional photography and videography community.* [cite: 1594]
