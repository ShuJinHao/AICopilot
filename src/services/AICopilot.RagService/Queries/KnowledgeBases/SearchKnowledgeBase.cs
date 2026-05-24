using System.Globalization;
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
    IReadRepository<KnowledgeCategory> categoryRepo,
    IKnowledgeVectorSearchService vectorSearchService,
    ICurrentUser currentUser,
    IAuditLogWriter auditLogWriter)
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

        var topK = Math.Clamp(request.TopK, 1, 20);
        var minScore = Math.Clamp(request.MinScore, 0.0, 1.0);
        var searchResults = await vectorSearchService.SearchAsync(
            kb,
            embeddingModelConfig,
            request.QueryText,
            topK,
            minScore,
            cancellationToken);

        var results = new List<SearchKnowledgeBaseResult>(searchResults.Count);
        var now = DateTime.UtcNow;
        var isAdmin = KnowledgeBaseAccessPolicy.IsAdmin(currentUser);
        var categories = await categoryRepo.ListAsync(cancellationToken: cancellationToken);
        var categoryAccess = KnowledgeCategoryAccessContext.Create(categories, currentUser, isAdmin);
        var candidateDocuments = kb.Documents
            .Where(document => document.CanEnterFinalPrompt(now))
            .Where(categoryAccess.CanReadDocument)
            .ToArray();
        var candidateDocumentsById = candidateDocuments.ToDictionary(document => document.Id.Value);
        var searchableDocuments = SelectLatestEffectiveDocuments(candidateDocuments)
            .ToDictionary(document => document.Id.Value);
        var excludedDocumentCount = Math.Max(0, kb.Documents.Count - searchableDocuments.Count);
        var supplements = await supplementRepo.ListAsync(cancellationToken: cancellationToken);
        var applicableSupplements = supplements
            .Where(supplement => supplement.CanApply(now))
            .Where(categoryAccess.CanReadSupplement)
            .ToArray();
        var filteredVectorHitCount = 0;
        var auditWarningCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in searchResults)
        {
            if (!searchableDocuments.TryGetValue(record.DocumentId, out var document))
            {
                filteredVectorHitCount++;
                auditWarningCodes.Add(candidateDocumentsById.ContainsKey(record.DocumentId)
                    ? RagGovernanceWarningCodes.OutdatedDocumentSkipped
                    : RagGovernanceWarningCodes.GovernanceFilteredDocumentSkipped);
                continue;
            }

            var isLowConfidence = record.Score < 0.65;
            var supplementHits = ResolveSupplementHits(document, applicableSupplements);
            var warningCodes = ResolveWarningCodes(isLowConfidence, supplementHits);
            foreach (var warningCode in warningCodes)
            {
                auditWarningCodes.Add(warningCode);
            }

            results.Add(new SearchKnowledgeBaseResult
            {
                Text = BuildContextText(record.Text, supplementHits),
                Score = record.Score,
                DocumentId = document.Id.Value,
                DocumentName = document.Name,
                ChunkIndex = record.ChunkIndex,
                IsLowConfidence = isLowConfidence,
                LowConfidenceReason = isLowConfidence
                    ? "命中分数低于 0.65，请结合更多来源或人工确认。"
                    : null,
                SupplementHits = supplementHits,
                GovernanceEvidence = new KnowledgeRetrievalGovernanceEvidenceDto(
                    [BuildCitation(document, record.ChunkIndex)],
                    warningCodes,
                    supplementHits.Any(IsGovernanceOverride),
                    0)
            });
        }

        for (var i = 0; i < results.Count; i++)
        {
            var evidence = results[i].GovernanceEvidence;
            if (evidence is not null)
            {
                results[i] = results[i] with
                {
                    GovernanceEvidence = evidence with { FilteredVectorHitCount = filteredVectorHitCount }
                };
            }
        }

        await WriteRecallAuditAsync(
            request,
            kb,
            topK,
            minScore,
            results,
            filteredVectorHitCount,
            excludedDocumentCount,
            auditWarningCodes,
            cancellationToken);

        return Result.Success(results);
    }

    private static IReadOnlyCollection<Document> SelectLatestEffectiveDocuments(
        IReadOnlyCollection<Document> documents)
    {
        return documents
            .GroupBy(document => document.DocumentGroupId)
            .Select(group => group
                .OrderByDescending(document => document.VersionNo)
                .ThenByDescending(document => document.EffectiveAt ?? document.CreatedAt)
                .First())
            .ToArray();
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
                ComputeHash(supplement.Content),
                document.DocumentGroupId,
                document.VersionNo,
                IsGovernanceOverride(supplement.Priority)
                    ? RagGovernanceWarningCodes.SupplementOverrideApplied
                    : null))
            .ToArray();
    }

    private static bool IsGovernanceOverride(KnowledgeSupplementHitDto supplement)
    {
        return string.Equals(supplement.Priority, KnowledgeSupplementPriority.CriticalOverride.ToString(), StringComparison.Ordinal)
               || string.Equals(supplement.Priority, KnowledgeSupplementPriority.High.ToString(), StringComparison.Ordinal);
    }

    private static bool IsGovernanceOverride(KnowledgeSupplementPriority priority)
    {
        return priority is KnowledgeSupplementPriority.CriticalOverride or KnowledgeSupplementPriority.High;
    }

    private static IReadOnlyCollection<string> ResolveWarningCodes(
        bool isLowConfidence,
        IReadOnlyCollection<KnowledgeSupplementHitDto> supplementHits)
    {
        var warnings = new List<string>();
        if (isLowConfidence)
        {
            warnings.Add(RagGovernanceWarningCodes.LowConfidence);
        }

        if (supplementHits.Any(IsGovernanceOverride))
        {
            warnings.Add(RagGovernanceWarningCodes.SupplementOverrideApplied);
        }

        return warnings;
    }

    private static KnowledgeDocumentCitationDto BuildCitation(Document document, int chunkIndex)
    {
        return new KnowledgeDocumentCitationDto(
            document.Id.Value,
            document.Name,
            chunkIndex,
            document.DocumentGroupId,
            document.VersionNo,
            document.Classification.ToString(),
            document.SourceType.ToString(),
            document.CategoryId?.Value,
            ComputeHash($"{document.Id.Value}|{document.DocumentGroupId}|{document.VersionNo}|{chunkIndex}|{document.FileHash}"));
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

    private async Task WriteRecallAuditAsync(
        SearchKnowledgeBaseQuery request,
        KnowledgeBase knowledgeBase,
        int topK,
        double minScore,
        IReadOnlyCollection<SearchKnowledgeBaseResult> results,
        int filteredVectorHitCount,
        int excludedDocumentCount,
        IReadOnlyCollection<string> warningCodes,
        CancellationToken cancellationToken)
    {
        var hitDocumentIds = string.Join(",", results.Select(result => result.DocumentId).Distinct().OrderBy(value => value));
        var supplementHashes = string.Join(
            ",",
            results
                .SelectMany(result => result.SupplementHits)
                .Select(hit => hit.ContentHash)
                .Where(hash => !string.IsNullOrWhiteSpace(hash))
                .Distinct()
                .OrderBy(hash => hash, StringComparer.Ordinal));
        var normalizedWarnings = string.Join(",", warningCodes.Order(StringComparer.Ordinal));
        var queryHash = ComputeHash(request.QueryText);
        var metadata = new Dictionary<string, string>
        {
            ["queryHash"] = queryHash,
            ["knowledgeBaseId"] = knowledgeBase.Id.Value.ToString(),
            ["topK"] = topK.ToString(CultureInfo.InvariantCulture),
            ["minScore"] = minScore.ToString("0.###", CultureInfo.InvariantCulture),
            ["hitDocumentIds"] = hitDocumentIds,
            ["supplementHashes"] = supplementHashes,
            ["warningCodes"] = normalizedWarnings,
            ["filteredVectorHitCount"] = filteredVectorHitCount.ToString(CultureInfo.InvariantCulture),
            ["excludedDocumentCount"] = excludedDocumentCount.ToString(CultureInfo.InvariantCulture),
            ["resultCount"] = results.Count.ToString(CultureInfo.InvariantCulture)
        };

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Rag,
                "Rag.SearchKnowledgeBaseRecall",
                "KnowledgeBase",
                knowledgeBase.Id.Value.ToString(),
                knowledgeBase.Name,
                AuditResults.Succeeded,
                $"RAG recall completed. queryHash={queryHash}; hits={results.Count}; filtered={filteredVectorHitCount}; warnings={normalizedWarnings}.",
                Array.Empty<string>(),
                metadata),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
    }

    private static class RagGovernanceWarningCodes
    {
        public const string GovernanceFilteredDocumentSkipped = "GOVERNANCE_FILTERED_DOCUMENT_SKIPPED";
        public const string LowConfidence = "LOW_CONFIDENCE";
        public const string OutdatedDocumentSkipped = "OUTDATED_DOCUMENT_SKIPPED";
        public const string SupplementOverrideApplied = "SUPPLEMENT_OVERRIDE_APPLIED";
    }

    private sealed class KnowledgeCategoryAccessContext(
        IReadOnlyDictionary<Guid, KnowledgeCategory> categories,
        ICurrentUser currentUser,
        bool isAdmin)
    {
        public static KnowledgeCategoryAccessContext Create(
            IReadOnlyCollection<KnowledgeCategory> categories,
            ICurrentUser currentUser,
            bool isAdmin)
        {
            return new KnowledgeCategoryAccessContext(
                categories.ToDictionary(category => category.Id.Value),
                currentUser,
                isAdmin);
        }

        public bool CanReadDocument(Document document)
        {
            return !document.CategoryId.HasValue || CanReadCategory(document.CategoryId.Value.Value);
        }

        public bool CanReadSupplement(KnowledgeSupplement supplement)
        {
            return !supplement.CategoryId.HasValue || CanReadCategory(supplement.CategoryId.Value.Value);
        }

        private bool CanReadCategory(Guid categoryId)
        {
            if (!categories.TryGetValue(categoryId, out var category))
            {
                return false;
            }

            if (!category.IsEnabled)
            {
                return false;
            }

            if (isAdmin)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(category.Visibility)
                || string.Equals(category.Visibility, "AuthenticatedUsers", StringComparison.OrdinalIgnoreCase))
            {
                return currentUser.IsAuthenticated;
            }

            if (string.Equals(category.Visibility, "Department", StringComparison.OrdinalIgnoreCase))
            {
                return MatchesDepartment(category.Department, currentUser.CloudDepartmentId)
                       || MatchesDepartment(category.Department, currentUser.CloudDepartmentName);
            }

            return false;
        }

        private static bool MatchesDepartment(string categoryDepartment, string? userDepartment)
        {
            return !string.IsNullOrWhiteSpace(categoryDepartment)
                   && !string.IsNullOrWhiteSpace(userDepartment)
                   && string.Equals(categoryDepartment.Trim(), userDepartment.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
