using AICopilot.AiGatewayService.Workflows.Executors;

namespace AICopilot.BackendTests;

public sealed class IntentRoutingResultParserTests
{
    [Fact]
    public void TryParse_ShouldParseFencedJsonArray()
    {
        var text =
            """
            ```json
            [{"intent":"General.Chat","confidence":0.99,"reasoning":"normal chat"}]
            ```
            """;

        var parsed = IntentRoutingResultParser.TryParse(text, out var intents);

        parsed.Should().BeTrue();
        intents.Should().ContainSingle();
        intents[0].Intent.Should().Be("General.Chat");
        intents[0].Confidence.Should().BeApproximately(0.99, 0.001);
    }

    [Fact]
    public void TryParse_ShouldFailForUnparseableText()
    {
        var parsed = IntentRoutingResultParser.TryParse("I cannot produce JSON right now.", out var intents);

        parsed.Should().BeFalse();
        intents.Should().BeEmpty();
    }

    [Fact]
    public void TryParse_ShouldNormalizeSimplifiedSkillSelection()
    {
        var parsed = IntentRoutingResultParser.TryParse(
            """{"skillCode":"device_log_analysis","reason":"用户要求查看设备日志并分析根因"}""",
            out var intents);

        parsed.Should().BeTrue();
        intents.Should().ContainSingle();
        intents[0].Intent.Should().Be("Skill.device_log_analysis");
        intents[0].Reasoning.Should().Be("用户要求查看设备日志并分析根因");
    }
}
