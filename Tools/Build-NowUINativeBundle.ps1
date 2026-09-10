[CmdletBinding()]
param([string] $OutputRoot)
$ErrorActionPreference = 'Stop'
$nativeRepository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputRoot) { $OutputRoot = Join-Path $nativeRepository 'Assets/NowUI/Native~/tools' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$nativePackStage = Join-Path $nativeRepository ('artifacts/local/native-pack/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $nativePackStage -Force | Out-Null
& dotnet pack (Join-Path $nativeRepository 'Standalone/NowUI.Cli/NowUI.Cli.csproj') -c Release -o $nativePackStage --nologo
if ($LASTEXITCODE -ne 0) { throw "Native tool pack failed ($LASTEXITCODE)." }
$nativePackages = @(Get-ChildItem -LiteralPath $nativePackStage -Filter '*.nupkg' -File)
if ($nativePackages.Count -ne 1) { throw 'Expected exactly one native tool package.' }
$nativePackage = $nativePackages[0]
$nativeArchive = [IO.Compression.ZipFile]::OpenRead($nativePackage.FullName)
try {
    $nativeSpec = $nativeArchive.Entries | Where-Object FullName -Like '*.nuspec' | Select-Object -First 1
    $nativeReader = [IO.StreamReader]::new($nativeSpec.Open())
    try { [xml]$nativeManifest = $nativeReader.ReadToEnd() } finally { $nativeReader.Dispose() }
    foreach ($required in @(
        'tools/net9.0/any/nowui.dll',
        'tools/net9.0/any/NowUI.Extensions.Sdf.dll',
        'tools/net9.0/any/NowUI.Extensions.Markup.dll',
        'tools/net9.0/any/Shaders/nowui-text.frag',
        'tools/net9.0/any/NowUI/Resources/NowUI/NotoSans-Regular.ttf'
    )) {
        if (-not $nativeArchive.GetEntry($required)) { throw "Tool package is missing $required." }
    }
    foreach ($nativeRid in @('win-x64','linux-x64','osx-x64','osx-arm64')) {
        $nativePrefix = if ($nativeRid -eq 'win-x64') { '' } else { 'lib' }
        $nativeSuffix = if ($nativeRid -eq 'win-x64') { '.dll' } elseif ($nativeRid -eq 'linux-x64') { '.so' } else { '.dylib' }
        foreach ($nativeLibrary in @('nowui-msdf','nowui-vg','msdf-atlas-gen','msdfgen-core','msdfgen-ext')) {
            $required = "tools/net9.0/any/runtimes/$nativeRid/native/$nativePrefix$nativeLibrary$nativeSuffix"
            if (-not $nativeArchive.GetEntry($required)) { throw "Tool package is missing $required." }
        }
    }
    if (Test-Path -LiteralPath (Join-Path $nativeRepository 'Assets/NowUI/Native~/browser/kit.json')) {
        foreach ($browserFile in @('NowUI.Browser.dll', 'Browser.Native.targets', 'App/BrowserEntry.cs',
            'wwwroot/index.html', 'wwwroot/main.js', 'wwwroot/nowui-gl.js', 'wwwroot/nowui-input.js',
            'native/nowui-msdf.o', 'native/nowui-vg.o', 'native/harfbuzz.o', 'native/harfbuzz.json', 'native/nowui-browser.o', 'native/nowui-browser.json',
            'native/HarfBuzz-LICENSE.txt', 'native/FreeType-LICENSE.txt', 'NowUI-LICENSE.md',
            'NowUI-LIBRARY-NOTICES.md', 'THIRD_PARTY_NOTICES.md', 'kit.json')) {
            $required = 'tools/net9.0/any/BrowserKit/' + $browserFile
            if (-not $nativeArchive.GetEntry($required)) { throw "Tool package is missing optional browser kit file $required." }
        }
    }
} finally { $nativeArchive.Dispose() }
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
Copy-Item -LiteralPath $nativePackage.FullName -Destination (Join-Path $OutputRoot $nativePackage.Name) -Force
$nativeBundle = [ordered]@{
    packageId = [string]$nativeManifest.package.metadata.id
    version = [string]$nativeManifest.package.metadata.version
    packageFile = $nativePackage.Name
    sha256 = (Get-FileHash -LiteralPath $nativePackage.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$nativeBundle | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputRoot 'bundle.json') -Encoding utf8
Write-Output ('Native tool bundle: ' + $OutputRoot)
