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

function Get-BaseRef {
    Push-Location $repoRoot
    try {
        $originMain = git rev-parse --verify origin/main 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($originMain)) {
            $mergeBase = git merge-base HEAD origin/main 2>$null
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($mergeBase)) {
                return $mergeBase.Trim()
            }
        }

        $parent = git rev-parse --verify HEAD~1 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($parent)) {
            return $parent.Trim()
        }

        return $null
    }
    finally {
        Pop-Location
    }
}

Push-Location $repoRoot
try {
    $changedFiles = @()
    $baseRef = Get-BaseRef
    if (-not [string]::IsNullOrWhiteSpace($baseRef)) {
        $changedFiles += git -c core.quotepath=false diff --name-only "${baseRef}...HEAD" -- 2>$null |
            ForEach-Object { Normalize-PathForGit $_ }
    }

    $changedFiles += git -c core.quotepath=false diff --name-only -- 2>$null |
        ForEach-Object { Normalize-PathForGit $_ }
    $changedFiles += git -c core.quotepath=false ls-files --others --exclude-standard 2>$null |
        ForEach-Object { Normalize-PathForGit $_ }

    $changedFiles = $changedFiles |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique

    $allowedPatterns = @(
        "^docs/AICopilotM2_1PilotAuthorizationHardeningScope\.md$",
        "^scripts/Test-AICopilotM2_1PilotAuthorizationHardeningScope\.ps1$",
        "^src/core/AICopilot\.Core\.AiGateway/Aggregates/PilotAuthorization/",
        "^src/core/AICopilot\.Core\.AiGateway/Specifications/PilotAuthorization/",
        "^src/services/AICopilot\.AiGatewayService/PilotAuthorization/",
        "^src/services/AICopilot\.IdentityService/Authorization/PermissionCatalog\.cs$",
        "^src/hosts/AICopilot\.HttpApi/Controllers/AiGatewayController\.cs$",
        "^src/hosts/AICopilot\.DataWorker/Program\.cs$",
        "^src/infrastructure/AICopilot\.EntityFrameworkCore/Configuration/AiGateway/PilotAuthorizationSubmissionConfiguration\.cs$",
        "^src/infrastructure/AICopilot\.EntityFrameworkCore/Migrations/AiGatewayDbContext/(AiGatewayDbContextModelSnapshot\.cs|[0-9]+_AddPilotAuthorizationHardeningM21(\.Designer)?\.cs)$",
        "^src/tests/AICopilot\.BackendTests/PilotAuthorizationWorkflowM2Tests\.cs$"
    )

    foreach ($file in $changedFiles) {
        if ($file -match "^(IIoT\.CloudPlatform|IIoT\.EdgeClient|../IIoT\.CloudPlatform|../IIoT\.EdgeClient)/") {
            Add-Error "Forbidden Cloud/Edge path changed: $file"
        }

        if ($file -match "^src/vues/") {
            Add-Error "Frontend path is frozen for M2.1: $file"
        }

        if ($file -match "(^|/)appsettings(\.Development)?\.json$") {
            Add-Error "Real endpoint/token configuration files are frozen: $file"
        }

        $isAllowed = $false
        foreach ($pattern in $allowedPatterns) {
            if ($file -match $pattern) {
                $isAllowed = $true
                break
            }
        }

        if (-not $isAllowed) {
            Add-Error "File outside M2.1 allowlist changed: $file"
        }
    }
}
finally {
    Pop-Location
}

Assert-Contains "docs/AICopilotM2_1PilotAuthorizationHardeningScope.md" @(
    "Batch 0 through Batch 4 only",
    "ExecutionPermission=not granted",
    "GateState=BlockedUntilExplicitM7Authorization",
    "No real Pilot execution",
    "No query_cloud_data_readonly enablement"
)

Assert-Contains "src/core/AICopilot.Core.AiGateway/Aggregates/PilotAuthorization/PilotAuthorizationSensitiveContentGuard.cs" @(
    "PilotAuthorizationSensitiveContentGuard",
    "Bearer token material is not allowed",
    "Database URL material is not allowed",
    "Sensitive Chinese security wording is not allowed"
)

Assert-Contains "src/services/AICopilot.AiGatewayService/PilotAuthorization/PilotAuthorizationWorkflow.cs" @(
    "PilotAuthorization.Expire",
    "pilot_authorization_self_review_forbidden",
    "PilotAuthorization.UnsafeDraftRejected",
    "BlockedUntilExplicitM7Authorization",
    "PilotAuthorizationExpiryWorker"
)

Assert-Contains "src/hosts/AICopilot.HttpApi/Controllers/AiGatewayController.cs" @(
    "pilot-authorization/submissions/{id:guid}/expire"
)

Assert-Contains "src/hosts/AICopilot.DataWorker/Program.cs" @(
    "PilotAuthorizationExpiryWorker"
)

Assert-Contains "src/services/AICopilot.IdentityService/Authorization/PermissionCatalog.cs" @(
    "PilotAuthorization.Expire"
)

$implementationText = @(
    Read-RepoText "src/core/AICopilot.Core.AiGateway/Aggregates/PilotAuthorization/PilotAuthorizationSubmission.cs"
    Read-RepoText "src/services/AICopilot.AiGatewayService/PilotAuthorization/PilotAuthorizationWorkflow.cs"
    Read-RepoText "src/hosts/AICopilot.HttpApi/Controllers/AiGatewayController.cs"
) -join "`n"

$forbiddenExecutionState = "Execution" + "Granted"
if ($implementationText -like "*$forbiddenExecutionState*") {
    Add-Error "Forbidden execution authorization state found in M2.1 implementation."
}

$scopeDoc = Read-RepoText "docs/AICopilotM2_1PilotAuthorizationHardeningScope.md"
$forbiddenClaims = @(
    "ExecutionPermission=granted",
    "query_cloud_data_readonly enabled",
    "query_cloud_data_readonly 已开放",
    "Cloud 写已开放",
    "Recipe/version 已开放",
    "正式 GA 已通过",
    "已执行真实 Pilot",
    "已配置真实 endpoint",
    "已配置真实 token"
)

foreach ($claim in $forbiddenClaims) {
    if ($scopeDoc -like "*$claim*") {
        Add-Error "Forbidden M2.1 claim found: $claim"
    }
}

if ($errors.Count -gt 0) {
    Write-Host "AICopilot M2.1 Pilot authorization hardening scope check failed:"
    foreach ($errorItem in $errors) {
        Write-Host " - $errorItem"
    }
    exit 1
}

Write-Host "AICopilot M2.1 Pilot authorization hardening scope check passed."
