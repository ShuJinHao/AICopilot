[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [string]$OutputDirectory = 'artifacts/mutation',
    [string]$BaselinePath = 'scripts/tests/baselines/aicopilot-mutation.json',
    [string]$SummaryPath = 'artifacts/quality/aicopilot-mutation.json',
    [switch]$UpdateBaseline,
    [switch]$SkipToolRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-RepositoryPath {
    param([Parameter(Mandatory)] [string]$Path)

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $Path))
}

$resolvedOutputDirectory = Resolve-RepositoryPath $OutputDirectory
$unitTestDirectory = Join-Path $RepositoryRoot 'src/tests/AICopilot.UnitTests'
$confirmScript = Join-Path $RepositoryRoot 'scripts/tests/Confirm-AICopilotMutation.ps1'

if (-not $SkipToolRestore) {
    & dotnet tool restore --tool-manifest (Join-Path $RepositoryRoot '.config/dotnet-tools.json')
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool restore failed with exit code $LASTEXITCODE."
    }
}

if (Test-Path $resolvedOutputDirectory) {
    Remove-Item $resolvedOutputDirectory -Recurse -Force
}
New-Item $resolvedOutputDirectory -ItemType Directory -Force | Out-Null
$logPath = Join-Path $resolvedOutputDirectory 'dotnet-stryker.log'

Push-Location $unitTestDirectory
try {
    & dotnet stryker --output $resolvedOutputDirectory --skip-version-check 2>&1 |
        Tee-Object -FilePath $logPath
    $strykerExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}
if ($strykerExitCode -ne 0) {
    throw "dotnet-stryker failed with exit code $strykerExitCode. See '$logPath'."
}

$jsonReports = @(Get-ChildItem $resolvedOutputDirectory -File -Recurse -Filter 'aicopilot-secret-protection-mutation.json')
$markdownReports = @(Get-ChildItem $resolvedOutputDirectory -File -Recurse -Filter 'aicopilot-secret-protection-mutation.md')
if ($jsonReports.Count -ne 1 -or $markdownReports.Count -ne 1) {
    throw "Mutation reporters did not produce exactly one JSON and one Markdown artifact: json=$($jsonReports.Count), markdown=$($markdownReports.Count)."
}

$confirmArguments = @{
    RepositoryRoot = $RepositoryRoot
    ReportPath = $jsonReports[0].FullName
    BaselinePath = $BaselinePath
    OutputPath = $SummaryPath
}
if ($UpdateBaseline) {
    $confirmArguments.UpdateBaseline = $true
}
& $confirmScript @confirmArguments
