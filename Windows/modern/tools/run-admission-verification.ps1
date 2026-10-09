param([switch]$BrokerGrant)
$ErrorActionPreference='Stop'
if(!$BrokerGrant){throw 'Explicit bounded helper-test grant required.'}
$publicationSource=$PSScriptRoot
$publicationPacket=Join-Path 'C:\Users\rsmit\.codex\EasyEjectBuildTools' ('PublicationAdmissionRun-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ'))
$publicationTestRoot=Join-Path 'C:\Users\rsmit\.codex\EasyEjectBuildTools' ('PublicationGuardTest-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ'))
$publicationApp='C:\Users\rsmit\.codex\EasyEjectBuildTools\PowerConnectionAtomic-20261008\review\app'
$publicationConfig='C:\Users\rsmit\AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local\EasyEject\Profiles'
$publicationManifest='C:\Users\rsmit\.codex\EasyEjectBuildTools\PowerConnectionAtomic-20261008\review\review-build.json'
$publicationResult=[ordered]@{ParentPid=$PID;StartedUtc=[DateTime]::UtcNow.ToString('o');FinishedUtc=$null;ExitCode=1;Failure=$null;Packet=$publicationPacket;TestRoot=$publicationTestRoot;Count=$null;LivePublicationPerformed=$false;NegativeCliOnly=$true}
$publicationSavedEnv=@{}
$publicationEnv=@{DOTNET_GENERATE_ASPNET_CERTIFICATE='false';DOTNET_CLI_TELEMETRY_OPTOUT='1';DOTNET_CLI_HOME='C:\Users\rsmit\.codex\EasyEjectBuildTools\cli';NUGET_PACKAGES='C:\Users\rsmit\.codex\EasyEjectBuildTools\nuget'}
function PublicationAppPin {
 $publicationLive=Get-Process -Id 21224 -ErrorAction Stop
 if($publicationLive.StartTime.ToUniversalTime().ToString('o') -ne '2026-10-09T02:46:46.1826671Z' -or $publicationLive.Path -ine (Join-Path $publicationApp 'EasyEject.Modern.exe')){throw 'Current app identity changed.'}
 [ordered]@{Pid=$publicationLive.Id;Path=$publicationLive.Path;StartedUtc=$publicationLive.StartTime.ToUniversalTime().ToString('o')}|ConvertTo-Json -Compress
}
function PublicationConfigPins {
 $publicationFiles=@(Get-ChildItem -LiteralPath $publicationConfig -File -Recurse | Sort-Object FullName)
 if($publicationFiles.Count -ne 2){throw 'Live configuration file set differs; preserve and rebase.'}
 @(foreach($publicationFile in $publicationFiles){[ordered]@{Path=$publicationFile.FullName;Length=$publicationFile.Length;Sha256=(Get-FileHash -LiteralPath $publicationFile.FullName).Hash}})|ConvertTo-Json -Compress
}
function PublicationManifestPin {
 if((Get-FileHash -LiteralPath $publicationManifest).Hash -ne '76F6D2C0109ED12C7D6042D6DFA504AC32B1AE3F4A7B4538856C5B551169A7A2'){throw 'Current manifest differs.'}
 $publicationFiles=(Get-Content -LiteralPath $publicationManifest -Raw|ConvertFrom-Json).files
 if(@($publicationFiles).Count -ne 649 -or @(Get-ChildItem -LiteralPath $publicationApp -File -Recurse).Count -ne 649){throw 'Current payload file set differs.'}
 foreach($publicationFile in $publicationFiles){$publicationPath=Join-Path $publicationApp $publicationFile.path;if((Get-Item -LiteralPath $publicationPath).Length -ne $publicationFile.length -or (Get-FileHash -LiteralPath $publicationPath).Hash -ine $publicationFile.sha256){throw ('Current payload differs: '+$publicationFile.path)}}
 [ordered]@{ManifestSha256=(Get-FileHash -LiteralPath $publicationManifest).Hash;Files=649}|ConvertTo-Json -Compress
}
New-Item -ItemType Directory -Path $publicationPacket|Out-Null
try {
 $publicationResult.BeforeApp=PublicationAppPin;$publicationResult.BeforeConfig=PublicationConfigPins;$publicationResult.BeforePayload=PublicationManifestPin
 $publicationResult.HeadBefore=git rev-parse HEAD;$publicationResult.GitStatusBefore=@(git status --short)
 $publicationResult.SourcePins=@(foreach($publicationName in @('OwnedRecordPublication.cs','PublicationGuardTests.cs','OwnedRecordPublication.csproj','NativeHistoryPublication.cs','NativeHistoryPublication.csproj','nuget.config','run-admission-verification.ps1')){[ordered]@{Name=$publicationName;Sha256=(Get-FileHash -LiteralPath (Join-Path $publicationSource $publicationName)).Hash}})
 $publicationResult|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $publicationPacket 'before.json') -Encoding utf8
 foreach($publicationName in $publicationEnv.Keys){$publicationSavedEnv[$publicationName]=[Environment]::GetEnvironmentVariable($publicationName,'Process');[Environment]::SetEnvironmentVariable($publicationName,$publicationEnv[$publicationName],'Process')}
 & 'C:\Users\rsmit\.codex\EasyEjectBuildTools\dotnet-10.0.401\dotnet.exe' publish (Join-Path $publicationSource 'NativeHistoryPublication.csproj') -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.12 -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=false "-p:BaseIntermediateOutputPath=$publicationPacket\obj\" "-p:BaseOutputPath=$publicationPacket\bin\" "-p:EasyEjectFixtureBackend=$publicationApp\EasyEject.Runtime.dll" "-p:RestoreConfigFile=$publicationSource\nuget.config" -o "$publicationPacket\runner" --verbosity minimal *> "$publicationPacket\compile.log"
 if($LASTEXITCODE -ne 0){throw 'Publication helper compilation failed.'}
 $publicationResult.RuntimePairs=@(foreach($publicationName in @('EasyEject.Runtime.dll','hostfxr.dll','hostpolicy.dll','coreclr.dll','System.Private.CoreLib.dll','System.Windows.Forms.dll','System.Drawing.Common.dll')){$publicationHash=(Get-FileHash -LiteralPath (Join-Path $publicationApp $publicationName)).Hash;if((Get-FileHash -LiteralPath "$publicationPacket\runner\$publicationName").Hash -ne $publicationHash){throw ('Helper runtime differs: '+$publicationName)};[ordered]@{Name=$publicationName;Sha256=$publicationHash}})
 & "$publicationPacket\runner\NativeHistoryPublication.exe" --core-self-test $publicationTestRoot 1> "$publicationPacket\tests.stdout.log" 2> "$publicationPacket\tests.stderr.log"
 $publicationResult.TestExitCode=$LASTEXITCODE
 if($LASTEXITCODE -ne 0){throw 'Publication guard tests failed; retain complete logs and fixture root.'}
 $publicationResult.CoreCount=Get-Content -LiteralPath (Join-Path $publicationTestRoot 'test-result.json') -Raw|ConvertFrom-Json
 $publicationAdmissionRoot=Join-Path 'C:\Users\rsmit\.codex\EasyEjectBuildTools' ('PublicationAdmissionTest-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ'))
 $publicationResult.AdmissionRoot=$publicationAdmissionRoot
 & "$publicationPacket\runner\NativeHistoryPublication.exe" --admission-self-test $publicationAdmissionRoot 1> "$publicationPacket\admission.stdout.log" 2> "$publicationPacket\admission.stderr.log"
 $publicationResult.AdmissionExitCode=$LASTEXITCODE
 if($LASTEXITCODE -ne 0){throw 'Admission/integration tests failed; retain complete logs and fixtures.'}
 $publicationResult.AdmissionCount=Get-Content -LiteralPath (Join-Path $publicationAdmissionRoot 'result.json') -Raw|ConvertFrom-Json
 $publicationNegativeLedger=Join-Path 'C:\Users\rsmit\.codex\EasyEjectBuildTools' ('NativeHistoryLive-Negative-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ'))
 $publicationProof=Join-Path $publicationPacket 'synthetic-stale-proof.json'
 [ordered]@{capturedUtc=[DateTime]::UtcNow.AddMinutes(-10).ToString('o');window=[ordered]@{app=('process:'+(Join-Path $publicationApp 'EasyEject.Modern.exe'));id=1967754;title='Easy Eject'};tree='button Refresh connected storage ID: Refresh'}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $publicationProof -Encoding utf8
 $publicationProofHash=(Get-FileHash -LiteralPath $publicationProof).Hash
 $publicationCliCases=@(
  @{Name='missing-mode';Arguments=@()},
  @{Name='wrong-mode';Arguments=@('--unsupported',$publicationNegativeLedger,$publicationProof,$publicationProofHash,'21224','2026-10-09T02:46:46.1826671Z','--broker-grant')},
  @{Name='missing-grant';Arguments=@('--publish-exact-video-history',$publicationNegativeLedger,$publicationProof,$publicationProofHash,'21224','2026-10-09T02:46:46.1826671Z')},
  @{Name='missing-proof';Arguments=@('--publish-exact-video-history',$publicationNegativeLedger,(Join-Path $publicationPacket 'absent-proof.json'),$publicationProofHash,'21224','2026-10-09T02:46:46.1826671Z','--broker-grant')},
  @{Name='wrong-proof-hash';Arguments=@('--publish-exact-video-history',$publicationNegativeLedger,$publicationProof,'wrong','21224','2026-10-09T02:46:46.1826671Z','--broker-grant')},
  @{Name='wrong-pid';Arguments=@('--publish-exact-video-history',$publicationNegativeLedger,$publicationProof,$publicationProofHash,'0','2026-10-09T02:46:46.1826671Z','--broker-grant')},
  @{Name='wrong-start';Arguments=@('--publish-exact-video-history',$publicationNegativeLedger,$publicationProof,$publicationProofHash,'21224','wrong-start','--broker-grant')},
  @{Name='stale-native-proof';Arguments=@('--publish-exact-video-history',$publicationNegativeLedger,$publicationProof,$publicationProofHash,'21224','2026-10-09T02:46:46.1826671Z','--broker-grant')}
 )
 $publicationResult.NegativeCli=@(foreach($publicationCliCase in $publicationCliCases){
  $publicationCliArgs=$publicationCliCase.Arguments
  & "$publicationPacket\runner\NativeHistoryPublication.exe" @publicationCliArgs 1> "$publicationPacket\cli-$($publicationCliCase.Name).stdout.log" 2> "$publicationPacket\cli-$($publicationCliCase.Name).stderr.log"
  $publicationCliExit=$LASTEXITCODE
  if($publicationCliExit -ne 1 -or (Test-Path -LiteralPath $publicationNegativeLedger)){throw ('CLI negative refusal failed: '+$publicationCliCase.Name)}
  if((PublicationConfigPins) -ne $publicationResult.BeforeConfig){throw 'Negative CLI changed live configuration.'}
  [ordered]@{Name=$publicationCliCase.Name;ExitCode=$publicationCliExit;LedgerAbsent=$true;ConfigUnchanged=$true}
 })
 $publicationResult.HelperHashes=@(foreach($publicationName in @('NativeHistoryPublication.exe','NativeHistoryPublication.dll')){[ordered]@{Name=$publicationName;Sha256=(Get-FileHash -LiteralPath "$publicationPacket\runner\$publicationName").Hash}})
 $publicationResult.Count=[ordered]@{Core=$publicationResult.CoreCount.Passed;AdmissionAndIntegration=$publicationResult.AdmissionCount.Passed;NegativeCli=$publicationResult.NegativeCli.Count;Failed=0}
 $publicationResult.ExitCode=0
}catch{$publicationResult.Failure=$_.Exception.ToString();$publicationResult.ExitCode=1}
finally {
 try {
  $publicationResult.AfterApp=PublicationAppPin;$publicationResult.AfterConfig=PublicationConfigPins;$publicationResult.AfterPayload=PublicationManifestPin
  if($publicationResult.BeforeApp -ne $publicationResult.AfterApp -or $publicationResult.BeforeConfig -ne $publicationResult.AfterConfig -or $publicationResult.BeforePayload -ne $publicationResult.AfterPayload){throw 'Preservation guard failed.'}
  foreach($publicationPin in $publicationResult.SourcePins){if((Get-FileHash -LiteralPath (Join-Path $publicationSource $publicationPin.Name)).Hash -ne $publicationPin.Sha256){throw 'Helper source changed during execution.'}}
  $publicationResult.HeadAfter=git rev-parse HEAD;$publicationResult.GitStatusAfter=@(git status --short)
  $publicationResult.ActiveOwnedWorkers=@(Get-Process -Name NativeHistoryPublication -ErrorAction SilentlyContinue | Where-Object {$_.Path -like "$publicationPacket\runner\*"} | Select-Object Id,Path)
  if($publicationResult.ActiveOwnedWorkers.Count -ne 0){throw 'Owned helper remains running.'}
 }catch{$publicationResult.PreservationFailure=$_.Exception.ToString();$publicationResult.ExitCode=1}
 foreach($publicationName in $publicationSavedEnv.Keys){[Environment]::SetEnvironmentVariable($publicationName,$publicationSavedEnv[$publicationName],'Process')}
 $publicationResult.FinishedUtc=[DateTime]::UtcNow.ToString('o')
 $publicationResult|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $publicationPacket 'result.json') -Encoding utf8
 $publicationResult|ConvertTo-Json -Depth 7 -Compress
}
exit $publicationResult.ExitCode

