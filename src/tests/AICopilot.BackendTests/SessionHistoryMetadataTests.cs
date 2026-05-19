using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;

namespace AICopilot.BackendTests;

[Trait("Suite", "AiGateway")]
public sealed class SessionHistoryMetadataTests
{
    [Fact]
    public void AddFirstUserMessage_ShouldGenerateReadableTitleAndSummary()
    {
        var session = new Session(Guid.NewGuid(), ConversationTemplateId.New());

        session.AddMessage("  帮我分析最近一周产线异常，并生成一份报告  ", MessageType.User);

        session.Title.Should().Be("帮我分析最近一周产线异常，并生成一份报告");
        session.LastMessageSummary.Should().Be("帮我分析最近一周产线异常，并生成一份报告");
        session.LastMessageAt.Should().NotBeNull();
        session.MessageCount.Should().Be(1);
    }

    [Fact]
    public void Rename_ShouldPreventBlankTitle()
    {
        var session = new Session(Guid.NewGuid(), ConversationTemplateId.New());

        session.Rename("   ");

        session.Title.Should().Be(Session.UntitledTitle);
    }

    [Fact]
    public void AddLaterUserMessage_ShouldNotOverwriteManualTitle()
    {
        var session = new Session(Guid.NewGuid(), ConversationTemplateId.New());

        session.Rename("人工命名");
        session.AddMessage("新的用户消息", MessageType.User);

        session.Title.Should().Be("人工命名");
        session.MessageCount.Should().Be(1);
    }
}
