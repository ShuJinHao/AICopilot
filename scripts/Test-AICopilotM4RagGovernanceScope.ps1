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
        "^docs/AICopilotM4RagGovernanceLoop\.md$",
        "^scripts/Test-AICopilotM4RagGovernanceScope\.ps1$",
        "^src/services/AICopilot\.Services\.Contracts/Contracts/AuditContracts\.cs$",
        "^src/services/AICopilot\.Services\.Contracts/Contracts/RagContracts\.cs$",
        "^src/services/AICopilot\.RagService/KnowledgeBases/KnowledgeRetrievalService\.cs$",
        "^src/services/AICopilot\.RagService/Queries/KnowledgeBases/SearchKnowledgeBase\.cs$",
        "^src/services/AICopilot\.RagService/Queries/KnowledgeBases/SearchKnowledgeBaseResult\.cs$",
        "^src/tests/AICopilot\.BackendTests/AICopilotM4RagGovernanceLoopTests\.cs$",
        "^src/tests/AICopilot\.BackendTests/RagPermissionTests\.cs$"
    )

    foreach ($file in $changedFiles) {
        if ($file -match "^(IIoT\.CloudPlatform|IIoT\.EdgeClient|../IIoT\.CloudPlatform|../IIoT\.EdgeClient)/") {
            Add-Error "Forbidden Cloud/Edge path changed: $file"
        }

        if ($file -match "^src/vues/") {
            Add-Error "Frontend path is frozen for M4 RAG governance scope: $file"
        }

        if ($file -match "(^|/)appsettings(\.Development|\.RealSource\.template)?\.json$") {
            Add-Error "Real endpoint/token configuration files are frozen: $file"
        }

        if ($file -match "Migrations/") {
            Add-Error "Migrations are frozen for M4 RAG governance scope: $file"
        }

        $isAllowed = $false
        foreach ($pattern in $allowedPatterns) {
            if ($file -match $pattern) {
                $isAllowed = $true
                break
            }
        }

        if (-not $isAllowed) {
            Add-Error "File outside M4 RAG governance allowlist changed: $file"
        }
    }
}
finally {
    Pop-Location
}

Assert-Contains "docs/AICopilotM4RagGovernanceLoop.md" @(
    "M4 closes the RAG governance loop",
    "does not change AICopilot frontend, Cloud, Edge, appsettings, migrations",
    "does not persist query text",
    "query_cloud_data_readonly",
    "GA"
)

Assert-Contains "src/services/AICopilot.Services.Contracts/Contracts/RagContracts.cs" @(
    "KnowledgeRetrievalGovernanceEvidenceDto",
    "KnowledgeDocumentCitationDto",
    "SourceDocumentGroupId",
    "WarningCode"
)

Assert-Contains "src/services/AICopilot.RagService/Queries/KnowledgeBases/SearchKnowledgeBase.cs" @(
    "Rag.SearchKnowledgeBaseRecall",
    "CanReadDocument",
    "DocumentGroupId",
    "SUPPLEMENT_OVERRIDE_APPLIED",
    "OUTDATED_DOCUMENT_SKIPPED"
)

Assert-Contains "src/tests/AICopilot.BackendTests/AICopilotM4RagGovernanceLoopTests.cs" @(
    'Suite", "AICopilotM4RagGovernanceLoop',
    "Search_ShouldOnlyRecallLatestEffectiveDocumentVersion",
    "RecallAudit_ShouldOnlyPersistSafeSummaryFields",
    "Search_ShouldEnforceCategoryVisibility"
)

$scopeText = @(
    Read-RepoText "docs/AICopilotM4RagGovernanceLoop.md"
    Read-RepoText "src/services/AICopilot.Services.Contracts/Contracts/RagContracts.cs"
    Read-RepoText "src/services/AICopilot.RagService/Queries/KnowledgeBases/SearchKnowledgeBase.cs"
    Read-RepoText "src/tests/AICopilot.BackendTests/AICopilotM4RagGovernanceLoopTests.cs"
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
        Add-Error "Forbidden M4 claim found: $claim"
    }
}

if ($scopeText -match "sk-[A-Za-z0-9_\-]{8,}") {
    Add-Error "Potential plaintext provider key pattern found in M4 scope."
}

if ($errors.Count -gt 0) {
    Write-Host "AICopilot M4 RAG governance scope check failed:"
    foreach ($errorItem in $errors) {
        Write-Host " - $errorItem"
    }
    exit 1
}

Write-Host "AICopilot M4 RAG governance scope check passed."
