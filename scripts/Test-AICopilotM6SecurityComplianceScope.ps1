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
    $originMain = git rev-parse --verify origin/main 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($originMain)) {
        $changedFiles += git -c core.quotepath=false diff --name-only origin/main..HEAD -- 2>$null |
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
        "^docs/AICopilotEnterpriseAIReadinessBaselineV2\.md$",
        "^docs/AICopilotEnterpriseAIReadinessBaselineV2Checklist\.md$",
        "^docs/AICopilotEnterpriseAIReadinessBaselineV2KnownGaps\.md$",
        "^docs/AICopilotPostBaselineV2Roadmap\.md$",
        "^docs/AICopilotM6SecurityComplianceHardening\.md$",
        "^scripts/Test-AICopilotM6SecurityComplianceScope\.ps1$",
        "^src/tests/AICopilot\.BackendTests/AICopilotM6SecurityComplianceHardeningTests\.cs$"
    )

    foreach ($file in $changedFiles) {
        if ($file -match "^(IIoT\.CloudPlatform|IIoT\.EdgeClient|../IIoT\.CloudPlatform|../IIoT\.EdgeClient)/") {
            Add-Error "Forbidden Cloud/Edge path changed: $file"
        }

        if ($file -match "^src/vues/") {
            Add-Error "Frontend path is frozen for M6 security/compliance scope: $file"
        }

        if ($file -match "(^|/)appsettings(\.Development|\.RealSource\.template)?\.json$") {
            Add-Error "Real endpoint/token configuration files are frozen: $file"
        }

        if ($file -match "Migrations/") {
            Add-Error "Migrations are frozen for M6 security/compliance scope: $file"
        }

        $isAllowed = $false
        foreach ($pattern in $allowedPatterns) {
            if ($file -match $pattern) {
                $isAllowed = $true
                break
            }
        }

        if (-not $isAllowed) {
            Add-Error "File outside M6 security/compliance allowlist changed: $file"
        }
    }
}
finally {
    Pop-Location
}

Assert-Contains "docs/AICopilotM6SecurityComplianceHardening.md" @(
    "M6 closes the current security and compliance hardening baseline",
    "No plaintext secret, token, API key, connection string",
    "appsettings",
    "No migration is added",
    "ExecutionPermission=not granted",
    "GateState=BlockedUntilExplicitM7Authorization",
    "query_cloud_data_readonly",
    "GA"
)

Assert-Contains "src/tests/AICopilot.BackendTests/AICopilotM6SecurityComplianceHardeningTests.cs" @(
    'Suite", "AICopilotM6SecurityComplianceHardening',
    "SensitiveRuntimeContracts_ShouldExposeOnlySafeSecretAndEndpointMarkers",
    "ToolAndUploadPolicies_ShouldRejectWriteOrDangerousInputs",
    "AuditAndArtifactEvidence_ShouldUseHashesAndSafeMetadata"
)

Assert-Contains "src/services/AICopilot.Services.CrossCutting/Serialization/SensitiveValueMasker.cs" @(
    "******"
)

Assert-Contains "src/shared/AICopilot.SharedKernel/Ai/AiToolSafetyPolicy.cs" @(
    "Cloud-related tools must explicitly declare read-only behavior",
    "Cloud-related tools must not be side-effecting",
    "Cloud-related tool contains forbidden write semantics"
)

Assert-Contains "src/services/AICopilot.AiGatewayService/AgentTasks/AgentAuditRecorder.cs" @(
    "Agent.ArtifactDownload",
    "Agent.ArtifactVersionDownload",
    "queryHash",
    "resultHash"
)

$scopeText = @(
    Read-RepoText "docs/AICopilotM6SecurityComplianceHardening.md"
    Read-RepoText "src/tests/AICopilot.BackendTests/AICopilotM6SecurityComplianceHardeningTests.cs"
) -join "`n"

$forbiddenClaims = @(
    "ExecutionPermission=granted",
    "query_cloud_data_readonly enabled",
    "query_cloud_data_readonly 已开放",
    "Cloud 写已开放",
    "Recipe/version 已开放",
    "正式 GA 已通过",
    "已执行真实 Pilot",
    "真实 Pilot 已执行",
    "已配置真实 endpoint",
    "已配置真实 token"
)

foreach ($claim in $forbiddenClaims) {
    if ($scopeText -like "*$claim*") {
        Add-Error "Forbidden M6 claim found: $claim"
    }
}

if ($scopeText -match "sk-[A-Za-z0-9_\-]{8,}") {
    Add-Error "Potential plaintext provider key pattern found in M6 scope."
}

if ($errors.Count -gt 0) {
    Write-Host "AICopilot M6 security/compliance scope check failed:"
    foreach ($errorItem in $errors) {
        Write-Host " - $errorItem"
    }
    exit 1
}

Write-Host "AICopilot M6 security/compliance scope check passed."
