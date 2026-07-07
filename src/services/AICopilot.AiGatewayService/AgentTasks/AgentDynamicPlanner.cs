using System.Text;
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
using Microsoft.Extensions.Options;

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
    bool RequiresDataApproval = false,
    string? SkillCode = null,
    string? SkillName = null,
    string? SkillDescription = null)
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

public sealed class DefaultAgentDynamicPlanner(
    ConfiguredAgentRuntimeFactory configuredAgentFactory,
    IOptions<AgentModelCallTimeoutOptions>? modelCallTimeoutOptions = null) : IAgentDynamicPlanner
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
            await using var scopedAgent = await configuredAgentFactory.CreateAgentAsync(
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
            var responseText = await RunPlannerAsPlainTextAsync(
                scopedAgent,
                payload,
                request.PlannerModel,
                cancellationToken);

            var response = new StructuredAgentResponse<JsonElement>(responseText, default);
            using var document = AgentDynamicPlannerResponseParser.ParsePlannerResponse(response);
            var parseResult = AgentDynamicPlannerResponseParser.ParsePlanDocument(document.RootElement);
            return parseResult.IsSuccess
                ? Result.Success<IReadOnlyCollection<AgentStepPlanDto>>(parseResult.Value!)
                : Result.From(parseResult);
        }
        catch (AgentWorkflowException ex)
        {
            return Result.Failure(new ApiProblemDescriptor(
                ex.Code == AppProblemCodes.ModelRequestTimeout
                    ? AppProblemCodes.ModelRequestTimeout
                    : AppProblemCodes.PlannerModelUnavailable,
                ex.UserFacingMessage));
        }
        catch (JsonException)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                "Planner returned invalid JSON."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.PlannerModelUnavailable,
                "Planner model call failed before a valid plan was produced."));
        }
    }

    private async Task<string> RunPlannerAsPlainTextAsync(
        ScopedRuntimeAgent scopedAgent,
        string payload,
        LanguageModel plannerModel,
        CancellationToken cancellationToken)
    {
        var timeout = (modelCallTimeoutOptions?.Value ?? new AgentModelCallTimeoutOptions()).PlannerTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var timeoutToken = timeoutCts.Token;
            var session = await scopedAgent.Agent.CreateSessionAsync(timeoutToken);
            var builder = new StringBuilder();
            await foreach (var update in scopedAgent.Agent.RunStreamingAsync(
                    [new AiChatMessage(AiChatRole.User, payload)],
                    session,
                    new RuntimeAgentRunOptions(new AiChatOptions
                    {
                        Temperature = 0,
                        MaxOutputTokens = Math.Clamp(plannerModel.Parameters.MaxOutputTokens, 512, 4096),
                        Tools = []
                    }),
                    timeoutToken))
            {
                foreach (var content in update.Contents.OfType<AiTextContent>())
                {
                    builder.Append(content.Text);
                }
            }

            return builder.ToString();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new AgentWorkflowException(
                AppProblemCodes.ModelRequestTimeout,
                $"Agent planner model call exceeded {timeout.TotalSeconds:N0} seconds.",
                "任务规划模型响应超时，请稍后重试。");
        }
    }
}
