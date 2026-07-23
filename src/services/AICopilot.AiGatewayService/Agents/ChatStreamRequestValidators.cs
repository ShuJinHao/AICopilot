using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.SharedKernel.Result;
using AICopilot.Services.CrossCutting.Behaviors;

namespace AICopilot.AiGatewayService.Agents;

public sealed class ChatStreamRequestValidator : IRequestValidator<ChatStreamRequest>
{
    public ValueTask<ApiProblemDescriptor?> ValidateAsync(
        ChatStreamRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SessionId == Guid.Empty)
        {
            return ValueTask.FromResult<ApiProblemDescriptor?>(RequestValidation.Failed("SessionId is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return ValueTask.FromResult<ApiProblemDescriptor?>(RequestValidation.Failed("Message is required."));
        }

        if (request.ReferencedAgentTaskId == Guid.Empty)
        {
            return ValueTask.FromResult<ApiProblemDescriptor?>(
                RequestValidation.Failed("ReferencedAgentTaskId must be a non-empty identifier when supplied."));
        }

        return ValueTask.FromResult<ApiProblemDescriptor?>(null);
    }
}

public sealed class ApprovalDecisionStreamRequestValidator : IRequestValidator<ApprovalDecisionStreamRequest>
{
    public ValueTask<ApiProblemDescriptor?> ValidateAsync(
        ApprovalDecisionStreamRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SessionId == Guid.Empty)
        {
            return ValueTask.FromResult<ApiProblemDescriptor?>(RequestValidation.Failed("SessionId is required."));
        }

        if (string.IsNullOrWhiteSpace(request.CallId))
        {
            return ValueTask.FromResult<ApiProblemDescriptor?>(RequestValidation.Failed("Approval call id is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Decision))
        {
            return ValueTask.FromResult<ApiProblemDescriptor?>(RequestValidation.Failed("Approval decision is required."));
        }

        return ValueTask.FromResult<ApiProblemDescriptor?>(null);
    }
}

public sealed class PlanAgentTaskCommandValidator : IRequestValidator<PlanAgentTaskCommand>
{
    public ValueTask<ApiProblemDescriptor?> ValidateAsync(
        PlanAgentTaskCommand request,
        CancellationToken cancellationToken)
    {
        if (request.SessionId == Guid.Empty)
        {
            return ValueTask.FromResult<ApiProblemDescriptor?>(RequestValidation.Failed("SessionId is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Goal))
        {
            return ValueTask.FromResult<ApiProblemDescriptor?>(RequestValidation.Failed("Agent task goal is required."));
        }

        return ValueTask.FromResult<ApiProblemDescriptor?>(null);
    }
}
