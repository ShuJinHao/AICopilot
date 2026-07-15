using AICopilot.AiGatewayService.Safety;

namespace AICopilot.ContractTests;

public sealed class CloudReadonlyChatBoundaryTests
{
    [Theory]
    [InlineData("帮我修改配方 Recipe-A 并让它立即生效")]
    [InlineData("禁用设备 DEV-001")]
    [InlineData("补录昨天 DEV-001 的产能")]
    [InlineData("删除这条设备日志")]
    [InlineData("上传这批生产数据到 Cloud")]
    public void OperationalBoundaryPolicy_ShouldRejectCloudBusinessWriteRequests(string message)
    {
        var policy = new ManufacturingOperationalBoundaryPolicy();

        var blocked = policy.TryBlockControlRequest(message, out var decision);

        blocked.Should().BeTrue();
        decision.Should().NotBeNull();
        decision!.UserFacingMessage.Should().Contain("Cloud 业务");
        decision.UserFacingMessage.Should().Contain("人工执行");
    }

    [Theory]
    [InlineData("帮我分析 DEV-001 最近 24 小时日志")]
    [InlineData("查看 LINE-A 当前设备状态")]
    [InlineData("解释这个配方版本历史，只读分析")]
    public void OperationalBoundaryPolicy_ShouldAllowReadonlyAnalysisRequests(string message)
    {
        var policy = new ManufacturingOperationalBoundaryPolicy();

        var blocked = policy.TryBlockControlRequest(message, out _);

        blocked.Should().BeFalse();
    }
}
