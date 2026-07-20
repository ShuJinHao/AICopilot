using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentTaskPlanPersistencePolicy(
    IAgentPlanIntegrityValidator integrityValidator)
    : IAgentTaskPlanPersistencePolicy
{
    public AgentTaskPlanPersistenceValidationDecision Validate(
        AgentTaskPlanPersistenceValidationRequest request)
    {
        var validation = integrityValidator.ReadExecutionContract(request.PlanJson);
        if (validation.IsSuccess)
        {
            return AgentTaskPlanPersistenceValidationDecision.Valid(validation.Value!);
        }

        var problem = validation.Errors?
            .OfType<ApiProblemDescriptor>()
            .FirstOrDefault();
        return AgentTaskPlanPersistenceValidationDecision.Invalid(
            problem?.Code ?? AppProblemCodes.AgentPlanInvalid,
            problem?.Detail ?? "Persisted agent task plan failed the canonical v2 contract.");
    }
}
