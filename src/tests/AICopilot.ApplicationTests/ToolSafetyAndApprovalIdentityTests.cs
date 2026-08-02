using System.Text.Json;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.ApplicationTests;

public sealed class ToolSafetyAndApprovalIdentityTests
{
    [Fact]
    public void CloudReadOnlyToolSafety_ShouldRejectForbiddenWriteVerbs()
    {
        var decision = AiToolSafetyPolicy.Evaluate(
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.ReadOnlyQuery,
            AiToolRiskLevel.Low,
            "queryCreateDevicePlan",
            "Read Cloud device create plan",
            readOnlyDeclared: true);

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("forbidden write semantics");
    }

    [Fact]
    public void CloudReadOnlyToolSafety_ShouldAllowReadOnlyVerb()
    {
        var decision = AiToolSafetyPolicy.Evaluate(
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.ReadOnlyQuery,
            AiToolRiskLevel.Low,
            "queryDeviceLogs",
            "Read Cloud device logs",
            readOnlyDeclared: true);

        decision.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void CloudReadOnlyToolSafety_ShouldRequireExplicitReadOnlyDeclaration()
    {
        var missingDeclaration = AiToolSafetyPolicy.Evaluate(
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.ReadOnlyQuery,
            AiToolRiskLevel.Low,
            "queryDeviceLogs",
            "Read Cloud device logs");

        var diagnosticsCapability = AiToolSafetyPolicy.Evaluate(
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.Diagnostics,
            AiToolRiskLevel.Low,
            "queryDeviceLogs",
            "Read Cloud device logs",
            readOnlyDeclared: true);
        var localSuggestionCapability = AiToolSafetyPolicy.Evaluate(
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.LocalSuggestion,
            AiToolRiskLevel.Low,
            "queryDeviceLogs",
            "Read Cloud device logs",
            readOnlyDeclared: true);
        var unknownClassification = AiToolSafetyPolicy.Evaluate(
            AiToolExternalSystemType.Unknown,
            AiToolCapabilityKind.ReadOnlyQuery,
            AiToolRiskLevel.Low,
            "queryDeviceLogs",
            "Read-only-looking metadata must not make an unknown target executable",
            readOnlyDeclared: true);

        missingDeclaration.IsAllowed.Should().BeFalse();
        missingDeclaration.Reason.Should().Contain("read-only");
        diagnosticsCapability.IsAllowed.Should().BeFalse();
        diagnosticsCapability.Reason.Should().Contain("ReadOnlyQuery");
        localSuggestionCapability.IsAllowed.Should().BeFalse();
        localSuggestionCapability.Reason.Should().Contain("ReadOnlyQuery");
        unknownClassification.IsAllowed.Should().BeFalse();
        unknownClassification.Reason.Should().Contain("unknown external-system classification");
    }

    [Theory]
    [InlineData("getAndSetParameter")]
    [InlineData("queryAndResetAlarm")]
    [InlineData("analyzeThenApplyRecipe")]
    public void CloudReadOnlyToolSafety_ShouldRejectReadVerbWithEmbeddedWriteSemantics(string toolName)
    {
        var decision = AiToolSafetyPolicy.Evaluate(
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.ReadOnlyQuery,
            AiToolRiskLevel.Low,
            toolName,
            "Read Cloud data only",
            readOnlyDeclared: true);

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("forbidden write semantics");
    }

    [Fact]
    public void CloudReadOnlyToolSafety_ShouldRejectWriteSemanticsInSchema()
    {
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"resetMode":{"type":"string"}}}""");

        var decision = AiToolSafetyPolicy.Evaluate(
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.ReadOnlyQuery,
            AiToolRiskLevel.Low,
            "queryDeviceLogs",
            "Read Cloud device logs",
            readOnlyDeclared: true,
            inputSchema: schema.RootElement);

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("forbidden write semantics");
    }

    [Fact]
    public void CloudReadOnlyToolSafety_ShouldRejectMcpDestructiveHint()
    {
        var decision = AiToolSafetyPolicy.Evaluate(
            new AiToolSafetyDescriptor(
                ReadOnlyDeclared: true,
                McpReadOnlyHint: true,
                McpDestructiveHint: true,
                McpIdempotentHint: null,
                CapabilityKind: AiToolCapabilityKind.ReadOnlyQuery,
                ExternalSystemType: AiToolExternalSystemType.CloudReadOnly,
                RiskLevel: AiToolRiskLevel.Low,
                DeclaredEffects: [],
                BlockReasons: []),
            "queryDeviceLogs",
            "Read Cloud device logs");

        decision.IsAllowed.Should().BeFalse();
        decision.BlockReasons.Should().Contain(reason => reason.Contains("destructive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConfiguredMcpSafety_ShouldRejectMissingCanonicalToolName()
    {
        var tool = new AiToolDefinition
        {
            Name = AiToolIdentity.CreateRuntimeName(
                AiToolTargetType.McpServer,
                "runtime-mcp",
                "queryDeviceLogs"),
            Kind = AiToolCallKind.Mcp,
            TargetType = AiToolTargetType.McpServer,
            TargetName = "runtime-mcp",
            ExternalSystemType = AiToolExternalSystemType.CloudReadOnly,
            CapabilityKind = AiToolCapabilityKind.ReadOnlyQuery,
            RiskLevel = AiToolRiskLevel.Low,
            ReadOnlyDeclared = true
        };

        var decision = AiToolSafetyPolicy.EvaluateConfiguredMcp(tool);

        decision.IsAllowed.Should().BeFalse();
        decision.BlockReasons.Should().ContainSingle(reason =>
            reason.Contains("explicit canonical tool name", StringComparison.Ordinal));
    }

    [Fact]
    public void CloudReadOnlyToolSafety_ShouldRejectChineseWriteSemantics()
    {
        var decision = AiToolSafetyPolicy.Evaluate(
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.ReadOnlyQuery,
            AiToolRiskLevel.Low,
            "queryDeviceLogs",
            "查询设备日志并提交审批",
            readOnlyDeclared: true);

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("forbidden write semantics");
    }
}
