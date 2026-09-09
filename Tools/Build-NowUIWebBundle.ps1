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

    # Store the staged tree raw instead of brotli-compressed. The shipped bundle is compressed, because that is a
    # third of the size in git; pass this when producing a tree for a host that cannot serve it back.
    [switch]$NoCompressBundle,

    # Ship the fonts with their OpenType layout tables intact. They are dropped by default because
    # this host's parser never reads them and HarfBuzz is not linked into wasm; pass this if either
    # of those ever stops being true. See the (d1) block for the two file:line reasons.
    [switch]$NoLeanFonts,
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
Write-Step 'Cleaning obj/Release, bin/Release and the generated wwwroot/Fixtures'
foreach ($dir in @('obj/Release', 'bin/Release')) {
    $full = Join-Path $webDir $dir
    if (Test-Path $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}

# wwwroot/Fixtures is a MIRROR of Standalone/Tests/Fixtures, repopulated by the csproj's CopyNowUIFixtures target
# on every build. It is wiped here rather than merged there because a Copy only ever adds: a fixture the Unity
# export STOPS producing would otherwise sit in wwwroot forever and keep shipping, and the target cannot delete it
# itself - the static-web-assets glob is evaluated before any target runs, so a mid-build delete fails the publish
# with "No file exists for the asset". That is not hypothetical: four stale 200 KB .page0.bin sidecars went on being
# staged into the package the day the baked atlas pages were dropped from the export, by a build whose own report
# said "no baked pages in the staged fixtures". See the comment on CopyNowUIFixtures.
$fixtureMirror = Join-Path $webDir 'wwwroot/Fixtures'
if (Test-Path $fixtureMirror) {
    Remove-Item -LiteralPath $fixtureMirror -Recurse -Force
    Write-Host "  wiped $fixtureMirror; the build repopulates it from Standalone/Tests/Fixtures."
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

# --------------------------------------------------------------------------------------------- (d1) lean fonts
#
# Strip the OpenType LAYOUT tables from the four NotoSans faces in the staged tree. 380,791 B of brotli, 13.4% of
# the whole bundle, for a cost that is provably zero IN THIS HOST - more than every JavaScript file in the bundle
# combined.
#
# WHY IT IS FREE HERE. The browser build never reads those tables. Two independent reasons, both worth naming
# because the saving stops being free the day either changes:
#
#   1. Assets/NowUI/Runtime/NowTrueType.cs:158-220 - the managed TrueType parser, which is what this host uses,
#      matches exactly EIGHT table tags: head, maxp, hhea, hmtx, cmap, loca, glyf and post. GPOS, GSUB, GDEF and
#      kern are never looked up.
#   2. Native/build-msdf-webgl.sh:7 - "HarfBuzz: NOT bundled". Shaping resolves against a native library that does
#      not exist in wasm, so NowTextShaper.supported is false and every draw takes the per-codepoint path.
#
# IF ANYONE LINKS HARFBUZZ INTO THE WASM HOST, DELETE THIS STEP. Lean faces would then silently lose kerning and
# ligatures, which is the invisible kind of wrong.
#
# WHAT IS NOT TOUCHED, and this is the important half: the cmap. Every one of the 3,093 codepoints each face maps
# still maps, to the same glyph, with the same advance and the same outline - verified codepoint by codepoint
# against the parser's own read path, 12,366 comparisons with zero differences, and pixel-identical across 15
# gallery areas. Subsetting the cmap was measured and REJECTED: it saves twice as much and drops 86% of the
# codepoints, and a missing glyph here renders as nothing at all, with no tofu, no advance and no console message,
# so "Viet" loses a letter mid-word and Greek and Cyrillic become blank space while eight of nine gallery areas
# still look pixel-perfect. A saving CI can see and damage it cannot is the wrong trade.
#
# ONLY THE BROWSER BUNDLE. The Unity-side faces under Assets/NowUI/Assets/Fonts must keep their layout tables:
# the Unity player DOES have HarfBuzz, so leaning those would cost real kerning and ligatures in a built game.
#
# ONE MORE THING LEANING MUST NOT DO, added when the faces started shipping baked atlas pages: it must not RENUMBER
# THE GLYPH IDS. The pages are baked in Unity from the full face and carry a record per glyph index alongside the
# record per codepoint, so a renumbering would silently repoint them at other outlines. It does not today - the
# check after the subset run proves it, 380 comparisons with zero mismatches - and that check is there so the day it
# does, the build fails instead of the text.

if (-not $NoLeanFonts) {
    Write-Step 'Leaning the fonts (layout tables dropped, every codepoint kept)'

    $python = $null
    foreach ($candidate in @('python', 'python3', 'py')) {
        try { & $candidate -c "import fontTools" 2>$null; if ($LASTEXITCODE -eq 0) { $python = $candidate; break } } catch { }
    }

    if ($null -eq $python) {
        Write-Warning ("fontTools was not found, so the fonts ship FULL SIZE (about 381 KB larger). " +
                       "Install it with 'pip install fonttools' and rebuild, or pass -NoLeanFonts to silence this.")
    }
    else {
        $faceBefore = 0L
        $faceAfter  = 0L

        foreach ($entry in @($staged)) {
            if ($entry.Path -notlike '*.ttf') { continue }

            $ttf = Join-Path $OutputRoot $entry.Path
            if (-not (Test-Path -LiteralPath $ttf)) { continue }

            $faceBefore += $entry.Bytes

            & $python -m fontTools.subset $ttf --unicodes=* --output-file=$ttf `
                --layout-features= --drop-tables+=GPOS,GSUB,GDEF,DSIG,FFTM `
                --no-hinting --notdef-outline --name-IDs=* --recalc-bounds 2>$null

            if ($LASTEXITCODE -ne 0) { throw "fontTools.subset failed on $($entry.Path)." }

            $now = (Get-Item -LiteralPath $ttf).Length
            $entry.Bytes = $now
            $faceAfter += $now

            # The manifest declares the source length and the loader ENFORCES it: WebResourceProvider.cs:562
            # throws "declares N source bytes but the fetch returned M" and the page dies at boot. So the
            # declaration has to move with the file.
            $manifest = [IO.Path]::ChangeExtension($ttf, $null).TrimEnd('.') + '.font.json'
            if (Test-Path -LiteralPath $manifest) {
                $text = Get-Content -Raw -LiteralPath $manifest
                $patched = [Text.RegularExpressions.Regex]::Replace(
                    $text, '("fontByteCount"\s*:\s*)\d+', ('${1}' + $now))
                if ($patched -ne $text) {
                    Set-Content -LiteralPath $manifest -Value $patched -NoNewline -Encoding UTF8
                    foreach ($m in @($staged)) {
                        if ($m.Path -eq ($entry.Path -replace '\.ttf$', '.font.json')) {
                            $m.Bytes = (Get-Item -LiteralPath $manifest).Length
                        }
                    }
                }
                else {
                    throw "No fontByteCount to rewrite in $manifest - the loader would refuse the leaned face."
                }
            }
        }

        if ($faceBefore -gt 0) {
            Write-Host ("  four faces: {0:N0} B -> {1:N0} B ({2:P0}), every codepoint kept" -f `
                $faceBefore, $faceAfter, ($faceAfter / [double]$faceBefore))
        }

        # The one thing leaning could break that nothing else would notice. The baked atlas pages are baked in Unity
        # from the FULL face and carry glyph-INDEX records (keyed -1 - glyphIndex); a subsetter is entitled to
        # renumber glyph ids, and if it did, those records would point at the wrong outlines - right advance, right
        # position, wrong letter, no error. Measured today at 380 comparisons with zero mismatches, so this is a
        # tripwire and not a fix. See Tools/Check-LeanedGlyphIndices.py for what to do if it ever fires.
        $fixtureFonts = Join-Path $OutputRoot 'Fixtures/NowUI'
        if (Test-Path -LiteralPath $fixtureFonts) {
            & $python (Join-Path $PSScriptRoot 'Check-LeanedGlyphIndices.py') $fixtureFonts
            if ($LASTEXITCODE -ne 0) {
                throw "Leaning the fonts renumbered glyph ids the baked pages were baked against (see above). " +
                      "The bundle would render the wrong glyphs for shaped text; re-export, or drop the " +
                      "negative-keyed records from the pages."
            }
        }
    }
}

# ------------------------------------------------------------------------------------------------ (d2) squeeze
#
# Store the staged tree brotli-compressed and DROP the raw original, which is what makes the committed bundle a
# third of its published size: 8,570,227 B becomes 2,845,658 B, losslessly, in a folder that lives in git forever.
#
# This is a SIZE decision, not a transfer one, and that is why it is right even though the Editor's server runs on
# loopback where transfer costs nothing. Every clone of this repository, and every UPM git install, pays the raw
# size otherwise.
#
# NowWebPreviewServer.ServeFile is the other half: it serves NAME.br for a request for NAME, under
# Content-Encoding: br when the client accepts brotli, and inflates it here when it does not. Integrity is
# unaffected - _framework/blazor.boot.json holds SRI hashes of the RAW bytes, and a browser verifies integrity
# after decoding a content encoding, so the hashes still match and nothing under _framework/ needs excluding.
#
# Two files stay raw on purpose:
#   index.html    the very first request, before anything of ours is running, so it must need no cooperation.
#   bundle.json   read by the Editor window and by tooling with plain file I/O, not through the server.
# Files that do not get meaningfully smaller stay raw too: a PNG or JPEG is already compressed, and a .br sibling
# that saves nothing is a second copy for no reason.

if (-not $NoCompressBundle) {
    Write-Step 'Compressing the staged tree (brotli, raw originals dropped)'

    Add-Type -AssemblyName System.IO.Compression | Out-Null

    # index.html      the very first request, before anything of ours runs, so it must need no cooperation.
    # bundle.json     read by the Editor window with plain file I/O, never through the server.
    # blazor.boot.json  the .NET loader's own manifest AND the marker NowWebPreviewPaths.FindBundleRoot looks
    #                 for. It is fetched before the loader is in a position to report anything useful, and a
    #                 folder whose marker is missing is not recognised as a bundle at all - which is exactly how
    #                 this was caught. A few KB is a cheap price for both.
    $keepRaw = @('index.html', 'bundle.json', 'blazor.boot.json')
    $rawTotal = 0L
    $newTotal = 0L
    $squeezed = 0

    foreach ($entry in @($staged)) {
        $full = Join-Path $OutputRoot $entry.Path
        if (-not (Test-Path -LiteralPath $full)) { continue }

        $rawTotal += $entry.Bytes

        if ($keepRaw -contains (Split-Path $entry.Path -Leaf)) { $newTotal += $entry.Bytes; continue }

        $bytes = [IO.File]::ReadAllBytes($full)
        $ms = New-Object IO.MemoryStream
        $bs = New-Object IO.Compression.BrotliStream($ms, [IO.Compression.CompressionLevel]::SmallestSize, $true)
        $bs.Write($bytes, 0, $bytes.Length)
        $bs.Dispose()
        $packed = $ms.ToArray()
        $ms.Dispose()

        # A tenth off is the floor worth a second filename. Below it the raw file stays.
        if ($packed.Length -ge [int]($entry.Bytes * 0.9)) { $newTotal += $entry.Bytes; continue }

        [IO.File]::WriteAllBytes($full + '.br', $packed)
        Remove-Item -LiteralPath $full -Force
        $entry.Path = $entry.Path + '.br'
        $entry.Bytes = $packed.Length
        $newTotal += $packed.Length
        $squeezed++
    }

    Write-Host ("  {0} of {1} files stored compressed: {2:N0} B -> {3:N0} B ({4:P0})" -f `
        $squeezed, $staged.Count, $rawTotal, $newTotal, ($newTotal / [double]$rawTotal))
}

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
    bundleBrotli  = -not [bool]$NoCompressBundle
    leanFonts     = -not [bool]$NoLeanFonts
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
