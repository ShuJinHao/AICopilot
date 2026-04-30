using System.Text;
using AICopilot.AiGatewayService.BusinessPolicies;
using AICopilot.AiGatewayService.BusinessSemantics;
using AICopilot.AiGatewayService.Models;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public class BusinessPolicyExecutor(
    IBusinessSemanticsCatalog businessSemanticsCatalog,
    ILogger<BusinessPolicyExecutor> logger)
{
    public const string ExecutorId = nameof(BusinessPolicyExecutor);

    public Task<BranchResult> ExecuteAsync(
        List<IntentResult> intentResults,
        string? userQuestion,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var policyIntents = intentResults
            .Where(item => item.Confidence > 0.6 && businessSemanticsCatalog.TryGetPolicyIntent(item.Intent, out _))
            .GroupBy(item => item.Intent, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (policyIntents.Count == 0)
        {
            logger.LogDebug("No business policy intent detected, skipping policy branch.");
            return Task.FromResult(BranchResult.FromBusinessPolicy(string.Empty));
        }

        logger.LogInformation("Starting business policy branch. Policy intent count: {Count}", policyIntents.Count);

        var builder = new StringBuilder();

        foreach (var intent in policyIntents)
        {
            if (!businessSemanticsCatalog.TryGetPolicyIntent(intent.Intent, out var descriptor))
            {
                continue;
            }

            builder.AppendLine($"""<policy intent="{descriptor.Policy.Intent}">""");
            foreach (var section in descriptor.ResponseTemplate.Sections)
            {
                AppendSection(builder, section, descriptor.Policy, userQuestion);
            }

            builder.AppendLine("</policy>");
        }

        return Task.FromResult(BranchResult.FromBusinessPolicy(builder.ToString().Trim()));
    }

    private static void AppendSection(
        StringBuilder builder,
        BusinessPolicyResponseSection section,
        BusinessPolicyDescriptor descriptor,
        string? userQuestion)
    {
        switch (section.Key)
        {
            case "userQuestion" when !string.IsNullOrWhiteSpace(userQuestion):
                builder.AppendLine($"{section.Label}: {userQuestion}");
                break;
            case "conclusion":
                builder.AppendLine($"{section.Label}: {descriptor.Conclusion}");
                break;
            case "applicableConditions":
                builder.AppendLine($"{section.Label}:");
                foreach (var condition in descriptor.ApplicableConditions)
                {
                    builder.AppendLine($"- {condition}");
                }
                break;
            case "restrictedBoundaries":
                builder.AppendLine($"{section.Label}:");
                foreach (var boundary in descriptor.RestrictedBoundaries)
                {
                    builder.AppendLine($"- {boundary}");
                }
                break;
        }
    }
}
