#requires -Version 7.0
<#
.SYNOPSIS
    Captures the native sample's exact C# content through Unity for render comparison.
.DESCRIPTION
    Runs a graphics-enabled Unity batch process. The project's color space must be
    Gamma, matching the native renderer; no project settings are changed. Only a
    uniquely named staging folder under Assets is created and removed. The output
    PNG, metadata JSON, and Unity log remain outside Assets.
.EXAMPLE
    ./Tools/Standalone/Capture-NativeReference.ps1
.EXAMPLE
    ./Tools/Standalone/Capture-NativeReference.ps1 -Scene PixelAlignment -Width 960 -Height 640 -Output artifacts/local/native-comparison/alignment-unity.png
#>
[CmdletBinding()]
param(
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Unity.exe',
    [string] $Output = 'artifacts/local/native-comparison/unity.png',
    [ValidateSet('Interactive', 'PixelAlignment')] [string] $Scene = 'Interactive',
    [ValidateRange(1, 8192)] [int] $Width = 1100,
    [ValidateRange(1, 8192)] [int] $Height = 760,
    [ValidateRange(30, 1800)] [int] $TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assetsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Assets'))
$stagingName = '__NowUINativeReference_' + [Guid]::NewGuid().ToString('N')
$stagingRoot = [IO.Path]::GetFullPath((Join-Path $assetsRoot $stagingName))
$outputPath = [IO.Path]::GetFullPath($Output, $projectRoot)
$logPath = [IO.Path]::ChangeExtension($outputPath, '.log')
$metadataPath = [IO.Path]::ChangeExtension($outputPath, '.json')
$unityProcess = $null

if (-not (Test-Path -LiteralPath $Unity -PathType Leaf)) { throw "Unity executable not found: $Unity" }
if ([long]$Width * $Height -gt 16777216) { throw 'Capture is limited to 16,777,216 pixels.' }
if (Get-Process -Name Unity -ErrorAction SilentlyContinue) {
    throw 'Close the Unity Editor before starting the isolated reference capture.'
}
if ((Get-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectSettings.asset') -Raw) -notmatch 'm_ActiveColorSpace:\s*0\b') {
    throw 'The native reference requires an existing Gamma project. The script does not change project settings.'
}

try {
    New-Item -ItemType Directory -Path $stagingRoot | Out-Null
    New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Standalone/Samples/NativePreview/InteractiveContent.cs') -Destination (Join-Path $stagingRoot 'InteractiveContent.cs')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Standalone/Samples/NativePreview/PixelAlignmentContent.cs') -Destination (Join-Path $stagingRoot 'PixelAlignmentContent.cs')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeReferenceCapture.cs') -Destination (Join-Path $stagingRoot 'NativeReferenceCapture.cs')
    @{
        name = 'NowUI.NativeReferenceCapture'
        references = @('NowUI.Runtime')
        includePlatforms = @('Editor')
        autoReferenced = $false
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stagingRoot 'NowUI.NativeReferenceCapture.asmdef') -Encoding utf8

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = [IO.Path]::GetFullPath($Unity)
    $startInfo.WorkingDirectory = $projectRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($argument in @('-batchmode', '-quit', '-projectPath', $projectRoot, '-executeMethod', 'NowUI.NativeReferenceCapture.Capture', '-logFile', $logPath,
        '-nowuiCaptureOutput', $outputPath, '-nowuiCaptureWidth', "$Width", '-nowuiCaptureHeight', "$Height", '-nowuiCaptureScene', $Scene)) {
        $startInfo.ArgumentList.Add($argument)
    }
    $unityProcess = [Diagnostics.Process]::Start($startInfo)
    Write-Host "Unity reference capture PID $($unityProcess.Id). Log: $logPath"
    if (-not $unityProcess.WaitForExit($TimeoutSeconds * 1000)) {
        $unityProcess.Kill($true)
        $unityProcess.WaitForExit()
        throw "Unity capture exceeded $TimeoutSeconds seconds. See $logPath"
    }
    if ($unityProcess.ExitCode -ne 0) { throw "Unity exited with code $($unityProcess.ExitCode). See $logPath" }
    if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf) -or -not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
        throw "Unity did not write the capture and metadata. See $logPath"
    }
    Write-Host "Reference: $outputPath"
    Write-Host "Metadata: $metadataPath"
}
finally {
    if ($null -ne $unityProcess) {
        # If this script is interrupted, stop only its own batch process before
        # removing the sources it may still be importing.
        if (-not $unityProcess.HasExited) {
            $unityProcess.Kill($true)
            $unityProcess.WaitForExit()
        }
        $unityProcess.Dispose()
    }
    # Verify the final absolute target before recursive removal. Never enumerate
    # or delete other Assets content, and use only literal paths in this shell.
    $checkedStagingRoot = [IO.Path]::GetFullPath($stagingRoot)
    if ([IO.Path]::GetDirectoryName($checkedStagingRoot) -ne $assetsRoot -or
        [IO.Path]::GetFileName($checkedStagingRoot) -ne $stagingName -or
        $stagingName -notmatch '^__NowUINativeReference_[a-f0-9]{32}$') {
        throw "Refusing to clean an unexpected staging path: $checkedStagingRoot"
    }
    if (Test-Path -LiteralPath $checkedStagingRoot) { Remove-Item -LiteralPath $checkedStagingRoot -Recurse -Force }
    if (Test-Path -LiteralPath ($checkedStagingRoot + '.meta')) { Remove-Item -LiteralPath ($checkedStagingRoot + '.meta') -Force }
}
