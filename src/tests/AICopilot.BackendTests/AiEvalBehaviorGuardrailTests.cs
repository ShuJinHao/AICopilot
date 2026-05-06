using System.Reflection;
using System.Text.Json;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.BackendTests;

[Trait("Suite", "AiEval")]
public sealed class AiEvalBehaviorGuardrailTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ToolCallEval_ShouldRejectCloudWriteLikeToolEvenWithReadVerbPrefix()
    {
        var decision = AiToolSafetyPolicy.Evaluate(
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.ReadOnlyQuery,
            AiToolRiskLevel.Low,
            "queryAndResetAlarm",
            "Query alarms and reset the Cloud alarm state",
            readOnlyDeclared: true);

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("forbidden write semantics");
    }

    [Fact]
    public void ToolCallEval_ShouldRejectSideEffectingCloudToolEvenWhenApprovalWouldExist()
    {
        var decision = AiToolSafetyPolicy.Evaluate(
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.SideEffecting,
            AiToolRiskLevel.RequiresApproval,
            "queryDeviceLogs",
            "Read device logs",
            readOnlyDeclared: true);

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("side-effecting");
    }

    [Fact]
    public void DataAnalysisContext_ShouldExposeBusinessSourceLabelWithoutInternalNames()
    {
        var plan = new SemanticQueryPlan(
            "Analysis.Device.Status",
            SemanticQueryTarget.Device,
            SemanticQueryKind.Status,
            "查看设备状态",
            new SemanticProjection(["deviceCode", "status"]),
            [],
            null,
            null,
            20);
        var summary = new SemanticSummaryDto(
            "Device",
            "命中 1 台设备。",
            [],
            ["设备状态为 Running。"],
            "结果上限 20 条。");

        var analysis = InvokeBuildSemanticAnalysis(
            plan,
            "Cloud 设备只读视图",
            summary,
            isTruncated: false);
        var json = JsonSerializer.Serialize(new { analysis }, JsonOptions);

        analysis.SourceLabel.Should().Be("Cloud 设备只读视图");
        json.Should().Contain("source_label");
        json.Should().NotContain("database_name");
        json.Should().NotContain("DeviceSemanticReadonly");
        json.Should().NotContain("v_device_status");
        json.ToLowerInvariant().Should().NotContain("select");
        json.ToLowerInvariant().Should().NotContain("host=");
    }

    private static AnalysisDto InvokeBuildSemanticAnalysis(
        SemanticQueryPlan plan,
        string sourceLabel,
        SemanticSummaryDto summary,
        bool isTruncated)
    {
        var method = typeof(DataAnalysisExecutor).GetMethod(
            "BuildSemanticAnalysis",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        return (AnalysisDto)method!.Invoke(null, [plan, sourceLabel, summary, isTruncated])!;
    }
}
