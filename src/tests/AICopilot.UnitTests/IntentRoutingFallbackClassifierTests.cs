using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Workflows.Executors;

namespace AICopilot.UnitTests;

public sealed class IntentRoutingFallbackClassifierTests
{
    [Fact]
    public void TryClassify_ShouldNotInventLineFilterForDeviceMasterData()
    {
        var classified = IntentRoutingFallbackClassifier.TryClassify(
            "列出 LINE-A 当前设备状态，生成关键指标和记录摘要，只做只读分析",
            "routing JSON parse failed",
            AgentIntentRegistryV1.FrozenSnapshot,
            out var intents);

        classified.Should().BeTrue();
        intents.Should().ContainSingle();
        intents[0].Intent.Should().Be("Analysis.Device.List");
        intents[0].Confidence.Should().BeGreaterThan(0.6);
        intents[0].Query.Should().NotContain("lineName");

        using var document = JsonDocument.Parse(intents[0].Query!);
        document.RootElement.GetProperty("filters").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void TryClassify_ShouldRouteRecentDeviceInformationToReadonlyDeviceLogAnalysis()
    {
        var classified = IntentRoutingFallbackClassifier.TryClassify(
            "替我查询下模切设备最近的一些信息并帮我整理分类成表格图表",
            "routing model call failed",
            AgentIntentRegistryV1.FrozenSnapshot,
            out var intents);

        classified.Should().BeTrue();
        intents.Should().ContainSingle();
        intents[0].Intent.Should().Be("Analysis.DeviceLog.Latest");
        intents[0].Confidence.Should().BeGreaterThan(0.6);

        using var document = JsonDocument.Parse(intents[0].Query!);
        document.RootElement.GetProperty("queryText").GetString()
            .Should().Be("替我查询下模切设备最近的一些信息并帮我整理分类成表格图表");
        document.RootElement.GetProperty("sort").GetProperty("field").GetString().Should().Be("occurredAt");
    }

    [Fact]
    public void TryClassify_ShouldNotRouteNormalChatToAnalysis()
    {
        var classified = IntentRoutingFallbackClassifier.TryClassify(
            "介绍一下你的能力",
            "routing JSON parse failed",
            AgentIntentRegistryV1.FrozenSnapshot,
            out var intents);

        classified.Should().BeFalse();
        intents.Should().BeEmpty();
    }

    [Theory]
    [InlineData("列出工序主数据", "Analysis.Process.List")]
    [InlineData("列出 stable 通道的客户端发布版本", "Analysis.ClientRelease.List")]
    public void TryClassify_ShouldRouteNewCloudOnlyAgentCapabilities(string message, string expectedIntent)
    {
        var classified = IntentRoutingFallbackClassifier.TryClassify(
            message,
            "routing JSON parse failed",
            AgentIntentRegistryV1.FrozenSnapshot,
            out var intents);

        classified.Should().BeTrue();
        intents.Should().ContainSingle().Which.Intent.Should().Be(expectedIntent);
        using var document = JsonDocument.Parse(intents[0].Query!);
        document.RootElement.GetProperty("sort").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("filters").GetArrayLength().Should().Be(0);
    }
}
