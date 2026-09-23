# Website 1.5.4: clear import folder setup

Selecting DCIM previously opened a second folder chooser with no visible explanation, often still showing DCIM. Setup now labels the recording folder as step 1 and the save location as step 2, uses distinct action buttons, and starts a new destination choice in the Mac home folder. Changing a destination starts at the current folder when available. The review screen labels the saved-copy path and dated subfolders. Setup itself does not start an import.

Shared source applies to both editions. Website 1.5.4 is Developer ID signed, notarized and stapled; the existing Store submission is unchanged. Import behavior, stored profiles, original retention, permanent-deletion confirmation, Trash recovery and eject defaults are unchanged.

Validation: universal Website release archive and Store target build succeeded; 14 engine/cleanup tests passed. The installed Website app was checked with a generated disk image: numbered source and destination instructions, Mac home default, review screen, change-destination path, all four options off, and cancellation without saving. Production profile hashes matched before and after. The connected DJI volume disappeared during the initial check; no physical-device import or footage modification was performed.

Evidence: canonical workspace release-evidence/website-release-1.5.4. Previous installed 1.5.3 is retained under Library/Application Support/Easy Eject Release Backups.

DMG SHA-256: `6e336faccd586ad1cf210932f54296d6762db426d2ac6f9f29a8ca5100d944b2`.
