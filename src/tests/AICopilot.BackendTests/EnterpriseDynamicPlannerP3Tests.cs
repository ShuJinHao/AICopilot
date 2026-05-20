using System.Linq.Expressions;
using System.Text.Json;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.BackendTests;

[Trait("Suite", "EnterpriseDynamicPlannerP3")]
public sealed class EnterpriseDynamicPlannerP3Tests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SimulationDataSourceId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [Fact]
    public async Task DynamicPlanner_ShouldPersistSimulationSafetySummary_AndForcedSteps()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var taskRepository = new InMemoryRepository<AgentTask>();
        var plannerModel = CreatePlannerModel();
        var dynamicPlanner = new FixedDynamicPlanner(
            new AgentStepPlanDto(
                "Draft markdown",
                "Create the business report draft.",
                AgentStepType.ArtifactGeneration,
                "generate_markdown_report",
                false));
        var handler = CreatePlanHandler(
            session,
            dynamicPlanner,
            [plannerModel],
            [CreateSimulationDescriptor()],
            taskRepository);

        var result = await handler.Handle(
            new PlanAgentTaskCommand(
                session.Id.Value,
                "Analyze quality defects with charts.",
                AgentTaskType.ReportGeneration,
                null,
                DataSourceIds: [SimulationDataSourceId],
                BusinessDomains: ["Quality"],
                QueryMode: "TextToSql",
                RequiresDataApproval: true,
                ArtifactTypes: ["Chart", "Markdown"],
                TrialScenarioId: "quality-defects",
                TrialScenarioTitle: "Quality defects",
                IsSimulationTrial: true,
                PlannerMode: "DynamicOnly"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        dynamicPlanner.LastRequest.Should().NotBeNull();
        dynamicPlanner.LastRequest!.DataSources.Should().ContainSingle(source =>
            source.Id == SimulationDataSourceId &&
            source.ExternalSystemType == "SimulationBusiness" &&
            source.IsSimulation);
        dynamicPlanner.LastRequest.BusinessDomains.Should().ContainSingle().Which.Should().Be("Quality");

        var task = taskRepository.Items.Should().ContainSingle().Which;
        using var plan = JsonDocument.Parse(task.PlanJson);
        var root = plan.RootElement;
        root.GetProperty("plannerMode").GetString().Should().Be("Dynamic");
        root.GetProperty("plannerFallbackReason").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("dataSourceSummaries").EnumerateArray().Should().ContainSingle();
        var source = root.GetProperty("dataSourceSummaries")[0];
        source.GetProperty("sourceMode").GetString().Should().Be("SimulationBusiness");
        source.GetProperty("isSimulation").GetBoolean().Should().BeTrue();
        source.GetProperty("sourceLabel").GetString().Should().Be("AI 独立模拟业务库");
        root.GetProperty("plannerSafetySummary").GetProperty("isSimulationOnly").GetBoolean().Should().BeTrue();
        root.GetProperty("forcedStepCodes").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(["query_business_database_readonly", "summarize_business_query_result", "generate_business_chart", "finalize_artifacts"]);
        root.GetProperty("approvalCheckpoints").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(["query_business_database_readonly", "finalize_artifacts"]);
    }

    [Fact]
    public async Task AutoPlanner_ShouldUseStaticFallback_WhenNoPlannerModelIsAvailable()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var taskRepository = new InMemoryRepository<AgentTask>();
        var handler = CreatePlanHandler(
            session,
            new ThrowingDynamicPlanner(),
            [],
            [CreateSimulationDescriptor()],
            taskRepository);

        var result = await handler.Handle(
            new PlanAgentTaskCommand(
                session.Id.Value,
                "Analyze capacity.",
                AgentTaskType.ReportGeneration,
                null,
                DataSourceIds: [SimulationDataSourceId],
                BusinessDomains: ["Production"],
                ArtifactTypes: ["Chart", "Markdown"],
                PlannerMode: "Auto"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var task = taskRepository.Items.Should().ContainSingle().Which;
        using var plan = JsonDocument.Parse(task.PlanJson);
        plan.RootElement.GetProperty("plannerMode").GetString().Should().Be("StaticFallback");
        plan.RootElement.GetProperty("plannerFallbackReason").GetString().Should().Contain("No enabled planner model");
    }

    [Fact]
    public async Task DynamicOnlyPlanner_ShouldFail_WhenNoPlannerModelIsAvailable()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var handler = CreatePlanHandler(
            session,
            new ThrowingDynamicPlanner(),
            [],
            [CreateSimulationDescriptor()]);

        var result = await handler.Handle(
            new PlanAgentTaskCommand(
                session.Id.Value,
                "Analyze capacity.",
                AgentTaskType.ReportGeneration,
                null,
                DataSourceIds: [SimulationDataSourceId],
                PlannerMode: "DynamicOnly"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors!.OfType<ApiProblemDescriptor>().Single().Code
            .Should().Be(AppProblemCodes.PlannerModelUnavailable);
    }

    [Fact]
    public async Task DynamicPlanner_ShouldRejectSqlStatementInPlannerStep()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var plannerModel = CreatePlannerModel();
        var dynamicPlanner = new FixedDynamicPlanner(
            new AgentStepPlanDto(
                "Unsafe SQL",
                "SELECT * FROM employees",
                AgentStepType.ArtifactGeneration,
                "generate_markdown_report",
                false,
                """{"sql":"SELECT * FROM employees"}"""));
        var handler = CreatePlanHandler(
            session,
            dynamicPlanner,
            [plannerModel],
            [CreateSimulationDescriptor()]);

        var result = await handler.Handle(
            new PlanAgentTaskCommand(
                session.Id.Value,
                "Analyze employees.",
                AgentTaskType.ReportGeneration,
                null,
                DataSourceIds: [SimulationDataSourceId],
                PlannerMode: "DynamicOnly"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors!.OfType<ApiProblemDescriptor>().Single().Should().BeEquivalentTo(
            new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanToolDenied,
                "Agent plan contains SQL statement semantics."));
    }

    [Fact]
    public async Task PlanAgentTask_ShouldRejectNonSimulationBusinessDataSource()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var handler = CreatePlanHandler(
            session,
            new ThrowingDynamicPlanner(),
            [],
            [CreateSimulationDescriptor(), CreateNonSimulationDescriptor()]);

        var result = await handler.Handle(
            new PlanAgentTaskCommand(
                session.Id.Value,
                "Analyze external data.",
                AgentTaskType.ReportGeneration,
                null,
                DataSourceIds: [Guid.Parse("33333333-3333-4333-8333-333333333333")],
                PlannerMode: "Auto"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void P3AcceptanceScript_ShouldChainP2AndEmitDynamicPlannerReport()
    {
        var root = FindAicopilotRoot();
        var script = Path.Combine(root, "scripts", "Run-EnterpriseDynamicPlannerP3Acceptance.ps1");

        File.Exists(script).Should().BeTrue();
        var content = File.ReadAllText(script);
        content.Should().Contain("Run-EnterpriseAgentWorkbenchP2Acceptance.ps1");
        content.Should().Contain("enterprise-dynamic-planner-p3-latest.md");
        content.Should().Contain("EnterpriseDynamicPlannerP3Tests");
        content.Should().Contain("SimulationBusiness");
    }

    private static PlanAgentTaskCommandHandler CreatePlanHandler(
        Session session,
        IAgentDynamicPlanner dynamicPlanner,
        IReadOnlyCollection<LanguageModel> models,
        IReadOnlyCollection<BusinessDatabaseDescriptor> dataSources,
        InMemoryRepository<AgentTask>? taskRepository = null)
    {
        return new PlanAgentTaskCommandHandler(
            taskRepository ?? new InMemoryRepository<AgentTask>(),
            new InMemoryRepository<ApprovalRequest>(),
            new InMemoryRepository<Session>(session),
            new InMemoryRepository<UploadRecord>(),
            new InMemoryRepository<ConversationTemplate>(),
            new InMemoryRepository<LanguageModel>(models.ToArray()),
            new FixedRuntimeSettingsProvider(),
            new WorkspaceService(),
            new AgentAuditRecorder(new CapturingAuditLogWriter()),
            [],
            new AgentPlanToolGuard(
                CreateGuard(CreatePlannerTools()),
                new StubAgentPluginCatalog()),
            dynamicPlanner,
            new FixedCloudReadonlyAgentPlanService(),
            new TestCurrentUser(UserId),
            new StaticBusinessDatabaseReadService(dataSources));
    }

    private static IReadOnlyCollection<ToolRegistration> CreatePlannerTools()
    {
        return
        [
            CreateTool("query_business_database_readonly", ToolProviderType.BuiltIn, requiresApproval: true, riskLevel: AiToolRiskLevel.RequiresApproval),
            CreateTool("summarize_business_query_result", ToolProviderType.BuiltIn),
            CreateTool("generate_business_chart", ToolProviderType.Artifact),
            CreateTool("generate_markdown_report", ToolProviderType.Artifact),
            CreateTool("generate_html_report", ToolProviderType.Artifact),
            CreateTool("generate_pdf", ToolProviderType.Artifact, requiresApproval: true, riskLevel: AiToolRiskLevel.RequiresApproval),
            CreateTool("generate_pptx", ToolProviderType.Artifact, requiresApproval: true, riskLevel: AiToolRiskLevel.RequiresApproval),
            CreateTool("generate_xlsx", ToolProviderType.Artifact, requiresApproval: true, riskLevel: AiToolRiskLevel.RequiresApproval),
            CreateTool("finalize_artifacts", ToolProviderType.Artifact, requiresApproval: true, riskLevel: AiToolRiskLevel.RequiresApproval)
        ];
    }

    private static LanguageModel CreatePlannerModel()
    {
        return new LanguageModel(
            "FakeEval",
            "planner",
            "http://localhost/fake",
            "fake-key",
            new ModelParameters { MaxTokens = 4096, MaxOutputTokens = 1024, Temperature = 0 },
            "FakeEval",
            LanguageModelUsage.Chat | LanguageModelUsage.Planner,
            true);
    }

    private static ToolRegistryGuard CreateGuard(IReadOnlyCollection<ToolRegistration> tools)
    {
        return new ToolRegistryGuard(
            new InMemoryRepository<ToolRegistration>(tools.ToArray()),
            new StubIdentityAccessService());
    }

    private static ToolRegistration CreateTool(
        string toolCode,
        ToolProviderType providerType = ToolProviderType.BuiltIn,
        bool requiresApproval = false,
        AiToolRiskLevel riskLevel = AiToolRiskLevel.Low)
    {
        return new ToolRegistration(
            toolCode,
            toolCode,
            "test tool",
            providerType,
            ToolRegistrationTargetType.AgentRuntime,
            "AgentTaskRuntime",
            """{"type":"object"}""",
            """{"type":"object"}""",
            riskLevel,
            null,
            requiresApproval,
            true,
            120,
            ToolAuditLevel.Standard,
            DateTimeOffset.UtcNow);
    }

    private static BusinessDatabaseDescriptor CreateSimulationDescriptor()
    {
        return new BusinessDatabaseDescriptor(
            Id: SimulationDataSourceId,
            Name: "aicopilot_sim_business",
            Description: "AI independent simulation business database",
            Provider: DatabaseProviderType.PostgreSql,
            IsEnabled: true,
            IsReadOnly: true,
            ExternalSystemType: DataSourceExternalSystemType.SimulationBusiness,
            ReadOnlyCredentialVerified: true,
            Category: "Production",
            Tags: ["simulation"],
            OwnerDepartment: "AI Platform",
            BusinessDomain: "Production",
            SensitivityLevel: "Internal",
            DefaultQueryLimit: 200,
            MaxQueryLimit: 1000,
            IsSelectableInChat: true,
            IsSelectableInAgent: true);
    }

    private static BusinessDatabaseDescriptor CreateNonSimulationDescriptor()
    {
        return new BusinessDatabaseDescriptor(
            Id: Guid.Parse("33333333-3333-4333-8333-333333333333"),
            Name: "external_readonly",
            Description: "readonly external database",
            Provider: DatabaseProviderType.PostgreSql,
            IsEnabled: true,
            IsReadOnly: true,
            ExternalSystemType: DataSourceExternalSystemType.NonCloud,
            ReadOnlyCredentialVerified: true,
            IsSelectableInAgent: true);
    }

    private static string FindAicopilotRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "hosts", "AICopilot.HttpApi")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the AICopilot repository root.");
    }

    private sealed class FixedDynamicPlanner(params AgentStepPlanDto[] steps) : IAgentDynamicPlanner
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

    private sealed class ThrowingDynamicPlanner : IAgentDynamicPlanner
    {
        public Task<Result<IReadOnlyCollection<AgentStepPlanDto>>> CreatePlanAsync(
            AgentDynamicPlannerRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Dynamic planner should not be called.");
        }
    }

    private sealed class FixedCloudReadonlyAgentPlanService : ICloudReadonlyAgentPlanService
    {
        public Task<Result<CloudReadonlyAgentPlanIntent>> CreateIntentAsync(
            Guid sessionId,
            string goal,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(new CloudReadonlyAgentPlanIntent(
                "Analysis.Device.List",
                null,
                0.95,
                "Device",
                "List",
                "target=Device; kind=List")));
        }
    }

    private sealed class StaticBusinessDatabaseReadService(
        IReadOnlyCollection<BusinessDatabaseDescriptor> descriptors) : IBusinessDatabaseReadService
    {
        public Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListEnabledAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<BusinessDatabaseDescriptor>>(descriptors.ToArray());
        }

        public Task<BusinessDatabaseConnectionInfo?> GetByNameAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<BusinessDatabaseConnectionInfo?>(null);
        }
    }

    private sealed class FixedRuntimeSettingsProvider : IChatRuntimeSettingsProvider
    {
        public Task<ChatRuntimeSettingsDto> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatRuntimeSettingsDto(6, 12, 4, 30, 40, 12000));
        }
    }

    private sealed class WorkspaceService : IAgentArtifactWorkspaceService
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
    }

    private sealed class CapturingAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }
    }

    private sealed class StubIdentityAccessService : IIdentityAccessService
    {
        public Task<CurrentUserAccess?> GetCurrentUserAccessAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<CurrentUserAccess?>(new CurrentUserAccess(userId, "test-user", "User", []));
        }

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(string roleName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<string>>([]);
        }

        public Task SyncRolePermissionsAsync(
            string roleName,
            IEnumerable<string> permissionCodes,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubAgentPluginCatalog : IAgentPluginCatalog
    {
        public AiToolDefinition[] GetTools(params string[] names) => [];

        public AiToolDefinition[] GetPluginTools(string name) => [];

        public AiToolDefinition[] GetAllTools() => [];

        public IAgentPlugin? GetPlugin(string name) => null;

        public IAgentPlugin[] GetAllPlugin() => [];
    }

    private sealed class InMemoryRepository<T>(params T[] initialItems) : IRepository<T>
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
}
