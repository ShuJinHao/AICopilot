using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.AiGatewayService.Agents;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentReasoningPolicyAuthority
{
    public const string PolicyVersion = "agent-reasoning-policy:v1";
    public const string TemplateCode = "agent_reasoning_node";
    public const string ContextPolicy = "EvidenceOnly+SafeSummary";
    public const string OutputTruthClass = "LlmInference";
    public const int MaxTurns = 1;
    public const int RecoveryTurns = 1;
    public const int DerivationDepth = 1;
    public const int MaxInputTokens = 16_000;
    public const int MaxOutputTokens = 2_048;
    public const decimal MaxCostAmount = 5m;

    private static readonly BuiltInConversationTemplateDefinition Template =
        BuiltInConversationTemplates.Find(TemplateCode)
        ?? throw new InvalidOperationException("Built-in Agent reasoning template is missing.");

    public static string TemplateVersion => $"builtin:{Template.Version}";

    public static string PromptHash => CanonicalJson.ComputeSha256(Template.SystemPrompt);

    public static void ConfigureOptions(AiChatOptions options)
    {
        options.Tools = [];
        options.Temperature = 0;
        options.MaxOutputTokens = MaxOutputTokens;
    }

    public static AgentPlanModelPolicyDocument Create(RuntimeAgentConfigurationSnapshot modelConfiguration) =>
        new(
            modelConfiguration.ModelId,
            PolicyVersion,
            TemplateCode,
            TemplateVersion,
            PromptHash,
            modelConfiguration.ModelParametersHash,
            ContextPolicy,
            MaxTurns,
            RecoveryTurns,
            DerivationDepth,
            AllowedToolCodes: [],
            OutputTruthClass);

    public static bool Matches(
        AgentPlanModelPolicyDocument? policy,
        RuntimeAgentConfigurationSnapshot? runtimeConfiguration = null)
    {
        return policy is not null &&
               policy.ModelId is { } modelId && modelId != Guid.Empty &&
               string.Equals(policy.PolicyVersion, PolicyVersion, StringComparison.Ordinal) &&
               string.Equals(policy.TemplateCode, TemplateCode, StringComparison.Ordinal) &&
               string.Equals(policy.TemplateVersion, TemplateVersion, StringComparison.Ordinal) &&
               string.Equals(policy.PromptHash, PromptHash, StringComparison.Ordinal) &&
               IsSha256(policy.PromptHash) &&
               IsSha256(policy.ModelParametersHash) &&
               string.Equals(policy.ContextPolicy, ContextPolicy, StringComparison.Ordinal) &&
               policy.MaxTurns == MaxTurns &&
               policy.RecoveryTurns == RecoveryTurns &&
               policy.DerivationDepth == DerivationDepth &&
               policy.AllowedToolCodes is { Count: 0 } &&
               string.Equals(policy.OutputTruthClass, OutputTruthClass, StringComparison.Ordinal) &&
               (runtimeConfiguration is null ||
                runtimeConfiguration.ModelId == modelId &&
                string.Equals(runtimeConfiguration.TemplateCode, TemplateCode, StringComparison.Ordinal) &&
                string.Equals(runtimeConfiguration.TemplateVersion, TemplateVersion, StringComparison.Ordinal) &&
                string.Equals(runtimeConfiguration.PromptHash, PromptHash, StringComparison.Ordinal) &&
                string.Equals(
                    runtimeConfiguration.ModelParametersHash,
                    policy.ModelParametersHash,
                    StringComparison.Ordinal));
    }

    public static async Task<Result> VerifyCurrentConfigurationAsync(
        AgentTaskPlanDocument plan,
        ConfiguredAgentRuntimeFactory? agentFactory,
        CancellationToken cancellationToken)
    {
        var policies = (plan.Nodes ?? [])
            .Where(node => node.NodeKind == "AgentReasoningNode")
            .Select(node => node.ModelPolicy)
            .ToArray();
        if (policies.Length == 0)
        {
            return Result.Success();
        }

        if (agentFactory is null ||
            policies.Length != 1 ||
            policies[0]?.ModelId is not { } modelId ||
            modelId == Guid.Empty)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.ApprovalReconfirmationRequired,
                "Agent reasoning prompt/model verification is unavailable; generate and confirm a new PlanDraft."));
        }

        try
        {
            var current = await agentFactory.ReadConfigurationSnapshotAsync(
                TemplateCode,
                new LanguageModelId(modelId),
                ConfigureOptions,
                cancellationToken);
            return Matches(policies[0], current)
                ? Result.Success()
                : Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.ApprovalReconfirmationRequired,
                    "Agent reasoning prompt/model configuration changed after PlanDraft sealing; generate and confirm a new PlanDraft."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.ApprovalReconfirmationRequired,
                "Agent reasoning prompt/model configuration is unavailable; generate and confirm a new PlanDraft."));
        }
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
