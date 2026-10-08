# Honor the saved post-import eject choice · October 8, 2026

Source **2b87e5bc3d7d3c43bd41c7eaf825e3446bf9fe5c** fixes a Windows routing mismatch: a hidden window with notifications enabled suppressed an eligible import's saved **Ask to eject after import** choice. The shared route now honors that frozen opt-in regardless of visibility or notification preference. Opt-out remains off by default; a prompt grants no automatic eject, original removal or Quick Share action.

The authoritative Mac baseline is **384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c**, `DroneImportManager.swift:302–312,352–390`. After releasing import reservations, its current-connection/opt-in branch invokes `showEjectPrompt`; the single/grouped prompt activates the app and requires an explicit button choice. It does not condition this call on visibility or notification eligibility. Current normalized manager blob **e2460d8b5eeefa010a31204590eaf3d297074351** matches the baseline. The Apple notification permission/category matrix is separate from this actual call path.

Windows `Program.cs` and modern `MainWindow.Imports.cs` already route through `CompletionPresentation.ShowChoice`. The modern caller releases operation state, awaits visibility and opens its fresh process-bound review; both callers only register eligible current-source choices. This change preserves those connection, group, reservation, pending-token and fresh explicit-action checks. Existing Keep Connected default/Enter/Escape and manual sharing choices remain.

The two new `CompletionPromptContractTests` groups reproduce **0/2** against unchanged committed production source with only test overlays. Fixed focused Framework **2/0**; fresh full suites **1363/0 each runtime**, warning-free WinUI app and selected modern **242/0**. The new groups cover opt-in/opt-out across all visibility/notification combinations, and actual selected/filtered generated-file imports with persisted preferences. A later saved ask-off change cannot alter the completed import's frozen ask-on choice; it applies to the next import. Originals/verified copy remain exact, Keep Connected remains the default, and the explicit fake request consumes one current completion exactly once. The filtered branch completes in the fixed run; the combined baseline group stops at its first selected-import route failure.

Source is isolated **3ad6db6** plus three exact overlays, outside OneDrive. All three compiled normalized blobs match the source commit. App, selected runner, full runner and backend output share runtime SHA256 **9BC8FC4230BBAAC0C9056F8602EB105F6C1210226B28A22977271CADEF84515B**. Framework/modern full terminals **2854/77230 exit0**; owned processes zero. New Debug builds are not a new bundled review delivery or module/native acceptance observation. No actual camera/media import, eject, cleanup, supplied tool, normal configuration or system-setting action occurs in these tests.

```powershell
& <framework-or-modern-full-runner.exe> --completion-prompt-contract
& <framework-full-runner.exe>
& <isolated-source>/Windows/modern/build.ps1 -Test -FullTest -BuildRoot <outside-OneDrive-output> -CacheRoot <existing-private-cache>
```

Private `%USERPROFILE%/.codex/EasyEjectBuildTools`: `CompletionPromptBeforeSource-20261008` / `CompletionPromptBeforeFramework-20261008`, retained **0/2** comparison; `CompletionPromptFinalSource-20261008`, `CompletionPromptFinalFramework-20261008`, `CompletionPromptFinalModern-20261008`; source/result JSON, compiler/focused logs, `completion-prompt-framework-default-20261008.log`, `completion-prompt-modern-build-full-20261008.log`, and `completion-prompt-final-result-20261008.json` with hashes, terminals and scope.

Actual hidden-window reveal, foreground Enter/Escape/click, concurrent dialog/activation/notification/suppression and identified-device physical removal remain acceptance gates. Earlier native receipts retain their original candidate scope; this backend change has not been exercised in that live app. Remaining full-port media/official-LUT/hardware/filesystem/travel/trusted-distribution gates stay open; goal active, expired heartbeat paused.
