<#
.SYNOPSIS
    Compares two NUnit3 result files as SETS of test cases, not as counts.

.DESCRIPTION
    The M1 exit criterion for the engine-free core split is that Unity's own test results are unchanged by the change.
    Comparing pass/fail counts is not enough: a test that starts failing while another starts passing keeps the count
    identical. This script therefore compares the full name and result of every test case and reports four categories:

        Missing    a case in the baseline that the current run did not execute (renamed, removed, or filtered out)
        Added      a case in the current run that the baseline did not contain
        Changed    a case whose result differs (Passed -> Failed, Skipped -> Passed, ...)
        Identical  everything else

    Exit code is 0 when Missing, Added and Changed are all empty, and 1 otherwise, so it can gate a script.

    Known-unstable cases can be excluded with -Ignore. This machine's baseline, for example, fails
    NowHarnessAnimationTests.ReadmeShowcasesHaveTheirDeclaredCaptureDurations when the project is run from a git
    worktree, because that test resolves paths relative to the repository root.

.PARAMETER Baseline
    NUnit3 XML from the run before the change.

.PARAMETER Current
    NUnit3 XML from the run after the change.

.PARAMETER Ignore
    Zero or more full names (or wildcard patterns) to exclude from the comparison.

.PARAMETER Detailed
    List every differing case instead of the first 40 per category.

.EXAMPLE
    pwsh -File Tools/Standalone/Compare-TestResults.ps1 `
        -Baseline artifacts/local/standalone-baseline/EditMode/NowUI-EditMode-results.xml `
        -Current  artifacts/local/standalone-after/EditMode/NowUI-EditMode-results.xml
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Baseline,

    [Parameter(Mandatory = $true)]
    [string] $Current,

    [Parameter(Mandatory = $false)]
    [string[]] $Ignore = @(),

    [Parameter(Mandatory = $false)]
    [switch] $Detailed
)

$ErrorActionPreference = 'Stop'

function Read-TestCases {
    param([string] $Path, [string] $Label)

    if (!(Test-Path -LiteralPath $Path)) {
        throw "$Label results not found at '$Path'."
    }

    [xml] $document = Get-Content -LiteralPath $Path -Raw
    $cases = $document.SelectNodes('//test-case')

    if ($null -eq $cases -or $cases.Count -eq 0) {
        throw "$Label results at '$Path' contain no test cases. A run that discovered nothing must not be treated as a match."
    }

    $map = [ordered] @{}
    foreach ($case in $cases) {
        $name = $case.fullname
        if ([string]::IsNullOrWhiteSpace($name)) { $name = $case.name }

        # A parameterised case can repeat its full name across fixtures; keep the worst result so a regression cannot hide.
        if ($map.Contains($name) -and $map[$name] -ne $case.result) {
            $rank = @{ 'Passed' = 0; 'Skipped' = 1; 'Inconclusive' = 2; 'Failed' = 3 }
            $existing = $rank[[string] $map[$name]]
            $incoming = $rank[[string] $case.result]
            if ($null -ne $existing -and $null -ne $incoming -and $incoming -le $existing) { continue }
        }

        $map[$name] = $case.result
    }

    Write-Host ("{0,-8} {1,5} cases from {2}" -f $Label, $map.Count, (Split-Path -Leaf $Path))
    return $map
}

function Test-Ignored {
    param([string] $Name)
    foreach ($pattern in $Ignore) {
        if ($Name -like $pattern) { return $true }
    }
    return $false
}

$baselineCases = Read-TestCases -Path $Baseline -Label 'Baseline'
$currentCases = Read-TestCases -Path $Current -Label 'Current'

$missing = [System.Collections.Generic.List[string]]::new()
$added = [System.Collections.Generic.List[string]]::new()
$changed = [System.Collections.Generic.List[string]]::new()

foreach ($name in $baselineCases.Keys) {
    if (Test-Ignored -Name $name) { continue }

    if (!$currentCases.Contains($name)) {
        $missing.Add($name)
        continue
    }

    if ($baselineCases[$name] -ne $currentCases[$name]) {
        $changed.Add(("{0}: {1} -> {2}" -f $name, $baselineCases[$name], $currentCases[$name]))
    }
}

foreach ($name in $currentCases.Keys) {
    if (Test-Ignored -Name $name) { continue }
    if (!$baselineCases.Contains($name)) { $added.Add($name) }
}

function Write-Category {
    param([string] $Title, [System.Collections.Generic.List[string]] $Items)

    if ($Items.Count -eq 0) {
        Write-Host ("{0}: none" -f $Title) -ForegroundColor Green
        return
    }

    Write-Host ("{0}: {1}" -f $Title, $Items.Count) -ForegroundColor Red
    $limit = if ($Detailed) { $Items.Count } else { [Math]::Min(40, $Items.Count) }
    for ($i = 0; $i -lt $limit; $i++) { Write-Host ("  {0}" -f $Items[$i]) }
    if ($limit -lt $Items.Count) {
        Write-Host ("  ... {0} more (pass -Detailed to list them all)" -f ($Items.Count - $limit))
    }
}

Write-Host ''
Write-Category -Title 'Changed' -Items $changed
Write-Category -Title 'Missing' -Items $missing
Write-Category -Title 'Added' -Items $added

if ($Ignore.Count -gt 0) {
    Write-Host ''
    Write-Host ("Ignored patterns: {0}" -f ($Ignore -join ', '))
}

$differences = $changed.Count + $missing.Count + $added.Count

Write-Host ''
if ($differences -eq 0) {
    Write-Host 'Results are identical.' -ForegroundColor Green
    exit 0
}

Write-Host ("{0} difference(s)." -f $differences) -ForegroundColor Red
exit 1
