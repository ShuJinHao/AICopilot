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

$allowedChangedFiles = @(
    "docs/AICopilotEnterpriseGovernanceBaselineFreeze.md",
    "docs/AICopilotEnterpriseGovernanceBaselineChecklist.md",
    "docs/AICopilot后续PR拆分计划.md",
    "docs/AICopilotPR48收口PR正文草案.md",
    "scripts/Test-AICopilotBaselineFreezeScope.ps1"
)

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

        if ($allowedChangedFiles -notcontains $file) {
            Add-Error "Unexpected changed file for M1 baseline freeze: $file"
        }
    }
}
finally {
    Pop-Location
}

$documents = @{
    "docs/AICopilotEnterpriseGovernanceBaselineFreeze.md" = @(
        "PR #48",
        "P18.2",
        "不是 GA",
        "不是真实 Pilot",
        "ExecutionPermission=not granted",
        "GateState=BlockedNoSubmittedAuthorizationMaterials",
        "query_cloud_data_readonly",
        "disabled/hidden/non-executable",
        "Cloud/Edge 未改",
        "不再继续叠加 P19"
    )
    "docs/AICopilotEnterpriseGovernanceBaselineChecklist.md" = @(
        "P0-P18.2",
        "不是 GA",
        "不是真实 Pilot",
        "ExecutionPermission=not granted",
        "query_cloud_data_readonly",
        "disabled/hidden/non-executable",
        "Cloud/Edge 未改",
        "不再向 PR #48 叠 P19"
    )
    "docs/AICopilot后续PR拆分计划.md" = @(
        "PR #48",
        "Pilot Authorization Workflow",
        "不修改 `IIoT.CloudPlatform`",
        "不修改 `IIoT.EdgeClient`",
        "不执行真实 Pilot",
        "不配置真实 endpoint/token",
        "不开放 `query_cloud_data_readonly`"
    )
    "docs/AICopilotPR48收口PR正文草案.md" = @(
        "PR #48",
        "P18.2",
        "Not GA",
        "Not real Pilot execution",
        "ExecutionPermission=not granted",
        "query_cloud_data_readonly",
        "disabled/hidden/non-executable"
    )
}

foreach ($entry in $documents.GetEnumerator()) {
    $content = Read-RepoText $entry.Key
    foreach ($required in $entry.Value) {
        if ($content -notlike "*$required*") {
            Add-Error "$($entry.Key) is missing required marker: $required"
        }
    }
}

$allDocumentText = ($documents.Keys | ForEach-Object { Read-RepoText $_ }) -join "`n"
$forbiddenClaims = @(
    "已执行真实 Pilot",
    "真实 Pilot 已执行",
    "已获准执行真实 Pilot",
    "ExecutionPermission=granted",
    "query_cloud_data_readonly enabled",
    "query_cloud_data_readonly 已开放",
    "Cloud 写已开放",
    "Recipe/version 已开放",
    "GA 已通过",
    "正式 GA 已通过",
    "已配置真实 endpoint",
    "已配置真实 token"
)

foreach ($claim in $forbiddenClaims) {
    if ($allDocumentText -like "*$claim*") {
        Add-Error "Forbidden baseline claim found: $claim"
    }
}

if ($errors.Count -gt 0) {
    Write-Host "AICopilot baseline freeze scope check failed:"
    foreach ($errorItem in $errors) {
        Write-Host " - $errorItem"
    }
    exit 1
}

Write-Host "AICopilot baseline freeze scope check passed."
