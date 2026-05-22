[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-limited-pilot-signed-approval-p17_5-latest.md",
    [ValidateSet("MissingSignedExecutionApproval", "SignedApprovalIncomplete", "ReadyForManualPilotExecutionStep")]
    [string]$SignedApprovalState = "MissingSignedExecutionApproval",
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

function New-SignedApprovalPackage {
    [pscustomobject]@{
        PilotWindowName = "limited production readonly Pilot"
        StartAt = "ToBeSigned"
        EndAt = "ToBeSigned"
        Owner = "ToBeAssigned"
        Approver = "ToBeAssigned"
        Executor = "ToBeAssigned"
        RollbackOwner = "ToBeAssigned"
        EmergencyStopOwner = "ToBeAssigned"
        PilotUserCountTarget = "5-10"
        EndpointAllowlist = @("devices", "capacity_summary", "device_logs", "pass_station_records")
        TimeRange = "last 7 days"
        MaxRows = 50
        RequiresToolApproval = $true
        RequiresFinalApproval = $true
        OperationsLedgerPolicy = "hash-only"
    }
}

function New-CredentialWindow {
    [pscustomobject]@{
        ConfigurationOwner = "ToBeAssigned"
        Custodian = "ToBeAssigned"
        Approver = "ToBeAssigned"
        ConfigurationWindow = "ToBeSigned"
        RollbackRequirement = "Disable window and revoke future runtime configuration path"
        CredentialStatus = "NotConfigured"
        RealCredentialRead = $false
        RealCredentialWritten = $false
        RealCredentialDisplayed = $false
    }
}

function Test-SignedApprovalComplete {
    param([Parameter(Mandatory = $true)]$Package)

    foreach ($field in @("PilotWindowName", "StartAt", "EndAt", "Owner", "Approver", "Executor", "RollbackOwner", "EmergencyStopOwner", "PilotUserCountTarget", "TimeRange", "OperationsLedgerPolicy")) {
        if ([string]::IsNullOrWhiteSpace([string]$Package.$field)) {
            return $false
        }
    }

    foreach ($endpoint in @("devices", "capacity_summary", "device_logs", "pass_station_records")) {
        if ($Package.EndpointAllowlist -notcontains $endpoint) {
            return $false
        }
    }

    return $Package.MaxRows -eq 50 -and $Package.RequiresToolApproval -and $Package.RequiresFinalApproval
}

function Test-CredentialWindowComplete {
    param([Parameter(Mandatory = $true)]$CredentialWindow)

    foreach ($field in @("ConfigurationOwner", "Custodian", "Approver", "ConfigurationWindow", "RollbackRequirement", "CredentialStatus")) {
        if ([string]::IsNullOrWhiteSpace([string]$CredentialWindow.$field)) {
            return $false
        }
    }

    return -not ($CredentialWindow.RealCredentialRead -or $CredentialWindow.RealCredentialWritten -or $CredentialWindow.RealCredentialDisplayed)
}

function Get-GoNoGoDecision {
    param(
        [string]$State,
        [bool]$SignedApprovalComplete,
        [bool]$CredentialWindowComplete,
        [bool]$HasAcceptanceFailures
    )

    if ($HasAcceptanceFailures) {
        return "BlockedByAcceptanceFailure"
    }

    if ($State -eq "MissingSignedExecutionApproval") {
        return "MissingSignedExecutionApproval"
    }

    if (-not $SignedApprovalComplete) {
        return "SignedApprovalIncomplete"
    }

    if (-not $CredentialWindowComplete) {
        return "CredentialWindowIncomplete"
    }

    if ($State -eq "SignedApprovalIncomplete") {
        return "SignedApprovalIncomplete"
    }

    return "ReadyForManualPilotExecutionStep"
}

$results = @()

if (-not $SkipScopeGuard) {
    $results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
    }
}

$results += Invoke-Step -Name "P17.4 Authorization Decision Inheritance Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-authorization-decision-p17_4-scope.md",
        ".\docs\enterprise-limited-pilot-authorization-decision-p17_4-package.md",
        ".\docs\enterprise-limited-pilot-authorization-decision-p17_4-latest.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P17.4 evidence is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"

    foreach ($marker in @(
        "P17.4",
        "AuthorizationPending",
        "ExecutionPermission: not granted",
        "Pilot Window",
        "Credential Responsibility",
        "does not execute a real Pilot",
        "query_cloud_data_readonly"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P17.4 inheritance is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P17.4 inheritance"
    "P17.4 authorization decision and window freeze evidence are present."
}

$results += Invoke-Step -Name "P17.5 Scope And Package Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-signed-approval-p17_5-scope.md",
        ".\docs\enterprise-limited-pilot-signed-approval-p17_5-package.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P17.5 document is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"

    foreach ($marker in @(
        "P17.5",
        "MissingSignedExecutionApproval",
        "SignedApprovalIncomplete",
        "CredentialWindowIncomplete",
        "ReadyForManualPilotExecutionStep",
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
        "hash-only",
        "Credential Configuration Window",
        "query_cloud_data_readonly",
        "does not execute a real Pilot"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P17.5 material is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P17.5 material"
    "P17.5 scope and signed approval package markers passed."
}

$signedApprovalPackage = New-SignedApprovalPackage
$credentialWindow = New-CredentialWindow

$results += Invoke-Step -Name "P17.5 Signed Approval Matrix Check" -Script {
    $signedComplete = Test-SignedApprovalComplete -Package $signedApprovalPackage
    $credentialComplete = Test-CredentialWindowComplete -CredentialWindow $credentialWindow

    $missing = Get-GoNoGoDecision -State "MissingSignedExecutionApproval" -SignedApprovalComplete $signedComplete -CredentialWindowComplete $credentialComplete -HasAcceptanceFailures $false
    $incomplete = Get-GoNoGoDecision -State "SignedApprovalIncomplete" -SignedApprovalComplete $signedComplete -CredentialWindowComplete $credentialComplete -HasAcceptanceFailures $false
    $ready = Get-GoNoGoDecision -State "ReadyForManualPilotExecutionStep" -SignedApprovalComplete $signedComplete -CredentialWindowComplete $credentialComplete -HasAcceptanceFailures $false
    $signedMissing = Get-GoNoGoDecision -State "ReadyForManualPilotExecutionStep" -SignedApprovalComplete $false -CredentialWindowComplete $credentialComplete -HasAcceptanceFailures $false
    $credentialMissing = Get-GoNoGoDecision -State "ReadyForManualPilotExecutionStep" -SignedApprovalComplete $signedComplete -CredentialWindowComplete $false -HasAcceptanceFailures $false
    $acceptanceFailure = Get-GoNoGoDecision -State "ReadyForManualPilotExecutionStep" -SignedApprovalComplete $signedComplete -CredentialWindowComplete $credentialComplete -HasAcceptanceFailures $true

    if ($missing -ne "MissingSignedExecutionApproval") {
        throw "Missing signed approval mapped to unexpected result: $missing"
    }
    if ($incomplete -ne "SignedApprovalIncomplete") {
        throw "Incomplete signed approval mapped to unexpected result: $incomplete"
    }
    if ($ready -ne "ReadyForManualPilotExecutionStep") {
        throw "Ready signed approval mapped to unexpected result: $ready"
    }
    if ($signedMissing -ne "SignedApprovalIncomplete") {
        throw "Missing signed material mapped to unexpected result: $signedMissing"
    }
    if ($credentialMissing -ne "CredentialWindowIncomplete") {
        throw "Missing credential window mapped to unexpected result: $credentialMissing"
    }
    if ($acceptanceFailure -ne "BlockedByAcceptanceFailure") {
        throw "Acceptance failure mapped to unexpected result: $acceptanceFailure"
    }

    "Signed approval matrix passed: default stays MissingSignedExecutionApproval and never executes a real Pilot."
}

$results += Invoke-Step -Name "P17.5 Signed Approval Package Completeness Check" -Script {
    if (-not (Test-SignedApprovalComplete -Package $signedApprovalPackage)) {
        throw "Signed approval package is incomplete."
    }

    "Signed approval package includes Pilot Window, approver, executor, rollback owner, emergency stop owner, 5-10 user scope, endpoint allowlist, last-7-days boundary, maxRows 50, Final Approval, and hash-only ledger policy."
}

$results += Invoke-Step -Name "P17.5 Credential Window Check" -Script {
    if (-not (Test-CredentialWindowComplete -CredentialWindow $credentialWindow)) {
        throw "Credential window is incomplete or touched a real credential."
    }

    $json = $credentialWindow | ConvertTo-Json -Depth 4
    Assert-NoUnsafeReportContent -Content $json -Name "P17.5 credential window"
    "Credential window records owner, custodian, approver, configuration window, rollback requirement, and placeholder state only; no real credential is touched."
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

$results += Invoke-Step -Name "P17.5 No Execution Claim Check" -Script {
    $combined = @(
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-signed-approval-p17_5-scope.md" -Raw
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-signed-approval-p17_5-package.md" -Raw
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
            throw "P17.5 material contains forbidden execution marker: $forbidden"
        }
    }

    "P17.5 material records signed approval package only and does not claim execution."
}

$externalReviewState = Get-ExternalReviewState
$prSummary = Get-PrSummary
$localHead = (git rev-parse HEAD 2>$null | Out-String).Trim()
$branch = (git branch --show-current 2>$null | Out-String).Trim()
$failedResults = @($results | Where-Object { -not $_.Succeeded })
$hasFailures = $failedResults.Count -gt 0
$signedApprovalComplete = Test-SignedApprovalComplete -Package $signedApprovalPackage
$credentialWindowComplete = Test-CredentialWindowComplete -CredentialWindow $credentialWindow
$goNoGo = Get-GoNoGoDecision -State $SignedApprovalState -SignedApprovalComplete $signedApprovalComplete -CredentialWindowComplete $credentialWindowComplete -HasAcceptanceFailures $hasFailures
$executionPermission = if ($goNoGo -eq "ReadyForManualPilotExecutionStep") { "manual-step-planning-only" } else { "not granted" }

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Limited Pilot Signed Approval P17.5 Acceptance")
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
$reportLines.Add("- SignedApprovalState: $SignedApprovalState")
$reportLines.Add("- GoNoGo: $goNoGo")
$reportLines.Add("- ExecutionPermission: $executionPermission")
$reportLines.Add("- Boundary: P17.5 prepares a signed execution approval package only; it does not execute a real Pilot and is not GA")
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
$reportLines.Add("## Signed Approval")
$reportLines.Add("")
$reportLines.Add("- State: $SignedApprovalState")
$reportLines.Add("- Go/No-Go: $goNoGo")
$reportLines.Add("- Execution permission: $executionPermission")
$reportLines.Add("- Review evidence: 5.5 Pro $externalReviewState")
$reportLines.Add("")
$reportLines.Add("## Pilot Window Approval Template")
$reportLines.Add("")
$reportLines.Add("- Pilot Window: $($signedApprovalPackage.PilotWindowName)")
$reportLines.Add("- StartAt: $($signedApprovalPackage.StartAt)")
$reportLines.Add("- EndAt: $($signedApprovalPackage.EndAt)")
$reportLines.Add("- Owner: $($signedApprovalPackage.Owner)")
$reportLines.Add("- Approver: $($signedApprovalPackage.Approver)")
$reportLines.Add("- Executor: $($signedApprovalPackage.Executor)")
$reportLines.Add("- Rollback owner: $($signedApprovalPackage.RollbackOwner)")
$reportLines.Add("- Emergency stop owner: $($signedApprovalPackage.EmergencyStopOwner)")
$reportLines.Add("- Pilot user scope: $($signedApprovalPackage.PilotUserCountTarget)")
$reportLines.Add("- Endpoint allowlist: $($signedApprovalPackage.EndpointAllowlist -join ', ')")
$reportLines.Add("- Time range: $($signedApprovalPackage.TimeRange)")
$reportLines.Add("- maxRows: $($signedApprovalPackage.MaxRows)")
$reportLines.Add("- Tool Approval required: $($signedApprovalPackage.RequiresToolApproval)")
$reportLines.Add("- Final Approval required: $($signedApprovalPackage.RequiresFinalApproval)")
$reportLines.Add("- Operations ledger policy: $($signedApprovalPackage.OperationsLedgerPolicy)")
$reportLines.Add("")
$reportLines.Add("## Credential Configuration Window")
$reportLines.Add("")
$reportLines.Add("- Configuration owner: $($credentialWindow.ConfigurationOwner)")
$reportLines.Add("- Custodian: $($credentialWindow.Custodian)")
$reportLines.Add("- Approver: $($credentialWindow.Approver)")
$reportLines.Add("- Configuration window: $($credentialWindow.ConfigurationWindow)")
$reportLines.Add("- Rollback requirement: $($credentialWindow.RollbackRequirement)")
$reportLines.Add("- Credential status placeholder: $($credentialWindow.CredentialStatus)")
$reportLines.Add("- Real credential read: $($credentialWindow.RealCredentialRead)")
$reportLines.Add("- Real credential written: $($credentialWindow.RealCredentialWritten)")
$reportLines.Add("- Real credential displayed: $($credentialWindow.RealCredentialDisplayed)")
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
$reportLines.Add("- P17.5 does not execute a real Pilot.")
$reportLines.Add("- Default output is MissingSignedExecutionApproval unless a complete signed package is supplied.")
$reportLines.Add("- ReadyForManualPilotExecutionStep is still not a real endpoint call.")
$reportLines.Add("- Real endpoint/token use remains outside P17.5 and requires a future explicit execution stage.")
$reportLines.Add("- Future execution still requires separate user instruction, approved Pilot Window, approved credential configuration, executor confirmation, rollback window, emergency stop online verification, and post-execution audit archival.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Limited Pilot Signed Approval P17.5 acceptance report written to: $ReportPath"

if ($hasFailures) {
    exit 1
}
