# Thin checkout launcher. Arguments and relative paths retain the caller's working directory.
[CmdletBinding(PositionalBinding = $false)]
param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)

$ErrorActionPreference = 'Stop'
$cliProject = Join-Path $PSScriptRoot '../Standalone/NowUI.Cli/NowUI.Cli.csproj'
& dotnet run --project $cliProject --configuration Release -- @Arguments
exit $LASTEXITCODE
