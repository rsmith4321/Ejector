param(
    [switch]$Test,
    [switch]$FullTest,
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
foreach ($preview in @(Get-Process -Name 'EasyEject.Modern', 'EasyEject.ModernContracts', 'EasyEject.FullContracts' -ErrorAction SilentlyContinue)) {
    if ($preview.Path -and $preview.Path.StartsWith($resolvedBuildRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'An app or contract runner in this output directory is running. Let it finish, quit the app, or choose a new output directory.' }
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
        if ($FullTest) {
            & $SdkPath build (Join-Path $PSScriptRoot 'tests/EasyEject.FullContracts.csproj') "-p:EasyEjectBuildRoot=$resolvedBuildRoot" --verbosity minimal
            if ($LASTEXITCODE -ne 0) { throw 'Full modern contract compilation failed.' }
            $fullOutput = Join-Path $resolvedBuildRoot 'bin/EasyEject.FullContracts/Debug/net10.0-windows/win-x64'
            # Existing Framework package-policy and native protocol fixtures need
            # owned unsigned Framework binaries, never a real release/tool/device.
            $fixtureCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
            if (!(Test-Path -LiteralPath $fixtureCompiler -PathType Leaf)) { throw 'The installed Framework compiler is required for existing package/protocol fixtures.' }
            $fixtureSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../src') -Filter '*.cs' | ForEach-Object FullName)
            $fixtureTests = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../tests') -Filter '*.cs' | ForEach-Object FullName)
            $fixtureReferences = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Security.dll', '/r:System.Xml.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Runtime.Serialization.dll', '/r:System.IO.Compression.dll', '/r:System.Net.Http.dll')
            $fixtureTools = Join-Path $fullOutput 'fixture-tools'
            New-Item -ItemType Directory -Force -Path $fixtureTools | Out-Null
            & $fixtureCompiler /nologo /warn:4 /warnaserror+ /optimize+ /platform:x64 /target:winexe "/win32manifest:$PSScriptRoot/../app.manifest" "/out:$fullOutput/EasyEject.Windows.exe" @fixtureReferences @fixtureSources
            if ($LASTEXITCODE -ne 0) { throw 'Unsigned Framework package fixture compilation failed.' }
            & $fixtureCompiler /nologo /warn:4 /warnaserror+ /optimize+ /platform:x64 /target:exe /main:EasyEject.Tests "/win32manifest:$PSScriptRoot/../app.manifest" "/out:$fixtureTools/EasyEject.Tests.exe" @fixtureReferences @fixtureSources @fixtureTests
            if ($LASTEXITCODE -ne 0) { throw 'Synthetic Framework protocol fixture compilation failed.' }
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../app.config') -Destination (Join-Path $fullOutput 'EasyEject.Windows.exe.config')
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../app.config') -Destination (Join-Path $fixtureTools 'EasyEject.Tests.exe.config')
            & (Join-Path $fullOutput 'EasyEject.FullContracts.exe')
            if ($LASTEXITCODE -ne 0) { throw 'Full modern contract tests failed.' }
        }
    } finally { Pop-Location }
} finally {
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
}
Write-Output "Modern preview: $resolvedBuildRoot/bin/EasyEject.Modern/Debug/net10.0-windows10.0.26100.0/win-x64/EasyEject.Modern.exe"
