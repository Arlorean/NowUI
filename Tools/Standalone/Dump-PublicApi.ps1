<#
.SYNOPSIS
    Dumps the public API surface of an assembly to a sorted, diffable text file.

.DESCRIPTION
    Wrapper around Tools/Standalone/ApiDump, a small metadata-only .NET tool. It exists because the engine-free core
    split has to prove two things, and both are diffs of this output:

      1. Unity gate (exit criterion D8(d)). Dump Library/ScriptAssemblies/NowUI.Runtime.dll before and after the change.
         The diff must be empty: the split may add guards, new files and interfaces, but it may not alter one byte of
         NowUI's public contract in Unity.

      2. Standalone delta. Dump the standalone build's NowUI.Runtime.dll and diff it against the Unity dump. The result
         is checked in as Docs/Standalone/StandaloneApiDelta.md, so any future divergence is a reviewed decision rather
         than an accident. M3 generates the JavaScript surface from this metadata, which makes the delta a product
         artifact rather than an implementation detail.

    The work is done by a compiled tool rather than by PowerShell reflection because NowUI.Runtime references UnityEngine
    assemblies that a plain pwsh process cannot load; MetadataLoadContext reads the metadata without resolving or
    executing any of it.

.PARAMETER Assembly
    Path to the .dll to inspect.

.PARAMETER Output
    Path to write. Defaults to <assembly name>.api.txt beside the input.

.PARAMETER IncludeInternals
    Also emit internal types and members. Off by default: the gate is about the public contract, and NowUI's internals
    move by design during the split.

.EXAMPLE
    pwsh -File Tools/Standalone/Dump-PublicApi.ps1 -Assembly Library/ScriptAssemblies/NowUI.Runtime.dll `
        -Output artifacts/local/api/unity-NowUI.Runtime.api.txt
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Assembly,

    [Parameter(Mandatory = $false)]
    [string] $Output,

    [Parameter(Mandatory = $false)]
    [switch] $IncludeInternals,

    [Parameter(Mandatory = $false)]
    [string[]] $ReferencePath = @()
)

$ErrorActionPreference = 'Stop'

if (!(Test-Path -LiteralPath $Assembly)) {
    throw "Assembly not found at '$Assembly'."
}

function Get-UnityManagedDirectory {
    <#
        Unity's own assemblies are needed to print real type names for members whose signatures mention UnityEngine
        types; without them those members come out marked <unresolved-signature>. The editor is located the same way
        Tools/NowUI-Harness.ps1 locates it, so both agree on which install is authoritative.
    #>
    if (![string]::IsNullOrWhiteSpace($env:UNITY_EDITOR) -and (Test-Path -LiteralPath $env:UNITY_EDITOR)) {
        $editorRoot = Split-Path -Parent (Resolve-Path -LiteralPath $env:UNITY_EDITOR).Path
        $candidate = Join-Path $editorRoot 'Data/Managed'
        if (Test-Path -LiteralPath $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
    }

    $repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
    $versionFile = Join-Path $repositoryRoot 'ProjectSettings/ProjectVersion.txt'
    if (!(Test-Path -LiteralPath $versionFile)) { return $null }

    $versionLine = Get-Content -LiteralPath $versionFile |
        Where-Object { $_ -match '^m_EditorVersion:\s*(.+)$' } |
        Select-Object -First 1
    if ($null -eq $versionLine) { return $null }

    $version = ($versionLine -replace '^m_EditorVersion:\s*', '').Trim()
    $candidate = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Data/Managed"
    if (Test-Path -LiteralPath $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }

    return $null
}

$toolProject = Join-Path $PSScriptRoot 'ApiDump/ApiDump.csproj'
if (!(Test-Path -LiteralPath $toolProject)) {
    throw "ApiDump project not found at '$toolProject'."
}

$arguments = @('--assembly', (Resolve-Path -LiteralPath $Assembly).Path)

if (![string]::IsNullOrWhiteSpace($Output)) {
    $arguments += @('--output', $Output)
}

if ($IncludeInternals) {
    $arguments += '--include-internals'
}

$references = [System.Collections.Generic.List[string]]::new()
foreach ($path in $ReferencePath) { $references.Add($path) }

if ($references.Count -eq 0) {
    $unityManaged = Get-UnityManagedDirectory
    if ($null -ne $unityManaged) {
        $references.Add($unityManaged)
    }
    else {
        Write-Warning 'Unity managed assemblies were not found; UnityEngine-typed members will be marked <unresolved-signature>.'
    }
}

foreach ($path in $references) { $arguments += @('--reference-path', $path) }

# --verbosity quiet keeps the build chatter out of the way; a build failure still surfaces through the exit code.
& dotnet run --project $toolProject --configuration Release --verbosity quiet -- @arguments

if ($LASTEXITCODE -ne 0) {
    throw "ApiDump failed with exit code $LASTEXITCODE."
}
