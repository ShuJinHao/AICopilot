using System.Text;
using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public class KnowledgeRetrievalExecutor(
    IKnowledgeRetrievalService knowledgeRetrievalService,
    IKnowledgeBaseReadService knowledgeBaseReadService,
    ILogger<KnowledgeRetrievalExecutor> logger)
{
    public const string ExecutorId = nameof(KnowledgeRetrievalExecutor);
    private const string KnowledgeIntentPrefix = "Knowledge.";

    public async Task<BranchResult> ExecuteAsync(
        List<IntentResult> intentResults,
        CancellationToken ct = default)
    {
        var knowledgeIntents = intentResults
            .Where(i => i.Intent.StartsWith(KnowledgeIntentPrefix, StringComparison.OrdinalIgnoreCase)
                        && i.Confidence > 0.6)
            .ToList();

        if (knowledgeIntents.Count == 0)
        {
            logger.LogDebug("No knowledge intent detected, skipping retrieval.");
            return BranchResult.FromKnowledge(string.Empty);
        }

        logger.LogInformation("Starting knowledge retrieval. Intent count: {Count}", knowledgeIntents.Count);

        var kbNames = knowledgeIntents
            .Select(i => i.Intent.Substring(KnowledgeIntentPrefix.Length))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var knowledgeBases = await knowledgeBaseReadService.GetByNamesAsync(kbNames, ct);
        ct.ThrowIfCancellationRequested();

        if (knowledgeBases.Count == 0)
        {
            logger.LogWarning("Knowledge intents matched {Names}, but no knowledge base configuration was found.", string.Join(", ", kbNames));
            return BranchResult.FromKnowledge(string.Empty);
        }

        var searchTasks = new List<Task<string>>();

        foreach (var intent in knowledgeIntents)
        {
            var kbName = intent.Intent.Substring(KnowledgeIntentPrefix.Length);
            var kb = knowledgeBases.FirstOrDefault(k => k.Name.Equals(kbName, StringComparison.OrdinalIgnoreCase));

            if (kb == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(intent.Query))
            {
                logger.LogWarning("Intent {Intent} has no query text, skipping.", intent.Intent);
                continue;
            }

            searchTasks.Add(ExecuteSearchAsync(kb.Id, kb.Name, intent.Query, ct));
        }

        if (searchTasks.Count == 0)
        {
            return BranchResult.FromKnowledge(string.Empty);
        }

        var searchResults = await Task.WhenAll(searchTasks);
        ct.ThrowIfCancellationRequested();

        var combinedContext = string.Join("\n\n", searchResults.Where(s => !string.IsNullOrWhiteSpace(s)));

        return BranchResult.FromKnowledge(combinedContext);
    }

    private async Task<string> ExecuteSearchAsync(
        Guid kbId,
        string kbName,
        string queryText,
        CancellationToken ct)
    {
        try
        {
            var result = await knowledgeRetrievalService.SearchAsync(kbId, queryText, topK: 3, minScore: 0.5, ct);
            ct.ThrowIfCancellationRequested();

            if (result.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (var item in result)
            {
                sb.AppendLine($"<document id=\"{item.DocumentId}\" name=\"{item.DocumentName}\" score=\"{item.Score:F2}\">");
                sb.AppendLine(item.Text);
                sb.AppendLine("</document>");
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Knowledge retrieval failed for {KbName}.", kbName);
            return string.Empty;
        }
    }
}
