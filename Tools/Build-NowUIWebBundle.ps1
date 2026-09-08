<#
.SYNOPSIS
    Builds the precompiled NowUI web bundle that ships inside the package at Assets/NowUI/WebBundle~.

.DESCRIPTION
    This is the ONE command a NowUI maintainer runs to regenerate the browser build that a Unity user gets for
    free. The user never runs it: they install the package, click Tools > NowUI > Web Preview, and the Editor
    serves the tree this script produced. No .NET SDK, no emscripten, no terminal, no network on their side.

    Three publish properties do the work, and each was measured rather than assumed:

      WasmFingerprintAssets=false   filenames stop carrying a content hash, so the framework blobs are the SAME
      WasmFingerprintDotnetJs=false git objects on every rebuild. With fingerprints ON every filename changes on
                                    every publish, so each regeneration would add ~5.5 MB of fresh git objects.
      CompressionEnabled=false      drops the .br and .gz siblings (~3.95 MB) that buy exactly nothing over a
                                    loopback socket at memory speed.

    Baseline for comparison, measured on this repository:
      dotnet publish -c Release with SDK defaults ... 191 files, 13,733,363 B (13.10 MiB)
      + the three properties above ................  87 files,  9,780,373 B ( 9.33 MiB)
      + the exclude list ..........................  86 files,  8,483,668 B ( 8.09 MiB)   <- the shipped bundle

    -Compress produces the same tree WITH .br/.gz siblings, for anyone dropping it on a real web server. That is
    not what the Editor serves and it is not what is committed.

.PARAMETER OutputRoot
    Where the staged bundle lands. Default: <repo>/Assets/NowUI/WebBundle~ - the trailing tilde is what keeps it
    out of Unity's AssetDatabase entirely (no import, no .meta, no compile), the same mechanism already carrying
    AI~, Analyzers~, Documentation~ and Samples~ in this package.

.PARAMETER Compress
    Publish with CompressionEnabled=true, so the staged tree also holds .br/.gz siblings. For hosted deploys.

.PARAMETER MaxBytes
    Fail the build if the staged tree exceeds this. Default 10,000,000. This is the guard that catches the NEXT
    stray multi-megabyte fixture with a red build instead of with someone noticing six months later.

.PARAMETER MaxFiles
    Fail the build if the staged tree holds more files than this. Default 120.

.PARAMETER SkipGuard
    Skip the "is a dev server still serving this tree?" port probe. Only for a machine where the probe misfires.

.EXAMPLE
    pwsh -File Tools/Build-NowUIWebBundle.ps1

.EXAMPLE
    pwsh -File Tools/Build-NowUIWebBundle.ps1 -Compress -OutputRoot D:/out/nowui-web
#>
[CmdletBinding()]
param(
    [string]$OutputRoot,
    [switch]$Compress,
    [long]$MaxBytes = 10000000,
    [int]$MaxFiles = 120,
    [switch]$SkipGuard
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot  = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$webProj   = Join-Path $repoRoot 'Standalone/Web/NowUI.Web/NowUI.Web.csproj'
$webDir    = Split-Path $webProj -Parent
$abiJs     = Join-Path $webDir 'wwwroot/nowui/abi.js'
$excludeFile = Join-Path $PSScriptRoot 'Standalone/web-bundle-exclude.txt'

if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot 'Assets/NowUI/WebBundle~' }
if (-not (Test-Path $webProj)) { throw "NowUI.Web.csproj not found at $webProj." }

function Write-Step([string]$text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }

# ------------------------------------------------------------------------------------------------------- (a) guard
# The project's hardest-won rule: NEVER publish while a dev server is serving this tree. Doing so leaves a
# zero-byte wasm that the browser refuses on an SRI mismatch, with no useful message. Enforced, not remembered.
if (-not $SkipGuard) {
    Write-Step 'Checking that nothing is serving this tree'
    foreach ($port in @(8973..8982) + @(5000, 5001)) {
        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $async = $client.BeginConnect('127.0.0.1', $port, $null, $null)
            if ($async.AsyncWaitHandle.WaitOne(120) -and $client.Connected) {
                throw "A server is listening on 127.0.0.1:$port. Stop the NowUI Web Preview " +
                      "(Tools > NowUI > Stop Web Preview) or the 'dotnet run' host before rebuilding; publishing " +
                      "over a served tree leaves a zero-byte wasm the browser refuses on an SRI mismatch. " +
                      "Re-run with -SkipGuard to override."
            }
        } finally { $client.Close() }
    }
    Write-Host "  ports 8973-8982, 5000, 5001 are clear."
}

# ------------------------------------------------------------------------------------------------------- (b) clean
# Unconditional: this is the documented recovery from the zero-byte-wasm state, and a shipped bundle built on top
# of a poisoned intermediate is exactly the failure that is impossible to diagnose from a bug report.
Write-Step 'Cleaning obj/Release and bin/Release'
foreach ($dir in @('obj/Release', 'bin/Release')) {
    $full = Join-Path $webDir $dir
    if (Test-Path $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}

# ----------------------------------------------------------------------------------------------------- (c) publish
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("nowui-web-bundle-" + [guid]::NewGuid().ToString('N'))
Write-Step "Publishing to $temp"
$compression = if ($Compress) { 'true' } else { 'false' }

& dotnet publish $webProj -c Release `
    -p:WasmFingerprintAssets=false `
    -p:WasmFingerprintDotnetJs=false `
    -p:CompressionEnabled=$compression `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $temp
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$siteRoot = Join-Path $temp 'wwwroot'
if (-not (Test-Path (Join-Path $siteRoot '_framework/blazor.boot.json'))) {
    throw "The publish produced no _framework/blazor.boot.json under $siteRoot. Nothing was staged."
}

# ------------------------------------------------------------------------------------------------------- (d) stage
Write-Step "Staging into $OutputRoot"

$patterns = @()
if (Test-Path $excludeFile) {
    foreach ($line in Get-Content -LiteralPath $excludeFile) {
        $trimmed = $line.Trim()
        if ($trimmed -and -not $trimmed.StartsWith('#')) { $patterns += $trimmed }
    }
}
foreach ($p in $patterns) {
    # _framework is SRI-hashed by blazor.boot.json. Removing anything under it bricks the bundle silently.
    if ($p -like '_framework*' -or $p -like '*/_framework/*') {
        throw "web-bundle-exclude.txt lists '$p', which is inside _framework. Files under _framework carry " +
              "SHA-256 SRI hashes in blazor.boot.json; removing one makes the browser refuse the module. " +
              "Anything that must change inside _framework changes through an MSBuild property instead."
    }
}

# A stale file from an older bundle is worse than a slow copy: it would be served, and nothing would say so.
if (Test-Path $OutputRoot) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

$excluded = [System.Collections.Generic.List[string]]::new()
$staged   = [System.Collections.Generic.List[object]]::new()
$prefix   = (Resolve-Path $siteRoot).Path.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar

foreach ($file in Get-ChildItem -LiteralPath $siteRoot -Recurse -File) {
    $rel = $file.FullName.Substring($prefix.Length).Replace('\', '/')

    $skip = $false
    foreach ($p in $patterns) { if ($rel -like $p -or (Split-Path $rel -Leaf) -like $p) { $skip = $true; break } }
    if ($skip) { $excluded.Add($rel); continue }

    $dest = Join-Path $OutputRoot $rel
    $destDir = Split-Path $dest -Parent
    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
    Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
    $staged.Add([pscustomobject]@{ Path = $rel; Bytes = $file.Length })
}

Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue

$totalBytes = ($staged | Measure-Object -Property Bytes -Sum).Sum
$totalFiles = $staged.Count

# ------------------------------------------------------------------------------------------------------- (e) stamp
Write-Step 'Stamping bundle.json'

function Try-Run([string]$exe, [string[]]$exeArgs) {
    try { $out = & $exe @exeArgs 2>$null; if ($LASTEXITCODE -eq 0 -and $out) { return ($out | Select-Object -First 1).ToString().Trim() } } catch { }
    return ''
}

Push-Location $repoRoot
try {
    $commit = Try-Run 'git' @('rev-parse', '--short', 'HEAD')
    $status = & git status --porcelain 2>$null
    $dirty  = [bool]($status -and $status.Length -gt 0)
} finally { Pop-Location }

$pkgVersion = ''
$pkgJson = Join-Path $repoRoot 'Assets/NowUI/package.json'
if (Test-Path $pkgJson) { $pkgVersion = (Get-Content -Raw -LiteralPath $pkgJson | ConvertFrom-Json).version }

# The surface hash from the JavaScript half of the ABI table - the same fold abi.js and Abi.cs both compute, so
# stamping the JS side records exactly the number a stale bundle would disagree with.
$surfaceHash = ''
if (Test-Path $abiJs) {
    $uri = ([System.Uri]((Resolve-Path $abiJs).Path)).AbsoluteUri
    $surfaceHash = Try-Run 'node' @('--input-type=module', '-e',
        "import('$uri').then(m=>console.log('0x'+(m.SURFACE_HASH>>>0).toString(16).toUpperCase().padStart(8,'0')))")
}

$stamp = [ordered]@{
    _comment      = 'Generated by Tools/Build-NowUIWebBundle.ps1. Do not edit by hand. The bundle is rebuilt ON ' +
                    'DEMAND, not on every release: when the JavaScript surface changes (surfaceHash moves), when ' +
                    'a Runtime change alters browser behaviour a user would notice, or when the .NET SDK moves. ' +
                    'A release that touches only Unity-side code ships the previous bundle.'
    nowuiVersion  = $pkgVersion
    commit        = $commit
    dirty         = $dirty
    builtUtc      = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    dotnetSdk     = (Try-Run 'dotnet' @('--version'))
    surfaceHash   = $surfaceHash
    compressed    = [bool]$Compress
    files         = [int]$totalFiles
    bytes         = [long]$totalBytes
    excluded      = @($excluded)
}
$stampPath = Join-Path $OutputRoot 'bundle.json'
($stamp | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $stampPath -Encoding UTF8
# bundle.json itself is counted from here on, so the guard measures what is actually committed.
$totalFiles += 1
$totalBytes += (Get-Item -LiteralPath $stampPath).Length

# -------------------------------------------------------------------------------------------------- (f) size guard
Write-Step 'Report'
$largest = $staged | Sort-Object -Property Bytes -Descending | Select-Object -First 10
Write-Host ("  files        {0}   ({1} payload + bundle.json)" -f $totalFiles, ($totalFiles - 1))
Write-Host ("  bytes        {0:N0}  ({1:N2} MiB)" -f $totalBytes, ($totalBytes / 1MB))
if ($excluded.Count -gt 0) { Write-Host ("  excluded     {0}" -f ($excluded -join ', ')) }
Write-Host "  largest ten:"
foreach ($f in $largest) { Write-Host ("    {0,12:N0}  {1}" -f $f.Bytes, $f.Path) }

if ($Compress) {
    $br = ($staged | Where-Object { $_.Path -like '*.br' } | Measure-Object -Property Bytes -Sum).Sum
    Write-Host ("  brotli siblings total {0:N0} B" -f $br)
}

if ($totalBytes -gt $MaxBytes) {
    throw "The staged bundle is $('{0:N0}' -f $totalBytes) B, over the -MaxBytes limit of $('{0:N0}' -f $MaxBytes) B. " +
          "Either add the offending file to Tools/Standalone/web-bundle-exclude.txt (see the largest-ten list " +
          "above) or raise -MaxBytes deliberately."
}
if ($totalFiles -gt $MaxFiles) {
    throw "The staged bundle holds $totalFiles files, over the -MaxFiles limit of $MaxFiles."
}

Write-Host ""
Write-Host "Bundle staged at $OutputRoot" -ForegroundColor Green
Write-Host "Commit it with: git add -f `"$OutputRoot`"" -ForegroundColor Green
