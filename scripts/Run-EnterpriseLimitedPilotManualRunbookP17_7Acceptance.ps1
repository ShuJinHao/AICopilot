[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-limited-pilot-manual-runbook-p17_7-latest.md",
    [ValidateSet("BlockedNoSignedApproval", "BlockedRunbookIncomplete", "BlockedCredentialPreflightIncomplete", "ReadyForExplicitManualExecutionRequest")]
    [string]$RunbookState = "BlockedNoSignedApproval",
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
            Head         = $json.headRefOid
            Url          = $json.url
            CiStatus     = if ($check) { $check.status } else { "MISSING" }
            CiConclusion = if ($check) { $check.conclusion } else { "MISSING" }
            CiDetails    = if ($check) { $check.detailsUrl } else { "" }
        }
    } catch {
        return [pscustomobject]@{
            Head         = "unknown"
            Url          = "unknown"
            CiStatus     = "ERROR"
            CiConclusion = "ERROR"
            CiDetails    = ""
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

function New-ManualExecutionRunbook {
    [pscustomobject]@{
        PilotWindowName        = "limited production readonly Pilot"
        SignedApprovalReceived = $false
        PilotWindowApproved    = $false
        Owner                  = "ToBeSigned"
        Approver               = "ToBeSigned"
        Executor               = "ToBeSigned"
        RollbackOwner          = "ToBeSigned"
        EmergencyStopOwner     = "ToBeSigned"
        PilotUserCountTarget   = "5-10"
        EndpointAllowlist      = @("devices", "capacity_summary", "device_logs", "pass_station_records")
        TimeRange              = "last 7 days"
        MaxRows                = 50
        RequiresToolApproval   = $true
        RequiresFinalApproval  = $true
        OperationsLedgerPolicy = "hash-only"
        FutureExecutionRequiresSeparateUserInstruction = $true
    }
}

function New-OfflinePreflight {
    [pscustomobject]@{
        MaterialCompletenessCheck       = "Required"
        ApprovalPlaceholderCheck        = "Required"
        PilotWindowStatusCheck          = "RequiredBeforeFutureExecution"
        EmergencyStopOnlineVerification = "RequiredBeforeFutureExecution"
        RollbackChecklist               = "Disable future execution window and revoke future runtime configuration path"
        ConfigurationOwner              = "ToBeSigned"
        Custodian                       = "ToBeSigned"
        Approver                        = "ToBeSigned"
        ConfigurationWindow             = "ToBeSigned"
        CredentialStatus                = "NotConfigured"
        RealCredentialRead              = $false
        RealCredentialWritten           = $false
        RealCredentialDisplayed         = $false
        RealEndpointCalled              = $false
        RealProductionArtifactGenerated = $false
    }
}

function Test-RunbookComplete {
    param([Parameter(Mandatory = $true)]$Runbook)

    foreach ($field in @("PilotWindowName", "Owner", "Approver", "Executor", "RollbackOwner", "EmergencyStopOwner", "PilotUserCountTarget", "TimeRange", "OperationsLedgerPolicy")) {
        if ([string]::IsNullOrWhiteSpace([string]$Runbook.$field)) {
            return $false
        }
    }

    foreach ($endpoint in @("devices", "capacity_summary", "device_logs", "pass_station_records")) {
        if ($Runbook.EndpointAllowlist -notcontains $endpoint) {
            return $false
        }
    }

    return $Runbook.MaxRows -eq 50 `
        -and $Runbook.RequiresToolApproval `
        -and $Runbook.RequiresFinalApproval `
        -and $Runbook.FutureExecutionRequiresSeparateUserInstruction
}

function Test-OfflinePreflightComplete {
    param([Parameter(Mandatory = $true)]$Preflight)

    foreach ($field in @("MaterialCompletenessCheck", "ApprovalPlaceholderCheck", "PilotWindowStatusCheck", "EmergencyStopOnlineVerification", "RollbackChecklist", "ConfigurationOwner", "Custodian", "Approver", "ConfigurationWindow", "CredentialStatus")) {
        if ([string]::IsNullOrWhiteSpace([string]$Preflight.$field)) {
            return $false
        }
    }

    return -not (
        $Preflight.RealCredentialRead `
        -or $Preflight.RealCredentialWritten `
        -or $Preflight.RealCredentialDisplayed `
        -or $Preflight.RealEndpointCalled `
        -or $Preflight.RealProductionArtifactGenerated
    )
}

function Get-GoNoGoDecision {
    param(
        [string]$State,
        [bool]$RunbookComplete,
        [bool]$PreflightComplete,
        [bool]$HasAcceptanceFailures
    )

    if ($HasAcceptanceFailures) {
        return "BlockedByAcceptanceFailure"
    }

    if ($State -eq "BlockedNoSignedApproval") {
        return "BlockedNoSignedApproval"
    }

    if (-not $RunbookComplete) {
        return "BlockedRunbookIncomplete"
    }

    if (-not $PreflightComplete) {
        return "BlockedCredentialPreflightIncomplete"
    }

    if ($State -eq "BlockedRunbookIncomplete") {
        return "BlockedRunbookIncomplete"
    }

    if ($State -eq "BlockedCredentialPreflightIncomplete") {
        return "BlockedCredentialPreflightIncomplete"
    }

    return "ReadyForExplicitManualExecutionRequest"
}

$results = @()

if (-not $SkipScopeGuard) {
    $results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
    }
}

$results += Invoke-Step -Name "P17.6 Signed Approval Intake Inheritance Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-signed-approval-intake-p17_6-scope.md",
        ".\docs\enterprise-limited-pilot-signed-approval-intake-p17_6-package.md",
        ".\docs\enterprise-limited-pilot-signed-approval-intake-p17_6-latest.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P17.6 evidence is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"

    foreach ($marker in @(
        "P17.6",
        "NoSignedApprovalReceived",
        "ExecutionPermission: not granted",
        "ReadyForManualExecutionStepPlanning",
        "query_cloud_data_readonly",
        "does not execute a real Pilot"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P17.6 inheritance is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P17.6 inheritance"
    "P17.6 signed approval intake remains non-executable."
}

$results += Invoke-Step -Name "P17.7 Scope And Runbook Package Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-manual-runbook-p17_7-scope.md",
        ".\docs\enterprise-limited-pilot-manual-runbook-p17_7-package.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P17.7 document is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"

    foreach ($marker in @(
        "P17.7",
        "BlockedNoSignedApproval",
        "BlockedRunbookIncomplete",
        "BlockedCredentialPreflightIncomplete",
        "ReadyForExplicitManualExecutionRequest",
        "Manual Execution Runbook",
        "Offline Preflight",
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
        "query_cloud_data_readonly",
        "does not execute"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P17.7 material is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P17.7 material"
    "P17.7 scope and manual runbook package markers passed."
}

$manualRunbook = New-ManualExecutionRunbook
$offlinePreflight = New-OfflinePreflight

$results += Invoke-Step -Name "P17.7 Go/No-Go Matrix Check" -Script {
    $runbookComplete = Test-RunbookComplete -Runbook $manualRunbook
    $preflightComplete = Test-OfflinePreflightComplete -Preflight $offlinePreflight

    $blockedNoSignedApproval = Get-GoNoGoDecision -State "BlockedNoSignedApproval" -RunbookComplete $runbookComplete -PreflightComplete $preflightComplete -HasAcceptanceFailures $false
    $blockedRunbook = Get-GoNoGoDecision -State "BlockedRunbookIncomplete" -RunbookComplete $runbookComplete -PreflightComplete $preflightComplete -HasAcceptanceFailures $false
    $blockedCredential = Get-GoNoGoDecision -State "BlockedCredentialPreflightIncomplete" -RunbookComplete $runbookComplete -PreflightComplete $preflightComplete -HasAcceptanceFailures $false
    $ready = Get-GoNoGoDecision -State "ReadyForExplicitManualExecutionRequest" -RunbookComplete $runbookComplete -PreflightComplete $preflightComplete -HasAcceptanceFailures $false
    $runbookMissing = Get-GoNoGoDecision -State "ReadyForExplicitManualExecutionRequest" -RunbookComplete $false -PreflightComplete $preflightComplete -HasAcceptanceFailures $false
    $preflightMissing = Get-GoNoGoDecision -State "ReadyForExplicitManualExecutionRequest" -RunbookComplete $runbookComplete -PreflightComplete $false -HasAcceptanceFailures $false
    $acceptanceFailure = Get-GoNoGoDecision -State "ReadyForExplicitManualExecutionRequest" -RunbookComplete $runbookComplete -PreflightComplete $preflightComplete -HasAcceptanceFailures $true

    if ($blockedNoSignedApproval -ne "BlockedNoSignedApproval") {
        throw "No signed approval mapped to unexpected result: $blockedNoSignedApproval"
    }
    if ($blockedRunbook -ne "BlockedRunbookIncomplete") {
        throw "Runbook incomplete state mapped to unexpected result: $blockedRunbook"
    }
    if ($blockedCredential -ne "BlockedCredentialPreflightIncomplete") {
        throw "Credential preflight incomplete state mapped to unexpected result: $blockedCredential"
    }
    if ($ready -ne "ReadyForExplicitManualExecutionRequest") {
        throw "Ready state mapped to unexpected result: $ready"
    }
    if ($runbookMissing -ne "BlockedRunbookIncomplete") {
        throw "Missing runbook mapped to unexpected result: $runbookMissing"
    }
    if ($preflightMissing -ne "BlockedCredentialPreflightIncomplete") {
        throw "Missing credential preflight mapped to unexpected result: $preflightMissing"
    }
    if ($acceptanceFailure -ne "BlockedByAcceptanceFailure") {
        throw "Acceptance failure mapped to unexpected result: $acceptanceFailure"
    }

    "Go/No-Go matrix passed: default stays BlockedNoSignedApproval and readiness only permits a later explicit manual execution request."
}

$results += Invoke-Step -Name "P17.7 Manual Execution Runbook Completeness Check" -Script {
    if (-not (Test-RunbookComplete -Runbook $manualRunbook)) {
        throw "Manual execution runbook is incomplete."
    }

    "Manual runbook includes Pilot Window, approver, executor, rollback owner, emergency stop owner, 5-10 user scope, endpoint allowlist, last-7-days boundary, maxRows 50, Tool Approval, Final Approval, and hash-only ledger policy."
}

$results += Invoke-Step -Name "P17.7 Offline Preflight Check" -Script {
    if (-not (Test-OfflinePreflightComplete -Preflight $offlinePreflight)) {
        throw "Offline preflight is incomplete or touched real execution material."
    }

    $json = $offlinePreflight | ConvertTo-Json -Depth 4
    Assert-NoUnsafeReportContent -Content $json -Name "P17.7 offline preflight"
    "Offline preflight records completeness, approval placeholders, emergency stop verification requirement, rollback checklist, and credential responsibility placeholders only; no real endpoint or credential is touched."
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

$results += Invoke-Step -Name "P17.7 No Execution Claim Check" -Script {
    $combined = @(
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-manual-runbook-p17_7-scope.md" -Raw
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-manual-runbook-p17_7-package.md" -Raw
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
            throw "P17.7 material contains forbidden execution marker: $forbidden"
        }
    }

    "P17.7 material records manual runbook and offline preflight only and does not claim execution."
}

$externalReviewState = Get-ExternalReviewState
$prSummary = Get-PrSummary
$localHead = (git rev-parse HEAD 2>$null | Out-String).Trim()
$branch = (git branch --show-current 2>$null | Out-String).Trim()
$failedResults = @($results | Where-Object { -not $_.Succeeded })
$hasFailures = $failedResults.Count -gt 0
$runbookComplete = Test-RunbookComplete -Runbook $manualRunbook
$preflightComplete = Test-OfflinePreflightComplete -Preflight $offlinePreflight
$goNoGo = Get-GoNoGoDecision -State $RunbookState -RunbookComplete $runbookComplete -PreflightComplete $preflightComplete -HasAcceptanceFailures $hasFailures
$executionPermission = if ($goNoGo -eq "ReadyForExplicitManualExecutionRequest") { "explicit-manual-request-required" } else { "not granted" }

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Limited Pilot Manual Runbook P17.7 Acceptance")
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
$reportLines.Add("- RunbookState: $RunbookState")
$reportLines.Add("- GoNoGo: $goNoGo")
$reportLines.Add("- ExecutionPermission: $executionPermission")
$reportLines.Add("- Boundary: P17.7 freezes a manual execution runbook and offline preflight package only; it does not execute a real Pilot and is not GA")
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
$reportLines.Add("## Manual Runbook Decision")
$reportLines.Add("")
$reportLines.Add("- State: $RunbookState")
$reportLines.Add("- Go/No-Go: $goNoGo")
$reportLines.Add("- Execution permission: $executionPermission")
$reportLines.Add("- Review evidence: 5.5 Pro $externalReviewState")
$reportLines.Add("")
$reportLines.Add("## Manual Execution Runbook")
$reportLines.Add("")
$reportLines.Add("- Pilot Window: $($manualRunbook.PilotWindowName)")
$reportLines.Add("- Signed approval received: $($manualRunbook.SignedApprovalReceived)")
$reportLines.Add("- Pilot Window approved: $($manualRunbook.PilotWindowApproved)")
$reportLines.Add("- Owner: $($manualRunbook.Owner)")
$reportLines.Add("- Approver: $($manualRunbook.Approver)")
$reportLines.Add("- Executor: $($manualRunbook.Executor)")
$reportLines.Add("- Rollback owner: $($manualRunbook.RollbackOwner)")
$reportLines.Add("- Emergency stop owner: $($manualRunbook.EmergencyStopOwner)")
$reportLines.Add("- Pilot user scope: $($manualRunbook.PilotUserCountTarget)")
$reportLines.Add("- Endpoint allowlist: $($manualRunbook.EndpointAllowlist -join ', ')")
$reportLines.Add("- Time range: $($manualRunbook.TimeRange)")
$reportLines.Add("- maxRows: $($manualRunbook.MaxRows)")
$reportLines.Add("- Tool Approval required: $($manualRunbook.RequiresToolApproval)")
$reportLines.Add("- Final Approval required: $($manualRunbook.RequiresFinalApproval)")
$reportLines.Add("- Operations ledger policy: $($manualRunbook.OperationsLedgerPolicy)")
$reportLines.Add("- Future execution requires separate user instruction: $($manualRunbook.FutureExecutionRequiresSeparateUserInstruction)")
$reportLines.Add("")
$reportLines.Add("## Offline Preflight")
$reportLines.Add("")
$reportLines.Add("- Material completeness check: $($offlinePreflight.MaterialCompletenessCheck)")
$reportLines.Add("- Approval placeholder check: $($offlinePreflight.ApprovalPlaceholderCheck)")
$reportLines.Add("- Pilot Window status check: $($offlinePreflight.PilotWindowStatusCheck)")
$reportLines.Add("- Emergency stop online verification: $($offlinePreflight.EmergencyStopOnlineVerification)")
$reportLines.Add("- Rollback checklist: $($offlinePreflight.RollbackChecklist)")
$reportLines.Add("- Configuration owner: $($offlinePreflight.ConfigurationOwner)")
$reportLines.Add("- Custodian: $($offlinePreflight.Custodian)")
$reportLines.Add("- Approver: $($offlinePreflight.Approver)")
$reportLines.Add("- Configuration window: $($offlinePreflight.ConfigurationWindow)")
$reportLines.Add("- Credential status placeholder: $($offlinePreflight.CredentialStatus)")
$reportLines.Add("- Real credential read: $($offlinePreflight.RealCredentialRead)")
$reportLines.Add("- Real credential written: $($offlinePreflight.RealCredentialWritten)")
$reportLines.Add("- Real credential displayed: $($offlinePreflight.RealCredentialDisplayed)")
$reportLines.Add("- Real endpoint called: $($offlinePreflight.RealEndpointCalled)")
$reportLines.Add("- Real production artifact generated: $($offlinePreflight.RealProductionArtifactGenerated)")
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
$reportLines.Add("- P17.7 does not execute a real Pilot.")
$reportLines.Add("- Default output is BlockedNoSignedApproval because no signed approval has been received.")
$reportLines.Add("- ReadyForExplicitManualExecutionRequest still only permits a future explicit request; it is not a real endpoint call.")
$reportLines.Add("- Real endpoint/token use remains outside P17.7 and requires a later explicit execution stage.")
$reportLines.Add("- Future execution still requires separate user instruction, signed approval, approved Pilot Window, approved credential configuration, executor confirmation, rollback window, emergency stop online verification, and post-execution audit archival.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

$reportContent = $reportLines -join [Environment]::NewLine
Assert-NoUnsafeReportContent -Content $reportContent -Name "P17.7 report"
Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Limited Pilot Manual Runbook P17.7 acceptance report written to: $ReportPath"

if ($hasFailures) {
    exit 1
}

