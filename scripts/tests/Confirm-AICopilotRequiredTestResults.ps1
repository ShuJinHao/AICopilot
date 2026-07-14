[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [string]$InventoryPath = 'artifacts/test-inventory.json',
    [string]$ResultsDirectory = 'artifacts/test-results',
    [string]$VitestPath,
    [string]$DeploymentPath,
    [string]$OutputPath = 'artifacts/test-results/required-test-summary.json'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-RepositoryPath {
    param([Parameter(Mandatory)] [string]$Path)
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $script:root $Path
}

$root = (Resolve-Path $RepositoryRoot).Path
$resolvedInventoryPath = Resolve-RepositoryPath $InventoryPath
$resolvedResultsDirectory = Resolve-RepositoryPath $ResultsDirectory

if (-not (Test-Path $resolvedInventoryPath -PathType Leaf)) {
    throw "Required test inventory is missing: $resolvedInventoryPath"
}

$inventory = Get-Content $resolvedInventoryPath -Raw | ConvertFrom-Json
$requiredProjects = @($inventory.projects | Where-Object { $_.role -eq 'Runner' -and [bool]$_.required })
if ($requiredProjects.Count -eq 0) {
    throw 'The inventory contains no required test projects.'
}

$projectResults = [System.Collections.Generic.List[object]]::new()
$discoveredTotal = 0
$executedTotal = 0
$passedTotal = 0
$failedTotal = 0
$skippedTotal = 0

foreach ($project in $requiredProjects) {
    $trxPath = Join-Path $resolvedResultsDirectory "$($project.projectName).trx"
    if (-not (Test-Path $trxPath -PathType Leaf)) {
        throw "Missing required TRX for $($project.projectName): $trxPath"
    }

    [xml]$trx = Get-Content $trxPath -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if ($null -eq $counters) {
        throw "TRX counters are missing for $($project.projectName): $trxPath"
    }

    $discovered = [int]$counters.total
    $executed = [int]$counters.executed
    $passed = [int]$counters.passed
    $failed = [int]$counters.failed
    $skipped = [int]$counters.notExecuted

    if ($discovered -le 0) {
        throw "$($project.projectName) reconciliation failed: required runner discovered no tests."
    }

    if ($discovered -ne $executed -or $failed -ne 0 -or $skipped -ne 0 -or $passed -ne $discovered) {
        throw "$($project.projectName) reconciliation failed: discovered=$discovered, executed=$executed, passed=$passed, failed=$failed, skipped=$skipped"
    }

    $discoveredTotal += $discovered
    $executedTotal += $executed
    $passedTotal += $passed
    $failedTotal += $failed
    $skippedTotal += $skipped
    $projectResults.Add([pscustomobject]@{
        projectName = $project.projectName
        discovered = $discovered
        executed = $executed
        passed = $passed
        failed = $failed
        skipped = $skipped
    })
}

$vitestSummary = $null
if (-not [string]::IsNullOrWhiteSpace($VitestPath)) {
    $resolvedVitestPath = Resolve-RepositoryPath $VitestPath
    if (-not (Test-Path $resolvedVitestPath -PathType Leaf)) {
        throw "Missing required Vitest result: $resolvedVitestPath"
    }

    $vitest = Get-Content $resolvedVitestPath -Raw | ConvertFrom-Json
    $vitestTotal = [int]$vitest.numTotalTests
    $vitestPassed = [int]$vitest.numPassedTests
    $vitestFailed = [int]$vitest.numFailedTests
    $vitestPending = [int]$vitest.numPendingTests
    $vitestTodo = [int]$vitest.numTodoTests
    $vitestSkipped = $vitestPending + $vitestTodo
    if (-not [bool]$vitest.success -or $vitestTotal -ne $vitestPassed -or $vitestFailed -ne 0 -or $vitestSkipped -ne 0) {
        throw "Vitest reconciliation failed: discovered=$vitestTotal, executed=$vitestPassed, failed=$vitestFailed, skipped=$vitestSkipped"
    }

    $vitestSummary = [pscustomobject]@{
        files = @($vitest.testResults).Count
        discovered = $vitestTotal
        executed = $vitestPassed
        failed = $vitestFailed
        skipped = $vitestSkipped
    }
}

$deploymentSummary = $null
if (-not [string]::IsNullOrWhiteSpace($DeploymentPath)) {
    $resolvedDeploymentPath = Resolve-RepositoryPath $DeploymentPath
    if (-not (Test-Path $resolvedDeploymentPath -PathType Leaf)) {
        throw "Missing required deployment behavior log: $resolvedDeploymentPath"
    }

    $deploymentLines = @(Get-Content $resolvedDeploymentPath)
    $deploymentCases = @($deploymentLines | Where-Object { $_ -match '^TEST ' }).Count
    $nonProductionMarker = @($deploymentLines | Where-Object { $_ -eq 'NON_PRODUCTION_MECHANISM_TEST productionEligible=false result=passed' }).Count
    $completionMarker = @($deploymentLines | Where-Object { $_ -eq 'All AICopilot deployment behavior tests passed.' }).Count
    if ($deploymentCases -le 0 -or $nonProductionMarker -ne 1 -or $completionMarker -ne 1) {
        throw "Deployment behavior reconciliation failed: cases=$deploymentCases, nonProductionMarker=$nonProductionMarker, completionMarker=$completionMarker"
    }

    $deploymentSummary = [pscustomobject]@{ executed = $deploymentCases; failed = 0; skipped = 0 }
}

$summary = [pscustomobject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    requiredProjects = $projectResults
    dotnet = [pscustomobject]@{
        discovered = $discoveredTotal
        executed = $executedTotal
        passed = $passedTotal
        failed = $failedTotal
        skipped = $skippedTotal
    }
    vitest = $vitestSummary
    deployment = $deploymentSummary
}

$resolvedOutputPath = Resolve-RepositoryPath $OutputPath
New-Item -ItemType Directory -Path (Split-Path $resolvedOutputPath -Parent) -Force | Out-Null
$summary | ConvertTo-Json -Depth 6 | Set-Content $resolvedOutputPath -Encoding utf8
Write-Host "Required .NET reconciliation: discovered=$discoveredTotal, executed=$executedTotal, failed=$failedTotal, skipped=$skippedTotal."
Write-Host "Summary written to $resolvedOutputPath"
