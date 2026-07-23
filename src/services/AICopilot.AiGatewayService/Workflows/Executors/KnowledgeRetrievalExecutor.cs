using System.Text;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public class KnowledgeRetrievalExecutor(
    IKnowledgeRetrievalService knowledgeRetrievalService,
    IKnowledgeBaseReadService knowledgeBaseReadService,
    ILogger<KnowledgeRetrievalExecutor> logger)
{
    public const string ExecutorId = nameof(KnowledgeRetrievalExecutor);
    private const string KnowledgeIntentPrefix = "Knowledge.";

    internal static bool IsRelevant(IEnumerable<IntentResult> intents, AgentIntentRegistrySnapshot registry) =>
        AgentWorkflowIntentSelector.Any(intents, registry, 0.6, "Knowledge.Retrieve", AgentIntentClass.Knowledge);

    internal async Task<BranchResult> ExecuteAsync(
        List<IntentResult> intentResults,
        AgentIntentRegistrySnapshot registry,
        CancellationToken ct = default)
    {
        var knowledgeIntents = AgentWorkflowIntentSelector.Select(
            intentResults, registry, 0.6, "Knowledge.Retrieve", AgentIntentClass.Knowledge);

        if (knowledgeIntents.Count == 0)
        {
            logger.LogDebug("No knowledge intent detected, skipping retrieval.");
            return BranchResult.Skipped(BranchType.Knowledge);
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
            return BranchResult.Failed(
                BranchType.Knowledge,
                AppProblemCodes.ChatConfigurationMissing,
                "Knowledge base configuration is unavailable for the routed intent.");
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
            return BranchResult.Failed(
                BranchType.Knowledge,
                AppProblemCodes.ChatStreamFailed,
                "Knowledge retrieval could not build a valid search request.");
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
                sb.AppendLine($"<document id=\"{item.DocumentId}\" name=\"{item.DocumentName}\" chunk=\"{item.ChunkIndex}\" score=\"{item.Score:F2}\" low_confidence=\"{item.IsLowConfidence}\">");
                if (item.IsLowConfidence && !string.IsNullOrWhiteSpace(item.LowConfidenceReason))
                {
                    sb.AppendLine($"<low_confidence_reason>{item.LowConfidenceReason}</low_confidence_reason>");
                }

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
            logger.LogError(
                "Knowledge retrieval failed for {KbName}. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                kbName,
                ex.GetType().Name);
            throw;
        }
    }

}
