# Easy Eject App Store edition

Target `EjectorStore` shares the verified import engine with the website edition. The Store app is a useful standalone sandboxed Mac utility. It has no updater, donation UI, license gate, broad metadata cleanup, downloaded helper, or device Trash recovery.

## Identity and distribution

- App Store Connect app: **6767951388**, Easy Eject. Reuses the existing empty listing; no duplicate app record.
- Permanent bundle: `com.ryansmithphotography.EasyEject.store`, registered ID `XX33FPA5TF`, team `MCJMHBLT27`.
- Initial Store version **1.0**, build **2**. Product file `Easy Eject Store.app`, visible window/title Easy Eject.
- Website app remains `com.ryansmithphotography.Ejector`, version 1.5.2. Do not overwrite `/Applications/Easy Eject.app`.
- Sandbox profiles/logs live in the Store bundle's own container. No prototype or website profile is migrated automatically.
- Listing is free with automatic release **AFTER_APPROVAL**. Upload, processing, review submission, approval, and live availability are distinct gates.
- Submitted September 22, 2026 at 04:09:47 UTC: version 1.0 (2) is **WAITING_FOR_REVIEW**. Binary commit `6bc1fbb`; build `17a69278-1ae0-4e9d-b05d-8ac0311ec2f0`; review submission `1f29516c-55a7-4b40-bb95-9a840a0ee52a`. Approval and public Store availability remain pending.

## Permissions and safety

`NSOpenPanel` grants only selected source and destination folders. `ScopedFolder` resolves app-scoped bookmarks, refreshes stale data, owns one access claim, and releases it after the worker and eject callback complete. Invalid permissions fail closed and can be renewed in Edit. Source volume UUID, destination identity, and physical disk separation are rechecked.

Automatic importing and original deletion start off. Originals are retained by default. Enabling deletion requires a permanent-deletion warning with Cancel as the default. Per-file saved-copy verification precedes deletion; earlier deletions cannot be rolled back if a later file fails. Keep independent backups.

Eject uses non-forced `FileManager.unmountVolume` with all partitions and no implicit UI. Busy-device failure is visible. Source and destination disks are protected while importing. Automatic eject pauses if another enrolled partition shares the physical disk, or coordination identity is unavailable. Manual eject remains explicit. Reconnecting clears prior safe-to-unplug status.

The menu bar uses the original shipping eject.fill glyph at the native unstyled symbol size and regular weight. Recognized SD/CFexpress/XQD hardware and enrolled air units are counted once per physical disk; progress/errors override the idle count. Control-Shift-Command-J opens the eject menu using a registered Carbon hotkey. Registration failures are visible. Launch at login is opt-in through SMAppService. Reopening the app restores its imports window.

Release entitlements:

- `com.apple.security.app-sandbox`
- `com.apple.security.files.user-selected.read-write`
- `com.apple.security.files.bookmarks.app-scope`

No networking, USB, Accessibility, broad filesystem, temporary exception, or release debugging entitlement. The privacy manifest declares local preferences, container/user-selected file metadata, and disk-space checks. No data collection or tracking.

## Build and submit

Use an Apple-accepted production toolchain. The release was archived with separately extracted Xcode 26.6 RC 2 **17F113**, macOS 26.5 SDK. Apple's [release notes](https://developer.apple.com/help/app-store-connect/release-notes/) explicitly accept this toolchain. Installed Xcode 27.1 **27A9269** is beta and is not the release toolchain.

Archive `EjectorStore` for `generic/platform=macOS`, Release, with automatic signing and the existing App Store Connect key supplied by path. Export with `Store/ExportOptions-AppStore.plist`. Never print the private key or a JWT. Confirm the exact bundle/version/build before upload. Do not run ShootCal's release scripts unmodified.

Before submission, inspect fresh ASC state, wait for the exact build to become VALID, attach it to version 1.0, and verify screenshots, privacy disclosure, age rating, free pricing, territory availability, review contact, and AFTER_APPROVAL. Submit one review item, then verify Apple's returned submission/version state. Never duplicate an uncertain submission.

Public resources: [support](https://easyeject.com/support/), [privacy](https://easyeject.com/privacy/), [neutral comparison](https://easyeject.com/editions/). None advertises Store availability before approval.

## Verification

See [VERIFICATION.md](VERIFICATION.md). Earlier prototype hardware evidence belongs to the historical `storeprototype` identity and must not be presented as final Store validation.

- `Tests/StorePrototype/run-sandbox-tests.sh /absolute/output/path`: 15 signed sandbox engine tests.
- Compile `Ejector/StoreEjectPolicy.swift` and `Tests/StoreEjectPolicyTests.swift` with `swiftc -D APP_STORE`: 9 physical-disk coordination cases.

Local release evidence: `/Users/ryansmith/Easy-Eject-Review/app-store-release/`. Final submission status is recorded there after checking the provider. A successful build alone does not establish App Review acceptance or live availability.
