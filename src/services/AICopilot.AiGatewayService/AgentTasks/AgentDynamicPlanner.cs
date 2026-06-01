using System.Text.Json;
using System.Text.Json.Serialization;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Safety;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed record AgentDynamicPlannerRequest(
    string Goal,
    AgentTaskType TaskType,
    IReadOnlyCollection<Guid> UploadIds,
    IReadOnlyCollection<Guid> KnowledgeBaseIds,
    PlannerToolCatalog ToolCatalog,
    LanguageModel PlannerModel,
    ChatRuntimeSettingsDto RuntimeSettings,
    IReadOnlyCollection<AgentPlannerDataSourceSummary>? DataSources = null,
    IReadOnlyCollection<string>? BusinessDomains = null,
    string? QueryMode = null,
    IReadOnlyCollection<string>? ArtifactTypes = null,
    string? TrialScenarioId = null,
    string? TrialScenarioTitle = null,
    bool IsSimulationTrial = false,
    bool RequiresDataApproval = false)
{
    public AgentDynamicPlannerRequest(
        string goal,
        AgentTaskType taskType,
        IReadOnlyCollection<Guid> uploadIds,
        IReadOnlyCollection<Guid> knowledgeBaseIds,
        IReadOnlyCollection<AgentPlannerToolSummary> availableTools,
        LanguageModel plannerModel,
        ChatRuntimeSettingsDto runtimeSettings)
        : this(
            goal,
            taskType,
            uploadIds,
            knowledgeBaseIds,
            new PlannerToolCatalog(
                PlannerToolCatalog.CurrentVersion,
                availableTools.Count,
                availableTools),
            plannerModel,
            runtimeSettings)
    {
    }

    public IReadOnlyCollection<AgentPlannerToolSummary> AvailableTools => ToolCatalog.Tools;
}

public sealed record AgentPlannerDataSourceSummary(
    Guid Id,
    string Name,
    string ExternalSystemType,
    string? BusinessDomain,
    bool IsSimulation,
    string SourceLabel);

public interface IAgentDynamicPlanner
{
    Task<Result<IReadOnlyCollection<AgentStepPlanDto>>> CreatePlanAsync(
        AgentDynamicPlannerRequest request,
        CancellationToken cancellationToken);
}

public sealed class DefaultAgentDynamicPlanner(ChatAgentFactory chatAgentFactory) : IAgentDynamicPlanner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    public async Task<Result<IReadOnlyCollection<AgentStepPlanDto>>> CreatePlanAsync(
        AgentDynamicPlannerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scopedAgent = await chatAgentFactory.CreateAgentAsync(
                "agent_planner",
                request.PlannerModel,
                AgentDynamicPlannerPromptComposer.ComposeInstructions,
                options =>
                {
                    options.Temperature = 0;
                    options.MaxOutputTokens = Math.Clamp(request.PlannerModel.Parameters.MaxOutputTokens, 512, 4096);
                    options.Tools = [];
                });

            var payload = JsonSerializer.Serialize(AgentDynamicPlannerInputBuilder.Build(request), JsonOptions);
            var response = await scopedAgent.Agent.RunStructuredAsync<JsonElement>(
                [new AiChatMessage(AiChatRole.User, payload)],
                null,
                JsonOptions,
                new RuntimeAgentRunOptions(new AiChatOptions
                {
                    Temperature = 0,
                    MaxOutputTokens = Math.Clamp(request.PlannerModel.Parameters.MaxOutputTokens, 512, 4096),
                    Tools = []
                }),
                cancellationToken);

            using var document = AgentDynamicPlannerResponseParser.ParsePlannerResponse(response);
            var parseResult = AgentDynamicPlannerResponseParser.ParsePlanDocument(document.RootElement);
            return parseResult.IsSuccess
                ? Result.Success<IReadOnlyCollection<AgentStepPlanDto>>(parseResult.Value!)
                : Result.From(parseResult);
        }
        catch (ChatWorkflowException ex)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.PlannerModelUnavailable,
                ex.UserFacingMessage));
        }
        catch (JsonException ex)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                $"Planner returned invalid JSON: {ex.Message}"));
        }
    }
}
