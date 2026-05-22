[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-limited-pilot-authorization-submission-p18_2-latest.md",
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

function New-LimitedPilotAuthorizationSubmission {
    param(
        [bool]$Submitted,
        [int]$PilotUserCount = 5,
        [string[]]$EndpointAllowlist = @("devices", "capacity_summary", "device_logs", "pass_station_records"),
        [int]$TimeRangeDays = 7,
        [int]$MaxRows = 50,
        [bool]$MissingApprover = $false,
        [bool]$MissingExecutor = $false,
        [bool]$MissingRollbackOwner = $false,
        [bool]$MissingEmergencyStopOwner = $false,
        [bool]$MissingCredentialWindow = $false,
        [bool]$UnsafeCredentialMaterial = $false,
        [bool]$UnsafeRawPayload = $false,
        [bool]$UnsafeRuntimeRows = $false,
        [bool]$UnsafeFullSql = $false,
        [bool]$UnsafeSensitiveContext = $false
    )

    $pilotUsers = @()
    for ($i = 1; $i -le $PilotUserCount; $i++) {
        $pilotUsers += [pscustomobject]@{
            User            = "pilot-user-$('{0:d2}' -f $i)"
            Role            = if ($i -eq 1) { "Approver" } else { "ReadonlyPilotUser" }
            Department      = "PilotDepartment"
            PermissionScope = "CloudReadonlyLimitedPilot"
            ApprovalStatus  = "Approved"
        }
    }

    [pscustomobject]@{
        SubmissionType                  = "LimitedPilotAuthorizationSubmission"
        Submitted                       = $Submitted
        PilotWindowName                 = if ($Submitted) { "limited production readonly Pilot" } else { "ToBeSubmitted" }
        StartAt                         = if ($Submitted) { "ApprovedWindowStart" } else { "ToBeSubmitted" }
        EndAt                           = if ($Submitted) { "ApprovedWindowEnd" } else { "ToBeSubmitted" }
        Owner                           = if ($Submitted) { "PilotOwner" } else { "ToBeSubmitted" }
        Approver                        = if (-not $Submitted -or $MissingApprover) { "ToBeSubmitted" } else { "PilotApprover" }
        Executor                        = if (-not $Submitted -or $MissingExecutor) { "ToBeSubmitted" } else { "PilotExecutor" }
        RollbackOwner                   = if (-not $Submitted -or $MissingRollbackOwner) { "ToBeSubmitted" } else { "PilotRollbackOwner" }
        EmergencyStopOwner              = if (-not $Submitted -or $MissingEmergencyStopOwner) { "ToBeSubmitted" } else { "PilotEmergencyStopOwner" }
        PilotUsers                      = $pilotUsers
        EndpointAllowlist               = $EndpointAllowlist
        TimeRangeDays                   = $TimeRangeDays
        MaxRows                         = $MaxRows
        RequiresToolApproval            = $true
        RequiresFinalApproval           = $true
        OutputBoundary                  = "draft artifacts, Final Approval, final lock"
        OperationsLedgerPolicy          = "hash-only"
        CredentialConfigurationOwner    = if ($Submitted) { "CredentialConfigurationOwner" } else { "ToBeSubmitted" }
        CredentialCustodian             = if ($Submitted) { "CredentialCustodian" } else { "ToBeSubmitted" }
        CredentialApprover              = if ($Submitted) { "CredentialApprover" } else { "ToBeSubmitted" }
        CredentialConfigurationWindow   = if (-not $Submitted -or $MissingCredentialWindow) { "ToBeSubmitted" } else { "ApprovedConfigurationWindow" }
        CredentialRollbackRequirement   = if ($Submitted) { "CredentialRollbackRequired" } else { "ToBeSubmitted" }
        EmergencyStopVerification       = if ($Submitted) { "OnlineVerificationRequired" } else { "ToBeSubmitted" }
        ContainsRealCredentialMaterial  = $UnsafeCredentialMaterial
        ContainsRawPayload              = $UnsafeRawPayload
        ContainsRuntimeRows             = $UnsafeRuntimeRows
        ContainsFullSql                 = $UnsafeFullSql
        ContainsSensitiveContext        = $UnsafeSensitiveContext
    }
}

function Test-HasUnsafeSubmissionMaterial {
    param([Parameter(Mandatory = $true)]$Submission)

    return $Submission.ContainsRealCredentialMaterial `
        -or $Submission.ContainsRawPayload `
        -or $Submission.ContainsRuntimeRows `
        -or $Submission.ContainsFullSql `
        -or $Submission.ContainsSensitiveContext
}

function Test-LimitedPilotAuthorizationSubmissionValid {
    param([Parameter(Mandatory = $true)]$Submission)

    if (-not $Submission.Submitted) {
        return $false
    }

    if ($Submission.SubmissionType -ne "LimitedPilotAuthorizationSubmission") {
        return $false
    }

    foreach ($field in @("PilotWindowName", "StartAt", "EndAt", "Owner", "Approver", "Executor", "RollbackOwner", "EmergencyStopOwner", "OutputBoundary", "OperationsLedgerPolicy", "CredentialConfigurationOwner", "CredentialCustodian", "CredentialApprover", "CredentialConfigurationWindow", "CredentialRollbackRequirement", "EmergencyStopVerification")) {
        if ([string]::IsNullOrWhiteSpace([string]$Submission.$field) -or $Submission.$field -eq "ToBeSubmitted") {
            return $false
        }
    }

    if ($Submission.PilotUsers.Count -lt 5 -or $Submission.PilotUsers.Count -gt 10) {
        return $false
    }

    foreach ($user in $Submission.PilotUsers) {
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
    foreach ($endpoint in $Submission.EndpointAllowlist) {
        if ($allowedEndpoints -notcontains $endpoint) {
            return $false
        }
    }

    foreach ($endpoint in $allowedEndpoints) {
        if ($Submission.EndpointAllowlist -notcontains $endpoint) {
            return $false
        }
    }

    return $Submission.TimeRangeDays -le 7 `
        -and $Submission.MaxRows -le 50 `
        -and $Submission.RequiresToolApproval `
        -and $Submission.RequiresFinalApproval `
        -and $Submission.OperationsLedgerPolicy -eq "hash-only"
}

function Get-GoNoGoDecision {
    param(
        [string]$State,
        [bool]$Submitted,
        [bool]$Valid,
        [bool]$Unsafe,
        [bool]$HasAcceptanceFailures
    )

    if ($HasAcceptanceFailures) {
        return "BlockedByAcceptanceFailure"
    }

    if ($Unsafe -or $State -eq "BlockedUnsafeCredentialMaterial") {
        return "BlockedUnsafeCredentialMaterial"
    }

    if (-not $Submitted -or $State -eq "BlockedNoSubmittedAuthorizationMaterials") {
        return "BlockedNoSubmittedAuthorizationMaterials"
    }

    if (-not $Valid -or $State -eq "BlockedInvalidAuthorizationMaterials") {
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

$results += Invoke-Step -Name "P18.1 Fillable Template Inheritance Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-authorization-template-p18_1-scope.md",
        ".\docs\enterprise-limited-pilot-authorization-template-p18_1-package.md",
        ".\docs\enterprise-limited-pilot-authorization-template-p18_1-latest.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P18.1 inheritance document is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"
    foreach ($marker in @(
        "P18.1",
        "BlockedNoSubmittedAuthorizationMaterials",
        "ExecutionPermission: not granted",
        "Fillable Authorization Template",
        "ReadyForCredentialWindowPlanning",
        "query_cloud_data_readonly",
        "does not execute"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P18.1 inheritance is missing marker: $marker"
        }
    }

    "P18.1 remains a non-executing fillable template and offline validation gate."
}

$results += Invoke-Step -Name "P18.2 Scope And Submission Package Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-authorization-submission-p18_2-scope.md",
        ".\docs\enterprise-limited-pilot-authorization-submission-p18_2-package.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P18.2 document is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"
    foreach ($marker in @(
        "P18.2",
        "LimitedPilotAuthorizationSubmission",
        "machine validation",
        "BlockedNoSubmittedAuthorizationMaterials",
        "BlockedInvalidAuthorizationMaterials",
        "BlockedUnsafeCredentialMaterial",
        "ReadyForCredentialWindowPlanning",
        "Safe Submission Package",
        "Invalid Submission Package",
        "Unsafe Submission Package",
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
            throw "P18.2 material is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P18.2 material"
    "P18.2 scope and submission package markers passed."
}

$missingSubmission = New-LimitedPilotAuthorizationSubmission -Submitted $false
$safeSubmission = New-LimitedPilotAuthorizationSubmission -Submitted $true
$invalidSubmission = New-LimitedPilotAuthorizationSubmission -Submitted $true -PilotUserCount 4 -EndpointAllowlist @("devices", "recipe_versions") -MaxRows 51 -MissingApprover $true
$unsafeSubmission = New-LimitedPilotAuthorizationSubmission -Submitted $true -UnsafeCredentialMaterial $true -UnsafeRawPayload $true -UnsafeRuntimeRows $true -UnsafeFullSql $true

$results += Invoke-Step -Name "P18.2 Submission Format Completeness Check" -Script {
    foreach ($field in @("PilotWindowName", "StartAt", "EndAt", "Owner", "Approver", "Executor", "RollbackOwner", "EmergencyStopOwner", "PilotUsers", "EndpointAllowlist", "TimeRangeDays", "MaxRows", "OutputBoundary", "OperationsLedgerPolicy", "CredentialConfigurationOwner", "CredentialCustodian", "CredentialApprover", "CredentialConfigurationWindow", "CredentialRollbackRequirement", "EmergencyStopVerification")) {
        if (-not ($safeSubmission.PSObject.Properties.Name -contains $field)) {
            throw "Submission format is missing field: $field"
        }
    }

    if (-not (Test-LimitedPilotAuthorizationSubmissionValid -Submission $safeSubmission)) {
        throw "Safe submission format did not validate."
    }

    if (Test-HasUnsafeSubmissionMaterial -Submission $safeSubmission) {
        throw "Safe submission contains unsafe markers."
    }

    "Submission package includes Pilot Window, users, approval chain, executor, rollback, emergency stop, endpoints, data boundary, output boundary, and credential responsibility."
}

$results += Invoke-Step -Name "P18.2 Offline Machine Validation Matrix Check" -Script {
    $missingDecision = Get-GoNoGoDecision -State "BlockedNoSubmittedAuthorizationMaterials" -Submitted $missingSubmission.Submitted -Valid (Test-LimitedPilotAuthorizationSubmissionValid -Submission $missingSubmission) -Unsafe (Test-HasUnsafeSubmissionMaterial -Submission $missingSubmission) -HasAcceptanceFailures $false
    $invalidDecision = Get-GoNoGoDecision -State "ReadyForCredentialWindowPlanning" -Submitted $invalidSubmission.Submitted -Valid (Test-LimitedPilotAuthorizationSubmissionValid -Submission $invalidSubmission) -Unsafe (Test-HasUnsafeSubmissionMaterial -Submission $invalidSubmission) -HasAcceptanceFailures $false
    $unsafeDecision = Get-GoNoGoDecision -State "ReadyForCredentialWindowPlanning" -Submitted $unsafeSubmission.Submitted -Valid (Test-LimitedPilotAuthorizationSubmissionValid -Submission $unsafeSubmission) -Unsafe (Test-HasUnsafeSubmissionMaterial -Submission $unsafeSubmission) -HasAcceptanceFailures $false
    $safeDecision = Get-GoNoGoDecision -State "ReadyForCredentialWindowPlanning" -Submitted $safeSubmission.Submitted -Valid (Test-LimitedPilotAuthorizationSubmissionValid -Submission $safeSubmission) -Unsafe (Test-HasUnsafeSubmissionMaterial -Submission $safeSubmission) -HasAcceptanceFailures $false

    if ($missingDecision -ne "BlockedNoSubmittedAuthorizationMaterials") {
        throw "Missing submission mapped to unexpected result: $missingDecision"
    }
    if ($invalidDecision -ne "BlockedInvalidAuthorizationMaterials") {
        throw "Invalid submission mapped to unexpected result: $invalidDecision"
    }
    if ($unsafeDecision -ne "BlockedUnsafeCredentialMaterial") {
        throw "Unsafe submission mapped to unexpected result: $unsafeDecision"
    }
    if ($safeDecision -ne "ReadyForCredentialWindowPlanning") {
        throw "Safe submission mapped to unexpected result: $safeDecision"
    }

    "Machine validation matrix covers missing, invalid, unsafe, and safe submission outcomes without execution."
}

$results += Invoke-Step -Name "P18.2 Sample Submission Safety Check" -Script {
    $sampleJson = [pscustomobject]@{
        Safe    = $safeSubmission
        Invalid = $invalidSubmission
        Unsafe  = [pscustomobject]@{
            Submitted                      = $unsafeSubmission.Submitted
            ContainsRealCredentialMaterial = $unsafeSubmission.ContainsRealCredentialMaterial
            ContainsRawPayload             = $unsafeSubmission.ContainsRawPayload
            ContainsRuntimeRows            = $unsafeSubmission.ContainsRuntimeRows
            ContainsFullSql                = $unsafeSubmission.ContainsFullSql
            ContainsSensitiveContext       = $unsafeSubmission.ContainsSensitiveContext
        }
    } | ConvertTo-Json -Depth 7

    Assert-NoUnsafeReportContent -Content $sampleJson -Name "P18.2 sample set"
    "Sample submissions contain safe, invalid, and unsafe shapes without real credential values or production payload."
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

$results += Invoke-Step -Name "P18.2 No Execution Claim Check" -Script {
    $combined = @(
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-authorization-submission-p18_2-scope.md" -Raw
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-authorization-submission-p18_2-package.md" -Raw
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
            throw "P18.2 material contains forbidden execution marker: $forbidden"
        }
    }

    "P18.2 material records offline submission format and machine validation only and does not claim execution."
}

$externalReviewState = Get-ExternalReviewState
$prSummary = Get-PrSummary
$localHead = (git rev-parse HEAD 2>$null | Out-String).Trim()
$branch = (git branch --show-current 2>$null | Out-String).Trim()
$failedResults = @($results | Where-Object { -not $_.Succeeded })
$hasFailures = $failedResults.Count -gt 0
$goNoGo = Get-GoNoGoDecision -State $GateState -Submitted $missingSubmission.Submitted -Valid (Test-LimitedPilotAuthorizationSubmissionValid -Submission $missingSubmission) -Unsafe (Test-HasUnsafeSubmissionMaterial -Submission $missingSubmission) -HasAcceptanceFailures $hasFailures
$executionPermission = if ($goNoGo -eq "ReadyForCredentialWindowPlanning") { "credential-window-planning-only" } else { "not granted" }

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Limited Pilot Authorization Submission P18.2 Acceptance")
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
$reportLines.Add("- Boundary: P18.2 provides an offline authorization submission format and machine-validation gate only; it does not execute a real Pilot and is not GA")
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
$reportLines.Add("## Submission Gate Decision")
$reportLines.Add("")
$reportLines.Add("- State: $GateState")
$reportLines.Add("- Go/No-Go: $goNoGo")
$reportLines.Add("- Execution permission: $executionPermission")
$reportLines.Add("- Review evidence: 5.5 Pro $externalReviewState")
$reportLines.Add("")
$reportLines.Add("## LimitedPilotAuthorizationSubmission")
$reportLines.Add("")
$reportLines.Add("- Submission type: $($safeSubmission.SubmissionType)")
$reportLines.Add("- Pilot Window fields: PilotWindowName, StartAt, EndAt, Owner, Approver, Executor, RollbackOwner, EmergencyStopOwner")
$reportLines.Add("- Pilot user fields: User, Role, Department, PermissionScope, ApprovalStatus")
$reportLines.Add("- Pilot user range: 5-10")
$reportLines.Add("- Endpoint allowlist: devices, capacity_summary, device_logs, pass_station_records")
$reportLines.Add("- Time range days: 7")
$reportLines.Add("- maxRows: 50")
$reportLines.Add("- Tool Approval required: True")
$reportLines.Add("- Final Approval required: True")
$reportLines.Add("- Output boundary: draft artifacts, Final Approval, final lock")
$reportLines.Add("- Operations ledger policy: hash-only")
$reportLines.Add("- Credential responsibility fields: CredentialConfigurationOwner, CredentialCustodian, CredentialApprover, CredentialConfigurationWindow, CredentialRollbackRequirement")
$reportLines.Add("- Emergency stop verification field: EmergencyStopVerification")
$reportLines.Add("- Real credential material present: False")
$reportLines.Add("- Real endpoint call: False")
$reportLines.Add("- Real production artifact: False")
$reportLines.Add("")
$reportLines.Add("## Offline Submission Outcomes")
$reportLines.Add("")
$reportLines.Add("- Missing submission: BlockedNoSubmittedAuthorizationMaterials")
$reportLines.Add("- Invalid submission: BlockedInvalidAuthorizationMaterials")
$reportLines.Add("- Unsafe submission: BlockedUnsafeCredentialMaterial")
$reportLines.Add("- Safe submission: ReadyForCredentialWindowPlanning")
$reportLines.Add("- Safe submission user count: $($safeSubmission.PilotUsers.Count)")
$reportLines.Add("- Invalid submission user count: $($invalidSubmission.PilotUsers.Count)")
$reportLines.Add("- Unsafe submission credential marker present: $($unsafeSubmission.ContainsRealCredentialMaterial)")
$reportLines.Add("- Unsafe submission raw payload marker present: $($unsafeSubmission.ContainsRawPayload)")
$reportLines.Add("- Unsafe submission runtime row marker present: $($unsafeSubmission.ContainsRuntimeRows)")
$reportLines.Add("- Unsafe submission full SQL marker present: $($unsafeSubmission.ContainsFullSql)")
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
$reportLines.Add("- P18.2 does not execute a real Pilot.")
$reportLines.Add("- Default output is BlockedNoSubmittedAuthorizationMaterials because user-filled and signed authorization materials have not been received.")
$reportLines.Add("- ReadyForCredentialWindowPlanning only means the safe sample is complete enough for a later credential-window planning stage.")
$reportLines.Add("- Machine validation is not credential configuration, credential validation, endpoint testing, or production query execution.")
$reportLines.Add("- Real endpoint/token use remains outside P18.2 and requires a later explicit execution stage.")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

$reportContent = $reportLines -join [Environment]::NewLine
Assert-NoUnsafeReportContent -Content $reportContent -Name "P18.2 report"
Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Limited Pilot Authorization Submission P18.2 acceptance report written to: $ReportPath"

if ($hasFailures) {
    exit 1
}
