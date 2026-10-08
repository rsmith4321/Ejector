#requires -Version 7.0
param(
    [string]$SdkPath = (Join-Path $env:USERPROFILE '.codex/EasyEjectBuildTools/dotnet-10.0.401/dotnet.exe'),
    [string]$BuildRoot = (Join-Path $env:LOCALAPPDATA ('EasyEject/ReviewBuild-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))),
    [string]$CacheRoot = (Join-Path $env:USERPROFILE '.codex/EasyEjectBuildTools'),
    [ValidatePattern('^$|^[0-9a-f]{40}$')][string]$SourceRevision = ''
)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $SdkPath -PathType Leaf)) { throw 'The existing pinned SDK is required; this command does not install it.' }
$reviewRoot = [IO.Path]::GetFullPath($BuildRoot).TrimEnd('\', '/')
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd('\', '/')
foreach ($excluded in @($sourceRoot, $env:OneDrive, $env:OneDriveConsumer, $env:OneDriveCommercial)) {
    if (![string]::IsNullOrWhiteSpace($excluded)) {
        $excludedRoot = [IO.Path]::GetFullPath($excluded).TrimEnd('\', '/')
        if ($reviewRoot.Equals($excludedRoot, [StringComparison]::OrdinalIgnoreCase) -or $reviewRoot.StartsWith($excludedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Choose a review directory outside the source checkout and OneDrive.' }
    }
}
if (Test-Path -LiteralPath $reviewRoot) { throw 'Choose a fresh review directory; existing files and running copies are never overwritten.' }
for ($ancestor = [IO.DirectoryInfo]::new($reviewRoot); $ancestor -ne $null; $ancestor = $ancestor.Parent) {
    if ($ancestor.Exists -and ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Choose an ordinary review path without redirected parent directories.' }
}
New-Item -ItemType Directory -Path $reviewRoot | Out-Null
$payload = Join-Path $reviewRoot 'app'
$savedEnvironment = @{}
$reviewEnvironment = @{
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_CLI_HOME = (Join-Path $CacheRoot 'cli')
    NUGET_PACKAGES = (Join-Path $CacheRoot 'nuget')
}
try {
    foreach ($name in $reviewEnvironment.Keys) {
        $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $reviewEnvironment[$name], 'Process')
    }
    Push-Location $PSScriptRoot
    try {
        $sdkVersion = & $SdkPath --version
        if ($LASTEXITCODE -ne 0 -or $sdkVersion.Trim() -ne '10.0.401') { throw 'SDK 10.0.401 is required for this review build.' }
        # Local SDK publish means preparing files, not publishing a website/channel.
        # Keep native WinUI files separate; retain full assemblies and their metadata.
        & $SdkPath publish (Join-Path $PSScriptRoot 'app/EasyEject.Modern.csproj') -c Release -r win-x64 --self-contained true "-p:EasyEjectBuildRoot=$reviewRoot/build" -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=false -o $payload --verbosity minimal 2>&1 | Tee-Object -FilePath (Join-Path $reviewRoot 'build.log')
        if ($LASTEXITCODE -ne 0) { throw 'Local review build failed; retain its log and choose a new directory for retry.' }
    } finally { Pop-Location }
} finally {
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
}
foreach ($required in @('EasyEject.Modern.exe', 'EasyEject.Modern.dll', 'EasyEject.Runtime.dll', 'EasyEject.Modern.pri', 'EasyEject.Modern.runtimeconfig.json', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll', 'System.Windows.Forms.dll', 'Microsoft.UI.Xaml.dll', 'Microsoft.WindowsAppRuntime.dll', 'Assets/EasyEject.png')) {
    if (!(Test-Path -LiteralPath (Join-Path $payload $required) -PathType Leaf)) { throw "Review output is incomplete: $required" }
}
$runtime = Get-Content -LiteralPath (Join-Path $payload 'EasyEject.Modern.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks -or !$runtime.runtimeOptions.includedFrameworks) { throw 'Review output still requires an installed .NET framework.' }
& (Join-Path $PSScriptRoot 'preserve-vendor-notices.ps1') -AssetsPath (Join-Path $reviewRoot 'build/obj/EasyEject.Modern/project.assets.json') -PackageRoot (Join-Path $CacheRoot 'nuget') -PayloadRoot $payload -ReportPath (Join-Path $reviewRoot 'vendor-notices.json')
& (Join-Path $PSScriptRoot 'inspect-native-dependencies.ps1') -PayloadRoot $payload -ReportPath (Join-Path $reviewRoot 'native-imports.json')
$readme = @'
Easy Eject local Windows review

Keep this complete app folder together. The .NET and Windows App SDK files are
included for local review; clean-computer/native dependency acceptance is pending.
This unsigned development copy is incomplete and is not an approved release.

Before starting this copy, finish or safely stop work and use Quit preview in
any running Easy Eject preview. A second launch reveals the running copy; it
does not switch versions. Then start EasyEject.Modern.exe manually for review.
Closing its window hides it in the tray. Use Quit preview after work to exit.
No startup entry, installation, tool/LUT download or device action is performed
by this build command. Keep device operations limited to an explicitly identified
disposable device and agreed test scope. Do not erase real original media.

Settings and saved history use the normal separate user settings folder.
This is not a sandboxed test configuration. Keep the app stopped until ready.
FFmpeg/FFprobe and the official Luna LUT are not included. Quick Share stays
opt-in per device and manual per action. The complete full-quality copies remain.

review-build.json in the parent folder records local hashes, not a signature or
trusted release proof.

ThirdPartyNotices preserves existing package license/notice texts and metadata.
vendor-notices.json records their exact source pins. Final publication license
and notice review is still pending; this is not permission to redistribute.
'@
[IO.File]::WriteAllText((Join-Path $payload 'README-REVIEW.txt'), $readme, [Text.UTF8Encoding]::new($false))
$files = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
    if ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Unexpected redirected review payload.' }
    [ordered]@{ path = [IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\', '/'); length = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$evidence = [ordered]@{
    kind = 'unsigned-local-review'; declaredSourceRevision = $SourceRevision
    sdk = $sdkVersion.Trim(); architecture = 'win-x64'; configuration = 'Release'
    installedFrameworkRequired = $false; includedFrameworks = $runtime.runtimeOptions.includedFrameworks
    nativeImportObservation = 'native-imports.json (static names only; not loader or clean-computer proof)'
    vendorNoticeObservation = 'vendor-notices.json (exact pinned package texts/metadata; final publication acceptance pending)'
    nativeCleanComputerAcceptance = 'pending'; foregroundAcceptance = 'pending'
    generatedUtc = [DateTime]::UtcNow.ToString('o'); files = $files
}
$evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $reviewRoot 'review-build.json') -Encoding utf8
Write-Output "Local review folder: $payload"
Write-Output 'No app was launched, installed, registered at login, signed or publicly released.'
