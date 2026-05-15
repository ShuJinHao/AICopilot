using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Core.AiGateway.Specifications.Uploads;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public interface IAgentTaskRuntime
{
    Task<Result<AgentTask>> RunAsync(AgentTask task, CancellationToken cancellationToken = default);
}

public sealed class AgentTaskRuntime(
    IRepository<AgentTask> taskRepository,
    IRepository<ArtifactWorkspace> workspaceRepository,
    IRepository<ApprovalRequest> approvalRepository,
    IReadRepository<UploadRecord> uploadRepository,
    IAgentArtifactWorkspaceService workspaceService,
    IFileStorageService fileStorage,
    IAgentTableFileParser tableFileParser,
    IAgentArtifactDocumentGenerator documentGenerator,
    IKnowledgeRetrievalService knowledgeRetrievalService,
    ICloudAiReadClient cloudAiReadClient,
    AgentAuditRecorder auditRecorder)
    : IAgentTaskRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };

    public async Task<Result<AgentTask>> RunAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (task.Status is AgentTaskStatus.Approved or AgentTaskStatus.WaitingToolApproval)
        {
            task.Start(now);
        }

        if (task.Status is not AgentTaskStatus.Running and not AgentTaskStatus.GeneratingArtifacts)
        {
            return Result.Invalid("Only approved or running agent tasks can be executed.");
        }

        var plan = DeserializePlan(task.PlanJson);
        var workspace = await LoadWorkspaceAsync(task, cancellationToken);
        var state = new AgentTaskRunState();

        foreach (var step in task.Steps.OrderBy(step => step.StepIndex))
        {
            if (step.Status == AgentStepStatus.Completed)
            {
                continue;
            }

            if (RequiresRuntimeApproval(step) && step.Status == AgentStepStatus.Pending)
            {
                step.WaitForApproval();
            }

            if (step.Status == AgentStepStatus.WaitingApproval)
            {
                if (string.Equals(step.ToolCode, "finalize_artifacts", StringComparison.OrdinalIgnoreCase))
                {
                    await EnsureApprovalRequestAsync(
                        task,
                        AgentApprovalType.FinalOutput,
                        workspace.WorkspaceCode,
                        cancellationToken);
                    task.MarkWorkspaceReady(now);
                    task.WaitForFinalApproval(now);

                    await SaveAsync(task, workspace, cancellationToken);
                    return Result.Success(task);
                }
                else
                {
                    var stepTargetId = step.Id.Value.ToString();
                    if (await HasApprovedApprovalAsync(task, AgentApprovalType.ToolCall, stepTargetId, cancellationToken))
                    {
                        step.Approve();
                    }
                    else
                    {
                        await EnsureApprovalRequestAsync(
                            task,
                            AgentApprovalType.ToolCall,
                            stepTargetId,
                            cancellationToken);
                        task.WaitForToolApproval(now);

                        await SaveAsync(task, workspace, cancellationToken);
                        return Result.Success(task);
                    }
                }
            }

            if (step.Status != AgentStepStatus.Pending)
            {
                continue;
            }

            try
            {
                step.Start(DateTimeOffset.UtcNow);
                if (task.Status == AgentTaskStatus.Running &&
                    step.StepType is AgentStepType.ChartGeneration or AgentStepType.ArtifactGeneration)
                {
                    task.BeginArtifactGeneration(DateTimeOffset.UtcNow);
                }

                var output = await ExecuteStepAsync(task, workspace, plan, step, state, cancellationToken);
                step.Complete(JsonSerializer.Serialize(output, JsonOptions), DateTimeOffset.UtcNow);
                await auditRecorder.RecordToolAsync(
                    task,
                    workspace,
                    step,
                    AuditResults.Succeeded,
                    $"Agent step {step.StepIndex} executed.",
                    ExtractArtifactId(output),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                step.Fail(ex.Message, DateTimeOffset.UtcNow);
                await auditRecorder.RecordToolAsync(
                    task,
                    workspace,
                    step,
                    AuditResults.Rejected,
                    ex.Message,
                    null,
                    cancellationToken);
                task.Fail($"步骤 {step.StepIndex} 执行失败：{ex.Message}", DateTimeOffset.UtcNow);
                await SaveAsync(task, workspace, cancellationToken);
                return Result.Success(task);
            }
        }

        task.MarkWorkspaceReady(DateTimeOffset.UtcNow);
        task.WaitForFinalApproval(DateTimeOffset.UtcNow);
        await SaveAsync(task, workspace, cancellationToken);
        return Result.Success(task);
    }

    private async Task<object> ExecuteStepAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentTaskPlanDocument plan,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        return step.ToolCode switch
        {
            "read_uploaded_file" => await ReadUploadedFilesAsync(task.UserId, workspace, step, plan, state, writeSourceArtifacts: true, cancellationToken: cancellationToken),
            "parse_csv_json" => await ParseTableFileAsync(task.UserId, workspace, step, plan, state, cancellationToken),
            "parse_table_file" => await ParseTableFileAsync(task.UserId, workspace, step, plan, state, cancellationToken),
            "rag_search" => await SearchRagAsync(task, plan, state, cancellationToken),
            "query_cloud_data_readonly" => QueryCloudReadonly(state),
            "generate_chart_data" => await GenerateChartDataAsync(workspace, step, state, cancellationToken),
            "generate_markdown_report" => await GenerateMarkdownReportAsync(task, workspace, step, state, cancellationToken),
            "generate_html_report" => await GenerateHtmlReportAsync(task, workspace, step, state, cancellationToken),
            "generate_pdf" => await GeneratePdfReportAsync(task, workspace, step, state, cancellationToken),
            "generate_pptx" => await GeneratePptxReportAsync(task, workspace, step, state, cancellationToken),
            "generate_xlsx" => await GenerateXlsxReportAsync(task, workspace, step, state, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported agent tool code: {step.ToolCode}")
        };
    }

    private async Task<object> ReadUploadedFilesAsync(
        Guid userId,
        ArtifactWorkspace? workspace,
        AgentStep? step,
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        bool writeSourceArtifacts,
        CancellationToken cancellationToken)
    {
        var uploads = await LoadUploadsAsync(plan.UploadIds, userId, cancellationToken);
        foreach (var upload in uploads)
        {
            if (state.Uploads.Any(item => item.Id == upload.Id.Value))
            {
                continue;
            }

            string? preview = null;
            if (!string.IsNullOrWhiteSpace(upload.StoragePath))
            {
                await using var stream = await fileStorage.GetAsync(upload.StoragePath, cancellationToken);
                if (stream is not null)
                {
                    await using var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer, cancellationToken);
                    var bytes = buffer.ToArray();
                    preview = BuildTextPreview(upload.FileName, bytes);

                    if (writeSourceArtifacts && workspace is not null && step is not null)
                    {
                        await workspaceService.WriteDraftBinaryArtifactAsync(
                            workspace,
                            ResolveArtifactType(upload.FileName),
                            upload.FileName,
                            $"source/{SafeFileName(upload.FileName)}",
                            bytes,
                            upload.ContentType,
                            step.Id,
                            cancellationToken);
                    }
                }
            }

            state.Uploads.Add(new AgentUploadSummary(
                upload.Id.Value,
                upload.FileName,
                upload.ContentType,
                upload.FileSize,
                upload.Sha256,
                upload.StoragePath,
                preview));
        }

        return new { status = "completed", files = state.Uploads };
    }

    private async Task<object> ParseTableFileAsync(
        Guid userId,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        if (state.Uploads.Count == 0)
        {
            await ReadUploadedFilesAsync(userId, workspace, step, plan, state, writeSourceArtifacts: false, cancellationToken: cancellationToken);
        }

        foreach (var upload in state.Uploads)
        {
            if (string.IsNullOrWhiteSpace(upload.StoragePath))
            {
                continue;
            }

            await using var stream = await fileStorage.GetAsync(upload.StoragePath, cancellationToken);
            if (stream is null)
            {
                continue;
            }

            var table = await tableFileParser.ParseAsync(
                new AgentTableFileParseRequest(upload.FileName, upload.ContentType, stream),
                cancellationToken);
            if (table is null)
            {
                continue;
            }

            state.Tables.Add(table);
            state.ParsedData.Add(new AgentParsedData(upload.FileName, "table", BuildTablePreview(table)));
            var fileStem = SafeFileStem(upload.FileName);
            var json = JsonSerializer.Serialize(table, JsonOptions);
            await workspaceService.WriteDraftTextArtifactAsync(
                workspace,
                ArtifactType.Json,
                $"{fileStem}.normalized.json",
                $"data/{fileStem}.normalized.json",
                json,
                "application/json",
                step.Id,
                cancellationToken);
            await workspaceService.WriteDraftTextArtifactAsync(
                workspace,
                ArtifactType.Csv,
                $"{fileStem}.normalized.csv",
                $"data/{fileStem}.normalized.csv",
                BuildCsv(table),
                "text/csv",
                step.Id,
                cancellationToken);
        }

        return new
        {
            status = "completed",
            tables = state.Tables.Select(table => new
            {
                table.Name,
                table.Columns,
                rowCount = table.Rows.Count
            }),
            parsed = state.ParsedData
        };
    }

    private async Task<object> SearchRagAsync(
        AgentTask task,
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        foreach (var knowledgeBaseId in plan.KnowledgeBaseIds)
        {
            var results = await knowledgeRetrievalService.SearchAsync(
                knowledgeBaseId,
                task.Goal,
                topK: 3,
                minScore: 0.5,
                cancellationToken);
            state.RagResults.AddRange(results.Select(result => new AgentRagResult(
                knowledgeBaseId,
                result.DocumentId,
                result.DocumentName,
                result.ChunkIndex,
                result.Score,
                result.IsLowConfidence,
                result.LowConfidenceReason,
                result.Text)));
        }

        return new
        {
            status = "completed",
            lowConfidence = state.RagResults.Count == 0 || state.RagResults.All(item => item.Score < 0.65),
            sources = state.RagResults
        };
    }

    private object QueryCloudReadonly(AgentTaskRunState state)
    {
        state.CloudReadonlySummary = cloudAiReadClient.IsEnabled
            ? "Cloud AiRead 已启用；MVP 仅保留只读边界，不从自由文本构造写入或任意查询。"
            : "Cloud AiRead 未启用，本步骤未访问 Cloud。";
        return new { status = "completed", summary = state.CloudReadonlySummary };
    }

    private async Task<object> GenerateChartDataAsync(
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var payload = BuildChartPayload(state);
        var content = JsonSerializer.Serialize(payload, JsonOptions);
        var artifact = await workspaceService.WriteDraftTextArtifactAsync(
            workspace,
            ArtifactType.Chart,
            "chart-data.json",
            "charts/chart-data.json",
            content,
            "application/json",
            step.Id,
            cancellationToken);
        return new { status = "completed", artifactId = artifact.Id.Value, artifact.RelativePath };
    }

    private async Task<object> GenerateMarkdownReportAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var markdown = BuildMarkdownReport(task, state);
        var artifact = await workspaceService.WriteDraftTextArtifactAsync(
            workspace,
            ArtifactType.Markdown,
            "report.md",
            "draft/report.md",
            markdown,
            "text/markdown",
            step.Id,
            cancellationToken);
        return new { status = "completed", artifactId = artifact.Id.Value, artifact.RelativePath };
    }

    private async Task<object> GenerateHtmlReportAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var html = BuildHtmlReport(task, state);
        var artifact = await workspaceService.WriteDraftTextArtifactAsync(
            workspace,
            ArtifactType.Html,
            "report.html",
            "draft/report.html",
            html,
            "text/html",
            step.Id,
            cancellationToken);
        return new { status = "completed", artifactId = artifact.Id.Value, artifact.RelativePath };
    }

    private async Task<object> GeneratePdfReportAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var content = await documentGenerator.GeneratePdfAsync(BuildReportDocument(task, state), cancellationToken);
        var artifact = await workspaceService.WriteDraftBinaryArtifactAsync(
            workspace,
            ArtifactType.Pdf,
            "report.pdf",
            "draft/report.pdf",
            content,
            "application/pdf",
            step.Id,
            cancellationToken);
        return new { status = "completed", artifactId = artifact.Id.Value, artifact.RelativePath };
    }

    private async Task<object> GeneratePptxReportAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var content = await documentGenerator.GeneratePptxAsync(BuildReportDocument(task, state), cancellationToken);
        var artifact = await workspaceService.WriteDraftBinaryArtifactAsync(
            workspace,
            ArtifactType.Pptx,
            "report.pptx",
            "draft/report.pptx",
            content,
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            step.Id,
            cancellationToken);
        return new { status = "completed", artifactId = artifact.Id.Value, artifact.RelativePath };
    }

    private async Task<object> GenerateXlsxReportAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var content = await documentGenerator.GenerateXlsxAsync(BuildReportDocument(task, state), cancellationToken);
        var artifact = await workspaceService.WriteDraftBinaryArtifactAsync(
            workspace,
            ArtifactType.Xlsx,
            "report.xlsx",
            "draft/report.xlsx",
            content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            step.Id,
            cancellationToken);
        return new { status = "completed", artifactId = artifact.Id.Value, artifact.RelativePath };
    }

    private async Task<ArtifactWorkspace> LoadWorkspaceAsync(AgentTask task, CancellationToken cancellationToken)
    {
        if (task.WorkspaceId is null)
        {
            var created = await workspaceService.CreateForTaskAsync(task, DateTimeOffset.UtcNow, cancellationToken);
            task.AttachWorkspace(created.Id, DateTimeOffset.UtcNow);
            return created;
        }

        var workspace = await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByIdSpec(task.WorkspaceId.Value, includeArtifacts: true),
            cancellationToken);
        if (workspace is null)
        {
            throw new InvalidOperationException("Agent task workspace was not found.");
        }

        return workspace;
    }

    private async Task EnsureApprovalRequestAsync(
        AgentTask task,
        AgentApprovalType approvalType,
        string targetId,
        CancellationToken cancellationToken)
    {
        var existing = await approvalRepository.FirstOrDefaultAsync(
            new PendingApprovalRequestByTaskAndTargetSpec(task.Id, approvalType, targetId),
            cancellationToken);
        if (existing is not null)
        {
            return;
        }

        approvalRepository.Add(new ApprovalRequest(
            task.Id,
            approvalType,
            targetId,
            task.UserId,
            DateTimeOffset.UtcNow));
    }

    private async Task<bool> HasApprovedApprovalAsync(
        AgentTask task,
        AgentApprovalType approvalType,
        string targetId,
        CancellationToken cancellationToken)
    {
        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id),
            cancellationToken);
        return approvals.Any(approval =>
            approval.ApprovalType == approvalType &&
            approval.TargetId == targetId &&
            approval.Status == AgentApprovalStatus.Approved);
    }

    private static bool RequiresRuntimeApproval(AgentStep step)
    {
        if (step.RequiresApproval)
        {
            return true;
        }

        return step.ToolCode is "generate_pdf" or "generate_pptx" or "generate_xlsx" or "finalize_artifacts";
    }

    private static string? ExtractArtifactId(object output)
    {
        try
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(output, JsonOptions));
            return document.RootElement.TryGetProperty("artifactId", out var artifactId)
                ? artifactId.ToString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<List<UploadRecord>> LoadUploadsAsync(
        IReadOnlyCollection<Guid> uploadIds,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (uploadIds.Count == 0)
        {
            return [];
        }

        return await uploadRepository.ListAsync(
            new UploadRecordsByIdsForUserSpec(uploadIds.Select(id => new UploadRecordId(id)).ToArray(), userId),
            cancellationToken);
    }

    private async Task SaveAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        CancellationToken cancellationToken)
    {
        taskRepository.Update(task);
        workspaceRepository.Update(workspace);
        await taskRepository.SaveChangesAsync(cancellationToken);
    }

    private static AgentTaskPlanDocument DeserializePlan(string planJson)
    {
        return JsonSerializer.Deserialize<AgentTaskPlanDocument>(planJson, JsonOptions)
               ?? throw new InvalidOperationException("Agent task plan JSON is invalid.");
    }

    private static string BuildMarkdownReport(AgentTask task, AgentTaskRunState state)
    {
        var report = BuildReportDocument(task, state);
        var builder = new StringBuilder();
        builder.AppendLine($"# {report.Title}");
        builder.AppendLine();
        builder.AppendLine("## 任务目标");
        builder.AppendLine(report.Goal);
        builder.AppendLine();
        builder.AppendLine("## 输入文件");
        builder.AppendLine(report.UploadSummaries.Count == 0
            ? "- 未绑定上传文件。"
            : string.Join(Environment.NewLine, report.UploadSummaries.Select(item => $"- {item}")));
        builder.AppendLine();
        builder.AppendLine("## 表格数据");
        if (report.Tables.Count == 0)
        {
            builder.AppendLine("- 未解析到 CSV/JSON/XLSX 表格数据。");
        }
        else
        {
            foreach (var table in report.Tables)
            {
                builder.AppendLine($"### {table.Name}");
                builder.AppendLine(BuildMarkdownTable(table));
                builder.AppendLine();
            }
        }

        builder.AppendLine();
        builder.AppendLine("## 知识库来源");
        builder.AppendLine(report.Sources.Count == 0
            ? "- 未检索到可靠知识库来源。"
            : string.Join(Environment.NewLine, report.Sources.Select(item =>
                $"- {item.SourceType}: {item.Name}，{item.Detail}{(item.Score.HasValue ? $"，分数 {item.Score:F2}" : string.Empty)}{(item.IsLowConfidence ? "，低置信度" : string.Empty)}")));
        builder.AppendLine();
        builder.AppendLine("## Cloud 只读边界");
        builder.AppendLine(report.CloudReadonlySummary ?? "未访问 Cloud。");
        builder.AppendLine();
        builder.AppendLine("> 草稿产物，正式输出前需要用户确认。");
        return builder.ToString();
    }

    private static string BuildHtmlReport(AgentTask task, AgentTaskRunState state)
    {
        var report = BuildReportDocument(task, state);
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"zh-CN\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\">");
        builder.AppendLine($"  <title>{EscapeHtml(report.Title)}</title>");
        builder.AppendLine("  <style>body{font-family:Arial,'Microsoft YaHei',sans-serif;margin:32px;color:#1f2937}table{border-collapse:collapse;width:100%;margin:12px 0 24px}th,td{border:1px solid #d1d5db;padding:6px 8px;text-align:left}th{background:#f3f4f6}.muted{color:#6b7280}</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine($"<h1>{EscapeHtml(report.Title)}</h1>");
        builder.AppendLine($"<p>{EscapeHtml(report.Goal)}</p>");
        builder.AppendLine("<h2>输入文件</h2>");
        builder.AppendLine("<ul>");
        foreach (var upload in report.UploadSummaries.DefaultIfEmpty("未绑定上传文件。"))
        {
            builder.AppendLine($"<li>{EscapeHtml(upload)}</li>");
        }

        builder.AppendLine("</ul>");
        builder.AppendLine("<h2>表格数据</h2>");
        foreach (var table in report.Tables)
        {
            builder.AppendLine($"<h3>{EscapeHtml(table.Name)}</h3>");
            builder.AppendLine(BuildHtmlTable(table));
        }

        if (report.Tables.Count == 0)
        {
            builder.AppendLine("<p class=\"muted\">未解析到 CSV/JSON/XLSX 表格数据。</p>");
        }

        builder.AppendLine("<h2>来源与边界</h2>");
        builder.AppendLine("<ul>");
        foreach (var source in report.Sources)
        {
            builder.AppendLine($"<li>{EscapeHtml(source.SourceType)}: {EscapeHtml(source.Name)} - {EscapeHtml(source.Detail)}</li>");
        }

        builder.AppendLine($"<li>{EscapeHtml(report.CloudReadonlySummary ?? "未访问 Cloud。")}</li>");
        builder.AppendLine("</ul>");
        builder.AppendLine("<p class=\"muted\">草稿产物，正式输出前需要用户确认。</p>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static AgentReportDocument BuildReportDocument(AgentTask task, AgentTaskRunState state)
    {
        return new AgentReportDocument(
            task.Title,
            task.Goal,
            state.Uploads
                .Select(item => $"{item.FileName} ({item.FileSize} bytes, sha256={SafeShaPrefix(item.Sha256)})")
                .ToArray(),
            state.Tables,
            state.RagResults
                .Select(item => new AgentReportSource(
                    "RAG",
                    item.DocumentName,
                    $"DocumentId={item.DocumentId}, Chunk={item.ChunkIndex}",
                    item.Score,
                    item.IsLowConfidence))
                .ToArray(),
            state.CloudReadonlySummary,
            DateTimeOffset.UtcNow);
    }

    private static object BuildChartPayload(AgentTaskRunState state)
    {
        var table = state.Tables.FirstOrDefault();
        if (table is not null && table.Rows.Count > 0 && table.Columns.Count > 0)
        {
            var labelColumn = table.Columns[0];
            var valueColumn = table.Columns.FirstOrDefault(column =>
                table.Rows.Any(row => row.TryGetValue(column, out var value) && TryParseNumber(value, out _)));
            if (valueColumn is not null)
            {
                return new
                {
                    type = "bar",
                    source = table.Name,
                    labels = table.Rows
                        .Take(20)
                        .Select(row => row.TryGetValue(labelColumn, out var value) ? value : string.Empty)
                        .ToArray(),
                    values = table.Rows
                        .Take(20)
                        .Select(row => row.TryGetValue(valueColumn, out var value) && TryParseNumber(value, out var number) ? number : 0)
                        .ToArray(),
                    generatedAt = DateTimeOffset.UtcNow
                };
            }
        }

        return new
        {
            type = "bar",
            source = "parsed-preview",
            labels = state.ParsedData.Select(item => item.FileName).DefaultIfEmpty("输入").ToArray(),
            values = state.ParsedData.Select(item => item.Preview.Length).DefaultIfEmpty(0).ToArray(),
            generatedAt = DateTimeOffset.UtcNow
        };
    }

    private static string BuildTablePreview(AgentReportTable table)
    {
        return string.Join(Environment.NewLine, table.Rows.Take(5).Select(row =>
            "- " + string.Join("; ", table.Columns.Select(column =>
                $"{column}: {(row.TryGetValue(column, out var value) ? value : string.Empty)}"))));
    }

    private static string BuildMarkdownTable(AgentReportTable table)
    {
        if (table.Columns.Count == 0)
        {
            return "_无列数据_";
        }

        var builder = new StringBuilder();
        builder.AppendLine("| " + string.Join(" | ", table.Columns.Select(EscapeMarkdownCell)) + " |");
        builder.AppendLine("| " + string.Join(" | ", table.Columns.Select(_ => "---")) + " |");
        foreach (var row in table.Rows.Take(20))
        {
            builder.AppendLine("| " + string.Join(" | ", table.Columns.Select(column =>
                EscapeMarkdownCell(row.TryGetValue(column, out var value) ? value : string.Empty))) + " |");
        }

        return builder.ToString();
    }

    private static string BuildHtmlTable(AgentReportTable table)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<table>");
        builder.AppendLine("<thead><tr>");
        foreach (var column in table.Columns)
        {
            builder.AppendLine($"<th>{EscapeHtml(column)}</th>");
        }

        builder.AppendLine("</tr></thead>");
        builder.AppendLine("<tbody>");
        foreach (var row in table.Rows.Take(50))
        {
            builder.AppendLine("<tr>");
            foreach (var column in table.Columns)
            {
                builder.AppendLine($"<td>{EscapeHtml(row.TryGetValue(column, out var value) ? value : string.Empty)}</td>");
            }

            builder.AppendLine("</tr>");
        }

        builder.AppendLine("</tbody></table>");
        return builder.ToString();
    }

    private static string BuildCsv(AgentReportTable table)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", table.Columns.Select(EscapeCsv)));
        foreach (var row in table.Rows)
        {
            builder.AppendLine(string.Join(",", table.Columns.Select(column =>
                EscapeCsv(row.TryGetValue(column, out var value) ? value : string.Empty))));
        }

        return builder.ToString();
    }

    private static string? BuildTextPreview(string fileName, byte[] content)
    {
        var extension = Path.GetExtension(fileName);
        if (!new[] { ".txt", ".md", ".csv", ".json", ".html" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var length = Math.Min(content.Length, 4096);
        return Encoding.UTF8.GetString(content, 0, length);
    }

    private static ArtifactType ResolveArtifactType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".csv" => ArtifactType.Csv,
            ".json" => ArtifactType.Json,
            ".html" => ArtifactType.Html,
            ".pdf" => ArtifactType.Pdf,
            ".pptx" => ArtifactType.Pptx,
            ".xlsx" => ArtifactType.Xlsx,
            ".md" => ArtifactType.Markdown,
            _ => ArtifactType.Log
        };
    }

    private static bool TryParseNumber(string value, out double number)
    {
        return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number)
               || double.TryParse(value, out number);
    }

    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "upload.bin" : sanitized;
    }

    private static string SafeFileStem(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(SafeFileName(fileName));
        return string.IsNullOrWhiteSpace(stem) ? "data" : stem;
    }

    private static string SafeShaPrefix(string sha256)
    {
        return sha256.Length <= 12 ? sha256 : sha256[..12];
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string EscapeMarkdownCell(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
    }

    private static string EscapeHtml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}

internal sealed class AgentTaskRunState
{
    public List<AgentUploadSummary> Uploads { get; } = [];

    public List<AgentParsedData> ParsedData { get; } = [];

    public List<AgentReportTable> Tables { get; } = [];

    public List<AgentRagResult> RagResults { get; } = [];

    public string? CloudReadonlySummary { get; set; }
}

internal sealed record AgentUploadSummary(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSize,
    string Sha256,
    string? StoragePath,
    string? Preview);

internal sealed record AgentParsedData(string FileName, string Format, string Preview);

internal sealed record AgentRagResult(
    Guid KnowledgeBaseId,
    int DocumentId,
    string DocumentName,
    int ChunkIndex,
    double Score,
    bool IsLowConfidence,
    string? LowConfidenceReason,
    string Text);
