#requires -Version 7.0

<#
.SYNOPSIS
    Differences a browser render against its Unity reference and gates on a stated tolerance.

.DESCRIPTION
    Given two PNGs of identical size - typically Unity's render of a scene and the browser's render of the same
    scene - it reports, per channel and overall:

        max          the single largest absolute difference. Diagnostic only; see "Why not the maximum" below.
        mean         the average absolute difference across every pixel.
        p99          the 99th percentile absolute difference.
        differing    how many pixels differ by more than -Threshold on any channel, as a count and a fraction,
                     and the bounding box those pixels occupy.

    It writes a diff image, and exits non-zero when a scene is outside tolerance.

.NOTES
    WHY NOT THE MAXIMUM.
    Peak difference is the wrong statistic for anything containing text, and this is measured rather than assumed.
    Docs/Standalone/M2-Scouting.md records the quick-start comparison: median 2/255 across the whole image, and a
    maximum of 252/255 - at x=415, which is precisely the one-pixel overhang where the browser's glyph run ends a
    pixel wider than Unity's at 2x. On a hard black-to-white glyph edge a HALF-pixel disagreement produces a
    255-wide channel difference. Two independent rasterisers of the same distance field are expected to disagree
    by half a pixel. So a gate on the maximum would fail every text scene forever while telling you nothing, and
    lowering it until things passed would be tuning the instrument to the answer.

    WHERE THE DEFAULTS COME FROM.
    -Threshold 8. The same per-channel tolerance NowVisualHarnessRunner's own golden comparison uses for its
    default scenarios (GoldenComparisonTolerance(8, 0.01f)). Adopting the project's existing number rather than
    inventing one keeps the parity gate on the same scale as the oracle the repository already trusts.

    -MaxDifferingRatio 0.02. Twice the 1% that same comparison allows, and the reason for the factor is the
    difference in what is being compared: Unity-against-Unity differs only by GPU non-determinism, where
    Unity-against-browser differs by two independent rasterisers, two independent glyph-quad roundings and two
    independent derivative evaluations. Glyph outline pixels are where that lands, and in these scenes text
    covers of the order of one to two percent of the image. 2% is therefore the smallest round number that does
    not fail on antialiasing alone. It is a ceiling on how much of the image may disagree, not a licence: a real
    porting error moves this number by a lot, not by a little - an unported gradient branch turns every glyph
    body from a ramp to a flat fill, which is tens of percent, and a wrong blend function moves EVERY pixel.

    -MaxPercentile99 16. Two channel tolerances. The distribution this catches is the one the ratio does not:
    a difference that is small everywhere rather than large somewhere - a blend function off by a gamma step, a
    clear colour that does not match, a colour-space conversion applied once too often. Those move the bulk of
    the histogram while leaving few pixels over -Threshold.

    THESE NUMBERS ARE A STARTING POSITION, NOT A RESULT. They are defensible a priori, which is what a gate needs
    before it has data. Once every shader is ported and a full run exists, the right move is to read the measured
    distribution off -Json and tighten them per scene - tighter for the flat-fill scenes, which should be nearly
    exact, and no looser for the text ones. Run with -NoFail to measure without gating while that is being done.

.PARAMETER Reference
    The Unity render: a PNG, or a directory of them (from `NowUI-Harness.ps1 -Mode Visual`).

.PARAMETER Candidate
    The browser render: a PNG, or a directory of them. A directory is paired with the reference directory by
    file name, and only names present in both are compared; names present in only one are reported.

.PARAMETER DiffPath
    Where to write the diff image, or the directory to write them into. Defaults to beside the candidate,
    named "<candidate>.diff.png".

.PARAMETER Threshold
    Per-channel absolute difference above which a pixel counts as differing. Default 8.

.PARAMETER MaxDifferingRatio
    The largest fraction of differing pixels a scene may have and still pass. Default 0.02.

.PARAMETER MaxPercentile99
    The largest 99th-percentile per-channel difference a scene may have and still pass. Default 16.

.PARAMETER Json
    Optional path for a machine-readable report of every measurement.

.PARAMETER NoFail
    Measure and report, but always exit 0. For establishing a distribution before choosing a tolerance.

.EXAMPLE
    pwsh -File Tools/Standalone/Compare-Renders.ps1 `
        -Reference artifacts/local/parity-unity/parity-rect-fill.png `
        -Candidate artifacts/local/parity-web/parity-rect-fill.png

.EXAMPLE
    pwsh -File Tools/Standalone/Compare-Renders.ps1 `
        -Reference artifacts/local/parity-unity -Candidate artifacts/local/parity-web `
        -DiffPath artifacts/local/parity-diff -Json artifacts/local/parity-diff/report.json
#>

param(
    [Parameter(Mandatory = $true)]
    [string] $Reference,

    [Parameter(Mandatory = $true)]
    [string] $Candidate,

    [Parameter(Mandatory = $false)]
    [string] $DiffPath,

    [Parameter(Mandatory = $false)]
    [ValidateRange(0, 255)]
    [int] $Threshold = 8,

    [Parameter(Mandatory = $false)]
    [ValidateRange(0.0, 1.0)]
    [double] $MaxDifferingRatio = 0.02,

    [Parameter(Mandatory = $false)]
    [ValidateRange(0, 255)]
    [int] $MaxPercentile99 = 16,

    [Parameter(Mandatory = $false)]
    [string] $Json,

    [Parameter(Mandatory = $false)]
    [switch] $NoFail
)

$ErrorActionPreference = "Stop"

# System.Drawing is the only PNG codec in the box, and on .NET it is Windows-only by design. Said out loud here
# rather than left to surface as a PlatformNotSupportedException from inside a pixel loop: if this gate ever has
# to run on CI Linux, the fix is a small .NET tool alongside Tools/Standalone/ApiDump, not a workaround here.
if (-not $IsWindows) {
    throw "Compare-Renders.ps1 needs System.Drawing, which is Windows-only on .NET. Port the comparison to a .NET tool (see Tools/Standalone/ApiDump) to run it elsewhere."
}

Add-Type -AssemblyName System.Drawing

function Read-Pixels {
    <#
        Returns @{ width; height; bytes } with bytes in BGRA order, four per pixel, top row first.
        LockBits rather than GetPixel: a 960x640 capture is 614400 pixels, and GetPixel in PowerShell would take
        minutes per image.
    #>
    param([string] $Path)

    $bitmap = [System.Drawing.Bitmap]::new($Path)
    try {
        $rect = [System.Drawing.Rectangle]::new(0, 0, $bitmap.Width, $bitmap.Height)
        $data = $bitmap.LockBits(
            $rect,
            [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

        try {
            $stride = [Math]::Abs($data.Stride)
            $rowBytes = $bitmap.Width * 4
            $bytes = [byte[]]::new($rowBytes * $bitmap.Height)

            for ($y = 0; $y -lt $bitmap.Height; $y++) {
                $source = [IntPtr]::Add($data.Scan0, $y * $stride)
                [System.Runtime.InteropServices.Marshal]::Copy($source, $bytes, $y * $rowBytes, $rowBytes)
            }

            return @{ width = $bitmap.Width; height = $bitmap.Height; bytes = $bytes }
        }
        finally {
            $bitmap.UnlockBits($data)
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

function Write-DiffImage {
    <#
        The candidate, darkened, with every differing pixel painted red at an intensity proportional to how far
        off it is. Darkening the base rather than showing a bare difference map keeps the marks locatable: "the
        top-right corner of the third tile" is actionable, a constellation of red dots on black is not.
    #>
    param(
        [hashtable] $Candidate,
        [byte[]] $Worst,
        [string] $Path
    )

    $width = $Candidate.width
    $height = $Candidate.height
    $bitmap = [System.Drawing.Bitmap]::new($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    try {
        $rect = [System.Drawing.Rectangle]::new(0, 0, $width, $height)
        $data = $bitmap.LockBits(
            $rect,
            [System.Drawing.Imaging.ImageLockMode]::WriteOnly,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

        try {
            $rowBytes = $width * 4
            $out = [byte[]]::new($rowBytes * $height)
            $source = $Candidate.bytes

            for ($p = 0; $p -lt ($width * $height); $p++) {
                $i = $p * 4
                $delta = $Worst[$p]

                if ($delta -eq 0) {
                    # BGRA. A quarter-brightness copy of the candidate: enough to see the layout, dim enough that
                    # the marks read as marks.
                    $out[$i] = [byte]($source[$i] * 0.25)
                    $out[$i + 1] = [byte]($source[$i + 1] * 0.25)
                    $out[$i + 2] = [byte]($source[$i + 2] * 0.25)
                }
                else {
                    # Red, ramped from a visible floor so a difference of 1 is still findable by eye.
                    $intensity = [byte][Math]::Min(255, 80 + $delta * 2)
                    $out[$i] = 0
                    $out[$i + 1] = 0
                    $out[$i + 2] = $intensity
                }

                $out[$i + 3] = 255
            }

            $stride = [Math]::Abs($data.Stride)
            for ($y = 0; $y -lt $height; $y++) {
                $destination = [IntPtr]::Add($data.Scan0, $y * $stride)
                [System.Runtime.InteropServices.Marshal]::Copy($out, $y * $rowBytes, $destination, $rowBytes)
            }
        }
        finally {
            $bitmap.UnlockBits($data)
        }

        $directory = Split-Path -Parent $Path
        if (![string]::IsNullOrWhiteSpace($directory) -and !(Test-Path -LiteralPath $directory)) {
            New-Item -ItemType Directory -Force -Path $directory | Out-Null
        }

        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

function Compare-OnePair {
    param(
        [string] $Name,
        [string] $ReferencePath,
        [string] $CandidatePath,
        [string] $DiffOutputPath
    )

    $reference = Read-Pixels -Path $ReferencePath
    $candidate = Read-Pixels -Path $CandidatePath

    if ($reference.width -ne $candidate.width -or $reference.height -ne $candidate.height) {
        # Not a tolerance failure and not comparable: a size mismatch means the two hosts were asked for
        # different things (usually the browser at ?dpr=1 against a Unity render at renderScale 2), and every
        # per-pixel number below would be meaningless.
        return [pscustomobject]@{
            name = $Name
            status = "size-mismatch"
            detail = "reference $($reference.width)x$($reference.height), candidate $($candidate.width)x$($candidate.height)"
            passed = $false
        }
    }

    $width = $reference.width
    $height = $reference.height
    $pixelCount = $width * $height

    $a = $reference.bytes
    $b = $candidate.bytes

    # Channel order in the buffers is BGRA; the report is written in RGBA order, so this maps one to the other.
    $channelOffsets = @(2, 1, 0, 3)
    $channelNames = @("R", "G", "B", "A")

    # A histogram per channel rather than a list of every difference: 256 buckets give an exact percentile over
    # an integer-valued quantity, in constant memory, in one pass.
    $histograms = @()
    for ($c = 0; $c -lt 4; $c++) { $histograms += , ([long[]]::new(256)) }

    $worst = [byte[]]::new($pixelCount)

    $differing = 0
    $minX = [int]::MaxValue; $minY = [int]::MaxValue; $maxX = -1; $maxY = -1

    for ($p = 0; $p -lt $pixelCount; $p++) {
        $i = $p * 4
        $pixelWorst = 0

        for ($c = 0; $c -lt 4; $c++) {
            $o = $i + $channelOffsets[$c]
            $d = [Math]::Abs([int]$a[$o] - [int]$b[$o])
            $histograms[$c][$d]++
            if ($d -gt $pixelWorst) { $pixelWorst = $d }
        }

        $worst[$p] = [byte]$pixelWorst

        if ($pixelWorst -gt $Threshold) {
            $differing++
            $x = $p % $width
            $y = [int][Math]::Floor($p / $width)
            if ($x -lt $minX) { $minX = $x }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }

    $channels = @()
    $overallMax = 0
    $overallP99 = 0
    $overallMeanSum = 0.0

    for ($c = 0; $c -lt 4; $c++) {
        $histogram = $histograms[$c]

        $max = 0
        $sum = 0.0
        for ($d = 0; $d -lt 256; $d++) {
            if ($histogram[$d] -gt 0) { $max = $d }
            $sum += [double]$d * $histogram[$d]
        }

        # The 99th percentile: the smallest difference at or below which 99% of this channel's pixels fall.
        $target = [long][Math]::Ceiling($pixelCount * 0.99)
        $running = [long]0
        $p99 = 0
        for ($d = 0; $d -lt 256; $d++) {
            $running += $histogram[$d]
            if ($running -ge $target) { $p99 = $d; break }
        }

        $mean = $sum / $pixelCount

        $channels += [pscustomobject]@{
            channel = $channelNames[$c]
            max = $max
            mean = [Math]::Round($mean, 4)
            p99 = $p99
        }

        if ($max -gt $overallMax) { $overallMax = $max }
        if ($p99 -gt $overallP99) { $overallP99 = $p99 }
        $overallMeanSum += $mean
    }

    $ratio = $differing / [double]$pixelCount

    $boundingBox = $null
    if ($differing -gt 0) {
        $boundingBox = [pscustomobject]@{
            x = $minX
            y = $minY
            width = $maxX - $minX + 1
            height = $maxY - $minY + 1
        }
    }

    Write-DiffImage -Candidate $candidate -Worst $worst -Path $DiffOutputPath

    $failures = @()
    if ($ratio -gt $MaxDifferingRatio) {
        $failures += "differing ratio {0:P4} exceeds {1:P4}" -f $ratio, $MaxDifferingRatio
    }
    if ($overallP99 -gt $MaxPercentile99) {
        $failures += "99th percentile $overallP99 exceeds $MaxPercentile99"
    }

    return [pscustomobject]@{
        name = $Name
        status = if ($failures.Count -eq 0) { "pass" } else { "fail" }
        passed = $failures.Count -eq 0
        detail = ($failures -join "; ")
        width = $width
        height = $height
        pixels = $pixelCount
        threshold = $Threshold
        differingPixels = $differing
        differingRatio = [Math]::Round($ratio, 6)
        boundingBox = $boundingBox
        maxDifference = $overallMax
        meanDifference = [Math]::Round($overallMeanSum / 4.0, 4)
        percentile99 = $overallP99
        channels = $channels
        referencePath = (Resolve-Path -LiteralPath $ReferencePath).Path
        candidatePath = (Resolve-Path -LiteralPath $CandidatePath).Path
        diffPath = $DiffOutputPath
    }
}

# ------------------------------------------------------------------------------------------------ pair up work

if (!(Test-Path -LiteralPath $Reference)) { throw "Reference not found: '$Reference'." }
if (!(Test-Path -LiteralPath $Candidate)) { throw "Candidate not found: '$Candidate'." }

$referenceIsDirectory = (Get-Item -LiteralPath $Reference).PSIsContainer
$candidateIsDirectory = (Get-Item -LiteralPath $Candidate).PSIsContainer

if ($referenceIsDirectory -ne $candidateIsDirectory) {
    throw "Reference and candidate must both be files or both be directories."
}

$pairs = [System.Collections.Generic.List[object]]::new()
$missing = [System.Collections.Generic.List[string]]::new()

if ($referenceIsDirectory) {
    $referenceFiles = Get-ChildItem -LiteralPath $Reference -Filter *.png -File |
        Where-Object { $_.Name -notlike "*.diff.png" } |
        Sort-Object Name

    foreach ($file in $referenceFiles) {
        $candidateFile = Join-Path $Candidate $file.Name
        if (!(Test-Path -LiteralPath $candidateFile)) {
            $missing.Add("$($file.BaseName): no candidate at $candidateFile")
            continue
        }

        $diffDirectory = if ([string]::IsNullOrWhiteSpace($DiffPath)) { $Candidate } else { $DiffPath }
        $pairs.Add(@{
            name = $file.BaseName
            reference = $file.FullName
            candidate = $candidateFile
            diff = (Join-Path $diffDirectory "$($file.BaseName).diff.png")
        })
    }
}
else {
    $referenceItem = Get-Item -LiteralPath $Reference
    $diff = if ([string]::IsNullOrWhiteSpace($DiffPath)) {
        Join-Path (Split-Path -Parent (Resolve-Path -LiteralPath $Candidate).Path) `
            ("{0}.diff.png" -f [System.IO.Path]::GetFileNameWithoutExtension($Candidate))
    }
    elseif ((Test-Path -LiteralPath $DiffPath) -and (Get-Item -LiteralPath $DiffPath).PSIsContainer) {
        Join-Path $DiffPath ("{0}.diff.png" -f [System.IO.Path]::GetFileNameWithoutExtension($Candidate))
    }
    else { $DiffPath }

    $pairs.Add(@{
        name = $referenceItem.BaseName
        reference = (Resolve-Path -LiteralPath $Reference).Path
        candidate = (Resolve-Path -LiteralPath $Candidate).Path
        diff = $diff
    })
}

if ($pairs.Count -eq 0) {
    throw "Nothing to compare. $($missing -join '; ')"
}

# ------------------------------------------------------------------------------------------------------- run

$results = [System.Collections.Generic.List[object]]::new()

foreach ($pair in $pairs) {
    $results.Add((Compare-OnePair `
        -Name $pair.name `
        -ReferencePath $pair.reference `
        -CandidatePath $pair.candidate `
        -DiffOutputPath $pair.diff))
}

# ---------------------------------------------------------------------------------------------------- report

Write-Output ""
Write-Output ("Parity comparison - threshold {0}/255, allowed differing {1:P2}, allowed p99 {2}/255" -f `
    $Threshold, $MaxDifferingRatio, $MaxPercentile99)
Write-Output ("-" * 96)
Write-Output ("{0,-26} {1,7} {2,7} {3,7} {4,10} {5,9}  {6}" -f "scene", "max", "mean", "p99", "differing", "ratio", "result")
Write-Output ("-" * 96)

foreach ($result in $results) {
    if ($result.status -eq "size-mismatch") {
        Write-Output ("{0,-26} {1}" -f $result.name, "SIZE MISMATCH - $($result.detail)")
        continue
    }

    $box = if ($null -eq $result.boundingBox) { "" } else {
        " box ({0},{1}) {2}x{3}" -f $result.boundingBox.x, $result.boundingBox.y, $result.boundingBox.width, $result.boundingBox.height
    }

    Write-Output ("{0,-26} {1,7} {2,7:0.000} {3,7} {4,10} {5,8:P3}  {6}{7}" -f `
        $result.name,
        $result.maxDifference,
        $result.meanDifference,
        $result.percentile99,
        $result.differingPixels,
        $result.differingRatio,
        $result.status.ToUpperInvariant(),
        $box)

    if ($result.status -eq "fail") {
        Write-Output ("{0,-26}   {1}" -f "", $result.detail)
    }

    foreach ($channel in $result.channels) {
        Write-Output ("{0,-26}   {1}: max {2,3}  mean {3,7:0.000}  p99 {4,3}" -f `
            "", $channel.channel, $channel.max, $channel.mean, $channel.p99)
    }
}

foreach ($line in $missing) {
    Write-Output ("MISSING  {0}" -f $line)
}

Write-Output ("-" * 96)

if (![string]::IsNullOrWhiteSpace($Json)) {
    $jsonDirectory = Split-Path -Parent $Json
    if (![string]::IsNullOrWhiteSpace($jsonDirectory) -and !(Test-Path -LiteralPath $jsonDirectory)) {
        New-Item -ItemType Directory -Force -Path $jsonDirectory | Out-Null
    }

    [pscustomobject]@{
        threshold = $Threshold
        maxDifferingRatio = $MaxDifferingRatio
        maxPercentile99 = $MaxPercentile99
        missing = @($missing)
        scenes = @($results)
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Json -Encoding utf8

    Write-Output "Report: $Json"
}

$failed = @($results | Where-Object { -not $_.passed })
$failedCount = $failed.Count + $missing.Count

if ($failedCount -eq 0) {
    Write-Output "$($results.Count) scene(s) within tolerance."
}
else {
    Write-Output "$failedCount of $($results.Count + $missing.Count) scene(s) outside tolerance or missing."
}

if ($NoFail) {
    Write-Output "-NoFail: exiting 0 regardless."
    exit 0
}

exit ($failedCount -eq 0 ? 0 : 1)
