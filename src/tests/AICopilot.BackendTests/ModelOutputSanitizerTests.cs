using AICopilot.AiGatewayService.Agents;

namespace AICopilot.BackendTests;

public sealed class ModelOutputSanitizerTests
{
    [Fact]
    public void Strip_ShouldRemoveGlmThinkingTags()
    {
        var result = ModelOutputSanitizer.Strip("<mm:think>内部推理</mm:think>最终回答");

        result.CleanText.Should().Be("最终回答");
        result.ThinkingText.Should().Be("内部推理");
    }

    [Fact]
    public void Strip_ShouldRemoveDeepSeekThinkingTags()
    {
        var result = ModelOutputSanitizer.Strip("<think>reasoning\nline2</think>\nAnswer");

        result.CleanText.Should().Be("\nAnswer");
        result.ThinkingText.Should().Be("reasoning\nline2");
    }

    [Fact]
    public void Strip_ShouldRemoveResidualAndNakedThinkingLines()
    {
        var result = ModelOutputSanitizer.Strip("mm:think用户说了半句\n继续回答</mm:think>");

        result.CleanText.Should().BeEmpty();
        result.ThinkingText.Should().Contain("mm:think用户说了半句");
    }

    [Fact]
    public void Strip_ShouldLeaveNormalTextUntouched()
    {
        var result = ModelOutputSanitizer.Strip("普通文本内容");

        result.CleanText.Should().Be("普通文本内容");
        result.ThinkingText.Should().BeNull();
    }

    [Fact]
    public void StreamingFilter_ShouldRemoveThinkingSplitAcrossChunks()
    {
        var filter = new StreamingThinkTagFilter();

        filter.Append("<think>The user is asking").Should().BeEmpty();
        filter.Append(" for capabilities</think>最终回答").Should().Be("最终回答");
        filter.Flush().Should().BeEmpty();
    }

    [Fact]
    public void StreamingFilter_ShouldRemoveNakedThinkingSplitAcrossChunks()
    {
        var filter = new StreamingThinkTagFilter();

        filter.Append("mm:think用户说了半句\n继续推理").Should().BeEmpty();
        filter.Append("</mm:think>正式回答").Should().Be("正式回答");
        filter.Flush().Should().BeEmpty();
    }

    [Fact]
    public void StreamingFilter_ShouldFlushSafePendingText()
    {
        var filter = new StreamingThinkTagFilter();

        filter.Append("最终回答 <thi").Should().Be("最终回答 ");
        filter.Flush().Should().BeEmpty();
    }
}
