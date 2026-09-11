#requires -Version 7.0
<#
.SYNOPSIS
Publishes a frozen native rendering benchmark runtime and measures fresh processes.
.DESCRIPTION
Per-frame timers exclude PNG/readback/present. Process startup is measured
separately up to the first completed UI submission. OS file caches are not reset.
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory = 'artifacts/local/native-performance/current',
    [ValidateRange(1, 10)] [int] $Runs = 3,
    [ValidateRange(1, 10000)] [int] $Warmup = 240,
    [ValidateRange(2, 10000)] [int] $Samples = 600,
    [switch] $ManagedVg,
    [switch] $SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory, $root)
$runtime = Join-Path $outputRoot 'runtime'
if (-not $SkipBuild) {
    & dotnet publish (Join-Path $root 'Standalone/Benchmarks/NativeRendering') -c Release '-p:NowUIUseNativeVg=true' -o $runtime --nologo
    if ($LASTEXITCODE -ne 0) { throw "Benchmark publish failed: $LASTEXITCODE" }
}
$dll = Join-Path $runtime 'NativeRendering.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw "No frozen benchmark runtime: $dll" }
for ($run = 1; $run -le $Runs; $run++) {
    $runPath = Join-Path $outputRoot "run$run"
    New-Item -ItemType Directory -Force -Path $runPath | Out-Null
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true
    foreach ($arg in @($dll, '--output', (Join-Path $runPath 'report.json'), '--warmup', "$Warmup", '--samples', "$Samples")) {
        $start.ArgumentList.Add($arg)
    }
    if ($ManagedVg) { $start.ArgumentList.Add('--managed-vg') }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($start)
    $readyMs = $null
    try {
        while ($null -ne ($line = $process.StandardOutput.ReadLine())) {
            if ($line -eq 'NOWUI_BENCHMARK_READY') { $readyMs = $timer.Elapsed.TotalMilliseconds }
            else { Write-Host $line }
        }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "Benchmark run $run failed: $($process.ExitCode)" }
        if ($null -eq $readyMs) { throw 'Benchmark did not signal its first submitted frame.' }
        @{
            process_start_to_first_submission_ms = $readyMs
            scope = 'Fresh process including host startup, resource/scene setup, first C# draw and submission; GPU completion and OS cache flushing are excluded.'
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runPath 'startup.json') -Encoding utf8
    }
    finally { $process.Dispose() }
}
