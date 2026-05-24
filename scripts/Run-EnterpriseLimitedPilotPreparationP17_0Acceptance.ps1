[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-limited-pilot-preparation-p17_0-latest.md",
    [ValidateSet("", "ReviewPending", "BlockedByReview", "NoBlocker")]
    [string]$ReviewResultOverride = "",
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
        "(?i)(^|[^A-Za-z])rows\s*[:=]"
    )

    foreach ($pattern in $forbiddenPatterns) {
        if ($Content -match $pattern) {
            throw "$Name contains unsafe report content pattern: $pattern"
        }
    }
}

function Get-P16ReviewResult {
    if (-not [string]::IsNullOrWhiteSpace($ReviewResultOverride)) {
        return $ReviewResultOverride
    }

    $reportPath = ".\docs\enterprise-production-pilot-review-result-p16_6-latest.md"
    $ledgerPath = ".\docs\enterprise-production-pilot-review-result-p16_6-ledger.md"

    if (Test-Path $reportPath) {
        $report = Get-Content -LiteralPath $reportPath -Raw
        $match = [regex]::Match($report, "ReviewResult:\s+5\.5 Pro\s+(ReviewPending|BlockedByReview|NoBlocker)")
        if ($match.Success) {
            return $match.Groups[1].Value
        }
    }

    if (Test-Path $ledgerPath) {
        $ledger = Get-Content -LiteralPath $ledgerPath -Raw
        $match = [regex]::Match($ledger, "Review Result:\s+`?(ReviewPending|BlockedByReview|NoBlocker)`?")
        if ($match.Success) {
            return $match.Groups[1].Value
        }
    }

    return "ReviewPending"
}

function Get-GoNoGoDecision {
    param(
        [string]$ReviewResult,
        [bool]$HasFailures
    )

    if ($HasFailures) {
        return "BlockedByAcceptanceFailure"
    }

    if ($ReviewResult -eq "BlockedByReview") {
        return "BlockedByP16Review"
    }

    if ($ReviewResult -eq "NoBlocker") {
        return "ReadyForLimitedPilotPreparation"
    }

    return "BlockedByP16ReviewPending"
}

$results = @()

if (-not $SkipScopeGuard) {
    $results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
    }
}

$results += Invoke-Step -Name "P16.6 Review Result Inheritance Check" -Script {
    $p166Report = ".\docs\enterprise-production-pilot-review-result-p16_6-latest.md"
    $p166Scope = ".\docs\enterprise-production-pilot-review-result-p16_6-scope.md"
    $p166Ledger = ".\docs\enterprise-production-pilot-review-result-p16_6-ledger.md"
    foreach ($path in @($p166Report, $p166Scope, $p166Ledger)) {
        if (-not (Test-Path $path)) {
            throw "P16.6 evidence is missing: $path"
        }
    }

    $combined = @(
        Get-Content -LiteralPath $p166Report -Raw
        Get-Content -LiteralPath $p166Scope -Raw
        Get-Content -LiteralPath $p166Ledger -Raw
    ) -join "`n"

    foreach ($marker in @(
        "ReviewPending",
        "BlockedByReview",
        "NoBlocker",
        "query_cloud_data_readonly",
        "does not execute a real Pilot",
        "Execution Permission: not granted"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P16.6 evidence is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P16.6 evidence"
    "P16.6 review result evidence is present and remains non-executing."
}

$results += Invoke-Step -Name "P17.0 Scope And Preparation Package Check" -Script {
    $scope = ".\docs\enterprise-limited-pilot-preparation-p17_0-scope.md"
    $package = ".\docs\enterprise-limited-pilot-preparation-p17_0-package.md"
    foreach ($path in @($scope, $package)) {
        if (-not (Test-Path $path)) {
            throw "P17.0 document is missing: $path"
        }
    }

    $scopeContent = Get-Content -LiteralPath $scope -Raw
    $packageContent = Get-Content -LiteralPath $package -Raw

    foreach ($marker in @(
        "P17.0",
        "5-10 approved pilot users",
        "devices",
        "capacity_summary",
        "device_logs",
        "pass_station_records",
        "last 7 days",
        "maxRows: 50",
        "Tool Approval",
        "Final Approval",
        "Emergency stop",
        "runtime-only",
        "hash-only",
        "BlockedByP16ReviewPending",
        "ReadyForLimitedPilotPreparation",
        "query_cloud_data_readonly"
    )) {
        if (($scopeContent -notmatch [regex]::Escape($marker)) -and ($packageContent -notmatch [regex]::Escape($marker))) {
            throw "P17.0 material is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $scopeContent -Name "P17.0 scope"
    Assert-NoUnsafeReportContent -Content $packageContent -Name "P17.0 package"

    "P17.0 scope and preparation package markers passed."
}

$results += Invoke-Step -Name "P17.0 Go No-Go Matrix Check" -Script {
    $pending = Get-GoNoGoDecision -ReviewResult "ReviewPending" -HasFailures $false
    $blocked = Get-GoNoGoDecision -ReviewResult "BlockedByReview" -HasFailures $false
    $noBlocker = Get-GoNoGoDecision -ReviewResult "NoBlocker" -HasFailures $false
    $failure = Get-GoNoGoDecision -ReviewResult "NoBlocker" -HasFailures $true

    if ($pending -ne "BlockedByP16ReviewPending") {
        throw "ReviewPending mapped to unexpected decision: $pending"
    }
    if ($blocked -ne "BlockedByP16Review") {
        throw "BlockedByReview mapped to unexpected decision: $blocked"
    }
    if ($noBlocker -ne "ReadyForLimitedPilotPreparation") {
        throw "NoBlocker mapped to unexpected decision: $noBlocker"
    }
    if ($failure -ne "BlockedByAcceptanceFailure") {
        throw "Failure state mapped to unexpected decision: $failure"
    }

    "Go/No-Go matrix passed: pending and blocked stay blocked, no blocker enables preparation only."
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

$results += Invoke-Step -Name "P17.0 No Execution Claim Check" -Script {
    $combined = @(
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-preparation-p17_0-scope.md" -Raw
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-preparation-p17_0-package.md" -Raw
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
            throw "P17.0 material contains forbidden execution marker: $forbidden"
        }
    }

    "P17.0 material records preparation only and does not claim execution."
}

$p16ReviewResult = Get-P16ReviewResult
$prSummary = Get-PrSummary
$localHead = (git rev-parse HEAD 2>$null | Out-String).Trim()
$branch = (git branch --show-current 2>$null | Out-String).Trim()
$failedResults = @($results | Where-Object { -not $_.Succeeded })
$hasFailures = $failedResults.Count -gt 0
$goNoGoDecision = Get-GoNoGoDecision -ReviewResult $p16ReviewResult -HasFailures $hasFailures

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Limited Pilot Preparation P17.0 Acceptance")
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
$reportLines.Add("- P16ReviewResult: 5.5 Pro $p16ReviewResult")
$reportLines.Add("- GoNoGo: $goNoGoDecision")
$reportLines.Add("- Boundary: P17.0 prepares limited Pilot execution material only; it does not execute a real Pilot and is not GA")
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
$reportLines.Add("## Preparation Package")
$reportLines.Add("")
$reportLines.Add("- Pilot users: 5-10 approved users.")
$reportLines.Add("- Endpoint allowlist: devices, capacity_summary, device_logs, pass_station_records.")
$reportLines.Add("- Default time range: last 7 days.")
$reportLines.Add("- Default maxRows: 50.")
$reportLines.Add("- Required approvals: Tool Approval before query and Final Approval before final artifact state.")
$reportLines.Add("- Credential plan: record custody and approval only; do not store runtime secrets in this package.")
$reportLines.Add("- Retention plan: runtime-only rows use; operations ledger remains hash-only.")
$reportLines.Add("- Emergency stop and rollback must be rehearsed before execution.")
$reportLines.Add("")
$reportLines.Add("## Go No-Go")
$reportLines.Add("")
$reportLines.Add("- P16 review result: 5.5 Pro $p16ReviewResult.")
$reportLines.Add("- Current Go/No-Go: $goNoGoDecision.")
$reportLines.Add("- Pending or blocked P16.6 review prevents real Pilot execution.")
$reportLines.Add("- No-Blocker review only enables execution preparation, not execution.")
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
$reportLines.Add("- P16.6 formal no-Blocker review has not been supplied to this script by default.")
$reportLines.Add("- Real endpoint/token use remains outside P17.0 and must stay behind approved Pilot Window and rollback strategy.")
$reportLines.Add("- Limited Pilot execution must not start until approved Pilot Window, approved chain, rollback, emergency stop, and approved runtime credentials are all present.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Limited Pilot Preparation P17.0 acceptance report written to: $ReportPath"

if ($hasFailures) {
    exit 1
}
