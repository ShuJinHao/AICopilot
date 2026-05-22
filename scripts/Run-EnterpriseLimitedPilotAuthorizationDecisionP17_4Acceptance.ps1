[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-limited-pilot-authorization-decision-p17_4-latest.md",
    [ValidateSet("AuthorizationPending", "AuthorizationRejected", "AuthorizationGrantedForPlanning")]
    [string]$AuthorizationDecision = "AuthorizationPending",
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

function New-WindowFreeze {
    [pscustomobject]@{
        PilotWindowName = "limited production readonly Pilot"
        StartAt = "ToBeApproved"
        EndAt = "ToBeApproved"
        Owner = "ToBeAssigned"
        Approver = "ToBeAssigned"
        RollbackOwner = "ToBeAssigned"
        EmergencyStopOwner = "ToBeAssigned"
        PilotUserCountTarget = "5-10"
        EndpointAllowlist = @("devices", "capacity_summary", "device_logs", "pass_station_records")
        TimeRange = "last 7 days"
        MaxRows = 50
        RequiresToolApproval = $true
        RequiresFinalApproval = $true
    }
}

function New-CredentialResponsibility {
    [pscustomobject]@{
        ConfigurationOwner = "ToBeAssigned"
        Custodian = "ToBeAssigned"
        Approver = "ToBeAssigned"
        CredentialStatus = "NotConfigured"
        RealCredentialRead = $false
        RealCredentialWritten = $false
        RealCredentialDisplayed = $false
    }
}

function Test-WindowFreezeComplete {
    param([Parameter(Mandatory = $true)]$WindowFreeze)

    foreach ($field in @("PilotWindowName", "StartAt", "EndAt", "Owner", "Approver", "RollbackOwner", "EmergencyStopOwner", "PilotUserCountTarget", "TimeRange")) {
        if ([string]::IsNullOrWhiteSpace([string]$WindowFreeze.$field)) {
            return $false
        }
    }

    foreach ($endpoint in @("devices", "capacity_summary", "device_logs", "pass_station_records")) {
        if ($WindowFreeze.EndpointAllowlist -notcontains $endpoint) {
            return $false
        }
    }

    return $WindowFreeze.MaxRows -eq 50 -and $WindowFreeze.RequiresToolApproval -and $WindowFreeze.RequiresFinalApproval
}

function Test-CredentialResponsibilityComplete {
    param([Parameter(Mandatory = $true)]$CredentialResponsibility)

    foreach ($field in @("ConfigurationOwner", "Custodian", "Approver", "CredentialStatus")) {
        if ([string]::IsNullOrWhiteSpace([string]$CredentialResponsibility.$field)) {
            return $false
        }
    }

    return -not ($CredentialResponsibility.RealCredentialRead -or $CredentialResponsibility.RealCredentialWritten -or $CredentialResponsibility.RealCredentialDisplayed)
}

function Get-GoNoGoDecision {
    param(
        [string]$Decision,
        [bool]$WindowFreezeComplete,
        [bool]$CredentialResponsibilityComplete,
        [bool]$HasAcceptanceFailures
    )

    if ($HasAcceptanceFailures) {
        return "BlockedByAcceptanceFailure"
    }

    if (-not $WindowFreezeComplete) {
        return "WindowFreezeIncomplete"
    }

    if (-not $CredentialResponsibilityComplete) {
        return "CredentialResponsibilityIncomplete"
    }

    if ($Decision -eq "AuthorizationRejected") {
        return "AuthorizationRejected"
    }

    if ($Decision -eq "AuthorizationGrantedForPlanning") {
        return "AuthorizationGrantedForPlanning"
    }

    return "AuthorizationPending"
}

$results = @()

if (-not $SkipScopeGuard) {
    $results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
    }
}

$results += Invoke-Step -Name "P17.3 Authorization Package Inheritance Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-execution-authorization-p17_3-scope.md",
        ".\docs\enterprise-limited-pilot-execution-authorization-p17_3-package.md",
        ".\docs\enterprise-limited-pilot-execution-authorization-p17_3-latest.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P17.3 evidence is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"

    foreach ($marker in @(
        "P17.3",
        "Execution Authorization Request",
        "Credential Readiness Preflight",
        "MissingAuthorization",
        "ReadyForExplicitExecutionApproval",
        "does not execute a real Pilot",
        "query_cloud_data_readonly"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P17.3 inheritance is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P17.3 inheritance"
    "P17.3 authorization package and latest evidence are present."
}

$results += Invoke-Step -Name "P17.4 Scope And Package Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-authorization-decision-p17_4-scope.md",
        ".\docs\enterprise-limited-pilot-authorization-decision-p17_4-package.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P17.4 document is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"

    foreach ($marker in @(
        "P17.4",
        "AuthorizationPending",
        "AuthorizationRejected",
        "AuthorizationGrantedForPlanning",
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
        "Credential Responsibility",
        "query_cloud_data_readonly",
        "does not execute a real Pilot"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P17.4 material is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P17.4 material"
    "P17.4 scope and authorization decision package markers passed."
}

$windowFreeze = New-WindowFreeze
$credentialResponsibility = New-CredentialResponsibility

$results += Invoke-Step -Name "P17.4 Authorization Decision Matrix Check" -Script {
    $windowComplete = Test-WindowFreezeComplete -WindowFreeze $windowFreeze
    $credentialComplete = Test-CredentialResponsibilityComplete -CredentialResponsibility $credentialResponsibility

    $pending = Get-GoNoGoDecision -Decision "AuthorizationPending" -WindowFreezeComplete $windowComplete -CredentialResponsibilityComplete $credentialComplete -HasAcceptanceFailures $false
    $rejected = Get-GoNoGoDecision -Decision "AuthorizationRejected" -WindowFreezeComplete $windowComplete -CredentialResponsibilityComplete $credentialComplete -HasAcceptanceFailures $false
    $granted = Get-GoNoGoDecision -Decision "AuthorizationGrantedForPlanning" -WindowFreezeComplete $windowComplete -CredentialResponsibilityComplete $credentialComplete -HasAcceptanceFailures $false
    $windowMissing = Get-GoNoGoDecision -Decision "AuthorizationGrantedForPlanning" -WindowFreezeComplete $false -CredentialResponsibilityComplete $credentialComplete -HasAcceptanceFailures $false
    $credentialMissing = Get-GoNoGoDecision -Decision "AuthorizationGrantedForPlanning" -WindowFreezeComplete $windowComplete -CredentialResponsibilityComplete $false -HasAcceptanceFailures $false
    $acceptanceFailure = Get-GoNoGoDecision -Decision "AuthorizationGrantedForPlanning" -WindowFreezeComplete $windowComplete -CredentialResponsibilityComplete $credentialComplete -HasAcceptanceFailures $true

    if ($pending -ne "AuthorizationPending") {
        throw "Pending decision mapped to unexpected result: $pending"
    }
    if ($rejected -ne "AuthorizationRejected") {
        throw "Rejected decision mapped to unexpected result: $rejected"
    }
    if ($granted -ne "AuthorizationGrantedForPlanning") {
        throw "Planning authorization mapped to unexpected result: $granted"
    }
    if ($windowMissing -ne "WindowFreezeIncomplete") {
        throw "Missing window freeze mapped to unexpected result: $windowMissing"
    }
    if ($credentialMissing -ne "CredentialResponsibilityIncomplete") {
        throw "Missing credential responsibility mapped to unexpected result: $credentialMissing"
    }
    if ($acceptanceFailure -ne "BlockedByAcceptanceFailure") {
        throw "Acceptance failure mapped to unexpected result: $acceptanceFailure"
    }

    "Authorization decision matrix passed: default stays AuthorizationPending and never executes a real Pilot."
}

$results += Invoke-Step -Name "P17.4 Pilot Window Freeze Completeness Check" -Script {
    if (-not (Test-WindowFreezeComplete -WindowFreeze $windowFreeze)) {
        throw "Pilot Window freeze is incomplete."
    }

    "Pilot Window freeze includes named draft, 5-10 user scope, approval chain, rollback owner, emergency stop owner, endpoint allowlist, last-7-days boundary, and maxRows 50."
}

$results += Invoke-Step -Name "P17.4 Credential Responsibility Check" -Script {
    if (-not (Test-CredentialResponsibilityComplete -CredentialResponsibility $credentialResponsibility)) {
        throw "Credential responsibility freeze is incomplete or touched a real credential."
    }

    $json = $credentialResponsibility | ConvertTo-Json -Depth 4
    Assert-NoUnsafeReportContent -Content $json -Name "P17.4 credential responsibility"
    "Credential responsibility records owner, custodian, approver, and placeholder state only; no real credential is touched."
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

$results += Invoke-Step -Name "P17.4 No Execution Claim Check" -Script {
    $combined = @(
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-authorization-decision-p17_4-scope.md" -Raw
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-authorization-decision-p17_4-package.md" -Raw
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
            throw "P17.4 material contains forbidden execution marker: $forbidden"
        }
    }

    "P17.4 material records authorization decision and window freeze only and does not claim execution."
}

$externalReviewState = Get-ExternalReviewState
$prSummary = Get-PrSummary
$localHead = (git rev-parse HEAD 2>$null | Out-String).Trim()
$branch = (git branch --show-current 2>$null | Out-String).Trim()
$failedResults = @($results | Where-Object { -not $_.Succeeded })
$hasFailures = $failedResults.Count -gt 0
$windowFreezeComplete = Test-WindowFreezeComplete -WindowFreeze $windowFreeze
$credentialResponsibilityComplete = Test-CredentialResponsibilityComplete -CredentialResponsibility $credentialResponsibility
$goNoGo = Get-GoNoGoDecision -Decision $AuthorizationDecision -WindowFreezeComplete $windowFreezeComplete -CredentialResponsibilityComplete $credentialResponsibilityComplete -HasAcceptanceFailures $hasFailures
$executionPermission = if ($goNoGo -eq "AuthorizationGrantedForPlanning") { "planning-only" } else { "not granted" }

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Limited Pilot Authorization Decision P17.4 Acceptance")
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
$reportLines.Add("- AuthorizationDecision: $AuthorizationDecision")
$reportLines.Add("- GoNoGo: $goNoGo")
$reportLines.Add("- ExecutionPermission: $executionPermission")
$reportLines.Add("- Boundary: P17.4 records authorization decision and freezes execution planning material only; it does not execute a real Pilot and is not GA")
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
$reportLines.Add("## Authorization Decision")
$reportLines.Add("")
$reportLines.Add("- Decision: $AuthorizationDecision")
$reportLines.Add("- Go/No-Go: $goNoGo")
$reportLines.Add("- Execution permission: $executionPermission")
$reportLines.Add("- Review evidence: 5.5 Pro $externalReviewState")
$reportLines.Add("")
$reportLines.Add("## Pilot Window Freeze")
$reportLines.Add("")
$reportLines.Add("- Pilot Window: $($windowFreeze.PilotWindowName)")
$reportLines.Add("- StartAt: $($windowFreeze.StartAt)")
$reportLines.Add("- EndAt: $($windowFreeze.EndAt)")
$reportLines.Add("- Owner: $($windowFreeze.Owner)")
$reportLines.Add("- Approver: $($windowFreeze.Approver)")
$reportLines.Add("- Rollback owner: $($windowFreeze.RollbackOwner)")
$reportLines.Add("- Emergency stop owner: $($windowFreeze.EmergencyStopOwner)")
$reportLines.Add("- Pilot user scope: $($windowFreeze.PilotUserCountTarget)")
$reportLines.Add("- Endpoint allowlist: $($windowFreeze.EndpointAllowlist -join ', ')")
$reportLines.Add("- Time range: $($windowFreeze.TimeRange)")
$reportLines.Add("- maxRows: $($windowFreeze.MaxRows)")
$reportLines.Add("- Tool Approval required: $($windowFreeze.RequiresToolApproval)")
$reportLines.Add("- Final Approval required: $($windowFreeze.RequiresFinalApproval)")
$reportLines.Add("")
$reportLines.Add("## Credential Responsibility")
$reportLines.Add("")
$reportLines.Add("- Configuration owner: $($credentialResponsibility.ConfigurationOwner)")
$reportLines.Add("- Custodian: $($credentialResponsibility.Custodian)")
$reportLines.Add("- Approver: $($credentialResponsibility.Approver)")
$reportLines.Add("- Credential status placeholder: $($credentialResponsibility.CredentialStatus)")
$reportLines.Add("- Real credential read: $($credentialResponsibility.RealCredentialRead)")
$reportLines.Add("- Real credential written: $($credentialResponsibility.RealCredentialWritten)")
$reportLines.Add("- Real credential displayed: $($credentialResponsibility.RealCredentialDisplayed)")
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
$reportLines.Add("- P17.4 does not execute a real Pilot.")
$reportLines.Add("- Default output is AuthorizationPending unless explicit user authorization is supplied.")
$reportLines.Add("- Planning-only authorization does not grant real endpoint calls.")
$reportLines.Add("- Real endpoint/token use remains outside P17.4 and requires a future explicit execution stage.")
$reportLines.Add("- Future execution still requires approved Pilot Window, approved chain, rollback, emergency stop, and approved runtime credential configuration.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Limited Pilot Authorization Decision P17.4 acceptance report written to: $ReportPath"

if ($hasFailures) {
    exit 1
}
