# Website 1.6.1: ask before ejecting after import

The per-device automatic-eject setting is now **Ask to eject after import**. Existing enabled profiles ask before ejecting; new profiles leave the optional prompt off. Saved profile bytes do not need migration.

A successful import (including verified copies already present) offers **Import complete. Eject now?** Empty selections, photos-only video selections, and skipped-preview/index-only runs offer **No media found. Eject now?** The prompt explains that other files may remain. **Keep Connected** is the default; only the explicit **Eject Now** response requests ejection. Ejection affects the full disk and all partitions. The app retains operation reservations while the choice is pending and rechecks source/destination and original physical disk identity after confirmation.

An absent recording folder is treated as empty only when a readable card scan confirms no other ordinary files. Inaccessible media and failed imports remain errors, not successful empty scans. The import selection, saved-copy verification and deletion opt-ins are otherwise unchanged.

Validation: 46 standard importer checks and 48 signed-sandbox checks passed; Website universal Release archive and Store universal Release build succeeded. Native prompt tests verified both titles, explanatory text, default button, and real modal return values for both buttons using synthetic dialogs, without operating on any disk. Website tests/build passed. Signed/notarized installer, installation, public and website verification receipts are stored in release-evidence/eject-prompt-2026-09-26. Existing Store build 8 submission is unchanged.
