using System.ComponentModel;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Agents;

internal sealed class MainChatKnowledgeQueryTool(
    IKnowledgeRetrievalService knowledgeRetrievalService,
    IReadOnlyCollection<KnowledgeBaseDescriptor> authorizedKnowledgeBases)
{
    private const int PerKnowledgeBaseLimit = 3;
    private const int TotalLimit = 12;

    public static AiToolDefinition CreateDefinition(
        MainChatKnowledgeQueryTool target,
        IReadOnlyCollection<KnowledgeBaseDescriptor> authorizedKnowledgeBases)
    {
        var method = typeof(MainChatKnowledgeQueryTool).GetMethod(
                         nameof(KnowledgeQuery))
                     ?? throw new InvalidOperationException(
                         "KnowledgeQuery tool method is missing.");
        var authorizedNames = string.Join(
            ", ",
            authorizedKnowledgeBases.Select(item => item.Name));
        return new AiToolDefinition
        {
            Name = "KnowledgeQuery",
            ToolName = "KnowledgeQuery",
            Description =
                $"Search only the current user's authorized knowledge bases. Authorized catalog: [{authorizedNames}]. With one authorized base, an empty knowledgeBaseNames array auto-selects it; with multiple bases, provide only names from this catalog.",
            Method = method,
            Target = target,
            ExternalSystemType = AiToolExternalSystemType.NonCloud,
            CapabilityKind = AiToolCapabilityKind.ReadOnlyQuery,
            RiskLevel = AiToolRiskLevel.Low,
            ReadOnlyDeclared = true,
            RequiredPermission = "Rag.SearchKnowledgeBase",
            AuditLevel = "Standard",
            DataBoundary = "RagContextOnly",
            SchemaVersion = 1
        };
    }

    [Description(
        "Search authorized knowledge bases. Unknown or unauthorized names are rejected without revealing whether they exist. Returns only redacted summaries, citations, and governance evidence.")]
    public async Task<object> KnowledgeQuery(
        string question,
        string[] knowledgeBaseNames,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return Failure(
                "knowledge_question_required",
                "A knowledge query question is required.");
        }

        var selected = ResolveSelectedKnowledgeBases(knowledgeBaseNames);
        if (selected.Error is not null)
        {
            return selected.Error;
        }

        var hits = new List<KnowledgeQueryHit>();
        var citations = new List<KnowledgeQueryGovernedCitation>();
        var warningCodes = new HashSet<string>(StringComparer.Ordinal);
        var filteredVectorHitCount = 0;
        var hasGovernanceOverride = false;

        try
        {
            foreach (var knowledgeBase in selected.KnowledgeBases)
            {
                var results = await knowledgeRetrievalService.SearchAsync(
                    knowledgeBase.Id,
                    question.Trim(),
                    PerKnowledgeBaseLimit,
                    minScore: 0.5,
                    cancellationToken);
                foreach (var result in results.Take(PerKnowledgeBaseLimit))
                {
                    if (hits.Count >= TotalLimit)
                    {
                        break;
                    }

                    hits.Add(new KnowledgeQueryHit(
                        knowledgeBase.Name,
                        TrustedKnowledgeSummary.Redact(result.Text),
                        Math.Round(result.Score, 4),
                        result.IsLowConfidence,
                        TrustedKnowledgeSummary.Redact(
                            result.LowConfidenceReason,
                            maxLength: 240),
                        new KnowledgeQueryCitation(
                            result.DocumentId,
                            TrustedKnowledgeSummary.Redact(
                                result.DocumentName,
                                maxLength: 160),
                            result.ChunkIndex)));

                    var governance = result.GovernanceEvidence;
                    if (governance is null)
                    {
                        continue;
                    }

                    filteredVectorHitCount += governance.FilteredVectorHitCount;
                    hasGovernanceOverride |= governance.HasGovernanceOverride;
                    warningCodes.UnionWith(governance.WarningCodes);
                    citations.AddRange(governance.Citations.Select(citation =>
                        new KnowledgeQueryGovernedCitation(
                            citation.DocumentId,
                            TrustedKnowledgeSummary.Redact(
                                citation.DocumentName,
                                maxLength: 160),
                            citation.ChunkIndex,
                            citation.DocumentGroupId,
                            citation.VersionNo,
                            TrustedKnowledgeSummary.Redact(
                                citation.Classification,
                                maxLength: 80),
                            TrustedKnowledgeSummary.Redact(
                                citation.SourceType,
                                maxLength: 80),
                            citation.CategoryId,
                            citation.CitationHash)));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Never expose provider or vector-store failure details to the model.
            return Failure(
                "knowledge_search_failed",
                "The authorized knowledge search could not be completed.");
        }

        return new KnowledgeQueryResponse(
            hits.Count == 0 ? "empty" : "succeeded",
            hits.Count,
            hits,
            citations.Distinct().ToArray(),
            new KnowledgeQueryGovernance(
                warningCodes.Order(StringComparer.Ordinal).ToArray(),
                hasGovernanceOverride,
                filteredVectorHitCount,
                PerKnowledgeBaseLimit,
                TotalLimit));
    }

    private KnowledgeSelection ResolveSelectedKnowledgeBases(
        IEnumerable<string>? requestedNames)
    {
        var requested = (requestedNames ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requested.Length == 0)
        {
            return authorizedKnowledgeBases.Count == 1
                ? new KnowledgeSelection(
                    new[] { authorizedKnowledgeBases.Single() },
                    null)
                : new KnowledgeSelection(
                    Array.Empty<KnowledgeBaseDescriptor>(),
                    Failure(
                        "knowledge_scope_required",
                        "Select one or more knowledge bases from the authorized catalog."));
        }

        if (requested.Length > TotalLimit / PerKnowledgeBaseLimit)
        {
            return new KnowledgeSelection(
                Array.Empty<KnowledgeBaseDescriptor>(),
                Failure(
                    "knowledge_scope_limit_exceeded",
                    "At most four authorized knowledge bases may be searched per call."));
        }

        var authorizedByName = authorizedKnowledgeBases.ToDictionary(
            item => item.Name,
            StringComparer.OrdinalIgnoreCase);
        if (requested.Any(name => !authorizedByName.ContainsKey(name)))
        {
            return new KnowledgeSelection(
                Array.Empty<KnowledgeBaseDescriptor>(),
                Failure(
                    "knowledge_scope_denied",
                    "One or more requested knowledge bases are outside the authorized catalog."));
        }

        return new KnowledgeSelection(
            requested.Select(name => authorizedByName[name]).ToArray(),
            null);
    }

    private static KnowledgeQueryFailure Failure(string code, string message) =>
        new("failed", code, message);

    private sealed record KnowledgeSelection(
        IReadOnlyList<KnowledgeBaseDescriptor> KnowledgeBases,
        KnowledgeQueryFailure? Error);
}

internal sealed record KnowledgeQueryFailure(string Status, string Code, string Message);

internal sealed record KnowledgeQueryResponse(
    string Status,
    int ResultCount,
    IReadOnlyCollection<KnowledgeQueryHit> Results,
    IReadOnlyCollection<KnowledgeQueryGovernedCitation> Citations,
    KnowledgeQueryGovernance Governance);

internal sealed record KnowledgeQueryHit(
    string KnowledgeBase,
    string? Summary,
    double Score,
    bool LowConfidence,
    string? LowConfidenceReason,
    KnowledgeQueryCitation Citation);

internal sealed record KnowledgeQueryCitation(
    int DocumentId,
    string? DocumentName,
    int ChunkIndex);

internal sealed record KnowledgeQueryGovernedCitation(
    int DocumentId,
    string? DocumentName,
    int ChunkIndex,
    Guid DocumentGroupId,
    int VersionNo,
    string? Classification,
    string? SourceType,
    Guid? CategoryId,
    string CitationHash);

internal sealed record KnowledgeQueryGovernance(
    IReadOnlyCollection<string> WarningCodes,
    bool HasGovernanceOverride,
    int FilteredVectorHitCount,
    int PerKnowledgeBaseLimit,
    int TotalLimit);

internal static class TrustedKnowledgeSummary
{
    private const int DefaultMaxLength = 800;
    private const string Redacted = "[content removed by knowledge safety policy]";
    private static readonly string[] DangerousFragments =
    [
        "ignore previous",
        "ignore system",
        "ignore instructions",
        "system prompt",
        "developer message",
        "connection string",
        "connectionstring",
        "password=",
        "pwd=",
        "api_key",
        "apikey",
        "执行sql",
        "绕过审批",
        "忽略系统",
        "忽略指令"
    ];

    public static string? Redact(string? value, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string(value
                .Where(character => !char.IsControl(character) || char.IsWhiteSpace(character))
                .ToArray())
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        while (normalized.Contains("  ", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (DangerousFragments.Any(fragment =>
                normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return Redacted;
        }

        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..maxLength]}...";
    }
}
