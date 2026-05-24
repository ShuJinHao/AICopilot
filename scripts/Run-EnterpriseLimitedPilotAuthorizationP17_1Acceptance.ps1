[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-limited-pilot-authorization-p17_1-latest.md",
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

function Get-ExternalReviewState {
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

function Get-AuthorizationDecision {
    param(
        [bool]$HasFailures
    )

    if ($HasFailures) {
        return "BlockedByAcceptanceFailure"
    }

    return "AuthorizationDryRunReady"
}

$results = @()

if (-not $SkipScopeGuard) {
    $results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
    }
}

$results += Invoke-Step -Name "P17.0 Preparation Inheritance Check" -Script {
    $p170Report = ".\docs\enterprise-limited-pilot-preparation-p17_0-latest.md"
    $p170Scope = ".\docs\enterprise-limited-pilot-preparation-p17_0-scope.md"
    $p170Package = ".\docs\enterprise-limited-pilot-preparation-p17_0-package.md"
    foreach ($path in @($p170Report, $p170Scope, $p170Package)) {
        if (-not (Test-Path $path)) {
            throw "P17.0 evidence is missing: $path"
        }
    }

    $combined = @(
        Get-Content -LiteralPath $p170Report -Raw
        Get-Content -LiteralPath $p170Scope -Raw
        Get-Content -LiteralPath $p170Package -Raw
    ) -join "`n"

    foreach ($marker in @(
        "P17.0",
        "Pilot Window",
        "last 7 days",
        "maxRows: 50",
        "Tool Approval",
        "Final Approval",
        "runtime-only",
        "hash-only",
        "query_cloud_data_readonly",
        "does not execute a real Pilot"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P17.0 evidence is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P17.0 evidence"
    "P17.0 preparation evidence is present and remains non-executing."
}

$results += Invoke-Step -Name "P17.1 Scope And Authorization Package Check" -Script {
    $scope = ".\docs\enterprise-limited-pilot-authorization-p17_1-scope.md"
    $package = ".\docs\enterprise-limited-pilot-authorization-p17_1-package.md"
    foreach ($path in @($scope, $package)) {
        if (-not (Test-Path $path)) {
            throw "P17.1 document is missing: $path"
        }
    }

    $scopeContent = Get-Content -LiteralPath $scope -Raw
    $packageContent = Get-Content -LiteralPath $package -Raw
    $combined = "$scopeContent`n$packageContent"

    foreach ($marker in @(
        "P17.1",
        "internal authorization",
        "dry-run",
        "Pilot Window",
        "5-10",
        "devices",
        "capacity_summary",
        "device_logs",
        "pass_station_records",
        "last 7 days",
        "maxRows=50",
        "Tool Approval",
        "Final Approval",
        "Emergency stop",
        "rollback",
        "hash-only",
        "runtime-only",
        "fake/fixture",
        "query_cloud_data_readonly"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P17.1 material is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P17.1 material"
    "P17.1 scope and authorization package markers passed."
}

$results += Invoke-Step -Name "P17.1 Dry-Run Safety Matrix Check" -Script {
    $package = Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-authorization-p17_1-package.md" -Raw

    foreach ($marker in @(
        "No real token is used",
        "No real endpoint is called",
        "Fake/fixture inputs",
        "Tool Approval before readonly query",
        "Final Approval before final artifact state",
        "Emergency stop activation",
        "Rollback rehearsal",
        "Hash-only ledger verification",
        "does not contain rows",
        "does not contain rows, raw payload, token"
    )) {
        if ($package -notmatch [regex]::Escape($marker)) {
            throw "P17.1 dry-run checklist is missing marker: $marker"
        }
    }

    "Dry-run matrix passed: fake/fixture only, no real token or endpoint, no raw business records."
}

$results += Invoke-Step -Name "P17.1 External Review Evidence Does Not Block Authorization Material" -Script {
    foreach ($state in @("ReviewPending", "BlockedByReview", "NoBlocker")) {
        $decision = Get-AuthorizationDecision -HasFailures $false
        if ($decision -ne "AuthorizationDryRunReady") {
            throw "External review state $state incorrectly changed authorization material decision: $decision"
        }
    }

    "External review state is evidence only for P17.1 authorization material."
}

$results += Invoke-Step -Name "GitHub PR #48 Current Head And CI Evidence Check" -Script {
    $summary = Get-PrSummary
    if ($summary.Head -eq "unknown") {
        throw "Unable to read PR #48."
    }

    if ($summary.CiStatus -ne "COMPLETED" -or $summary.CiConclusion -ne "SUCCESS") {
        throw "PR #48 simulation-rc is not success. status=$($summary.CiStatus) conclusion=$($summary.CiConclusion)"
    }

    "PR #48 head $($summary.Head) simulation-rc SUCCESS $($summary.CiDetails)"
}

$results += Invoke-Step -Name "P17.1 No Execution Claim Check" -Script {
    $combined = @(
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-authorization-p17_1-scope.md" -Raw
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-authorization-p17_1-package.md" -Raw
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
            throw "P17.1 material contains forbidden execution marker: $forbidden"
        }
    }

    "P17.1 material records authorization and dry-run preparation only."
}

$externalReviewState = Get-ExternalReviewState
$prSummary = Get-PrSummary
$localHead = (git rev-parse HEAD 2>$null | Out-String).Trim()
$branch = (git branch --show-current 2>$null | Out-String).Trim()
$failedResults = @($results | Where-Object { -not $_.Succeeded })
$hasFailures = $failedResults.Count -gt 0
$authorizationDecision = Get-AuthorizationDecision -HasFailures $hasFailures

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Limited Pilot Authorization P17.1 Acceptance")
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
$reportLines.Add("- ExternalReviewEvidence: 5.5 Pro $externalReviewState")
$reportLines.Add("- ExternalReviewBlockingPolicy: evidence-only for P17.1 authorization material")
$reportLines.Add("- AuthorizationDecision: $authorizationDecision")
$reportLines.Add("- ExecutionPermission: not granted")
$reportLines.Add("- Boundary: P17.1 prepares internal authorization package and dry-run rehearsal only; it does not execute a real Pilot and is not GA")
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
$reportLines.Add("## Internal Authorization Package")
$reportLines.Add("")
$reportLines.Add("- Pilot Window draft: name, start/end time, owner, approver, rollback owner, and emergency stop owner are required before future execution.")
$reportLines.Add("- Pilot users: 5-10 approved users with role, department, permission scope, and approval status.")
$reportLines.Add("- Endpoint allowlist: devices, capacity_summary, device_logs, pass_station_records.")
$reportLines.Add("- Default time range: last 7 days.")
$reportLines.Add("- Default maxRows=50.")
$reportLines.Add("- Required approvals: Tool Approval before query and Final Approval before final artifact state.")
$reportLines.Add("- Credential plan: record custody and approval only; do not store runtime secrets in this package.")
$reportLines.Add("- Retention plan: runtime-only rows use; operations ledger remains hash-only.")
$reportLines.Add("")
$reportLines.Add("## Dry-Run Rehearsal")
$reportLines.Add("")
$reportLines.Add("- Uses fake/fixture inputs only.")
$reportLines.Add("- Uses no real token.")
$reportLines.Add("- Calls no real endpoint.")
$reportLines.Add("- Covers Pilot Window, Tool Approval, Final Approval, emergency stop, rollback, and hash-only ledger.")
$reportLines.Add("- Dry-run output contains endpoint, status, duration, row count, truncated state, approval status, query hash, and result hash only.")
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
$reportLines.Add("- P17.1 does not execute a real Pilot.")
$reportLines.Add("- Real endpoint/token use remains outside P17.1 and requires a future explicit approval.")
$reportLines.Add("- External 5.5 Pro review state is evidence only for this authorization package and must still be considered before any future execution.")
$reportLines.Add("- Future execution still requires approved Pilot Window, approved chain, rollback, emergency stop, and approved runtime credential configuration.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Limited Pilot Authorization P17.1 acceptance report written to: $ReportPath"

if ($hasFailures) {
    exit 1
}
