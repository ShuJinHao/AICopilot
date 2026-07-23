using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows.Executors;

namespace AICopilot.UnitTests;

public sealed class IntentRoutingResultParserTests
{
    [Fact]
    public void TryParse_ShouldParseFencedJsonArray()
    {
        var text =
            """
            ```json
            [{"intent":"General.Chat","confidence":0.99,"query":null}]
            ```
            """;

        var parsed = IntentRoutingResultParser.TryParse(text, out var intents);

        parsed.Should().BeTrue();
        VerifySingleIntent(intents, "General.Chat", 0.99);
    }

    [Fact]
    public void TryParse_ShouldFailForUnparseableText()
    {
        var parsed = IntentRoutingResultParser.TryParse("I cannot produce JSON right now.", out var intents);

        parsed.Should().BeFalse();
        intents.Should().BeEmpty();
    }

    [Fact]
    public void TryParse_ShouldIgnoreThinkingBeforeJson()
    {
        var parsed = IntentRoutingResultParser.TryParse(
            """
            <think>[{"intent":"General.Chat","confidence":1,"reasoning":"wrong bracket in thinking"}]</think>
            [{"intent":"Analysis.Device.List","confidence":0.92,"query":"{\"queryText\":\"列出设备主数据\",\"filters\":[]}"}]
            """,
            out var intents);

        parsed.Should().BeTrue();
        VerifySingleIntent(intents, "Analysis.Device.List", 0.92);
    }

    [Fact]
    public void TryParse_ShouldRejectReasoningOrUnknownFields()
    {
        var parsed = IntentRoutingResultParser.TryParse(
            """[{"intent":"General.Chat","confidence":1,"reasoning":"must-not-cross-boundary"}]""",
            out var intents);

        parsed.Should().BeFalse();
        intents.Should().BeEmpty();
    }

    private static void VerifySingleIntent(
        IReadOnlyList<IntentResult> intents,
        string expectedIntent,
        double expectedConfidence)
    {
        intents.Should().ContainSingle();
        intents[0].Intent.Should().Be(expectedIntent);
        intents[0].Confidence.Should().BeApproximately(expectedConfidence, 0.001);
    }
}
