#requires -Version 7.0
# Packaged native launcher. Keeps the package and global dotnet tool settings untouched.
[CmdletBinding(PositionalBinding = $false)]
param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)
$ErrorActionPreference = 'Stop'
$nativeBundleRoot = Join-Path $PSScriptRoot 'tools'
$nativeBundlePath = Join-Path $nativeBundleRoot 'bundle.json'
if (-not (Test-Path -LiteralPath $nativeBundlePath)) {
    throw 'The native CLI bundle is missing. In a source checkout run Tools/Build-NowUINativeBundle.ps1.'
}
$nativeBundle = Get-Content -LiteralPath $nativeBundlePath -Raw | ConvertFrom-Json
$nativePackage = Join-Path $nativeBundleRoot $nativeBundle.packageFile
$nativeHash = (Get-FileHash -LiteralPath $nativePackage -Algorithm SHA256).Hash.ToLowerInvariant()
if ($nativeHash -ne $nativeBundle.sha256) { throw 'Native tool package does not match its bundle manifest.' }
$nativeProjectRoot = $null
foreach ($nativeStart in @((Get-Location).Path, $PSScriptRoot)) {
    $nativeDirectory = [IO.DirectoryInfo]::new($nativeStart)
    while ($nativeDirectory) {
        if ((Test-Path -LiteralPath (Join-Path $nativeDirectory.FullName 'Assets') -PathType Container) -and
            (Test-Path -LiteralPath (Join-Path $nativeDirectory.FullName 'ProjectSettings') -PathType Container)) {
            $nativeProjectRoot = $nativeDirectory.FullName
            break
        }
        $nativeDirectory = $nativeDirectory.Parent
    }
    if ($nativeProjectRoot) { break }
}
$nativeCacheRoot = if ($nativeProjectRoot) { Join-Path $nativeProjectRoot 'NowUI/.tools' }
    else { Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'NowUI/tools' }
$nativeToolRoot = Join-Path $nativeCacheRoot ($nativeBundle.version + '-' + $nativeHash.Substring(0,16))
$nativeCommand = Join-Path $nativeToolRoot $(if ($IsWindows) { 'nowui.exe' } else { 'nowui' })
if (-not (Test-Path -LiteralPath $nativeCommand)) {
    # Isolate same-version development builds from NuGet's global package cache.
    $nativePreviousNuget = $env:NUGET_PACKAGES
    try {
        New-Item -ItemType Directory -Path $nativeToolRoot -Force | Out-Null
        # Resolve this verified bundle only, even if another feed carries the same package version.
        $nativeConfig = Join-Path $nativeToolRoot 'NuGet.Config'
        $nativeSource = [Security.SecurityElement]::Escape($nativeBundleRoot)
        [IO.File]::WriteAllText($nativeConfig, '<configuration><packageSources><clear/><add key="bundle" value="' + $nativeSource + '"/></packageSources></configuration>')
        $env:NUGET_PACKAGES = Join-Path $nativeToolRoot 'packages'
        & dotnet tool install $nativeBundle.packageId --version $nativeBundle.version --configfile $nativeConfig --tool-path $nativeToolRoot --no-http-cache
        if ($LASTEXITCODE -ne 0) { throw "Native tool installation failed ($LASTEXITCODE)." }
    } finally { $env:NUGET_PACKAGES = $nativePreviousNuget }
}
& $nativeCommand @Arguments
exit $LASTEXITCODE
