using System.Linq.Expressions;
using System.Net.Http;
using System.Text;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Skills;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Uploads;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Skills;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using AICopilot.SharedKernel.Specification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AICopilot.AgentWorkflowTestKit;

public abstract class ToolRegistryGovernanceTestBase
{
    internal static readonly Guid UserId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    internal static AgentTaskRuntime CreateRuntime(
        IRepository<AgentTask> taskRepository,
        IRepository<ArtifactWorkspace> workspaceRepository,
        IRepository<ApprovalRequest> approvalRepository,
        IToolExecutionAuditStore executionRepository,
        ToolRegistryGuard guard,
        IAgentArtifactWorkspaceService? workspaceService = null,
        ICloudReadonlyAgentToolExecutor? cloudReadonlyToolExecutor = null,
        IEnumerable<IAgentToolExecutor>? toolExecutors = null,
        IAgentTaskRunAttemptStore? runAttemptRepository = null,
        IEnumerable<IKnowledgeBaseAccessChecker>? knowledgeBaseAccessCheckers = null,
        IKnowledgeRetrievalService? knowledgeRetrievalService = null,
        IIdentityAccessService? identityAccessService = null,
        SkillDefinitionGuard? skillDefinitionGuard = null,
        AgentTaskPlanFreshReadGate? freshReadGate = null,
        IAuditLogWriter? auditLogWriter = null)
    {
        _ = skillDefinitionGuard;
        return new AgentTaskRuntime(
            taskRepository,
            runAttemptRepository ?? new InMemoryAgentTaskRunAttemptStore(),
            workspaceRepository,
            approvalRepository,
            new InMemoryRepository<UploadRecord>(),
            workspaceService ?? new ThrowingWorkspaceService(),
            new CapturingFileStorage(),
            new NoopTableFileParser(),
            new NoopDocumentGenerator(),
            knowledgeRetrievalService ?? new NoopKnowledgeRetrievalService(),
            knowledgeBaseAccessCheckers ?? [],
            cloudReadonlyToolExecutor ?? new ThrowingCloudReadonlyAgentToolExecutor(),
            identityAccessService ?? new StubIdentityAccessService([]),
            guard,
            new MatchingAgentPlanRuntimeSnapshotVerifier(),
            new AgentRuntimeEventRecorder(
                executionRepository,
            new AgentAuditRecorder(auditLogWriter ?? new CapturingAuditLogWriter())),
            toolExecutors ?? [],
            freshReadGate ?? AgentPlanV2TestData.CreateMatchingFreshReadGate());
    }

    private sealed class MatchingAgentPlanRuntimeSnapshotVerifier : IAgentPlanRuntimeSnapshotVerifier
    {
        public Task<Result> VerifyAsync(
            AgentTaskPlanDocument plan,
            Guid userId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result.Success());
        }
    }

    internal static AgentTaskLifecycleCoordinator CreateLifecycleCoordinator(
        IRepository<AgentTask> taskRepository,
        IRepository<ArtifactWorkspace> workspaceRepository,
        IRepository<ApprovalRequest> approvalRepository,
        IAgentTaskRunQueueStore queueRepository,
        IAgentTaskRunAttemptStore? runAttemptRepository = null,
        IOptions<AgentRunQueueOptions>? options = null,
        AgentAuditRecorder? auditRecorder = null,
        AgentTaskPlanFreshReadGate? freshReadGate = null)
    {
        var effectiveFreshReadGate = freshReadGate ?? AgentPlanV2TestData.CreateMatchingFreshReadGate();
        return new AgentTaskLifecycleCoordinator(
            taskRepository,
            approvalRepository,
            workspaceRepository,
            queueRepository,
            new InMemoryAgentTaskCancellationStore(
                taskRepository,
                approvalRepository,
                queueRepository,
                runAttemptRepository ?? new InMemoryAgentTaskRunAttemptStore()),
            new AgentTaskRunQueue(
                queueRepository,
                effectiveFreshReadGate),
            effectiveFreshReadGate,
            options,
            auditRecorder);
    }

    internal static AgentTaskDtoQueryService CreateAgentTaskDtoQueryService(
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<ApprovalRequest> approvalRepository,
        IAgentTaskRunQueueStore queueRepository)
    {
        return new AgentTaskDtoQueryService(
            workspaceRepository,
            approvalRepository,
            queueRepository);
    }

    internal static AgentTaskAuditQueryCoordinator CreateAuditQueryCoordinator(
        IRepository<AgentTask> taskRepository,
        IReadRepository<ArtifactWorkspace>? workspaceRepository = null,
        IToolExecutionAuditStore? executionRepository = null,
        IAuditLogQueryService? auditLogQueryService = null,
        IAgentTaskRunAttemptStore? runAttemptRepository = null,
        IAgentTaskRunQueueStore? queueRepository = null,
        ICurrentUser? currentUser = null)
    {
        return new AgentTaskAuditQueryCoordinator(
            taskRepository,
            workspaceRepository ?? new InMemoryRepository<ArtifactWorkspace>(),
            executionRepository ?? new InMemoryToolExecutionAuditStore(),
            auditLogQueryService ?? new FixedAuditLogQueryService(),
            runAttemptRepository ?? new InMemoryAgentTaskRunAttemptStore(),
            queueRepository ?? new InMemoryAgentTaskRunQueueStore(),
            currentUser ?? new TestCurrentUser(UserId));
    }

    internal static (AgentTask Task, ArtifactWorkspace Workspace) CreateApprovedTask(
        string toolCode,
        bool requiresApproval = false,
        string? skillCode = null)
    {
        var now = DateTimeOffset.UtcNow;
        var planJson = AgentPlanV2TestData.CreateSingleStep(
            toolCode,
            executable: false,
            requiresApproval: requiresApproval,
            skillCode: skillCode);
        var task = new AgentTask(
            SessionId.New(),
            UserId,
            "生成报告",
            "生成报告",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            planJson,
            now);
        var trackedSteps = AddTrackedPlanSteps(task, planJson, now);
        var workspace = new ArtifactWorkspace(
            task.Id,
            $"ws_{Guid.NewGuid():N}",
            @"C:\aicopilot-workspaces\test",
            "/api/aigateway/workspaces/test",
            now);
        task.AttachWorkspace(workspace.Id, now);
        task.ConfirmExecutablePlan(
            task.PlanJson,
            trackedSteps.Where(step => step.RequiresApproval).Select(step => step.StepIndex).ToArray(),
            now);
        task.ApprovePlan(now);
        return (task, workspace);
    }

    internal static (AgentTask Task, ArtifactWorkspace Workspace) CreateRagApprovedTask(Guid knowledgeBaseId)
    {
        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            SessionId.New(),
            UserId,
            "RAG admin-only search",
            "RAG admin-only search",
            AgentTaskType.DataAnalysis,
            AgentTaskRiskLevel.Low,
            null,
            CreateRagPlanJson(knowledgeBaseId),
            now);
        var trackedSteps = AddTrackedPlanSteps(task, task.PlanJson, now);
        var workspace = new ArtifactWorkspace(
            task.Id,
            $"ws_{Guid.NewGuid():N}",
            @"C:\aicopilot-workspaces\test",
            "/api/aigateway/workspaces/test",
            now);
        task.AttachWorkspace(workspace.Id, now);
        task.ConfirmExecutablePlan(
            task.PlanJson,
            trackedSteps.Where(step => step.RequiresApproval).Select(step => step.StepIndex).ToArray(),
            now);
        task.ApprovePlan(now);
        return (task, workspace);
    }

    internal static (AgentTask Task, ArtifactWorkspace Workspace) CreateCloudApprovedTask()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            SessionId.New(),
            UserId,
            "Cloud readonly report",
            "Cloud readonly report",
            AgentTaskType.CloudDataReport,
            AgentTaskRiskLevel.Medium,
            null,
            CreateCloudPlanJson(),
            now);
        var trackedSteps = AddTrackedPlanSteps(task, task.PlanJson, now);
        var workspace = new ArtifactWorkspace(
            task.Id,
            $"ws_{Guid.NewGuid():N}",
            @"C:\aicopilot-workspaces\test",
            "/api/aigateway/workspaces/test",
            now);
        task.AttachWorkspace(workspace.Id, now);
        task.ConfirmExecutablePlan(
            task.PlanJson,
            trackedSteps.Where(step => step.RequiresApproval).Select(step => step.StepIndex).ToArray(),
            now);
        task.ApprovePlan(now);
        return (task, workspace);
    }

    internal static string CreatePlanJson(string toolCode, string? skillCode = null, string? inputJson = null)
    {
        return AgentPlanV2TestData.CreateSingleStep(
            toolCode,
            executable: false,
            skillCode: skillCode,
            inputJson: inputJson);
    }

    internal static string CreateRagPlanJson(Guid knowledgeBaseId)
    {
        return AgentPlanV2TestData.CreateRag(knowledgeBaseId, executable: false);
    }

    internal static string CreateCloudPlanJson()
    {
        return AgentPlanV2TestData.CreateCloud(executable: false);
    }

    private static IReadOnlyCollection<AgentStep> AddTrackedPlanSteps(
        AgentTask task,
        string planJson,
        DateTimeOffset now)
    {
        return AgentPlanV2TestData.AddTrackedPlanSteps(task, planJson, now);
    }

    internal static SemanticQueryPlan CreateDeviceSemanticPlan()
    {
        return new SemanticQueryPlan(
            "Analysis.Device.List",
            SemanticQueryTarget.Device,
            SemanticQueryKind.List,
            null,
            new SemanticProjection(["deviceId", "deviceCode", "deviceName", "processId"]),
            [new SemanticFilter("deviceCode", SemanticFilterOperator.Equal, "DEV-001")],
            null,
            new SemanticSort("deviceCode", SemanticSortDirection.Asc),
            20);
    }

    internal static ToolRegistryGuard CreateAgentRuntimeGuardWithCloudEnabled()
    {
        return CreateGuard(
            CreateTool(
                "query_cloud_data_readonly",
                ToolProviderType.CloudReadonly,
                isEnabled: true,
                requiresApproval: true,
                riskLevel: AiToolRiskLevel.RequiresApproval),
            CreateTool("generate_chart_data", ToolProviderType.Artifact),
            CreateTool("generate_markdown_report", ToolProviderType.Artifact),
            CreateTool("generate_html_report", ToolProviderType.Artifact),
            CreateTool("generate_pdf", ToolProviderType.Artifact, requiresApproval: true, riskLevel: AiToolRiskLevel.RequiresApproval),
            CreateTool("generate_pptx", ToolProviderType.Artifact, requiresApproval: true, riskLevel: AiToolRiskLevel.RequiresApproval),
            CreateTool("generate_xlsx", ToolProviderType.Artifact, requiresApproval: true, riskLevel: AiToolRiskLevel.RequiresApproval),
            CreateTool("finalize_artifacts", ToolProviderType.Artifact, requiresApproval: true, riskLevel: AiToolRiskLevel.RequiresApproval));
    }

    internal static AgentPlanToolGuard CreatePlanToolGuard(ToolRegistryGuard guard, params AiToolDefinition[] runtimeTools)
    {
        return new AgentPlanToolGuard(guard, new StubAgentPluginCatalog(runtimeTools));
    }

    internal static SkillDefinitionGuard CreateSkillGuard(params SkillDefinition[] skills)
    {
        return new SkillDefinitionGuard(new InMemoryRepository<SkillDefinition>(skills));
    }

    internal static SkillDefinition CreateSkill(string skillCode, IReadOnlyCollection<string> allowedToolCodes)
    {
        return new SkillDefinition(
            skillCode,
            skillCode,
            "test skill",
            allowedToolCodes,
            AiToolRiskLevel.Low,
            "None",
            [],
            [],
            ["markdown"],
            isEnabled: true,
            isBuiltIn: false,
            version: 1,
            DateTimeOffset.UtcNow);
    }

    internal static Task<Result<IReadOnlyCollection<AgentStepPlanDto>>> ValidateSingleAsync(
        AgentPlanToolGuard guard,
        string toolCode,
        string? inputJson = null)
    {
        return guard.ValidateStepsAsync(
            [new AgentStepPlanDto("Step", "Step", AgentStepType.Analysis, toolCode, false, inputJson)],
            AgentTaskType.ReportGeneration,
            UserId,
            CancellationToken.None);
    }

    internal static PlanAgentTaskCommandHandler CreatePlanHandler(
        Session session,
        ToolRegistryGuard guard,
        IAgentDynamicPlanner? dynamicPlanner = null,
        IReadOnlyCollection<LanguageModel>? models = null,
        ICloudReadonlyAgentPlanService? cloudReadonlyPlanService = null,
        IReadOnlyCollection<AiToolDefinition>? runtimeTools = null,
        InMemoryRepository<AgentTask>? taskRepository = null,
        SkillDefinitionGuard? skillDefinitionGuard = null,
        IAgentSkillAutoSelector? skillAutoSelector = null)
    {
        _ = dynamicPlanner;
        _ = models;
        _ = skillAutoSelector;
        var planToolGuard = new AgentPlanToolGuard(
            guard,
            new StubAgentPluginCatalog((runtimeTools ?? []).ToArray()),
            skillDefinitionGuard);
        return new PlanAgentTaskCommandHandler(
            new PlanAgentTaskCoordinator(
                taskRepository ?? new InMemoryRepository<AgentTask>(),
                new InMemoryRepository<Session>(session),
                new InMemoryRepository<UploadRecord>(),
                new AgentAuditRecorder(new CapturingAuditLogWriter()),
                [],
                new TestCurrentUser(UserId),
                planToolGuard: planToolGuard,
                cloudReadonlyPlanService: cloudReadonlyPlanService ?? new FixedCloudReadonlyAgentPlanService()));
    }

    internal static LanguageModel CreatePlannerModel()
    {
        return new LanguageModel(
            "FakeEval",
            "planner",
            "http://localhost/fake",
            "fake-key",
            new ModelParameters { MaxTokens = 4096, MaxOutputTokens = 1024, Temperature = 0.2f },
            "FakeEval",
            LanguageModelUsage.Chat | LanguageModelUsage.Planner,
            true);
    }

    internal static ToolRegistryGuard CreateGuard(params ToolRegistration[] tools)
    {
        return CreateGuard((IReadOnlyCollection<ToolRegistration>)tools, []);
    }

    internal static ToolRegistryGuard CreateGuard(ToolRegistration tool, params string[] permissions)
    {
        return CreateGuard([tool], permissions);
    }

    internal static ToolRegistryGuard CreateGuard(IReadOnlyCollection<ToolRegistration> tools, IReadOnlyCollection<string> permissions)
    {
        return new ToolRegistryGuard(
            new InMemoryRepository<ToolRegistration>(tools.ToArray()),
            new StubIdentityAccessService(permissions));
    }

    internal static ToolRegistration CreateTool(
        string toolCode,
        ToolProviderType providerType = ToolProviderType.BuiltIn,
        ToolRegistrationTargetType targetType = ToolRegistrationTargetType.AgentRuntime,
        string targetName = "AgentTaskRuntime",
        bool isEnabled = true,
        bool requiresApproval = false,
        AiToolRiskLevel riskLevel = AiToolRiskLevel.Low,
        string? requiredPermission = null,
        string? inputSchemaJson = null,
        string? outputSchemaJson = null)
    {
        var builtIn = BuiltInToolRegistrations.FindAgentRuntimeTool(toolCode);
        return new ToolRegistration(
            toolCode,
            toolCode,
            "test tool",
            providerType,
            targetType,
            targetName,
            inputSchemaJson ?? builtIn?.InputSchemaJson ??
                """{"type":"object","properties":{},"additionalProperties":false}""",
            outputSchemaJson ?? builtIn?.OutputSchemaJson ??
                """{"type":"object","properties":{},"additionalProperties":false}""",
            riskLevel,
            requiredPermission,
            requiresApproval,
            isEnabled,
            120,
            ToolAuditLevel.Standard,
            DateTimeOffset.UtcNow,
            schemaVersion: builtIn?.SchemaVersion ?? BuiltInToolRegistrations.CurrentSchemaVersion,
            catalogVersion: builtIn?.CatalogVersion ?? BuiltInToolRegistrations.CurrentCatalogVersion);
    }

    internal static ServiceProvider CreateQueueWorkerProvider(
        InMemoryRepository<AgentTask> taskRepository,
        InMemoryAgentTaskRunQueueStore queueRepository,
        InMemoryAgentTaskRunAttemptStore attemptRepository,
        IAgentTaskRuntime runtime)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRepository<AgentTask>>(taskRepository);
        services.AddSingleton<IAgentTaskRunQueueStore>(queueRepository);
        services.AddSingleton<IAgentTaskRunAttemptStore>(attemptRepository);
        services.AddSingleton<IAgentTaskRunQueue>(new AgentTaskRunQueue(
            queueRepository,
            AgentPlanV2TestData.CreateMatchingFreshReadGate()));
        services.AddSingleton<IAgentDurableTaskClaimStore>(
            new InMemoryAgentDurableTaskClaimStore(taskRepository, queueRepository, attemptRepository));
        services.AddSingleton<DurableTaskClaimCoordinator>();
        services.AddSingleton<IOptions<AgentRunQueueOptions>>(
            Options.Create(new AgentRunQueueOptions
            {
                StaleLeaseAction = AgentRunQueueStaleLeaseAction.Fail
            }));
        services.AddSingleton(runtime);
        services.AddSingleton<AgentTaskRunQueueWorkerCoordinator>();
        return services.BuildServiceProvider();
    }

    internal sealed class RecordingAgentTaskRuntime(IAgentTaskRunAttemptStore attemptRepository) : IAgentTaskRuntime
    {
        public Task<Result<AgentTask>> RunAsync(AgentTask task, CancellationToken cancellationToken = default)
        {
            return RunAsync(task, AgentTaskRunTriggerType.Manual, cancellationToken);
        }

        public async Task<Result<AgentTask>> RunAsync(
            AgentTask task,
            AgentTaskRunTriggerType triggerType = AgentTaskRunTriggerType.Manual,
            CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            var attempt = new AgentTaskRunAttempt(
                task.Id,
                task.RunAttemptCount + 1,
                triggerType,
                "test-data-worker",
                now,
                TimeSpan.FromMinutes(5));
            attemptRepository.Add(attempt);
            task.BeginRunAttempt(
                attempt.Id,
                attempt.AttemptNo,
                attempt.LeaseId!.Value,
                attempt.LeaseOwner!,
                attempt.LeaseExpiresAt!.Value,
                now);
            attempt.WaitForApproval(now, "Waiting for final output approval.");
            task.ReleaseRunLease(now, clearActiveAttempt: false);
            await attemptRepository.SaveChangesAsync(cancellationToken);
            return Result.Success(task);
        }

        public async Task<Result<AgentTask>> RunClaimedAsync(
            DurableTaskClaim claim,
            CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            claim.RunAttempt.WaitForApproval(now, "Waiting for final output approval.");
            claim.Task.ReleaseRunLease(now, clearActiveAttempt: false);
            await attemptRepository.SaveChangesAsync(cancellationToken);
            return Result.Success(claim.Task);
        }
    }

    private sealed class InMemoryAgentDurableTaskClaimStore(
        InMemoryRepository<AgentTask> taskRepository,
        InMemoryAgentTaskRunQueueStore queueRepository,
        InMemoryAgentTaskRunAttemptStore attemptRepository)
        : IAgentDurableTaskClaimStore
    {
        public Task<DurableTaskClaim?> TryClaimNextAsync(
            string leaseOwner,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            var queueItem = queueRepository.Items
                .Where(item => item.CanBeLeased(now))
                .OrderBy(item => item.AvailableAt)
                .ThenBy(item => item.CreatedAt)
                .FirstOrDefault();
            if (queueItem is null)
            {
                return Task.FromResult<DurableTaskClaim?>(null);
            }

            var task = taskRepository.Items.Single(item => item.Id == queueItem.TaskId);
            var attempt = new AgentTaskRunAttempt(
                task.Id,
                task.RunAttemptCount + 1,
                queueItem.TriggerType,
                leaseOwner,
                now,
                leaseDuration);
            attemptRepository.Add(attempt);
            task.BeginRunAttempt(
                attempt.Id,
                attempt.AttemptNo,
                attempt.LeaseId!.Value,
                leaseOwner,
                attempt.LeaseExpiresAt!.Value,
                now);
            attempt.BindTaskFencingToken(task.RunFencingToken);
            queueItem.AcquireLease(
                attempt.LeaseId.Value,
                leaseOwner,
                now,
                leaseDuration,
                task.RunFencingToken);
            queueItem.LinkRunAttempt(attempt.Id, now);

            return Task.FromResult<DurableTaskClaim?>(new DurableTaskClaim(
                queueItem,
                task,
                attempt,
                task.RunFencingToken,
                attempt.LeaseId.Value,
                attempt.LeaseExpiresAt.Value));
        }

        public Task<AgentFencedWriteResult> TryMarkStartedAsync(
            DurableTaskClaim claim,
            DateTimeOffset startedAtUtc,
            CancellationToken cancellationToken = default)
        {
            claim.QueueItem.MarkStarted(claim.RunAttempt.Id, startedAtUtc);
            return Task.FromResult(AgentFencedWriteResult.Succeeded);
        }

        public Task<AgentFencedWriteResult> TryCompleteAsync(
            DurableTaskClaim claim,
            AgentTaskRunQueueStatus terminalStatus,
            string? failureCode,
            string safeMessage,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken = default)
        {
            switch (terminalStatus)
            {
                case AgentTaskRunQueueStatus.Succeeded:
                    claim.QueueItem.MarkSucceeded(completedAtUtc, safeMessage);
                    break;
                case AgentTaskRunQueueStatus.Failed:
                    claim.QueueItem.MarkFailed(failureCode ?? "agent_task_run_failed", safeMessage, completedAtUtc);
                    break;
                case AgentTaskRunQueueStatus.Cancelled:
                    claim.QueueItem.Cancel(completedAtUtc, safeMessage);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(terminalStatus));
            }

            return Task.FromResult(AgentFencedWriteResult.Succeeded);
        }

        public Task<int> RecoverExpiredStartedAsync(
            DateTimeOffset nowUtc,
            int maxItems,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }

    internal sealed class ThrowingRuntime : IAgentTaskRuntime
    {
        public Task<Result<AgentTask>> RunAsync(AgentTask task, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Runtime should not be called by this test.");
        }

        public Task<Result<AgentTask>> RunAsync(
            AgentTask task,
            AgentTaskRunTriggerType triggerType = AgentTaskRunTriggerType.Manual,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Runtime should not be called by this test.");
        }
    }

    internal sealed class InMemoryToolExecutionAuditStore(params ToolExecutionRecord[] initialItems)
        : IToolExecutionAuditStore
    {
        public List<ToolExecutionRecord> Items { get; } = [.. initialItems];

        public ToolExecutionRecord Add(ToolExecutionRecord record)
        {
            Items.Add(record);
            return record;
        }

        public Task<List<ToolExecutionRecord>> ListByTaskAsync(
            AgentTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.Where(record => record.TaskId == taskId).ToList());
        }
    }

    internal sealed class InMemoryMessageTimelineProjectionStore(params MessageEvent[] initialItems)
        : IMessageTimelineProjectionStore
    {
        public List<MessageEvent> Items { get; } = [.. initialItems];

        public Task<List<MessageEvent>> ListBySessionAsync(
            SessionId sessionId,
            bool includeMessage = false,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items
                .Where(messageEvent => messageEvent.SessionId == sessionId)
                .OrderBy(messageEvent => messageEvent.Sequence)
                .ToList());
        }

        public MessageEvent Add(MessageEvent messageEvent)
        {
            Items.Add(messageEvent);
            return messageEvent;
        }
    }

    internal sealed class InMemoryAgentWorkerHeartbeatStore(params AgentWorkerHeartbeat[] initialItems)
        : IAgentWorkerHeartbeatStore
    {
        public List<AgentWorkerHeartbeat> Items { get; } = [.. initialItems];

        public Task<AgentWorkerHeartbeat?> FirstByWorkerIdAsync(
            string workerId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.FirstOrDefault(heartbeat => heartbeat.WorkerId == workerId));
        }

        public Task<List<AgentWorkerHeartbeat>> ListAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.OrderByDescending(heartbeat => heartbeat.LastSeenAt).ToList());
        }

        public AgentWorkerHeartbeat Add(AgentWorkerHeartbeat heartbeat)
        {
            Items.Add(heartbeat);
            return heartbeat;
        }

        public void Update(AgentWorkerHeartbeat heartbeat)
        {
            if (!Items.Contains(heartbeat))
            {
                Items.Add(heartbeat);
            }
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }
    }

    internal sealed class InMemoryAgentTaskRunQueueStore(params AgentTaskRunQueueItem[] initialItems)
        : IAgentTaskRunQueueStore
    {
        public List<AgentTaskRunQueueItem> Items { get; } = [.. initialItems];

        public Task<AgentTaskRunQueueItem?> FirstActiveByTaskAsync(
            AgentTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items
                .Where(item => item.TaskId == taskId && IsActive(item))
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefault());
        }

        public Task<AgentTaskRunQueueItem?> FirstByIdAsync(
            AgentTaskRunQueueItemId id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.FirstOrDefault(item => item.Id == id));
        }

        public Task<List<AgentTaskRunQueueItem>> ListActiveByTaskAsync(
            AgentTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items
                .Where(item => item.TaskId == taskId && IsActive(item))
                .OrderByDescending(item => item.CreatedAt)
                .ToList());
        }

        public Task<List<AgentTaskRunQueueItem>> ListActiveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items
                .Where(IsActive)
                .OrderBy(item => item.AvailableAt)
                .ToList());
        }

        public Task<List<AgentTaskRunQueueItem>> ListAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.OrderByDescending(item => item.CreatedAt).ToList());
        }

        public Task<List<AgentTaskRunQueueItem>> ListByTaskAsync(
            AgentTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items
                .Where(item => item.TaskId == taskId)
                .OrderByDescending(item => item.CreatedAt)
                .ToList());
        }

        public AgentTaskRunQueueItem Add(AgentTaskRunQueueItem item)
        {
            Items.Add(item);
            return item;
        }

        public void Update(AgentTaskRunQueueItem item)
        {
            if (!Items.Contains(item))
            {
                Items.Add(item);
            }
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        private static bool IsActive(AgentTaskRunQueueItem item)
        {
            return item.IsActive;
        }
    }

    internal sealed class InMemoryAgentTaskRunAttemptStore(params AgentTaskRunAttempt[] initialItems)
        : IAgentTaskRunAttemptStore
    {
        public List<AgentTaskRunAttempt> Items { get; } = [.. initialItems];

        public Task<AgentTaskRunAttempt?> FirstByIdAsync(
            AgentTaskRunAttemptId id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.FirstOrDefault(attempt => attempt.Id == id));
        }

        public Task<List<AgentTaskRunAttempt>> ListByTaskAsync(
            AgentTaskId taskId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items
                .Where(attempt => attempt.TaskId == taskId)
                .OrderByDescending(attempt => attempt.StartedAt)
                .ToList());
        }

        public AgentTaskRunAttempt Add(AgentTaskRunAttempt attempt)
        {
            Items.Add(attempt);
            return attempt;
        }

        public void Update(AgentTaskRunAttempt attempt)
        {
            if (!Items.Contains(attempt))
            {
                Items.Add(attempt);
            }
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }
    }

    internal sealed class InMemoryAgentTaskCancellationStore(
        IRepository<AgentTask> taskRepository,
        IRepository<ApprovalRequest> approvalRepository,
        IAgentTaskRunQueueStore queueRepository,
        IAgentTaskRunAttemptStore runAttemptRepository)
        : IAgentTaskCancellationStore
    {
        public async Task<AgentTaskCancellationCheckpoint> RequestAsync(
            AgentTaskId taskId,
            DateTimeOffset requestedAtUtc,
            string safeMessage,
            CancellationToken cancellationToken = default)
        {
            var task = await taskRepository.FirstOrDefaultAsync(
                new AgentTaskByIdSpec(taskId, includeSteps: true),
                cancellationToken);
            if (task is null)
            {
                return new AgentTaskCancellationCheckpoint(
                    AgentTaskCancellationDisposition.StateConflict,
                    [],
                    "Agent task no longer exists.");
            }

            var queues = await queueRepository.ListActiveByTaskAsync(taskId, cancellationToken);
            var queueSnapshots = queues
                .Select(item => new AgentTaskCancellationQueueItem(item, item.Status))
                .ToArray();
            if (task.Status is AgentTaskStatus.Completed
                or AgentTaskStatus.Finalized
                or AgentTaskStatus.Failed
                or AgentTaskStatus.Rejected
                or AgentTaskStatus.Cancelled)
            {
                return new AgentTaskCancellationCheckpoint(
                    AgentTaskCancellationDisposition.AlreadyTerminal,
                    queueSnapshots,
                    "Agent task is already terminal.");
            }

            AgentTaskRunAttempt? attempt = null;
            if (task.ActiveRunAttemptId is not null)
            {
                attempt = await runAttemptRepository.FirstByIdAsync(
                    task.ActiveRunAttemptId.Value,
                    cancellationToken);
                if (attempt is null)
                {
                    return new AgentTaskCancellationCheckpoint(
                        AgentTaskCancellationDisposition.StateConflict,
                        queueSnapshots,
                        "Active run attempt is missing; cancellation did not guess a terminal state.");
                }
            }

            var approvals = await approvalRepository.ListAsync(
                new ApprovalRequestsByTaskSpec(taskId, pendingOnly: true),
                cancellationToken);
            foreach (var approval in approvals)
            {
                approval.Cancel(requestedAtUtc);
                approvalRepository.Update(approval);
            }

            if (attempt is not null && !attempt.IsTerminal)
            {
                attempt.Cancel(requestedAtUtc, safeMessage);
                runAttemptRepository.Update(attempt);
            }

            foreach (var queue in queues)
            {
                queue.Cancel(requestedAtUtc, safeMessage);
                queueRepository.Update(queue);
            }

            foreach (var step in task.Steps.Where(step =>
                         step.Status is not AgentStepStatus.Completed
                             and not AgentStepStatus.Failed
                             and not AgentStepStatus.Cancelled))
            {
                step.Cancel(requestedAtUtc);
            }

            task.Cancel(requestedAtUtc);
            taskRepository.Update(task);
            await approvalRepository.SaveChangesAsync(cancellationToken);
            await runAttemptRepository.SaveChangesAsync(cancellationToken);
            await queueRepository.SaveChangesAsync(cancellationToken);
            await taskRepository.SaveChangesAsync(cancellationToken);
            return new AgentTaskCancellationCheckpoint(
                AgentTaskCancellationDisposition.Cancelled,
                queueSnapshots,
                safeMessage);
        }
    }

    internal sealed class InMemoryRepository<T>(params T[] initialItems) : IRepository<T>
        where T : class, IEntity, IAggregateRoot
    {
        public List<T> Items { get; } = [.. initialItems];

        public T Add(T entity)
        {
            Items.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
        }

        public void Delete(T entity)
        {
            Items.Remove(entity);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        public Task<List<T>> ListAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).ToList());
        }

        public Task<T?> FirstOrDefaultAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).FirstOrDefault());
        }

        public Task<int> CountAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).Count());
        }

        public Task<bool> AnyAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).Any());
        }

        public Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            return Task.FromResult(Items.FirstOrDefault(item => Equals(GetId(item), id)));
        }

        public Task<List<T>> GetListAsync(Expression<Func<T, bool>> expression, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.AsQueryable().Where(expression).ToList());
        }

        public Task<int> GetCountAsync(Expression<Func<T, bool>> expression, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.AsQueryable().Count(expression));
        }

        public Task<T?> GetAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.AsQueryable().FirstOrDefault(expression));
        }

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.AsQueryable().Where(expression).ToList());
        }

        private IQueryable<T> Apply(ISpecification<T>? specification)
        {
            var query = Items.AsQueryable();
            return specification?.FilterCondition is null
                ? query
                : query.Where(specification.FilterCondition);
        }

        private static object? GetId(T item)
        {
            return typeof(T).GetProperty("Id")?.GetValue(item);
        }
    }

    internal sealed class FixedCloudReadonlyAgentPlanService : ICloudReadonlyAgentPlanService
    {
        private readonly Result<CloudReadonlyAgentPlanIntent> result;

        public FixedCloudReadonlyAgentPlanService(Result<CloudReadonlyAgentPlanIntent>? result = null)
        {
            this.result = result ?? Result.Success(CloudReadonlyAgentPlanIntent.FromSemanticPlan(
                new SemanticQueryPlan(
                    "Analysis.Device.List",
                    SemanticQueryTarget.Device,
                    SemanticQueryKind.List,
                    null,
                    new SemanticProjection(["deviceId", "deviceCode", "status"]),
                    [],
                    null,
                    null,
                    20),
                0.95));
        }

        public Task<Result<CloudReadonlyAgentPlanIntent>> CreateIntentAsync(
            Guid sessionId,
            string goal,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }

        public Result<CloudReadonlyAgentPlanIntent> CreateIntentFromRouted(
            string goal,
            IReadOnlyCollection<IntentResult> routedIntents)
        {
            return result;
        }
    }

    internal sealed class FixedSkillAutoSelector(string? skillCode, string? reason = "test selector") : IAgentSkillAutoSelector
    {
        public int CallCount { get; private set; }

        public Task<AgentSkillSelection?> SelectSkillAsync(
            Guid sessionId,
            string goal,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<AgentSkillSelection?>(new AgentSkillSelection(skillCode, reason));
        }
    }

    internal sealed class ThrowingDynamicPlanner : IAgentDynamicPlanner
    {
        public Task<Result<IReadOnlyCollection<AgentStepPlanDto>>> CreatePlanAsync(
            AgentDynamicPlannerRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Dynamic planner should not be called by this test.");
        }
    }

    internal sealed class FixedDynamicPlanner(params AgentStepPlanDto[] steps) : IAgentDynamicPlanner
    {
        public AgentDynamicPlannerRequest? LastRequest { get; private set; }

        public Task<Result<IReadOnlyCollection<AgentStepPlanDto>>> CreatePlanAsync(
            AgentDynamicPlannerRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Result.Success<IReadOnlyCollection<AgentStepPlanDto>>(steps));
        }
    }

    internal sealed class FailingDynamicPlanner(string code, string detail) : IAgentDynamicPlanner
    {
        public Task<Result<IReadOnlyCollection<AgentStepPlanDto>>> CreatePlanAsync(
            AgentDynamicPlannerRequest request,
            CancellationToken cancellationToken)
        {
            Result<IReadOnlyCollection<AgentStepPlanDto>> result = Result.Failure(new ApiProblemDescriptor(code, detail));
            return Task.FromResult(result);
        }
    }

    internal sealed class StubAgentPluginCatalog(params AiToolDefinition[] tools) : IAgentPluginCatalog
    {
        public AiToolDefinition[] GetTools(params string[] names)
        {
            return tools
                .Where(tool => names.Contains(tool.Name, StringComparer.OrdinalIgnoreCase))
                .ToArray();
        }

        public AiToolDefinition[] GetPluginTools(string name)
        {
            return [];
        }

        public AiToolDefinition[] GetAllTools()
        {
            return tools;
        }

        public IAgentPlugin? GetPlugin(string name)
        {
            return null;
        }

        public IAgentPlugin[] GetAllPlugin()
        {
            return [];
        }
    }

    internal sealed class ThrowingCloudReadonlyAgentToolExecutor : ICloudReadonlyAgentToolExecutor
    {
        public Task<CloudReadonlyAgentToolResult> ExecuteAsync(
            CloudReadonlyAgentToolRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud readonly tool executor should not be called by this test.");
        }
    }

    internal sealed class FixedSemanticQueryPlanner(SemanticQueryPlan plan) : ISemanticQueryPlanner
    {
        public SemanticPlanningResult Plan(string intent, string? query)
        {
            return SemanticPlanningResult.Success(plan);
        }
    }

    internal static CloudReadonlyAgentToolExecutor CreateRealCloudReadonlyExecutor(
        ISemanticQueryPlanner planner,
        ICloudAiReadClient cloudClient)
    {
        var options = Options.Create(new CloudReadonlyOptions
        {
            Mode = CloudReadonlyDataSourceMode.Real,
            Real = new CloudReadonlyRealOptions
            {
                Enabled = true,
                AllowProductionRead = true
            }
        });
        return new CloudReadonlyAgentToolExecutor(new FixedCloudReadonlyDataProviderResolver(
            new RealCloudReadonlyDataProvider(cloudClient, options)));
    }

    internal sealed class FixedCloudReadonlyDataProviderResolver(ICloudReadonlyDataProvider provider)
        : ICloudReadonlyDataProviderResolver
    {
        public ICloudReadonlyDataProvider Resolve()
        {
            return provider;
        }
    }

    internal sealed class FixedRuntimeSettingsProvider : IAgentRuntimeSettingsProvider
    {
        public Task<ChatRuntimeSettingsDto> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatRuntimeSettingsDto(6, 12, 4, 30, 12000));
        }
    }

    internal sealed class StubIdentityAccessService(
        IReadOnlyCollection<string> permissions,
        string? roleName = "User") : IIdentityAccessService
    {
        public Task<CurrentUserAccess?> GetCurrentUserAccessAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<CurrentUserAccess?>(new CurrentUserAccess(userId, "test-user", roleName, permissions));
        }

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(string roleName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(permissions);
        }

        public Task SyncRolePermissionsAsync(
            string roleName,
            IEnumerable<string> permissionCodes,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    internal sealed class RecordingKnowledgeBaseAccessChecker : IKnowledgeBaseAccessChecker
    {
        public Guid? ObservedUserId { get; private set; }

        public bool? ObservedIsAdmin { get; private set; }

        public Task<bool> CanReadAsync(
            Guid knowledgeBaseId,
            Guid userId,
            bool isAdmin,
            CancellationToken cancellationToken = default)
        {
            ObservedUserId = userId;
            ObservedIsAdmin = isAdmin;
            return Task.FromResult(isAdmin);
        }

        public Task<bool> CanWriteAsync(
            Guid knowledgeBaseId,
            Guid userId,
            bool isAdmin,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(isAdmin);
        }
    }

    internal sealed class TestAgentToolExecutor(Func<ToolRegistration, bool> canExecute) : IAgentToolExecutor
    {
        public bool CanExecute(ToolRegistration tool, AgentStep step)
        {
            return canExecute(tool);
        }

        public Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext context)
        {
            return Task.FromResult(AgentToolExecutionResult.From(new { ok = true }));
        }
    }

    internal sealed class CapturingAuditLogWriter : IAuditLogWriter
    {
        public List<AuditLogWriteRequest> Requests { get; } = [];

        public Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Requests.Count);
        }
    }

    internal sealed class FixedAuditLogQueryService(params AuditLogSummaryDto[] logs) : IAuditLogQueryService
    {
        public Task<AuditLogListDto> GetListAsync(
            int page,
            int pageSize,
            string? actionGroup,
            string? actionCode,
            string? targetType,
            string? targetId,
            string? targetName,
            string? operatorUserName,
            string? result,
            DateTime? from,
            DateTime? to,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AuditLogListDto(page, pageSize, logs.Length, logs));
        }
    }

    internal sealed class FixedWorkspaceFingerprintProvider(string hash) : IAgentWorkspaceFingerprintProvider
    {
        public string GetWorkspaceRootHash()
        {
            return hash;
        }
    }

    internal sealed class ThrowingWorkspaceService(bool throwOnWrite = false) : IAgentArtifactWorkspaceService
    {
        public Task<ArtifactWorkspace> CreateForTaskAsync(
            AgentTask task,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ArtifactWorkspace(
                task.Id,
                $"ws_{Guid.NewGuid():N}",
                @"C:\aicopilot-workspaces\test",
                "/api/aigateway/workspaces/test",
                nowUtc));
        }

        public Task<Artifact> WriteDraftTextArtifactAsync(
            ArtifactWorkspace workspace,
            ArtifactType artifactType,
            string name,
            string relativePath,
            string content,
            string mimeType,
            AgentStepId? stepId,
            ArtifactSourceMetadata? sourceMetadata,
            CancellationToken cancellationToken)
        {
            if (throwOnWrite)
            {
                throw new InvalidOperationException(@"apiKey: sk-test C:\secrets\report.txt Host=db;Password=super-secret;");
            }

            var artifact = workspace.AddDraftArtifact(
                artifactType,
                name,
                relativePath,
                content.Length,
                mimeType,
                stepId,
                DateTimeOffset.UtcNow);
            artifact.ApplySourceMetadata(sourceMetadata);
            return Task.FromResult(artifact);
        }

        public Task<Artifact> WriteDraftBinaryArtifactAsync(
            ArtifactWorkspace workspace,
            ArtifactType artifactType,
            string name,
            string relativePath,
            byte[] content,
            string mimeType,
            AgentStepId? stepId,
            ArtifactSourceMetadata? sourceMetadata,
            CancellationToken cancellationToken)
        {
            var artifact = workspace.AddDraftArtifact(
                artifactType,
                name,
                relativePath,
                content.Length,
                mimeType,
                stepId,
                DateTimeOffset.UtcNow);
            artifact.ApplySourceMetadata(sourceMetadata);
            return Task.FromResult(artifact);
        }

        public Task<IReadOnlyList<Artifact>> WriteDraftArtifactSetAsync(
            ArtifactWorkspace workspace,
            IReadOnlyCollection<AgentDraftArtifactWriteRequest> artifacts,
            CancellationToken cancellationToken)
        {
            if (throwOnWrite)
            {
                throw new InvalidOperationException(@"apiKey: sk-test C:\secrets\report.txt Host=db;Password=super-secret;");
            }

            var created = artifacts.Select(request =>
            {
                var artifact = workspace.AddDraftArtifact(
                    request.ArtifactType,
                    request.Name,
                    request.RelativePath,
                    request.Content.Length,
                    request.MimeType,
                    request.StepId,
                    DateTimeOffset.UtcNow);
                artifact.ApplySourceMetadata(request.SourceMetadata);
                return artifact;
            }).ToArray();
            return Task.FromResult<IReadOnlyList<Artifact>>(created);
        }
    }

    internal sealed class CapturingWorkspaceService(ArtifactWorkspace workspace) : IAgentArtifactWorkspaceService
    {
        public Dictionary<string, string> TextArtifacts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<ArtifactWorkspace> CreateForTaskAsync(
            AgentTask task,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(workspace);
        }

        public Task<Artifact> WriteDraftTextArtifactAsync(
            ArtifactWorkspace artifactWorkspace,
            ArtifactType artifactType,
            string name,
            string relativePath,
            string content,
            string mimeType,
            AgentStepId? stepId,
            ArtifactSourceMetadata? sourceMetadata,
            CancellationToken cancellationToken)
        {
            TextArtifacts[relativePath] = content;
            var artifact = artifactWorkspace.AddDraftArtifact(
                artifactType,
                name,
                relativePath,
                content.Length,
                mimeType,
                stepId,
                DateTimeOffset.UtcNow);
            artifact.ApplySourceMetadata(sourceMetadata);
            return Task.FromResult(artifact);
        }

        public Task<Artifact> WriteDraftBinaryArtifactAsync(
            ArtifactWorkspace artifactWorkspace,
            ArtifactType artifactType,
            string name,
            string relativePath,
            byte[] content,
            string mimeType,
            AgentStepId? stepId,
            ArtifactSourceMetadata? sourceMetadata,
            CancellationToken cancellationToken)
        {
            var artifact = artifactWorkspace.AddDraftArtifact(
                artifactType,
                name,
                relativePath,
                content.Length,
                mimeType,
                stepId,
                DateTimeOffset.UtcNow);
            artifact.ApplySourceMetadata(sourceMetadata);
            return Task.FromResult(artifact);
        }

        public Task<IReadOnlyList<Artifact>> WriteDraftArtifactSetAsync(
            ArtifactWorkspace artifactWorkspace,
            IReadOnlyCollection<AgentDraftArtifactWriteRequest> artifacts,
            CancellationToken cancellationToken)
        {
            var created = artifacts.Select(request =>
            {
                if (request.MimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                    request.MimeType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
                    request.MimeType.Contains("markdown", StringComparison.OrdinalIgnoreCase))
                {
                    TextArtifacts[request.RelativePath] = Encoding.UTF8.GetString(request.Content);
                }

                var artifact = artifactWorkspace.AddDraftArtifact(
                    request.ArtifactType,
                    request.Name,
                    request.RelativePath,
                    request.Content.Length,
                    request.MimeType,
                    request.StepId,
                    DateTimeOffset.UtcNow);
                artifact.ApplySourceMetadata(request.SourceMetadata);
                return artifact;
            }).ToArray();
            return Task.FromResult<IReadOnlyList<Artifact>>(created);
        }
    }

    internal sealed class InMemoryArtifactWorkspaceFileStore : IArtifactWorkspaceFileStore
    {
        private readonly Dictionary<string, StoredFile> _files = new(StringComparer.OrdinalIgnoreCase);

        public void AddFile(string workspaceCode, string relativePath, byte[] content, string mimeType)
        {
            _files[Key(workspaceCode, relativePath)] = new StoredFile(relativePath, content, mimeType, DateTimeOffset.UtcNow);
        }

        public ArtifactWorkspaceStorageSettings GetSettings()
        {
            return new ArtifactWorkspaceStorageSettings(
                "/tmp/aicopilot-test",
                ["draft", "final", "versions"],
                ["Markdown", "Html", "Pdf", "Pptx", "Xlsx", "Chart"],
                AllowsUserDefinedPath: false);
        }

        public Task<ArtifactWorkspaceStorageInfo> CreateWorkspaceAsync(
            string workspaceCode,
            Guid taskId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ArtifactWorkspaceStorageInfo($"/tmp/{workspaceCode}", $"/workspaces/{workspaceCode}"));
        }

        public Task<ArtifactFileWriteResult> WriteTextAsync(
            string workspaceCode,
            string relativePath,
            string content,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            return WriteBytesAsync(workspaceCode, relativePath, Encoding.UTF8.GetBytes(content), mimeType, cancellationToken);
        }

        public Task<ArtifactFileWriteResult> WriteBytesAsync(
            string workspaceCode,
            string relativePath,
            byte[] content,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            AddFile(workspaceCode, relativePath, content, mimeType);
            return Task.FromResult(new ArtifactFileWriteResult(relativePath, content.LongLength, mimeType));
        }

        public Task<ArtifactFileWriteResult> CopyAsync(
            string workspaceCode,
            string sourceRelativePath,
            string targetRelativePath,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            var source = _files[Key(workspaceCode, sourceRelativePath)];
            return WriteBytesAsync(workspaceCode, targetRelativePath, source.Content, mimeType, cancellationToken);
        }

        public Task<ArtifactFileReadResult?> OpenReadAsync(
            string workspaceCode,
            string relativePath,
            string mimeType,
            CancellationToken cancellationToken = default)
        {
            if (!_files.TryGetValue(Key(workspaceCode, relativePath), out var file))
            {
                return Task.FromResult<ArtifactFileReadResult?>(null);
            }

            return Task.FromResult<ArtifactFileReadResult?>(new ArtifactFileReadResult(
                new MemoryStream(file.Content.ToArray()),
                Path.GetFileName(file.RelativePath),
                file.MimeType,
                file.Content.LongLength));
        }

        public Task<IReadOnlyCollection<ArtifactWorkspaceFileItem>> ListAsync(
            string workspaceCode,
            CancellationToken cancellationToken = default)
        {
            var prefix = workspaceCode + "/";
            IReadOnlyCollection<ArtifactWorkspaceFileItem> items = _files
                .Where(item => item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(item => new ArtifactWorkspaceFileItem(
                    Path.GetFileName(item.Value.RelativePath),
                    item.Value.RelativePath,
                    IsDirectory: false,
                    item.Value.Content.LongLength,
                    item.Value.UpdatedAt))
                .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Task.FromResult(items);
        }

        private static string Key(string workspaceCode, string relativePath)
        {
            return workspaceCode + "/" + relativePath.Replace('\\', '/');
        }

        private sealed record StoredFile(
            string RelativePath,
            byte[] Content,
            string MimeType,
            DateTimeOffset UpdatedAt);
    }

    internal sealed class NoopTableFileParser : IAgentTableFileParser
    {
        public Task<AgentReportTable?> ParseAsync(
            AgentTableFileParseRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<AgentReportTable?>(null);
        }
    }

    internal sealed class NoopDocumentGenerator : IAgentArtifactDocumentGenerator
    {
        public Task<byte[]> GeneratePdfAsync(AgentReportDocument document, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Array.Empty<byte>());
        }

        public Task<byte[]> GeneratePptxAsync(AgentReportDocument document, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Array.Empty<byte>());
        }

        public Task<byte[]> GenerateXlsxAsync(AgentReportDocument document, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Array.Empty<byte>());
        }
    }

    internal sealed class NoopKnowledgeRetrievalService : IKnowledgeRetrievalService
    {
        public Task<IReadOnlyList<KnowledgeRetrievalResult>> SearchAsync(
            Guid knowledgeBaseId,
            string queryText,
            int topK,
            double minScore,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<KnowledgeRetrievalResult>>([]);
        }
    }

    internal sealed class DisabledCloudAiReadClient : ICloudAiReadClient
    {
        public bool IsEnabled => false;

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadProcessDto>> GetProcessesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadClientReleaseVersionDto>> GetClientReleasesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceClientStateDto>> GetDeviceClientStatesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadCapacityHourlyDto>> GetCapacityHourlyAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadProductionRecordDto>> GetProductionRecordsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<object>> QuerySemanticAsync(
            SemanticQueryPlan plan,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    internal sealed class RecordingCloudAiReadClient(CloudAiReadResult<object> result) : ICloudAiReadClient
    {
        public List<SemanticQueryPlan> RequestedPlans { get; } = [];

        public bool IsEnabled => true;

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadProcessDto>> GetProcessesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadClientReleaseVersionDto>> GetClientReleasesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceClientStateDto>> GetDeviceClientStatesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadCapacityHourlyDto>> GetCapacityHourlyAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadProductionRecordDto>> GetProductionRecordsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<object>> QuerySemanticAsync(
            SemanticQueryPlan plan,
            CancellationToken cancellationToken = default)
        {
            RequestedPlans.Add(plan);
            return Task.FromResult(result);
        }
    }

    internal sealed class FailingCloudAiReadClient(CloudAiReadException exception) : ICloudAiReadClient
    {
        public bool IsEnabled => true;

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<CloudAiReadResult<CloudAiReadProcessDto>> GetProcessesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<CloudAiReadResult<CloudAiReadClientReleaseVersionDto>> GetClientReleasesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceClientStateDto>> GetDeviceClientStatesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<CloudAiReadResult<CloudAiReadCapacityHourlyDto>> GetCapacityHourlyAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<CloudAiReadResult<CloudAiReadProductionRecordDto>> GetProductionRecordsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<CloudAiReadResult<object>> QuerySemanticAsync(
            SemanticQueryPlan plan,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }
    }
}
