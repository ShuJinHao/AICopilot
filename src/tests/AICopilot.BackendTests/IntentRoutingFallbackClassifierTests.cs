using System.Text.Json;
using AICopilot.AiGatewayService.Workflows.Executors;

namespace AICopilot.BackendTests;

public sealed class IntentRoutingFallbackClassifierTests
{
    [Fact]
    public void TryClassify_ShouldRouteLineDeviceStatusToReadonlyAnalysis()
    {
        var classified = IntentRoutingFallbackClassifier.TryClassify(
            "列出 LINE-A 当前设备状态，生成关键指标和记录摘要，只做只读分析",
            "routing JSON parse failed",
            out var intents);

        classified.Should().BeTrue();
        intents.Should().ContainSingle();
        intents[0].Intent.Should().Be("Analysis.Device.List");
        intents[0].Confidence.Should().BeGreaterThan(0.6);
        intents[0].Query.Should().Contain("lineName");
        intents[0].Query.Should().Contain("LINE-A");

        using var document = JsonDocument.Parse(intents[0].Query!);
        document.RootElement.GetProperty("filters")[0].GetProperty("field").GetString().Should().Be("lineName");
    }

    [Fact]
    public void TryClassify_ShouldNotRouteNormalChatToAnalysis()
    {
        var classified = IntentRoutingFallbackClassifier.TryClassify(
            "介绍一下你的能力",
            "routing JSON parse failed",
            out var intents);

        classified.Should().BeFalse();
        intents.Should().BeEmpty();
    }
}
