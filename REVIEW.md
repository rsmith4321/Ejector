# Easy Eject 1.5.0 implementation review

Reviewed against version 1.4.2 (`6072491`).

## Existing code reviewed and corrected

- DriveManager was owned by the menu view. Monitoring, notifications, and shortcut setup could depend on the menu lifecycle. It now has one app-lifetime owner.
- Concurrent eject requests were not serialized per physical disk. Manual actions now reserve their disk and reject duplicate requests; imports reserve source and destination disks before background work.
- Clean & Eject silently returned zero on an unreadable directory and ignored deletion failures, then reported a successful cleanup/eject. The cleaner now throws on scan/delete/identity failures, surfaces the error, and stops ejection.
- Cleanup recursively removed entire `__MACOSX` folders, potentially including unrelated user files. It now removes only known metadata files and empty `__MACOSX` directories, skips symlinks and protected system directories, and verifies the volume identity during traversal.
- Eject failure invoked a synchronous NSWorkspace fallback on the main thread and could hide failure unless debug logging was enabled. The native asynchronous failure is now visible; there is no force-eject fallback.
- Classification cached only mount paths, allowing a replacement volume at the same path to inherit the old classification. Cache keys now use volume identity; generic MISC/CONTENTS names alone no longer identify camera cards, and camera-folder checks require a directory.
- Event taps disabled by macOS are restarted, and repeated keydown events no longer initiate duplicate ejects.
- A configured shortcut letter A (virtual key 0) is no longer mistaken for a missing preference and replaced by E.
- The global shortcut ignored the saved Clean Cards Before Ejecting preference. It now passes that preference to the shared bulk-eject path.
- The deployment target was unnecessarily 26.4. Version 1.5.0 builds against a macOS 14 minimum. This is compile-time compatibility, not a completed macOS 14 runtime test.

## New importer boundaries

Profiles require explicit enrollment and pin both source and destination volume identities. Automatic import, source deletion, and Trash recovery default off. There is one import queue, with cancellation between chunks and guards before destructive actions. File copies use temporary files, storage flush, SHA-256 checks, and an exclusive atomic commit. Errors and changed/new source files prevent successful completion/eject. Per-file verified deletions are not rolled back if a later file fails.

Volume identity checks clear URL resource caches so reconnect checks do not rely on cached UUIDs. Reused destination files pass the same storage-flush gate as new copies. Preview builds keep profiles and logs separate from the installed app.

An unavailable destination or another synchronous setup failure on one profile no longer stalls other connected devices. The automatic queue continues scanning eligible profiles until an import actually starts.

An enabled standalone O4 helper blocks Easy Eject imports, preventing two importers from handling the same device. The native app does not disable or delete helper files itself. Device detection is filesystem based, not a claim that every air unit or USB mode is supported. Normal imports copy every regular file in the selected media folder, including unfamiliar RAW and sidecar formats, while skipping hidden items.

## Verification completed

- Debug, Developer ID-signed preview, and universal Release builds succeeded. The release contains arm64 and x86_64 slices, and its code signature passed strict verification.
- 14 isolated tests passed: nested copy/verify/delete, copy-only/reimport, collisions, RAW and unfamiliar sidecars, missing destination, cancellation/partial cleanup, changed volume validation, audit failure, source and destination symlinks, Trash recovery, arriving media, changed retained originals, and cleanup preservation.
- Mounted-volume integration: a 102,144,000-byte disposable file was copied from an HFS+ disk image, SHA-256 verified, removed from source, and the source ejected through the same native FileManager API. All copy/verification phases were observed.
- After adding uncached volume checks and a flush for reused copies, the mounted-volume test passed again with a different 38,000,000-byte file, preserving the earlier destination file through collision handling and confirming native eject.
- Native enrollment was completed through the UI, selecting a media folder and a destination on a different disk. Folder pickers use attached sheets. The app visibly blocked importing while a conflicting standalone helper was enabled.
- The installed native app manually copied and verified three real DJI O4 recordings totaling approximately 5.2 GB, retained the originals, and successfully ejected the O4. Native progress and the final success state were observed. The audit log recorded the SHA-256 checks and completion.
- Reconnecting the O4 triggered the installed notarized app automatically. It reverified the three saved recordings, removed each verified original, completed the enabled device Trash check, and ejected the device. The audit log and native success window confirmed completion. Nonempty Trash recovery was covered by the isolated tests.
- Apple accepted notarization of the app and DMG. Stapling, strict signature verification, disk-image checksum verification, and Gatekeeper assessment passed for the release package.

## Still open

- Other physical air units, Intel runtime, and macOS 14 runtime remain untested.

This review is a code inspection with targeted tests, not a guarantee against all filesystem races, hardware failures, or third-party ejection. External apps and physical disconnection can still interrupt an import; remaining originals are preserved when verification cannot finish.
