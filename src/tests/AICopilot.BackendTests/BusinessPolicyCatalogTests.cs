using AICopilot.AiGatewayService.BusinessPolicies;

namespace AICopilot.BackendTests;

public sealed class BusinessPolicyCatalogTests
{
    [Fact]
    public void Catalog_ShouldExposeAllRequiredPolicyIntents()
    {
        var catalog = new BusinessPolicyCatalog();

        var descriptors = catalog.GetAll();

        descriptors.Select(item => item.Intent).Should().BeEquivalentTo(
        [
            "Policy.EmployeeAuthorization",
            "Policy.DeviceRegistration",
            "Policy.DeviceLifecycle",
            "Policy.BootstrapIdentity",
            "Policy.RecipeVersioning"
        ]);
    }

    [Theory]
    [InlineData("Policy.EmployeeAuthorization", "设备分配")]
    [InlineData("Policy.DeviceRegistration", "管理员")]
    [InlineData("Policy.DeviceLifecycle", "历史依赖")]
    [InlineData("Policy.BootstrapIdentity", "DeviceId")]
    [InlineData("Policy.RecipeVersioning", "V1.0")]
    public void Catalog_ShouldProvideConcreteBusinessBoundaries(string intent, string expectedFragment)
    {
        var catalog = new BusinessPolicyCatalog();

        var found = catalog.TryGet(intent, out var descriptor);

        found.Should().BeTrue();
        descriptor.RestrictedBoundaries.Should().NotBeEmpty();
        string.Join(Environment.NewLine, descriptor.ApplicableConditions.Concat(descriptor.RestrictedBoundaries))
            .Should().Contain(expectedFragment);
    }
}
