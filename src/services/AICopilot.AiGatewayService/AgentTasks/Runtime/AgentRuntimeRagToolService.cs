using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentRuntimeRagToolService(
    IKnowledgeRetrievalService knowledgeRetrievalService,
    IEnumerable<IKnowledgeBaseAccessChecker> knowledgeBaseAccessCheckers,
    IIdentityAccessService identityAccessService)
{
    public async Task<object> SearchRagAsync(
        AgentTask task,
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var isTaskOwnerAdmin = await IsTaskOwnerAdminAsync(task, cancellationToken);
        foreach (var knowledgeBaseId in plan.KnowledgeBaseIds)
        {
            var accessChecker = knowledgeBaseAccessCheckers.FirstOrDefault();
            if (accessChecker is null)
            {
                throw new InvalidOperationException("RAG knowledge base access checker is not configured.");
            }

            var canRead = await accessChecker.CanReadAsync(
                knowledgeBaseId,
                task.UserId,
                isTaskOwnerAdmin,
                cancellationToken);
            if (!canRead)
            {
                throw new UnauthorizedAccessException("RAG knowledge base is not visible to the current agent task.");
            }

            var results = await knowledgeRetrievalService.SearchAsync(
                knowledgeBaseId,
                task.Goal,
                topK: 3,
                minScore: 0.5,
                cancellationToken);
            state.RagResults.AddRange(results.Select(result => new AgentRagResult(
                knowledgeBaseId,
                result.DocumentId,
                result.DocumentName,
                result.ChunkIndex,
                result.Score,
                result.IsLowConfidence,
                result.LowConfidenceReason,
                result.Text)));
        }

        return new
        {
            status = "completed",
            lowConfidence = state.RagResults.Count == 0 || state.RagResults.All(item => item.Score < 0.65),
            sources = state.RagResults
        };
    }

    private async Task<bool> IsTaskOwnerAdminAsync(AgentTask task, CancellationToken cancellationToken)
    {
        var access = await identityAccessService.GetCurrentUserAccessAsync(task.UserId, cancellationToken);
        return string.Equals(access?.RoleName, "Admin", StringComparison.OrdinalIgnoreCase);
    }
}
