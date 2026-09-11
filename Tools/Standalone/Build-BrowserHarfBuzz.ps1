#requires -Version 7.0
<#
.SYNOPSIS
Builds the prepared browser HarfBuzz object used with NowUI's native font plugin.
.DESCRIPTION
Maintainer operation only. Uses the installed .NET wasm-tools Emscripten pack
and an upstream HarfBuzz checkout. End users receive the prepared object in the
optional browser kit and do not need these source or toolchain dependencies.
#>
[CmdletBinding()]
param(
    [string] $HarfBuzzSource,
    [string] $Output,
    [string] $DotnetRoot = (Split-Path (Get-Command dotnet).Source -Parent)
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $HarfBuzzSource) { $HarfBuzzSource = Join-Path $root 'Native/harfbuzz' }
if (-not $Output) { $Output = Join-Path $root 'Standalone/NowUI.Browser/native/harfbuzz.bc' }
$source = [IO.Path]::GetFullPath((Join-Path $HarfBuzzSource 'src/harfbuzz.cc'))
$outputPath = [IO.Path]::GetFullPath($Output, $root)
if (-not (Test-Path -LiteralPath $source)) { throw "Missing HarfBuzz amalgamation: $source" }
function Get-PackTools([string] $Name) {
    $versions = Get-ChildItem -Directory -LiteralPath (Join-Path $DotnetRoot "packs/$Name")
    $version = $versions | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $version) { throw "Install .NET wasm-tools before building HarfBuzz: missing $Name" }
    return Join-Path $version.FullName 'tools'
}
$sdk = Get-PackTools 'Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.win-x64'
$pythonTools = Get-PackTools 'Microsoft.NET.Runtime.Emscripten.3.1.56.Python.win-x64'
$nodeTools = Get-PackTools 'Microsoft.NET.Runtime.Emscripten.3.1.56.Node.win-x64'
$cacheTools = Get-PackTools 'Microsoft.NET.Runtime.Emscripten.3.1.56.Cache.win-x64'
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $pythonTools 'python.exe'))
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.WorkingDirectory = $root
$start.Environment['EM_CONFIG'] = Join-Path $sdk 'emscripten/.emscripten'
$start.Environment['DOTNET_EMSCRIPTEN_LLVM_ROOT'] = Join-Path $sdk 'bin'
$start.Environment['DOTNET_EMSCRIPTEN_BINARYEN_ROOT'] = $sdk
$start.Environment['DOTNET_EMSCRIPTEN_NODE_JS'] = Join-Path $nodeTools 'bin/node.exe'
$start.Environment['EM_CACHE'] = Join-Path $cacheTools 'emscripten/cache'
foreach ($argument in @((Join-Path $sdk 'emscripten/emcc.py'), '-c', '-O3', '-std=c++11', '-fno-exceptions',
    '-fno-rtti', '-ffunction-sections', '-fdata-sections', '-DHB_NO_MT', '-DNDEBUG', $source, '-o', $outputPath)) {
    $start.ArgumentList.Add($argument)
}
$process = [Diagnostics.Process]::Start($start)
try {
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "HarfBuzz compilation failed: $($process.ExitCode)" }
}
finally { $process.Dispose() }
Copy-Item -LiteralPath (Join-Path $HarfBuzzSource 'COPYING') -Destination (Join-Path ([IO.Path]::GetDirectoryName($outputPath)) 'HarfBuzz-LICENSE.txt')
$versionLine = Get-Content -LiteralPath (Join-Path $HarfBuzzSource 'meson.build') | Where-Object { $_ -match '^\s*version:' } | Select-Object -First 1
$revision = & git -C $HarfBuzzSource rev-parse HEAD 2>$null
if ($LASTEXITCODE -ne 0) { $revision = 'source archive (no Git revision)' }
@{
    harfbuzz = $versionLine.Trim()
    upstream_revision = $revision
    emscripten = '3.1.56 (.NET wasm-tools)'
    source_sha256 = (Get-FileHash -LiteralPath $source).Hash
    object_sha256 = (Get-FileHash -LiteralPath $outputPath).Hash
    flags = '-O3 -std=c++11 -fno-exceptions -fno-rtti -ffunction-sections -fdata-sections -DHB_NO_MT -DNDEBUG'
} | ConvertTo-Json | Set-Content -LiteralPath ([IO.Path]::ChangeExtension($outputPath, '.json')) -Encoding utf8
Write-Host "Prepared HarfBuzz object: $outputPath"
