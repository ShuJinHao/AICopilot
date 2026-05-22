[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-production-pilot-review-intake-p16_5-latest.md",
    [ValidateSet("ReviewPending", "BlockedByReview", "NoBlockerPlanningOnly")]
    [string]$ReviewIntake = "ReviewPending",
    [switch]$SkipScopeGuard
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Script
    )

    Write-Host "==> $Name"
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $global:LASTEXITCODE = 0
        $output = & $Script 2>&1 | Out-String
        $succeeded = $LASTEXITCODE -eq 0
    } catch {
        $output = $_ | Out-String
        $succeeded = $false
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    [pscustomobject]@{
        Name      = $Name
        Succeeded = $succeeded
        Output    = $output.Trim()
    }
}

function ConvertTo-ReportSafeOutput {
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    $safe = $Text
    $safe = [regex]::Replace($safe, [regex]::Escape($repoRoot), "<local-repo>", "IgnoreCase")

    $userProfile = [Environment]::GetFolderPath("UserProfile")
    if (-not [string]::IsNullOrWhiteSpace($userProfile)) {
        $safe = [regex]::Replace($safe, [regex]::Escape($userProfile), "<user-profile>", "IgnoreCase")
    }

    return $safe
}

function Get-PrSummary {
    try {
        $json = gh pr view 48 --json headRefOid,statusCheckRollup,url 2>$null | ConvertFrom-Json
        $check = $json.statusCheckRollup | Where-Object { $_.name -eq "simulation-rc" } | Select-Object -First 1
        return [pscustomobject]@{
            Head = $json.headRefOid
            Url = $json.url
            CiStatus = if ($check) { $check.status } else { "MISSING" }
            CiConclusion = if ($check) { $check.conclusion } else { "MISSING" }
            CiDetails = if ($check) { $check.detailsUrl } else { "" }
        }
    } catch {
        return [pscustomobject]@{
            Head = "unknown"
            Url = "unknown"
            CiStatus = "ERROR"
            CiConclusion = "ERROR"
            CiDetails = ""
        }
    }
}

function Assert-NoUnsafeReportContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $forbiddenPatterns = @(
        "C:\\Users",
        "AppData",
        "(?i)token\s*[:=]\s*[^,\r\n]+",
        "(?i)api\s*key\s*[:=]\s*[^,\r\n]+",
        "(?i)connection\s*string\s*[:=]\s*[^,\r\n]+",
        "(?i)raw\s+payload\s*[:=]",
        "(?i)full\s+sql\s*[:=]",
        "(?i)rows\s*[:=]"
    )

    foreach ($pattern in $forbiddenPatterns) {
        if ($Content -match $pattern) {
            throw "$Name contains unsafe report content pattern: $pattern"
        }
    }
}

function Get-NextStageDecision {
    param(
        [string]$Intake,
        [bool]$HasFailures
    )

    if ($HasFailures -or $Intake -eq "BlockedByReview") {
        return "BlockedByReview"
    }

    if ($Intake -eq "NoBlockerPlanningOnly") {
        return "ReadyForPilotExecutionPlanning"
    }

    return "ReviewPending"
}

$results = @()

if (-not $SkipScopeGuard) {
    $results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
    }
}

$results += Invoke-Step -Name "P16.4 Review Gate Inheritance Check" -Script {
    $p164Report = ".\docs\enterprise-production-pilot-review-gate-p16_4-latest.md"
    $p164Ledger = ".\docs\enterprise-production-pilot-review-gate-p16_4-review-ledger.md"
    foreach ($path in @($p164Report, $p164Ledger)) {
        if (-not (Test-Path $path)) {
            throw "P16.4 evidence is missing: $path"
        }
    }

    $report = Get-Content -LiteralPath $p164Report -Raw
    $ledger = Get-Content -LiteralPath $p164Ledger -Raw
    foreach ($marker in @(
        "ReviewConclusion: 5.5 Pro ReviewPending",
        "GoNoGo: ReadyForLimitedPilotExecutionPlanning",
        "does not execute a real Pilot",
        "query_cloud_data_readonly remains disabled"
    )) {
        if ($report -notmatch [regex]::Escape($marker)) {
            throw "P16.4 report is missing marker: $marker"
        }
    }

    foreach ($marker in @(
        "ReviewPending",
        "BlockedByReview",
        "ReadyForLimitedPilotExecutionPlanning",
        "Open",
        "AcceptedRisk",
        "FixedInCurrentHead",
        "DeferredToP17+",
        "OutOfScope"
    )) {
        if ($ledger -notmatch [regex]::Escape($marker)) {
            throw "P16.4 ledger is missing marker: $marker"
        }
    }

    "P16.4 review gate evidence is present and remains planning-only."
}

$results += Invoke-Step -Name "P16.5 Scope And Intake Ledger Check" -Script {
    $scope = ".\docs\enterprise-production-pilot-review-intake-p16_5-scope.md"
    $ledger = ".\docs\enterprise-production-pilot-review-intake-p16_5-ledger.md"
    foreach ($path in @($scope, $ledger)) {
        if (-not (Test-Path $path)) {
            throw "P16.5 document is missing: $path"
        }
    }

    $scopeContent = Get-Content -LiteralPath $scope -Raw
    $ledgerContent = Get-Content -LiteralPath $ledger -Raw

    foreach ($marker in @(
        "P16.5",
        "ReviewPending",
        "BlockedByReview",
        "NoBlockerPlanningOnly",
        "query_cloud_data_readonly",
        "No Cloud write"
    )) {
        if ($scopeContent -notmatch [regex]::Escape($marker)) {
            throw "P16.5 scope is missing marker: $marker"
        }
    }

    foreach ($marker in @(
        "ReviewPending",
        "BlockedByReview",
        "NoBlockerPlanningOnly",
        "ReadyForPilotExecutionPlanning",
        "Open",
        "AcceptedRisk",
        "FixedInCurrentHead",
        "DeferredToP17+",
        "OutOfScope",
        "query_cloud_data_readonly"
    )) {
        if ($ledgerContent -notmatch [regex]::Escape($marker)) {
            throw "P16.5 intake ledger is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $scopeContent -Name "P16.5 scope"
    Assert-NoUnsafeReportContent -Content $ledgerContent -Name "P16.5 intake ledger"

    "P16.5 scope and intake ledger markers passed."
}

$results += Invoke-Step -Name "Review Intake Routing Matrix Check" -Script {
    $pending = Get-NextStageDecision -Intake "ReviewPending" -HasFailures $false
    $blocked = Get-NextStageDecision -Intake "BlockedByReview" -HasFailures $false
    $noBlocker = Get-NextStageDecision -Intake "NoBlockerPlanningOnly" -HasFailures $false
    $failure = Get-NextStageDecision -Intake "NoBlockerPlanningOnly" -HasFailures $true

    if ($pending -ne "ReviewPending") {
        throw "ReviewPending mapped to unexpected decision: $pending"
    }
    if ($blocked -ne "BlockedByReview") {
        throw "BlockedByReview mapped to unexpected decision: $blocked"
    }
    if ($noBlocker -ne "ReadyForPilotExecutionPlanning") {
        throw "NoBlockerPlanningOnly mapped to unexpected decision: $noBlocker"
    }
    if ($failure -ne "BlockedByReview") {
        throw "Failure state mapped to unexpected decision: $failure"
    }

    "Routing matrix passed: pending stays pending, blocker routes repair, no blocker routes planning only."
}

$results += Invoke-Step -Name "GitHub PR #48 Current Head And CI Check" -Script {
    $summary = Get-PrSummary
    if ($summary.Head -eq "unknown") {
        throw "Unable to read PR #48."
    }

    if ($summary.CiStatus -ne "COMPLETED" -or $summary.CiConclusion -ne "SUCCESS") {
        throw "PR #48 simulation-rc is not success. status=$($summary.CiStatus) conclusion=$($summary.CiConclusion)"
    }

    "PR #48 head $($summary.Head) simulation-rc SUCCESS $($summary.CiDetails)"
}

$results += Invoke-Step -Name "P16.5 No Execution Claim Check" -Script {
    $combined = @(
        Get-Content -LiteralPath ".\docs\enterprise-production-pilot-review-intake-p16_5-scope.md" -Raw
        Get-Content -LiteralPath ".\docs\enterprise-production-pilot-review-intake-p16_5-ledger.md" -Raw
    ) -join "`n"

    foreach ($forbidden in @(
        "ReadyForLimitedPilotExecution",
        "Real Pilot executed",
        "Real endpoint configured",
        "Real token configured",
        "query_cloud_data_readonly enabled",
        "GA Permission: granted"
    )) {
        if ($combined -match [regex]::Escape($forbidden)) {
            throw "P16.5 material contains forbidden execution marker: $forbidden"
        }
    }

    "P16.5 material records review intake only and does not claim execution."
}

$prSummary = Get-PrSummary
$localHead = (git rev-parse HEAD 2>$null | Out-String).Trim()
$branch = (git branch --show-current 2>$null | Out-String).Trim()
$failedResults = @($results | Where-Object { -not $_.Succeeded })
$hasFailures = $failedResults.Count -gt 0
$nextStageDecision = Get-NextStageDecision -Intake $ReviewIntake -HasFailures $hasFailures

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Production Pilot Review Intake P16.5 Acceptance")
$reportLines.Add("")
$reportLines.Add("- GeneratedAt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$reportLines.Add("- Repository: <local-repo>")
$reportLines.Add("- LocalHeadAtGeneration: $localHead")
$reportLines.Add("- Branch: $branch")
$reportLines.Add("- SubmittedStateNote: after committing this report refresh, use PR #48 current head and GitHub checks as the authoritative submitted-state evidence")
$reportLines.Add("- PullRequest: $($prSummary.Url)")
$reportLines.Add("- PullRequestHeadAtGeneration: $($prSummary.Head)")
$reportLines.Add("- GitHubCIAtGeneration: simulation-rc status=$($prSummary.CiStatus) conclusion=$($prSummary.CiConclusion)")
$reportLines.Add("- GitHubCIDetails: $($prSummary.CiDetails)")
$reportLines.Add("- ReviewIntake: 5.5 Pro $ReviewIntake")
$reportLines.Add("- NextStageDecision: $nextStageDecision")
$reportLines.Add("- Boundary: P16.5 records review intake only; it does not execute a real Pilot and is not GA")
$reportLines.Add("- Default State: query_cloud_data_readonly remains disabled, hidden, and non-executable")
$reportLines.Add("- Forbidden: Cloud write, Recipe/version, free SQL, raw payload, raw business records, token/API key/connection string output")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## Review Intake")
$reportLines.Add("")
$reportLines.Add("- Review intake state: 5.5 Pro $ReviewIntake.")
$reportLines.Add("- Pending review keeps the project planning-only.")
$reportLines.Add("- Blocker review routes to P16.6 repair.")
$reportLines.Add("- No-Blocker review routes to P17.0 limited Pilot execution preparation only.")
$reportLines.Add("- Limited execution approval is not claimed by P16.5.")
$reportLines.Add("")
$reportLines.Add("## Details")
$reportLines.Add("")

foreach ($result in $results) {
    $reportLines.Add("### $($result.Name)")
    $reportLines.Add("")
    $reportLines.Add('```text')
    $safeOutput = ConvertTo-ReportSafeOutput -Text $result.Output
    foreach ($line in ($safeOutput -split "`r?`n")) {
        $reportLines.Add($line.TrimEnd())
    }
    $reportLines.Add('```')
    $reportLines.Add("")
}

$reportLines.Add("## Remaining Risk")
$reportLines.Add("")
$reportLines.Add("- 5.5 Pro review text has not been supplied to this script by default.")
$reportLines.Add("- Real endpoint/token use remains outside P16.5 and must stay behind approved Pilot Window and rollback strategy.")
$reportLines.Add("- Limited Pilot execution must not start until no-Blocker review, approved Pilot Window, approved chain, rollback, emergency stop, and approved runtime credentials are all present.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Production Pilot Review Intake P16.5 acceptance report written to: $ReportPath"

if ($hasFailures) {
    exit 1
}
