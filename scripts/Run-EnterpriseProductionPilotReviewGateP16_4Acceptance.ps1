[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-production-pilot-review-gate-p16_4-latest.md",
    [ValidateSet("ReviewPending", "BlockedByReview", "NoBlocker")]
    [string]$ReviewConclusion = "ReviewPending",
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

function Get-GoNoGo {
    param(
        [string]$Conclusion,
        [bool]$HasFailures
    )

    if ($HasFailures -or $Conclusion -eq "BlockedByReview") {
        return "BlockedByReview"
    }

    return "ReadyForLimitedPilotExecutionPlanning"
}

$results = @()

if (-not $SkipScopeGuard) {
    $results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
    }
}

$results += Invoke-Step -Name "P16.3 Execution Plan Inheritance Check" -Script {
    $p163Report = ".\docs\enterprise-production-pilot-execution-plan-p16_3-latest.md"
    $p163Package = ".\docs\enterprise-production-pilot-execution-plan-p16_3-package.md"
    foreach ($path in @($p163Report, $p163Package)) {
        if (-not (Test-Path $path)) {
            throw "P16.3 evidence is missing: $path"
        }
    }

    $report = Get-Content -LiteralPath $p163Report -Raw
    $package = Get-Content -LiteralPath $p163Package -Raw
    foreach ($marker in @(
        "ReviewConclusion: 5.5 Pro ReviewPending",
        "GoNoGo: ReadyForLimitedPilotExecutionPlanning",
        "does not execute a real Pilot",
        "query_cloud_data_readonly remains disabled"
    )) {
        if ($report -notmatch [regex]::Escape($marker)) {
            throw "P16.3 report is missing marker: $marker"
        }
    }

    foreach ($marker in @(
        "ReviewPending",
        "ReadyForLimitedPilotExecutionPlanning",
        "ReadyForLimitedPilotExecution",
        "Pilot Window name",
        "Production operations ledger",
        "query_cloud_data_readonly"
    )) {
        if ($package -notmatch [regex]::Escape($marker)) {
            throw "P16.3 package is missing marker: $marker"
        }
    }

    "P16.3 execution plan evidence is present and remains planning-only."
}

$results += Invoke-Step -Name "P16.4 Scope And Review Ledger Check" -Script {
    $scope = ".\docs\enterprise-production-pilot-review-gate-p16_4-scope.md"
    $ledger = ".\docs\enterprise-production-pilot-review-gate-p16_4-review-ledger.md"
    foreach ($path in @($scope, $ledger)) {
        if (-not (Test-Path $path)) {
            throw "P16.4 document is missing: $path"
        }
    }

    $scopeContent = Get-Content -LiteralPath $scope -Raw
    $ledgerContent = Get-Content -LiteralPath $ledger -Raw

    foreach ($marker in @(
        "P16.4",
        "ReviewPending",
        "BlockedByReview",
        "ReadyForLimitedPilotExecutionPlanning",
        "query_cloud_data_readonly",
        "No Cloud write"
    )) {
        if ($scopeContent -notmatch [regex]::Escape($marker)) {
            throw "P16.4 scope is missing marker: $marker"
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
        "OutOfScope",
        "query_cloud_data_readonly"
    )) {
        if ($ledgerContent -notmatch [regex]::Escape($marker)) {
            throw "P16.4 review ledger is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $scopeContent -Name "P16.4 scope"
    Assert-NoUnsafeReportContent -Content $ledgerContent -Name "P16.4 review ledger"

    "P16.4 scope and review ledger markers passed."
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

$results += Invoke-Step -Name "P16.4 No Execution Claim Check" -Script {
    $combined = @(
        Get-Content -LiteralPath ".\docs\enterprise-production-pilot-review-gate-p16_4-scope.md" -Raw
        Get-Content -LiteralPath ".\docs\enterprise-production-pilot-review-gate-p16_4-review-ledger.md" -Raw
    ) -join "`n"

    foreach ($forbidden in @(
        "Real Pilot executed",
        "Real endpoint configured",
        "Real token configured",
        "query_cloud_data_readonly enabled",
        "GA Permission: granted"
    )) {
        if ($combined -match [regex]::Escape($forbidden)) {
            throw "P16.4 material contains forbidden execution marker: $forbidden"
        }
    }

    "P16.4 material records review gate only and does not claim execution."
}

$prSummary = Get-PrSummary
$localHead = (git rev-parse HEAD 2>$null | Out-String).Trim()
$branch = (git branch --show-current 2>$null | Out-String).Trim()
$failedResults = @($results | Where-Object { -not $_.Succeeded })
$hasFailures = $failedResults.Count -gt 0
$goNoGo = Get-GoNoGo -Conclusion $ReviewConclusion -HasFailures $hasFailures

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Production Pilot Review Gate P16.4 Acceptance")
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
$reportLines.Add("- ReviewConclusion: 5.5 Pro $ReviewConclusion")
$reportLines.Add("- GoNoGo: $goNoGo")
$reportLines.Add("- Boundary: P16.4 records the review gate only; it does not execute a real Pilot and is not GA")
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
$reportLines.Add("## Review Gate")
$reportLines.Add("")
$reportLines.Add("- Review state: 5.5 Pro $ReviewConclusion.")
$reportLines.Add("- If Blocker exists: next stage is P16.5 repair.")
$reportLines.Add("- If no Blocker exists: next stage is limited Pilot execution planning only.")
$reportLines.Add("- ReadyForLimitedPilotExecution is not claimed by P16.4.")
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
$reportLines.Add("- Real endpoint/token use remains outside P16.4 and must stay behind approved Pilot Window and rollback strategy.")
$reportLines.Add("- ReadyForLimitedPilotExecution must not be claimed until CI success, no-Blocker review, approved Pilot Window, and approved runtime credentials are all present.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Production Pilot Review Gate P16.4 acceptance report written to: $ReportPath"

if ($hasFailures) {
    exit 1
}

