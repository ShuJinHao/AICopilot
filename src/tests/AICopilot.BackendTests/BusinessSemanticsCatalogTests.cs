using AICopilot.AiGatewayService.BusinessPolicies;
using AICopilot.AiGatewayService.BusinessSemantics;
using AICopilot.DataAnalysisService.Semantics;

namespace AICopilot.BackendTests;

public sealed class BusinessSemanticsCatalogTests
{
    [Fact]
    public void Catalog_ShouldRegisterAllPolicyDescriptorsWithResponseTemplate()
    {
        var catalog = CreateCatalog();
        var expectedSectionKeys = new[]
        {
            "userQuestion",
            "conclusion",
            "applicableConditions",
            "restrictedBoundaries"
        };

        var descriptors = catalog.GetPolicyIntents();

        descriptors.Select(item => item.Policy.Intent).Should().BeEquivalentTo(
        [
            "Policy.EmployeeAuthorization",
            "Policy.DeviceRegistration",
            "Policy.DeviceLifecycle",
            "Policy.BootstrapIdentity",
            "Policy.RecipeVersioning"
        ]);

        foreach (var descriptor in descriptors)
        {
            descriptor.ResponseTemplate.Sections.Select(section => section.Key).Should().Equal(expectedSectionKeys);
        }
    }

    [Fact]
    public void Catalog_ShouldRegisterAllSummaryProfiles()
    {
        var catalog = CreateCatalog();

        var profiles = catalog.GetSummaryProfiles();

        profiles.Select(item => item.Target).Should().BeEquivalentTo(
        [
            SemanticQueryTarget.Device,
            SemanticQueryTarget.DeviceLog,
            SemanticQueryTarget.Recipe,
            SemanticQueryTarget.Capacity,
            SemanticQueryTarget.ProductionData
        ]);
        profiles.Should().OnlyContain(item => item.ExampleQuestions.Count > 0);
        profiles.Should().OnlyContain(item =>
            item.ResponseContract.ConclusionSection == "结论" &&
            item.ResponseContract.MetricSection == "关键指标" &&
            item.ResponseContract.HighlightSection == "关键记录" &&
            item.ResponseContract.ScopeSection == "查询范围");
    }

    [Fact]
    public void Catalog_ShouldRegisterStructuredSemanticDescriptorsForAllConfirmedIntents()
    {
        var catalog = CreateCatalog();
        var definitions = new SemanticDefinitionCatalog();
        var semanticIntentCatalog = new SemanticIntentCatalog(definitions);

        var descriptors = catalog.GetStructuredIntents();

        descriptors.Select(item => item.Intent.Intent).Should().BeEquivalentTo(
            semanticIntentCatalog.GetAll().Select(item => item.Intent));
        descriptors.Should().OnlyContain(item => item.ExampleQuestions.Count > 0);
        descriptors.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.QueryJsonExample));
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
