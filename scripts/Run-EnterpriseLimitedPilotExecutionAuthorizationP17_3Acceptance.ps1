[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-limited-pilot-execution-authorization-p17_3-latest.md",
    [ValidateSet("", "ReviewPending", "BlockedByReview", "NoBlocker")]
    [string]$ReviewResultOverride = "",
    [switch]$ExecutionAuthorizationGranted,
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

function New-AuthorizationRequest {
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
        ExecutionAuthorizationGranted = [bool]$ExecutionAuthorizationGranted
    }
}

function New-CredentialReadiness {
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

function Get-GoNoGoDecision {
    param(
        [bool]$DryRunComplete,
        [bool]$CredentialPlanComplete,
        [bool]$AuthorizationGranted,
        [bool]$HasAcceptanceFailures
    )

    if ($HasAcceptanceFailures) {
        return "BlockedByAcceptanceFailure"
    }

    if (-not $DryRunComplete) {
        return "BlockedByDryRunFailure"
    }

    if (-not $CredentialPlanComplete) {
        return "MissingCredentialPlan"
    }

    if (-not $AuthorizationGranted) {
        return "MissingAuthorization"
    }

    return "ReadyForExplicitExecutionApproval"
}

$results = @()

if (-not $SkipScopeGuard) {
    $results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
    }
}

$results += Invoke-Step -Name "P17.2 Dry-Run Inheritance Check" -Script {
    $p172Report = ".\docs\enterprise-limited-pilot-dry-run-p17_2-latest.md"
    $p172Scope = ".\docs\enterprise-limited-pilot-dry-run-p17_2-scope.md"
    $p172Evidence = ".\docs\enterprise-limited-pilot-dry-run-p17_2-evidence.md"
    foreach ($path in @($p172Report, $p172Scope, $p172Evidence)) {
        if (-not (Test-Path $path)) {
            throw "P17.2 evidence is missing: $path"
        }
    }

    $combined = @(
        Get-Content -LiteralPath $p172Report -Raw
        Get-Content -LiteralPath $p172Scope -Raw
        Get-Content -LiteralPath $p172Evidence -Raw
    ) -join "`n"

    foreach ($marker in @(
        "P17.2",
        "DryRunEvidenceReady",
        "FixedTemplate",
        "ControlledGoal",
        "BlockedByPolicy",
        "EmergencyStopActive",
        "HashOnlyLedgerPreserved",
        "query_cloud_data_readonly",
        "does not execute a real Pilot"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P17.2 evidence is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P17.2 evidence"
    "P17.2 dry-run evidence is present and remains non-executing."
}

$results += Invoke-Step -Name "P17.3 Scope And Authorization Package Check" -Script {
    $scope = ".\docs\enterprise-limited-pilot-execution-authorization-p17_3-scope.md"
    $package = ".\docs\enterprise-limited-pilot-execution-authorization-p17_3-package.md"
    foreach ($path in @($scope, $package)) {
        if (-not (Test-Path $path)) {
            throw "P17.3 document is missing: $path"
        }
    }

    $combined = @(
        Get-Content -LiteralPath $scope -Raw
        Get-Content -LiteralPath $package -Raw
    ) -join "`n"

    foreach ($marker in @(
        "P17.3",
        "Execution Authorization Request",
        "Credential Readiness Preflight",
        "MissingAuthorization",
        "MissingCredentialPlan",
        "BlockedByDryRunFailure",
        "ReadyForExplicitExecutionApproval",
        "5-10",
        "devices",
        "capacity_summary",
        "device_logs",
        "pass_station_records",
        "last 7 days",
        "maxRows=50",
        "Tool Approval",
        "Final Approval",
        "query_cloud_data_readonly"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P17.3 material is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P17.3 material"
    "P17.3 scope and authorization package markers passed."
}

$authorizationRequest = New-AuthorizationRequest
$credentialReadiness = New-CredentialReadiness

$results += Invoke-Step -Name "P17.3 Authorization Request Completeness Check" -Script {
    foreach ($field in @("PilotWindowName", "StartAt", "EndAt", "Owner", "Approver", "RollbackOwner", "EmergencyStopOwner", "PilotUserCountTarget", "TimeRange")) {
        if ([string]::IsNullOrWhiteSpace([string]$authorizationRequest.$field)) {
            throw "Authorization request is missing field: $field"
        }
    }

    foreach ($endpoint in @("devices", "capacity_summary", "device_logs", "pass_station_records")) {
        if ($authorizationRequest.EndpointAllowlist -notcontains $endpoint) {
            throw "Authorization request is missing endpoint allowlist item: $endpoint"
        }
    }

    if ($authorizationRequest.MaxRows -ne 50) {
        throw "Authorization request has unexpected maxRows: $($authorizationRequest.MaxRows)"
    }
    if (-not $authorizationRequest.RequiresToolApproval -or -not $authorizationRequest.RequiresFinalApproval) {
        throw "Authorization request must require Tool Approval and Final Approval."
    }

    "Authorization request has Pilot Window, 5-10 users, allowlist endpoints, last-7-days boundary, maxRows 50, approval chain, rollback owner, and emergency stop owner."
}

$results += Invoke-Step -Name "P17.3 Credential Readiness Preflight Check" -Script {
    foreach ($field in @("ConfigurationOwner", "Custodian", "Approver", "CredentialStatus")) {
        if ([string]::IsNullOrWhiteSpace([string]$credentialReadiness.$field)) {
            throw "Credential readiness is missing field: $field"
        }
    }

    if ($credentialReadiness.RealCredentialRead -or $credentialReadiness.RealCredentialWritten -or $credentialReadiness.RealCredentialDisplayed) {
        throw "Credential readiness touched or exposed a real credential."
    }

    $json = $credentialReadiness | ConvertTo-Json -Depth 4
    Assert-NoUnsafeReportContent -Content $json -Name "P17.3 credential readiness"
    "Credential readiness records responsibility and placeholder status only; no real credential is read, written, or displayed."
}

$results += Invoke-Step -Name "P17.3 Go No-Go Matrix Check" -Script {
    $blockedDryRun = Get-GoNoGoDecision -DryRunComplete $false -CredentialPlanComplete $true -AuthorizationGranted $true -HasAcceptanceFailures $false
    $missingCredential = Get-GoNoGoDecision -DryRunComplete $true -CredentialPlanComplete $false -AuthorizationGranted $true -HasAcceptanceFailures $false
    $missingAuthorization = Get-GoNoGoDecision -DryRunComplete $true -CredentialPlanComplete $true -AuthorizationGranted $false -HasAcceptanceFailures $false
    $readyForApproval = Get-GoNoGoDecision -DryRunComplete $true -CredentialPlanComplete $true -AuthorizationGranted $true -HasAcceptanceFailures $false
    $acceptanceFailure = Get-GoNoGoDecision -DryRunComplete $true -CredentialPlanComplete $true -AuthorizationGranted $true -HasAcceptanceFailures $true

    if ($blockedDryRun -ne "BlockedByDryRunFailure") {
        throw "Dry-run failure mapped to unexpected decision: $blockedDryRun"
    }
    if ($missingCredential -ne "MissingCredentialPlan") {
        throw "Missing credential plan mapped to unexpected decision: $missingCredential"
    }
    if ($missingAuthorization -ne "MissingAuthorization") {
        throw "Missing authorization mapped to unexpected decision: $missingAuthorization"
    }
    if ($readyForApproval -ne "ReadyForExplicitExecutionApproval") {
        throw "Complete request mapped to unexpected decision: $readyForApproval"
    }
    if ($acceptanceFailure -ne "BlockedByAcceptanceFailure") {
        throw "Acceptance failure mapped to unexpected decision: $acceptanceFailure"
    }

    "Go/No-Go matrix passed: missing explicit execution approval defaults to MissingAuthorization."
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

$results += Invoke-Step -Name "P17.3 No Execution Claim Check" -Script {
    $combined = @(
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-execution-authorization-p17_3-scope.md" -Raw
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-execution-authorization-p17_3-package.md" -Raw
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
            throw "P17.3 material contains forbidden execution marker: $forbidden"
        }
    }

    "P17.3 material records authorization request only and does not claim execution."
}

$externalReviewState = Get-ExternalReviewState
$prSummary = Get-PrSummary
$localHead = (git rev-parse HEAD 2>$null | Out-String).Trim()
$branch = (git branch --show-current 2>$null | Out-String).Trim()
$failedResults = @($results | Where-Object { -not $_.Succeeded })
$hasFailures = $failedResults.Count -gt 0
$credentialPlanComplete = -not ($credentialReadiness.RealCredentialRead -or $credentialReadiness.RealCredentialWritten -or $credentialReadiness.RealCredentialDisplayed)
$goNoGo = Get-GoNoGoDecision -DryRunComplete $true -CredentialPlanComplete $credentialPlanComplete -AuthorizationGranted ([bool]$ExecutionAuthorizationGranted) -HasAcceptanceFailures $hasFailures

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Limited Pilot Execution Authorization P17.3 Acceptance")
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
$reportLines.Add("- ExplicitExecutionAuthorization: $([bool]$ExecutionAuthorizationGranted)")
$reportLines.Add("- GoNoGo: $goNoGo")
$reportLines.Add("- ExecutionPermission: not granted")
$reportLines.Add("- Boundary: P17.3 prepares an execution authorization request and credential readiness preflight only; it does not execute a real Pilot and is not GA")
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
$reportLines.Add("## Authorization Request")
$reportLines.Add("")
$reportLines.Add("- Pilot Window: $($authorizationRequest.PilotWindowName)")
$reportLines.Add("- StartAt: $($authorizationRequest.StartAt)")
$reportLines.Add("- EndAt: $($authorizationRequest.EndAt)")
$reportLines.Add("- Owner: $($authorizationRequest.Owner)")
$reportLines.Add("- Approver: $($authorizationRequest.Approver)")
$reportLines.Add("- Rollback owner: $($authorizationRequest.RollbackOwner)")
$reportLines.Add("- Emergency stop owner: $($authorizationRequest.EmergencyStopOwner)")
$reportLines.Add("- Pilot user scope: $($authorizationRequest.PilotUserCountTarget)")
$reportLines.Add("- Endpoint allowlist: $($authorizationRequest.EndpointAllowlist -join ', ')")
$reportLines.Add("- Time range: $($authorizationRequest.TimeRange)")
$reportLines.Add("- maxRows: $($authorizationRequest.MaxRows)")
$reportLines.Add("- Tool Approval required: $($authorizationRequest.RequiresToolApproval)")
$reportLines.Add("- Final Approval required: $($authorizationRequest.RequiresFinalApproval)")
$reportLines.Add("")
$reportLines.Add("## Credential Readiness Preflight")
$reportLines.Add("")
$reportLines.Add("- Configuration owner: $($credentialReadiness.ConfigurationOwner)")
$reportLines.Add("- Custodian: $($credentialReadiness.Custodian)")
$reportLines.Add("- Approver: $($credentialReadiness.Approver)")
$reportLines.Add("- Credential status placeholder: $($credentialReadiness.CredentialStatus)")
$reportLines.Add("- Real credential read: $($credentialReadiness.RealCredentialRead)")
$reportLines.Add("- Real credential written: $($credentialReadiness.RealCredentialWritten)")
$reportLines.Add("- Real credential displayed: $($credentialReadiness.RealCredentialDisplayed)")
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
$reportLines.Add("- P17.3 does not execute a real Pilot.")
$reportLines.Add("- Default output is MissingAuthorization because explicit user execution approval is not supplied.")
$reportLines.Add("- Real endpoint/token use remains outside P17.3 and requires a future explicit approval.")
$reportLines.Add("- Future execution still requires approved Pilot Window, approved chain, rollback, emergency stop, and approved runtime credential configuration.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Limited Pilot Execution Authorization P17.3 acceptance report written to: $ReportPath"

if ($hasFailures) {
    exit 1
}
