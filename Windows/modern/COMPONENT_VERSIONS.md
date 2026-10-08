# Held component version consistency · October 8, 2026

Source **64a3138190f23560ab6275c1546d2416aa1c0804** compares existing authenticated dependency file/assembly version declarations with actual held component files. Previously, complete inventory authentication and correct application identity could admit a component whose declared version differed from its bytes. This adds version consistency; complete vendor code/catalog authenticity remains a separate gate.

The strict dependency parser returns immutable per-asset version records only after validating the entire graph. Composed application review reads the held declarations again and inspects their unique flattened destinations. Batch inspection checks the whole held package before/after the batch, individual file identities around each callback, cancellation, membership and restored stream positions. Version-resource and assembly-manifest APIs receive absolute paths bound to those held handles, with identity checked before and after their reads. No candidate code is executed.

Windows numeric file-version fields are compared rather than localized strings. Resource size is bounded to one MiB. The observed SDK payload includes a zero file-version declaration with no version resource (`System.Diagnostics.EventLog.Messages.dll`); zero absence remains lack of version evidence and never grants code authenticity. Its declared assembly version is checked separately. Assets without a declared version remain outside that kind of proof.

## Tested scope

Isolated baseline **851f704** plus seven owned source/test overlays, outside OneDrive. This includes the notification source checkpoint but excludes subsequent actual-tool crash tests **5dcc6b5** and current foreign busy-crash work.

- Unchanged before-fix production plus the new composed tests reproduces **7 passed/2 failed**: file and assembly mismatches were admitted. The negative fixtures dispose unexpected reviews, so failures are the actual missing refusals.
- Framework and modern app/selected/full compilation use warnings as errors. Modern selected **242/0**; Framework and modern full default **1350/0 each**, both report no real eject API used. Full terminals **5731/25058 exit zero**. Counts overlap.
- Component/batch focused **5/0** on both runtimes: correct actual fixture versions/no candidate load, independent mismatches, path/cancellation/malformed refusal, frozen unique batch membership/read-only handles/restored positions and late-layout/disposed ownership refusal.
- Read-only actual sample **1/0** on both runtimes inspects all **258 declared assets**, with **257 file-version declarations** and **236 assembly-version declarations**. Each actual file remains hash-identical and candidate assemblies are not added to the domain. This uses synthetic release declarations for an unsigned development folder, not authenticated vendor approval.
- Final composed sample **9/0**, archive/channel/release samples **2/0 each** on both runtimes. Adjacent main/backend/runtime checks remain green. Modern focused terminal **56894 exit zero**; Framework focused logs complete all modes, but its terminal handle **59603** was unavailable on final collection, so no parent exit code is claimed for that follow-up. Earlier Framework component/composed terminal **51890 exit zero** remains recorded.
- App, selected and full default backend SHA **93F93739E83F5A0E8CEDB44C4BBEE6B5BEC73933F949CEFD73C96DA6AC947E89**. The separately rebuilt final sample runner initially differed (**A4C055A5…**); only its owned runtime copy was replaced with exact app bytes before final modern focused execution. These are Debug/installed-runtime checks, not a new self-contained publication or loader/module acceptance claim.
- Original **601-file** `IntegratedReview-20261008/app` stays unchanged and unlaunched. Final owned component test/app process count is zero; foreign apps/tasks are preserved.

Earlier failures are retained: the first Framework actual sample refused the SDK's absent-resource zero declaration; final behavior explicitly represents that absence. The initial modern sample's name-only no-load assertion confused newly loaded installed framework DLLs with candidate files. The final assertion uses actual assembly locations; diagnostics identify `System.Diagnostics.FileVersionInfo`, `System.Reflection.Metadata`, `System.IO.MemoryMappedFiles` and `System.Collections.Immutable` under the installed .NET10.0.0 runtime, never the sample folder. This final correction affects only the opt-in sample method; production/default bodies stay unchanged and final focused runs cover it.

## Reproduction and retained evidence

Under `%USERPROFILE%/.codex/EasyEjectBuildTools`: `ComponentsBeforeSource-20261008`, `ComponentsAfterSource-20261008`, `ComponentsAfterFinalSource-20261008` and final `ComponentsVersionsFinalSource-20261008`. Their JSON overlay/hash records distinguish initial production, the absent-resource correction and the sample-only location correction.

```powershell
& ./Windows/modern/build.ps1 -Test -FullTest -BuildRoot <fresh-outside-OneDrive-directory>
& <runner.exe> --modern-windows-component-versions
& <runner.exe> --modern-windows-component-versions-sample <read-only-app-folder>
& <runner.exe> --modern-windows-application-package-sample <read-only-app-folder>
```

Before evidence: `ComponentsBeforeFramework-20261008/application-sample.log`. Default evidence: `ComponentsAfterFinalFramework-20261008/full-default.log`, `components-after-final-modern-20261008.log`. Final focused evidence: `ComponentsVersionsFinalFramework-20261008`, `ComponentsVersionsFinalFrameworkFocused-20261008`, `ComponentsVersionsFinalModernFocused-20261008`, `components-modern-final-basic-20261008.log` and `components-modern-final-sample-20261008.log`. Compile/source/binding details are retained in the corresponding JSON/compile logs and `verify-components-versions-final-20261008.ps1`; resource/assembly observations are in `dependency-resource-observation-20261008.json` and `dependency-assembly-observation-20261008.json`.

The API contract specifies that [GetAssemblyName reads a manifest without adding the assembly to the domain](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.assemblyname.getassemblyname?view=net-10.0). Windows documents [numeric fixed version information as coming from the binary](https://learn.microsoft.com/en-us/windows/win32/api/winver/nf-winver-getfileversioninfow), while localized strings can come from a MUI file.

This checkpoint neither identifies/authenticates every vendor component nor approves an installer, publisher or endpoint. Complete component/catalog/loader/trusted direct-distribution, modern foreground/tray/lifetime/notification, official user-supplied Luna LUT/genuine camera and explicitly identified disposable hardware/filesystem/travel acceptance remain open. No real media/device/settings/system action, installation or publication occurred. Full goal active; expired heartbeat paused.
