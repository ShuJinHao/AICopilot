using System.Text.Json;
using System.Text.Json.Serialization;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Tools;
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
    private const int MaxDynamicSteps = 20;
    private const int MaxPlannerResponseTextLength = 32000;
    private const int MaxStepInputJsonLength = 8000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    private static readonly HashSet<string> AllowedRootProperties = new(StringComparer.Ordinal)
    {
        "steps"
    };

    private static readonly HashSet<string> AllowedStepProperties = new(StringComparer.Ordinal)
    {
        "title",
        "description",
        "stepType",
        "toolCode",
        "requiresApproval",
        "inputJson"
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
                ComposeInstructions,
                options =>
                {
                    options.Temperature = 0;
                    options.MaxOutputTokens = Math.Clamp(request.PlannerModel.Parameters.MaxOutputTokens, 512, 4096);
                    options.Tools = [];
                });

            var payload = JsonSerializer.Serialize(BuildPlannerInput(request), JsonOptions);
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

            using var document = ParsePlannerResponse(response);
            var parseResult = ParsePlanDocument(document.RootElement);
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

    private static string ComposeInstructions(string basePrompt)
    {
        return string.Join(
            "\n\n",
            basePrompt,
            "You are a backend-controlled planning component. Return JSON only.",
            "Return exactly one JSON object: {\"steps\":[...]} with no Markdown and no explanation.",
            "Each step may contain only title, description, stepType, toolCode, requiresApproval, inputJson.",
            "Use only toolCode values supplied by the backend plannerToolCatalog. Do not invent tools, shell commands, paths, SQL, Cloud writes, or Cloud intent.",
            "Use each tool's inputSchema summary to create inputJson when arguments are required.",
            "inputJson must be a JSON object string or object matching the supplied registry schema summary. Omit it when no arguments are needed.");
    }

    private static object BuildPlannerInput(AgentDynamicPlannerRequest request)
    {
        return new
        {
            goal = SanitizePlannerField(request.Goal, 2000),
            taskType = request.TaskType.ToString(),
            uploadIds = request.UploadIds.Select(id => id.ToString("N")).ToArray(),
            knowledgeBaseIds = request.KnowledgeBaseIds.Select(id => id.ToString("N")).ToArray(),
            dataSources = (request.DataSources ?? []).Select(source => new
            {
                id = source.Id.ToString("N"),
                name = SanitizePlannerField(source.Name, 160),
                externalSystemType = SanitizePlannerField(source.ExternalSystemType, 80),
                businessDomain = SanitizePlannerField(source.BusinessDomain, 120),
                isSimulation = source.IsSimulation,
                sourceLabel = SanitizePlannerField(source.SourceLabel, 160)
            }),
            businessDomains = (request.BusinessDomains ?? [])
                .Select(domain => SanitizePlannerField(domain, 120))
                .Where(domain => !string.IsNullOrWhiteSpace(domain))
                .ToArray(),
            queryMode = SanitizePlannerField(request.QueryMode, 80),
            artifactTypes = (request.ArtifactTypes ?? [])
                .Select(type => SanitizePlannerField(type, 40))
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .ToArray(),
            trialScenario = string.IsNullOrWhiteSpace(request.TrialScenarioId)
                ? null
                : new
                {
                    id = SanitizePlannerField(request.TrialScenarioId, 160),
                    title = SanitizePlannerField(request.TrialScenarioTitle, 200),
                    isSimulationOnly = request.IsSimulationTrial
                },
            plannerToolCatalog = new
            {
                version = request.ToolCatalog.Version,
                availableToolCount = request.ToolCatalog.AvailableToolCount,
                mockMcpOnly = true,
                riskSummary = request.ToolCatalog.Tools
                    .GroupBy(tool => tool.RiskLevel, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase)
            },
            tools = request.ToolCatalog.Tools.Select(tool => new
            {
                toolCode = SanitizePlannerField(tool.ToolCode, 160),
                displayName = SanitizePlannerField(tool.DisplayName, 160),
                description = SanitizePlannerField(tool.Description, 1000),
                providerType = tool.ProviderType,
                providerKind = tool.ProviderKind,
                isMock = tool.IsMock,
                category = SanitizePlannerField(tool.Category, 120),
                businessDomains = (tool.BusinessDomains ?? [])
                    .Select(domain => SanitizePlannerField(domain, 120))
                    .Where(domain => !string.IsNullOrWhiteSpace(domain))
                    .ToArray(),
                dataBoundary = tool.DataBoundary,
                targetType = tool.TargetType,
                targetName = SanitizePlannerField(tool.TargetName, 200),
                riskLevel = tool.RiskLevel,
                requiresApproval = tool.RequiresApproval,
                approvalPolicy = SanitizePlannerField(tool.ApprovalPolicy, 120),
                schemaVersion = tool.SchemaVersion,
                catalogVersion = tool.CatalogVersion,
                timeoutSeconds = tool.TimeoutSeconds,
                auditLevel = tool.AuditLevel,
                runtimeAvailable = tool.RuntimeAvailable,
                inputSchema = tool.InputSchema ?? BuildFallbackSchemaSummary(tool.InputSchemaJson),
                outputSchema = tool.OutputSchema ?? BuildFallbackSchemaSummary(null)
            }),
            constraints = new
            {
                maxSteps = MaxDynamicSteps,
                output = "json_only",
                cloudIntent = "backend_only",
                simulationOnly = request.IsSimulationTrial || (request.DataSources ?? []).Any(source => source.IsSimulation),
                mockMcpOnly = true,
                externalMcp = "disabled_in_p4",
                requiresDataApproval = request.RequiresDataApproval,
                forbidden = new[] { "shell", "arbitrary_path", "sql", "cloud_write", "real_external_mcp", "unregistered_tool", "non_simulation_business_source" }
            },
            runtimeSettings = new
            {
                request.RuntimeSettings.AgentPlanningHistoryCount,
                request.RuntimeSettings.ContextTokenLimit
            }
        };
    }

    private static PlannerToolSchemaSummary BuildFallbackSchemaSummary(string? schemaJson)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(schemaJson) ? "{}" : schemaJson);
            var root = document.RootElement;
            var type = root.ValueKind == JsonValueKind.Object &&
                       root.TryGetProperty("type", out var typeElement) &&
                       typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : "object";
            return new PlannerToolSchemaSummary(
                SanitizePlannerField(type, 64) ?? "object",
                [],
                [],
                null,
                !string.IsNullOrWhiteSpace(schemaJson) && schemaJson.Length > 4000);
        }
        catch (JsonException)
        {
            return new PlannerToolSchemaSummary("object", [], [], null, true);
        }
    }

    private static JsonDocument ParsePlannerResponse(StructuredAgentResponse<JsonElement> response)
    {
        if (response.Result.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
        {
            return JsonDocument.Parse(response.Result.GetRawText());
        }

        if (string.IsNullOrWhiteSpace(response.Text))
        {
            throw new JsonException("Planner response was empty.");
        }

        var text = response.Text.Trim();
        if (text.Length > MaxPlannerResponseTextLength)
        {
            throw new JsonException("Planner response exceeds the allowed length.");
        }

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            throw new JsonException("Planner response must be raw JSON and must not be wrapped in Markdown.");
        }

        return JsonDocument.Parse(text);
    }

    private static Result<IReadOnlyCollection<AgentStepPlanDto>> ParsePlanDocument(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Invalid("Planner output root must be a JSON object.");
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!AllowedRootProperties.Contains(property.Name))
            {
                return Invalid($"Planner output contains unknown root field '{property.Name}'.");
            }
        }

        if (!root.TryGetProperty("steps", out var stepsElement) ||
            stepsElement.ValueKind != JsonValueKind.Array)
        {
            return Invalid("Planner output must contain a steps array.");
        }

        var steps = new List<AgentStepPlanDto>();
        foreach (var stepElement in stepsElement.EnumerateArray())
        {
            if (steps.Count >= MaxDynamicSteps)
            {
                return Invalid($"Planner output exceeds the maximum of {MaxDynamicSteps} steps.");
            }

            var stepResult = ParseStep(stepElement);
            if (!stepResult.IsSuccess)
            {
                return Result.From(stepResult);
            }

            steps.Add(stepResult.Value!);
        }

        if (steps.Count == 0)
        {
            return Invalid("Planner output must contain at least one step.");
        }

        return Result.Success<IReadOnlyCollection<AgentStepPlanDto>>(steps);
    }

    private static Result<AgentStepPlanDto> ParseStep(JsonElement stepElement)
    {
        if (stepElement.ValueKind != JsonValueKind.Object)
        {
            return InvalidStep("Planner step must be a JSON object.");
        }

        foreach (var property in stepElement.EnumerateObject())
        {
            if (!AllowedStepProperties.Contains(property.Name))
            {
                return InvalidStep($"Planner step contains unknown field '{property.Name}'.");
            }
        }

        var title = ReadRequiredString(stepElement, "title");
        var toolCode = ReadRequiredString(stepElement, "toolCode");
        if (title is null || toolCode is null)
        {
            return InvalidStep("Planner step title and toolCode are required.");
        }

        var description = ReadString(stepElement, "description") ?? title;
        if (!TryReadStepType(stepElement, out var stepType))
        {
            return InvalidStep("Planner step has an invalid stepType.");
        }

        var requiresApproval = ReadBool(stepElement, "requiresApproval") ?? false;
        var inputResult = ReadInputJson(stepElement);
        if (!inputResult.IsSuccess)
        {
            return Result.From(inputResult);
        }

        return Result.Success(new AgentStepPlanDto(
            title,
            description,
            stepType,
            toolCode,
            requiresApproval,
            inputResult.Value));
    }

    private static string? ReadRequiredString(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? CloudReadonlyAgentTextGuard.SanitizeForPlan(property.GetString(), 1000)
            : null;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
    }

    private static bool TryReadStepType(JsonElement element, out AgentStepType stepType)
    {
        stepType = default;
        if (!element.TryGetProperty("stepType", out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return Enum.TryParse(property.GetString(), ignoreCase: true, out stepType);
        }

        if (property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out var value) ||
            !Enum.IsDefined(typeof(AgentStepType), value))
        {
            return false;
        }

        stepType = (AgentStepType)value;
        return true;
    }

    private static Result<string?> ReadInputJson(JsonElement stepElement)
    {
        if (!stepElement.TryGetProperty("inputJson", out var property) ||
            property.ValueKind == JsonValueKind.Null ||
            property.ValueKind == JsonValueKind.Undefined)
        {
            return Result.Success<string?>(null);
        }

        if (property.ValueKind == JsonValueKind.Object)
        {
            var rawObject = property.GetRawText();
            return rawObject.Length > MaxStepInputJsonLength
                ? InvalidInput("Planner step inputJson exceeds the allowed length.")
                : Result.Success<string?>(rawObject);
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var raw = property.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Result.Success<string?>(null);
            }

            if (raw.Length > MaxStepInputJsonLength)
            {
                return InvalidInput("Planner step inputJson exceeds the allowed length.");
            }

            try
            {
                using var document = JsonDocument.Parse(raw);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return InvalidInput("Planner step inputJson must be a JSON object.");
                }

                return Result.Success<string?>(document.RootElement.GetRawText());
            }
            catch (JsonException ex)
            {
                return InvalidInput($"Planner step inputJson is invalid JSON: {ex.Message}");
            }
        }

        return InvalidInput("Planner step inputJson must be omitted, a JSON object, or a JSON object string.");
    }

    private static Result<IReadOnlyCollection<AgentStepPlanDto>> Invalid(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(AppProblemCodes.AgentPlanInvalid, detail));
    }

    private static Result<AgentStepPlanDto> InvalidStep(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(AppProblemCodes.AgentPlanInvalid, detail));
    }

    private static Result<string?> InvalidInput(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(AppProblemCodes.AgentPlanInvalid, detail));
    }

    private static string? SanitizePlannerField(string? value, int maxLength)
    {
        return ToolExecutionRecordSanitizer.Sanitize(
            CloudReadonlyAgentTextGuard.SanitizeForPlan(value, maxLength),
            maxLength);
    }
}
