using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.RoutingModels;
using AICopilot.Core.AiGateway.Aggregates.Skills;
using AICopilot.Core.AiGateway.Specifications.Skills;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Skills;

public sealed record AgentSkillSelection(string? SkillCode, string? Reason);

public interface IAgentSkillAutoSelector
{
    Task<AgentSkillSelection?> SelectSkillAsync(Guid sessionId, string goal, CancellationToken cancellationToken);
}

public sealed class AgentSkillRouterAutoSelector(
    ConfiguredAgentRuntimeFactory configuredAgentFactory,
    IRoutingModelResolver routingModelResolver,
    IAgentExecutionMetadataAccessor executionMetadataAccessor,
    IReadRepository<SkillDefinition> skillRepository,
    ILogger<AgentSkillRouterAutoSelector> logger) : IAgentSkillAutoSelector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    public async Task<AgentSkillSelection?> SelectSkillAsync(
        Guid sessionId,
        string goal,
        CancellationToken cancellationToken)
    {
        var skills = await skillRepository.ListAsync(new EnabledSkillDefinitionsSpec(), cancellationToken);
        if (skills.Count == 0)
        {
            return new AgentSkillSelection(null, "当前没有已启用 Skill，无法自动生成 Agent 计划。");
        }

        try
        {
            var selection = await SelectWithModelAsync(skills, goal, cancellationToken);
            if (selection is null)
            {
                return new AgentSkillSelection(null, "Skill 自动识别没有返回可解析结果，请检查路由模型配置。");
            }

            if (string.IsNullOrWhiteSpace(selection.SkillCode))
            {
                return new AgentSkillSelection(null, selection.Reason);
            }

            var enabledSkill = skills.FirstOrDefault(skill =>
                string.Equals(skill.SkillCode, selection.SkillCode, StringComparison.OrdinalIgnoreCase));
            if (enabledSkill is null)
            {
                logger.LogWarning(
                    "Agent skill router returned unavailable skill {SkillCode} for session {SessionId}.",
                    selection.SkillCode,
                    sessionId);
                return new AgentSkillSelection(
                    null,
                    $"Skill 自动识别返回了未启用或不存在的 Skill：{selection.SkillCode}。");
            }

            logger.LogInformation(
                "Agent skill router selected skill {SkillCode} for session {SessionId}. Reason: {Reason}",
                enabledSkill.SkillCode,
                sessionId,
                selection.Reason);
            return new AgentSkillSelection(enabledSkill.SkillCode, selection.Reason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Agent skill router failed for session {SessionId}; returning no automatic skill selection.", sessionId);
            return new AgentSkillSelection(null, "Skill 自动识别失败，请检查路由模型配置后重试。");
        }
    }

    private async Task<AgentSkillSelection?> SelectWithModelAsync(
        IReadOnlyCollection<SkillDefinition> skills,
        string goal,
        CancellationToken cancellationToken)
    {
        var activeRoutingModel = await routingModelResolver.ResolveActiveModelAsync();
        var instructions = BuildInstructions(skills);
        await using var scopedAgent = activeRoutingModel is null
            ? await configuredAgentFactory.CreateAgentAsync(
                "IntentRoutingAgent",
                _ => instructions,
                ConfigureRouterOptions)
            : await configuredAgentFactory.CreateAgentAsync(
                "IntentRoutingAgent",
                activeRoutingModel,
                _ => instructions,
                ConfigureRouterOptions);

        if (activeRoutingModel is not null)
        {
            executionMetadataAccessor.SetRoutingModel(activeRoutingModel);
        }

        var session = await scopedAgent.Agent.CreateSessionAsync(cancellationToken);
        var responseText = await RunRouterAsPlainTextAsync(scopedAgent, BuildInput(goal), session, cancellationToken);
        return ParseSelection(responseText);
    }

    private static void ConfigureRouterOptions(AiChatOptions options)
    {
        options.Temperature = 0;
        options.MaxOutputTokens = 512;
        options.Tools = [];
    }

    private static string BuildInstructions(IReadOnlyCollection<SkillDefinition> skills)
    {
        var builder = new StringBuilder();
        builder.AppendLine("你是 A助理的 Agent Skill Router。");
        builder.AppendLine("你只负责为一次计划模式 Agent 任务选择一个已启用 Skill，不回答问题，不生成计划，不调用工具。");
        builder.AppendLine("输出只能是一个 JSON 对象，格式为：{\"skillCode\":\"<enabled skillCode or null>\",\"reason\":\"一句话说明选择依据\"}。");
        builder.AppendLine("如果没有任何 Skill 明确匹配，skillCode 必须为 null，并说明需要用户补充目标或手动选择 Skill。");
        builder.AppendLine("Skill 只能收窄 Planner 可见工具和 Executor 可执行工具，不能扩大 ToolRegistry、权限或 Cloud 只读边界。");
        builder.AppendLine("禁止为 Cloud 写入、设备控制、配方修改、PLC 写入、MES 上传、审批提交、删除数据等请求选择执行类 Skill。");
        builder.AppendLine();
        builder.AppendLine("可选 Skill：");
        foreach (var skill in skills.OrderBy(skill => skill.SkillCode, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"- skillCode: {skill.SkillCode}");
            builder.AppendLine($"  name: {skill.DisplayName}");
            builder.AppendLine($"  description: {skill.Description}");
            builder.AppendLine($"  dataSourceModes: {Format(skill.AllowedDataSourceModes)}");
            builder.AppendLine($"  knowledgeScopes: {Format(skill.AllowedKnowledgeScopes)}");
            builder.AppendLine($"  outputTypes: {Format(skill.OutputComponentTypes)}");
            builder.AppendLine($"  allowedTools: {Format(skill.AllowedToolCodes)}");
            builder.AppendLine($"  risk: {skill.RiskLevel}; approval: {skill.ApprovalPolicy}");
        }

        return builder.ToString();
    }

    private static string BuildInput(string goal)
    {
        return JsonSerializer.Serialize(new
        {
            goal,
            task = "select-agent-skill",
            outputContract = new
            {
                skillCode = "one enabled skillCode or null",
                reason = "short user-facing reason"
            }
        }, JsonOptions);
    }

    private static async Task<string> RunRouterAsPlainTextAsync(
        ScopedRuntimeAgent scopedAgent,
        string payload,
        IRuntimeAgentSession session,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        await foreach (var update in scopedAgent.Agent.RunStreamingAsync(
            [new AiChatMessage(AiChatRole.User, payload)],
            session,
            new RuntimeAgentRunOptions(new AiChatOptions
            {
                Temperature = 0,
                MaxOutputTokens = 512,
                Tools = []
            }),
            cancellationToken))
        {
            foreach (var content in update.Contents.OfType<AiTextContent>())
            {
                builder.Append(content.Text);
            }
        }

        return builder.ToString();
    }

    internal static AgentSkillSelection? ParseSelection(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var json = ExtractJson(text);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!document.RootElement.TryGetProperty("skillCode", out var skillElement))
        {
            return null;
        }

        var reason = document.RootElement.TryGetProperty("reason", out var reasonElement) &&
                     reasonElement.ValueKind == JsonValueKind.String
            ? reasonElement.GetString()?.Trim()
            : null;
        var skillCode = skillElement.ValueKind == JsonValueKind.String
            ? skillElement.GetString()?.Trim()
            : null;
        if (string.Equals(skillCode, "null", StringComparison.OrdinalIgnoreCase))
        {
            skillCode = null;
        }

        return new AgentSkillSelection(skillCode, reason);
    }

    private static string? ExtractJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            if (firstLineEnd >= 0)
            {
                trimmed = trimmed[(firstLineEnd + 1)..].Trim();
            }

            if (trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^3].Trim();
            }
        }

        var objectStart = trimmed.IndexOf('{');
        var objectEnd = trimmed.LastIndexOf('}');
        return objectStart >= 0 && objectEnd > objectStart
            ? trimmed[objectStart..(objectEnd + 1)]
            : null;
    }

    private static string Format(IReadOnlyCollection<string> values)
    {
        return values.Count == 0 ? "none" : string.Join(",", values);
    }
}
