using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.Uploads;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentRuntimeFileInputToolService(
    IReadRepository<UploadRecord> uploadRepository,
    IAgentArtifactWorkspaceService workspaceService,
    IFileStorageService fileStorage,
    IAgentTableFileParser tableFileParser)
{
    public async Task<object> ReadUploadedFilesAsync(
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
                            sourceMetadata: null,
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

        return new
        {
            status = "completed",
            resultType = "upload-summary",
            itemCount = state.Uploads.Count
        };
    }

    public async Task<object> ParseTableFileAsync(
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
            var json = JsonSerializer.Serialize(table, AgentRuntimeJson.Options);
            await workspaceService.WriteDraftTextArtifactAsync(
                workspace,
                ArtifactType.Json,
                $"{fileStem}.normalized.json",
                $"data/{fileStem}.normalized.json",
                json,
                "application/json",
                step.Id,
                sourceMetadata: null,
                cancellationToken);
            await workspaceService.WriteDraftTextArtifactAsync(
                workspace,
                ArtifactType.Csv,
                $"{fileStem}.normalized.csv",
                $"data/{fileStem}.normalized.csv",
                BuildCsv(table),
                "text/csv",
                step.Id,
                sourceMetadata: null,
                cancellationToken);
        }

        return new
        {
            status = "completed",
            resultType = "table-summary",
            itemCount = state.Tables.Count,
            rowCount = state.Tables.Sum(table => table.Rows.Count)
        };
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

    private static string BuildTablePreview(AgentReportTable table)
    {
        return string.Join(Environment.NewLine, table.Rows.Take(5).Select(row =>
            "- " + string.Join("; ", table.Columns.Select(column =>
                $"{column}: {(row.TryGetValue(column, out var value) ? value : string.Empty)}"))));
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

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
