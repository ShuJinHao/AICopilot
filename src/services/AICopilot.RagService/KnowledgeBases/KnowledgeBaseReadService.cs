using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Specifications.KnowledgeBase;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.RagService.KnowledgeBases;

public sealed class KnowledgeBaseReadService(
    IReadRepository<KnowledgeBase> repository,
    ICurrentUser currentUser)
    : IKnowledgeBaseReadService
{
    public async Task<IReadOnlyList<KnowledgeBaseDescriptor>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId)
        {
            return [];
        }

        var isAdmin = KnowledgeBaseAccessPolicy.IsAdmin(currentUser);
        var knowledgeBases = await repository.ListAsync(cancellationToken: cancellationToken);
        return knowledgeBases
            .Where(knowledgeBase => KnowledgeBaseAccessPolicy.CanRead(knowledgeBase, userId, isAdmin))
            .OrderBy(knowledgeBase => knowledgeBase.Name)
            .Select(ToDescriptor)
            .ToArray();
    }

    public async Task<IReadOnlyList<KnowledgeBaseDescriptor>> GetByNamesAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId)
        {
            return [];
        }

        var isAdmin = KnowledgeBaseAccessPolicy.IsAdmin(currentUser);
        var knowledgeBases = await repository.ListAsync(
            new KnowledgeBasesByNamesSpec(names),
            cancellationToken);

        return knowledgeBases
            .Where(knowledgeBase => KnowledgeBaseAccessPolicy.CanRead(knowledgeBase, userId, isAdmin))
            .Select(ToDescriptor)
            .ToArray();
    }

    private static KnowledgeBaseDescriptor ToDescriptor(KnowledgeBase knowledgeBase)
    {
        return new KnowledgeBaseDescriptor(
            knowledgeBase.Id,
            knowledgeBase.Name,
            knowledgeBase.Description);
    }
}
