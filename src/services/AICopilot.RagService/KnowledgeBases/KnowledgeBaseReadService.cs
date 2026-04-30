using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Specifications.KnowledgeBase;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.RagService.KnowledgeBases;

public sealed class KnowledgeBaseReadService(IReadRepository<KnowledgeBase> repository)
    : IKnowledgeBaseReadService
{
    public async Task<IReadOnlyList<KnowledgeBaseDescriptor>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var knowledgeBases = await repository.ListAsync(cancellationToken: cancellationToken);
        return knowledgeBases
            .OrderBy(knowledgeBase => knowledgeBase.Name)
            .Select(ToDescriptor)
            .ToArray();
    }

    public async Task<IReadOnlyList<KnowledgeBaseDescriptor>> GetByNamesAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken = default)
    {
        var knowledgeBases = await repository.ListAsync(
            new KnowledgeBasesByNamesSpec(names),
            cancellationToken);

        return knowledgeBases
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
