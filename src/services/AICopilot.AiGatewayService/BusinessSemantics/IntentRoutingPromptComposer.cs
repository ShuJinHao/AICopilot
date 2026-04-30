using System.Text;

namespace AICopilot.AiGatewayService.BusinessSemantics;

public sealed class IntentRoutingPromptComposer(
    IBusinessSemanticsCatalog businessSemanticsCatalog)
{
    public string BuildBusinessPolicyIntentSection()
    {
        var builder = new StringBuilder();
        foreach (var descriptor in businessSemanticsCatalog.GetPolicyIntents())
        {
            builder.AppendLine($"- {descriptor.Policy.Intent}: {descriptor.Policy.Description}");
            if (descriptor.Policy.ExampleQuestions.Count != 0)
            {
                builder.AppendLine($"  Query example: {descriptor.Policy.ExampleQuestions[0]}");
            }
        }

        AppendGuidance(builder, "Policy routing rules", businessSemanticsCatalog.PolicyRoutingGuidance);
        return builder.ToString();
    }

    public string BuildStructuredIntentSection()
    {
        var builder = new StringBuilder();
        foreach (var descriptor in businessSemanticsCatalog.GetStructuredIntents())
        {
            builder.AppendLine($"- {descriptor.Intent.Intent}: {descriptor.Intent.Description}");
            if (descriptor.ExampleQuestions.Count != 0)
            {
                builder.AppendLine($"  Query example: {descriptor.ExampleQuestions[0]}");
            }

            builder.AppendLine($"  Query JSON example: {descriptor.QueryJsonExample}");
        }

        AppendGuidance(builder, "Semantic routing rules", businessSemanticsCatalog.StructuredRoutingGuidance);
        return builder.ToString();
    }

    private static void AppendGuidance(
        StringBuilder builder,
        string title,
        RoutingGuidance guidance)
    {
        builder.AppendLine($"  {title}:");
        foreach (var rule in guidance.Rules)
        {
            builder.AppendLine($"  - {rule}");
        }

        foreach (var priorityRule in guidance.PriorityRules)
        {
            builder.AppendLine($"  - Priority: {priorityRule}");
        }

        foreach (var note in guidance.Notes)
        {
            builder.AppendLine($"  - {note}");
        }
    }
}
