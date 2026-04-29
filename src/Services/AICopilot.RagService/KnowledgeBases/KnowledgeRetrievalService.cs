using AICopilot.RagService.Queries.KnowledgeBases;
using AICopilot.Services.Contracts;
using MediatR;

namespace AICopilot.RagService.KnowledgeBases;

public sealed class KnowledgeRetrievalService(IMediator mediator) : IKnowledgeRetrievalService
{
    public async Task<IReadOnlyList<KnowledgeRetrievalResult>> SearchAsync(
        Guid knowledgeBaseId,
        string queryText,
        int topK,
        double minScore,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new SearchKnowledgeBaseQuery(knowledgeBaseId, queryText, topK, minScore),
            cancellationToken);

        if (!result.IsSuccess || result.Value == null)
        {
            return [];
        }

        return result.Value
            .Select(item => new KnowledgeRetrievalResult(
                item.Text,
                item.Score,
                item.DocumentId,
                item.DocumentName ?? string.Empty))
            .ToArray();
    }
}
