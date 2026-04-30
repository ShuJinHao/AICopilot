using AICopilot.AiGatewayService.BusinessPolicies;
using AICopilot.AiGatewayService.BusinessSemantics;
using AICopilot.DataAnalysisService.Semantics;

namespace AICopilot.BackendTests;

public sealed class IntentRoutingPromptComposerTests
{
    [Fact]
    public void Composer_ShouldIncludeAllConfirmedThemesAndExamples()
    {
        var catalog = CreateCatalog();
        var composer = new IntentRoutingPromptComposer(catalog);

        var content = composer.BuildBusinessPolicyIntentSection()
            + Environment.NewLine
            + composer.BuildStructuredIntentSection();

        foreach (var descriptor in catalog.GetPolicyIntents())
        {
            content.Should().Contain(descriptor.Policy.Intent);
            content.Should().Contain(descriptor.Policy.ExampleQuestions[0]);
        }

        foreach (var descriptor in catalog.GetStructuredIntents())
        {
            content.Should().Contain(descriptor.Intent.Intent);
            content.Should().Contain(descriptor.ExampleQuestions[0]);
            content.Should().Contain(descriptor.QueryJsonExample);
        }
    }

    private static BusinessSemanticsCatalog CreateCatalog()
    {
        var definitions = new SemanticDefinitionCatalog();
        var semanticIntentCatalog = new SemanticIntentCatalog(definitions);
        var businessPolicyCatalog = new BusinessPolicyCatalog();
        var summaryProfileCatalog = new SemanticSummaryProfileCatalog();

        return new BusinessSemanticsCatalog(
            businessPolicyCatalog,
            semanticIntentCatalog,
            summaryProfileCatalog);
    }
}
