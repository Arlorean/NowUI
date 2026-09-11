#requires -Version 7.0
<#
.SYNOPSIS
Builds the compact native-call bridge for the optional .NET browser target.
#>
[CmdletBinding()]
param(
    [string] $DotnetRoot = (Split-Path (Get-Command dotnet).Source -Parent),
    [switch] $SmokeTest
)
$ErrorActionPreference = 'Stop'
$bridgeRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$bridgeSource = Join-Path $bridgeRoot 'Standalone/NowUI.Browser/native/nowui_browser.cpp'
$bridgeOutput = Join-Path $bridgeRoot 'Standalone/NowUI.Browser/native/nowui-browser.o'
function Get-BridgeTools([string] $Name) {
    $pack = Get-ChildItem -Directory -LiteralPath (Join-Path $DotnetRoot "packs/$Name") |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $pack) { throw "Install .NET wasm-tools: missing $Name" }
    return Join-Path $pack.FullName 'tools'
}
$bridgeSdk = Get-BridgeTools 'Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.win-x64'
$bridgePython = Get-BridgeTools 'Microsoft.NET.Runtime.Emscripten.3.1.56.Python.win-x64'
$bridgeNode = Get-BridgeTools 'Microsoft.NET.Runtime.Emscripten.3.1.56.Node.win-x64'
$bridgeCache = Get-BridgeTools 'Microsoft.NET.Runtime.Emscripten.3.1.56.Cache.win-x64'
$bridgeStart = [Diagnostics.ProcessStartInfo]::new((Join-Path $bridgePython 'python.exe'))
$bridgeStart.UseShellExecute = $false
$bridgeStart.CreateNoWindow = $true
$bridgeStart.RedirectStandardOutput = $true
$bridgeStart.RedirectStandardError = $true
$bridgeStart.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$bridgeStart.WorkingDirectory = $bridgeRoot
$bridgeStart.Environment['EM_CONFIG'] = Join-Path $bridgeSdk 'emscripten/.emscripten'
$bridgeStart.Environment['DOTNET_EMSCRIPTEN_LLVM_ROOT'] = Join-Path $bridgeSdk 'bin'
$bridgeStart.Environment['DOTNET_EMSCRIPTEN_BINARYEN_ROOT'] = $bridgeSdk
$bridgeStart.Environment['DOTNET_EMSCRIPTEN_NODE_JS'] = Join-Path $bridgeNode 'bin/node.exe'
$bridgeStart.Environment['EM_CACHE'] = Join-Path $bridgeCache 'emscripten/cache'
foreach ($argument in @((Join-Path $bridgeSdk 'emscripten/emcc.py'), '-c', '-O3', '-std=c++11',
    '-fno-exceptions', '-fno-rtti', '-ffunction-sections', '-fdata-sections',
    '-I', (Join-Path $bridgeRoot 'Assets/NowUI/Plugins/Native/nowui-vg'),
    '-I', (Join-Path $bridgeRoot 'Assets/NowUI/Plugins/Native/nowui-msdf'),
    $bridgeSource, '-o', $bridgeOutput)) { $bridgeStart.ArgumentList.Add($argument) }
$bridgeProcess = [Diagnostics.Process]::Start($bridgeStart)
try {
    $bridgeStdout = $bridgeProcess.StandardOutput.ReadToEndAsync()
    $bridgeStderr = $bridgeProcess.StandardError.ReadToEndAsync()
    $bridgeProcess.WaitForExit()
    Write-Host ($bridgeStdout.GetAwaiter().GetResult()) -NoNewline
    Write-Host ($bridgeStderr.GetAwaiter().GetResult()) -NoNewline
    if ($bridgeProcess.ExitCode -ne 0) { throw "Browser bridge compilation failed: $($bridgeProcess.ExitCode)" }
} finally { $bridgeProcess.Dispose() }
[ordered]@{
    purpose = 'One-pointer ABI for .NET browser interpreter native calls; forwards to unchanged stock plugins'
    emscripten = '3.1.56 (.NET wasm-tools)'
    source_sha256 = (Get-FileHash -LiteralPath $bridgeSource).Hash
    header_sha256 = (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($bridgeSource, '.h'))).Hash
    object_sha256 = (Get-FileHash -LiteralPath $bridgeOutput).Hash
} | ConvertTo-Json | Set-Content -LiteralPath ([IO.Path]::ChangeExtension($bridgeOutput, '.json')) -Encoding utf8
Write-Output "Prepared browser ABI bridge: $bridgeOutput"
if ($SmokeTest) {
    $smokeRoot = Join-Path $bridgeRoot ('artifacts/local/browser-abi/' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $smokeRoot | Out-Null
    $smokeObjects = @{
        'nowui-vg.o' = 'Assets/NowUI/Plugins/WebGL/nowui-vg.bc'
        'nowui-msdf.o' = 'Assets/NowUI/Plugins/WebGL/nowui-msdf.bc'
        'harfbuzz.o' = 'Standalone/NowUI.Browser/native/harfbuzz.bc'
    }
    foreach ($object in $smokeObjects.GetEnumerator()) {
        Copy-Item -LiteralPath (Join-Path $bridgeRoot $object.Value) -Destination (Join-Path $smokeRoot $object.Key)
    }
    $smokeJs = Join-Path $smokeRoot 'bridge-smoke.js'
    $bridgeStart.ArgumentList.Clear()
    foreach ($argument in @((Join-Path $bridgeSdk 'emscripten/emcc.py'), '-O2', '-std=c++11', '-fwasm-exceptions',
        '-sENVIRONMENT=node', '-sSINGLE_FILE=1', '-sALLOW_MEMORY_GROWTH=1', '-sSTACK_SIZE=1048576',
        '-I', (Join-Path $bridgeRoot 'Assets/NowUI/Plugins/Native/nowui-vg'),
        '-I', (Join-Path $bridgeRoot 'Assets/NowUI/Plugins/Native/nowui-msdf'),
        (Join-Path $bridgeRoot 'Standalone/NowUI.Browser/native/bridge_smoke.cpp'), $bridgeOutput,
        (Join-Path $smokeRoot 'nowui-vg.o'), (Join-Path $smokeRoot 'nowui-msdf.o'), (Join-Path $smokeRoot 'harfbuzz.o'),
        '--embed-file', ((Join-Path $bridgeRoot 'Standalone/Tests/Fixtures/NowUI/NotoSans-Regular.ttf').Replace('\', '/') + '@/font.ttf'),
        '-o', $smokeJs)) { $bridgeStart.ArgumentList.Add($argument) }
    $smokeProcess = [Diagnostics.Process]::Start($bridgeStart)
    try {
        $smokeStdout = $smokeProcess.StandardOutput.ReadToEndAsync()
        $smokeStderr = $smokeProcess.StandardError.ReadToEndAsync()
        $smokeProcess.WaitForExit()
        Write-Host ($smokeStdout.GetAwaiter().GetResult()) -NoNewline
        Write-Host ($smokeStderr.GetAwaiter().GetResult()) -NoNewline
        if ($smokeProcess.ExitCode -ne 0) { throw "Browser bridge smoke compilation failed: $($smokeProcess.ExitCode)" }
    } finally { $smokeProcess.Dispose() }
    & (Join-Path $bridgeNode 'bin/node.exe') $smokeJs
    if ($LASTEXITCODE -ne 0) { throw "Browser bridge wasm smoke failed ($LASTEXITCODE)." }
    Write-Output "Browser ABI smoke artifacts: $smokeRoot"
}
