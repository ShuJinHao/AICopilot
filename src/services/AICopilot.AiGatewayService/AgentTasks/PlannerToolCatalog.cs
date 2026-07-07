using System.Text.Json;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed record PlannerToolCatalog(
    int Version,
    int AvailableToolCount,
    IReadOnlyCollection<AgentPlannerToolSummary> Tools)
{
    public const int CurrentVersion = BuiltInToolRegistrations.CurrentCatalogVersion;
}

public static class PlannerToolCatalogMetadata
{
    public static bool IsMockMcpOnly(IEnumerable<AgentPlannerToolSummary> tools)
    {
        var toolList = tools.ToArray();
        return toolList.Length > 0 && toolList.All(tool => tool.IsMock);
    }
}

public sealed record AgentPlannerToolSummary(
    string ToolCode,
    string DisplayName,
    string Description,
    string ProviderType,
    string TargetType,
    string TargetName,
    string InputSchemaJson,
    bool RequiresApproval,
    string RiskLevel,
    int TimeoutSeconds = 120,
    string AuditLevel = "Standard",
    bool RuntimeAvailable = true,
    PlannerToolSchemaSummary? InputSchema = null,
    PlannerToolSchemaSummary? OutputSchema = null,
    string Category = "General",
    IReadOnlyCollection<string>? BusinessDomains = null,
    string DataBoundary = "NoData",
    bool IsVisibleToPlanner = true,
    bool IsExecutableByAgent = true,
    int SchemaVersion = 1,
    int CatalogVersion = PlannerToolCatalog.CurrentVersion,
    string ApprovalPolicy = "None",
    string ProviderKind = "BuiltIn",
    bool IsMock = false);

public sealed record PlannerToolSchemaSummary(
    string Type,
    IReadOnlyCollection<string> Required,
    IReadOnlyCollection<PlannerToolPropertySummary> Properties,
    string? ItemsType,
    bool IsTruncated);

public sealed record PlannerToolPropertySummary(
    string Name,
    string Type,
    IReadOnlyCollection<string> Enum,
    bool Required);

internal static class PlannerToolCatalogBuilder
{
    private const int MaxSchemaProperties = 24;
    private const int MaxEnumValues = 12;
    private const int MaxTextLength = 240;

    public static Result<PlannerToolCatalog> Build(
        IEnumerable<ToolRegistration> tools,
        IReadOnlySet<string> runtimeMcpToolCodes)
    {
        var summaries = new List<AgentPlannerToolSummary>();
        foreach (var tool in tools.OrderBy(item => item.ToolCode, StringComparer.OrdinalIgnoreCase))
        {
            if (!tool.IsVisibleToPlanner ||
                !tool.IsExecutableByAgent ||
                tool.RiskLevel is AiToolRiskLevel.Blocked or AiToolRiskLevel.Critical)
            {
                continue;
            }

            var runtimeAvailable = tool.ProviderType != ToolProviderType.Mcp ||
                                   runtimeMcpToolCodes.Contains(tool.ToolCode);
            if (!runtimeAvailable)
            {
                continue;
            }

            var inputSchema = SummarizeSchema(tool.InputSchemaJson, $"{tool.ToolCode} input");
            if (!inputSchema.IsSuccess)
            {
                return Result.From(inputSchema);
            }

            var outputSchema = SummarizeSchema(tool.OutputSchemaJson, $"{tool.ToolCode} output");
            if (!outputSchema.IsSuccess)
            {
                return Result.From(outputSchema);
            }

            summaries.Add(new AgentPlannerToolSummary(
                Sanitize(tool.ToolCode, 160) ?? tool.ToolCode,
                Sanitize(tool.DisplayName, 160) ?? tool.ToolCode,
                Sanitize(tool.Description, 1000) ?? string.Empty,
                tool.ProviderType.ToString(),
                tool.TargetType.ToString(),
                Sanitize(tool.TargetName, 200) ?? string.Empty,
                Sanitize(tool.InputSchemaJson, 4000) ?? "{}",
                tool.RequiresApproval,
                tool.RiskLevel.ToString(),
                tool.TimeoutSeconds,
                tool.AuditLevel.ToString(),
                runtimeAvailable,
                inputSchema.Value,
                outputSchema.Value,
                Sanitize(tool.Category, 120) ?? "General",
                tool.BusinessDomains
                    .Select(domain => Sanitize(domain, 120))
                    .Where(domain => !string.IsNullOrWhiteSpace(domain))
                    .Cast<string>()
                    .ToArray(),
                tool.DataBoundary.ToString(),
                tool.IsVisibleToPlanner,
                tool.IsExecutableByAgent,
                tool.SchemaVersion,
                tool.CatalogVersion,
                Sanitize(tool.ApprovalPolicy, 120) ?? "None",
                tool.ProviderType.ToString(),
                tool.ProviderType == ToolProviderType.MockMcp));
        }

        return Result.Success(new PlannerToolCatalog(
            summaries.Count == 0
                ? PlannerToolCatalog.CurrentVersion
                : Math.Max(PlannerToolCatalog.CurrentVersion, summaries.Max(tool => tool.CatalogVersion)),
            summaries.Count,
            summaries));
    }

    private static Result<PlannerToolSchemaSummary> SummarizeSchema(string? schemaJson, string label)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return Result.Success(new PlannerToolSchemaSummary("object", [], [], null, false));
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(schemaJson);
        }
        catch (JsonException)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.PlannerToolSchemaUnsupported,
                $"Tool registry {label} schema is invalid JSON."));
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.PlannerToolSchemaUnsupported,
                    $"Tool registry {label} schema must be a JSON object."));
            }

            var type = ReadSchemaType(root) ?? "object";
            if (!IsSupportedType(type))
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.PlannerToolSchemaUnsupported,
                    $"Tool registry {label} schema type '{type}' is not supported by the planner catalog."));
            }

            var required = ReadRequired(root);
            var (properties, propertiesTruncated) = ReadProperties(root, required);
            var itemsType = ReadItemsType(root);
            var isTruncated = propertiesTruncated || schemaJson.Length > 4000;

            return Result.Success(new PlannerToolSchemaSummary(
                Sanitize(type, 64) ?? "object",
                required,
                properties,
                Sanitize(itemsType, 64),
                isTruncated));
        }
    }

    private static string? ReadSchemaType(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var typeElement))
        {
            return null;
        }

        if (typeElement.ValueKind == JsonValueKind.String)
        {
            return typeElement.GetString();
        }

        if (typeElement.ValueKind == JsonValueKind.Array)
        {
            return string.Join(
                "|",
                typeElement
                    .EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        return null;
    }

    private static bool IsSupportedType(string type)
    {
        return type
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(item => item is "object" or "array" or "string" or "number" or "integer" or "boolean" or "null");
    }

    private static IReadOnlyCollection<string> ReadRequired(JsonElement schema)
    {
        if (!schema.TryGetProperty("required", out var required) ||
            required.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return required
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => Sanitize(item.GetString(), 120))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static (IReadOnlyCollection<PlannerToolPropertySummary> Properties, bool IsTruncated) ReadProperties(
        JsonElement schema,
        IReadOnlyCollection<string> required)
    {
        if (!schema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return ([], false);
        }

        var summaries = new List<PlannerToolPropertySummary>();
        var truncated = false;
        foreach (var property in properties.EnumerateObject())
        {
            if (summaries.Count >= MaxSchemaProperties)
            {
                truncated = true;
                break;
            }

            var enumValues = ReadEnum(property.Value, out var enumTruncated);
            truncated |= enumTruncated;
            summaries.Add(new PlannerToolPropertySummary(
                Sanitize(property.Name, 120) ?? property.Name,
                Sanitize(ReadSchemaType(property.Value) ?? "any", 64) ?? "any",
                enumValues,
                required.Contains(property.Name, StringComparer.OrdinalIgnoreCase)));
        }

        return (summaries, truncated);
    }

    private static IReadOnlyCollection<string> ReadEnum(JsonElement schema, out bool truncated)
    {
        truncated = false;
        if (!schema.TryGetProperty("enum", out var enumElement) ||
            enumElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var item in enumElement.EnumerateArray())
        {
            if (values.Count >= MaxEnumValues)
            {
                truncated = true;
                break;
            }

            values.Add(Sanitize(item.ToString(), MaxTextLength) ?? string.Empty);
        }

        return values;
    }

    private static string? ReadItemsType(JsonElement schema)
    {
        if (!schema.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadSchemaType(items);
    }

    private static string? Sanitize(string? value, int maxLength)
    {
        return ToolExecutionRecordSanitizer.Sanitize(value, maxLength);
    }
}
