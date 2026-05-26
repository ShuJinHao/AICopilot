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
        "^docs/AICopilotM5EnterpriseDataSourcePlatformization\.md$",
        "^scripts/Test-AICopilotM5EnterpriseDataSourceScope\.ps1$",
        "^src/core/AICopilot\.Core\.AiGateway/Aggregates/Tools/BuiltInToolRegistrations\.cs$",
        "^src/hosts/AICopilot\.MigrationWorkApp/Worker\.cs$",
        "^src/hosts/AICopilot\.HttpApi/Controllers/DataAnalysisController\.cs$",
        "^src/services/AICopilot\.AiGatewayService/AgentTasks/AgentTaskCommands\.cs$",
        "^src/services/AICopilot\.AiGatewayService/AgentTasks/AgentTaskRuntime\.cs$",
        "^src/services/AICopilot\.DataAnalysisService/BusinessDatabases/BusinessDatabaseManagement\.cs$",
        "^src/services/AICopilot\.DataAnalysisService/BusinessDatabases/BusinessDatabaseReadService\.cs$",
        "^src/services/AICopilot\.DataAnalysisService/BusinessDatabases/BusinessDatabaseReadonlyQuery\.cs$",
        "^src/services/AICopilot\.DataAnalysisService/BusinessDatabases/BusinessTextToSql\.cs$",
        "^src/services/AICopilot\.DataAnalysisService/Plugins/DataAnalysisDatabaseResolver\.cs$",
        "^src/services/AICopilot\.DataAnalysisService/Plugins/DataAnalysisPlugin\.cs$",
        "^src/services/AICopilot\.DataAnalysisService/Plugins/DataAnalysisSqlQueryRunner\.cs$",
        "^src/services/AICopilot\.IdentityService/Authorization/PermissionCatalog\.cs$",
        "^src/services/AICopilot\.Services\.Contracts/Contracts/DataSourceContracts\.cs$",
        "^src/tests/AICopilot\.BackendTests/AICopilotM5EnterpriseDataSourcePlatformizationTests\.cs$",
        "^src/tests/AICopilot\.BackendTests/AICopilotM2M9GovernanceScopeTests\.cs$",
        "^src/tests/AICopilot\.BackendTests/DataSourceAuthorizationTests\.cs$",
        "^src/tests/AICopilot\.BackendTests/EnterpriseAgentWorkbenchP2Tests\.cs$",
        "^src/tests/AICopilot\.BackendTests/EnterpriseDataGovernanceP0Tests\.cs$",
        "^src/tests/AICopilot\.BackendTests/EnterpriseDynamicPlannerP3Tests\.cs$",
        "^src/tests/AICopilot\.BackendTests/FreshDatabaseSeedTests\.cs$",
        "^src/tests/AICopilot\.BackendTests/SemanticAnalysisRunnerTests\.cs$",
        "^src/tests/AICopilot\.BackendTests/ToolRegistryGovernanceTests\.cs$",
        "^src/tests/AICopilot\.BackendTests/TextToSqlReadOnlyTests\.cs$"
    )

    foreach ($file in $changedFiles) {
        if ($file -match "^(IIoT\.CloudPlatform|IIoT\.EdgeClient|../IIoT\.CloudPlatform|../IIoT\.EdgeClient)/") {
            Add-Error "Forbidden Cloud/Edge path changed: $file"
        }

        if ($file -match "^src/vues/") {
            Add-Error "Frontend path is frozen for M5 enterprise data source scope: $file"
        }

        if ($file -match "(^|/)appsettings(\.Development|\.RealSource\.template)?\.json$") {
            Add-Error "Real endpoint/token configuration files are frozen: $file"
        }

        if ($file -match "Migrations/") {
            Add-Error "Migrations are frozen for M5 enterprise data source scope: $file"
        }

        $isAllowed = $false
        foreach ($pattern in $allowedPatterns) {
            if ($file -match $pattern) {
                $isAllowed = $true
                break
            }
        }

        if (-not $isAllowed) {
            Add-Error "File outside M5 enterprise data source allowlist changed: $file"
        }
    }
}
finally {
    Pop-Location
}

Assert-Contains "docs/AICopilotM5EnterpriseDataSourcePlatformization.md" @(
    "M5 closes the enterprise data source platformization loop",
    "does not authorize real Pilot execution",
    "free SQL",
    "query_cloud_data_readonly",
    "GA",
    "CloudReadOnly remains blocked",
    "DataSource.QueryGovernedSql",
    "DataSource.TextToSql",
    "DataSourceSelectionMode.GovernedSql",
    "role does not receive either"
)

Assert-Contains "src/services/AICopilot.Services.Contracts/Contracts/DataSourceContracts.cs" @(
    "DataSourceSelectionMode",
    "GovernedSql",
    "BusinessQueryGovernanceDto",
    "IsSanitizedPreview",
    "RedactedColumnHashes"
)

Assert-Contains "src/services/AICopilot.IdentityService/Authorization/PermissionCatalog.cs" @(
    "DataSource.TextToSql",
    "DataSource.QueryGovernedSql",
    "UserDefaultPermissions"
)

Assert-Contains "src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessDatabaseReadonlyQuery.cs" @(
    'AuthorizeRequirement("DataSource.QueryGovernedSql")',
    "DataSource.QueryGovernedSql",
    "DataSourceSelectionMode.GovernedSql",
    "BusinessDataSourceGovernancePolicy",
    "Governed semantic schema is required",
    "Wildcard SELECT projections are not allowed",
    "SANITIZED_PREVIEW",
    "BOUNDED_PREVIEW_APPLIED",
    "SENSITIVE_VALUE_REDACTED"
)

Assert-Contains "src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessTextToSql.cs" @(
    'AuthorizeRequirement("DataSource.TextToSql")',
    "governed draft id",
    "SQL_PREVIEW_REDACTED_USE_DRAFT_ID",
    "free SQL preview execution is not allowed",
    "DataSourceSelectionMode.TextToSql"
)

Assert-Contains "src/core/AICopilot.Core.AiGateway/Aggregates/Tools/BuiltInToolRegistrations.cs" @(
    "query_business_database_readonly",
    "DataSource.TextToSql"
)

Assert-Contains "src/hosts/AICopilot.MigrationWorkApp/Worker.cs" @(
    "ResolveBuiltInRequiredPermission",
    "query_business_database_readonly",
    "definition.RequiredPermission"
)

Assert-Contains "src/tests/AICopilot.BackendTests/AICopilotM5EnterpriseDataSourcePlatformizationTests.cs" @(
    'Suite", "AICopilotM5EnterpriseDataSourcePlatformization',
    "CommandPermissions_ShouldSplitTextToSqlFromRawGovernedSql",
    "PermissionCatalog_ShouldExposeSplitPermissionsWithoutDefaultUserGrant",
    "AgentSourceSelection_ShouldRejectCloudReadOnlyEvenIfGrantedAndSelectable",
    "SourceSelection_ShouldApplyAuthorizationAndChatAgentSelectionFlags",
    "QueryExecution_ShouldRejectUngovernedSql",
    "QueryExecution_ShouldReturnSanitizedBoundedPreviewAndSafeAuditOnly",
    "TextToSql_ShouldRejectCloudReadOnlyEvenIfSelectable",
    "TextToSqlDraft_ShouldExposeHashOnlyAndExecuteThroughDraftId",
    "CloudReadOnlySource_ShouldRemainBlockedUntilGovernedSchemaExists"
)

$scopeText = @(
    Read-RepoText "docs/AICopilotM5EnterpriseDataSourcePlatformization.md"
    Read-RepoText "src/core/AICopilot.Core.AiGateway/Aggregates/Tools/BuiltInToolRegistrations.cs"
    Read-RepoText "src/hosts/AICopilot.MigrationWorkApp/Worker.cs"
    Read-RepoText "src/services/AICopilot.IdentityService/Authorization/PermissionCatalog.cs"
    Read-RepoText "src/services/AICopilot.Services.Contracts/Contracts/DataSourceContracts.cs"
    Read-RepoText "src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessDatabaseReadonlyQuery.cs"
    Read-RepoText "src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessTextToSql.cs"
    Read-RepoText "src/services/AICopilot.DataAnalysisService/Plugins/DataAnalysisSqlQueryRunner.cs"
    Read-RepoText "src/tests/AICopilot.BackendTests/AICopilotM5EnterpriseDataSourcePlatformizationTests.cs"
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
    "已配置真实 token",
    "DataAnalysis.ExecuteFreeSqlQuery",
    'AuthorizeRequirement("DataSource.Query")'
)

foreach ($claim in $forbiddenClaims) {
    if ($scopeText -like "*$claim*") {
        Add-Error "Forbidden M5 claim or free-SQL marker found: $claim"
    }
}

if ($scopeText -match "sk-[A-Za-z0-9_\-]{8,}") {
    Add-Error "Potential plaintext provider key pattern found in M5 scope."
}

if ($scopeText -match "ConnectionStrings__|API[_-]?KEY\s*=") {
    Add-Error "Potential credential configuration marker found in M5 scope."
}

$readonlyQuery = Read-RepoText "src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessDatabaseReadonlyQuery.cs"
if ($readonlyQuery -match 'ExecuteBusinessDatabaseReadonlyQueryCommand\([\s\S]*SelectionMode[\s\S]*\) : ICommand') {
    Add-Error "Raw readonly SQL command must not accept client-supplied query selection mode."
}

$textToSql = Read-RepoText "src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessTextToSql.cs"
if ($textToSql -like '*DataSource.Query*') {
    Add-Error "Text-to-SQL command path must use DataSource.TextToSql, not DataSource.Query."
}

$toolRegistrations = Read-RepoText "src/core/AICopilot.Core.AiGateway/Aggregates/Tools/BuiltInToolRegistrations.cs"
if ($toolRegistrations -match 'BusinessReadonly[\s\S]*"DataSource\.Query"') {
    Add-Error "Business readonly Agent tool must require DataSource.TextToSql, not DataSource.Query."
}

if ($errors.Count -gt 0) {
    Write-Host "AICopilot M5 enterprise data source scope check failed:"
    foreach ($errorItem in $errors) {
        Write-Host " - $errorItem"
    }
    exit 1
}

Write-Host "AICopilot M5 enterprise data source scope check passed."
