[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-production-pilot-review-result-p16_6-latest.md",
    [ValidateSet("ReviewPending", "BlockedByReview", "NoBlocker")]
    [string]$ReviewResult = "ReviewPending",
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
        [string]$Result,
        [bool]$HasFailures
    )

    if ($HasFailures -or $Result -eq "BlockedByReview") {
        return "BlockedByReview"
    }

    if ($Result -eq "NoBlocker") {
        return "ReadyForP17Planning"
    }

    return "ReviewPending"
}

$results = @()

if (-not $SkipScopeGuard) {
    $results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
    }
}

$results += Invoke-Step -Name "P16.5 Review Intake Inheritance Check" -Script {
    $p165Report = ".\docs\enterprise-production-pilot-review-intake-p16_5-latest.md"
    $p165Scope = ".\docs\enterprise-production-pilot-review-intake-p16_5-scope.md"
    $p165Ledger = ".\docs\enterprise-production-pilot-review-intake-p16_5-ledger.md"
    foreach ($path in @($p165Report, $p165Scope, $p165Ledger)) {
        if (-not (Test-Path $path)) {
            throw "P16.5 evidence is missing: $path"
        }
    }

    $combined = @(
        Get-Content -LiteralPath $p165Report -Raw
        Get-Content -LiteralPath $p165Scope -Raw
        Get-Content -LiteralPath $p165Ledger -Raw
    ) -join "`n"

    foreach ($marker in @(
        "ReviewPending",
        "BlockedByReview",
        "query_cloud_data_readonly",
        "does not execute a real Pilot",
        "Execution Permission: not granted"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P16.5 evidence is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P16.5 evidence"
    "P16.5 review intake evidence is present and remains non-executing."
}

$results += Invoke-Step -Name "P16.6 Scope And Review Result Ledger Check" -Script {
    $scope = ".\docs\enterprise-production-pilot-review-result-p16_6-scope.md"
    $ledger = ".\docs\enterprise-production-pilot-review-result-p16_6-ledger.md"
    foreach ($path in @($scope, $ledger)) {
        if (-not (Test-Path $path)) {
            throw "P16.6 document is missing: $path"
        }
    }

    $scopeContent = Get-Content -LiteralPath $scope -Raw
    $ledgerContent = Get-Content -LiteralPath $ledger -Raw

    foreach ($marker in @(
        "P16.6",
        "ReviewPending",
        "BlockedByReview",
        "NoBlocker",
        "ReadyForP17Planning",
        "query_cloud_data_readonly",
        "No Cloud write"
    )) {
        if (($scopeContent -notmatch [regex]::Escape($marker)) -and ($ledgerContent -notmatch [regex]::Escape($marker))) {
            throw "P16.6 material is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $scopeContent -Name "P16.6 scope"
    Assert-NoUnsafeReportContent -Content $ledgerContent -Name "P16.6 ledger"

    "P16.6 scope and review result ledger markers passed."
}

$results += Invoke-Step -Name "Review Result Routing Matrix Check" -Script {
    $pending = Get-NextStageDecision -Result "ReviewPending" -HasFailures $false
    $blocked = Get-NextStageDecision -Result "BlockedByReview" -HasFailures $false
    $noBlocker = Get-NextStageDecision -Result "NoBlocker" -HasFailures $false
    $failure = Get-NextStageDecision -Result "NoBlocker" -HasFailures $true

    if ($pending -ne "ReviewPending") {
        throw "ReviewPending mapped to unexpected decision: $pending"
    }
    if ($blocked -ne "BlockedByReview") {
        throw "BlockedByReview mapped to unexpected decision: $blocked"
    }
    if ($noBlocker -ne "ReadyForP17Planning") {
        throw "NoBlocker mapped to unexpected decision: $noBlocker"
    }
    if ($failure -ne "BlockedByReview") {
        throw "Failure state mapped to unexpected decision: $failure"
    }

    "Routing matrix passed: pending stays pending, blocker routes repair, no blocker routes P17 planning only."
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

$results += Invoke-Step -Name "P16.6 No Execution Claim Check" -Script {
    $combined = @(
        Get-Content -LiteralPath ".\docs\enterprise-production-pilot-review-result-p16_6-scope.md" -Raw
        Get-Content -LiteralPath ".\docs\enterprise-production-pilot-review-result-p16_6-ledger.md" -Raw
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
            throw "P16.6 material contains forbidden execution marker: $forbidden"
        }
    }

    "P16.6 material records formal review routing only and does not claim execution."
}

$prSummary = Get-PrSummary
$localHead = (git rev-parse HEAD 2>$null | Out-String).Trim()
$branch = (git branch --show-current 2>$null | Out-String).Trim()
$failedResults = @($results | Where-Object { -not $_.Succeeded })
$hasFailures = $failedResults.Count -gt 0
$nextStageDecision = Get-NextStageDecision -Result $ReviewResult -HasFailures $hasFailures

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Production Pilot Review Result P16.6 Acceptance")
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
$reportLines.Add("- ReviewResult: 5.5 Pro $ReviewResult")
$reportLines.Add("- NextStageDecision: $nextStageDecision")
$reportLines.Add("- Boundary: P16.6 records formal review routing only; it does not execute a real Pilot and is not GA")
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
$reportLines.Add("## Review Result Routing")
$reportLines.Add("")
$reportLines.Add("- Review result state: 5.5 Pro $ReviewResult.")
$reportLines.Add("- Pending review keeps the project planning-only.")
$reportLines.Add("- Blocker review routes to P16.7 repair.")
$reportLines.Add("- No-Blocker review routes to P17.0 limited Pilot execution planning only.")
$reportLines.Add("- Limited Pilot execution approval is not claimed by P16.6.")
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
$reportLines.Add("- 5.5 Pro formal review text has not been supplied to this script by default.")
$reportLines.Add("- Real endpoint/token use remains outside P16.6 and must stay behind approved Pilot Window and rollback strategy.")
$reportLines.Add("- Limited Pilot execution must not start until no-Blocker review, approved Pilot Window, approved chain, rollback, emergency stop, and approved runtime credentials are all present.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Production Pilot Review Result P16.6 acceptance report written to: $ReportPath"

if ($hasFailures) {
    exit 1
}
