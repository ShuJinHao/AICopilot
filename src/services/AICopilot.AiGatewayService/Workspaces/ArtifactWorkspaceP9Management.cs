using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed record AgentArtifactPreviewDto(
    Guid ArtifactId,
    string Name,
    string ArtifactType,
    string PreviewKind,
    string ArtifactStatus,
    int ArtifactVersion,
    string RelativePath,
    long FileSize,
    string MimeType,
    string? SourceMode,
    string? Boundary,
    bool IsSimulation,
    bool IsSandbox,
    string? SourceLabel,
    string? QueryHash,
    string? ResultHash,
    int RowCount,
    bool IsTruncated,
    string? Content,
    IReadOnlyCollection<string> Columns,
    IReadOnlyCollection<IReadOnlyDictionary<string, string>> Rows,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ArtifactRevisionCommentDto(
    Guid ArtifactId,
    int ArtifactVersion,
    string CommentHash,
    DateTimeOffset CreatedAt);

public sealed record RegenerateDraftArtifactRequest(string Content, int ExpectedVersion, string? Comment = null);

public sealed record CreateArtifactRevisionCommentRequest(string Comment, int ExpectedVersion);

[AuthorizeRequirement("AiGateway.GetWorkspace")]
public sealed record GetAgentArtifactPreviewQuery(Guid Id) : IQuery<Result<AgentArtifactPreviewDto>>;

[AuthorizeRequirement("AiGateway.EditArtifact")]
public sealed record CreateArtifactRevisionCommentCommand(Guid Id, string Comment, int ExpectedVersion)
    : ICommand<Result<ArtifactRevisionCommentDto>>;

[AuthorizeRequirement("AiGateway.EditArtifact")]
public sealed record RegenerateDraftArtifactCommand(Guid Id, string Content, int ExpectedVersion, string? Comment)
    : ICommand<Result<ArtifactWorkspaceDto>>;

[AuthorizeRequirement("AiGateway.SubmitFinalReview")]
public sealed record SubmitArtifactForFinalApprovalCommand(Guid Id) : ICommand<Result<ArtifactWorkspaceDto>>;

public sealed class GetAgentArtifactPreviewQueryHandler(
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<AgentTask> taskRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    AgentAuditRecorder auditRecorder,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : IQueryHandler<GetAgentArtifactPreviewQuery, Result<AgentArtifactPreviewDto>>
{
    public async Task<Result<AgentArtifactPreviewDto>> Handle(
        GetAgentArtifactPreviewQuery request,
        CancellationToken cancellationToken)
    {
        var access = await ArtifactVersioningAccess.LoadArtifactForReadAsync(
            workspaceRepository,
            taskRepository,
            approvalRepository,
            currentUser,
            identityAccessService,
            request.Id,
            AgentApprovalPermissions.GetWorkspace,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var context = access.Value!;
        if (context.Artifact.Status is ArtifactStatus.Deleted or ArtifactStatus.Rejected)
        {
            return Result.Invalid($"Artifact status {context.Artifact.Status} cannot be previewed.");
        }

        var preview = await ArtifactPreviewBuilder.BuildAsync(
            fileStore,
            context.Workspace.WorkspaceCode,
            context.Artifact,
            cancellationToken);
        if (!preview.IsSuccess)
        {
            return Result.From(preview);
        }

        await auditRecorder.RecordArtifactPreviewedAsync(
            context.Task,
            context.Workspace,
            context.Artifact,
            preview.Value!.PreviewKind,
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return preview;
    }
}

public sealed class CreateArtifactRevisionCommentCommandHandler(
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<AgentTask> taskRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    AgentAuditRecorder auditRecorder,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : ICommandHandler<CreateArtifactRevisionCommentCommand, Result<ArtifactRevisionCommentDto>>
{
    public async Task<Result<ArtifactRevisionCommentDto>> Handle(
        CreateArtifactRevisionCommentCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Comment))
        {
            return Result.Invalid("Revision comment is required.");
        }

        var access = await ArtifactVersioningAccess.LoadArtifactForOwnerEditAsync(
            workspaceRepository,
            taskRepository,
            currentUser,
            identityAccessService,
            request.Id,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var validation = await ArtifactWorkspaceP9Policy.ValidateDraftMutationAsync(
            approvalRepository,
            access.Value!,
            request.ExpectedVersion,
            allowBinaryArtifact: true,
            cancellationToken);
        if (!validation.IsSuccess)
        {
            return Result.From(validation);
        }

        var commentHash = ArtifactWorkspaceP9Policy.ComputeHash(request.Comment);
        await auditRecorder.RecordArtifactRevisionCommentAsync(
            access.Value!.Task,
            access.Value.Workspace,
            access.Value.Artifact,
            commentHash,
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return Result.Success(new ArtifactRevisionCommentDto(
            request.Id,
            access.Value.Artifact.Version,
            commentHash,
            DateTimeOffset.UtcNow));
    }
}

public sealed class RegenerateDraftArtifactCommandHandler(
    IRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<AgentTask> taskRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    AgentAuditRecorder auditRecorder,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : ICommandHandler<RegenerateDraftArtifactCommand, Result<ArtifactWorkspaceDto>>
{
    public async Task<Result<ArtifactWorkspaceDto>> Handle(
        RegenerateDraftArtifactCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            return Result.Invalid("Regenerated artifact content is required.");
        }

        if (Encoding.UTF8.GetByteCount(request.Content) > ArtifactVersioningPolicy.MaxContentBytes)
        {
            return Result.Invalid($"Regenerated artifact content exceeds the {ArtifactVersioningPolicy.MaxContentBytes} byte limit.");
        }

        var access = await ArtifactVersioningAccess.LoadArtifactForOwnerEditAsync(
            workspaceRepository,
            taskRepository,
            currentUser,
            identityAccessService,
            request.Id,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var context = access.Value!;
        var validation = await ArtifactVersioningPolicy.ValidateEditWindowAsync(
            approvalRepository,
            context.Workspace,
            context.Task,
            context.Artifact,
            request.ExpectedVersion,
            cancellationToken);
        if (!validation.IsSuccess)
        {
            return Result.From(validation);
        }

        var oldVersion = context.Artifact.Version;
        var archive = await ArtifactVersioningFiles.ArchiveCurrentVersionAsync(
            fileStore,
            context.Workspace.WorkspaceCode,
            context.Artifact,
            request.Comment,
            cancellationToken);
        if (!archive.IsSuccess)
        {
            return Result.From(archive);
        }

        var written = await fileStore.WriteTextAsync(
            context.Workspace.WorkspaceCode,
            context.Artifact.RelativePath,
            request.Content,
            context.Artifact.MimeType,
            cancellationToken);
        var sha256 = ArtifactVersioningFiles.ComputeSha256(request.Content);
        context.Artifact.AddVersion(written.RelativePath, written.FileSize, DateTimeOffset.UtcNow);

        workspaceRepository.Update(context.Workspace);
        await auditRecorder.RecordArtifactUpdatedAsync(
            context.Task,
            context.Workspace,
            context.Artifact,
            oldVersion,
            context.Artifact.Version,
            sha256,
            request.Comment,
            cancellationToken);
        await workspaceRepository.SaveChangesAsync(cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        var files = await fileStore.ListAsync(context.Workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(context.Workspace, context.Task, files));
    }
}

public sealed class SubmitArtifactForFinalApprovalCommandHandler(
    IRepository<ArtifactWorkspace> workspaceRepository,
    IRepository<AgentTask> taskRepository,
    IRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    AgentAuditRecorder auditRecorder,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : ICommandHandler<SubmitArtifactForFinalApprovalCommand, Result<ArtifactWorkspaceDto>>
{
    public async Task<Result<ArtifactWorkspaceDto>> Handle(
        SubmitArtifactForFinalApprovalCommand request,
        CancellationToken cancellationToken)
    {
        var access = await ArtifactVersioningAccess.LoadArtifactForReadAsync(
            workspaceRepository,
            taskRepository,
            approvalRepository,
            currentUser,
            identityAccessService,
            request.Id,
            AgentApprovalPermissions.SubmitFinalReview,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var context = access.Value!;
        if (!context.IsOwner)
        {
            return Result.NotFound();
        }

        var workspace = context.Workspace;
        var task = context.Task;
        if (workspace.Status == ArtifactWorkspaceStatus.Finalized)
        {
            return Result.Invalid("Workspace is already finalized.");
        }

        if (context.Artifact.Status == ArtifactStatus.Final)
        {
            return Result.Invalid("Artifact is already final.");
        }

        if (task.Status is not AgentTaskStatus.WorkspaceReady and not AgentTaskStatus.WaitingFinalApproval)
        {
            return Result.Invalid("Only workspace-ready tasks can submit final review.");
        }

        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id),
            cancellationToken);
        var finalApproval = approvals.FirstOrDefault(item =>
            item.ApprovalType == AgentApprovalType.FinalOutput &&
            string.Equals(item.TargetId, workspace.WorkspaceCode, StringComparison.Ordinal));

        if (finalApproval?.Status == AgentApprovalStatus.Rejected)
        {
            return Result.Invalid("Workspace final output approval was rejected.");
        }

        if (finalApproval?.Status == AgentApprovalStatus.Approved)
        {
            return Result.Invalid("Workspace final output is already approved; call finalize to publish final artifacts.");
        }

        var now = DateTimeOffset.UtcNow;
        if (finalApproval is null)
        {
            finalApproval = new ApprovalRequest(
                task.Id,
                AgentApprovalType.FinalOutput,
                workspace.WorkspaceCode,
                currentUser.Id!.Value,
                now);
            approvalRepository.Add(finalApproval);
            await auditRecorder.RecordFinalReviewSubmittedAsync(task, workspace, finalApproval, cancellationToken);
        }

        if (task.Status == AgentTaskStatus.WorkspaceReady)
        {
            task.WaitForFinalApproval(now);
        }

        taskRepository.Update(task);
        workspaceRepository.Update(workspace);
        await workspaceRepository.SaveChangesAsync(cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        var files = await fileStore.ListAsync(workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(workspace, task, files));
    }
}

internal static class ArtifactWorkspaceP9Policy
{
    public static async Task<Result> ValidateDraftMutationAsync(
        IReadRepository<ApprovalRequest> approvalRepository,
        ArtifactVersioningContext context,
        int expectedVersion,
        bool allowBinaryArtifact,
        CancellationToken cancellationToken)
    {
        if (context.Artifact.Status is ArtifactStatus.Final or ArtifactStatus.Deleted or ArtifactStatus.Rejected)
        {
            return Result.Invalid($"Artifact status {context.Artifact.Status} cannot be revised.");
        }

        if (!allowBinaryArtifact && ArtifactVersioningPolicy.ValidateTextArtifact(context.Artifact) is { } textError)
        {
            return Result.Invalid(textError);
        }

        if (expectedVersion != context.Artifact.Version)
        {
            return Result.Invalid("Artifact version has changed. Refresh and retry with the latest expectedVersion.");
        }

        if (context.Workspace.Status == ArtifactWorkspaceStatus.Finalized)
        {
            return Result.Invalid("Finalized workspaces cannot be revised.");
        }

        if (await WorkspaceAccess.HasFinalOutputApprovalAsync(
                approvalRepository,
                context.Task.Id,
                context.Workspace.WorkspaceCode,
                cancellationToken))
        {
            return Result.Invalid("Artifact drafts are locked after final review submission.");
        }

        return context.Task.Status != AgentTaskStatus.WorkspaceReady
            ? Result.Invalid("Artifact drafts can only be revised while the task is WorkspaceReady and before final review submission.")
            : Result.Success();
    }

    public static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}

internal static class ArtifactPreviewBuilder
{
    private const int MaxTextPreviewBytes = 2 * 1024 * 1024;
    private const int MaxBinaryPreviewBytes = 20 * 1024 * 1024;
    private static readonly Regex PdfPageRegex = new(@"/Type\s*/Page\b", RegexOptions.Compiled);

    public static async Task<Result<AgentArtifactPreviewDto>> BuildAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        CancellationToken cancellationToken)
    {
        var previewKind = ResolvePreviewKind(artifact);
        var content = await ResolveContentAsync(fileStore, workspaceCode, artifact, previewKind, cancellationToken);
        if (!content.IsSuccess)
        {
            return Result.From(content);
        }

        var value = content.Value!;
        return Result.Success(new AgentArtifactPreviewDto(
            artifact.Id.Value,
            artifact.Name,
            artifact.ArtifactType.ToString(),
            previewKind,
            ResolveArtifactStatus(artifact),
            artifact.Version,
            artifact.RelativePath,
            artifact.FileSize,
            artifact.MimeType,
            artifact.SourceMode,
            artifact.Boundary,
            artifact.IsSimulation,
            artifact.IsSandbox,
            artifact.SourceLabel,
            artifact.QueryHash,
            artifact.ResultHash,
            artifact.RowCount,
            artifact.IsTruncated,
            value.Content,
            value.Columns,
            value.Rows,
            value.Metadata));
    }

    private static async Task<Result<PreviewContent>> ResolveContentAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        string previewKind,
        CancellationToken cancellationToken)
    {
        if (previewKind is "markdown" or "html" or "json" or "chart" or "table")
        {
            var content = await ArtifactVersioningFiles.ReadTextAsync(
                fileStore,
                workspaceCode,
                artifact.RelativePath,
                artifact.MimeType,
                MaxTextPreviewBytes,
                cancellationToken);
            if (!content.IsSuccess)
            {
                return Result.From(content);
            }

            if (previewKind == "table")
            {
                var table = ParseCsvPreview(content.Value!);
                return Result.Success(new PreviewContent(
                    content.Value,
                    table.Columns,
                    table.Rows,
                    BuildBaseMetadata(artifact)));
            }

            return Result.Success(new PreviewContent(
                content.Value,
                [],
                [],
                BuildBaseMetadata(artifact)));
        }

        var binary = await ReadBinaryAsync(fileStore, workspaceCode, artifact, cancellationToken);
        if (!binary.IsSuccess)
        {
            return Result.From(binary);
        }

        var metadata = BuildBaseMetadata(artifact);
        IReadOnlyCollection<string> columns = [];
        IReadOnlyCollection<IReadOnlyDictionary<string, string>> rows = [];
        if (artifact.ArtifactType == ArtifactType.Pdf)
        {
            var text = Encoding.Latin1.GetString(binary.Value!);
            metadata["pageCount"] = PdfPageRegex.Matches(text).Count.ToString(CultureInfo.InvariantCulture);
        }
        else if (artifact.ArtifactType == ArtifactType.Pptx)
        {
            metadata["pageCount"] = CountZipEntries(binary.Value!, @"^ppt/slides/slide\d+\.xml$").ToString(CultureInfo.InvariantCulture);
        }
        else if (artifact.ArtifactType == ArtifactType.Xlsx)
        {
            var table = TryParseXlsxPreview(binary.Value!);
            columns = table.Columns;
            rows = table.Rows;
            metadata["previewRowCount"] = rows.Count.ToString(CultureInfo.InvariantCulture);
        }

        return Result.Success(new PreviewContent(null, columns, rows, metadata));
    }

    private static async Task<Result<byte[]>> ReadBinaryAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        CancellationToken cancellationToken)
    {
        var file = await fileStore.OpenReadAsync(
            workspaceCode,
            artifact.RelativePath,
            artifact.MimeType,
            cancellationToken);
        if (file is null)
        {
            return Result.NotFound("Artifact file does not exist.");
        }

        await using var stream = file.Stream;
        if (file.FileSize > MaxBinaryPreviewBytes)
        {
            return Result.Invalid($"Artifact binary preview exceeds the {MaxBinaryPreviewBytes} byte read limit.");
        }

        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return Result.Success(buffer.ToArray());
    }

    private static Dictionary<string, string> BuildBaseMetadata(Artifact artifact)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["downloadUrl"] = $"/api/aigateway/artifact/{artifact.Id.Value}/download",
            ["fileSize"] = artifact.FileSize.ToString(CultureInfo.InvariantCulture),
            ["mimeType"] = artifact.MimeType,
            ["finalizedAt"] = artifact.FinalizedAt?.ToString("O") ?? string.Empty,
            ["approvalStatus"] = artifact.Status == ArtifactStatus.Final ? "Approved" : "Pending",
            ["artifactVersion"] = artifact.Version.ToString(CultureInfo.InvariantCulture),
            ["artifactStatus"] = ResolveArtifactStatus(artifact)
        };
    }

    private static PreviewTable ParseCsvPreview(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Take(11)
            .ToArray();
        if (lines.Length == 0)
        {
            return new PreviewTable([], []);
        }

        var columns = SplitCsvLine(lines[0]);
        var rows = lines.Skip(1)
            .Select(line =>
            {
                var values = SplitCsvLine(line);
                return (IReadOnlyDictionary<string, string>)columns
                    .Select((column, index) => new { column, value = index < values.Count ? values[index] : string.Empty })
                    .ToDictionary(item => item.column, item => item.value, StringComparer.OrdinalIgnoreCase);
            })
            .ToArray();
        return new PreviewTable(columns, rows);
    }

    private static IReadOnlyList<string> SplitCsvLine(string line)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
            {
                builder.Append('"');
                i++;
                continue;
            }

            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                values.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        values.Add(builder.ToString());
        return values;
    }

    private static int CountZipEntries(byte[] content, string pattern)
    {
        using var stream = new MemoryStream(content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return archive.Entries.Count(entry => regex.IsMatch(entry.FullName));
    }

    private static PreviewTable TryParseXlsxPreview(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var sharedStrings = ReadSharedStrings(archive);
            var sheet = archive.Entries
                .Where(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (sheet is null)
            {
                return new PreviewTable([], []);
            }

            using var sheetStream = sheet.Open();
            var document = XDocument.Load(sheetStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var rows = document.Descendants(ns + "row").Take(11).ToArray();
            if (rows.Length == 0)
            {
                return new PreviewTable([], []);
            }

            var firstRowCells = rows[0].Elements(ns + "c").ToArray();
            var columns = firstRowCells
                .Select((cell, index) => ResolveCellText(cell, sharedStrings, ns) is { Length: > 0 } value ? value : $"Column{index + 1}")
                .ToArray();
            var previewRows = rows.Skip(1)
                .Select(row =>
                {
                    var values = row.Elements(ns + "c").Select(cell => ResolveCellText(cell, sharedStrings, ns)).ToArray();
                    return (IReadOnlyDictionary<string, string>)columns
                        .Select((column, index) => new { column, value = index < values.Length ? values[index] : string.Empty })
                        .ToDictionary(item => item.column, item => item.value, StringComparer.OrdinalIgnoreCase);
                })
                .ToArray();
            return new PreviewTable(columns, previewRows);
        }
        catch (InvalidDataException)
        {
            return new PreviewTable([], []);
        }
    }

    private static string[] ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Descendants(ns + "si")
            .Select(item => string.Concat(item.Descendants(ns + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static string ResolveCellText(XElement cell, IReadOnlyList<string> sharedStrings, XNamespace ns)
    {
        var raw = cell.Element(ns + "v")?.Value ?? string.Empty;
        if (string.Equals(cell.Attribute("t")?.Value, "s", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex) &&
            sharedIndex >= 0 &&
            sharedIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedIndex];
        }

        return raw;
    }

    private static string ResolvePreviewKind(Artifact artifact)
    {
        return artifact.ArtifactType switch
        {
            ArtifactType.Chart => "chart",
            ArtifactType.Json => "json",
            ArtifactType.Csv => "table",
            ArtifactType.Markdown => "markdown",
            ArtifactType.Html => "html",
            ArtifactType.Pdf => "pdf",
            ArtifactType.Pptx => "pptx",
            ArtifactType.Xlsx => "spreadsheet",
            _ => "download"
        };
    }

    private static string ResolveArtifactStatus(Artifact artifact)
    {
        return artifact.Status switch
        {
            ArtifactStatus.Draft => "Draft",
            ArtifactStatus.Reviewing or ArtifactStatus.Approved => "FinalPendingApproval",
            ArtifactStatus.Final => "Final",
            ArtifactStatus.Deleted => "Deleted",
            _ => artifact.Status.ToString()
        };
    }

    private sealed record PreviewContent(
        string? Content,
        IReadOnlyCollection<string> Columns,
        IReadOnlyCollection<IReadOnlyDictionary<string, string>> Rows,
        Dictionary<string, string> Metadata);

    private sealed record PreviewTable(
        IReadOnlyCollection<string> Columns,
        IReadOnlyCollection<IReadOnlyDictionary<string, string>> Rows);
}
