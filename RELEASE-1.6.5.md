# Local 1.6.5: compact camera eject dialog

The multi-source dialog now uses the short title “Eject camera?”, two concise paragraphs, and Keep Connected / Eject Camera buttons. The shorter content allows the native compact alert layout with a consistent left text edge and stacked full-width buttons. It still names every source covered by the choice and warns that other storage may contain unimported files. The grouped unmount behavior and Keep Connected Return-key default are unchanged from 1.6.4.

Verified native installed dialog on the actual Internal + SD Card connection, Website archive/export and Store build, existing prompt/default-action checks. Installed locally as Developer ID signed 1.6.5; prior 1.6.4 preserved and profile bytes unchanged. No new public release, notarization, website change or Apple submission. Real camera ejection was not exercised by Codex. Evidence: release-evidence/compact-dialog-2026-09-28.
