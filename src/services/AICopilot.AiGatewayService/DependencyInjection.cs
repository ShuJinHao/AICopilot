using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.BusinessSemantics;
using AICopilot.AiGatewayService.BusinessPolicies;
using AICopilot.AiGatewayService.Observability;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.AiGatewayService.RoutingModels;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Uploads;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Behaviors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System.Reflection;

namespace AICopilot.AiGatewayService;

public static class DependencyInjection
{
    public static void AddAiGatewayService(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<CloudReadonlyOptions>(
            builder.Configuration.GetSection(CloudReadonlyOptions.SectionName));
        builder.Services.Configure<CloudAiReadOptions>(
            builder.Configuration.GetSection(CloudAiReadOptions.SectionName));
        builder.Services.Configure<AgentRunQueueOptions>(
            builder.Configuration.GetSection(AgentRunQueueOptions.SectionName));
        builder.Services.Configure<MockMcpOptions>(
            builder.Configuration.GetSection(MockMcpOptions.SectionName));
        builder.Services.Configure<AgentModelCallTimeoutOptions>(
            builder.Configuration.GetSection(AgentModelCallTimeoutOptions.SectionName));
        builder.Services.Configure<CloudReadOnlyTextToSqlOptions>(
            builder.Configuration.GetSection(CloudReadOnlyTextToSqlOptions.SectionName));

        builder.Services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
        });

        builder.Services.AddScoped<ConfiguredAgentRuntimeFactory>();
        builder.Services.AddScoped<IAgentExecutionMetadataAccessor, AgentExecutionMetadataAccessor>();
        builder.Services.AddScoped<IRoutingModelResolver, RoutingModelResolver>();
        builder.Services.AddScoped<IAgentRuntimeSettingsProvider, AgentRuntimeSettingsProvider>();
        builder.Services.AddScoped<IAgentArtifactWorkspaceService, AgentArtifactWorkspaceService>();
        builder.Services.AddScoped<IAgentTaskRuntime, AgentTaskRuntime>();
        builder.Services.AddScoped<IAgentPlanRuntimeSnapshotVerifier, AgentPlanRuntimeSnapshotVerifier>();
        builder.Services.AddScoped<IAgentPlanAuthorizationFreshVerifier, AgentPlanAuthorizationFreshVerifier>();
        builder.Services.AddScoped<IAgentTaskRunQueue, AgentTaskRunQueue>();
        AddScopedComponents(builder.Services,
            typeof(AgentTaskDtoQueryService), typeof(AgentTaskPlanPreparationService), typeof(PlanAgentTaskCoordinator), typeof(AgentTaskLifecycleCoordinator),
            typeof(AgentApprovalQueryCoordinator), typeof(AgentApprovalDecisionCoordinator), typeof(ArtifactWorkspaceLifecycleCoordinator),
            typeof(ArtifactWorkspaceQueryCoordinator), typeof(ArtifactVersioningQueryCoordinator), typeof(ArtifactVersioningCommandCoordinator),
            typeof(ArtifactWorkspaceP9Coordinator), typeof(AgentRuntimeEventRecorder), typeof(AgentTaskRunQueueWorkerCoordinator),
            typeof(DurableTaskClaimCoordinator), typeof(AgentNodeRunMaterializer), typeof(NodeRunClaimCoordinator),
            typeof(NodeCheckpointCoordinator));
        AddAgentRuntimeComponents(builder.Services);
        AddScopedComponents(builder.Services,
            typeof(AgentFinalizationNodeExecutor), typeof(NodeOutcomeReconciliationCoordinator),
            typeof(AgentArtifactReferenceEvidenceResolver), typeof(AgentArtifactFileSetCheckpointGate),
            typeof(AgentRuntimeWriteAuthorityAccessor), typeof(SessionTimelineQueryCoordinator),
            typeof(AgentTaskAuditQueryCoordinator), typeof(AgentTaskToolExecutionQueryCoordinator),
            typeof(UploadRecordCoordinator));
        builder.Services.AddScoped<IAgentWorkspaceFingerprintProvider, AgentWorkspaceFingerprintProvider>();
        builder.Services.AddScoped<IAgentWorkerHeartbeatService, AgentWorkerHeartbeatService>();
        builder.Services.AddScoped<ICloudReadonlyAgentPlanService, CloudReadonlyAgentPlanService>();
        builder.Services.AddScoped<IBusinessQueryProvider, CloudAiReadBusinessQueryProvider>();
        builder.Services.AddScoped<IBusinessQueryProviderRegistry, BusinessQueryProviderRegistry>();
        var mockMcpEnabled = builder.Environment.IsDevelopment() &&
                             builder.Configuration
                                 .GetSection(MockMcpOptions.SectionName)
                                 .Get<MockMcpOptions>() is { Enabled: true };
        if (mockMcpEnabled)
        {
            builder.Services.AddScoped<IAgentToolExecutor, MockMcpAgentToolExecutor>();
        }

        builder.Services.AddScoped<IAgentToolExecutor, McpAgentToolExecutor>();
        builder.Services.AddScoped<IMcpToolRegistryReadService, McpToolRegistryReadService>();
        builder.Services.AddScoped<ToolRegistryGuard>();
        builder.Services.AddScoped<AgentPlanToolGuard>();
        builder.Services.AddScoped<AgentPlanDraftConfirmationService>();
        builder.Services.AddScoped<AgentTaskPlanFreshReadGate>();
        builder.Services.AddSingleton<AgentPlanCanonicalizer>();
        builder.Services.AddSingleton<IAgentPlanIntegrityValidator>(provider =>
            provider.GetRequiredService<AgentPlanCanonicalizer>());
        builder.Services.AddSingleton<IAgentTaskPlanPersistencePolicy, AgentTaskPlanPersistencePolicy>();
        builder.Services.AddSingleton<AgentIntentRegistryProjector>();
        builder.Services.AddSingleton<IAgentPlanCompiler, DeterministicAgentPlanCompiler>();
        builder.Services.AddSingleton(provider => new AgentPlanDraftContractAuthority(
            provider.GetRequiredService<AgentIntentRegistryProjector>(),
            provider.GetRequiredService<AgentPlanCanonicalizer>(),
            provider.GetRequiredService<IAgentPlanCompiler>()));
        builder.Services.AddScoped<AgentAuditRecorder>();
        builder.Services.TryAddSingleton<ISessionExecutionLock, InMemorySessionExecutionLock>();
        builder.Services.AddSingleton<IRequestValidator<ChatStreamRequest>, ChatStreamRequestValidator>();
        builder.Services.AddSingleton<IRequestValidator<ApprovalDecisionStreamRequest>, ApprovalDecisionStreamRequestValidator>();
        builder.Services.AddSingleton<IRequestValidator<PlanAgentTaskCommand>, PlanAgentTaskCommandValidator>();
        builder.Services.AddSingleton<IOperationalBoundaryPolicy, ManufacturingOperationalBoundaryPolicy>();
        builder.Services.AddSingleton<IManufacturingSceneClassifier, KeywordManufacturingSceneClassifier>();
        builder.Services.AddSingleton<ITokenBudgetPolicy, ChatTokenBudgetPolicy>();
        builder.Services.AddSingleton<IChatTokenTelemetry, ChatTokenTelemetry>();
        builder.Services.AddScoped<ApprovalRequirementResolver>();
        builder.Services.AddScoped<IAgentStreamRuntime, AgentStreamRuntime>();
        builder.Services.AddScoped<IApprovalRequirementReadService, ApprovalRequirementReadService>();
        builder.Services.AddScoped<ApprovalToolResolver>();
        builder.Services.AddScoped<IFinalAgentContextSerializer, FinalAgentContextSerializer>();
        builder.Services.AddScoped<SessionMessagePersistenceService>();
        builder.Services.AddScoped<MessageTimelineProjectionWriter>();
        builder.Services.AddSingleton<IBusinessPolicyCatalog, BusinessPolicyCatalog>();
        builder.Services.AddSingleton<ISemanticSummaryProfileCatalog, SemanticSummaryProfileCatalog>();
        builder.Services.AddSingleton<IBusinessSemanticsCatalog, BusinessSemanticsCatalog>();

        builder.Services.AddAgentPlugin(registrar =>
        {
            registrar.RegisterPluginFromAssembly(Assembly.GetExecutingAssembly());
        });

        AddScopedComponents(builder.Services,
            typeof(IntentRoutingAgentBuilder),
            typeof(IntentRoutingExecutor), typeof(KnowledgeRetrievalExecutor),
            typeof(SemanticAnalysisRunner), typeof(BusinessTextToSqlFallbackRunner),
            typeof(DataAnalysisWidgetEmitter), typeof(DataAnalysisAuditRecorder), typeof(ToolExecutionAuditRecorder),
            typeof(DataAnalysisExecutor), typeof(BusinessPolicyExecutor), typeof(ContextAggregatorExecutor),
            typeof(FinalAgentBuildExecutor), typeof(FinalAgentRunExecutor), typeof(AgentWorkflowPipeline));
        builder.Services.AddScoped<IAgentRoutingConfigurationSnapshotReader>(services =>
            services.GetRequiredService<IntentRoutingAgentBuilder>());
        builder.Services.AddScoped<IBusinessTextToSqlGenerator, BusinessLlmTextToSqlGenerator>();
        builder.Services.AddScoped<IBusinessTextToSqlFallbackRunner>(services =>
            services.GetRequiredService<BusinessTextToSqlFallbackRunner>());
        builder.Services.AddScoped<IAgentTaskChatEvidenceProvider, AgentTaskChatEvidenceProvider>();
    }

    private static void AddAgentRuntimeComponents(IServiceCollection services)
    {
        AddScopedComponents(
            services,
            typeof(AgentRuntimeArtifactBuilder), typeof(AgentReasoningNodeExecutor),
            typeof(AgentBuiltInToolDispatcher), typeof(AgentParallelReadNodeExecutor));
        services.AddScoped<IAgentOutcomeAuthorityProbe, ArtifactFileSetOutcomeAuthorityProbe>();
    }

    private static void AddScopedComponents(IServiceCollection services, params Type[] componentTypes)
    {
        foreach (var componentType in componentTypes)
        {
            services.AddScoped(componentType);
        }
    }
}
