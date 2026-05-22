[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-limited-pilot-authorization-intake-p18_0-latest.md",
    [ValidateSet("BlockedNoSubmittedAuthorizationMaterials", "BlockedInvalidAuthorizationMaterials", "BlockedUnsafeCredentialMaterial", "ReadyForCredentialWindowPlanning")]
    [string]$GateState = "BlockedNoSubmittedAuthorizationMaterials",
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

function New-SubmittedAuthorizationMaterials {
    [pscustomobject]@{
        Submitted                     = $false
        PilotWindowName               = "ToBeSubmitted"
        StartAt                       = "ToBeSubmitted"
        EndAt                         = "ToBeSubmitted"
        Owner                         = "ToBeSubmitted"
        Approver                      = "ToBeSubmitted"
        Executor                      = "ToBeSubmitted"
        RollbackOwner                 = "ToBeSubmitted"
        EmergencyStopOwner            = "ToBeSubmitted"
        PilotUsers                    = @()
        EndpointAllowlist             = @("devices", "capacity_summary", "device_logs", "pass_station_records")
        TimeRangeDays                 = 7
        MaxRows                       = 50
        RequiresToolApproval          = $true
        RequiresFinalApproval         = $true
        OutputBoundary                = "draft artifacts, Final Approval, final lock"
        OperationsLedgerPolicy        = "hash-only"
        ContainsRealCredentialMaterial = $false
        ContainsRawPayload            = $false
        ContainsRuntimeRows           = $false
        ContainsFullSql               = $false
        ContainsSensitiveContext      = $false
    }
}

function New-CompleteSubmittedAuthorizationMaterials {
    $materials = New-SubmittedAuthorizationMaterials
    $materials.Submitted = $true
    $materials.PilotWindowName = "limited production readonly Pilot"
    $materials.StartAt = "ApprovedWindowStart"
    $materials.EndAt = "ApprovedWindowEnd"
    $materials.Owner = "PilotOwner"
    $materials.Approver = "PilotApprover"
    $materials.Executor = "PilotExecutor"
    $materials.RollbackOwner = "PilotRollbackOwner"
    $materials.EmergencyStopOwner = "PilotEmergencyStopOwner"
    $materials.PilotUsers = @(
        [pscustomobject]@{ User = "pilot-user-01"; Role = "Viewer"; Department = "Operations"; PermissionScope = "ReadonlyPilot"; ApprovalStatus = "Approved" },
        [pscustomobject]@{ User = "pilot-user-02"; Role = "Viewer"; Department = "Quality"; PermissionScope = "ReadonlyPilot"; ApprovalStatus = "Approved" },
        [pscustomobject]@{ User = "pilot-user-03"; Role = "Viewer"; Department = "Production"; PermissionScope = "ReadonlyPilot"; ApprovalStatus = "Approved" },
        [pscustomobject]@{ User = "pilot-user-04"; Role = "Approver"; Department = "Operations"; PermissionScope = "ReadonlyPilot"; ApprovalStatus = "Approved" },
        [pscustomobject]@{ User = "pilot-user-05"; Role = "Auditor"; Department = "IT"; PermissionScope = "ReadonlyPilot"; ApprovalStatus = "Approved" }
    )
    return $materials
}

function New-CredentialWindowPreparation {
    [pscustomobject]@{
        ConfigurationOwner             = "ToBeSubmitted"
        Custodian                      = "ToBeSubmitted"
        Approver                       = "ToBeSubmitted"
        ConfigurationWindow            = "ToBeSubmitted"
        RollbackRequirement            = "ToBeSubmitted"
        EmergencyStopOnlineVerification = "RequiredBeforeFutureExecution"
        CredentialStatus               = "NotConfigured"
        RealCredentialRead             = $false
        RealCredentialWritten          = $false
        RealCredentialDisplayed        = $false
        RealCredentialValidated        = $false
        RealEndpointCalled             = $false
    }
}

function New-CompleteCredentialWindowPreparation {
    $window = New-CredentialWindowPreparation
    $window.ConfigurationOwner = "CredentialConfigurationOwner"
    $window.Custodian = "CredentialCustodian"
    $window.Approver = "CredentialApprover"
    $window.ConfigurationWindow = "ApprovedConfigurationWindow"
    $window.RollbackRequirement = "CredentialRollbackRequired"
    return $window
}

function Test-HasUnsafeCredentialMaterial {
    param(
        [Parameter(Mandatory = $true)]$Materials,
        [Parameter(Mandatory = $true)]$CredentialWindow
    )

    return $Materials.ContainsRealCredentialMaterial `
        -or $Materials.ContainsRawPayload `
        -or $Materials.ContainsRuntimeRows `
        -or $Materials.ContainsFullSql `
        -or $Materials.ContainsSensitiveContext `
        -or $CredentialWindow.RealCredentialRead `
        -or $CredentialWindow.RealCredentialWritten `
        -or $CredentialWindow.RealCredentialDisplayed `
        -or $CredentialWindow.RealCredentialValidated `
        -or $CredentialWindow.RealEndpointCalled
}

function Test-SubmittedAuthorizationMaterialsValid {
    param([Parameter(Mandatory = $true)]$Materials)

    if (-not $Materials.Submitted) {
        return $false
    }

    foreach ($field in @("PilotWindowName", "StartAt", "EndAt", "Owner", "Approver", "Executor", "RollbackOwner", "EmergencyStopOwner", "OutputBoundary", "OperationsLedgerPolicy")) {
        if ([string]::IsNullOrWhiteSpace([string]$Materials.$field) -or $Materials.$field -eq "ToBeSubmitted") {
            return $false
        }
    }

    if ($Materials.PilotUsers.Count -lt 5 -or $Materials.PilotUsers.Count -gt 10) {
        return $false
    }

    foreach ($user in $Materials.PilotUsers) {
        foreach ($field in @("User", "Role", "Department", "PermissionScope", "ApprovalStatus")) {
            if ([string]::IsNullOrWhiteSpace([string]$user.$field)) {
                return $false
            }
        }
        if ($user.ApprovalStatus -ne "Approved") {
            return $false
        }
    }

    $allowedEndpoints = @("devices", "capacity_summary", "device_logs", "pass_station_records")
    foreach ($endpoint in $Materials.EndpointAllowlist) {
        if ($allowedEndpoints -notcontains $endpoint) {
            return $false
        }
    }

    foreach ($endpoint in $allowedEndpoints) {
        if ($Materials.EndpointAllowlist -notcontains $endpoint) {
            return $false
        }
    }

    return $Materials.TimeRangeDays -le 7 `
        -and $Materials.MaxRows -le 50 `
        -and $Materials.RequiresToolApproval `
        -and $Materials.RequiresFinalApproval `
        -and $Materials.OperationsLedgerPolicy -eq "hash-only"
}

function Test-CredentialWindowPreparationComplete {
    param([Parameter(Mandatory = $true)]$CredentialWindow)

    foreach ($field in @("ConfigurationOwner", "Custodian", "Approver", "ConfigurationWindow", "RollbackRequirement", "EmergencyStopOnlineVerification", "CredentialStatus")) {
        if ([string]::IsNullOrWhiteSpace([string]$CredentialWindow.$field) -or $CredentialWindow.$field -eq "ToBeSubmitted") {
            return $false
        }
    }

    return -not $CredentialWindow.RealCredentialRead `
        -and -not $CredentialWindow.RealCredentialWritten `
        -and -not $CredentialWindow.RealCredentialDisplayed `
        -and -not $CredentialWindow.RealCredentialValidated `
        -and -not $CredentialWindow.RealEndpointCalled
}

function Get-GoNoGoDecision {
    param(
        [string]$State,
        [bool]$MaterialsSubmitted,
        [bool]$MaterialsValid,
        [bool]$CredentialWindowComplete,
        [bool]$HasUnsafeCredentialMaterial,
        [bool]$HasAcceptanceFailures
    )

    if ($HasAcceptanceFailures) {
        return "BlockedByAcceptanceFailure"
    }

    if ($HasUnsafeCredentialMaterial -or $State -eq "BlockedUnsafeCredentialMaterial") {
        return "BlockedUnsafeCredentialMaterial"
    }

    if (-not $MaterialsSubmitted -or $State -eq "BlockedNoSubmittedAuthorizationMaterials") {
        return "BlockedNoSubmittedAuthorizationMaterials"
    }

    if (-not $MaterialsValid -or -not $CredentialWindowComplete -or $State -eq "BlockedInvalidAuthorizationMaterials") {
        return "BlockedInvalidAuthorizationMaterials"
    }

    return "ReadyForCredentialWindowPlanning"
}

$results = @()

if (-not $SkipScopeGuard) {
    $results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
    }
}

$results += Invoke-Step -Name "P17.9 Authorization Template Inheritance Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-authorization-template-p17_9-scope.md",
        ".\docs\enterprise-limited-pilot-authorization-template-p17_9-package.md",
        ".\docs\enterprise-limited-pilot-authorization-template-p17_9-latest.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P17.9 inheritance document is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"
    foreach ($marker in @(
        "P17.9",
        "BlockedByMissingAuthorizationMaterials",
        "ExecutionPermission: not granted",
        "MissingExplicitExecutionRequest",
        "ReadyForCredentialWindowPlanning",
        "query_cloud_data_readonly",
        "does not execute"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P17.9 inheritance is missing marker: $marker"
        }
    }

    "P17.9 remains a non-executing authorization template and blocking ledger."
}

$results += Invoke-Step -Name "P18.0 Scope And Authorization Intake Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-authorization-intake-p18_0-scope.md",
        ".\docs\enterprise-limited-pilot-authorization-intake-p18_0-package.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P18.0 document is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"
    foreach ($marker in @(
        "P18.0",
        "Authorization Intake",
        "Credential Window",
        "BlockedNoSubmittedAuthorizationMaterials",
        "BlockedInvalidAuthorizationMaterials",
        "BlockedUnsafeCredentialMaterial",
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
            throw "P18.0 material is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P18.0 material"
    "P18.0 scope and authorization intake package markers passed."
}

$submittedMaterials = New-SubmittedAuthorizationMaterials
$credentialWindow = New-CredentialWindowPreparation

$results += Invoke-Step -Name "P18.0 Go/No-Go Matrix Check" -Script {
    $defaultMaterials = New-SubmittedAuthorizationMaterials
    $defaultWindow = New-CredentialWindowPreparation
    $completeMaterials = New-CompleteSubmittedAuthorizationMaterials
    $completeWindow = New-CompleteCredentialWindowPreparation
    $invalidMaterials = New-CompleteSubmittedAuthorizationMaterials
    $invalidMaterials.EndpointAllowlist = @("devices", "recipe_versions")
    $unsafeMaterials = New-CompleteSubmittedAuthorizationMaterials
    $unsafeMaterials.ContainsRealCredentialMaterial = $true

    $defaultDecision = Get-GoNoGoDecision -State "BlockedNoSubmittedAuthorizationMaterials" -MaterialsSubmitted $defaultMaterials.Submitted -MaterialsValid (Test-SubmittedAuthorizationMaterialsValid -Materials $defaultMaterials) -CredentialWindowComplete (Test-CredentialWindowPreparationComplete -CredentialWindow $defaultWindow) -HasUnsafeCredentialMaterial (Test-HasUnsafeCredentialMaterial -Materials $defaultMaterials -CredentialWindow $defaultWindow) -HasAcceptanceFailures $false
    $invalidDecision = Get-GoNoGoDecision -State "ReadyForCredentialWindowPlanning" -MaterialsSubmitted $invalidMaterials.Submitted -MaterialsValid (Test-SubmittedAuthorizationMaterialsValid -Materials $invalidMaterials) -CredentialWindowComplete (Test-CredentialWindowPreparationComplete -CredentialWindow $completeWindow) -HasUnsafeCredentialMaterial (Test-HasUnsafeCredentialMaterial -Materials $invalidMaterials -CredentialWindow $completeWindow) -HasAcceptanceFailures $false
    $unsafeDecision = Get-GoNoGoDecision -State "ReadyForCredentialWindowPlanning" -MaterialsSubmitted $unsafeMaterials.Submitted -MaterialsValid (Test-SubmittedAuthorizationMaterialsValid -Materials $unsafeMaterials) -CredentialWindowComplete (Test-CredentialWindowPreparationComplete -CredentialWindow $completeWindow) -HasUnsafeCredentialMaterial (Test-HasUnsafeCredentialMaterial -Materials $unsafeMaterials -CredentialWindow $completeWindow) -HasAcceptanceFailures $false
    $readyDecision = Get-GoNoGoDecision -State "ReadyForCredentialWindowPlanning" -MaterialsSubmitted $completeMaterials.Submitted -MaterialsValid (Test-SubmittedAuthorizationMaterialsValid -Materials $completeMaterials) -CredentialWindowComplete (Test-CredentialWindowPreparationComplete -CredentialWindow $completeWindow) -HasUnsafeCredentialMaterial (Test-HasUnsafeCredentialMaterial -Materials $completeMaterials -CredentialWindow $completeWindow) -HasAcceptanceFailures $false
    $acceptanceFailure = Get-GoNoGoDecision -State "ReadyForCredentialWindowPlanning" -MaterialsSubmitted $completeMaterials.Submitted -MaterialsValid $true -CredentialWindowComplete $true -HasUnsafeCredentialMaterial $false -HasAcceptanceFailures $true

    if ($defaultDecision -ne "BlockedNoSubmittedAuthorizationMaterials") {
        throw "Default state mapped to unexpected result: $defaultDecision"
    }
    if ($invalidDecision -ne "BlockedInvalidAuthorizationMaterials") {
        throw "Invalid material mapped to unexpected result: $invalidDecision"
    }
    if ($unsafeDecision -ne "BlockedUnsafeCredentialMaterial") {
        throw "Unsafe material mapped to unexpected result: $unsafeDecision"
    }
    if ($readyDecision -ne "ReadyForCredentialWindowPlanning") {
        throw "Complete material mapped to unexpected result: $readyDecision"
    }
    if ($acceptanceFailure -ne "BlockedByAcceptanceFailure") {
        throw "Acceptance failure mapped to unexpected result: $acceptanceFailure"
    }

    "Go/No-Go matrix passed: default blocks missing materials, invalid material blocks, unsafe material blocks, and readiness stays credential-window planning only."
}

$results += Invoke-Step -Name "P18.0 Authorization Material Validation Check" -Script {
    $missing = New-SubmittedAuthorizationMaterials
    if (Test-SubmittedAuthorizationMaterialsValid -Materials $missing) {
        throw "Missing submitted materials unexpectedly passed validation."
    }

    $complete = New-CompleteSubmittedAuthorizationMaterials
    if (-not (Test-SubmittedAuthorizationMaterialsValid -Materials $complete)) {
        throw "Complete submitted materials did not pass validation."
    }

    $badUserCount = New-CompleteSubmittedAuthorizationMaterials
    $badUserCount.PilotUsers = @($badUserCount.PilotUsers[0..2])
    if (Test-SubmittedAuthorizationMaterialsValid -Materials $badUserCount) {
        throw "Pilot user count outside 5-10 unexpectedly passed validation."
    }

    $badRange = New-CompleteSubmittedAuthorizationMaterials
    $badRange.TimeRangeDays = 8
    if (Test-SubmittedAuthorizationMaterialsValid -Materials $badRange) {
        throw "Time range above last 7 days unexpectedly passed validation."
    }

    $badRows = New-CompleteSubmittedAuthorizationMaterials
    $badRows.MaxRows = 51
    if (Test-SubmittedAuthorizationMaterialsValid -Materials $badRows) {
        throw "maxRows above 50 unexpectedly passed validation."
    }

    "Authorization material validation covers missing material, required fields, 5-10 pilot users, endpoint allowlist, last-7-days boundary, and maxRows 50."
}

$results += Invoke-Step -Name "P18.0 Credential Window Preparation Check" -Script {
    $defaultWindow = New-CredentialWindowPreparation
    if (Test-CredentialWindowPreparationComplete -CredentialWindow $defaultWindow) {
        throw "Placeholder credential window unexpectedly passed completeness."
    }

    $completeWindow = New-CompleteCredentialWindowPreparation
    if (-not (Test-CredentialWindowPreparationComplete -CredentialWindow $completeWindow)) {
        throw "Complete credential window did not pass validation."
    }

    $unsafeWindow = New-CompleteCredentialWindowPreparation
    $unsafeWindow.RealCredentialValidated = $true
    if (-not (Test-HasUnsafeCredentialMaterial -Materials (New-CompleteSubmittedAuthorizationMaterials) -CredentialWindow $unsafeWindow)) {
        throw "Credential validation flag did not trigger unsafe material detection."
    }

    $json = $completeWindow | ConvertTo-Json -Depth 5
    Assert-NoUnsafeReportContent -Content $json -Name "P18.0 credential window"
    "Credential window preparation records only responsibility, approval, configuration window, rollback, and emergency stop verification placeholders."
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

$results += Invoke-Step -Name "P18.0 No Execution Claim Check" -Script {
    $combined = @(
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-authorization-intake-p18_0-scope.md" -Raw
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-authorization-intake-p18_0-package.md" -Raw
    ) -join "`n"

    foreach ($forbidden in @(
        "ReadyForLimitedPilotExecution",
        "Real Pilot executed",
        "Real endpoint configured",
        "Real token configured",
        "query_cloud_data_readonly enabled",
        "GA Permission: granted",
        "ExecutionPermission: granted",
        "Credential configured: true"
    )) {
        if ($combined -match [regex]::Escape($forbidden)) {
            throw "P18.0 material contains forbidden execution marker: $forbidden"
        }
    }

    "P18.0 material records authorization intake and credential-window preparation only and does not claim execution."
}

$externalReviewState = Get-ExternalReviewState
$prSummary = Get-PrSummary
$localHead = (git rev-parse HEAD 2>$null | Out-String).Trim()
$branch = (git branch --show-current 2>$null | Out-String).Trim()
$failedResults = @($results | Where-Object { -not $_.Succeeded })
$hasFailures = $failedResults.Count -gt 0
$materialsSubmitted = $submittedMaterials.Submitted
$materialsValid = Test-SubmittedAuthorizationMaterialsValid -Materials $submittedMaterials
$credentialWindowComplete = Test-CredentialWindowPreparationComplete -CredentialWindow $credentialWindow
$hasUnsafeCredentialMaterial = Test-HasUnsafeCredentialMaterial -Materials $submittedMaterials -CredentialWindow $credentialWindow
$goNoGo = Get-GoNoGoDecision -State $GateState -MaterialsSubmitted $materialsSubmitted -MaterialsValid $materialsValid -CredentialWindowComplete $credentialWindowComplete -HasUnsafeCredentialMaterial $hasUnsafeCredentialMaterial -HasAcceptanceFailures $hasFailures
$executionPermission = if ($goNoGo -eq "ReadyForCredentialWindowPlanning") { "credential-window-planning-only" } else { "not granted" }

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Limited Pilot Authorization Intake P18.0 Acceptance")
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
$reportLines.Add("- Boundary: P18.0 validates submitted authorization materials and prepares credential-window planning only; it does not execute a real Pilot and is not GA")
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
$reportLines.Add("## Authorization Intake Decision")
$reportLines.Add("")
$reportLines.Add("- State: $GateState")
$reportLines.Add("- Go/No-Go: $goNoGo")
$reportLines.Add("- Execution permission: $executionPermission")
$reportLines.Add("- Review evidence: 5.5 Pro $externalReviewState")
$reportLines.Add("")
$reportLines.Add("## Submitted Authorization Materials")
$reportLines.Add("")
$reportLines.Add("- Submitted: $($submittedMaterials.Submitted)")
$reportLines.Add("- Pilot Window: $($submittedMaterials.PilotWindowName)")
$reportLines.Add("- Start: $($submittedMaterials.StartAt)")
$reportLines.Add("- End: $($submittedMaterials.EndAt)")
$reportLines.Add("- Owner: $($submittedMaterials.Owner)")
$reportLines.Add("- Approver: $($submittedMaterials.Approver)")
$reportLines.Add("- Executor: $($submittedMaterials.Executor)")
$reportLines.Add("- Rollback owner: $($submittedMaterials.RollbackOwner)")
$reportLines.Add("- Emergency stop owner: $($submittedMaterials.EmergencyStopOwner)")
$reportLines.Add("- Pilot user count: $($submittedMaterials.PilotUsers.Count)")
$reportLines.Add("- Pilot user range: 5-10")
$reportLines.Add("- Endpoint allowlist: $($submittedMaterials.EndpointAllowlist -join ', ')")
$reportLines.Add("- Time range days: $($submittedMaterials.TimeRangeDays)")
$reportLines.Add("- maxRows: $($submittedMaterials.MaxRows)")
$reportLines.Add("- Tool Approval required: $($submittedMaterials.RequiresToolApproval)")
$reportLines.Add("- Final Approval required: $($submittedMaterials.RequiresFinalApproval)")
$reportLines.Add("- Output boundary: $($submittedMaterials.OutputBoundary)")
$reportLines.Add("- Operations ledger policy: $($submittedMaterials.OperationsLedgerPolicy)")
$reportLines.Add("- Real credential material present: $($submittedMaterials.ContainsRealCredentialMaterial)")
$reportLines.Add("- Raw payload material present: $($submittedMaterials.ContainsRawPayload)")
$reportLines.Add("- Runtime row material present: $($submittedMaterials.ContainsRuntimeRows)")
$reportLines.Add("- Full SQL material present: $($submittedMaterials.ContainsFullSql)")
$reportLines.Add("- Sensitive context material present: $($submittedMaterials.ContainsSensitiveContext)")
$reportLines.Add("")
$reportLines.Add("## Credential Window Preparation")
$reportLines.Add("")
$reportLines.Add("- Configuration owner: $($credentialWindow.ConfigurationOwner)")
$reportLines.Add("- Custodian: $($credentialWindow.Custodian)")
$reportLines.Add("- Approver: $($credentialWindow.Approver)")
$reportLines.Add("- Configuration window: $($credentialWindow.ConfigurationWindow)")
$reportLines.Add("- Rollback requirement: $($credentialWindow.RollbackRequirement)")
$reportLines.Add("- Emergency stop online verification: $($credentialWindow.EmergencyStopOnlineVerification)")
$reportLines.Add("- Credential status: $($credentialWindow.CredentialStatus)")
$reportLines.Add("- Real credential read: $($credentialWindow.RealCredentialRead)")
$reportLines.Add("- Real credential written: $($credentialWindow.RealCredentialWritten)")
$reportLines.Add("- Real credential displayed: $($credentialWindow.RealCredentialDisplayed)")
$reportLines.Add("- Real credential validated: $($credentialWindow.RealCredentialValidated)")
$reportLines.Add("- Real endpoint called: $($credentialWindow.RealEndpointCalled)")
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
$reportLines.Add("- P18.0 does not execute a real Pilot.")
$reportLines.Add("- Default output is BlockedNoSubmittedAuthorizationMaterials because user-filled and signed authorization materials have not been received.")
$reportLines.Add("- ReadyForCredentialWindowPlanning only means the materials are complete enough for a later credential-window planning stage.")
$reportLines.Add("- Credential-window planning is not credential configuration, credential validation, endpoint testing, or production query execution.")
$reportLines.Add("- Real endpoint/token use remains outside P18.0 and requires a later explicit execution stage.")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

$reportContent = $reportLines -join [Environment]::NewLine
Assert-NoUnsafeReportContent -Content $reportContent -Name "P18.0 report"
Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Limited Pilot Authorization Intake P18.0 acceptance report written to: $ReportPath"

if ($hasFailures) {
    exit 1
}
