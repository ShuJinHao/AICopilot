using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Safety;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.ContractTests;

public sealed class ChatErrorContractTests
{
    [Fact]
    public void AgentWorkflowException_ShouldKeepWorkflowCodeAndUserMessage()
    {
        var chunk = AgentStreamRuntime.CreateErrorChunk(
            new AgentWorkflowException(
                AppProblemCodes.ChatConfigurationMissing,
                "Conversation template is missing.",
                "当前对话模板不存在，请联系管理员检查配置。"),
            "test",
            AppProblemCodes.ChatStreamFailed,
            "对话执行失败，请稍后重试。");

        var payload = ReadPayload(chunk.Content);

        payload.Code.Should().Be(AppProblemCodes.ChatConfigurationMissing);
        payload.Detail.Should().Be("错误码 chat_configuration_missing：对话运行配置不可用，请管理员检查模板、模型或密钥配置。");
        payload.UserFacingMessage.Should().Be("当前对话模板不存在，请联系管理员检查配置。");
    }

    [Fact]
    public void ProviderNetworkFailure_ShouldReturnProviderUnavailableWithoutInternalMessage()
    {
        var chunk = AgentStreamRuntime.CreateErrorChunk(
            new HttpRequestException("api-key or private endpoint leaked here"),
            "test",
            AppProblemCodes.ChatStreamFailed,
            "对话执行失败，请稍后重试。");

        var payload = ReadPayload(chunk.Content);

        payload.Code.Should().Be(AppProblemCodes.ModelProviderUnavailable);
        payload.UserFacingMessage.Should().Be("模型服务暂时不可用，请稍后重试或联系管理员检查模型网络。");
        chunk.Content.Should().NotContain("api-key");
        chunk.Content.Should().NotContain("private endpoint");
    }

    [Fact]
    public void WrappedProviderNetworkFailure_ShouldReturnProviderUnavailableWithoutInternalMessage()
    {
        var chunk = AgentStreamRuntime.CreateErrorChunk(
            new InvalidOperationException(
                "SDK wrapper leaked provider endpoint",
                new HttpRequestException("api-key or private endpoint leaked here")),
            "test",
            AppProblemCodes.ChatStreamFailed,
            "对话执行失败，请稍后重试。");

        var payload = ReadPayload(chunk.Content);

        payload.Code.Should().Be(AppProblemCodes.ModelProviderUnavailable);
        payload.UserFacingMessage.Should().Be("模型服务暂时不可用，请稍后重试或联系管理员检查模型网络。");
        chunk.Content.Should().NotContain("api-key");
        chunk.Content.Should().NotContain("private endpoint");
        chunk.Content.Should().NotContain("provider endpoint");
    }

    [Fact]
    public void ModelTimeout_ShouldReturnTimeoutCode()
    {
        var chunk = AgentStreamRuntime.CreateErrorChunk(
            new TimeoutException("internal timeout detail"),
            "test",
            AppProblemCodes.ChatStreamFailed,
            "对话执行失败，请稍后重试。");

        var payload = ReadPayload(chunk.Content);

        payload.Code.Should().Be(AppProblemCodes.ModelRequestTimeout);
        payload.UserFacingMessage.Should().Be("模型响应超时，请稍后重试或缩小问题范围。");
        chunk.Content.Should().NotContain("internal timeout detail");
    }

    [Fact]
    public void GenericException_ShouldUseSafeFallback()
    {
        var chunk = AgentStreamRuntime.CreateErrorChunk(
            new InvalidOperationException("database password leaked here"),
            "test",
            AppProblemCodes.ChatStreamFailed,
            "对话执行失败，请稍后重试。");

        var payload = ReadPayload(chunk.Content);

        payload.Code.Should().Be(AppProblemCodes.ChatStreamFailed);
        payload.Detail.Should().Be("对话执行失败，请稍后重试。");
        payload.UserFacingMessage.Should().Be("对话执行失败，请稍后重试。");
        chunk.Content.Should().NotContain("database password");
    }

    [Fact]
    public void WorkflowExceptionDetail_ShouldUseWhitelistedSafeDetail()
    {
        var chunk = AgentStreamRuntime.CreateErrorChunk(
            new AgentWorkflowException(
                AppProblemCodes.ChatStreamFailed,
                "Language model 'private-model' failed at https://provider.example/v1 with SELECT * FROM production.devices WHERE apiKey=secret at /Users/test/project/file.cs",
                "对话执行失败，请稍后重试。"),
            "test",
            AppProblemCodes.ChatStreamFailed,
            "对话执行失败，请稍后重试。");

        var payload = ReadPayload(chunk.Content);

        payload.Detail.Should().Be("错误码 chat_stream_failed：请求未能完成，详情已按安全策略隐藏。");
        chunk.Content.Should().NotContain("production.devices");
        chunk.Content.Should().NotContain("private-model");
        chunk.Content.Should().NotContain("provider.example");
        chunk.Content.Should().NotContain("secret");
        chunk.Content.Should().NotContain("/Users/test");

        var planCases = new[]
        {
            (
                AppProblemCodes.AgentPlanInvalid,
                "Agent task plan failed integrity validation.",
                "计划草案未通过完整性校验，请刷新后重新生成。"),
            (
                AppProblemCodes.AgentPlanSchemaInvalid,
                "Agent task plan does not match the required schema.",
                "计划草案结构无效，请刷新后重新生成。"),
            (
                AppProblemCodes.PlanPayloadTooLarge,
                "Agent task plan exceeds the maximum allowed size of 262144 UTF-8 bytes.",
                "计划草案超过 262144 UTF-8 字节上限，请缩小请求后重试。")
        };

        foreach (var (code, expectedDetail, expectedUserFacingMessage) in planCases)
        {
            const string unsafeDetail = "opaque-plan-secret-9f4c raw owner=node-controlled SELECT payroll";
            const string unsafeUserFacingMessage = "opaque-user-secret-7a31";
            var exceptionChunk = AgentStreamRuntime.CreateErrorChunk(
                new AgentTaskPlanPersistenceIntegrityException(Guid.NewGuid(), code, unsafeDetail),
                "test",
                AppProblemCodes.ChatStreamFailed,
                "对话执行失败，请稍后重试。");
            var directChunk = AgentStreamRuntime.CreateErrorChunk(
                code,
                unsafeDetail,
                "test",
                unsafeUserFacingMessage);

            foreach (var planChunk in new[] { exceptionChunk, directChunk })
            {
                var planPayload = ReadPayload(planChunk.Content);
                planPayload.Code.Should().Be(code);
                planPayload.Detail.Should().Be(expectedDetail);
                planPayload.UserFacingMessage.Should().Be(expectedUserFacingMessage);
                planChunk.Content.Should().NotContain("opaque-plan-secret-9f4c")
                    .And.NotContain("node-controlled")
                    .And.NotContain("payroll")
                    .And.NotContain("opaque-user-secret-7a31");
            }
        }
    }

    [Fact]
    public void ToolOutputSchemaFailure_ShouldUseDedicatedRecoveryMessageWithoutRawOutput()
    {
        var chunk = AgentStreamRuntime.CreateErrorChunk(
            new AgentWorkflowException(
                AppProblemCodes.ToolOutputSchemaInvalid,
                "provider raw token=secret SELECT * FROM payroll",
                "工具输出与注册契约不一致，请联系管理员。"),
            "test",
            AppProblemCodes.ChatStreamFailed,
            "对话执行失败，请稍后重试。");

        var payload = ReadPayload(chunk.Content);

        payload.Code.Should().Be(AppProblemCodes.ToolOutputSchemaInvalid);
        payload.Detail.Should().Contain("tool_output_schema_invalid")
            .And.Contain("持久化绑定校验");
        payload.UserFacingMessage.Should().Be("工具输出与注册契约不一致，请联系管理员。");
        chunk.Content.Should().NotContain("token=secret").And.NotContain("payroll");
    }

    private static ChatErrorPayload ReadPayload(string content)
    {
        return JsonSerializer.Deserialize<ChatErrorPayload>(content, new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new InvalidOperationException("Error chunk content is not a valid chat error payload.");
    }

    private sealed record ChatErrorPayload(string? Code, string? Detail, string? UserFacingMessage);
}
