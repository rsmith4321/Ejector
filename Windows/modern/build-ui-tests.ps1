#requires -Version 7.0
param(
    [string]$SdkPath = (Join-Path $env:USERPROFILE '.codex/EasyEjectBuildTools/dotnet-10.0.401/dotnet.exe'),
    [string]$BuildRoot = (Join-Path $env:USERPROFILE ('.codex/EasyEjectBuildTools/UiTray-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($BuildRoot).TrimEnd('\', '/')
foreach ($excluded in @((Join-Path $PSScriptRoot '../..'), $env:OneDrive, $env:OneDriveConsumer, $env:OneDriveCommercial)) {
    if ([string]::IsNullOrWhiteSpace($excluded)) { continue }
    $path = [IO.Path]::GetFullPath($excluded).TrimEnd('\', '/')
    if ($root.Equals($path, [StringComparison]::OrdinalIgnoreCase) -or $root.StartsWith($path + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'UI test output must be outside the checkout and OneDrive.' }
}
if (Test-Path -LiteralPath $root) { throw 'Choose a fresh UI-test output directory.' }
for ($ancestor = [IO.DirectoryInfo]::new($root); $ancestor -ne $null; $ancestor = $ancestor.Parent) {
    if ($ancestor.Exists -and ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Choose an ordinary UI-test output path without redirected parents.' }
}
if (!(Test-Path -LiteralPath $SdkPath -PathType Leaf)) { throw 'The existing pinned SDK is required. Nothing is installed.' }
$cache = Join-Path $env:USERPROFILE '.codex/EasyEjectBuildTools'
$saved = @{}
$environment = @{ DOTNET_CLI_HOME = (Join-Path $cache 'cli'); NUGET_PACKAGES = (Join-Path $cache 'nuget'); DOTNET_CLI_TELEMETRY_OPTOUT = '1'; DOTNET_ROOT = (Split-Path -Parent $SdkPath) }
try {
    foreach ($name in $environment.Keys) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process'); [Environment]::SetEnvironmentVariable($name, $environment[$name], 'Process') }
    if ((& $SdkPath --version).Trim() -ne '10.0.401') { throw 'SDK 10.0.401 is required.' }
    & $SdkPath build (Join-Path $PSScriptRoot 'app/EasyEject.Modern.csproj') "-p:EasyEjectBuildRoot=$root" -p:EasyEjectUiTests=true --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'UI-test compilation failed.' }
    $fixture = Join-Path $env:USERPROFILE ('.codex/EasyEjectFixtures/ui-' + [Guid]::NewGuid().ToString('N'))
    $exe = Join-Path $root 'bin/EasyEject.Modern/Debug/net10.0-windows10.0.26100.0/win-x64/EasyEject.Modern.exe'
    $process = Start-Process -FilePath $exe -ArgumentList ('"' + $fixture + '"') -PassThru -WindowStyle Hidden
    $process.PriorityClass = 'BelowNormal'
    if (!$process.WaitForExit(60000)) {
        if ($process.Path -ieq $exe) { Stop-Process -Id $process.Id }
        throw "Owned UI-test process timed out. Evidence: $fixture"
    }
    $result = Join-Path $fixture 'result.txt'
    if (!(Test-Path -LiteralPath $result)) { throw "UI test produced no result. Evidence: $fixture" }
    $text = Get-Content -LiteralPath $result -Raw
    Write-Output $text
    if ($process.ExitCode -ne 0 -or !$text.StartsWith('PASS ', [StringComparison]::Ordinal)) { throw 'Native tray-command UI tests failed.' }
    Write-Output "Retained isolated UI fixture: $fixture"
} finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
}
