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
    $rangeChangedFiles = @()
    $rangeChangedFiles += git -c core.quotepath=false diff --name-only origin/main..HEAD 2>$null | ForEach-Object { Normalize-PathForGit $_ }
    $rangeChangedFiles += git -c core.quotepath=false diff --name-only -- 2>$null | ForEach-Object { Normalize-PathForGit $_ }
    $rangeChangedFiles += git -c core.quotepath=false ls-files --others --exclude-standard 2>$null | ForEach-Object { Normalize-PathForGit $_ }
    $rangeChangedFiles = $rangeChangedFiles |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique
}
finally {
    Pop-Location
}

$allowedFiles = @(
    "docs/AICopilotEnterpriseAIReadinessBaselineV2.md",
    "docs/AICopilotEnterpriseAIReadinessBaselineV2Checklist.md",
    "docs/AICopilotEnterpriseAIReadinessBaselineV2KnownGaps.md",
    "docs/AICopilotPostBaselineV2Roadmap.md",
    "docs/AICopilotM6SecurityComplianceHardening.md",
    "docs/enterprise-limited-pilot-dry-run-p17_2-latest.md",
    "docs/AICopilotM2-M9连续推进执行记录.md",
    "docs/AICopilotM9GPT总审包.md",
    "scripts/Run-EnterpriseLimitedPilotDryRunP17_2Acceptance.ps1",
    "scripts/Test-AICopilotM6SecurityComplianceScope.ps1",
    "scripts/Test-AICopilot55TotalReviewScope.ps1",
    "src/tests/AICopilot.BackendTests/AICopilotM6SecurityComplianceHardeningTests.cs"
)

foreach ($file in $rangeChangedFiles) {
    if ($allowedFiles -notcontains $file) {
        Add-Error "File is outside the 5.5 total review scope: $file"
    }

    if ($file -match "^(IIoT\.CloudPlatform|IIoT\.EdgeClient|../IIoT\.CloudPlatform|../IIoT\.EdgeClient)/") {
        Add-Error "Forbidden Cloud/Edge path changed: $file"
    }

    if ($file -match "^src/vues/") {
        Add-Error "Frontend path is frozen for 5.5 total review: $file"
    }

    if ($file -match "(^|/)appsettings.*\.json$") {
        Add-Error "Runtime configuration file is frozen for 5.5 total review: $file"
    }

    if ($file -match "(^|/)Migrations?/") {
        Add-Error "Migration path is frozen for 5.5 total review: $file"
    }
}

Assert-Contains "docs/AICopilotM9GPT总审包.md" @(
    "GPT/5.5 Pro",
    "6c0b9cd",
    "9f09986",
    "ad0092a",
    "a75e4b8",
    "不是真实 Pilot",
    "不是 GA",
    "Draft PR #56",
    "simulation-rc"
)

Assert-Contains "docs/AICopilotM2-M9连续推进执行记录.md" @(
    "PR #55",
    "PR #56",
    "DataSource.QueryGovernedSql",
    "DataSource.TextToSql",
    "fake/fixture dry-run evidence",
    "远端 review 和 CI success 不等于真实 Pilot"
)

Assert-Contains "docs/AICopilotEnterpriseAIReadinessBaselineV2.md" @(
    "Enterprise AI Readiness Baseline v2",
    "PR #55",
    "DataSource.QueryGovernedSql",
    "DataSource.TextToSql"
)

Assert-Contains "docs/AICopilotM6SecurityComplianceHardening.md" @(
    "Security And Compliance Hardening",
    "hashes and bounded metadata",
    "No plaintext secret"
)

Assert-Contains "docs/enterprise-limited-pilot-dry-run-p17_2-latest.md" @(
    "DryRunDecision: DryRunEvidenceReady",
    "PR #56",
    "GitHub PR Submitted-State Evidence",
    "simulation-rc status=COMPLETED conclusion=SUCCESS",
    "ExecutionPermission: not granted",
    "query_cloud_data_readonly remains disabled",
    "Rollback real credential touched: False"
)

Assert-Contains "scripts/Run-EnterpriseLimitedPilotDryRunP17_2Acceptance.ps1" @(
    "SkipGitHubPrCheck",
    "GitHubCIAtGeneration",
    "Dry-run evidence is hash-only"
)

Assert-Contains "docs/AICopilotM3_1ModelApiPoolRuntimeProductionization.md" @(
    "Model/API Pool Runtime Productionization",
    "No real provider endpoint/token/API key/connection string is committed"
)

Assert-Contains "docs/AICopilotM4RagGovernanceLoop.md" @(
    "M4 RAG Governance Loop",
    "raw payload",
    "full SQL"
)

Assert-Contains "docs/AICopilotM5EnterpriseDataSourcePlatformization.md" @(
    "Enterprise Data Source Platformization",
    "DataSource.QueryGovernedSql",
    "DataSource.TextToSql"
)

$m5BoundaryText = @(
    Read-RepoText "src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessDatabaseReadonlyQuery.cs"
    Read-RepoText "src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessTextToSql.cs"
    Read-RepoText "src/services/AICopilot.IdentityService/Authorization/PermissionCatalog.cs"
) -join "`n"

foreach ($marker in @(
    "DataSource.QueryGovernedSql",
    "DataSource.TextToSql",
    "DataSourceSelectionMode.GovernedSql",
    "DraftId"
)) {
    if ($m5BoundaryText -notlike "*$marker*") {
        Add-Error "M5 boundary implementation is missing required marker: $marker"
    }
}

$reviewDocs = @(
    "docs/AICopilotEnterpriseAIReadinessBaselineV2.md",
    "docs/AICopilotEnterpriseAIReadinessBaselineV2Checklist.md",
    "docs/AICopilotEnterpriseAIReadinessBaselineV2KnownGaps.md",
    "docs/AICopilotPostBaselineV2Roadmap.md",
    "docs/AICopilotM6SecurityComplianceHardening.md",
    "docs/enterprise-limited-pilot-dry-run-p17_2-latest.md",
    "docs/AICopilotM2-M9连续推进执行记录.md",
    "docs/AICopilotM9GPT总审包.md"
)

$combinedReviewText = ($reviewDocs | ForEach-Object { Read-RepoText $_ }) -join "`n"

$forbiddenClaims = @(
    ("ExecutionPermission=" + "granted"),
    ("query_cloud_data_readonly " + "enabled"),
    ("query_cloud_data_readonly " + "已开放"),
    ("Cloud 写" + "已开放"),
    ("Recipe/version " + "已开放"),
    ("正式 GA " + "已通过"),
    ("GA " + "已通过"),
    ("真实 Pilot " + "已执行"),
    ("已执行" + "真实 Pilot"),
    ("已配置" + "真实 endpoint"),
    ("已配置" + "真实 token")
)

foreach ($claim in $forbiddenClaims) {
    if ($combinedReviewText -like "*$claim*") {
        Add-Error "Forbidden 5.5 total review claim found: $claim"
    }
}

if ($errors.Count -gt 0) {
    Write-Host "AICopilot 5.5 total review scope check failed:"
    foreach ($errorItem in $errors) {
        Write-Host " - $errorItem"
    }
    exit 1
}

Write-Host "AICopilot 5.5 total review scope check passed. Checked $($rangeChangedFiles.Count) candidate file(s)."
