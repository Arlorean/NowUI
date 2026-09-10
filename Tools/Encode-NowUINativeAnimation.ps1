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
& (Join-Path $PSScriptRoot '../Assets/NowUI/Native~/encode-animation.ps1') @PSBoundParameters
