using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.Sessions;

namespace AICopilot.ContractTests;

public sealed class FrontendContractSnapshotTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ChatStreamRequest_ShouldExposeOnlySessionAndMessage()
    {
        typeof(ChatStreamRequest).GetConstructors().Should().ContainSingle()
            .Which.GetParameters().Select(parameter => parameter.Name)
            .Should().Equal("SessionId", "Message");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            new ChatStreamRequest(Guid.NewGuid(), "hello"),
            JsonOptions));
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal("sessionId", "message");
    }

    [Fact]
    public void ApprovalDecisionRequest_ShouldExposeOnlyProtectedBindingKeyAndDecision()
    {
        typeof(ApprovalDecisionStreamRequest).GetConstructors().Should().ContainSingle()
            .Which.GetParameters().Select(parameter => parameter.Name)
            .Should().Equal("SessionId", "CallId", "Decision");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            new ApprovalDecisionStreamRequest(Guid.NewGuid(), "call-1", "approve"),
            JsonOptions));
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal("sessionId", "callId", "decision");
    }

    [Fact]
    public void SessionAndModelProvenanceContracts_ShouldExcludeRetiredClientState()
    {
        var sessionProperties = typeof(SessionDto).GetProperties()
            .Select(property => property.Name)
            .ToArray();
        sessionProperties.Should().NotContain(name =>
            name.Contains("Onsite", StringComparison.OrdinalIgnoreCase));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            new MessageModelSnapshot(Guid.NewGuid(), "answer-model", 32_768, 1_024),
            JsonOptions));
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal("finalModelId", "finalModelName", "contextWindowTokens", "maxOutputTokens");
    }

    [Fact]
    public void ToolRegistrationContract_ShouldRetainCurrentGovernanceWithoutLegacyPlannerPolicy()
    {
        var properties = typeof(ToolRegistrationDto).GetProperties()
            .Select(property => property.Name)
            .ToArray();

        properties.Should().Contain([
            "RequiresApproval",
            "RiskLevel",
            "RequiredPermission",
            "AuditLevel",
            "DataBoundary",
            "IsExecutableByAgent",
            "SchemaVersion",
            "CatalogVersion"
        ]);
        properties.Should().NotContain([
            "IsVisibleToPlanner",
            "ApprovalPolicy"
        ]);
    }
}
