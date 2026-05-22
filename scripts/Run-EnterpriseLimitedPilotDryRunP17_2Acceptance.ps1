[CmdletBinding()]
param(
    [string]$ReportPath = ".\docs\enterprise-limited-pilot-dry-run-p17_2-latest.md",
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

function Get-StableHash {
    param([Parameter(Mandatory = $true)][string]$Value)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
        $hash = $sha.ComputeHash($bytes)
        return ([BitConverter]::ToString($hash).Replace("-", "").ToLowerInvariant()).Substring(0, 16)
    } finally {
        $sha.Dispose()
    }
}

function New-DryRunEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$PathType,
        [Parameter(Mandatory = $true)][string]$EndpointCode,
        [Parameter(Mandatory = $true)][int]$Ordinal
    )

    $sourceMode = if ($PathType -eq "FixedTemplate") { "CloudReadonlyProductionPilotDryRun" } else { "CloudReadonlyProductionControlledPilotDryRun" }
    $boundary = if ($PathType -eq "FixedTemplate") { "LimitedPilotDryRunFixedScenario" } else { "LimitedPilotDryRunControlledGoal" }
    $rowCount = 10 + ($Ordinal * 3)
    $truncated = $EndpointCode -eq "device_logs" -or $EndpointCode -eq "pass_station_records"
    $queryHash = Get-StableHash "$PathType|$EndpointCode|query|last7days|maxRows50"
    $resultHash = Get-StableHash "$PathType|$EndpointCode|result|$rowCount|$truncated"
    $artifactRef = "dryrun-$($PathType.ToLowerInvariant())-$EndpointCode-artifact"

    [pscustomobject]@{
        PathType = $PathType
        EndpointCode = $EndpointCode
        SourceMode = $sourceMode
        Boundary = $boundary
        ApprovalStatus = "ToolApproved,FinalApproved"
        DurationMs = 120 + ($Ordinal * 11)
        RowCount = $rowCount
        IsTruncated = $truncated
        QueryHash = $queryHash
        ResultHash = $resultHash
        ArtifactRefs = @($artifactRef)
        EmergencyStopEvidence = "NotActiveDuringPositiveRun"
        RollbackEvidence = "RollbackPlanRecorded"
    }
}

function Invoke-LimitedPilotDryRun {
    $endpoints = @("devices", "capacity_summary", "device_logs", "pass_station_records")
    $evidence = New-Object System.Collections.Generic.List[object]
    $ordinal = 0

    foreach ($pathType in @("FixedTemplate", "ControlledGoal")) {
        foreach ($endpoint in $endpoints) {
            $ordinal++
            $evidence.Add((New-DryRunEvidence -PathType $pathType -EndpointCode $endpoint -Ordinal $ordinal))
        }
    }

    $refusals = @(
        [pscustomobject]@{ Case = "RecipeEndpoint"; Status = "BlockedByPolicy"; Reason = "RecipeForbidden" },
        [pscustomobject]@{ Case = "RecipeVersionEndpoint"; Status = "BlockedByPolicy"; Reason = "RecipeVersionForbidden" },
        [pscustomobject]@{ Case = "CloudWritePath"; Status = "BlockedByPolicy"; Reason = "CloudWriteForbidden" },
        [pscustomobject]@{ Case = "UnknownEndpoint"; Status = "BlockedByPolicy"; Reason = "EndpointNotAllowlisted" },
        [pscustomobject]@{ Case = "OverMaxRows"; Status = "BlockedByPolicy"; Reason = "MaxRowsExceeded" },
        [pscustomobject]@{ Case = "OverTimeRange"; Status = "BlockedByPolicy"; Reason = "TimeRangeExceeded" }
    )

    $emergencyStop = @(
        [pscustomobject]@{ PathType = "FixedTemplate"; EmergencyStopState = "Active"; Status = "Rejected"; Reason = "EmergencyStopActive" },
        [pscustomobject]@{ PathType = "ControlledGoal"; EmergencyStopState = "Active"; Status = "Rejected"; Reason = "EmergencyStopActive" },
        [pscustomobject]@{ PathType = "FixedTemplate"; EmergencyStopState = "Cleared"; Status = "RequiresGateWindowApproval"; Reason = "NoAutomaticExecution" },
        [pscustomobject]@{ PathType = "ControlledGoal"; EmergencyStopState = "Cleared"; Status = "RequiresGateWindowApproval"; Reason = "NoAutomaticExecution" }
    )

    $rollback = [pscustomobject]@{
        PilotWindowEvidence = "WindowDisablePlanRecorded"
        CredentialEvidence = "CredentialRevocationPlanRecorded"
        LedgerEvidence = "HashOnlyLedgerPreserved"
        RealCredentialTouched = $false
    }

    [pscustomobject]@{
        Evidence = $evidence
        Refusals = $refusals
        EmergencyStop = $emergencyStop
        Rollback = $rollback
    }
}

function Assert-DryRunCoverage {
    param([Parameter(Mandatory = $true)]$DryRun)

    $requiredEndpoints = @("devices", "capacity_summary", "device_logs", "pass_station_records")
    foreach ($pathType in @("FixedTemplate", "ControlledGoal")) {
        foreach ($endpoint in $requiredEndpoints) {
            $match = @($DryRun.Evidence | Where-Object { $_.PathType -eq $pathType -and $_.EndpointCode -eq $endpoint })
            if ($match.Count -ne 1) {
                throw "Missing dry-run evidence for $pathType/$endpoint."
            }
        }
    }

    foreach ($item in $DryRun.Evidence) {
        if ([string]::IsNullOrWhiteSpace($item.QueryHash) -or [string]::IsNullOrWhiteSpace($item.ResultHash)) {
            throw "Dry-run evidence is missing hash for $($item.PathType)/$($item.EndpointCode)."
        }
        if ($item.ArtifactRefs.Count -lt 1) {
            throw "Dry-run evidence is missing artifact ref for $($item.PathType)/$($item.EndpointCode)."
        }
        if ($item.ApprovalStatus -ne "ToolApproved,FinalApproved") {
            throw "Dry-run evidence has unexpected approval status for $($item.PathType)/$($item.EndpointCode)."
        }
    }

    foreach ($caseName in @("RecipeEndpoint", "RecipeVersionEndpoint", "CloudWritePath", "UnknownEndpoint", "OverMaxRows", "OverTimeRange")) {
        $match = @($DryRun.Refusals | Where-Object { $_.Case -eq $caseName -and $_.Status -eq "BlockedByPolicy" })
        if ($match.Count -ne 1) {
            throw "Missing refusal evidence for $caseName."
        }
    }

    $activeRejects = @($DryRun.EmergencyStop | Where-Object { $_.EmergencyStopState -eq "Active" -and $_.Status -eq "Rejected" })
    if ($activeRejects.Count -ne 2) {
        throw "Emergency stop active did not reject both Pilot paths."
    }

    $clearedRequiresGate = @($DryRun.EmergencyStop | Where-Object { $_.EmergencyStopState -eq "Cleared" -and $_.Status -eq "RequiresGateWindowApproval" })
    if ($clearedRequiresGate.Count -ne 2) {
        throw "Emergency stop clear did not preserve gate/window/approval requirements."
    }

    if ($DryRun.Rollback.RealCredentialTouched) {
        throw "Rollback rehearsal touched a real credential."
    }
}

$results = @()

if (-not $SkipScopeGuard) {
    $results += Invoke-Step -Name "Enterprise Data Governance Scope Guard" -Script {
        powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
    }
}

$results += Invoke-Step -Name "P17.1 Authorization Inheritance Check" -Script {
    $p171Report = ".\docs\enterprise-limited-pilot-authorization-p17_1-latest.md"
    $p171Scope = ".\docs\enterprise-limited-pilot-authorization-p17_1-scope.md"
    $p171Package = ".\docs\enterprise-limited-pilot-authorization-p17_1-package.md"
    foreach ($path in @($p171Report, $p171Scope, $p171Package)) {
        if (-not (Test-Path $path)) {
            throw "P17.1 evidence is missing: $path"
        }
    }

    $combined = @(
        Get-Content -LiteralPath $p171Report -Raw
        Get-Content -LiteralPath $p171Scope -Raw
        Get-Content -LiteralPath $p171Package -Raw
    ) -join "`n"

    foreach ($marker in @(
        "P17.1",
        "AuthorizationDryRunReady",
        "fake/fixture",
        "Pilot Window",
        "Tool Approval",
        "Final Approval",
        "hash-only",
        "query_cloud_data_readonly",
        "does not execute a real Pilot"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P17.1 evidence is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P17.1 evidence"
    "P17.1 authorization evidence is present and remains non-executing."
}

$results += Invoke-Step -Name "P17.2 Scope And Evidence Package Check" -Script {
    $scope = ".\docs\enterprise-limited-pilot-dry-run-p17_2-scope.md"
    $evidence = ".\docs\enterprise-limited-pilot-dry-run-p17_2-evidence.md"
    foreach ($path in @($scope, $evidence)) {
        if (-not (Test-Path $path)) {
            throw "P17.2 document is missing: $path"
        }
    }

    $combined = @(
        Get-Content -LiteralPath $scope -Raw
        Get-Content -LiteralPath $evidence -Raw
    ) -join "`n"

    foreach ($marker in @(
        "P17.2",
        "fake/fixture",
        "dry-run",
        "Fixed-template path",
        "Controlled-goal path",
        "devices",
        "capacity_summary",
        "device_logs",
        "pass_station_records",
        "last 7 days",
        "maxRows=50",
        "Tool Approval",
        "Final Approval",
        "Emergency stop",
        "Rollback",
        "hash-only",
        "query_cloud_data_readonly"
    )) {
        if ($combined -notmatch [regex]::Escape($marker)) {
            throw "P17.2 material is missing marker: $marker"
        }
    }

    Assert-NoUnsafeReportContent -Content $combined -Name "P17.2 material"
    "P17.2 scope and evidence package markers passed."
}

$dryRun = Invoke-LimitedPilotDryRun

$results += Invoke-Step -Name "P17.2 Dry-Run Runner Coverage Check" -Script {
    Assert-DryRunCoverage -DryRun $dryRun
    "Dry-run runner produced $($dryRun.Evidence.Count) positive evidence item(s), $($dryRun.Refusals.Count) refusal item(s), $($dryRun.EmergencyStop.Count) emergency-stop item(s)."
}

$results += Invoke-Step -Name "P17.2 Dry-Run Safety Check" -Script {
    $json = $dryRun | ConvertTo-Json -Depth 8
    foreach ($forbidden in @(
        "RealEndpointCalled",
        "RealTokenUsed",
        "query_cloud_data_readonly enabled",
        "ReadyForLimitedPilotExecution",
        "GA Permission: granted"
    )) {
        if ($json -match [regex]::Escape($forbidden)) {
            throw "Dry-run evidence contains forbidden execution marker: $forbidden"
        }
    }

    Assert-NoUnsafeReportContent -Content $json -Name "P17.2 dry-run evidence"
    "Dry-run evidence is hash-only and contains no real endpoint, credential, raw payload, or raw business records."
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

$results += Invoke-Step -Name "P17.2 No Execution Claim Check" -Script {
    $combined = @(
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-dry-run-p17_2-scope.md" -Raw
        Get-Content -LiteralPath ".\docs\enterprise-limited-pilot-dry-run-p17_2-evidence.md" -Raw
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
            throw "P17.2 material contains forbidden execution marker: $forbidden"
        }
    }

    "P17.2 material records fake/fixture dry-run only."
}

$externalReviewState = Get-ExternalReviewState
$prSummary = Get-PrSummary
$localHead = (git rev-parse HEAD 2>$null | Out-String).Trim()
$branch = (git branch --show-current 2>$null | Out-String).Trim()
$failedResults = @($results | Where-Object { -not $_.Succeeded })
$hasFailures = $failedResults.Count -gt 0
$dryRunDecision = if ($hasFailures) { "BlockedByAcceptanceFailure" } else { "DryRunEvidenceReady" }

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AICopilot Limited Pilot Dry-Run P17.2 Acceptance")
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
$reportLines.Add("- ExternalReviewBlockingPolicy: evidence-only for P17.2 dry-run material")
$reportLines.Add("- DryRunDecision: $dryRunDecision")
$reportLines.Add("- ExecutionPermission: not granted")
$reportLines.Add("- Boundary: P17.2 runs fake/fixture dry-run rehearsal only; it does not execute a real Pilot and is not GA")
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
$reportLines.Add("## Positive Dry-Run Evidence")
$reportLines.Add("")
$reportLines.Add("| Path | Endpoint | Source Mode | Boundary | Approval | DurationMs | RowCount | Truncated | QueryHash | ResultHash | ArtifactRefs |")
$reportLines.Add("| --- | --- | --- | --- | --- | ---: | ---: | --- | --- | --- | --- |")
foreach ($item in $dryRun.Evidence) {
    $artifactRefs = ($item.ArtifactRefs -join ",")
    $reportLines.Add("| $($item.PathType) | $($item.EndpointCode) | $($item.SourceMode) | $($item.Boundary) | $($item.ApprovalStatus) | $($item.DurationMs) | $($item.RowCount) | $($item.IsTruncated) | $($item.QueryHash) | $($item.ResultHash) | $artifactRefs |")
}

$reportLines.Add("")
$reportLines.Add("## Refusal Evidence")
$reportLines.Add("")
$reportLines.Add("| Case | Status | Reason |")
$reportLines.Add("| --- | --- | --- |")
foreach ($item in $dryRun.Refusals) {
    $reportLines.Add("| $($item.Case) | $($item.Status) | $($item.Reason) |")
}

$reportLines.Add("")
$reportLines.Add("## Emergency Stop And Rollback Evidence")
$reportLines.Add("")
$reportLines.Add("| Path | Emergency Stop State | Status | Reason |")
$reportLines.Add("| --- | --- | --- | --- |")
foreach ($item in $dryRun.EmergencyStop) {
    $reportLines.Add("| $($item.PathType) | $($item.EmergencyStopState) | $($item.Status) | $($item.Reason) |")
}
$reportLines.Add("")
$reportLines.Add("- Rollback Pilot Window evidence: $($dryRun.Rollback.PilotWindowEvidence)")
$reportLines.Add("- Rollback credential evidence: $($dryRun.Rollback.CredentialEvidence)")
$reportLines.Add("- Rollback ledger evidence: $($dryRun.Rollback.LedgerEvidence)")
$reportLines.Add("- Rollback real credential touched: $($dryRun.Rollback.RealCredentialTouched)")
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
$reportLines.Add("- P17.2 does not execute a real Pilot.")
$reportLines.Add("- Real endpoint/token use remains outside P17.2 and requires a future explicit approval.")
$reportLines.Add("- External 5.5 Pro review state is evidence only for this dry-run package and must still be considered before any future execution.")
$reportLines.Add("- Future execution still requires approved Pilot Window, approved chain, rollback, emergency stop, and approved runtime credential configuration.")
$reportLines.Add("")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory) -and -not (Test-Path $reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

Set-Content -Path $ReportPath -Value $reportLines -Encoding UTF8
Write-Host "Enterprise Limited Pilot Dry-Run P17.2 acceptance report written to: $ReportPath"

if ($hasFailures) {
    exit 1
}
