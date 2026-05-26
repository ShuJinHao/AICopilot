using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Result;
using MediatR;

namespace AICopilot.BackendTests;

[Trait("Suite", "EnterpriseAgentWorkbenchP2")]
public sealed class EnterpriseAgentWorkbenchP2Tests
{
    private static readonly Guid SimulationDataSourceId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TrialScenarioCatalog_ShouldExposeSixSimulationBusinessTemplates()
    {
        var scenarios = AgentTrialScenarioCatalog.Build([CreateSimulationDescriptor()]);

        scenarios.Should().HaveCount(6);
        scenarios.Should().OnlyContain(item => item.IsSimulationOnly);
        scenarios.Should().OnlyContain(item => item.RequiresApproval);
        scenarios.Should().OnlyContain(item => item.DefaultDataSourceIds.Contains(SimulationDataSourceId));
        scenarios.Should().Contain(item => item.Id == "capacity-analysis" && item.DefaultArtifactTypes.Contains("Pptx"));
        scenarios.Should().Contain(item => item.Id == "employee-policy-rag" &&
                                           item.DefaultPrompt.Contains("CriticalOverride", StringComparison.Ordinal) &&
                                           item.DefaultPrompt.Contains("模拟制度", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateTaskFromTrialScenario_ShouldUseAutoSimulationPlannerMode()
    {
        var sender = new CapturingSender();
        var handler = new CreateAgentTaskFromTrialScenarioCommandHandler(
            new StaticBusinessDatabaseReadService([CreateSimulationDescriptor()]),
            sender);

        var result = await handler.Handle(
            new CreateAgentTaskFromTrialScenarioCommand(
                Guid.Parse("11111111-1111-4111-8111-111111111111"),
                "quality-defects"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sender.PlanCommand.Should().NotBeNull();
        sender.PlanCommand!.TaskType.Should().Be(AgentTaskType.ReportGeneration);
        sender.PlanCommand.PlannerMode.Should().Be("Auto");
        sender.PlanCommand.ForceStaticPlanner.Should().BeFalse();
        sender.PlanCommand.IsSimulationTrial.Should().BeTrue();
        sender.PlanCommand.TrialScenarioId.Should().Be("quality-defects");
        sender.PlanCommand.QueryMode.Should().Be("TextToSql");
        sender.PlanCommand.RequiresDataApproval.Should().BeTrue();
        sender.PlanCommand.DataSourceIds.Should().BeEquivalentTo([SimulationDataSourceId]);
        sender.PlanCommand.BusinessDomains.Should().ContainSingle().Which.Should().Be("Quality");
    }

    [Fact]
    public async Task CreateTaskFromTrialScenario_ShouldRejectNonSimulationOverrides()
    {
        var handler = new CreateAgentTaskFromTrialScenarioCommandHandler(
            new StaticBusinessDatabaseReadService([CreateSimulationDescriptor(), CreateNonCloudDescriptor()]),
            new CapturingSender());

        var result = await handler.Handle(
            new CreateAgentTaskFromTrialScenarioCommand(
                Guid.Parse("11111111-1111-4111-8111-111111111111"),
                "capacity-analysis",
                DataSourceIds: [Guid.Parse("33333333-3333-4333-8333-333333333333")]),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void AgentReportComposer_ShouldRenderBusinessQueryMarkersForP2Artifacts()
    {
        var task = new AgentTask(
            SessionId.New(),
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            "P2 SimulationBusiness trial",
            "Run a capacity trial scenario.",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            "{}",
            DateTimeOffset.UtcNow);
        var state = new AgentTaskRunState
        {
            CloudReadonlySummary = "BusinessDatabase Text-to-SQL executed. sourceType=BusinessDatabase; sourceMode=SimulationBusiness; isSimulation=true; sourceLabel=AI 独立模拟业务库; queryHash=abc123; rows=2; truncated=false.",
            CloudReadonlySourceMode = "SimulationBusiness",
            CloudReadonlyIsSimulation = true,
            CloudReadonlySourceLabel = "AI 独立模拟业务库",
            CloudReadonlySourcePath = "BusinessDataSourceCenter/TextToSql",
            CloudReadonlyRowCount = 2,
            CloudReadonlyIsTruncated = false,
            BusinessQueryHash = "abc123",
            CloudReadonlyRows =
            [
                new Dictionary<string, object?> { ["line"] = "Line-A", ["actualOutput"] = 100 },
                new Dictionary<string, object?> { ["line"] = "Line-B", ["actualOutput"] = 95 }
            ]
        };
        state.BusinessQueryResults.Add(new AgentBusinessQuerySummary(
            SimulationDataSourceId,
            "aicopilot_sim_business",
            "SimulationBusiness",
            true,
            "AI 独立模拟业务库",
            "abc123",
            2,
            false,
            null));

        var report = AgentReportComposer.BuildReportDocument(task, state);
        var markdown = AgentReportComposer.BuildMarkdownReport(task, state);
        var html = AgentReportComposer.BuildHtmlReport(task, state);
        using var chartDocument = JsonDocument.Parse(JsonSerializer.Serialize(AgentReportComposer.BuildChartPayload(state), JsonOptions));

        report.BusinessQueryResults.Should().ContainSingle(item => item.QueryHash == "abc123");
        markdown.Should().Contain("## Business Query Results");
        markdown.Should().Contain("sourceMode=SimulationBusiness");
        markdown.Should().Contain("AI 独立模拟业务库");
        markdown.Should().Contain("abc123");
        html.Should().Contain("Business Query Results");
        html.Should().Contain("SimulationBusiness");
        chartDocument.RootElement.GetProperty("sourceInfo").GetProperty("queryHash").GetString().Should().Be("abc123");
    }

    [Fact]
    public void P2AcceptanceScript_ShouldChainP15AndEmitWorkbenchReport()
    {
        var root = FindAicopilotRoot();
        var script = Path.Combine(root, "scripts", "Run-EnterpriseAgentWorkbenchP2Acceptance.ps1");

        File.Exists(script).Should().BeTrue();
        var content = File.ReadAllText(script);
        content.Should().Contain("Run-EnterpriseDataGovernanceP1_5Acceptance.ps1");
        content.Should().Contain("enterprise-agent-workbench-p2-latest.md");
        content.Should().Contain("EnterpriseAgentWorkbenchP2Tests");
        content.Should().Contain("SimulationBusiness");
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

    private static BusinessDatabaseDescriptor CreateNonCloudDescriptor()
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

    private sealed class StaticBusinessDatabaseReadService(
        IReadOnlyList<BusinessDatabaseDescriptor> descriptors) : IBusinessDatabaseReadService
    {
        public Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListEnabledAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(descriptors);
        }

        public Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListSelectableAsync(
            DataSourceSelectionMode selectionMode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(descriptors);
        }

        public Task<BusinessDatabaseConnectionInfo?> GetByNameAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<BusinessDatabaseConnectionInfo?>(null);
        }
    }

    private sealed class CapturingSender : ISender
    {
        public PlanAgentTaskCommand? PlanCommand { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            if (request is PlanAgentTaskCommand command)
            {
                PlanCommand = command;
                var dto = new AgentTaskDto(
                    Guid.NewGuid(),
                    "agt_p2",
                    command.SessionId,
                    command.TrialScenarioTitle ?? "P2 trial",
                    command.Goal,
                    command.TaskType.ToString(),
                    "WaitingPlanApproval",
                    "Low",
                    null,
                    Guid.NewGuid(),
                    "{}",
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    null,
                    []);
                return Task.FromResult((TResponse)(object)Result.Success(dto));
            }

            throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
