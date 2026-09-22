# Store 1.0 (5): compact, dismissible issue notices

- Idle failures no longer expand the menu bar with “Import needs attention.” The warning symbol and usual card count remain; active transfer progress is unchanged.
- A compact View Issue menu action opens the import window. The full error explanation is shown there instead of widening the eject menu.
- Dismiss acknowledges the current issue and restores the normal menu-bar icon. “Last issue (dismissed)” retains the explanation and does not claim success. Dismissal does not retry, clear retry bookkeeping, alter saved profiles, enable automatic behavior, or modify files. A later failure restores the warning. Dismissal is ignored during active work.

Validation: universal Release archive succeeded with preserved Xcode 26.6 RC2. A separately signed sandbox app compiled all current production sources, replacing only the @main entry point with Tests/StorePrototype/IssueNoticeProbe.swift. Assertions passed for compact idle title, retained message/failure phase, unchanged profiles/no retry, refresh preserving dismissal, same/new failure rearming, busy guard, and success resetting acknowledgement. Native UI confirmed View Issue opens the details, Dismiss clears the menu action and preserves the explanation. No real media operations ran and production profiles were unchanged. Evidence: release-evidence/store-issue-release.

The import engine, permissions, ejection policies and existing menu/permission listing screenshots are unchanged. Existing build-4 screenshots remain accurate for normal operation.
