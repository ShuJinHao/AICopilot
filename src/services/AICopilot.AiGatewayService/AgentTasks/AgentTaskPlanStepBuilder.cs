using AICopilot.Core.AiGateway.Aggregates.AgentTasks;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentTaskPlanStepBuilder
{
    public static IReadOnlyCollection<AgentStepPlanDto> EnsureMandatorySteps(
        IReadOnlyCollection<AgentStepPlanDto> steps,
        bool hasUploads,
        bool hasKnowledgeBases,
        bool hasBusinessDataSources,
        AgentTaskType taskType,
        bool requiresDataApproval,
        IReadOnlyCollection<string>? artifactTypes)
    {
        var result = steps.ToList();
        var normalizedArtifactTypes = NormalizeArtifactTypes(artifactTypes);
        if (hasUploads)
        {
            InsertBeforeOutputs(
                result,
                "read_uploaded_file",
                new AgentStepPlanDto(
                    "Read uploaded files",
                    "Read task uploads into the controlled workspace source area.",
                    AgentStepType.FileRead,
                    "read_uploaded_file",
                    false));
            InsertBeforeOutputs(
                result,
                "parse_table_file",
                new AgentStepPlanDto(
                    "Parse table files",
                    "Parse CSV, JSON, or XLSX uploads into normalized data.",
                    AgentStepType.Analysis,
                    "parse_table_file",
                    false));
        }

        if (hasKnowledgeBases)
        {
            InsertBeforeOutputs(
                result,
                "rag_search",
                new AgentStepPlanDto(
                    "Search knowledge bases",
                    "Retrieve only authorized knowledge base context for the task.",
                    AgentStepType.RagSearch,
                    "rag_search",
                    false));
        }

        if (taskType == AgentTaskType.CloudDataReport)
        {
            InsertBeforeOutputs(
                result,
                "query_cloud_data_readonly",
                new AgentStepPlanDto(
                    "Query Cloud readonly data",
                    "Read Cloud business data only through the AiRead readonly boundary.",
                    AgentStepType.DataQuery,
                    "query_cloud_data_readonly",
                    false));
        }

        if (hasBusinessDataSources)
        {
            InsertBeforeOutputs(
                result,
                "query_business_database_readonly",
                new AgentStepPlanDto(
                    "Query business database",
                    "Run Text-to-SQL only through authorized BusinessDatabase readonly guardrails.",
                    AgentStepType.DataQuery,
                    "query_business_database_readonly",
                    requiresDataApproval));
            InsertBeforeOutputs(
                result,
                "summarize_business_query_result",
                new AgentStepPlanDto(
                    "Summarize business query result",
                    "Summarize readonly business query output with source labels and query hashes.",
                    AgentStepType.Analysis,
                    "summarize_business_query_result",
                    false));
        }

        EnsureArtifactSteps(result, hasBusinessDataSources, normalizedArtifactTypes);

        if (!ContainsTool(result, "finalize_artifacts"))
        {
            result.Add(new AgentStepPlanDto(
                "Confirm final output",
                "Wait for final output approval before moving draft artifacts to final.",
                AgentStepType.Finalize,
                "finalize_artifacts",
                true));
        }

        return result;
    }

    public static IReadOnlyCollection<AgentStepPlanDto> BuildPlanSteps(
        bool hasUploads,
        bool hasKnowledgeBases,
        bool hasBusinessDataSources,
        AgentTaskType taskType,
        AgentTaskRiskLevel riskLevel,
        IReadOnlyCollection<string>? artifactTypes)
    {
        var steps = new List<AgentStepPlanDto>();
        var normalizedArtifactTypes = NormalizeArtifactTypes(artifactTypes);
        if (hasUploads)
        {
            steps.Add(new AgentStepPlanDto("Read uploaded files", "Read task uploads into the controlled workspace source area.", AgentStepType.FileRead, "read_uploaded_file", false));
            steps.Add(new AgentStepPlanDto("Parse table files", "Parse CSV, JSON, or XLSX uploads into normalized data.", AgentStepType.Analysis, "parse_table_file", false));
        }

        if (hasKnowledgeBases)
        {
            steps.Add(new AgentStepPlanDto("Search knowledge bases", "Retrieve only authorized knowledge base context for the task.", AgentStepType.RagSearch, "rag_search", false));
        }

        if (taskType == AgentTaskType.CloudDataReport)
        {
            steps.Add(new AgentStepPlanDto("Query Cloud readonly data", "Read Cloud business data only through the AiRead readonly boundary.", AgentStepType.DataQuery, "query_cloud_data_readonly", false));
        }

        steps.Add(new AgentStepPlanDto("Generate chart data", "Generate frontend chart preview data from controlled task inputs.", AgentStepType.ChartGeneration, "generate_chart_data", false));
        steps.Add(new AgentStepPlanDto("Generate Markdown report", "Generate a Markdown draft in the controlled workspace.", AgentStepType.ArtifactGeneration, "generate_markdown_report", false));
        steps.Add(new AgentStepPlanDto("Generate HTML report", "Generate an HTML draft in the controlled workspace.", AgentStepType.ArtifactGeneration, "generate_html_report", false));
        steps.Add(new AgentStepPlanDto("Generate PDF draft", "Generate a basic PDF report draft in the controlled workspace.", AgentStepType.ArtifactGeneration, "generate_pdf", true));
        steps.Add(new AgentStepPlanDto("Generate PPTX draft", "Generate a basic PPTX presentation draft in the controlled workspace.", AgentStepType.ArtifactGeneration, "generate_pptx", true));
        steps.Add(new AgentStepPlanDto("Generate XLSX draft", "Generate a basic XLSX data draft in the controlled workspace.", AgentStepType.ArtifactGeneration, "generate_xlsx", true));
        steps.Add(new AgentStepPlanDto("Confirm final output", "Wait for user approval before moving draft artifacts to final output.", AgentStepType.Finalize, "finalize_artifacts", true));

        if (normalizedArtifactTypes is not null)
        {
            steps = steps
                .Where(step => ShouldKeepStepForArtifacts(step, normalizedArtifactTypes))
                .ToList();
        }

        return riskLevel >= AgentTaskRiskLevel.High
            ? steps.Select(step => step with { RequiresApproval = true }).ToArray()
            : steps;
    }

    public static IReadOnlySet<string>? NormalizeArtifactTypes(IReadOnlyCollection<string>? artifactTypes)
    {
        if (artifactTypes is null || artifactTypes.Count == 0)
        {
            return null;
        }

        var normalized = artifactTypes
            .Select(NormalizeArtifactType)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return normalized.Count == 0 ? null : normalized;
    }

    private static void InsertBeforeOutputs(
        List<AgentStepPlanDto> steps,
        string toolCode,
        AgentStepPlanDto step)
    {
        if (ContainsTool(steps, toolCode))
        {
            return;
        }

        var index = steps.FindIndex(item =>
            item.StepType is AgentStepType.ChartGeneration or AgentStepType.ArtifactGeneration or AgentStepType.Finalize);
        if (index < 0)
        {
            steps.Add(step);
        }
        else
        {
            steps.Insert(index, step);
        }
    }

    private static bool ContainsTool(IEnumerable<AgentStepPlanDto> steps, string toolCode)
    {
        return steps.Any(step => string.Equals(step.ToolCode, toolCode, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldKeepStepForArtifacts(
        AgentStepPlanDto step,
        IReadOnlySet<string> artifactTypes)
    {
        return step.ToolCode switch
        {
            "generate_chart_data" or "generate_business_chart" => artifactTypes.Contains("chart"),
            "generate_markdown_report" => artifactTypes.Contains("markdown"),
            "generate_html_report" => artifactTypes.Contains("html"),
            "generate_pdf" => artifactTypes.Contains("pdf"),
            "generate_pptx" => artifactTypes.Contains("pptx"),
            "generate_xlsx" => artifactTypes.Contains("xlsx"),
            _ => true
        };
    }

    private static void EnsureArtifactSteps(
        List<AgentStepPlanDto> steps,
        bool hasBusinessDataSources,
        IReadOnlySet<string>? artifactTypes)
    {
        var hasPlannedOutputs = steps.Any(step =>
            step.StepType is AgentStepType.ChartGeneration or AgentStepType.ArtifactGeneration);
        if (artifactTypes is null && hasPlannedOutputs)
        {
            artifactTypes = hasBusinessDataSources
                ? new HashSet<string>(["chart"], StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        else if (artifactTypes is null)
        {
            artifactTypes = hasBusinessDataSources
                ? new HashSet<string>(["chart", "markdown"], StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        if (hasBusinessDataSources)
        {
            steps.RemoveAll(step => string.Equals(step.ToolCode, "generate_chart_data", StringComparison.OrdinalIgnoreCase));
        }

        if (ShouldIncludeArtifact(artifactTypes, "chart"))
        {
            var toolCode = hasBusinessDataSources ? "generate_business_chart" : "generate_chart_data";
            InsertBeforeOutputs(
                steps,
                toolCode,
                new AgentStepPlanDto(
                    "Generate chart data",
                    hasBusinessDataSources
                        ? "Generate controlled chart data from approved BusinessDatabase readonly query results."
                        : "Generate chart preview data from controlled task inputs.",
                    AgentStepType.ChartGeneration,
                    toolCode,
                    false));
        }

        if (ShouldIncludeArtifact(artifactTypes, "markdown"))
        {
            InsertBeforeOutputs(
                steps,
                "generate_markdown_report",
                new AgentStepPlanDto(
                    "Generate Markdown report",
                    "Generate a Markdown draft in the controlled workspace.",
                    AgentStepType.ArtifactGeneration,
                    "generate_markdown_report",
                    false));
        }

        if (ShouldIncludeArtifact(artifactTypes, "html"))
        {
            InsertBeforeOutputs(
                steps,
                "generate_html_report",
                new AgentStepPlanDto(
                    "Generate HTML report",
                    "Generate an HTML draft in the controlled workspace.",
                    AgentStepType.ArtifactGeneration,
                    "generate_html_report",
                    false));
        }

        if (ShouldIncludeArtifact(artifactTypes, "pdf"))
        {
            InsertBeforeOutputs(
                steps,
                "generate_pdf",
                new AgentStepPlanDto(
                    "Generate PDF draft",
                    "Generate a PDF draft in the controlled workspace.",
                    AgentStepType.ArtifactGeneration,
                    "generate_pdf",
                    true));
        }

        if (ShouldIncludeArtifact(artifactTypes, "pptx"))
        {
            InsertBeforeOutputs(
                steps,
                "generate_pptx",
                new AgentStepPlanDto(
                    "Generate PPTX draft",
                    "Generate a PPTX draft in the controlled workspace.",
                    AgentStepType.ArtifactGeneration,
                    "generate_pptx",
                    true));
        }

        if (ShouldIncludeArtifact(artifactTypes, "xlsx"))
        {
            InsertBeforeOutputs(
                steps,
                "generate_xlsx",
                new AgentStepPlanDto(
                    "Generate XLSX draft",
                    "Generate an XLSX draft in the controlled workspace.",
                    AgentStepType.ArtifactGeneration,
                    "generate_xlsx",
                    true));
        }
    }

    private static bool ShouldIncludeArtifact(IReadOnlySet<string>? artifactTypes, string artifactType)
    {
        return artifactTypes is null || artifactTypes.Contains(artifactType);
    }

    private static string? NormalizeArtifactType(string? artifactType)
    {
        var value = artifactType?.Trim().ToLowerInvariant();
        return value switch
        {
            "chart" or "chartdata" or "chart-data" or "json" => "chart",
            "markdown" or "md" => "markdown",
            "html" => "html",
            "pdf" => "pdf",
            "ppt" or "pptx" or "presentation" => "pptx",
            "xls" or "xlsx" or "excel" or "spreadsheet" => "xlsx",
            _ => null
        };
    }
}
