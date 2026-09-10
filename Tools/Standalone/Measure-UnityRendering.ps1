#requires -Version 7.0
<#
.SYNOPSIS
Measures the native benchmark's unchanged C# workloads through Unity PlayMode.
.DESCRIPTION
Requires an idle Gamma Unity project. Creates one unique temporary Assets test
assembly, runs the three graphics workloads, and removes only its own staging
folder. Does not change project settings. Timing excludes yields/readback/PNG.
#>
[CmdletBinding()]
param(
    [string] $Unity = 'C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Unity.exe',
    [string] $Output = 'artifacts/local/native-performance/unity/report.json',
    [ValidateRange(1, 10000)] [int] $Warmup = 240,
    [ValidateRange(2, 10000)] [int] $Samples = 600,
    [ValidateRange(30, 1800)] [int] $TimeoutSeconds = 600
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assetsRoot = Join-Path $projectRoot 'Assets'
$stagingName = '__NowUIRenderingBenchmark_' + [Guid]::NewGuid().ToString('N')
$stagingRoot = Join-Path $assetsRoot $stagingName
$outputPath = [IO.Path]::GetFullPath($Output, $projectRoot)
$logPath = [IO.Path]::ChangeExtension($outputPath, '.log')
$testPath = [IO.Path]::ChangeExtension($outputPath, '.xml')
$unityProcess = $null
if (-not (Test-Path -LiteralPath $Unity -PathType Leaf)) { throw "Unity executable not found: $Unity" }
if (Get-Process -Name Unity -ErrorAction SilentlyContinue) { throw 'Close Unity before running the isolated benchmark.' }
if ((Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectSettings.asset')) -notmatch 'm_ActiveColorSpace:\s*0\b') {
    throw 'This comparison requires an existing Gamma project; project settings are not changed.'
}
try {
    New-Item -ItemType Directory -Path $stagingRoot | Out-Null
    New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
    foreach ($source in @('Standalone/Samples/NativePreview/InteractiveContent.cs',
        'Standalone/Benchmarks/NativeRendering/BenchmarkContent.cs',
        'Standalone/Benchmarks/NativeRendering/UnityRenderingBenchmark.cs')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $source) -Destination $stagingRoot
    }
    @{
        name = 'NowUI.SharedRenderingBenchmark'
        references = @('UnityEngine.TestRunner', 'NowUI.Runtime', 'NowUI.Extensions.Sdf', 'NowUI.BenchmarkSupport', 'Unity.PerformanceTesting')
        includePlatforms = @()
        overrideReferences = $true
        precompiledReferences = @('nunit.framework.dll')
        defineConstraints = @('UNITY_INCLUDE_TESTS')
        autoReferenced = $false
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stagingRoot 'NowUI.SharedRenderingBenchmark.asmdef') -Encoding utf8
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Unity
    $startInfo.WorkingDirectory = $projectRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($argument in @('-batchmode', '-projectPath', $projectRoot, '-runTests', '-testPlatform', 'PlayMode',
        '-testFilter', 'NowUI.Benchmarks.UnityRenderingBenchmark.SharedRenderingWorkloads', '-testResults', $testPath,
        '-logFile', $logPath, '-nowuiBenchmarkOutput', $outputPath, '-nowuiBenchmarkWarmup', "$Warmup", '-nowuiBenchmarkSamples', "$Samples")) {
        $startInfo.ArgumentList.Add($argument)
    }
    $unityProcess = [Diagnostics.Process]::Start($startInfo)
    Write-Host "Unity benchmark PID $($unityProcess.Id). Log: $logPath"
    if (-not $unityProcess.WaitForExit($TimeoutSeconds * 1000)) {
        $unityProcess.Kill($true); $unityProcess.WaitForExit()
        throw "Unity benchmark exceeded $TimeoutSeconds seconds. See $logPath"
    }
    if ($unityProcess.ExitCode -ne 0) { throw "Unity exited with $($unityProcess.ExitCode). See $logPath" }
    if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) { throw "Unity did not write a report. See $logPath" }
    Write-Host "Report: $outputPath"
}
finally {
    if ($null -ne $unityProcess) {
        if (-not $unityProcess.HasExited) { $unityProcess.Kill($true); $unityProcess.WaitForExit() }
        $unityProcess.Dispose()
    }
    $checkedPath = [IO.Path]::GetFullPath($stagingRoot)
    if ([IO.Path]::GetDirectoryName($checkedPath) -ne $assetsRoot -or
        [IO.Path]::GetFileName($checkedPath) -ne $stagingName -or
        $stagingName -notmatch '^__NowUIRenderingBenchmark_[a-f0-9]{32}$') { throw "Refusing unexpected cleanup: $checkedPath" }
    if (Test-Path -LiteralPath $checkedPath) { Remove-Item -LiteralPath $checkedPath -Recurse -Force }
    if (Test-Path -LiteralPath ($checkedPath + '.meta')) { Remove-Item -LiteralPath ($checkedPath + '.meta') -Force }
}
