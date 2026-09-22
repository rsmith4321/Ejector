# Easy Eject Store prototype

Separate target `EjectorStore`, branch `codex/app-store-prototype`, based on released full edition `04f642d15f45a82eae67395a84493b2e9584112c` (1.5.2). This is a locally signed feasibility prototype, not an App Store release or approval.

## Design

The Store target shares `MediaImportEngine`, `ImportVolumes`, `DroneImportManager`, and the import/profile views. `APP_STORE` excludes the original app shell and metadata cleaner. `StoreApp` provides a standalone menu bar app, ordinary onboarding, neutral Website / Compare editions link, registered Carbon hotkey, and opt-in `SMAppService.mainApp` login registration. No updater, license-acceptance gate, donation prompt, whole-volume cleaner, or downloaded helper is compiled into its shell. The Store engine refuses Trash recovery even if profile data requests it.

Bundle ID: `com.ryansmithphotography.EasyEject.storeprototype`. Product name: **Easy Eject Store Prototype**. App Sandbox puts profiles and logs in this bundle's own container, separate from the released app. The prototype is never installed over `/Applications/Easy Eject.app`.

Source and destination folders are explicitly selected through `NSOpenPanel`. Both persist app-scoped read/write bookmarks. `ScopedFolder` resolves with security scope, without UI or implicit mounting, starts one access claim, refreshes stale data while access is active, and releases exactly once through an idempotent locked close or deinit. Import work retains both claims through the background copy and asynchronous eject callback. Failed resolution directs the user to reconnect or authorize again; stale refresh persistence failure blocks import. The resolved source must still be inside the enrolled volume UUID, and destination identity and physical-disk separation are rechecked. Full Disk Access is neither requested nor treated as a sandbox escape.

Defaults: automatic importing off, original deletion off, eject after successful import on. SHA-256, stable-source checks, storage flush, exclusive rename, collision handling, progress and cancellation remain shared with the full edition. Enabling original deletion presents the permanent-deletion warning and developer disclaimer; Return chooses Cancel. Per-file verified deletion cannot roll back earlier deletions if a later file fails. Users should retain originals and keep independent backups.

The menu bar uses the full edition’s eject symbol at 18 points with semibold weight. Its idle count includes hardware-recognized SD/CFexpress/XQD cards and mounted enrolled air units, counting a multipartition physical disk once. It does not scan unauthorized folders or count all backup drives as cards. Import progress and errors take precedence over the idle count.

The fixed prototype shortcut **Control-Shift-Command-J** opens the eject menu, where the user chooses a disk. It does not perform bulk ejection. This combination avoids the full edition's default shortcut. Registration failures are visible. No Accessibility event tap or Input Monitoring request is used. Eject uses `FileManager.unmountVolume` with `allPartitionsAndEjectDisk` and `withoutUI`; it does not force an unmount. Ejecting one partition also ejects its siblings. Turn automatic eject off when other partitions need separate imports.

## Comparison

| Capability | Store prototype | Free website edition 1.5.2 |
|---|---|---|
| Enrolled-folder imports, dates, collisions, SHA-256 and durability gates | Yes, selected source and destination only | Yes |
| Retain originals by default | Yes | Yes |
| Optional verified source deletion | Authorized source folder only, explicit warning | Yes, explicit warning |
| Automatic import after reconnect | Observed with mounted images and real air-unit storage | Existing full implementation |
| Manual and post-import eject | Observed with single/multipartition images and real air-unit storage | Existing full implementation |
| Device Trash recovery | Omitted | Available with required permissions |
| Whole-volume metadata cleanup | Omitted | Available |
| Global shortcut | Registered shortcut opens eject menu | Accessibility-dependent eject shortcut |
| Launch at login | Opt-in SMAppService | Opt-in SMAppService |
| Updates | Future App Store distribution | GitHub updater |
| Website link | Neutral comparison link | Website/donation UI |

## Build

From this worktree, using existing signing facilities:

```sh
xcodebuild -project Ejector.xcodeproj -scheme EjectorStore \
  -configuration Release -derivedDataPath /Users/ryansmith/Easy-Eject-Review/store-prototype/build \
  CODE_SIGN_IDENTITY='Developer ID Application: Ryan Smith Photography, LLC (MCJMHBLT27)' \
  CODE_SIGN_STYLE=Manual build
```

This Developer ID signature is for local prototype testing. A future Store submission requires the appropriate App Store signing, registered bundle identity, archive, metadata, privacy disclosures and review. No submission, push, public release, or notarization of this prototype was performed.

Release entitlements are exactly:

- `com.apple.security.app-sandbox = true`
- `com.apple.security.files.user-selected.read-write = true`
- `com.apple.security.files.bookmarks.app-scope = true`

No `get-task-allow`, networking, device USB, Accessibility, temporary exception, or broad filesystem entitlement is present in the Release product.

## Verification and limits

See [VERIFICATION.md](VERIFICATION.md) for observed runtime outcomes and precise remaining checks.

Apple sources checked during implementation:

- [App Review Guidelines](https://developer.apple.com/app-store/review/guidelines/) for Store distribution constraints. The neutral developer website link remains subject to App Review; it is not an outside updater or unlock.
- [Bookmark resolution](https://developer.apple.com/documentation/foundation/nsurl/urlbyresolvingbookmarkdata:options:relativetourl:bookmarkdataisstale:error:) for explicit security-scope resolution and replacement of stale bookmarks.
- [Scoped access lifetime](https://developer.apple.com/documentation/foundation/url/startaccessingsecurityscopedresource()) for balanced start/stop access.
- [All-partition ejection](https://developer.apple.com/documentation/foundation/filemanager/unmountoptions/allpartitionsandejectdisk) for eject behavior.
- [SMAppService registration](https://developer.apple.com/documentation/servicemanagement/smappservice/register()) for user-controlled login registration.
- [Apple's hotkey modifier guidance](https://developer.apple.com/forums/thread/763878) for registered shortcuts on modern macOS. This combination contains both Control and Command.
