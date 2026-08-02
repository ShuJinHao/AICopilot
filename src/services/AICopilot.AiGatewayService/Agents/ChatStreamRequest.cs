using AICopilot.AiGatewayService.Models;
using AICopilot.Services.CrossCutting.Attributes;
using MediatR;

namespace AICopilot.AiGatewayService.Agents;

[AuthorizeRequirement("AiGateway.Chat")]
public record ChatStreamRequest(
    Guid SessionId,
    string Message) : IStreamRequest<ChatChunk>;

[AuthorizeRequirement("AiGateway.Chat")]
public record ApprovalDecisionStreamRequest(
    Guid SessionId,
    string CallId,
    string Decision) : IStreamRequest<ChatChunk>;
