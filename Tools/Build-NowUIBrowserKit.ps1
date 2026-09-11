#requires -Version 7.0
<#
.SYNOPSIS
Builds the optional browser kit without adding a WebAssembly build to native CLI builds.
.DESCRIPTION
Builds the browser class library and packages its interop files and prepared
native objects. Existing generated kits are parked under ignored artifacts
before replacement. Regenerating HarfBuzz is a separate maintainer operation.
#>
[CmdletBinding()]
param([string] $OutputRoot)
$ErrorActionPreference = 'Stop'
$browserRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$browserSource = Join-Path $browserRepository 'Standalone/NowUI.Browser'
if (-not $OutputRoot) { $OutputRoot = Join-Path $browserRepository 'Assets/NowUI/Native~/browser' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot, $browserRepository)
$browserStageRoot = Join-Path $browserRepository ('artifacts/local/browser-kit-build/' + [Guid]::NewGuid().ToString('N'))
$browserStage = Join-Path $browserStageRoot 'kit'
New-Item -ItemType Directory -Force -Path $browserStage | Out-Null
& dotnet build (Join-Path $browserSource 'NowUI.Browser.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Browser library build failed ($LASTEXITCODE)." }

foreach ($folder in @('App', 'native')) {
    New-Item -ItemType Directory -Path (Join-Path $browserStage $folder) | Out-Null
}
Copy-Item -LiteralPath (Join-Path $browserSource 'bin/Release/net9.0-browser/NowUI.Browser.dll') -Destination $browserStage
Copy-Item -LiteralPath (Join-Path $browserSource 'wwwroot') -Destination (Join-Path $browserStage 'wwwroot') -Recurse
Copy-Item -LiteralPath (Join-Path $browserSource 'App/BrowserEntry.cs') -Destination (Join-Path $browserStage 'App/BrowserEntry.cs')
Copy-Item -LiteralPath (Join-Path $browserSource 'Browser.Native.targets') -Destination $browserStage
Copy-Item -LiteralPath (Join-Path $browserSource 'THIRD_PARTY_NOTICES.md') -Destination $browserStage
Copy-Item -LiteralPath (Join-Path $browserRepository 'Assets/NowUI/LICENSE.md') -Destination (Join-Path $browserStage 'NowUI-LICENSE.md')
$libraryNotices = Get-Content -Raw -LiteralPath (Join-Path $browserRepository 'Assets/NowUI/THIRD_PARTY_LICENSES.md')
$libraryNotices.Replace('(Native~/THIRD_PARTY_NOTICES.md)', '(THIRD_PARTY_NOTICES.md)') |
    Set-Content -LiteralPath (Join-Path $browserStage 'NowUI-LIBRARY-NOTICES.md') -Encoding utf8
$browserObjects = [ordered]@{
    'nowui-msdf.o' = Join-Path $browserRepository 'Assets/NowUI/Plugins/WebGL/nowui-msdf.bc'
    'nowui-vg.o' = Join-Path $browserRepository 'Assets/NowUI/Plugins/WebGL/nowui-vg.bc'
    'nowui-browser.o' = Join-Path $browserSource 'native/nowui-browser.o'
    'harfbuzz.o' = Join-Path $browserSource 'native/harfbuzz.bc'
}
foreach ($entry in $browserObjects.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $entry.Value -PathType Leaf)) { throw "Missing prepared browser object: $($entry.Value)" }
    Copy-Item -LiteralPath $entry.Value -Destination (Join-Path $browserStage ('native/' + $entry.Key))
}
foreach ($file in @('HarfBuzz-LICENSE.txt', 'harfbuzz.json', 'FreeType-LICENSE.txt', 'nowui-browser.json')) {
    Copy-Item -LiteralPath (Join-Path $browserSource ('native/' + $file)) -Destination (Join-Path $browserStage ('native/' + $file))
}
$browserFiles = @(
    Get-ChildItem -LiteralPath $browserStage -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($browserStage, $_.FullName).Replace('\', '/');
            bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
    }
)
[ordered]@{
    format = 'NowUI.BrowserKit'
    version = 1
    targetFramework = 'net9.0-browser'
    preparedUtc = [DateTime]::UtcNow.ToString('O')
    authoring = 'Shared C# INowScene and NowUI API'
    colorSpace = 'Gamma'
    nativeLibraries = @('nowui-msdf', 'nowui-vg', 'harfbuzz', 'nowui-browser')
    files = $browserFiles
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $browserStage 'kit.json') -Encoding utf8

if (Test-Path -LiteralPath $OutputRoot) {
    $previousManifestPath = Join-Path $OutputRoot 'kit.json'
    if (-not (Test-Path -LiteralPath $previousManifestPath -PathType Leaf) -or
        (Get-Content -Raw -LiteralPath $previousManifestPath | ConvertFrom-Json).format -ne 'NowUI.BrowserKit') {
        throw "Refusing to replace a directory that is not a generated NowUI browser kit: $OutputRoot"
    }
    # Validate the two final absolute paths before moving the generated tree.
    $checkedOutput = [IO.Path]::GetFullPath($OutputRoot)
    $browserBackup = [IO.Path]::GetFullPath((Join-Path $browserStageRoot 'previous-kit'))
    if ($checkedOutput -eq $browserRepository -or
        -not $browserBackup.StartsWith($browserRepository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing unexpected browser kit move: $checkedOutput -> $browserBackup"
    }
    Move-Item -LiteralPath $checkedOutput -Destination $browserBackup
}
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($OutputRoot)) | Out-Null
Copy-Item -LiteralPath $browserStage -Destination $OutputRoot -Recurse
Write-Output ('Optional browser kit: ' + $OutputRoot)
