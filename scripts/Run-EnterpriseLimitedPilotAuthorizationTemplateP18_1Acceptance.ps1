[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-limited-pilot-authorization-template-p18_1-latest.md",
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

function New-FillableAuthorizationTemplate {
    [pscustomobject]@{
        PilotWindowFields       = @("Name", "StartAt", "EndAt", "Owner", "Approver", "Executor", "RollbackOwner", "EmergencyStopOwner")
        PilotUserFields         = @("User", "Role", "Department", "PermissionScope", "ApprovalStatus")
        PilotUserRange          = "5-10"
        EndpointAllowlist       = @("devices", "capacity_summary", "device_logs", "pass_station_records")
        TimeRangeDays           = 7
        MaxRows                 = 50
        RequiresToolApproval    = $true
        RequiresFinalApproval   = $true
        OutputBoundary          = "draft artifacts, Final Approval, final lock"
        OperationsLedgerPolicy  = "hash-only"
        CredentialFields        = @("ConfigurationOwner", "Custodian", "Approver", "ConfigurationWindow", "RollbackRequirement")
        RealCredentialMaterial  = $false
        RealEndpointCall        = $false
        RealProductionArtifact  = $false
    }
}

function New-AuthorizationSubmission {
    param(
        [bool]$Submitted,
        [int]$PilotUserCount = 5,
        [string[]]$EndpointAllowlist = @("devices", "capacity_summary", "device_logs", "pass_station_records"),
        [int]$TimeRangeDays = 7,
        [int]$MaxRows = 50,
        [bool]$MissingApprover = $false,
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
            Role            = if ($i -eq 1) { "Approver" } else { "Viewer" }
            Department      = "PilotDepartment"
            PermissionScope = "ReadonlyPilot"
            ApprovalStatus  = "Approved"
        }
    }

    [pscustomobject]@{
        Submitted                      = $Submitted
        PilotWindowName                = if ($Submitted) { "limited production readonly Pilot" } else { "ToBeSubmitted" }
        StartAt                        = if ($Submitted) { "ApprovedWindowStart" } else { "ToBeSubmitted" }
        EndAt                          = if ($Submitted) { "ApprovedWindowEnd" } else { "ToBeSubmitted" }
        Owner                          = if ($Submitted) { "PilotOwner" } else { "ToBeSubmitted" }
        Approver                       = if (-not $Submitted -or $MissingApprover) { "ToBeSubmitted" } else { "PilotApprover" }
        Executor                       = if ($Submitted) { "PilotExecutor" } else { "ToBeSubmitted" }
        RollbackOwner                  = if ($Submitted) { "PilotRollbackOwner" } else { "ToBeSubmitted" }
        EmergencyStopOwner             = if ($Submitted) { "PilotEmergencyStopOwner" } else { "ToBeSubmitted" }
        PilotUsers                     = $pilotUsers
        EndpointAllowlist              = $EndpointAllowlist
        TimeRangeDays                  = $TimeRangeDays
        MaxRows                        = $MaxRows
        RequiresToolApproval           = $true
        RequiresFinalApproval          = $true
        OutputBoundary                 = "draft artifacts, Final Approval, final lock"
        OperationsLedgerPolicy         = "hash-only"
        CredentialConfigurationOwner   = if ($Submitted) { "CredentialConfigurationOwner" } else { "ToBeSubmitted" }
        CredentialCustodian            = if ($Submitted) { "CredentialCustodian" } else { "ToBeSubmitted" }
        CredentialApprover             = if ($Submitted) { "CredentialApprover" } else { "ToBeSubmitted" }
        CredentialConfigurationWindow  = if ($Submitted) { "ApprovedConfigurationWindow" } else { "ToBeSubmitted" }
        CredentialRollbackRequirement  = if ($Submitted) { "CredentialRollbackRequired" } else { "ToBeSubmitted" }
        ContainsRealCredentialMaterial = $UnsafeCredentialMaterial
        ContainsRawPayload             = $UnsafeRawPayload
        ContainsRuntimeRows            = $UnsafeRuntimeRows
        ContainsFullSql                = $UnsafeFullSql
        ContainsSensitiveContext       = $UnsafeSensitiveContext
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

function Test-AuthorizationSubmissionValid {
    param([Parameter(Mandatory = $true)]$Submission)

    if (-not $Submission.Submitted) {
        return $false
    }

    foreach ($field in @("PilotWindowName", "StartAt", "EndAt", "Owner", "Approver", "Executor", "RollbackOwner", "EmergencyStopOwner", "OutputBoundary", "OperationsLedgerPolicy", "CredentialConfigurationOwner", "CredentialCustodian", "CredentialApprover", "CredentialConfigurationWindow", "CredentialRollbackRequirement")) {
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

$results += Invoke-Step -Name "P18.0 Authorization Intake Inheritance Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-authorization-intake-p18_0-scope.md",
        ".\docs\enterprise-limited-pilot-authorization-intake-p18_0-package.md",
        ".\docs\enterprise-limited-pilot-authorization-intake-p18_0-latest.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P18.0 inheritance document is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"
    foreach ($marker in @(
        "P18.0",
        "BlockedNoSubmittedAuthorizationMaterials",
        "ExecutionPermission: not granted",
        "Credential Window",
        "ReadyForCredentialWindowPlanning",
        "query_cloud_data_readonly",
        "does not execute"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P18.0 inheritance is missing marker: $marker"
        }
    }

    "P18.0 remains a non-executing authorization intake and credential-window preparation gate."
}

$results += Invoke-Step -Name "P18.1 Scope And Fillable Template Check" -Script {
    $paths = @(
        ".\docs\enterprise-limited-pilot-authorization-template-p18_1-scope.md",
        ".\docs\enterprise-limited-pilot-authorization-template-p18_1-package.md"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            throw "P18.1 document is missing: $path"
        }
    }

    $combined = ($paths | ForEach-Object { Get-Content -LiteralPath $_ -Raw }) -join "`n"
    foreach ($marker in @(
        "P18.1",
        "Fillable Authorization Template",
        "Offline Submission Validation",
        "BlockedNoSubmittedAuthorizationMaterials",
        "BlockedInvalidAuthorizationMaterials",
        "BlockedUnsafeCredentialMaterial",
        "ReadyForCredentialWindowPlanning",
        "Safe Sample",
        "Invalid Sample",
        "Unsafe Sample",
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
            throw "P18.1 material is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P18.1 material"
    "P18.1 scope and fillable template package markers passed."
}

$template = New-FillableAuthorizationTemplate
$defaultSubmission = New-AuthorizationSubmission -Submitted $false
$safeSample = New-AuthorizationSubmission -Submitted $true
$invalidSample = New-AuthorizationSubmission -Submitted $true -PilotUserCount 4 -EndpointAllowlist @("devices", "recipe_versions") -MaxRows 51 -MissingApprover $true
$unsafeSample = New-AuthorizationSubmission -Submitted $true -UnsafeCredentialMaterial $true -UnsafeRawPayload $true -UnsafeRuntimeRows $true -UnsafeFullSql $true

$results += Invoke-Step -Name "P18.1 Fillable Template Completeness Check" -Script {
    foreach ($field in @("Name", "StartAt", "EndAt", "Owner", "Approver", "Executor", "RollbackOwner", "EmergencyStopOwner")) {
        if ($template.PilotWindowFields -notcontains $field) {
            throw "Fillable template missing Pilot Window field: $field"
        }
    }

    foreach ($field in @("User", "Role", "Department", "PermissionScope", "ApprovalStatus")) {
        if ($template.PilotUserFields -notcontains $field) {
            throw "Fillable template missing Pilot user field: $field"
        }
    }

    foreach ($endpoint in @("devices", "capacity_summary", "device_logs", "pass_station_records")) {
        if ($template.EndpointAllowlist -notcontains $endpoint) {
            throw "Fillable template missing endpoint: $endpoint"
        }
    }

    foreach ($field in @("ConfigurationOwner", "Custodian", "Approver", "ConfigurationWindow", "RollbackRequirement")) {
        if ($template.CredentialFields -notcontains $field) {
            throw "Fillable template missing credential responsibility field: $field"
        }
    }

    if ($template.TimeRangeDays -ne 7 -or $template.MaxRows -ne 50 -or -not $template.RequiresToolApproval -or -not $template.RequiresFinalApproval -or $template.OperationsLedgerPolicy -ne "hash-only") {
        throw "Fillable template boundary values are incorrect."
    }

    if ($template.RealCredentialMaterial -or $template.RealEndpointCall -or $template.RealProductionArtifact) {
        throw "Fillable template should not contain execution material."
    }

    "Fillable template includes Pilot Window, 5-10 pilot users, approval chain, endpoint allowlist, data boundary, output boundary, credential responsibility, rollback, and emergency stop."
}

$results += Invoke-Step -Name "P18.1 Offline Validation Matrix Check" -Script {
    $missingDecision = Get-GoNoGoDecision -State "BlockedNoSubmittedAuthorizationMaterials" -Submitted $defaultSubmission.Submitted -Valid (Test-AuthorizationSubmissionValid -Submission $defaultSubmission) -Unsafe (Test-HasUnsafeSubmissionMaterial -Submission $defaultSubmission) -HasAcceptanceFailures $false
    $invalidDecision = Get-GoNoGoDecision -State "ReadyForCredentialWindowPlanning" -Submitted $invalidSample.Submitted -Valid (Test-AuthorizationSubmissionValid -Submission $invalidSample) -Unsafe (Test-HasUnsafeSubmissionMaterial -Submission $invalidSample) -HasAcceptanceFailures $false
    $unsafeDecision = Get-GoNoGoDecision -State "ReadyForCredentialWindowPlanning" -Submitted $unsafeSample.Submitted -Valid (Test-AuthorizationSubmissionValid -Submission $unsafeSample) -Unsafe (Test-HasUnsafeSubmissionMaterial -Submission $unsafeSample) -HasAcceptanceFailures $false
    $safeDecision = Get-GoNoGoDecision -State "ReadyForCredentialWindowPlanning" -Submitted $safeSample.Submitted -Valid (Test-AuthorizationSubmissionValid -Submission $safeSample) -Unsafe (Test-HasUnsafeSubmissionMaterial -Submission $safeSample) -HasAcceptanceFailures $false
    $acceptanceFailure = Get-GoNoGoDecision -State "ReadyForCredentialWindowPlanning" -Submitted $safeSample.Submitted -Valid $true -Unsafe $false -HasAcceptanceFailures $true

    if ($missingDecision -ne "BlockedNoSubmittedAuthorizationMaterials") {
        throw "Missing submission mapped to unexpected result: $missingDecision"
    }
    if ($invalidDecision -ne "BlockedInvalidAuthorizationMaterials") {
        throw "Invalid sample mapped to unexpected result: $invalidDecision"
    }
    if ($unsafeDecision -ne "BlockedUnsafeCredentialMaterial") {
        throw "Unsafe sample mapped to unexpected result: $unsafeDecision"
    }
    if ($safeDecision -ne "ReadyForCredentialWindowPlanning") {
        throw "Safe sample mapped to unexpected result: $safeDecision"
    }
    if ($acceptanceFailure -ne "BlockedByAcceptanceFailure") {
        throw "Acceptance failure mapped to unexpected result: $acceptanceFailure"
    }

    "Offline validation matrix covers missing, invalid, unsafe, and safe material outcomes without execution."
}

$results += Invoke-Step -Name "P18.1 Sample Set Safety Check" -Script {
    if (-not (Test-AuthorizationSubmissionValid -Submission $safeSample)) {
        throw "Safe sample did not pass material validation."
    }
    if (Test-HasUnsafeSubmissionMaterial -Submission $safeSample) {
        throw "Safe sample unexpectedly contains unsafe material markers."
    }
    if (Test-AuthorizationSubmissionValid -Submission $invalidSample) {
        throw "Invalid sample unexpectedly passed material validation."
    }
    if (-not (Test-HasUnsafeSubmissionMaterial -Submission $unsafeSample)) {
        throw "Unsafe sample did not trigger unsafe material markers."
    }

    $sampleJson = [pscustomobject]@{
        Safe    = $safeSample
        Invalid = $invalidSample
        Unsafe  = [pscustomobject]@{
            Submitted                      = $unsafeSample.Submitted
            ContainsRealCredentialMaterial = $unsafeSample.ContainsRealCredentialMaterial
            ContainsRawPayload             = $unsafeSample.ContainsRawPayload
            ContainsRuntimeRows            = $unsafeSample.ContainsRuntimeRows
            ContainsFullSql                = $unsafeSample.ContainsFullSql
            ContainsSensitiveContext       = $unsafeSample.ContainsSensitiveContext
        }
    } | ConvertTo-Json -Depth 7

    Assert-NoUnsafeReportContent -Content $sampleJson -Name "P18.1 sample set"
    "Sample set contains safe, invalid, and unsafe shapes without real credential values or production payload."
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

$results += Invoke-Step -Name "P18.1 No Execution Claim Check" -Script {
    $combined = @(
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-authorization-template-p18_1-scope.md" -Raw
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-authorization-template-p18_1-package.md" -Raw
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
            throw "P18.1 material contains forbidden execution marker: $forbidden"
        }
    }

    "P18.1 material records fillable template and offline validation only and does not claim execution."
}

$externalReviewState = Get-ExternalReviewState
$prSummary = Get-PrSummary
$localHead = (git rev-parse HEAD 2>$null | Out-String).Trim()
$branch = (git branch --show-current 2>$null | Out-String).Trim()
$failedResults = @($results | Where-Object { -not $_.Succeeded })
$hasFailures = $failedResults.Count -gt 0
$goNoGo = Get-GoNoGoDecision -State $GateState -Submitted $defaultSubmission.Submitted -Valid (Test-AuthorizationSubmissionValid -Submission $defaultSubmission) -Unsafe (Test-HasUnsafeSubmissionMaterial -Submission $defaultSubmission) -HasAcceptanceFailures $hasFailures
$executionPermission = if ($goNoGo -eq "ReadyForCredentialWindowPlanning") { "credential-window-planning-only" } else { "not granted" }

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Limited Pilot Fillable Authorization Template P18.1 Acceptance")
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
$reportLines.Add("- Boundary: P18.1 provides a fillable authorization template and offline validation only; it does not execute a real Pilot and is not GA")
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
$reportLines.Add("## Fillable Template Decision")
$reportLines.Add("")
$reportLines.Add("- State: $GateState")
$reportLines.Add("- Go/No-Go: $goNoGo")
$reportLines.Add("- Execution permission: $executionPermission")
$reportLines.Add("- Review evidence: 5.5 Pro $externalReviewState")
$reportLines.Add("")
$reportLines.Add("## Fillable Template")
$reportLines.Add("")
$reportLines.Add("- Pilot Window fields: $($template.PilotWindowFields -join ', ')")
$reportLines.Add("- Pilot user fields: $($template.PilotUserFields -join ', ')")
$reportLines.Add("- Pilot user range: $($template.PilotUserRange)")
$reportLines.Add("- Endpoint allowlist: $($template.EndpointAllowlist -join ', ')")
$reportLines.Add("- Time range days: $($template.TimeRangeDays)")
$reportLines.Add("- maxRows: $($template.MaxRows)")
$reportLines.Add("- Tool Approval required: $($template.RequiresToolApproval)")
$reportLines.Add("- Final Approval required: $($template.RequiresFinalApproval)")
$reportLines.Add("- Output boundary: $($template.OutputBoundary)")
$reportLines.Add("- Operations ledger policy: $($template.OperationsLedgerPolicy)")
$reportLines.Add("- Credential responsibility fields: $($template.CredentialFields -join ', ')")
$reportLines.Add("- Real credential material present: $($template.RealCredentialMaterial)")
$reportLines.Add("- Real endpoint call: $($template.RealEndpointCall)")
$reportLines.Add("- Real production artifact: $($template.RealProductionArtifact)")
$reportLines.Add("")
$reportLines.Add("## Offline Sample Outcomes")
$reportLines.Add("")
$reportLines.Add("- Missing material sample: BlockedNoSubmittedAuthorizationMaterials")
$reportLines.Add("- Invalid sample: BlockedInvalidAuthorizationMaterials")
$reportLines.Add("- Unsafe sample: BlockedUnsafeCredentialMaterial")
$reportLines.Add("- Safe sample: ReadyForCredentialWindowPlanning")
$reportLines.Add("- Safe sample user count: $($safeSample.PilotUsers.Count)")
$reportLines.Add("- Invalid sample user count: $($invalidSample.PilotUsers.Count)")
$reportLines.Add("- Unsafe sample credential marker present: $($unsafeSample.ContainsRealCredentialMaterial)")
$reportLines.Add("- Unsafe sample raw payload marker present: $($unsafeSample.ContainsRawPayload)")
$reportLines.Add("- Unsafe sample runtime row marker present: $($unsafeSample.ContainsRuntimeRows)")
$reportLines.Add("- Unsafe sample full SQL marker present: $($unsafeSample.ContainsFullSql)")
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
$reportLines.Add("- P18.1 does not execute a real Pilot.")
$reportLines.Add("- Default output is BlockedNoSubmittedAuthorizationMaterials because user-filled and signed authorization materials have not been received.")
$reportLines.Add("- ReadyForCredentialWindowPlanning only means the safe sample is complete enough for a later credential-window planning stage.")
$reportLines.Add("- Offline validation is not credential configuration, credential validation, endpoint testing, or production query execution.")
$reportLines.Add("- Real endpoint/token use remains outside P18.1 and requires a later explicit execution stage.")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

$reportContent = $reportLines -join [Environment]::NewLine
Assert-NoUnsafeReportContent -Content $reportContent -Name "P18.1 report"
Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Limited Pilot Fillable Authorization Template P18.1 acceptance report written to: $ReportPath"

if ($hasFailures) {
    exit 1
}
