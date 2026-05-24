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
        "^docs/AICopilotM3_1ModelApiPoolRuntimeProductionization\.md$",
        "^scripts/Test-AICopilotM3_1ModelApiPoolRuntimeScope\.ps1$",
        "^src/infrastructure/AICopilot\.AiRuntime/AgentRuntimeFactory\.cs$",
        "^src/infrastructure/AICopilot\.AiRuntime/ModelProviderReliability\.cs$",
        "^src/services/AICopilot\.Services\.Contracts/Contracts/AiRuntimeContracts\.cs$",
        "^src/services/AICopilot\.AiGatewayService/Agents/ChatAgentFactory\.cs$",
        "^src/tests/AICopilot\.BackendTests/AICopilotM3_1ModelPoolRuntimeTests\.cs$",
        "^src/tests/AICopilot\.BackendTests/ModelProviderReliabilityTests\.cs$",
        "^src/tests/AICopilot\.BackendTests/EnterpriseDataGovernanceP0Tests\.cs$",
        "^src/tests/AICopilot\.BackendTests/EnterpriseDataGovernanceP1Tests\.cs$"
    )

    foreach ($file in $changedFiles) {
        if ($file -match "^(IIoT\.CloudPlatform|IIoT\.EdgeClient|../IIoT\.CloudPlatform|../IIoT\.EdgeClient)/") {
            Add-Error "Forbidden Cloud/Edge path changed: $file"
        }

        if ($file -match "^src/vues/") {
            Add-Error "Frontend path is frozen for M3.1 runtime scope: $file"
        }

        if ($file -match "(^|/)appsettings(\.Development|\.RealSource\.template)?\.json$") {
            Add-Error "Real endpoint/token configuration files are frozen: $file"
        }

        if ($file -match "Migrations/") {
            Add-Error "Migrations are frozen for M3.1 runtime scope: $file"
        }

        $isAllowed = $false
        foreach ($pattern in $allowedPatterns) {
            if ($file -match $pattern) {
                $isAllowed = $true
                break
            }
        }

        if (-not $isAllowed) {
            Add-Error "File outside M3.1 allowlist changed: $file"
        }
    }
}
finally {
    Pop-Location
}

Assert-Contains "docs/AICopilotM3_1ModelApiPoolRuntimeProductionization.md" @(
    "No real provider endpoint/token/API key/connection string is committed",
    "appsettings*.json",
    "M3.1 does not enable real Pilot execution",
    "query_cloud_data_readonly",
    "GA"
)

Assert-Contains "src/services/AICopilot.Services.Contracts/Contracts/AiRuntimeContracts.cs" @(
    "AgentRuntimeCallerContext",
    "ModelInFlight",
    "HasBaseUrl"
)

Assert-Contains "src/infrastructure/AICopilot.AiRuntime/ModelProviderReliability.cs" @(
    "AcquireEndpoint",
    "ModelEndpointLease",
    "PerUserRpmLimit",
    "PerRoleRpmLimit",
    "PerTenantRpmLimit",
    "[redacted-endpoint]"
)

$scopeText = @(
    Read-RepoText "docs/AICopilotM3_1ModelApiPoolRuntimeProductionization.md"
    Read-RepoText "src/infrastructure/AICopilot.AiRuntime/ModelProviderReliability.cs"
    Read-RepoText "src/services/AICopilot.Services.Contracts/Contracts/AiRuntimeContracts.cs"
    Read-RepoText "src/tests/AICopilot.BackendTests/AICopilotM3_1ModelPoolRuntimeTests.cs"
) -join "`n"

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
    if ($scopeText -like "*$claim*") {
        Add-Error "Forbidden M3.1 claim found: $claim"
    }
}

if ($scopeText -match "sk-[A-Za-z0-9_\-]{8,}") {
    Add-Error "Potential plaintext provider key pattern found in M3.1 scope."
}

if ($errors.Count -gt 0) {
    Write-Host "AICopilot M3.1 model/API pool runtime scope check failed:"
    foreach ($errorItem in $errors) {
        Write-Host " - $errorItem"
    }
    exit 1
}

Write-Host "AICopilot M3.1 model/API pool runtime scope check passed."
