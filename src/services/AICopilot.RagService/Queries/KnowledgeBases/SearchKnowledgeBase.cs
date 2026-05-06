using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.RagService.Queries.KnowledgeBases;

[AuthorizeRequirement("Rag.SearchKnowledgeBase")]
public record SearchKnowledgeBaseQuery(
    Guid KnowledgeBaseId,
    string QueryText,
    int TopK = 3,
    double MinScore = 0.5)
    : IQuery<Result<List<SearchKnowledgeBaseResult>>>;

public class SearchKnowledgeBaseQueryHandler(
    IReadRepository<KnowledgeBase> kbRepo,
    IReadRepository<EmbeddingModel> embeddingModelRepo,
    IKnowledgeVectorSearchService vectorSearchService)
    : IQueryHandler<SearchKnowledgeBaseQuery, Result<List<SearchKnowledgeBaseResult>>>
{
    public async Task<Result<List<SearchKnowledgeBaseResult>>> Handle(
        SearchKnowledgeBaseQuery request,
        CancellationToken cancellationToken)
    {
        var kb = await kbRepo.GetByIdAsync(new KnowledgeBaseId(request.KnowledgeBaseId), cancellationToken);
        if (kb == null)
        {
            return Result.NotFound("知识库不存在");
        }

        var embeddingModelConfig = await embeddingModelRepo.GetByIdAsync(kb.EmbeddingModelId, cancellationToken);
        if (embeddingModelConfig == null)
        {
            return Result.Failure("未找到关联的嵌入模型配置");
        }

        var searchResults = await vectorSearchService.SearchAsync(
            kb,
            embeddingModelConfig,
            request.QueryText,
            Math.Clamp(request.TopK, 1, 20),
            Math.Clamp(request.MinScore, 0.0, 1.0),
            cancellationToken);

        var results = new List<SearchKnowledgeBaseResult>(searchResults.Count);
        foreach (var record in searchResults)
        {
            results.Add(new SearchKnowledgeBaseResult
            {
                Text = record.Text,
                Score = record.Score,
                DocumentId = record.DocumentId,
                DocumentName = record.DocumentName,
                ChunkIndex = record.ChunkIndex
            });
        }

        return Result.Success(results);
    }
}
