using System.Security.Cryptography;
using System.Text;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.Core.Rag.Specifications.KnowledgeBase;
using AICopilot.RagService.KnowledgeBases;
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
    IReadRepository<KnowledgeSupplement> supplementRepo,
    IKnowledgeVectorSearchService vectorSearchService,
    ICurrentUser currentUser)
    : IQueryHandler<SearchKnowledgeBaseQuery, Result<List<SearchKnowledgeBaseResult>>>
{
    public async Task<Result<List<SearchKnowledgeBaseResult>>> Handle(
        SearchKnowledgeBaseQuery request,
        CancellationToken cancellationToken)
    {
        var kb = await kbRepo.FirstOrDefaultAsync(
            new KnowledgeBaseByIdWithDocumentsSpec(new KnowledgeBaseId(request.KnowledgeBaseId)),
            cancellationToken);
        if (kb == null)
        {
            return Result.NotFound("知识库不存在");
        }

        if (currentUser.Id is not { } userId ||
            !KnowledgeBaseAccessPolicy.CanRead(kb, userId, KnowledgeBaseAccessPolicy.IsAdmin(currentUser)))
        {
            return Result.NotFound();
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
        var now = DateTime.UtcNow;
        var searchableDocuments = kb.Documents
            .Where(document => document.IsSearchable(now))
            .ToDictionary(document => document.Id.Value);
        var supplements = await supplementRepo.ListAsync(cancellationToken: cancellationToken);
        var applicableSupplements = supplements
            .Where(supplement => supplement.CanApply(now))
            .ToArray();
        foreach (var record in searchResults)
        {
            if (!searchableDocuments.TryGetValue(record.DocumentId, out var document))
            {
                continue;
            }

            var isLowConfidence = record.Score < 0.65;
            var supplementHits = ResolveSupplementHits(document, applicableSupplements);
            results.Add(new SearchKnowledgeBaseResult
            {
                Text = BuildContextText(record.Text, supplementHits),
                Score = record.Score,
                DocumentId = record.DocumentId,
                DocumentName = record.DocumentName,
                ChunkIndex = record.ChunkIndex,
                IsLowConfidence = isLowConfidence,
                LowConfidenceReason = isLowConfidence
                    ? "命中分数低于 0.65，请结合更多来源或人工确认。"
                    : null,
                SupplementHits = supplementHits
            });
        }

        return Result.Success(results);
    }

    private static IReadOnlyCollection<KnowledgeSupplementHitDto> ResolveSupplementHits(
        Document document,
        IReadOnlyCollection<KnowledgeSupplement> supplements)
    {
        return supplements
            .Where(supplement =>
                supplement.DocumentId?.Value == document.Id.Value ||
                (document.CategoryId.HasValue &&
                 supplement.CategoryId?.Value == document.CategoryId.Value.Value))
            .OrderByDescending(supplement => supplement.Priority)
            .ThenByDescending(supplement => supplement.CreatedAt)
            .Take(5)
            .Select(supplement => new KnowledgeSupplementHitDto(
                supplement.Id.Value,
                supplement.Title,
                supplement.Priority.ToString(),
                supplement.Content,
                supplement.CategoryId?.Value,
                supplement.DocumentId?.Value,
                ComputeHash(supplement.Content)))
            .ToArray();
    }

    private static string BuildContextText(
        string originalText,
        IReadOnlyCollection<KnowledgeSupplementHitDto> supplementHits)
    {
        if (supplementHits.Count == 0)
        {
            return originalText;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Knowledge supplements with higher priority than matched documents:");
        foreach (var supplement in supplementHits)
        {
            builder
                .Append('[')
                .Append(supplement.Priority)
                .Append("] ")
                .Append(supplement.Title)
                .Append(" (supplementId=")
                .Append(supplement.SupplementId)
                .Append(", hash=")
                .Append(supplement.ContentHash)
                .AppendLine(")");
            builder.AppendLine(supplement.Content);
        }

        builder.AppendLine();
        builder.AppendLine("Matched document excerpt:");
        builder.Append(originalText);
        return builder.ToString();
    }

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(bytes)[..16].ToLowerInvariant()}";
    }
}
