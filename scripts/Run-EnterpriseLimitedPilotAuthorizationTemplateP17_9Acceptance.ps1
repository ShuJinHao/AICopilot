[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-limited-pilot-authorization-template-p17_9-latest.md",
    [ValidateSet("BlockedByMissingAuthorizationMaterials", "BlockedByUnsafeBoundary", "ReadyForUserMaterialSubmission", "ReadyForCredentialWindowPlanning")]
    [string]$GateState = "BlockedByMissingAuthorizationMaterials",
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

function New-AuthorizationTemplate {
    [pscustomobject]@{
        PilotWindowName        = "limited production readonly Pilot"
        StartAt                = "ToBeFilledByUser"
        EndAt                  = "ToBeFilledByUser"
        Owner                  = "ToBeFilledByUser"
        Approver               = "ToBeFilledByUser"
        Executor               = "ToBeFilledByUser"
        RollbackOwner          = "ToBeFilledByUser"
        EmergencyStopOwner     = "ToBeFilledByUser"
        PilotUserScope         = "5-10"
        PilotUserFields        = @("User", "Role", "Department", "PermissionScope", "ApprovalStatus")
        EndpointAllowlist      = @("devices", "capacity_summary", "device_logs", "pass_station_records")
        TimeRange              = "last 7 days"
        MaxRows                = 50
        RequiresToolApproval   = $true
        RequiresFinalApproval  = $true
        OutputBoundary         = "draft artifacts, Final Approval, final lock"
        OperationsLedgerPolicy = "hash-only"
        CredentialFields       = @("ConfigurationOwner", "Custodian", "Approver", "ConfigurationWindow", "RollbackRequirement")
        RealEndpointCall       = $false
        RealCredentialMaterial = $false
        RealProductionArtifact = $false
    }
}

function New-BlockingLedger {
    [pscustomobject]@{
        MissingExplicitExecutionRequest       = "Open"
        MissingSignedApproval                 = "Open"
        MissingFrozenPilotWindow              = "Open"
        MissingExecutorOrApprover             = "Open"
        MissingRollbackOrEmergencyStopOwner   = "Open"
        MissingCredentialWindow               = "Open"
        MissingDryRunEvidence                 = "Open"
        UnsafeProductionBoundary              = "NotDetected"
    }
}

function Test-AuthorizationTemplateComplete {
    param([Parameter(Mandatory = $true)]$Template)

    foreach ($field in @("PilotWindowName", "StartAt", "EndAt", "Owner", "Approver", "Executor", "RollbackOwner", "EmergencyStopOwner", "PilotUserScope", "TimeRange", "OutputBoundary", "OperationsLedgerPolicy")) {
        if ([string]::IsNullOrWhiteSpace([string]$Template.$field)) {
            return $false
        }
    }

    foreach ($endpoint in @("devices", "capacity_summary", "device_logs", "pass_station_records")) {
        if ($Template.EndpointAllowlist -notcontains $endpoint) {
            return $false
        }
    }

    foreach ($field in @("User", "Role", "Department", "PermissionScope", "ApprovalStatus")) {
        if ($Template.PilotUserFields -notcontains $field) {
            return $false
        }
    }

    foreach ($field in @("ConfigurationOwner", "Custodian", "Approver", "ConfigurationWindow", "RollbackRequirement")) {
        if ($Template.CredentialFields -notcontains $field) {
            return $false
        }
    }

    return $Template.MaxRows -eq 50 `
        -and $Template.RequiresToolApproval `
        -and $Template.RequiresFinalApproval `
        -and -not $Template.RealEndpointCall `
        -and -not $Template.RealCredentialMaterial `
        -and -not $Template.RealProductionArtifact
}

function Test-HasOpenAuthorizationBlockers {
    param([Parameter(Mandatory = $true)]$Ledger)

    foreach ($field in @("MissingExplicitExecutionRequest", "MissingSignedApproval", "MissingFrozenPilotWindow", "MissingExecutorOrApprover", "MissingRollbackOrEmergencyStopOwner", "MissingCredentialWindow", "MissingDryRunEvidence")) {
        if ($Ledger.$field -eq "Open") {
            return $true
        }
    }

    return $false
}

function Test-HasUnsafeBoundary {
    param([Parameter(Mandatory = $true)]$Ledger)

    return $Ledger.UnsafeProductionBoundary -eq "Open"
}

function Get-GoNoGoDecision {
    param(
        [string]$State,
        [bool]$TemplateComplete,
        [bool]$HasOpenAuthorizationBlockers,
        [bool]$HasUnsafeBoundary,
        [bool]$HasAcceptanceFailures
    )

    if ($HasAcceptanceFailures) {
        return "BlockedByAcceptanceFailure"
    }

    if ($HasUnsafeBoundary -or $State -eq "BlockedByUnsafeBoundary") {
        return "BlockedByUnsafeBoundary"
    }

    if (-not $TemplateComplete -or $HasOpenAuthorizationBlockers -or $State -eq "BlockedByMissingAuthorizationMaterials") {
        return "BlockedByMissingAuthorizationMaterials"
    }

    if ($State -eq "ReadyForCredentialWindowPlanning") {
        return "ReadyForCredentialWindowPlanning"
    }

    return "ReadyForUserMaterialSubmission"
}

$results = @()

if (-not $SkipScopeGuard) {
    $results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
    }
}

$results += Invoke-Step -Name "P17.8 Explicit Request Inheritance Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-explicit-execution-request-p17_8-scope.md",
        ".\docs\enterprise-limited-pilot-explicit-execution-request-p17_8-package.md",
        ".\docs\enterprise-limited-pilot-explicit-execution-request-p17_8-latest.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P17.8 inheritance document is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"
    foreach ($marker in @(
        "P17.8",
        "BlockedNoExplicitExecutionRequest",
        "ExecutionPermission: not granted",
        "Explicit Manual Execution Request",
        "ReadyForCredentialConfigurationWindow",
        "query_cloud_data_readonly",
        "does not execute"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P17.8 inheritance is missing marker: $marker"
        }
    }

    "P17.8 remains a non-executing explicit request intake and pre-execution gate."
}

$results += Invoke-Step -Name "P17.9 Scope And Authorization Template Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-authorization-template-p17_9-scope.md",
        ".\docs\enterprise-limited-pilot-authorization-template-p17_9-package.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P17.9 document is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"
    foreach ($marker in @(
        "P17.9",
        "Authorization Template",
        "Blocking Ledger",
        "MissingExplicitExecutionRequest",
        "MissingSignedApproval",
        "MissingFrozenPilotWindow",
        "MissingExecutorOrApprover",
        "MissingRollbackOrEmergencyStopOwner",
        "MissingCredentialWindow",
        "MissingDryRunEvidence",
        "UnsafeProductionBoundary",
        "BlockedByMissingAuthorizationMaterials",
        "BlockedByUnsafeBoundary",
        "ReadyForUserMaterialSubmission",
        "ReadyForCredentialWindowPlanning",
        "5-10",
        "devices",
        "capacity_summary",
        "device_logs",
        "pass_station_records",
        "last 7 days",
        "maxRows=50",
        "Final Approval",
        "hash-only",
        "query_cloud_data_readonly",
        "does not execute"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P17.9 material is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P17.9 material"
    "P17.9 scope and authorization template package markers passed."
}

$authorizationTemplate = New-AuthorizationTemplate
$blockingLedger = New-BlockingLedger

$results += Invoke-Step -Name "P17.9 Go/No-Go Matrix Check" -Script {
    $completeTemplate = New-AuthorizationTemplate
    $openLedger = New-BlockingLedger
    $closedLedger = New-BlockingLedger
    foreach ($field in @("MissingExplicitExecutionRequest", "MissingSignedApproval", "MissingFrozenPilotWindow", "MissingExecutorOrApprover", "MissingRollbackOrEmergencyStopOwner", "MissingCredentialWindow", "MissingDryRunEvidence")) {
        $closedLedger.$field = "Closed"
    }
    $unsafeLedger = New-BlockingLedger
    $unsafeLedger.UnsafeProductionBoundary = "Open"

    $defaultDecision = Get-GoNoGoDecision -State "BlockedByMissingAuthorizationMaterials" -TemplateComplete (Test-AuthorizationTemplateComplete -Template $completeTemplate) -HasOpenAuthorizationBlockers (Test-HasOpenAuthorizationBlockers -Ledger $openLedger) -HasUnsafeBoundary $false -HasAcceptanceFailures $false
    $unsafeDecision = Get-GoNoGoDecision -State "ReadyForUserMaterialSubmission" -TemplateComplete $true -HasOpenAuthorizationBlockers $false -HasUnsafeBoundary (Test-HasUnsafeBoundary -Ledger $unsafeLedger) -HasAcceptanceFailures $false
    $missingTemplate = Get-GoNoGoDecision -State "ReadyForUserMaterialSubmission" -TemplateComplete $false -HasOpenAuthorizationBlockers $false -HasUnsafeBoundary $false -HasAcceptanceFailures $false
    $userSubmission = Get-GoNoGoDecision -State "ReadyForUserMaterialSubmission" -TemplateComplete (Test-AuthorizationTemplateComplete -Template $completeTemplate) -HasOpenAuthorizationBlockers (Test-HasOpenAuthorizationBlockers -Ledger $closedLedger) -HasUnsafeBoundary $false -HasAcceptanceFailures $false
    $credentialPlanning = Get-GoNoGoDecision -State "ReadyForCredentialWindowPlanning" -TemplateComplete (Test-AuthorizationTemplateComplete -Template $completeTemplate) -HasOpenAuthorizationBlockers (Test-HasOpenAuthorizationBlockers -Ledger $closedLedger) -HasUnsafeBoundary $false -HasAcceptanceFailures $false
    $acceptanceFailure = Get-GoNoGoDecision -State "ReadyForUserMaterialSubmission" -TemplateComplete $true -HasOpenAuthorizationBlockers $false -HasUnsafeBoundary $false -HasAcceptanceFailures $true

    if ($defaultDecision -ne "BlockedByMissingAuthorizationMaterials") {
        throw "Default state mapped to unexpected result: $defaultDecision"
    }
    if ($unsafeDecision -ne "BlockedByUnsafeBoundary") {
        throw "Unsafe boundary mapped to unexpected result: $unsafeDecision"
    }
    if ($missingTemplate -ne "BlockedByMissingAuthorizationMaterials") {
        throw "Missing template mapped to unexpected result: $missingTemplate"
    }
    if ($userSubmission -ne "ReadyForUserMaterialSubmission") {
        throw "User submission state mapped to unexpected result: $userSubmission"
    }
    if ($credentialPlanning -ne "ReadyForCredentialWindowPlanning") {
        throw "Credential planning state mapped to unexpected result: $credentialPlanning"
    }
    if ($acceptanceFailure -ne "BlockedByAcceptanceFailure") {
        throw "Acceptance failure mapped to unexpected result: $acceptanceFailure"
    }

    "Go/No-Go matrix passed: default stays BlockedByMissingAuthorizationMaterials, unsafe evidence blocks, and readiness does not execute."
}

$results += Invoke-Step -Name "P17.9 Authorization Template Completeness Check" -Script {
    if (-not (Test-AuthorizationTemplateComplete -Template $authorizationTemplate)) {
        throw "Authorization template is incomplete or allows real execution material."
    }

    $json = $authorizationTemplate | ConvertTo-Json -Depth 5
    Assert-NoUnsafeReportContent -Content $json -Name "P17.9 authorization template"
    "Authorization template includes Pilot Window, 5-10 pilot users, approval chain, executor, rollback owner, emergency stop owner, endpoint allowlist, last-7-days boundary, maxRows 50, credential responsibility, Final Approval, and hash-only ledger policy."
}

$results += Invoke-Step -Name "P17.9 Blocking Ledger Check" -Script {
    foreach ($field in @("MissingExplicitExecutionRequest", "MissingSignedApproval", "MissingFrozenPilotWindow", "MissingExecutorOrApprover", "MissingRollbackOrEmergencyStopOwner", "MissingCredentialWindow", "MissingDryRunEvidence")) {
        if ($blockingLedger.$field -ne "Open") {
            throw "Expected blocker to be open: $field"
        }
    }

    if ($blockingLedger.UnsafeProductionBoundary -ne "NotDetected") {
        throw "UnsafeProductionBoundary should not be open by default."
    }

    $json = $blockingLedger | ConvertTo-Json -Depth 4
    Assert-NoUnsafeReportContent -Content $json -Name "P17.9 blocking ledger"
    "Blocking ledger records all P17.8 material gaps as open blockers and keeps UnsafeProductionBoundary as NotDetected."
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

$results += Invoke-Step -Name "P17.9 No Execution Claim Check" -Script {
    $combined = @(
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-authorization-template-p17_9-scope.md" -Raw
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-authorization-template-p17_9-package.md" -Raw
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
            throw "P17.9 material contains forbidden execution marker: $forbidden"
        }
    }

    "P17.9 material records authorization template and blocking ledger only and does not claim execution."
}

$externalReviewState = Get-ExternalReviewState
$prSummary = Get-PrSummary
$localHead = (git rev-parse HEAD 2>$null | Out-String).Trim()
$branch = (git branch --show-current 2>$null | Out-String).Trim()
$failedResults = @($results | Where-Object { -not $_.Succeeded })
$hasFailures = $failedResults.Count -gt 0
$templateComplete = Test-AuthorizationTemplateComplete -Template $authorizationTemplate
$hasOpenAuthorizationBlockers = Test-HasOpenAuthorizationBlockers -Ledger $blockingLedger
$hasUnsafeBoundary = Test-HasUnsafeBoundary -Ledger $blockingLedger
$goNoGo = Get-GoNoGoDecision -State $GateState -TemplateComplete $templateComplete -HasOpenAuthorizationBlockers $hasOpenAuthorizationBlockers -HasUnsafeBoundary $hasUnsafeBoundary -HasAcceptanceFailures $hasFailures
$executionPermission = if ($goNoGo -eq "ReadyForCredentialWindowPlanning") { "credential-window-planning-only" } else { "not granted" }

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Limited Pilot Authorization Template P17.9 Acceptance")
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
$reportLines.Add("- GateState: $GateState")
$reportLines.Add("- GoNoGo: $goNoGo")
$reportLines.Add("- ExecutionPermission: $executionPermission")
$reportLines.Add("- Boundary: P17.9 freezes a human authorization template and blocking ledger only; it does not execute a real Pilot and is not GA")
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
$reportLines.Add("## Authorization Template Decision")
$reportLines.Add("")
$reportLines.Add("- State: $GateState")
$reportLines.Add("- Go/No-Go: $goNoGo")
$reportLines.Add("- Execution permission: $executionPermission")
$reportLines.Add("- Review evidence: 5.5 Pro $externalReviewState")
$reportLines.Add("")
$reportLines.Add("## Human Authorization Template")
$reportLines.Add("")
$reportLines.Add("- Pilot Window: $($authorizationTemplate.PilotWindowName)")
$reportLines.Add("- Start: $($authorizationTemplate.StartAt)")
$reportLines.Add("- End: $($authorizationTemplate.EndAt)")
$reportLines.Add("- Owner: $($authorizationTemplate.Owner)")
$reportLines.Add("- Approver: $($authorizationTemplate.Approver)")
$reportLines.Add("- Executor: $($authorizationTemplate.Executor)")
$reportLines.Add("- Rollback owner: $($authorizationTemplate.RollbackOwner)")
$reportLines.Add("- Emergency stop owner: $($authorizationTemplate.EmergencyStopOwner)")
$reportLines.Add("- Pilot user scope: $($authorizationTemplate.PilotUserScope)")
$reportLines.Add("- Pilot user fields: $($authorizationTemplate.PilotUserFields -join ', ')")
$reportLines.Add("- Endpoint allowlist: $($authorizationTemplate.EndpointAllowlist -join ', ')")
$reportLines.Add("- Time range: $($authorizationTemplate.TimeRange)")
$reportLines.Add("- maxRows: $($authorizationTemplate.MaxRows)")
$reportLines.Add("- Tool Approval required: $($authorizationTemplate.RequiresToolApproval)")
$reportLines.Add("- Final Approval required: $($authorizationTemplate.RequiresFinalApproval)")
$reportLines.Add("- Output boundary: $($authorizationTemplate.OutputBoundary)")
$reportLines.Add("- Operations ledger policy: $($authorizationTemplate.OperationsLedgerPolicy)")
$reportLines.Add("- Credential fields: $($authorizationTemplate.CredentialFields -join ', ')")
$reportLines.Add("- Real endpoint call: $($authorizationTemplate.RealEndpointCall)")
$reportLines.Add("- Real credential material: $($authorizationTemplate.RealCredentialMaterial)")
$reportLines.Add("- Real production artifact: $($authorizationTemplate.RealProductionArtifact)")
$reportLines.Add("")
$reportLines.Add("## Blocking Ledger")
$reportLines.Add("")
$reportLines.Add("- MissingExplicitExecutionRequest: $($blockingLedger.MissingExplicitExecutionRequest)")
$reportLines.Add("- MissingSignedApproval: $($blockingLedger.MissingSignedApproval)")
$reportLines.Add("- MissingFrozenPilotWindow: $($blockingLedger.MissingFrozenPilotWindow)")
$reportLines.Add("- MissingExecutorOrApprover: $($blockingLedger.MissingExecutorOrApprover)")
$reportLines.Add("- MissingRollbackOrEmergencyStopOwner: $($blockingLedger.MissingRollbackOrEmergencyStopOwner)")
$reportLines.Add("- MissingCredentialWindow: $($blockingLedger.MissingCredentialWindow)")
$reportLines.Add("- MissingDryRunEvidence: $($blockingLedger.MissingDryRunEvidence)")
$reportLines.Add("- UnsafeProductionBoundary: $($blockingLedger.UnsafeProductionBoundary)")
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
$reportLines.Add("- P17.9 does not execute a real Pilot.")
$reportLines.Add("- Default output is BlockedByMissingAuthorizationMaterials because the explicit execution request, signed approval, frozen Pilot Window, credential window, and dry-run evidence are not user-submitted as complete materials.")
$reportLines.Add("- ReadyForUserMaterialSubmission only means the template can be handed to the user for manual filling.")
$reportLines.Add("- ReadyForCredentialWindowPlanning still only permits a later credential-window planning stage; it is not a real endpoint call.")
$reportLines.Add("- Real endpoint/token use remains outside P17.9 and requires a later explicit execution stage.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

$reportContent = $reportLines -join [Environment]::NewLine
Assert-NoUnsafeReportContent -Content $reportContent -Name "P17.9 report"
Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Limited Pilot Authorization Template P17.9 acceptance report written to: $ReportPath"

if ($hasFailures) {
    exit 1
}
