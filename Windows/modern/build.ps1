param(
    [switch]$Test,
    [string]$SdkPath = (Join-Path $env:USERPROFILE '.codex/EasyEjectBuildTools/dotnet-10.0.401/dotnet.exe'),
    [string]$BuildRoot = (Join-Path $env:LOCALAPPDATA 'EasyEject/ModernBuild'),
    [string]$CacheRoot = (Join-Path $env:USERPROFILE '.codex/EasyEjectBuildTools')
)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $SdkPath -PathType Leaf)) { throw 'The pinned portable .NET SDK is required; this script does not install it.' }
$resolvedBuildRoot = [IO.Path]::GetFullPath($BuildRoot)
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd('\', '/')
if ($resolvedBuildRoot.Equals($repoRoot, [StringComparison]::OrdinalIgnoreCase) -or $resolvedBuildRoot.StartsWith($repoRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Choose a build directory outside the repository and OneDrive.' }
foreach ($syncRoot in @($env:OneDrive, $env:OneDriveConsumer, $env:OneDriveCommercial)) {
    if (![string]::IsNullOrWhiteSpace($syncRoot)) {
        $resolvedSyncRoot = [IO.Path]::GetFullPath($syncRoot).TrimEnd('\', '/')
        if ($resolvedBuildRoot.Equals($resolvedSyncRoot, [StringComparison]::OrdinalIgnoreCase) -or $resolvedBuildRoot.StartsWith($resolvedSyncRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Choose a build directory outside OneDrive.' }
    }
}
foreach ($preview in @(Get-Process -Name 'EasyEject.Modern' -ErrorAction SilentlyContinue)) {
    if ($preview.Path -and $preview.Path.StartsWith($resolvedBuildRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'The preview in this output directory is running. Quit it or choose a new output directory.' }
}
$savedEnvironment = @{}
$buildEnvironment = @{
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_CLI_HOME = (Join-Path $CacheRoot 'cli')
    NUGET_PACKAGES = (Join-Path $CacheRoot 'nuget')
}
try {
    foreach ($name in $buildEnvironment.Keys) {
        $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $buildEnvironment[$name], 'Process')
    }
    Push-Location $PSScriptRoot
    try {
        $version = & $SdkPath --version
        if ($LASTEXITCODE -ne 0 -or $version.Trim() -ne '10.0.401') { throw 'SDK 10.0.401 is required by this milestone.' }
        & $SdkPath build (Join-Path $PSScriptRoot 'app/EasyEject.Modern.csproj') "-p:EasyEjectBuildRoot=$resolvedBuildRoot" --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'Modern interface build failed.' }
        if ($Test) {
            & $SdkPath build (Join-Path $PSScriptRoot 'tests/EasyEject.ModernContracts.csproj') "-p:EasyEjectBuildRoot=$resolvedBuildRoot" --verbosity minimal
            if ($LASTEXITCODE -ne 0) { throw 'Modern contract compilation failed.' }
            & (Join-Path $resolvedBuildRoot 'bin/EasyEject.ModernContracts/Debug/net10.0-windows/win-x64/EasyEject.ModernContracts.exe')
            if ($LASTEXITCODE -ne 0) { throw 'Modern contract tests failed.' }
        }
    } finally { Pop-Location }
} finally {
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
}
Write-Output "Modern preview: $resolvedBuildRoot/bin/EasyEject.Modern/Debug/net10.0-windows10.0.26100.0/win-x64/EasyEject.Modern.exe"
