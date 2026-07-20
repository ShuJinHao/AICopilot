using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentTaskPlanStepBuilder
{
    private static readonly string[] DefaultArtifactTypes =
        ["chart", "html", "markdown", "pdf", "pptx", "xlsx"];

    public static Result<IReadOnlySet<string>> ResolveArtifactTypes(
        IReadOnlyCollection<string>? artifactTypes)
    {
        if (artifactTypes is null)
        {
            return Result.Success<IReadOnlySet<string>>(
                new HashSet<string>(DefaultArtifactTypes, StringComparer.Ordinal));
        }

        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifactType in artifactTypes)
        {
            var value = NormalizeArtifactType(artifactType);
            if (value is null)
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.AgentPlanSchemaInvalid,
                    "artifactTypes contains an unknown Plan v2 artifact target."));
            }

            normalized.Add(value);
        }

        return Result.Success<IReadOnlySet<string>>(normalized);
    }

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
                    "读取上传文件",
                    "将本次任务上传的文件读取到受控工作区。",
                    AgentStepType.FileRead,
                    "read_uploaded_file",
                    false));
            InsertBeforeOutputs(
                result,
                "parse_table_file",
                new AgentStepPlanDto(
                    "解析表格文件",
                    "将 CSV、JSON 或 XLSX 上传文件解析为结构化数据。",
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
                    "检索知识库",
                    "只检索当前任务已授权的知识库内容。",
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
                    "查询 Cloud 只读数据",
                    "仅通过 AiRead 只读边界读取 Cloud 业务数据。",
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
                    "查询只读业务库",
                    "仅通过已授权的 BusinessDatabase 只读边界执行查询。",
                    AgentStepType.DataQuery,
                    "query_business_database_readonly",
                    requiresDataApproval));
            InsertBeforeOutputs(
                result,
                "summarize_business_query_result",
                new AgentStepPlanDto(
                    "总结查询结果",
                    "基于只读查询结果生成来源明确的摘要。",
                    AgentStepType.Analysis,
                    "summarize_business_query_result",
                    false));
        }

        EnsureArtifactSteps(result, hasBusinessDataSources, normalizedArtifactTypes);

        if (normalizedArtifactTypes is { Count: > 0 } && !ContainsTool(result, "finalize_artifacts"))
        {
            result.Add(new AgentStepPlanDto(
                "确认最终产物",
                "等待用户确认后，才允许将草稿产物转为最终输出。",
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
            steps.Add(new AgentStepPlanDto("读取上传文件", "将本次任务上传的文件读取到受控工作区。", AgentStepType.FileRead, "read_uploaded_file", false));
            steps.Add(new AgentStepPlanDto("解析表格文件", "将 CSV、JSON 或 XLSX 上传文件解析为结构化数据。", AgentStepType.Analysis, "parse_table_file", false));
        }

        if (hasKnowledgeBases)
        {
            steps.Add(new AgentStepPlanDto("检索知识库", "只检索当前任务已授权的知识库内容。", AgentStepType.RagSearch, "rag_search", false));
        }

        if (taskType == AgentTaskType.CloudDataReport)
        {
            steps.Add(new AgentStepPlanDto("查询 Cloud 只读数据", "仅通过 AiRead 只读边界读取 Cloud 业务数据。", AgentStepType.DataQuery, "query_cloud_data_readonly", false));
        }

        steps.Add(new AgentStepPlanDto("生成图表数据", "基于受控任务输入生成前端图表预览数据。", AgentStepType.ChartGeneration, "generate_chart_data", false));
        steps.Add(new AgentStepPlanDto("生成 Markdown 报告", "在受控工作区生成 Markdown 草稿。", AgentStepType.ArtifactGeneration, "generate_markdown_report", false));
        steps.Add(new AgentStepPlanDto("生成 HTML 报告", "在受控工作区生成 HTML 草稿。", AgentStepType.ArtifactGeneration, "generate_html_report", false));
        steps.Add(new AgentStepPlanDto("生成 PDF 草稿", "在受控工作区生成 PDF 报告草稿。", AgentStepType.ArtifactGeneration, "generate_pdf", true));
        steps.Add(new AgentStepPlanDto("生成 PPTX 草稿", "在受控工作区生成 PPTX 演示草稿。", AgentStepType.ArtifactGeneration, "generate_pptx", true));
        steps.Add(new AgentStepPlanDto("生成 XLSX 草稿", "在受控工作区生成 XLSX 数据草稿。", AgentStepType.ArtifactGeneration, "generate_xlsx", true));
        steps.Add(new AgentStepPlanDto("确认最终产物", "等待用户确认后，才允许将草稿产物转为最终输出。", AgentStepType.Finalize, "finalize_artifacts", true));

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
        var result = ResolveArtifactTypes(artifactTypes);
        return result.IsSuccess ? result.Value : null;
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
            "finalize_artifacts" => artifactTypes.Count > 0,
            _ => true
        };
    }

    private static void EnsureArtifactSteps(
        List<AgentStepPlanDto> steps,
        bool hasBusinessDataSources,
        IReadOnlySet<string>? artifactTypes)
    {
        artifactTypes ??= new HashSet<string>(DefaultArtifactTypes, StringComparer.Ordinal);

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
                    "生成图表数据",
                    hasBusinessDataSources
                        ? "基于已批准的 BusinessDatabase 只读查询结果生成受控图表数据。"
                        : "基于受控任务输入生成图表预览数据。",
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
                    "生成 Markdown 报告",
                    "在受控工作区生成 Markdown 草稿。",
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
                    "生成 HTML 报告",
                    "在受控工作区生成 HTML 草稿。",
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
                    "生成 PDF 草稿",
                    "在受控工作区生成 PDF 报告草稿。",
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
                    "生成 PPTX 草稿",
                    "在受控工作区生成 PPTX 演示草稿。",
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
                    "生成 XLSX 草稿",
                    "在受控工作区生成 XLSX 数据草稿。",
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
