param([switch]$Test)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The installed .NET Framework x64 C# compiler is required.' }
$output = Join-Path $env:LOCALAPPDATA 'EasyEject/PrototypeBuild'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$references = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Security.dll', '/r:System.Xml.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Runtime.Serialization.dll', '/r:System.IO.Compression.dll', '/r:System.Net.Http.dll')
& $compiler /nologo /warn:4 /warnaserror+ /optimize+ /platform:x64 /target:winexe "/win32manifest:$PSScriptRoot/app.manifest" "/out:$output/EasyEject.Windows.exe" @references @sources
if ($LASTEXITCODE -ne 0) { throw 'App compilation failed.' }
$testSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /warn:4 /warnaserror+ /optimize+ /platform:x64 /target:exe /main:EasyEject.Tests "/win32manifest:$PSScriptRoot/app.manifest" "/out:$output/EasyEject.Tests.exe" @references @sources @testSources
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app.config') -Destination (Join-Path $output 'EasyEject.Windows.exe.config')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app.config') -Destination (Join-Path $output 'EasyEject.Tests.exe.config')
Write-Output "Build: $output"
if ($Test) { & "$output/EasyEject.Tests.exe"; if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' } }
