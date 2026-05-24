[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = Split-Path -Parent $PSScriptRoot
$errors = [System.Collections.Generic.List[string]]::new()

function Add-Error {
    param([string]$Message)
    $script:errors.Add($Message) | Out-Null
}

function Normalize-PathForGit {
    param([string]$Path)
    return ($Path -replace "\\", "/").Trim()
}

function Read-RepoText {
    param([string]$RelativePath)
    $fullPath = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        Add-Error "Missing required file: $RelativePath"
        return ""
    }

    return Get-Content -LiteralPath $fullPath -Raw -Encoding UTF8
}

function Assert-Contains {
    param(
        [string]$RelativePath,
        [string[]]$Markers
    )

    $content = Read-RepoText $RelativePath
    foreach ($marker in $Markers) {
        if ($content -notlike "*$marker*") {
            Add-Error "$RelativePath is missing required marker: $marker"
        }
    }
}

Push-Location $repoRoot
try {
    $changedFiles = @()
    $changedFiles += git -c core.quotepath=false diff --name-only -- 2>$null | ForEach-Object { Normalize-PathForGit $_ }
    $changedFiles += git -c core.quotepath=false ls-files --others --exclude-standard 2>$null | ForEach-Object { Normalize-PathForGit $_ }
    $changedFiles = $changedFiles |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique

    foreach ($file in $changedFiles) {
        if ($file -match "^(IIoT\.CloudPlatform|IIoT\.EdgeClient|../IIoT\.CloudPlatform|../IIoT\.EdgeClient)/") {
            Add-Error "Forbidden Cloud/Edge path changed: $file"
        }

        if ($file -match "^src/vues/") {
            Add-Error "Frontend path is frozen for M2-M9 backend/governance scope: $file"
        }

        if ($file -match "(^|/)appsettings(\.Development)?\.json$") {
            Add-Error "Real endpoint/token configuration files are frozen: $file"
        }
    }
}
finally {
    Pop-Location
}

Assert-Contains "docs/AICopilotM2-M9连续推进执行记录.md" @(
    "M2 Pilot Authorization Workflow",
    "M3 Model/API Pool Productionization",
    "M4 RAG Governance Completion",
    "M5 Enterprise Data Source Platformization",
    "M6 Security & Compliance Hardening",
    "M7 前硬停",
    "未配置真实 endpoint/token",
    "未开启 Cloud 写或自由 SQL",
    "未声明 GA"
)

Assert-Contains "docs/AICopilotM7真实Pilot前硬停授权包.md" @(
    "ExecutionPermission=not granted",
    "GateState=BlockedUntilExplicitM7Authorization",
    "当前只允许 planning 和 readiness，不允许 execution"
)

Assert-Contains "docs/AICopilotM9GPT总审包.md" @(
    "GPT/5.5 Pro",
    "planning",
    "不把 M9 审核包视为 GA 发布批准"
)

Assert-Contains "src/hosts/AICopilot.HttpApi/Controllers/AiGatewayController.cs" @(
    "pilot-authorization/submissions",
    "pilot-authorization/submissions/{id:guid}",
    "pilot-authorization/submissions/{id:guid}/submit",
    "pilot-authorization/submissions/{id:guid}/approve-credential-window-planning",
    "pilot-authorization/submissions/{id:guid}/approve-limited-pilot-execution-planning",
    "pilot-authorization/submissions/{id:guid}/reject",
    "pilot-authorization/submissions/{id:guid}/revoke"
)

Assert-Contains "src/core/AICopilot.Core.AiGateway/Aggregates/PilotAuthorization/PilotAuthorizationSubmission.cs" @(
    "Draft",
    "Submitted",
    "MachineRejected",
    "ReviewPending",
    "ApprovedForCredentialWindowPlanning",
    "ApprovedForLimitedPilotExecutionPlanning",
    "Rejected",
    "Expired",
    "Revoked"
)

Assert-Contains "src/services/AICopilot.IdentityService/Authorization/PermissionCatalog.cs" @(
    "PilotAuthorization.Submit",
    "PilotAuthorization.View",
    "PilotAuthorization.Review",
    "PilotAuthorization.ApprovePlanning",
    "PilotAuthorization.Reject",
    "PilotAuthorization.Audit"
)

Assert-Contains "src/infrastructure/AICopilot.EntityFrameworkCore/AuditLogs/AuditMetadataCodec.cs" @(
    "pilotAuthorizationStatus",
    "endpointCount",
    "maxRows",
    "timeRangeDays",
    "ownerCount",
    "machineValidationStatus"
)

$pilotAuthorizationText = @(
    Read-RepoText "src/core/AICopilot.Core.AiGateway/Aggregates/PilotAuthorization/PilotAuthorizationSubmission.cs"
    Read-RepoText "src/services/AICopilot.AiGatewayService/PilotAuthorization/PilotAuthorizationWorkflow.cs"
    Read-RepoText "src/hosts/AICopilot.HttpApi/Controllers/AiGatewayController.cs"
) -join "`n"

$forbiddenExecutionState = "Execution" + "Granted"
if ($pilotAuthorizationText -like "*$forbiddenExecutionState*") {
    Add-Error "Forbidden execution authorization state found in PilotAuthorization implementation."
}

$m2M9Docs = @(
    Read-RepoText "docs/AICopilotM2-M9连续推进执行记录.md"
    Read-RepoText "docs/AICopilotM7真实Pilot前硬停授权包.md"
    Read-RepoText "docs/AICopilotM9GPT总审包.md"
) -join "`n"

$forbiddenClaims = @(
    "已执行真实 Pilot",
    "真实 Pilot 已执行",
    "已获准执行真实 Pilot",
    "ExecutionPermission=granted",
    "query_cloud_data_readonly enabled",
    "query_cloud_data_readonly 已开放",
    "Cloud 写已开放",
    "Recipe/version 已开放",
    "正式 GA 已通过",
    "已配置真实 endpoint",
    "已配置真实 token"
)

foreach ($claim in $forbiddenClaims) {
    if ($m2M9Docs -like "*$claim*") {
        Add-Error "Forbidden M2-M9 claim found: $claim"
    }
}

if ($errors.Count -gt 0) {
    Write-Host "AICopilot M2-M9 governance scope check failed:"
    foreach ($errorItem in $errors) {
        Write-Host " - $errorItem"
    }
    exit 1
}

Write-Host "AICopilot M2-M9 governance scope check passed."
