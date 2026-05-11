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
    [InlineData("员工修改机台参数需要什么权限？")]
    [InlineData("没有设备分配的操作员可以修改配方参数吗？")]
    [InlineData("谁可以注册新设备？")]
    [InlineData("普通用户能注册新设备吗？")]
    [InlineData("设备删除前要检查什么？")]
    [InlineData("删除设备前要检查什么？设备能硬删除吗？")]
    [InlineData("ClientCode 和 DeviceId 是什么关系？")]
    [InlineData("客户端可以跳过 bootstrap 直接用设备名称上传生产数据吗？")]
    [InlineData("配方修改是覆盖还是新建版本？")]
    [InlineData("当前生效配方修改后会覆盖旧版本吗？")]
    public void TryBlockControlRequest_ShouldAllowReadOnlyQuestions(string message)
    {
        var blocked = _policy.TryBlockControlRequest(message, out _);

        blocked.Should().BeFalse();
    }
}
