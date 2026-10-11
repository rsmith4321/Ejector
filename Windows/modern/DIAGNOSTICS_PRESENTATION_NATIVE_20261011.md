# Native diagnostic presentation cancellation and worker lifetime · October 11, 2026

Cancelling the actual WinUI `ContentDialog.ShowAsync` operation during a real diagnostic Refresh removes the dialog and raises `TaskCanceledException`. The existing production cleanup keeps the exact worker/view and global review ownership until the read finishes. Actual tray Quit refuses during that wait; fresh review and normal shutdown then recover. This proves native presentation cancellation, not every XAML rendering failure.

## Engineering boundary

Normal behavior is unchanged. `#if UI_TESTS` substitutes a wrapper around the same real `ShowAsync` operation and records cleanup transitions. Only an explicit owned marker enables cancellation inspection; otherwise it directly awaits the original dialog. After actual native Refresh starts its existing controlled real-read pause, an owned trigger calls `Cancel()` on that actual WinUI operation. No synthetic exception replaces the operation. The real outer catch/finally, `DiagnosticsReview`, file read, issue persistence, busy admission and tray Quit remain in use. Normal metadata excludes the wrapper/probe and prior engineering hooks.

Actual PID **27040** runs `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-debug-presentation-ui-20261011\bin\EasyEject.Modern\Debug\net10.0-windows10.0.26100.0\win-x64\EasyEject.Modern.exe`. Fresh fixture: `C:\Users\rsmit\.codex\EasyEjectFixtures\ui-debug-presentation-20261011`, seeded solely from prior generated debug settings/log/review marker. Modern/Runtime/installed .NET10.0.0 coreclr paths/hashes are recorded. Normal Release app is not launched. Read-only storage inventory still shows five internal/protected volumes on one disk, no external camera/card.

| Native action / transition | Observed result |
| --- | --- |
| Pause next debug Refresh → ordinary Debug log → actual Refresh | Real controller enters Busy at **19:45:59.8749929Z** and holds before reading the generated log. Screenshot shows working/disabled diagnostic actions. |
| Cancel actual ShowAsync at **19:46:00.4533055Z** | Native operation returns from Cancel and raises **System.Threading.Tasks.TaskCanceledException** while owner Busy=true. Dialog disappears; real outer catch reports retained diagnostics and saves an unacknowledged issue. |
| Existing finally enters at **19:46:00.5175397Z** | WorkerCompleted=false, owner Busy=true, saved-review Working=true. Main diagnostic/update/refresh controls stay disabled despite the missing modal. No early disposal or second action admission. |
| Actual Test tray Quit during cleanup | App remains alive and reports “Wait for the operation to finish or cancel it before quitting. Easy Eject stays in the tray.” Busy controls remain; observation precedes real read completion. |
| Release exact Refresh marker | Actual generated-log read completes at **19:46:22.2672179Z**, Busy=true inside callback,416 characters. Finally resumes only after its exact task terminates; then owner Busy=false/Open=true, followed by Dispose/Working=false/no confirmation at **19:46:22.2732994Z**. Controls recover. |
| Retire cancellation marker → fresh ordinary Debug log | Normal viewer opens with all retained generated text (whitespace-normalized native value comparison) and normal Refresh/Copy/Clear availability. Close succeeds. No Copy or Clear invoked. |
| View issue → Cancel | Needs attention still shows the cancellation explanation; saved Dismissed=false remains. No operation is retried or issue acknowledged. |
| Settled idle → actual tray Quit | Exit **0**, no `unhandled.txt`; no repeat input after unusable-window capture. |

Immediate accessibility text can lag screenshot rendering after input; settled observations and transition chronology are used before further actions. In particular, immediate Quit capture's tree retains the prior Summary while its screenshot shows the refusal; subsequent recovered tree contains that same Quit message. The message remains visible after controls recover; this evidence does not treat it as an ongoing busy state. Screenshots are inspected directly and not saved/reprocessed.

## Verification and preservation

- `Codex020-debug-presentation-ui-20261011.log`: **0 warnings/0 errors**, existing automated native root/theme/text/completion/pending/presenter/Stop/tray/editor/busy/idle regressions PASS; fixture `ui-74006e70dd6e43f9a8e18677dc6758ad`, no exception.
- `Codex020-debug-presentation-normal-20261011.log`: **0 warnings/0 errors**, selected **246 passed/0 failed**. Normal PE retains production methods and excludes `ShowReviewDiagnostics`, `RecordReviewDiagnosticsLifetime`, host/backend/pause/activity/diagnostic probes. No full-suite/tool-count or binary-identity claim.
- Lifetime trace has exactly CancelRequested, CancelReturned, FinallyEntered, WorkerTerminal and Disposed. Before release, only the first three exist; actual worker/read/Quit/cleanup times prove the wait interval and release ordering.
- Both live records and the isolated seed settings/log retain exact hash/length/write time. The isolated Profiles has exactly those two files plus the expected166-byte unacknowledged `issue-state.json`. No log deletion, Clipboard change, settings Save or real media/device action. Cancellation marker is renamed ended; original evidence is retained.
- Login registration absent and Mac baseline384cb767 exact. Production/package source remains **0040e58**; unsigned ZIP stays **107459831 bytes**, SHA-256 **e941e2c49a9bb88d65d6d24234e6cd2fbcd711350ef2c7cf43105a7e56198bb4**. No OS setting, install, autostart, push or publication change.

Evidence root: `C:\Users\rsmit\.codex\EasyEjectBuildTools\Codex020-debug-presentation-native-20261011`. Launch/finalizer scripts, actual native observations, exception/worker/lifetime records, process/modules/exit and preservation pins remain. Aggregate `final.json` SHA-256 **3feaf138021484341cfeb6b423b227dbef04bdefefef5d0097e59aa80efa62bc** verifies the scoped assertions.

Native Clear/clear-failure and general XAML rendering/worker failures remain open. Native ShowAsync cancellation now has actual failure-path/lifetime evidence; this does not substitute for physical slow I/O, genuine-media/official-LUT/travel/display/configured-update/normal-loader/signing/publishing acceptance. Full goal active.
