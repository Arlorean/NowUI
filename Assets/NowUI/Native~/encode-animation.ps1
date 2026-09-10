#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Frames,
    [Parameter(Mandatory = $true)][string] $Output,
    [string] $Python = 'python',
    [ValidateRange(0, 100)][int] $Quality = 80,
    [ValidateRange(0, 6)][int] $Method = 6
)
$ErrorActionPreference = 'Stop'
$nativeFrames = (Resolve-Path -LiteralPath $Frames).Path
$nativeRecording = Get-Content -LiteralPath (Join-Path $nativeFrames 'animation.json') -Raw | ConvertFrom-Json
if ($nativeRecording.frames -le 0 -or $nativeRecording.fps -le 0 -or $nativeRecording.pattern -ne 'frame-%06d.png') {
    throw 'Expected a native animation.json manifest with positive frames/fps and frame-%06d.png pattern.'
}
$nativeOutput = [IO.Path]::GetFullPath($Output)
if ([IO.Path]::GetExtension($nativeOutput) -ne '.webp') { throw 'Output must have a .webp extension.' }
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($nativeOutput)) -Force | Out-Null
$nativeTemporary = $nativeOutput + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    & $Python (Join-Path $PSScriptRoot 'encode-webp.py') --frames $nativeFrames --pattern $nativeRecording.pattern --count $nativeRecording.frames --fps $nativeRecording.fps --quality $Quality --method $Method --output $nativeTemporary
    if ($LASTEXITCODE -ne 0) { throw "Animation encoding failed ($LASTEXITCODE). Install Python 3 with Pillow or pass -Python <executable>." }
    [IO.File]::Move($nativeTemporary, $nativeOutput, $true)
} finally {
    if ([IO.File]::Exists($nativeTemporary)) { [IO.File]::Delete($nativeTemporary) }
}
Write-Output $nativeOutput
