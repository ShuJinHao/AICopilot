namespace AICopilot.AiGatewayService.Safety;

public sealed class ChatWorkflowException(string code, string detail, string userFacingMessage) : Exception(detail)
{
    public string Code { get; } = code;

    public string Detail { get; } = detail;

    public string UserFacingMessage { get; } = userFacingMessage;
}
