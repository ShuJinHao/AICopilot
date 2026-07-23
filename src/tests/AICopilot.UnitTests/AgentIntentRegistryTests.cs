using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.BusinessPolicies;
using AICopilot.AiGatewayService.BusinessSemantics;
using AICopilot.AiGatewayService.Models;
using AICopilot.DataAnalysisService.Semantics;

namespace AICopilot.UnitTests;

public sealed class AgentIntentRegistryTests
{
    [Fact]
    public void RegistrySnapshot_ShouldOwnPromptInventoryAndFallbackSubset()
    {
        var catalog = CreateCatalog();
        var definitions = catalog.GetPolicyIntents()
            .Select(descriptor => new AgentIntentRegistryPromptDefinition(
                descriptor.Policy.Intent,
                descriptor.Policy.Description,
                descriptor.Policy.ExampleQuestions[0]))
            .Concat(catalog.GetStructuredIntents().Select(descriptor =>
                new AgentIntentRegistryPromptDefinition(
                    descriptor.Intent.Intent,
                    descriptor.Intent.Description,
                    descriptor.ExampleQuestions[0],
                    descriptor.QueryJsonExample)))
            .Append(new AgentIntentRegistryPromptDefinition("General.Chat", "General chat"))
            .ToArray();

        var snapshot = AgentIntentRegistryV1.CreateRoutingSnapshot(definitions);

        foreach (var descriptor in catalog.GetPolicyIntents())
        {
            snapshot.PromptInventory.Should().Contain(descriptor.Policy.Intent);
            snapshot.PromptInventory.Should().Contain(descriptor.Policy.ExampleQuestions[0]);
        }

        foreach (var descriptor in catalog.GetStructuredIntents())
        {
            snapshot.PromptInventory.Should().Contain(descriptor.Intent.Intent);
            snapshot.PromptInventory.Should().Contain(descriptor.ExampleQuestions[0]);
            snapshot.PromptInventory.Should().Contain(descriptor.QueryJsonExample);
        }

        AgentIntentRegistryV1.FallbackIntentCodes.Should()
            .OnlyContain(code => snapshot.Descriptors.ContainsKey(code));
        AgentIntentRegistryV1.ValidateRoutedResults(
                snapshot,
                [new IntentResult { Intent = "Unregistered.Intent", Confidence = 1 }])
            .Should().BeFalse();
    }

    private static BusinessSemanticsCatalog CreateCatalog()
    {
        var definitions = new SemanticDefinitionCatalog();
        var semanticQuerySchemaRegistry = new SemanticQuerySchemaRegistry(definitions);
        var businessPolicyCatalog = new BusinessPolicyCatalog();
        var summaryProfileCatalog = new SemanticSummaryProfileCatalog();

        return new BusinessSemanticsCatalog(
            businessPolicyCatalog,
            semanticQuerySchemaRegistry,
            summaryProfileCatalog);
    }
}
