# Easy Eject for Windows · prototype 0.1

Native C# / Windows Forms source for Windows 11 x64, using .NET Framework 4.8 and supported Windows storage/PnP APIs. This implementation is isolated from the Mac targets and website. The current prototype passes 49 fixture/policy tests, native read-only enumeration, and a hidden-window notification smoke check. Real card/removal compatibility and visual UI acceptance are pending. See [PROGRESS.md](PROGRESS.md) for exact evidence and [PARITY.md](PARITY.md) for the full-project parity scope and direct-download plan. The exact camera model, reader and destination remain to be confirmed; this is not a complete port yet.

## Build and run

From the repository root in PowerShell:

```powershell
./Windows/build.ps1
./Windows/inspect-readonly.ps1
```

The build uses the installed `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, compiles app and test executable with `/warnaserror+ /platform:x64`, and places binaries/configuration under `%LOCALAPPDATA%\EasyEject\PrototypeBuild`, outside the OneDrive checkout. No SDK, NuGet download, installer, elevation, or startup registration is involved. A modern SDK project can be added later; this first slice uses the compiler already available on this laptop.

Only in an environment where execution is permitted by the existing security policy:

```powershell
./Windows/build.ps1 -Test
& "$env:LOCALAPPDATA/EasyEject/PrototypeBuild/EasyEject.Tests.exe" --inventory
& "$env:LOCALAPPDATA/EasyEject/PrototypeBuild/EasyEject.Tests.exe" --ui-smoke
& "$env:LOCALAPPDATA/EasyEject/PrototypeBuild/EasyEject.Windows.exe"
```

`-Test` runs 49 cases. They use fake discovery/removal adapters and disposable files under `%USERPROFILE%\.codex\EasyEjectFixtures\run-<id>`. Source and destination fixture directories share the host disk while the fake guard supplies two conceptual disk identities. Production imports always require different physical disks. Junction tests create junctions only within that fixture and do not change OS permissions. A configuration test writes/removes its own randomly identified fixture profile in the app's configuration storage. Default tests never invoke `CM_Request_Device_Eject`. `--inventory` reads native storage inventory, without a notification window or device operations. `--ui-smoke` constructs a hidden device window, registers disk notifications, reads inventory and verifies system-eject policy; it briefly creates/disposes its tray icon but does not show a window or operate on devices. The separate PowerShell diagnostic uses OS CIM providers.

Smart App Control initially blocked an unsigned compiler probe. Ryan subsequently turned it off himself, after reviewing Microsoft instructions, and authorized continued testing. Ordinary executable launches now work. The agent did not change security policy, modify allowlists, install a trusted root, or use an alternate loader. Microsoft documents trusted code signing as the supported path for distributing an app with Smart App Control enabled; no signing account or credentials were used here.

## Use after runtime acceptance

The app opens a device window and adds a tray icon. Close hides the window; the tray's Quit exits when no operation is active. A session mutex prevents duplicate instances. Refresh performs read-only inventory. Device notifications refresh the list and invalidate pending actions. Connections never start import or eject automatically.

Select a device and choose **Safe eject…**. The confirmation names every known volume under the selected removal target, including sibling partitions and LUNs. No is the default. The service reserves affected physical disks, re-enumerates and compares identities/topology/connection generation, and requests Windows safe removal. Windows veto type, veto name, and CONFIGRET failures are shown without forced removal, retries, permissions changes, or broad hub ejection. Do not unplug after a failure.

Profiles normally live under local app-data. When launched by Codex's packaged host, Windows redirects the app's own configuration folder into that host's LocalCache. The profile store resolves this folder within local app-data and pins its canonical path. Source/destination media paths still reject redirection and all reparse points; there is no import-path exception for host virtualization.

For **Verified import…**, choose a complete media folder or camera package on the selected volume and an existing destination on a different physical disk. The app explains both paths and asks before copying. Saving folder choices as a volume-bound profile is optional and never enables automatic import. Formatting or a changed identity invalidates a profile; choose the folders again. Imports leave the device connected.

Each nonempty import creates a fresh dated batch containing `Camera originals/<original relative paths>` and a `verified-import.json` completion receipt. All regular files within the selection are copied, including unfamiliar extensions and package dependencies. Windows system-volume information and Recycle Bin folders are excluded. Partial `CLIP`/`STREAM` selections are rejected; select the complete package. Empty directories are not reproduced. This is folder backup, not the Mac video's-only filter, media conversion, or complete camera-package certification.

The copy engine now accepts frozen videos/all-media and include-preview options and applies the baseline still/movie, companion, proxy and structured-package rules. Seven file fixtures cover selection/package behavior. The current window continues to use all-media with previews until the defaults/profile/selection UI milestone. Device indexes are backed up separately under `.easy-eject-metadata`, retained on source and distinguished from selected media in the result/receipt. Clean layout and verified repeat-import reuse remain pending; see the full parity checklist.

Files are opened without following reparse points. Directory handles pin path components; source file handles deny concurrent write/delete. The importer checks source stamps, copies to exclusive private files with write-through/flush, hashes source and destination with SHA-256, re-reads source, and renames the held verified destination handle without replacing an existing file. It checks the whole source manifest and re-hashes all final copies/sources before publishing the completion receipt. Cancellation/failure retains originals and already published copies, removes only the held unpublished private file, and does not mark the batch complete. Flush errors fail the operation; this does not prove a drive's power-loss behavior.

## Conservative limits and differences from Mac

- CFexpress can appear as NVMe/SSD storage. Folder evidence makes it a camera-media **candidate**, never confirmed CFexpress. Generic external SSDs remain visible; internal storage with DCIM stays internal.
- Volume GUID, filesystem serial, physical disk number/extents, PnP instance/devnode, removal target ancestry, and connection generation constrain actions. Disk numbers and drive letters alone are not identities. Session actions are not persisted across restart.
- The nearest removable PnP ancestor is considered; hub, PCI, ACPI, root, and docking boundaries are not removal targets. All known storage affected by that target participates in confirmation/reservations. The Windows API can remove multiple LUNs together; this differs from the Mac's sequential volume unmount.
- Unknown/spanning mappings, unavailable connection notifications, unreadable source identities, protected siblings, or incomplete inventory block operations. Empty/unsupported readers may conservatively block all operations until removed or successfully identified. Integrated readers without a suitable removable devnode may require Windows' own removal UI. No force-unmount fallback is implemented.
- Production imports reject same-disk partitions and different LUNs under the same removable target. Both source and destination disk groups are reserved. A device event conservatively cancels pending work, including unrelated device changes.
- Local drive-letter paths only, at most 240 characters per normalized path in this slice. UNC/device paths, alternate stream syntax, ambiguous components, symbolic links, directory junctions, and cloud-placeholder reparse points are rejected. Use an ordinary local destination outside OneDrive placeholders. No destination overwrite or deduplicated reuse is implemented.
- Notifications, driver-reported ancestry, descriptor layouts, safe removal, handle rename/delete, filesystem durability, high-DPI layout, and tray behavior require runtime/hardware acceptance. Read-only CIM observations do not certify these.
- PTP/MTP-only cameras are unsupported. There is no auto-import, auto-eject, original deletion, metadata cleaning, Trash recovery, Quick Share, hotkey, installer, updater, startup entry, packaging, Store submission, or public release.

## Architecture

`Models.cs` contains inventory models, classification, reservations, and eject policy; `Discovery.cs` provides read-only native inventory and connection generation; `Native.cs` holds P/Invoke and the Windows removal adapter; `SafePaths.cs` pins/revalidates file paths; `MediaSelection.cs` supplies frozen selection/package rules; `Importer.cs` handles verified copy, physical-device guards, receipts and profiles; `Program.cs` supplies the manual tray/device flow. `IDiscovery`, `IEjectApi`, and `IImportGuard` make safety policy and file fixtures independent of real-device actions.

## Microsoft references checked October 6, 2026

- [Volume disk extents](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_volume_get_volume_disk_extents) and [VOLUME_DISK_EXTENTS](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-volume_disk_extents): map volumes to all disk extents, including spanning storage.
- [Disk interface device number](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_storage_get_device_number): join disk interfaces with volume disk numbers.
- [Storage descriptor](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-storage_device_descriptor) and [hotplug information](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-storage_hotplug_info): distinguish media removability, device hotplug, bus and hardware metadata without equating SSD with permanent storage.
- [Default device-change notifications](https://learn.microsoft.com/en-us/windows/win32/devio/detecting-media-insertion-or-removal) and [RegisterDeviceNotification](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerdevicenotificationw): top-level notification window plus explicit disk interface notifications.
- [CM_Request_Device_EjectW](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_request_device_ejectw): supported removal request, veto reporting, and session/privilege limitations. The prototype does not elevate itself.
- [CreateFileW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew), [GetFinalPathNameByHandleW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew), and [SetFileInformationByHandle](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle): handle sharing, resolved-path checks, exclusive rename and owned-file cleanup.
- [Smart App Control signing](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control): trusted signing is the supported distribution path for protected computers; this local test blocker was resolved by Ryan.
