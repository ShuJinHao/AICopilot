using System.Reflection;
using System.Text.Json;

namespace AICopilot.BackendTests;

public sealed class FakeAiRoutingPriorityTests
{
    [Theory]
    [InlineData("TryResolveSemanticIntent", "show recipe Recipe-Cut-01 version history detail", "Analysis.Recipe.VersionHistory")]
    [InlineData("TryResolveSemanticIntent", "show capacity for DEV-001 from 2026-04-20T00:00:00Z to 2026-04-21T23:59:59Z", "Analysis.Capacity.Range")]
    [InlineData("TryResolveSemanticIntent", "show latest production data for device DEV-001", "Analysis.ProductionData.Latest")]
    [InlineData("TryResolvePolicyIntent", "ClientCode and DeviceId are used for upload identity, right?", "Policy.BootstrapIdentity")]
    [InlineData("TryResolvePolicyIntent", "Can an operator change recipe settings without device assignment?", "Policy.EmployeeAuthorization")]
    public void RoutingStubs_ShouldHonorConfiguredPriority(string methodName, string question, string expectedIntent)
    {
        var method = typeof(FakeAiProviderHost).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        var arguments = new object?[] { question, null };
        var matched = (bool)method!.Invoke(null, arguments)!;

        matched.Should().BeTrue();
        arguments[1].Should().NotBeNull();

        var payload = JsonSerializer.Serialize(arguments[1]);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("intent").GetString().Should().Be(expectedIntent);
    }
}
