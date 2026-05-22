param(
    [string[]]$ChangedFiles,
    [string]$BaseRef,
    [switch]$IncludeWorkingTree
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

function Normalize-PathForGit {
    param([string]$Path)
    return ($Path -replace "\\", "/").Trim()
}

function Add-Error {
    param([string]$Message)
    $script:Errors.Add($Message) | Out-Null
}

$Errors = [System.Collections.Generic.List[string]]::new()

if (-not $ChangedFiles -or $ChangedFiles.Count -eq 0) {
    if ($BaseRef) {
        $ChangedFiles = git -c core.autocrlf=false diff --name-only $BaseRef -- 2>$null | ForEach-Object { Normalize-PathForGit $_ }
    }
    elseif ($IncludeWorkingTree) {
        $ChangedFiles = git -c core.autocrlf=false diff --name-only -- 2>$null | ForEach-Object { Normalize-PathForGit $_ }
        $ChangedFiles += git -c core.autocrlf=false ls-files --others --exclude-standard 2>$null | ForEach-Object { Normalize-PathForGit $_ }
    }
    else {
        $ChangedFiles = git -c core.autocrlf=false diff --name-only --cached -- 2>$null | ForEach-Object { Normalize-PathForGit $_ }
    }
}
else {
    $ChangedFiles = $ChangedFiles | ForEach-Object { Normalize-PathForGit $_ }
}

$ChangedFiles = $ChangedFiles | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique

$forbiddenPathPatterns = @(
    "^IIoT\.CloudPlatform/",
    "^IIoT\.EdgeClient/",
    "^\.\./IIoT\.CloudPlatform/",
    "^\.\./IIoT\.EdgeClient/"
)

foreach ($file in $ChangedFiles) {
    foreach ($pattern in $forbiddenPathPatterns) {
        if ($file -match $pattern) {
            Add-Error "Forbidden Cloud/Edge path in Enterprise Data Governance scope: $file"
        }
    }
}

$appSettingsFiles = @(
    "src/hosts/AICopilot.HttpApi/appsettings.json",
    "src/hosts/AICopilot.HttpApi/appsettings.Development.json"
)

foreach ($path in $appSettingsFiles) {
    if (-not (Test-Path $path)) {
        Add-Error "Missing appsettings file: $path"
        continue
    }

    $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($json.CloudReadonly.Mode -ne "Disabled") {
        Add-Error "$path must keep CloudReadonly.Mode=Disabled by default."
    }
    if ($json.CloudAiRead.Enabled -ne $false) {
        Add-Error "$path must keep CloudAiRead.Enabled=false by default."
    }
    if (-not $json.CloudReadonlySandbox) {
        Add-Error "$path must define CloudReadonlySandbox for P6 sandbox smoke readiness."
    }
    elseif ($json.CloudReadonlySandbox.Enabled -ne $false) {
        Add-Error "$path must keep CloudReadonlySandbox.Enabled=false by default."
    }
    if (-not $json.CloudReadonlySandboxAgentTrial) {
        Add-Error "$path must define CloudReadonlySandboxAgentTrial for P7 sandbox agent trial governance."
    }
    elseif ($json.CloudReadonlySandboxAgentTrial.Enabled -ne $false) {
        Add-Error "$path must keep CloudReadonlySandboxAgentTrial.Enabled=false by default."
    }
    if (-not $json.CloudReadonlySandboxControlledTrial) {
        Add-Error "$path must define CloudReadonlySandboxControlledTrial for P8 controlled sandbox trial governance."
    }
    elseif ($json.CloudReadonlySandboxControlledTrial.Enabled -ne $false) {
        Add-Error "$path must keep CloudReadonlySandboxControlledTrial.Enabled=false by default."
    }
    if (-not $json.CloudReadonlyPilotReadiness) {
        Add-Error "$path must define CloudReadonlyPilotReadiness for P11 Pilot readiness rehearsal."
    }
    elseif ($json.CloudReadonlyPilotReadiness.Enabled -ne $false) {
        Add-Error "$path must keep CloudReadonlyPilotReadiness.Enabled=false by default."
    }
    if (-not $json.CloudReadonlyProductionPilot) {
        Add-Error "$path must define CloudReadonlyProductionPilot for P12 fixed-template production Pilot."
    }
    elseif ($json.CloudReadonlyProductionPilot.Enabled -ne $false) {
        Add-Error "$path must keep CloudReadonlyProductionPilot.Enabled=false by default."
    }
    if (-not $json.CloudReadonlyProductionControlledPilot) {
        Add-Error "$path must define CloudReadonlyProductionControlledPilot for P13 controlled production Pilot."
    }
    elseif ($json.CloudReadonlyProductionControlledPilot.Enabled -ne $false) {
        Add-Error "$path must keep CloudReadonlyProductionControlledPilot.Enabled=false by default."
    }
    elseif ($json.CloudReadonlyProductionControlledPilot.FreeGoalEnabled -ne $false) {
        Add-Error "$path must keep CloudReadonlyProductionControlledPilot.FreeGoalEnabled=false by default."
    }
    if (-not $json.Mcp -or -not $json.Mcp.Runtime) {
        Add-Error "$path must define Mcp.Runtime for P4 mock-only governance."
    }
    elseif ($json.Mcp.Runtime.Enabled -ne $false) {
        Add-Error "$path must keep Mcp.Runtime.Enabled=false by default."
    }
    elseif ($json.Mcp.Runtime.MockOnly -ne $true) {
        Add-Error "$path must keep Mcp.Runtime.MockOnly=true by default."
    }
}

$changedReadableFiles = $ChangedFiles | Where-Object { Test-Path $_ }
$csharpFiles = $changedReadableFiles | Where-Object { $_ -like "*.cs" -or $_ -like "src/*.cs" -or $_ -like "src/**/*.cs" }

foreach ($file in $csharpFiles) {
    $content = Get-Content -LiteralPath $file -Raw

    if ($content -match "ToolProviderType\.Shell|toolCode\s*=\s*""shell""|cmd\.exe|powershell\.exe|/bin/sh|ProcessStartInfo") {
        Add-Error "Shell capability pattern found in $file."
    }

    if ($content -match "File\.(WriteAllText|WriteAllBytes|AppendAllText|AppendAllLines|Create|OpenWrite)|Directory\.CreateDirectory|Path\.GetTempPath|Environment\.GetFolderPath" -and
        $file -notmatch "Infrastructure/Storage|Infrastructure/Artifacts|BackendTests|Migrations|Test-EnterpriseDataGovernanceScope") {
        Add-Error "Potential arbitrary filesystem write pattern found in $file."
    }

    if ($content -match "(?i)\b(insert|update|delete|drop|alter|create|truncate|merge)\b.+\b(table|into|from|set)\b" -and
        $file -notmatch "SimulationBusiness|Migrations|SqlGuardrail|DapperDatabaseConnector|AgentPlanToolGuard|MigrationWorkApp/Worker|BackendTests|Test-EnterpriseDataGovernanceScope") {
        Add-Error "Unexpected dangerous SQL-like pattern found in $file."
    }

    if ($content -match "(?i)(api[_-]?key|token|password|connectionstring|connection_string)\s*=\s*[""'][^""']{8,}[""']" -and
        $file -notmatch "Tests|Test-EnterpriseDataGovernanceScope") {
        Add-Error "Potential plaintext secret or connection string found in $file."
    }

    if ($content -match "RealCloudReadonly|CloudReadonlyDataSourceMode\.Real" -and
        $file -notmatch "CloudReadonlyDataProviders|CloudReadonlyOptions|CloudReadonlyContracts|DependencyInjection|BuiltInToolRegistrations|CloudReadonlyReadiness|BackendTests") {
        Add-Error "Unexpected Real CloudReadonly reference found in $file."
    }
}

$dataSourceContracts = "src/services/AICopilot.Services.Contracts/Contracts/DataSourceContracts.cs"
$businessQuery = "src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessDatabaseReadonlyQuery.cs"
$businessTextToSql = "src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessTextToSql.cs"
$promptPolicyManagement = "src/services/AICopilot.AiGatewayService/PromptPolicies/PromptPolicyManagement.cs"
$knowledgeGovernanceManagement = "src/services/AICopilot.RagService/Governance/KnowledgeGovernanceManagement.cs"
$modelProviderReliability = "src/infrastructure/AICopilot.AiRuntime/ModelProviderReliability.cs"
$mcpInfrastructure = "src/infrastructure/AICopilot.Infrastructure/DependencyInjection.cs"
$toolRegistrationAggregate = "src/core/AICopilot.Core.AiGateway/Aggregates/Tools/ToolRegistration.cs"
$builtInToolRegistrations = "src/core/AICopilot.Core.AiGateway/Aggregates/Tools/BuiltInToolRegistrations.cs"
$plannerToolCatalog = "src/services/AICopilot.AiGatewayService/AgentTasks/PlannerToolCatalog.cs"
$agentPlanDocument = "src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskPlanDocument.cs"
$mockMcpExecutor = "src/services/AICopilot.AiGatewayService/AgentTasks/MockMcpAgentToolExecutor.cs"
$toolRegistryManagement = "src/services/AICopilot.AiGatewayService/Tools/ToolRegistryManagement.cs"
$cloudReadonlyReadiness = "src/services/AICopilot.AiGatewayService/CloudReadiness/CloudReadonlyReadiness.cs"
$cloudReadonlyContracts = "src/services/AICopilot.Services.Contracts/Contracts/CloudReadonlyContracts.cs"
$cloudReadonlySandboxClient = "src/infrastructure/AICopilot.Infrastructure/CloudRead/CloudReadonlySandboxClient.cs"
$cloudReadonlySandboxAgentTrial = "src/services/AICopilot.AiGatewayService/CloudReadiness/CloudReadonlySandboxAgentTrial.cs"
$cloudReadonlySandboxControlledTrial = "src/services/AICopilot.AiGatewayService/CloudReadiness/CloudReadonlySandboxControlledTrial.cs"
$cloudReadonlyPilotReadiness = "src/services/AICopilot.AiGatewayService/CloudReadiness/CloudReadonlyPilotReadiness.cs"
$cloudReadonlyProductionPilot = "src/services/AICopilot.AiGatewayService/CloudReadiness/CloudReadonlyProductionPilot.cs"
$cloudReadonlyProductionControlledPilot = "src/services/AICopilot.AiGatewayService/CloudReadiness/CloudReadonlyProductionControlledPilot.cs"
$cloudReadonlyProductionOperations = "src/services/AICopilot.AiGatewayService/CloudReadiness/CloudReadonlyProductionOperations.cs"
$productionOperationsAggregate = "src/core/AICopilot.Core.AiGateway/Aggregates/ProductionOperations/ProductionPilotOperations.cs"
$productionOperationsEfConfig = "src/infrastructure/AICopilot.EntityFrameworkCore/Configuration/AiGateway/ProductionOperationConfiguration.cs"
$artifactAggregate = "src/core/AICopilot.Core.AiGateway/Aggregates/Artifacts/Artifact.cs"
$artifactWorkspaceManagement = "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactWorkspaceManagement.cs"
$artifactWorkspaceP9Management = "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactWorkspaceP9Management.cs"
$agentTaskRuntime = "src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskRuntime.cs"
$trialCampaignAggregate = "src/core/AICopilot.Core.AiGateway/Aggregates/TrialOperations/TrialCampaign.cs"
$trialOperationsManagement = "src/services/AICopilot.AiGatewayService/TrialOperations/TrialOperationsManagement.cs"
foreach ($requiredFile in @(
    $dataSourceContracts,
    $businessQuery,
    $businessTextToSql,
    $promptPolicyManagement,
    $knowledgeGovernanceManagement,
    $modelProviderReliability,
    $mcpInfrastructure,
    $toolRegistrationAggregate,
    $builtInToolRegistrations,
    $plannerToolCatalog,
    $agentPlanDocument,
    $mockMcpExecutor,
    $toolRegistryManagement,
    $cloudReadonlyReadiness,
    $cloudReadonlyContracts,
    $cloudReadonlySandboxClient,
    $cloudReadonlySandboxAgentTrial,
    $cloudReadonlySandboxControlledTrial,
    $cloudReadonlyPilotReadiness,
    $cloudReadonlyProductionPilot,
    $cloudReadonlyProductionControlledPilot,
    $cloudReadonlyProductionOperations,
    $productionOperationsAggregate,
    $productionOperationsEfConfig,
    $artifactAggregate,
    $artifactWorkspaceManagement,
    $artifactWorkspaceP9Management,
    $agentTaskRuntime,
    $trialCampaignAggregate,
    $trialOperationsManagement
)) {
    if (-not (Test-Path $requiredFile)) {
        Add-Error "Required Enterprise Data Governance file missing: $requiredFile"
    }
}

if (Test-Path $dataSourceContracts) {
    $content = Get-Content -LiteralPath $dataSourceContracts -Raw
    foreach ($required in @("SimulationBusiness", "BusinessQueryResultDto", "SourceMode", "IsSimulation", "SourceLabel")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required query contract marker '$required' is missing from $dataSourceContracts."
        }
    }
}

if (Test-Path $businessQuery) {
    $content = Get-Content -LiteralPath $businessQuery -Raw
    foreach ($required in @("BusinessDatabase", "SimulationBusiness", "SimulationSourceLabel", "ComputeQueryHash", "sqlLength")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required readonly query marker '$required' is missing from $businessQuery."
        }
    }
}

if (Test-Path $businessTextToSql) {
    $content = Get-Content -LiteralPath $businessTextToSql -Raw
    foreach ($required in @("BusinessTextToSqlDraftDto", "SimulationBusinessQuerySchema", "DataSource.TextToSqlDraft", "DataSource.TextToSqlExecute")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P1 Text-to-SQL marker '$required' is missing from $businessTextToSql."
        }
    }
}

if (Test-Path $promptPolicyManagement) {
    $content = Get-Content -LiteralPath $promptPolicyManagement -Raw
    foreach ($required in @("PromptPolicyDto", "ActivatePromptPolicyVersionCommand", "systemPromptHash", "outputFormatHash")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P1 Prompt Policy marker '$required' is missing from $promptPolicyManagement."
        }
    }
}

if (Test-Path $knowledgeGovernanceManagement) {
    $content = Get-Content -LiteralPath $knowledgeGovernanceManagement -Raw
    foreach ($required in @("UpsertKnowledgeCategoryCommand", "UpsertKnowledgeSupplementCommand", "KnowledgeSupplementPriority", "GetApplicableKnowledgeSupplementsQuery")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P1 RAG governance marker '$required' is missing from $knowledgeGovernanceManagement."
        }
    }
}

if (Test-Path $modelProviderReliability) {
    $content = Get-Content -LiteralPath $modelProviderReliability -Raw
    foreach ($required in @("EndpointPools", "LeastInFlight", "WeightedRoundRobin", "RecordStickyStreaming", "QueueLimit", "CircuitState")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P1 model pool marker '$required' is missing from $modelProviderReliability."
        }
    }
}

if (Test-Path $mcpInfrastructure) {
    $content = Get-Content -LiteralPath $mcpInfrastructure -Raw
    foreach ($required in @('GetValue("Mcp:Runtime:Enabled", false)', "AddMcpRuntime")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P4 mock-only MCP runtime marker '$required' is missing from $mcpInfrastructure."
        }
    }
}

if (Test-Path $toolRegistrationAggregate) {
    $content = Get-Content -LiteralPath $toolRegistrationAggregate -Raw
    foreach ($required in @("ToolProviderType", "MockMcp", "ToolDataBoundary", "IsVisibleToPlanner", "IsExecutableByAgent", "CatalogVersion", "ApprovalPolicy", "Critical")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P4 Tool Registry marker '$required' is missing from $toolRegistrationAggregate."
        }
    }
}

if (Test-Path $builtInToolRegistrations) {
    $content = Get-Content -LiteralPath $builtInToolRegistrations -Raw
    foreach ($required in @("CurrentCatalogVersion", "mock_mcp_health_check", "mock_mcp_kpi_formula_lookup", "mock_mcp_artifact_quality_check", "mock_mcp_external_ticket_preview", "MockMcpProvider")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P4 built-in Mock MCP marker '$required' is missing from $builtInToolRegistrations."
        }
    }

    foreach ($required in @("query_cloud_data_readonly", "IsEnabled: false", "IsVisibleToPlanner: false", "IsExecutableByAgent: false", "DisabledRealCloudReadonly")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P5 CloudReadonly tool-disabled marker '$required' is missing from $builtInToolRegistrations."
        }
    }

    foreach ($required in @("query_cloud_sandbox_readonly", "CloudReadonlySandboxOnly", "SandboxAgentTrial", "AiGateway.ToolRegistry.Execute")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P7 CloudReadonly sandbox tool marker '$required' is missing from $builtInToolRegistrations."
        }
    }

    foreach ($required in @("query_cloud_pilot_readiness_readonly", "CloudReadonlyPilotReadinessOnly", "PilotReadinessRehearsalOnly", "IsEnabled: false", "IsVisibleToPlanner: false", "IsExecutableByAgent: false")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P11 Pilot readiness tool marker '$required' is missing from $builtInToolRegistrations."
        }
    }

    foreach ($required in @("query_cloud_production_pilot_readonly", "CloudReadonlyProductionPilotOnly", "ProductionPilotToolApproval", "IsEnabled: false", "IsVisibleToPlanner: false", "IsExecutableByAgent: false")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P12 Production Pilot tool marker '$required' is missing from $builtInToolRegistrations."
        }
    }

    foreach ($required in @("query_cloud_production_controlled_readonly", "CloudReadonlyProductionControlledOnly", "ProductionControlledPilotToolApproval", "IsEnabled: false", "IsVisibleToPlanner: false", "IsExecutableByAgent: false")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P13 Production Controlled Pilot tool marker '$required' is missing from $builtInToolRegistrations."
        }
    }
}

if (Test-Path $plannerToolCatalog) {
    $content = Get-Content -LiteralPath $plannerToolCatalog -Raw
    foreach ($required in @("ToolProviderType.MockMcp", "ProviderKind", "IsMock", "DataBoundary", "CatalogVersion")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P4 planner catalog marker '$required' is missing from $plannerToolCatalog."
        }
    }
}

if (Test-Path $agentPlanDocument) {
    $content = Get-Content -LiteralPath $agentPlanDocument -Raw
    foreach ($required in @("toolCatalogVersion", "visibleToolCount", "toolRiskSummary", "mockMcpOnly", "toolApprovalCheckpoints")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P4 plan document marker '$required' is missing from $agentPlanDocument."
        }
    }
}

if (Test-Path $mockMcpExecutor) {
    $content = Get-Content -LiteralPath $mockMcpExecutor -Raw
    foreach ($required in @("isMock", "providerKind", "toolRunId", "toolCatalogVersion", "resultHash", "mock_mcp_health_check", "mock_mcp_external_ticket_preview")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P4 Mock MCP executor marker '$required' is missing from $mockMcpExecutor."
        }
    }
}

if (Test-Path $toolRegistryManagement) {
    $content = Get-Content -LiteralPath $toolRegistryManagement -Raw
    foreach ($required in @("GetToolCatalogQuery", "ToolRunAuditDto", "UpsertToolDefinitionCommand", "ActivateToolDefinitionVersionCommand", "DisableToolDefinitionCommand")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P4 Tool Registry management marker '$required' is missing from $toolRegistryManagement."
        }
    }
}

if (Test-Path $cloudReadonlyReadiness) {
    $content = Get-Content -LiteralPath $cloudReadonlyReadiness -Raw
    foreach ($required in @("CloudReadonlyReadinessDto", "RunCloudReadonlyReadinessCheckCommand", "GetCloudReadonlyReadinessHistoryQuery", "ReadinessOnly", "FakeEndpoint", "RealSandboxSmoke", "recipe_versions", "CloudAiReadEndpointPolicy")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P5 CloudReadonly readiness marker '$required' is missing from $cloudReadonlyReadiness."
        }
    }

    foreach ($required in @("CloudReadonlySandboxStatusDto", "GetCloudReadonlySandboxStatusQuery", "GetCloudReadonlySandboxSmokeHistoryQuery", "SandboxSmokeOnly", "CloudReadonlySandbox", "CloudReadonly.Mode must remain Disabled during P6 sandbox smoke", "query_cloud_data_readonly must remain disabled by default in P5/P6")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P6 CloudReadonly sandbox marker '$required' is missing from $cloudReadonlyReadiness."
        }
    }
}

if (Test-Path $cloudReadonlySandboxAgentTrial) {
    $content = Get-Content -LiteralPath $cloudReadonlySandboxAgentTrial -Raw
    foreach ($required in @("CloudReadonlySandboxAgentTrialStatusDto", "RunCloudReadonlySandboxAgentTrialCommand", "CloudReadonlySandboxAgentTrialOptions", "CloudReadonlySandboxAgentTrialMarkers", "CloudReadonlySandbox", "SandboxAgentTrial", "CloudReadonlySandboxAgentTrial only allows fixed sandbox trial scenarios")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P7 CloudReadonly sandbox agent trial marker '$required' is missing from $cloudReadonlySandboxAgentTrial."
        }
    }
}

if (Test-Path $cloudReadonlySandboxControlledTrial) {
    $content = Get-Content -LiteralPath $cloudReadonlySandboxControlledTrial -Raw
    foreach ($required in @("CloudReadonlySandboxControlledTrialStatusDto", "CloudSandboxGoalIntentDto", "CreateCloudReadonlySandboxControlledPlanCommand", "SandboxControlledTrial", "CloudReadonlySandboxControlledTrialMarkers.Boundary", "BlockedByPolicy", "CloudReadonlySandboxControlledTrial is not ready")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P8 CloudReadonly controlled sandbox trial marker '$required' is missing from $cloudReadonlySandboxControlledTrial."
        }
    }
}

if (Test-Path $cloudReadonlyContracts) {
    $content = Get-Content -LiteralPath $cloudReadonlyContracts -Raw
    foreach ($required in @("CloudReadonlySandboxOptions", "SectionName = ""CloudReadonlySandbox""", "ServiceAccountToken", "IsConfigured", "EnsureValid")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P6 CloudReadonly sandbox contract marker '$required' is missing from $cloudReadonlyContracts."
        }
    }

    foreach ($required in @("CloudReadonlySandboxAgentTrialOptions", "SectionName = ""CloudReadonlySandboxAgentTrial""", "CloudReadonlySandboxAgentTrialMarkers", "SourceMode = ""CloudReadonlySandbox""", "Boundary = ""SandboxAgentTrial""", "ToolCode = ""query_cloud_sandbox_readonly""")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P7 CloudReadonly sandbox agent trial contract marker '$required' is missing from $cloudReadonlyContracts."
        }
    }

    foreach ($required in @("CloudReadonlySandboxControlledTrialOptions", "SectionName = ""CloudReadonlySandboxControlledTrial""", "CloudReadonlySandboxControlledTrialMarkers", "Boundary = ""SandboxControlledTrial""", "TrialMode = ""ControlledGoal""")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P8 CloudReadonly controlled sandbox trial contract marker '$required' is missing from $cloudReadonlyContracts."
        }
    }

    foreach ($required in @("CloudReadonlyPilotReadinessOptions", "SectionName = ""CloudReadonlyPilotReadiness""", "CloudReadonlyPilotReadinessMarkers", "SourceMode = ""CloudReadonlyPilotReadiness""", "Boundary = ""PilotReadinessRehearsal""", "ToolCode = ""query_cloud_pilot_readiness_readonly""")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P11 Pilot readiness contract marker '$required' is missing from $cloudReadonlyContracts."
        }
    }

    foreach ($required in @("CloudReadonlyProductionPilotOptions", "SectionName = ""CloudReadonlyProductionPilot""", "CloudReadonlyProductionPilotMarkers", "SourceMode = ""CloudReadonlyProductionPilot""", "Boundary = ""ProductionPilot""", "ToolCode = ""query_cloud_production_pilot_readonly""")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P12 Production Pilot contract marker '$required' is missing from $cloudReadonlyContracts."
        }
    }

    foreach ($required in @("CloudReadonlyProductionControlledPilotOptions", "SectionName = ""CloudReadonlyProductionControlledPilot""", "CloudReadonlyProductionControlledPilotMarkers", "SourceMode = ""CloudReadonlyProductionControlledPilot""", "Boundary = ""ProductionControlledPilot""", "ToolCode = ""query_cloud_production_controlled_readonly""")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P13 Production Controlled Pilot contract marker '$required' is missing from $cloudReadonlyContracts."
        }
    }
}

if (Test-Path $cloudReadonlySandboxClient) {
    $content = Get-Content -LiteralPath $cloudReadonlySandboxClient -Raw
    foreach ($required in @("ICloudReadonlySandboxClient", "CloudReadonlySandboxOptions", "CloudAiReadEndpointPolicy", "AuthenticationHeaderValue", "CloudReadonlySandbox endpoint is unavailable")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P6 CloudReadonly sandbox client marker '$required' is missing from $cloudReadonlySandboxClient."
        }
    }
}

if (Test-Path $artifactAggregate) {
    $content = Get-Content -LiteralPath $artifactAggregate -Raw
    foreach ($required in @("ArtifactSourceMetadata", "SourceMode", "Boundary", "IsSimulation", "IsSandbox", "FinalizedAt", "ApplySourceMetadata")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P9 artifact metadata marker '$required' is missing from $artifactAggregate."
        }
    }
}

if (Test-Path $artifactWorkspaceManagement) {
    $content = Get-Content -LiteralPath $artifactWorkspaceManagement -Raw
    foreach ($required in @("DraftArtifacts", "FinalArtifacts", "ArtifactVersion", "ArtifactStatus", "SourceMode", "Boundary", "FinalizedAt")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P9 workspace DTO marker '$required' is missing from $artifactWorkspaceManagement."
        }
    }
}

if (Test-Path $artifactWorkspaceP9Management) {
    $content = Get-Content -LiteralPath $artifactWorkspaceP9Management -Raw
    foreach ($required in @("AgentArtifactPreviewDto", "CreateArtifactRevisionCommentCommand", "RegenerateDraftArtifactCommand", "SubmitArtifactForFinalApprovalCommand", "ArtifactWorkspaceP9Policy", "ArtifactPreviewBuilder")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P9 artifact workspace marker '$required' is missing from $artifactWorkspaceP9Management."
        }
    }
}

if (Test-Path $agentTaskRuntime) {
    $content = Get-Content -LiteralPath $agentTaskRuntime -Raw
    foreach ($required in @("BuildArtifactSourceMetadata", "SimulationBusiness", "CloudReadonlySandbox", "ArtifactSourceMetadata")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P9 artifact source propagation marker '$required' is missing from $agentTaskRuntime."
        }
    }
}

if (Test-Path $trialCampaignAggregate) {
    $content = Get-Content -LiteralPath $trialCampaignAggregate -Raw
    foreach ($required in @("TrialCampaignStatus", "PilotReadinessStatus", "ReadyForP11Planning", "SimulationBusiness", "CloudReadonlySandbox", "TrialRiskStatus", "trial_source_mode_blocked")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P10 trial campaign marker '$required' is missing from $trialCampaignAggregate."
        }
    }
}

if (Test-Path $trialOperationsManagement) {
    $content = Get-Content -LiteralPath $trialOperationsManagement -Raw
    foreach ($required in @("TrialCampaignDto", "TrialScenarioRunDto", "TrialRiskIssueDto", "PilotReadinessAssessmentDto", "TrialEvidencePackageDto", "AttachAgentTaskToTrialCampaignCommand", "RunPilotReadinessEvaluationCommand", "GenerateTrialEvidencePackageCommand", "query_cloud_data_readonly", "ReadyForP11Planning")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P10 trial operations marker '$required' is missing from $trialOperationsManagement."
        }
    }
}

if (Test-Path $cloudReadonlyPilotReadiness) {
    $content = Get-Content -LiteralPath $cloudReadonlyPilotReadiness -Raw
    foreach ($required in @("CloudReadonlyPilotReadinessStatusDto", "CloudReadonlyPilotConfigPackageDto", "PilotApprovalRehearsalDto", "RunCloudReadonlyPilotContractRehearsalCommand", "CloudReadonlyPilotReadinessStatuses", "PilotReadinessRehearsal", "IsProductionData", "BlockedByPolicy", "query_cloud_data_readonly must remain disabled")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P11 Pilot readiness marker '$required' is missing from $cloudReadonlyPilotReadiness."
        }
    }
}

if (Test-Path $cloudReadonlyProductionPilot) {
    $content = Get-Content -LiteralPath $cloudReadonlyProductionPilot -Raw
    foreach ($required in @("CloudReadonlyProductionPilotStatusDto", "CloudReadonlyProductionPilotWindowDto", "RunCloudReadonlyProductionPilotScenarioCommand", "CloudReadonlyProductionPilotStatuses", "CloudReadonlyProductionPilotMarkers.Boundary", "ProductionPilot", "isProductionData", "CloudReadonlyProductionPilot is not ready")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P12 Production Pilot marker '$required' is missing from $cloudReadonlyProductionPilot."
        }
    }
}

if (Test-Path $cloudReadonlyProductionControlledPilot) {
    $content = Get-Content -LiteralPath $cloudReadonlyProductionControlledPilot -Raw
    foreach ($required in @("CloudReadonlyProductionControlledPilotStatusDto", "CloudProductionGoalIntentDto", "CreateCloudReadonlyProductionControlledPlanCommand", "RunCloudReadonlyProductionControlledPilotCommand", "CloudReadonlyProductionControlledPilotStatuses", "CloudReadonlyProductionControlledPilotMarkers.Boundary", "ProductionControlledPilot", "isProductionData", "BlockedByPolicy", "CloudReadonlyProductionControlledPilot is not ready")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P13 Production Controlled Pilot marker '$required' is missing from $cloudReadonlyProductionControlledPilot."
        }
    }
}

if (Test-Path $cloudReadonlyProductionOperations) {
    $content = Get-Content -LiteralPath $cloudReadonlyProductionOperations -Raw
    foreach ($required in @("CloudReadonlyProductionOperationsStatusDto", "ProductionPilotRunLedgerDto", "ProductionPilotIncidentDto", "ActivateProductionPilotEmergencyStopCommand", "ClearProductionPilotEmergencyStopCommand", "RunProductionPilotGaReadinessEvaluationCommand", "ReadyForP15Planning", "RepositoryProductionPilotOperationsStore", "HasCompletedP12Evidence", "HasCompletedP13Evidence", "query_cloud_data_readonly")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P14 Production Operations marker '$required' is missing from $cloudReadonlyProductionOperations."
        }
    }
}

if (Test-Path $productionOperationsAggregate) {
    $content = Get-Content -LiteralPath $productionOperationsAggregate -Raw
    foreach ($required in @("ProductionPilotEmergencyStopState", "ProductionPilotIncident", "ProductionPilotRunLedger", "ProductionPilotGaReadinessAssessment", "QueryHash", "ResultHash")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P14.2 Production Operations persistence marker '$required' is missing from $productionOperationsAggregate."
        }
    }
}

if (Test-Path $productionOperationsEfConfig) {
    $content = Get-Content -LiteralPath $productionOperationsEfConfig -Raw
    foreach ($required in @("production_pilot_emergency_stop_states", "production_pilot_incidents", "production_pilot_run_ledgers", "production_pilot_ga_readiness_assessments", "checks_json")) {
        if ($content -notmatch [regex]::Escape($required)) {
            Add-Error "Required P14.2 Production Operations EF marker '$required' is missing from $productionOperationsEfConfig."
        }
    }
}

if ($Errors.Count -gt 0) {
    Write-Host "Enterprise Data Governance scope guard failed:" -ForegroundColor Red
    foreach ($errorItem in $Errors) {
        Write-Host " - $errorItem" -ForegroundColor Red
    }
    exit 1
}

Write-Host "Enterprise Data Governance scope guard passed. Checked $($ChangedFiles.Count) candidate file(s)." -ForegroundColor Green
