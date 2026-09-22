# Store 1.0 (5): compact, dismissible issue notices

- Idle failures no longer expand the menu bar with “Import needs attention.” The warning symbol and usual card count remain; active transfer progress is unchanged.
- A compact View Issue menu action opens the import window. The full error explanation is shown there instead of widening the eject menu.
- Dismiss acknowledges the current issue and restores the normal menu-bar icon. “Last issue (dismissed)” retains the explanation and does not claim success. Dismissal does not retry, clear retry bookkeeping, alter saved profiles, enable automatic behavior, or modify files. A later failure restores the warning. Dismissal is ignored during active work.

Validation: universal Release archive succeeded with preserved Xcode 26.6 RC2. A separately signed sandbox app compiled all current production sources, replacing only the @main entry point with Tests/StorePrototype/IssueNoticeProbe.swift. Assertions passed for compact idle title, retained message/failure phase, unchanged profiles/no retry, refresh preserving dismissal, same/new failure rearming, busy guard, and success resetting acknowledgement. Native UI confirmed View Issue opens the details, Dismiss clears the menu action and preserves the explanation. No real media operations ran and production profiles were unchanged. Evidence: release-evidence/store-issue-release.

The import engine, permissions, ejection policies and existing menu/permission listing screenshots are unchanged. Existing build-4 screenshots remain accurate for normal operation.

## Completed release

Binary source `68a7f4b`. Installed `/Applications/Easy Eject.app` Store 1.0 (5), signed sandbox archive, notarized (c7bb6cae-9ce5-4794-b00d-983d9f82c735, Accepted), stapled and spctl accepted. Only installed copy running; production profile bytes unchanged. Build 4 retained under evidence `retired-build4/`.

Apple build `e4134845-1466-4003-abbb-f8426849a6b0`, VALID/APP_STORE_ELIGIBLE. Review `6c1e00aa-62f3-452d-92b0-0ab3c2007c1a` submitted `2026-09-22T20:59:48.519Z`, verified WAITING_FOR_REVIEW/AFTER_APPROVAL. Build-4 review cancelled. Existing menu/permission screenshots retained and COMPLETE. Submission is not Apple approval or public availability. Website unchanged.
