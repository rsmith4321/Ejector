#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$PayloadRoot,
    [Parameter(Mandatory)][string]$ReportPath
)
$ErrorActionPreference = 'Stop'
$payload = [IO.Path]::GetFullPath($PayloadRoot).TrimEnd('\', '/')
$report = [IO.Path]::GetFullPath($ReportPath)
if (!(Test-Path -LiteralPath $payload -PathType Container)) { throw 'Choose an existing local review app folder.' }
if (Test-Path -LiteralPath $report) { throw 'Choose a fresh report path; existing evidence is retained.' }
if ($report.StartsWith($payload + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Keep the report outside the app payload and its recorded inventory.' }

function Read-PortableImports([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    function Require-Bytes([long]$offset, [long]$length) {
        if ($offset -lt 0 -or $length -lt 0 -or $offset -gt $bytes.Length -or $length -gt $bytes.Length - $offset) { throw "Truncated PE data: $path" }
    }
    function U16([long]$offset) { Require-Bytes $offset 2; [BitConverter]::ToUInt16($bytes, [int]$offset) }
    function U32([long]$offset) { Require-Bytes $offset 4; [BitConverter]::ToUInt32($bytes, [int]$offset) }
    Require-Bytes 0 64
    if ((U16 0) -ne 0x5a4d) { throw "Not a PE file: $path" }
    $pe = U32 60; Require-Bytes $pe 24
    if ((U32 $pe) -ne 0x4550) { throw "Invalid PE signature: $path" }
    $count = U16 ($pe + 6); $optionalSize = U16 ($pe + 20); $optional = $pe + 24
    Require-Bytes $optional $optionalSize
    $magic = U16 $optional
    $directoryOffset = if ($magic -eq 0x20b) { 112 } elseif ($magic -eq 0x10b) { 96 } else { throw "Unknown PE optional header: $path" }
    if ($optionalSize -lt $directoryOffset) { throw "Missing PE directories: $path" }
    $directoryCount = U32 ($optional + $directoryOffset - 4)
    if ($directoryCount -gt 16 -or $directoryOffset + $directoryCount * 8 -gt $optionalSize) { throw "Invalid PE directory bounds: $path" }
    $sections = @(); $table = $optional + $optionalSize
    Require-Bytes $table ([long]$count * 40)
    for ($s = 0; $s -lt $count; $s++) {
        $entry = $table + $s * 40
        $sections += @{ address = U32 ($entry + 12); length = U32 ($entry + 16); raw = U32 ($entry + 20) }
    }
    function Rva-Offset([uint32]$rva) {
        if ($rva -lt (U32 ($optional + 60))) { Require-Bytes $rva 1; return [long]$rva }
        foreach ($section in $sections) {
            if ([uint64]$rva -ge $section.address -and [uint64]$rva -lt [uint64]$section.address + $section.length) {
                $offset = [long]$section.raw + $rva - $section.address; Require-Bytes $offset 1; return $offset
            }
        }
        throw "Unmapped import RVA: $path"
    }
    function Import-Name([uint32]$rva) {
        $start = Rva-Offset $rva; $end = $start
        while ($end -lt $bytes.Length -and $end - $start -lt 512 -and $bytes[$end] -ne 0) { $end++ }
        if ($end -eq $bytes.Length -or $end - $start -ge 512) { throw "Unterminated import name: $path" }
        [Text.Encoding]::ASCII.GetString($bytes, [int]$start, [int]($end - $start))
    }
    $imports = @()
    foreach ($directory in @(1, 13)) {
        if ($directoryCount -le $directory) { continue }
        $rva = U32 ($optional + $directoryOffset + $directory * 8)
        $size = U32 ($optional + $directoryOffset + $directory * 8 + 4)
        if (!$rva -or !$size) { continue }
        $start = Rva-Offset $rva; $stride = if ($directory -eq 1) { 20 } else { 32 }
        for ($i = 0L; $i + $stride -le $size; $i += $stride) {
            $offset = $start + $i; Require-Bytes $offset $stride
            $nameRva = U32 ($offset + $(if ($directory -eq 1) { 12 } else { 4 }))
            if (!$nameRva) { break }
            if ($directory -eq 13 -and ((U32 $offset) -band 1) -eq 0) { throw "Legacy VA delay imports require separate inspection: $path" }
            $imports += [ordered]@{ dll = Import-Name $nameRva; kind = if ($directory -eq 1) { 'import' } else { 'delay' } }
        }
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
    [ordered]@{ path = [IO.Path]::GetRelativePath($payload, $path).Replace('\', '/'); sha256 = $hash; machine = ('{0:x4}' -f (U16 ($pe + 4))); imports = $imports }
}

$files = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Where-Object { $_.Extension -in @('.dll', '.exe') } | Sort-Object FullName)
$names = @{}; foreach ($file in $files) { $names[$file.Name] = $true }
$modules = @(foreach ($file in $files) {
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Do not inspect a redirected payload file.' }
    $module = Read-PortableImports $file.FullName
    foreach ($import in $module.imports) {
        $import.payloadNamePresent = $names.ContainsKey($import.dll)
        $import.system32Present = Test-Path -LiteralPath (Join-Path "$env:WINDIR/System32" $import.dll) -PathType Leaf
        $import.apiContractName = $import.dll -match '^(api-|ext-)'
    }
    $module
})
$allImports = @($modules | ForEach-Object { $_.imports })
$missing = @($allImports | Where-Object { !$_.payloadNamePresent -and !$_.system32Present -and !$_.apiContractName })
$evidence = [ordered]@{
    kind = 'static-local-pe-import-observation'; payload = $payload; generatedUtc = [DateTime]::UtcNow.ToString('o')
    peFiles = $modules.Count; importReferences = $allImports.Count
    ordinaryNamesAbsentLocally = $missing.Count
    visualCppRuntimeImports = @($allImports | Where-Object { $_.dll -match 'vcruntime|msvcp|msvcr|concrt' }).Count
    limitation = 'Names in any payload directory or this laptop System32 are observations, not loader resolution, baseline OS ownership or clean-computer proof. API contracts, dynamic loads, symbols, optional APIs and execution are not verified.'
    modules = $modules
}
$evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding utf8
Write-Output "Inspected $($modules.Count) PE files and $($allImports.Count) static import references; $($missing.Count) ordinary names absent locally. Report: $report"
