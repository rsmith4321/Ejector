# Selected-source results before grouped removal · October 8, 2026

Source **8f34d85aaf8f3c3da1e23a14fad13f80af020761** adds the missing grouped-completion explanation to both interfaces. A result from one storage source now names that captured source and says **“Other storage may still have files to import.”** The affected-volume list remains explicit. The modern grouped action now uses **Eject Device**, matching the classic interface; Keep Connected remains the default, and cleanup retains its existing explicit label/action.

Mac baseline **384cb767ffbbb87ec5fe67f45d0b4d0ce324ad7c**, `ImportEjectPrompt.swift:12–17`, distinguishes the selected source's result from the grouped removal scope and warns about other storage. Current normalized prompt blob **91c0a6d03a34866edd83db3411f5258c42d73d4d** matches that baseline. Windows removal remains its existing exact native parent-device plan; this change adds no grouping, source selection, removal authority or automatic action.

`ImportCompletionOrigin.SourceDisplay` captures the volume display string before import, alongside the existing process-only immutable evidence. It is presentation data, not a new identity field or persisted grant. The shared `CompletionPresentation` helpers provide the other-source explanation and grouped/single action label. Classic `ImportCompletionDialog` and modern `ReviewImportCompletion` use both helpers; current guards and affected sources are unchanged.

Two new generated-file/hidden-control groups reproduce **0/2** against unchanged committed production source; fixed **2/0 on Framework and modern .NET**. Each fixed group covers two partitions on one disk and two LUNs under one removable parent. Selected-source video copying or filtering does not import or change the sibling video; source photo/video and verified copy remain exact. The body names Internal and SD Card, warns that other storage can still have files, keeps Enter/Escape on Keep Connected and retains the original source display after a later label edit. The baseline groups stop at their first partition-description failure; later LUN branches are observed in the fixed runs, not claimed reproduced before.

Existing prompt **2/0**, completion-connections **17/0** and defaults **4/0** pass on each runtime. Warning-free WinUI app and selected modern **242/0**, terminal **10908 exit0**. Isolated **6465154** plus six exact overlays; all compiled normalized blobs match the source commit. Four modern runtime copies share SHA256 **DC183E75B00718477DDF14FC21B028E588EC896C28744D97A268977575321E21**; owned processes zero. No fresh full default suite is claimed; the prior **1363/0 each** belongs to the separate `2b87e5b` prompt-routing milestone.

```powershell
& <framework-or-modern-full-runner.exe> --grouped-completion-prompt
```

Private `%USERPROFILE%/.codex/EasyEjectBuildTools`: `GroupedPromptBeforeSource-20261008` / `GroupedPromptBeforeFramework-20261008`, retained comparison; corresponding `GroupedPromptFinalSource`, `GroupedPromptFinalFramework`, `GroupedPromptFinalModern`; `verify-grouped-prompt-{before-framework,final-framework,modern-focused}-20261008.ps1`, source/result JSON, compiler/grouped/related logs and `grouped-prompt-final-result-20261008.json`. Framework final command **bd00ca exit0** and modern focused command **7cc51d exit0**; no new foreground app launch or normal configuration change.

Actual live grouped-camera prompt, native DPI/keyboard/click, identified-device multi-source/LUN safe removal/veto/reconnect, official-LUT/genuine-media/filesystem/travel and trusted-distribution acceptance remain open. Concurrent untracked native tools are preserved and excluded. Full goal active; expired heartbeat paused.
