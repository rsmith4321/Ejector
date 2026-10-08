#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$AssetsPath,
    [Parameter(Mandatory)][string]$PackageRoot,
    [Parameter(Mandatory)][string]$PayloadRoot,
    [Parameter(Mandatory)][string]$ReportPath
)
$ErrorActionPreference = 'Stop'
$payload = [IO.Path]::GetFullPath($PayloadRoot).TrimEnd('\', '/')
$report = [IO.Path]::GetFullPath($ReportPath)
$cache = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\', '/')
$notices = Join-Path $payload 'ThirdPartyNotices'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd('\', '/')
function Require-OrdinaryPath([string]$path) {
    for ($probe = [IO.Path]::GetFullPath($path); $null -ne $probe;) {
        if (Test-Path -LiteralPath $probe) {
            $item = Get-Item -LiteralPath $probe -Force
            if ($item.Attributes -band ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Offline)) { throw 'Notice preparation requires ordinary local paths without redirects or offline placeholders.' }
        }
        $parent = [IO.DirectoryInfo]::new($probe).Parent
        $probe = if ($null -eq $parent) { $null } else { $parent.FullName }
    }
}
foreach ($excluded in @($sourceRoot, $cache, $env:OneDrive, $env:OneDriveConsumer, $env:OneDriveCommercial)) {
    if (![string]::IsNullOrWhiteSpace($excluded)) {
        $root = [IO.Path]::GetFullPath($excluded).TrimEnd('\', '/')
        foreach ($output in @($payload, $report)) {
            if ($output.Equals($root, [StringComparison]::OrdinalIgnoreCase) -or $output.StartsWith($root+'\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Keep notice outputs outside the checkout, OneDrive and package cache.' }
        }
    }
}
Require-OrdinaryPath $payload; Require-OrdinaryPath $report
if (!(Test-Path -LiteralPath $payload -PathType Container) -or !(Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($report)) -PathType Container)) { throw 'Choose an existing local payload and report parent.' }
if ((Test-Path -LiteralPath $notices) -or (Test-Path -LiteralPath $report)) { throw 'Notice output/evidence already exists; retain it and choose a fresh review directory.' }
if ($report.StartsWith($payload+'\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Keep notice provenance outside the app payload.' }
Require-OrdinaryPath $AssetsPath
if ((Get-Item -LiteralPath $AssetsPath).Length -gt 4194304) { throw 'The recorded dependency graph exceeds the notice preparation bound.' }
$assetsBytes = [IO.File]::ReadAllBytes($AssetsPath)
$assets = [Text.UTF8Encoding]::new($false,$true).GetString($assetsBytes) | ConvertFrom-Json -Depth 32
$pinsPath = Join-Path $PSScriptRoot 'vendor-notice-pins.json'
$pinsBytes = [IO.File]::ReadAllBytes($pinsPath)
$pins = [Text.UTF8Encoding]::new($false,$true).GetString($pinsBytes) | ConvertFrom-Json -Depth 12
if ($pins.format -ne 1 -or $pins.kind -ne 'existing-microsoft-package-notice-pins' -or $pins.productionApproval -ne $false -or $pins.packages.Count -ne 20) { throw 'Unsupported code-owned notice pin set.' }
$selected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($property in $assets.libraries.psobject.Properties) {
    if ($property.Value.type -eq 'package') { [void]$selected.Add($property.Name) }
}
foreach ($framework in $assets.project.frameworks.psobject.Properties.Value) {
    foreach ($download in $framework.downloadDependencies) {
        $match = [regex]::Match($download.version, '^\[([^, ]+), \1\]$')
        if (!$match.Success) { throw 'Notice source versions must be exact downloaded package versions.' }
        [void]$selected.Add($download.name+'/'+$match.Groups[1].Value)
    }
}
if (!$selected.SetEquals([string[]]$pins.packages.identity)) { throw 'Dependency selection differs from independently observed notice source packages; refresh provenance deliberately before changing pins.' }
$holds = [Collections.Generic.List[IDisposable]]::new()
$planned = [Collections.Generic.List[object]]::new()
$unique = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$packages = [Collections.Generic.List[object]]::new()
$total = 0L
try {
    foreach ($package in $pins.packages) {
        if ($package.identity -notmatch '^[A-Za-z0-9.-]+/[0-9]+(\.[0-9]+){2,3}$' -or $package.packageSha256 -notmatch '^[a-f0-9]{64}$') { throw 'Malformed code-owned package notice pin.' }
        $parts = $package.identity.ToLowerInvariant().Split('/')
        $path = Join-Path $cache ($parts[0]+'/'+$parts[1]+'/'+$parts[0]+'.'+$parts[1]+'.nupkg')
        Require-OrdinaryPath $path
        $stream = [IO.FileStream]::new($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        $holds.Add($stream)
        if ($stream.Length -gt 1073741824) { throw 'Package exceeds the notice source bound.' }
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
        if ($hash -cne $package.packageSha256) { throw "Package bytes differ from independently observed Microsoft source: $($package.identity)" }
        $stream.Position = 0
        $archive = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Read,$true)
        $holds.Add($archive)
        foreach ($pin in $package.entries) {
            if ($pin.destination -notmatch '^(tools/)?[A-Za-z0-9_-]+\.[Tt][Xx][Tt]$' -or $pin.length -le 0 -or $pin.length -gt 1048576 -or $pin.sha256 -notmatch '^[a-f0-9]{64}$') { throw 'Unsupported bounded notice entry.' }
            $entries = @($archive.Entries | Where-Object { $_.FullName -ceq $pin.entry })
            if ($entries.Count -ne 1 -or $entries[0].Length -ne $pin.length) { throw 'Pinned notice source entry is absent, aliased or changed.' }
            $entryStream = $entries[0].Open(); $memory = [IO.MemoryStream]::new()
            try { $entryStream.CopyTo($memory); $bytes = $memory.ToArray() } finally { $memory.Dispose(); $entryStream.Dispose() }
            if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -cne $pin.sha256) { throw 'Pinned notice bytes differ from independent source.' }
            $destination = 'ThirdPartyNotices/'+$package.identity.Replace('/','-')+'/'+$pin.destination
            if (!$unique.Add($destination)) { throw 'Notice publication paths collide.' }
            $total += $bytes.Length
            if ($planned.Count -ge 64 -or $total -gt 4194304) { throw 'Notice payload exceeds its file/aggregate bound.' }
            $planned.Add([pscustomobject]@{path=$destination;package=$package.identity;sourceEntry=$pin.entry;length=$bytes.Length;sha256=$pin.sha256;bytes=$bytes})
        }
        $packages.Add([pscustomobject]@{identity=$package.identity;sha256=$hash;sourceRole=$package.sourceRole})
    }
    # All sources and choices validate before the first payload mutation. Retain
    # read-only package holds through exact copying; never replace existing files.
    [void][IO.Directory]::CreateDirectory($notices)
    foreach ($file in $planned) {
        $path = Join-Path $payload $file.path
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
        $output = [IO.FileStream]::new($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try { $output.Write($file.bytes,0,$file.bytes.Length); $output.Flush($true) } finally { $output.Dispose() }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $file.sha256) { throw 'Notice output differs from exact source bytes; retain the failed review.' }
    }
    $evidence = [ordered]@{
        kind='pinned-existing-vendor-notice-preservation'; productionApproval=$false
        sourceObservationSha256=$pins.sourceObservationSha256; dotnet=$pins.dotnet; windowsAppSdk=$pins.windowsAppSdk
        noticePinsSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($pinsBytes)).ToLowerInvariant()
        assetsSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($assetsBytes)).ToLowerInvariant()
        licenseAcceptance='pending final publication review; preserved texts/metadata do not establish complete redistribution acceptance'
        missingEmbeddedLicenseText=@($pins.packages | Where-Object { @($_.entries | Where-Object { $_.entry -match '(?i)(^|/)license(\.|$)' }).Count -eq 0 } | ForEach-Object identity)
        generatedUtc=[DateTime]::UtcNow.ToString('o'); packages=$packages.ToArray()
        files=@($planned | Select-Object path,package,sourceEntry,length,sha256 | Sort-Object path)
    }
    $reportBytes = [Text.UTF8Encoding]::new($false).GetBytes(($evidence | ConvertTo-Json -Depth 8))
    $output = [IO.FileStream]::new($report,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $output.Write($reportBytes,0,$reportBytes.Length); $output.Flush($true) } finally { $output.Dispose() }
    Write-Output "Preserved $($planned.Count) exact package metadata/license/notice files ($total bytes); final publication review remains pending."
} finally {
    for ($i=$holds.Count-1; $i -ge 0; $i--) { $holds[$i].Dispose() }
}
