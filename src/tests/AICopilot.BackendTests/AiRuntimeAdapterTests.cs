using System.ComponentModel;
using AICopilot.AgentPlugin;
using AICopilot.AiRuntime;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.AI;

namespace AICopilot.BackendTests;

[Trait("Suite", "Architecture")]
public sealed class AiRuntimeAdapterTests
{
    [Fact]
    public async Task RuntimeToolAdapter_ShouldAdaptPluginMethodTool_AndInvokeThroughAiFunction()
    {
        var plugin = new EchoPlugin();
        var tool = plugin.GetTools()!.Single();

        var chatOptions = RuntimeToolAdapter.ToChatOptions(new AiChatOptions { Tools = [tool] });
        var function = chatOptions.Tools!.OfType<AIFunction>().Single();

        var result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["value"] = "line-1" }),
            CancellationToken.None);

        function.Name.Should().Be(nameof(EchoPlugin.Echo));
        result?.ToString().Should().Be("echo:line-1");
    }

    [Fact]
    public void RuntimeToolAdapter_ShouldWrapApprovalRequiredTools()
    {
        var plugin = new EchoPlugin();
        var tool = plugin.GetTools()!.Single().WithRequiresApproval(true);

        var chatOptions = RuntimeToolAdapter.ToChatOptions(new AiChatOptions { Tools = [tool] });
        var adaptedTool = chatOptions.Tools!.Single();

        adaptedTool.Name.Should().Be(nameof(EchoPlugin.Echo));
        adaptedTool.GetType().Name.Should().Contain("ApprovalRequired");
    }

    [Fact]
    public void RuntimeContentMapper_ShouldMapSdkUpdatesToOwnRuntimeContents()
    {
        var call = new FunctionCallContent(
            "call-1",
            "Echo",
            new Dictionary<string, object?> { ["value"] = "line-1" });
        var approval = new ToolApprovalRequestContent("approval-1", call);
        var usage = new UsageContent(new UsageDetails
        {
            InputTokenCount = 3,
            OutputTokenCount = 5,
            TotalTokenCount = 8
        });

        var contents = RuntimeContentMapper.ToRuntimeContents(
        [
            new TextContent("hello"),
            call,
            new FunctionResultContent("call-1", "ok"),
            approval,
            usage
        ]);

        contents.OfType<AiTextContent>().Single().Text.Should().Be("hello");
        contents.OfType<AiToolCallContent>().Single().ToolCall.Name.Should().Be("Echo");
        contents.OfType<AiFunctionResultContent>().Single().Result.Should().Be("ok");
        contents.OfType<AiToolApprovalRequestContent>().Single().Request.RequestId.Should().Be("approval-1");
        contents.OfType<AiUsageContent>().Single().Details.TotalTokenCount.Should().Be(8);
    }

    private sealed class EchoPlugin : AgentPluginBase
    {
        [Description("Echo a value.")]
        public string Echo(string value)
        {
            return $"echo:{value}";
        }
    }
}
