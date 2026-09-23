# Website 1.5.5: properly sized folder dialogs

Folder choosers now start at 800 by 600 points instead of inheriting an excessively wide, shallow saved size. Native resizing remains available. The same factory covers recording folders, import destinations, source reauthorization and card authorization. Card-authorization instructions use short lines so the message does not stretch the panel.

The change uses AppKit setContentSize and does not clear preferences or modify profiles, permissions, import behavior or retention settings. Shared source builds for both editions; the existing App Store submission is unchanged.

Verification: isolated native panel seeded with the reported 1588 by 477 preference opened at 800 by 600. Website universal archive and Store build passed, along with all 14 engine/cleanup tests. Release evidence is in the canonical EasyEject workspace under release-evidence/website-release-1.5.5.

Installed native source and destination dialogs were visually checked at 800 by 600. Production profile hashes unchanged; no profile saved or import run. App and DMG notarization accepted and stapled. Public DMG SHA-256: `9693f3ce48a27a95b59cfb15a02aecc02f71914298f1e8d74b45627d6666dc5c`.
