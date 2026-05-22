[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-limited-pilot-explicit-execution-request-p17_8-latest.md",
    [ValidateSet("BlockedNoExplicitExecutionRequest", "BlockedSignedApprovalMissing", "BlockedExecutionRequestIncomplete", "BlockedCredentialWindowMissing", "ReadyForCredentialConfigurationWindow")]
    [string]$RequestState = "BlockedNoExplicitExecutionRequest",
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

function New-ExplicitExecutionRequest {
    [pscustomobject]@{
        ExplicitUserExecutionRequestReceived = $false
        SignedApprovalEvidence               = "Missing"
        FrozenPilotWindowEvidence            = "Missing"
        PilotWindowName                      = "limited production readonly Pilot"
        Executor                             = "ToBeSigned"
        ApprovalChain                        = "ToBeSigned"
        RollbackOwner                        = "ToBeSigned"
        EmergencyStopOwner                   = "ToBeSigned"
        PilotUserCountTarget                 = "5-10"
        EndpointAllowlist                    = @("devices", "capacity_summary", "device_logs", "pass_station_records")
        TimeRange                            = "last 7 days"
        MaxRows                              = 50
        RequiresToolApproval                 = $true
        RequiresFinalApproval                = $true
        OperationsLedgerPolicy               = "hash-only"
        RealEndpointCallAllowed              = $false
        RealProductionArtifactGenerated      = $false
    }
}

function New-CredentialWindowPreflight {
    [pscustomobject]@{
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

function Test-SignedApprovalEvidenceComplete {
    param([Parameter(Mandatory = $true)]$Request)

    return $Request.ExplicitUserExecutionRequestReceived `
        -and $Request.SignedApprovalEvidence -eq "Present" `
        -and $Request.FrozenPilotWindowEvidence -eq "Present"
}

function Test-ExecutionRequestComplete {
    param([Parameter(Mandatory = $true)]$Request)

    foreach ($field in @("PilotWindowName", "Executor", "ApprovalChain", "RollbackOwner", "EmergencyStopOwner", "PilotUserCountTarget", "TimeRange", "OperationsLedgerPolicy")) {
        if ([string]::IsNullOrWhiteSpace([string]$Request.$field)) {
            return $false
        }
    }

    foreach ($endpoint in @("devices", "capacity_summary", "device_logs", "pass_station_records")) {
        if ($Request.EndpointAllowlist -notcontains $endpoint) {
            return $false
        }
    }

    return $Request.MaxRows -eq 50 `
        -and $Request.RequiresToolApproval `
        -and $Request.RequiresFinalApproval `
        -and -not $Request.RealEndpointCallAllowed `
        -and -not $Request.RealProductionArtifactGenerated
}

function Test-CredentialWindowComplete {
    param([Parameter(Mandatory = $true)]$CredentialWindow)

    foreach ($field in @("ConfigurationOwner", "Custodian", "Approver", "ConfigurationWindow", "CredentialStatus")) {
        if ([string]::IsNullOrWhiteSpace([string]$CredentialWindow.$field)) {
            return $false
        }
    }

    return -not (
        $CredentialWindow.RealCredentialRead `
        -or $CredentialWindow.RealCredentialWritten `
        -or $CredentialWindow.RealCredentialDisplayed `
        -or $CredentialWindow.RealEndpointCalled `
        -or $CredentialWindow.RealProductionArtifactGenerated
    )
}

function Get-GoNoGoDecision {
    param(
        [string]$State,
        [bool]$SignedApprovalComplete,
        [bool]$ExecutionRequestComplete,
        [bool]$CredentialWindowComplete,
        [bool]$HasAcceptanceFailures
    )

    if ($HasAcceptanceFailures) {
        return "BlockedByAcceptanceFailure"
    }

    if ($State -eq "BlockedNoExplicitExecutionRequest") {
        return "BlockedNoExplicitExecutionRequest"
    }

    if (-not $SignedApprovalComplete) {
        return "BlockedSignedApprovalMissing"
    }

    if (-not $ExecutionRequestComplete) {
        return "BlockedExecutionRequestIncomplete"
    }

    if (-not $CredentialWindowComplete) {
        return "BlockedCredentialWindowMissing"
    }

    if ($State -eq "BlockedSignedApprovalMissing") {
        return "BlockedSignedApprovalMissing"
    }

    if ($State -eq "BlockedExecutionRequestIncomplete") {
        return "BlockedExecutionRequestIncomplete"
    }

    if ($State -eq "BlockedCredentialWindowMissing") {
        return "BlockedCredentialWindowMissing"
    }

    return "ReadyForCredentialConfigurationWindow"
}

$results = @()

if (-not $SkipScopeGuard) {
    $results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
    }
}

$results += Invoke-Step -Name "P17.7 Manual Runbook Inheritance Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-manual-runbook-p17_7-scope.md",
        ".\docs\enterprise-limited-pilot-manual-runbook-p17_7-package.md",
        ".\docs\enterprise-limited-pilot-manual-runbook-p17_7-latest.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P17.7 inheritance document is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"
    foreach ($marker in @(
        "P17.7",
        "BlockedNoSignedApproval",
        "ExecutionPermission: not granted",
        "Manual Execution Runbook",
        "Offline Preflight",
        "ReadyForExplicitManualExecutionRequest",
        "query_cloud_data_readonly",
        "does not execute"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P17.7 inheritance is missing marker: $marker"
        }
    }

    "P17.7 remains a non-executing manual runbook and offline preflight baseline."
}

$results += Invoke-Step -Name "P17.8 Scope And Explicit Request Package Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-explicit-execution-request-p17_8-scope.md",
        ".\docs\enterprise-limited-pilot-explicit-execution-request-p17_8-package.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P17.8 document is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"
    foreach ($marker in @(
        "P17.8",
        "Explicit Manual Execution Request",
        "BlockedNoExplicitExecutionRequest",
        "BlockedSignedApprovalMissing",
        "BlockedExecutionRequestIncomplete",
        "BlockedCredentialWindowMissing",
        "ReadyForCredentialConfigurationWindow",
        "Credential",
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
            throw "P17.8 material is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P17.8 material"
    "P17.8 scope and explicit request package markers passed."
}

$explicitRequest = New-ExplicitExecutionRequest
$credentialWindow = New-CredentialWindowPreflight

$results += Invoke-Step -Name "P17.8 Go/No-Go Matrix Check" -Script {
    $completeRequest = New-ExplicitExecutionRequest
    $completeRequest.ExplicitUserExecutionRequestReceived = $true
    $completeRequest.SignedApprovalEvidence = "Present"
    $completeRequest.FrozenPilotWindowEvidence = "Present"
    $completeCredentialWindow = New-CredentialWindowPreflight

    $defaultDecision = Get-GoNoGoDecision -State "BlockedNoExplicitExecutionRequest" -SignedApprovalComplete $false -ExecutionRequestComplete $false -CredentialWindowComplete $false -HasAcceptanceFailures $false
    $missingSigned = Get-GoNoGoDecision -State "ReadyForCredentialConfigurationWindow" -SignedApprovalComplete $false -ExecutionRequestComplete $true -CredentialWindowComplete $true -HasAcceptanceFailures $false
    $missingExecution = Get-GoNoGoDecision -State "ReadyForCredentialConfigurationWindow" -SignedApprovalComplete $true -ExecutionRequestComplete $false -CredentialWindowComplete $true -HasAcceptanceFailures $false
    $missingCredential = Get-GoNoGoDecision -State "ReadyForCredentialConfigurationWindow" -SignedApprovalComplete $true -ExecutionRequestComplete $true -CredentialWindowComplete $false -HasAcceptanceFailures $false
    $stateSignedMissing = Get-GoNoGoDecision -State "BlockedSignedApprovalMissing" -SignedApprovalComplete $true -ExecutionRequestComplete $true -CredentialWindowComplete $true -HasAcceptanceFailures $false
    $stateExecutionIncomplete = Get-GoNoGoDecision -State "BlockedExecutionRequestIncomplete" -SignedApprovalComplete $true -ExecutionRequestComplete $true -CredentialWindowComplete $true -HasAcceptanceFailures $false
    $stateCredentialMissing = Get-GoNoGoDecision -State "BlockedCredentialWindowMissing" -SignedApprovalComplete $true -ExecutionRequestComplete $true -CredentialWindowComplete $true -HasAcceptanceFailures $false
    $ready = Get-GoNoGoDecision -State "ReadyForCredentialConfigurationWindow" -SignedApprovalComplete (Test-SignedApprovalEvidenceComplete -Request $completeRequest) -ExecutionRequestComplete (Test-ExecutionRequestComplete -Request $completeRequest) -CredentialWindowComplete (Test-CredentialWindowComplete -CredentialWindow $completeCredentialWindow) -HasAcceptanceFailures $false
    $acceptanceFailure = Get-GoNoGoDecision -State "ReadyForCredentialConfigurationWindow" -SignedApprovalComplete $true -ExecutionRequestComplete $true -CredentialWindowComplete $true -HasAcceptanceFailures $true

    if ($defaultDecision -ne "BlockedNoExplicitExecutionRequest") {
        throw "Default state mapped to unexpected result: $defaultDecision"
    }
    if ($missingSigned -ne "BlockedSignedApprovalMissing") {
        throw "Missing signed approval mapped to unexpected result: $missingSigned"
    }
    if ($missingExecution -ne "BlockedExecutionRequestIncomplete") {
        throw "Missing execution request mapped to unexpected result: $missingExecution"
    }
    if ($missingCredential -ne "BlockedCredentialWindowMissing") {
        throw "Missing credential window mapped to unexpected result: $missingCredential"
    }
    if ($stateSignedMissing -ne "BlockedSignedApprovalMissing") {
        throw "Signed missing state mapped to unexpected result: $stateSignedMissing"
    }
    if ($stateExecutionIncomplete -ne "BlockedExecutionRequestIncomplete") {
        throw "Execution incomplete state mapped to unexpected result: $stateExecutionIncomplete"
    }
    if ($stateCredentialMissing -ne "BlockedCredentialWindowMissing") {
        throw "Credential missing state mapped to unexpected result: $stateCredentialMissing"
    }
    if ($ready -ne "ReadyForCredentialConfigurationWindow") {
        throw "Ready state mapped to unexpected result: $ready"
    }
    if ($acceptanceFailure -ne "BlockedByAcceptanceFailure") {
        throw "Acceptance failure mapped to unexpected result: $acceptanceFailure"
    }

    "Go/No-Go matrix passed: default stays BlockedNoExplicitExecutionRequest and readiness only permits a later credential configuration window."
}

$results += Invoke-Step -Name "P17.8 Explicit Request Structural Check" -Script {
    if (-not (Test-ExecutionRequestComplete -Request $explicitRequest)) {
        throw "Explicit execution request structure is incomplete or allows real execution material."
    }

    foreach ($endpoint in @("devices", "capacity_summary", "device_logs", "pass_station_records")) {
        if ($explicitRequest.EndpointAllowlist -notcontains $endpoint) {
            throw "Endpoint allowlist is missing: $endpoint"
        }
    }

    $json = $explicitRequest | ConvertTo-Json -Depth 4
    Assert-NoUnsafeReportContent -Content $json -Name "P17.8 explicit request"
    "Explicit request package preserves the fixed endpoint allowlist, last-7-days boundary, maxRows 50, Tool Approval, Final Approval, hash-only ledger, and no real endpoint execution."
}

$results += Invoke-Step -Name "P17.8 Credential Window Preflight Check" -Script {
    if (-not (Test-CredentialWindowComplete -CredentialWindow $credentialWindow)) {
        throw "Credential window preflight is incomplete or touched real credentials."
    }

    $json = $credentialWindow | ConvertTo-Json -Depth 4
    Assert-NoUnsafeReportContent -Content $json -Name "P17.8 credential window"
    "Credential window preflight records responsibility placeholders only; no real credential is read, written, displayed, or tested."
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

$results += Invoke-Step -Name "P17.8 No Execution Claim Check" -Script {
    $combined = @(
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-explicit-execution-request-p17_8-scope.md" -Raw
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-explicit-execution-request-p17_8-package.md" -Raw
    ) -join "`n"

    foreach ($forbidden in @(
        "ReadyForLimitedPilotExecution",
        "Real Pilot executed",
        "Real endpoint configured",
        "Real token configured",
        "query_cloud_data_readonly enabled",
        "GA Permission: granted",
        "ExecutionPermission: granted"
    )) {
        if ($combined -match [regex]::Escape($forbidden)) {
            throw "P17.8 material contains forbidden execution marker: $forbidden"
        }
    }

    "P17.8 material records explicit request intake and pre-execution gate only and does not claim execution."
}

$externalReviewState = Get-ExternalReviewState
$prSummary = Get-PrSummary
$localHead = (git rev-parse HEAD 2>$null | Out-String).Trim()
$branch = (git branch --show-current 2>$null | Out-String).Trim()
$failedResults = @($results | Where-Object { -not $_.Succeeded })
$hasFailures = $failedResults.Count -gt 0
$signedApprovalComplete = Test-SignedApprovalEvidenceComplete -Request $explicitRequest
$executionRequestComplete = Test-ExecutionRequestComplete -Request $explicitRequest
$credentialWindowComplete = Test-CredentialWindowComplete -CredentialWindow $credentialWindow
$goNoGo = Get-GoNoGoDecision -State $RequestState -SignedApprovalComplete $signedApprovalComplete -ExecutionRequestComplete $executionRequestComplete -CredentialWindowComplete $credentialWindowComplete -HasAcceptanceFailures $hasFailures
$executionPermission = if ($goNoGo -eq "ReadyForCredentialConfigurationWindow") { "credential-window-preparation-only" } else { "not granted" }

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Limited Pilot Explicit Execution Request P17.8 Acceptance")
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
$reportLines.Add("- RequestState: $RequestState")
$reportLines.Add("- GoNoGo: $goNoGo")
$reportLines.Add("- ExecutionPermission: $executionPermission")
$reportLines.Add("- Boundary: P17.8 receives an explicit manual execution request and checks the final pre-execution gate only; it does not execute a real Pilot and is not GA")
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
$reportLines.Add("## Explicit Execution Request Decision")
$reportLines.Add("")
$reportLines.Add("- State: $RequestState")
$reportLines.Add("- Go/No-Go: $goNoGo")
$reportLines.Add("- Execution permission: $executionPermission")
$reportLines.Add("- Review evidence: 5.5 Pro $externalReviewState")
$reportLines.Add("")
$reportLines.Add("## Explicit Manual Execution Request")
$reportLines.Add("")
$reportLines.Add("- Explicit user execution request received: $($explicitRequest.ExplicitUserExecutionRequestReceived)")
$reportLines.Add("- Signed approval evidence: $($explicitRequest.SignedApprovalEvidence)")
$reportLines.Add("- Frozen Pilot Window evidence: $($explicitRequest.FrozenPilotWindowEvidence)")
$reportLines.Add("- Pilot Window: $($explicitRequest.PilotWindowName)")
$reportLines.Add("- Executor: $($explicitRequest.Executor)")
$reportLines.Add("- Approval chain: $($explicitRequest.ApprovalChain)")
$reportLines.Add("- Rollback owner: $($explicitRequest.RollbackOwner)")
$reportLines.Add("- Emergency stop owner: $($explicitRequest.EmergencyStopOwner)")
$reportLines.Add("- Pilot user scope: $($explicitRequest.PilotUserCountTarget)")
$reportLines.Add("- Endpoint allowlist: $($explicitRequest.EndpointAllowlist -join ', ')")
$reportLines.Add("- Time range: $($explicitRequest.TimeRange)")
$reportLines.Add("- maxRows: $($explicitRequest.MaxRows)")
$reportLines.Add("- Tool Approval required: $($explicitRequest.RequiresToolApproval)")
$reportLines.Add("- Final Approval required: $($explicitRequest.RequiresFinalApproval)")
$reportLines.Add("- Operations ledger policy: $($explicitRequest.OperationsLedgerPolicy)")
$reportLines.Add("- Real endpoint call allowed: $($explicitRequest.RealEndpointCallAllowed)")
$reportLines.Add("- Real production artifact generated: $($explicitRequest.RealProductionArtifactGenerated)")
$reportLines.Add("")
$reportLines.Add("## Credential Window Preflight")
$reportLines.Add("")
$reportLines.Add("- Configuration owner: $($credentialWindow.ConfigurationOwner)")
$reportLines.Add("- Custodian: $($credentialWindow.Custodian)")
$reportLines.Add("- Approver: $($credentialWindow.Approver)")
$reportLines.Add("- Configuration window: $($credentialWindow.ConfigurationWindow)")
$reportLines.Add("- Credential status placeholder: $($credentialWindow.CredentialStatus)")
$reportLines.Add("- Real credential read: $($credentialWindow.RealCredentialRead)")
$reportLines.Add("- Real credential written: $($credentialWindow.RealCredentialWritten)")
$reportLines.Add("- Real credential displayed: $($credentialWindow.RealCredentialDisplayed)")
$reportLines.Add("- Real endpoint called: $($credentialWindow.RealEndpointCalled)")
$reportLines.Add("- Real production artifact generated: $($credentialWindow.RealProductionArtifactGenerated)")
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
$reportLines.Add("- P17.8 does not execute a real Pilot.")
$reportLines.Add("- Default output is BlockedNoExplicitExecutionRequest because no explicit manual execution request has been received.")
$reportLines.Add("- ReadyForCredentialConfigurationWindow only permits a later credential configuration window preparation; it is not a real endpoint call.")
$reportLines.Add("- Real endpoint/token use remains outside P17.8 and requires a later explicit execution stage.")
$reportLines.Add("- Future execution still requires separate user instruction, signed approval, approved Pilot Window, approved credential configuration, executor confirmation, rollback window, emergency stop online verification, and post-execution audit archival.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

$reportContent = $reportLines -join [Environment]::NewLine
Assert-NoUnsafeReportContent -Content $reportContent -Name "P17.8 report"
Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Limited Pilot Explicit Execution Request P17.8 acceptance report written to: $ReportPath"

if ($hasFailures) {
    exit 1
}
