[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-limited-pilot-signed-approval-intake-p17_6-latest.md",
    [ValidateSet("NoSignedApprovalReceived", "SignedApprovalIncomplete", "CredentialWindowIncomplete", "ReadyForManualExecutionStepPlanning")]
    [string]$IntakeState = "NoSignedApprovalReceived",
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

function New-ManualExecutionChecklist {
    [pscustomobject]@{
        PilotWindowName = "limited production readonly Pilot"
        WindowSigned = $false
        Owner = "ToBeSigned"
        Approver = "ToBeSigned"
        Executor = "ToBeSigned"
        RollbackOwner = "ToBeSigned"
        EmergencyStopOwner = "ToBeSigned"
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
        ConfigurationOwner = "ToBeSigned"
        Custodian = "ToBeSigned"
        Approver = "ToBeSigned"
        ConfigurationWindow = "ToBeSigned"
        RollbackRequirement = "Disable future execution window and revoke future runtime configuration path"
        CredentialStatus = "NotConfigured"
        RealCredentialRead = $false
        RealCredentialWritten = $false
        RealCredentialDisplayed = $false
    }
}

function Test-SignedApprovalMaterialComplete {
    param([Parameter(Mandatory = $true)]$Checklist)

    foreach ($field in @("PilotWindowName", "Owner", "Approver", "Executor", "RollbackOwner", "EmergencyStopOwner", "PilotUserCountTarget", "TimeRange", "OperationsLedgerPolicy")) {
        if ([string]::IsNullOrWhiteSpace([string]$Checklist.$field)) {
            return $false
        }
    }

    foreach ($endpoint in @("devices", "capacity_summary", "device_logs", "pass_station_records")) {
        if ($Checklist.EndpointAllowlist -notcontains $endpoint) {
            return $false
        }
    }

    return $Checklist.MaxRows -eq 50 -and $Checklist.RequiresToolApproval -and $Checklist.RequiresFinalApproval
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

    if ($State -eq "NoSignedApprovalReceived") {
        return "NoSignedApprovalReceived"
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

    if ($State -eq "CredentialWindowIncomplete") {
        return "CredentialWindowIncomplete"
    }

    return "ReadyForManualExecutionStepPlanning"
}

$results = @()

if (-not $SkipScopeGuard) {
    $results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
    }
}

$results += Invoke-Step -Name "P17.5 Signed Approval Inheritance Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-signed-approval-p17_5-scope.md",
        ".\docs\enterprise-limited-pilot-signed-approval-p17_5-package.md",
        ".\docs\enterprise-limited-pilot-signed-approval-p17_5-latest.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P17.5 evidence is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"

    foreach ($marker in @(
        "P17.5",
        "MissingSignedExecutionApproval",
        "ExecutionPermission: not granted",
        "ReadyForManualPilotExecutionStep",
        "Credential Configuration Window",
        "query_cloud_data_readonly",
        "does not execute a real Pilot"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P17.5 inheritance is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P17.5 inheritance"
    "P17.5 signed approval package and latest evidence remain non-executable."
}

$results += Invoke-Step -Name "P17.6 Scope And Intake Package Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-signed-approval-intake-p17_6-scope.md",
        ".\docs\enterprise-limited-pilot-signed-approval-intake-p17_6-package.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P17.6 document is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"

    foreach ($marker in @(
        "P17.6",
        "NoSignedApprovalReceived",
        "SignedApprovalIncomplete",
        "CredentialWindowIncomplete",
        "ReadyForManualExecutionStepPlanning",
        "Manual Execution",
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
            throw "P17.6 material is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P17.6 material"
    "P17.6 scope and signed approval intake package markers passed."
}

$manualChecklist = New-ManualExecutionChecklist
$credentialWindow = New-CredentialWindow

$results += Invoke-Step -Name "P17.6 Signed Approval Intake Matrix Check" -Script {
    $signedComplete = Test-SignedApprovalMaterialComplete -Checklist $manualChecklist
    $credentialComplete = Test-CredentialWindowComplete -CredentialWindow $credentialWindow

    $none = Get-GoNoGoDecision -State "NoSignedApprovalReceived" -SignedApprovalComplete $signedComplete -CredentialWindowComplete $credentialComplete -HasAcceptanceFailures $false
    $incomplete = Get-GoNoGoDecision -State "SignedApprovalIncomplete" -SignedApprovalComplete $signedComplete -CredentialWindowComplete $credentialComplete -HasAcceptanceFailures $false
    $credentialIncomplete = Get-GoNoGoDecision -State "CredentialWindowIncomplete" -SignedApprovalComplete $signedComplete -CredentialWindowComplete $credentialComplete -HasAcceptanceFailures $false
    $ready = Get-GoNoGoDecision -State "ReadyForManualExecutionStepPlanning" -SignedApprovalComplete $signedComplete -CredentialWindowComplete $credentialComplete -HasAcceptanceFailures $false
    $signedMissing = Get-GoNoGoDecision -State "ReadyForManualExecutionStepPlanning" -SignedApprovalComplete $false -CredentialWindowComplete $credentialComplete -HasAcceptanceFailures $false
    $credentialMissing = Get-GoNoGoDecision -State "ReadyForManualExecutionStepPlanning" -SignedApprovalComplete $signedComplete -CredentialWindowComplete $false -HasAcceptanceFailures $false
    $acceptanceFailure = Get-GoNoGoDecision -State "ReadyForManualExecutionStepPlanning" -SignedApprovalComplete $signedComplete -CredentialWindowComplete $credentialComplete -HasAcceptanceFailures $true

    if ($none -ne "NoSignedApprovalReceived") {
        throw "No signed approval mapped to unexpected result: $none"
    }
    if ($incomplete -ne "SignedApprovalIncomplete") {
        throw "Incomplete signed approval mapped to unexpected result: $incomplete"
    }
    if ($credentialIncomplete -ne "CredentialWindowIncomplete") {
        throw "Incomplete credential window mapped to unexpected result: $credentialIncomplete"
    }
    if ($ready -ne "ReadyForManualExecutionStepPlanning") {
        throw "Ready intake mapped to unexpected result: $ready"
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

    "Signed approval intake matrix passed: default stays NoSignedApprovalReceived and never executes a real Pilot."
}

$results += Invoke-Step -Name "P17.6 Manual Execution Checklist Completeness Check" -Script {
    if (-not (Test-SignedApprovalMaterialComplete -Checklist $manualChecklist)) {
        throw "Manual execution checklist is incomplete."
    }

    "Manual execution checklist includes Pilot Window, approver, executor, rollback owner, emergency stop owner, 5-10 user scope, endpoint allowlist, last-7-days boundary, maxRows 50, Tool Approval, Final Approval, and hash-only ledger policy."
}

$results += Invoke-Step -Name "P17.6 Credential Window Check" -Script {
    if (-not (Test-CredentialWindowComplete -CredentialWindow $credentialWindow)) {
        throw "Credential window is incomplete or touched a real credential."
    }

    $json = $credentialWindow | ConvertTo-Json -Depth 4
    Assert-NoUnsafeReportContent -Content $json -Name "P17.6 credential window"
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

$results += Invoke-Step -Name "P17.6 No Execution Claim Check" -Script {
    $combined = @(
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-signed-approval-intake-p17_6-scope.md" -Raw
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-signed-approval-intake-p17_6-package.md" -Raw
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
            throw "P17.6 material contains forbidden execution marker: $forbidden"
        }
    }

    "P17.6 material records signed approval intake only and does not claim execution."
}

$externalReviewState = Get-ExternalReviewState
$prSummary = Get-PrSummary
$localHead = (git rev-parse HEAD 2>$null | Out-String).Trim()
$branch = (git branch --show-current 2>$null | Out-String).Trim()
$failedResults = @($results | Where-Object { -not $_.Succeeded })
$hasFailures = $failedResults.Count -gt 0
$signedApprovalComplete = Test-SignedApprovalMaterialComplete -Checklist $manualChecklist
$credentialWindowComplete = Test-CredentialWindowComplete -CredentialWindow $credentialWindow
$goNoGo = Get-GoNoGoDecision -State $IntakeState -SignedApprovalComplete $signedApprovalComplete -CredentialWindowComplete $credentialWindowComplete -HasAcceptanceFailures $hasFailures
$executionPermission = if ($goNoGo -eq "ReadyForManualExecutionStepPlanning") { "manual-step-planning-only" } else { "not granted" }

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Limited Pilot Signed Approval Intake P17.6 Acceptance")
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
$reportLines.Add("- IntakeState: $IntakeState")
$reportLines.Add("- GoNoGo: $goNoGo")
$reportLines.Add("- ExecutionPermission: $executionPermission")
$reportLines.Add("- Boundary: P17.6 receives signed approval results and freezes manual execution-step planning material only; it does not execute a real Pilot and is not GA")
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
$reportLines.Add("## Signed Approval Intake")
$reportLines.Add("")
$reportLines.Add("- State: $IntakeState")
$reportLines.Add("- Go/No-Go: $goNoGo")
$reportLines.Add("- Execution permission: $executionPermission")
$reportLines.Add("- Review evidence: 5.5 Pro $externalReviewState")
$reportLines.Add("")
$reportLines.Add("## Manual Execution Checklist")
$reportLines.Add("")
$reportLines.Add("- Pilot Window: $($manualChecklist.PilotWindowName)")
$reportLines.Add("- Window signed: $($manualChecklist.WindowSigned)")
$reportLines.Add("- Owner: $($manualChecklist.Owner)")
$reportLines.Add("- Approver: $($manualChecklist.Approver)")
$reportLines.Add("- Executor: $($manualChecklist.Executor)")
$reportLines.Add("- Rollback owner: $($manualChecklist.RollbackOwner)")
$reportLines.Add("- Emergency stop owner: $($manualChecklist.EmergencyStopOwner)")
$reportLines.Add("- Pilot user scope: $($manualChecklist.PilotUserCountTarget)")
$reportLines.Add("- Endpoint allowlist: $($manualChecklist.EndpointAllowlist -join ', ')")
$reportLines.Add("- Time range: $($manualChecklist.TimeRange)")
$reportLines.Add("- maxRows: $($manualChecklist.MaxRows)")
$reportLines.Add("- Tool Approval required: $($manualChecklist.RequiresToolApproval)")
$reportLines.Add("- Final Approval required: $($manualChecklist.RequiresFinalApproval)")
$reportLines.Add("- Operations ledger policy: $($manualChecklist.OperationsLedgerPolicy)")
$reportLines.Add("")
$reportLines.Add("## Credential Window")
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
$reportLines.Add("- P17.6 does not execute a real Pilot.")
$reportLines.Add("- Default output is NoSignedApprovalReceived unless signed approval material is supplied.")
$reportLines.Add("- ReadyForManualExecutionStepPlanning is still not a real endpoint call.")
$reportLines.Add("- Real endpoint/token use remains outside P17.6 and requires a future explicit execution stage.")
$reportLines.Add("- Future execution still requires separate user instruction, approved Pilot Window, approved credential configuration, executor confirmation, rollback window, emergency stop online verification, and post-execution audit archival.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

$reportContent = $reportLines -join [Environment]::NewLine
Assert-NoUnsafeReportContent -Content $reportContent -Name "P17.6 report"
Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Limited Pilot Signed Approval Intake P17.6 acceptance report written to: $ReportPath"

if ($hasFailures) {
    exit 1
}
