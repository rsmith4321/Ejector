# Website 1.6.2: eject from the import notification

With **Ask to eject after import** enabled, successful imports and no-media selections now post one actionable notification with **Eject Now** and **Open Import Folder**. macOS may display the actions under Options. Ignore or dismiss it to keep the device connected for Lightroom. Clicking the notification body opens Device Media Imports without ejecting.

If app notifications, macOS alerts or the alert style are disabled, the existing popup appears with **Keep Connected** as the default. Scheduling errors also fall back to the popup. Focus can hide allowed alerts, so ejection remains available in the menu and through a persistent per-device Eject Now button in the import window.

Import reservations and folder permissions are released after completion, without waiting for notification interaction. Eject actions are bound to an in-memory completion token, source UUID, physical disk and connection. Disconnect/reconnect, reimport and process restart expire old actions. Busy disks are protected and an accepted action is consumed once. Notification payloads cannot reconstruct eject authority. Ejection affects all partitions on the source disk; it never runs just because a notification is dismissed.

Validation: 101 routing/identity/presentation checks, 46 importer checks and 48 signed-sandbox checks passed. A signed integration fixture drove the actual importer and native fallback popup, verified the copied bytes and retained source, superseded an old action with another import, then exercised the notification handler to eject its disposable disk image and reject a duplicate click. Both universal Website and Store Release targets compiled. Ten website checks and the docs build passed. Native Notification Center button interaction, Focus behavior and physical-camera ejection are not hardware-tested by this fixture.

Existing folder-layout settings, original-retention options and real media remain unchanged. The submitted Store build 8 is unchanged. Release signing, notarization, installation and live site receipts are in release-evidence/import-notifications-2026-09-26.
