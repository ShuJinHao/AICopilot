[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-production-pilot-execution-readiness-p16_2-latest.md",
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

$results = @()

if (-not $SkipScopeGuard) {
    $results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
    }
}

$results += Invoke-Step -Name "P16.0 Acceptance Inheritance Check" -Script {
    $p160Report = ".\docs\enterprise-production-pilot-hardening-p16_0-latest.md"
    if (-not (Test-Path $p160Report)) {
        throw "P16.0 acceptance report is missing: $p160Report"
    }

    $content = Get-Content -LiteralPath $p160Report -Raw
    foreach ($marker in @(
        "Enterprise Production Pilot Hardening P16.0 Scope Guard: PASSED",
        "Run P16.0 Focused Backend Tests: PASSED",
        "Run CloudReadonly Route Contract Tests: PASSED",
        "P12 Store: ProductionPilotWindow and ProductionPilotRun are persisted",
        "P13 Store: ProductionControlledPilotIntent and ProductionControlledPilotRun are persisted",
        "Artifact Backfill: final P12/P13 artifacts backfill ProductionPilotRunLedger artifact refs",
        "Rows Retention: runtime rows remain short-lived"
    )) {
        if ($content -notmatch [regex]::Escape($marker)) {
            throw "P16.0 report is missing inherited marker: $marker"
        }
    }

    "Using existing P16.0 acceptance report: $p160Report"
}

$results += Invoke-Step -Name "P16.2 Readiness Package Check" -Script {
    $package = ".\docs\enterprise-production-pilot-execution-readiness-p16_2-package.md"
    if (-not (Test-Path $package)) {
        throw "P16.2 readiness package is missing: $package"
    }

    $content = Get-Content -LiteralPath $package -Raw
    foreach ($marker in @(
        "ReadyForPilotExecutionPlanning",
        "ReadyForLimitedPilotExecution",
        "BlockedByReview",
        "devices",
        "capacity_summary",
        "device_logs",
        "pass_station_records",
        "latest 7 days",
        "maxRows",
        "Tool Approval",
        "Final Approval",
        "emergency stop",
        "hash-only",
        "query_cloud_data_readonly"
    )) {
        if ($content -notmatch [regex]::Escape($marker)) {
            throw "P16.2 readiness package is missing marker: $marker"
        }
    }

    if ($content -match "(?i)(token\s*[:=]\s*[^,\r\n]+|api key\s*[:=]\s*[^,\r\n]+|connection string\s*[:=]\s*[^,\r\n]+)") {
        throw "P16.2 readiness package contains a secret-like literal."
    }

    "P16.2 readiness package markers passed."
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

$results += Invoke-Step -Name "P16.2 Report Safety Marker Check" -Script {
    $forbidden = @(
        "ReadyForLimitedPilotExecution: true",
        "Real endpoint configured",
        "Real token configured",
        "query_cloud_data_readonly enabled"
    )

    $package = Get-Content -LiteralPath ".\docs\enterprise-production-pilot-execution-readiness-p16_2-package.md" -Raw
    foreach ($marker in $forbidden) {
        if ($package -match [regex]::Escape($marker)) {
            throw "P16.2 package contains forbidden execution marker: $marker"
        }
    }

    "P16.2 package does not claim real execution readiness."
}

$prSummary = Get-PrSummary
$localHead = (git rev-parse HEAD 2>$null | Out-String).Trim()
$branch = (git branch --show-current 2>$null | Out-String).Trim()
$goNoGo = if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    "BlockedByReview"
} elseif ($prSummary.CiStatus -eq "COMPLETED" -and $prSummary.CiConclusion -eq "SUCCESS") {
    "ReadyForPilotExecutionPlanning"
} else {
    "BlockedByReview"
}

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Production Pilot Execution Readiness P16.2 Acceptance")
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
$reportLines.Add("- ReviewConclusion: 5.5 Pro pending")
$reportLines.Add("- GoNoGo: $goNoGo")
$reportLines.Add("- Boundary: P16.2 is execution readiness review only; it does not execute a real Pilot and is not GA")
$reportLines.Add("- Default State: query_cloud_data_readonly remains disabled, hidden, and non-executable")
$reportLines.Add("- Forbidden: Cloud write, Recipe/version, free SQL, raw payload, rows, token/API key/connection string output")
$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")

foreach ($result in $results) {
    $status = if ($result.Succeeded) { "PASSED" } else { "FAILED" }
    $reportLines.Add("- $($result.Name): $status")
}

$reportLines.Add("")
$reportLines.Add("## Execution Readiness Package")
$reportLines.Add("")
$reportLines.Add("- Pilot Window inputs required: name, time range, owner, approver, rollback owner, emergency stop owner.")
$reportLines.Add("- Endpoint allowlist: devices, capacity_summary, device_logs, pass_station_records.")
$reportLines.Add("- Default limits: latest 7 days, maxRows=50.")
$reportLines.Add("- Required approvals: Tool Approval and Final Approval.")
$reportLines.Add("- Retention: runtime rows only; operations ledger hash-only; reports/readiness/frontend do not return rows/raw payload.")
$reportLines.Add("- Current review state: 5.5 Pro pending, so this report does not authorize limited Pilot execution.")
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
$reportLines.Add("- 5.5 Pro has not yet returned a no-Blocker conclusion.")
$reportLines.Add("- Real endpoint/token use remains outside P16.2 and must stay behind approved Pilot Window and rollback strategy.")
$reportLines.Add("- ReadyForLimitedPilotExecution must not be claimed until CI success, no-Blocker review, approved Pilot Window, and approved runtime credentials are all present.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Production Pilot Execution Readiness P16.2 acceptance report written to: $ReportPath"

if (($results | Where-Object { -not $_.Succeeded }).Count -gt 0) {
    exit 1
}
