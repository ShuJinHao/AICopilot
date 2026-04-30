using AICopilot.AiGatewayService.Safety;

namespace AICopilot.BackendTests;

[Trait("Suite", "Phase43SafetyQuality")]
public sealed class OperationalBoundaryPolicyTests
{
    private readonly IOperationalBoundaryPolicy _policy = new ManufacturingOperationalBoundaryPolicy();

    [Theory]
    [InlineData("please restart the server")]
    [InlineData("请重启服务器")]
    [InlineData("请帮我下发参数到 PLC")]
    public void TryBlockControlRequest_ShouldRejectControlMessages(string message)
    {
        var blocked = _policy.TryBlockControlRequest(message, out var decision);

        blocked.Should().BeTrue();
        decision.Should().NotBeNull();
        decision!.Code.Should().Be("control_action_blocked");
        decision.UserFacingMessage.Should().Contain("不能直接执行");
    }

    [Theory]
    [InlineData("3 号叠片机昨晚报警，帮我看原因")]
    [InlineData("请给我一个诊断清单")]
    [InlineData("谁可以注册设备")]
    public void TryBlockControlRequest_ShouldAllowReadOnlyQuestions(string message)
    {
        var blocked = _policy.TryBlockControlRequest(message, out _);

        blocked.Should().BeFalse();
    }
}
