using AICopilot.SharedKernel.Result;

namespace AICopilot.Services.Contracts;

public interface IAgentTaskPlanPersistenceVerifier
{
    Task<Result<string>> VerifyFreshAsync(
        Guid taskId,
        string expectedCanonicalPlanJson,
        CancellationToken cancellationToken = default);
}
