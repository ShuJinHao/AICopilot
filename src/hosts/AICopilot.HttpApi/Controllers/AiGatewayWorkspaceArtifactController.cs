using AICopilot.AiGatewayService.Uploads;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.HttpApi.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace AICopilot.HttpApi.Controllers;

[Route("/api/aigateway")]
[Authorize]
public class AiGatewayWorkspaceArtifactController(ISender sender) : ApiControllerBase(sender)
{
    private const long MaxAiGatewayUploadBytes = 50_000_000;

    [HttpPost("upload")]
    [RequestSizeLimit(MaxAiGatewayUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAiGatewayUploadBytes)]
    public async Task<IActionResult> Upload(
        [FromForm] string scope,
        IFormFile? file,
        [FromForm] Guid? sessionId = null,
        [FromForm] Guid? agentTaskId = null)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "File is required." });
        }

        if (file.Length > MaxAiGatewayUploadBytes)
        {
            return BadRequest(new { error = "File exceeds the 50 MB upload limit." });
        }

        await using var stream = file.OpenReadStream();
        var command = new UploadRecordCommand(
            scope,
            new AiGatewayUploadStream(
                file.FileName,
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                file.Length,
            stream),
            sessionId,
            agentTaskId);
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("upload/list")]
    public async Task<IActionResult> GetUploadRecords([FromQuery] GetListUploadRecordsQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("workspace/{code}")]
    public async Task<IActionResult> GetWorkspace(string code)
    {
        return ReturnResult(await Sender.Send(new GetArtifactWorkspaceQuery(code)));
    }

    [HttpGet("workspace-settings")]
    public async Task<IActionResult> GetWorkspaceSettings()
    {
        return ReturnResult(await Sender.Send(new GetArtifactWorkspaceSettingsQuery()));
    }

    [HttpPost("workspace/{code}/submit-final-review")]
    public async Task<IActionResult> SubmitFinalReview(string code)
    {
        return ReturnResult(await Sender.Send(new SubmitFinalReviewCommand(code)));
    }

    [HttpPost("workspace/{code}/finalize")]
    public async Task<IActionResult> FinalizeWorkspace(string code)
    {
        return ReturnResult(await Sender.Send(new FinalizeArtifactWorkspaceCommand(code)));
    }

    [HttpGet("artifact/{id:guid}/download")]
    public async Task<IActionResult> DownloadArtifact(Guid id)
    {
        var result = await Sender.Send(new DownloadArtifactQuery(id));
        if (!result.IsSuccess || result.Value is null)
        {
            return ReturnResult(result);
        }

        return File(result.Value.Stream, result.Value.MimeType, result.Value.FileName);
    }

    [HttpGet("artifact/{id:guid}/content")]
    public async Task<IActionResult> GetArtifactContent(Guid id)
    {
        return ReturnResult(await Sender.Send(new GetArtifactContentQuery(id)));
    }

    [HttpGet("artifact/{id:guid}/preview")]
    public async Task<IActionResult> GetAgentArtifactPreview(Guid id)
    {
        return ReturnResult(await Sender.Send(new GetAgentArtifactPreviewQuery(id)));
    }

    [HttpPut("artifact/{id:guid}/content")]
    public async Task<IActionResult> UpdateArtifactContent(Guid id, UpdateArtifactContentRequest request)
    {
        return ReturnResult(await Sender.Send(new UpdateArtifactContentCommand(
            id,
            request.Content,
            request.ExpectedVersion,
            request.Comment)));
    }

    [HttpPost("artifact/{id:guid}/revision-comment")]
    public async Task<IActionResult> CreateArtifactRevisionComment(Guid id, CreateArtifactRevisionCommentRequest request)
    {
        return ReturnResult(await Sender.Send(new CreateArtifactRevisionCommentCommand(
            id,
            request.Comment,
            request.ExpectedVersion)));
    }

    [HttpPost("artifact/{id:guid}/regenerate-draft")]
    public async Task<IActionResult> RegenerateDraftArtifact(Guid id, RegenerateDraftArtifactRequest request)
    {
        return ReturnResult(await Sender.Send(new RegenerateDraftArtifactCommand(
            id,
            request.Content,
            request.ExpectedVersion,
            request.Comment)));
    }

    [HttpPost("artifact/{id:guid}/submit-final-approval")]
    public async Task<IActionResult> SubmitArtifactForFinalApproval(Guid id)
    {
        return ReturnResult(await Sender.Send(new SubmitArtifactForFinalApprovalCommand(id)));
    }

    [HttpGet("artifact/{id:guid}/versions")]
    public async Task<IActionResult> GetArtifactVersions(Guid id)
    {
        return ReturnResult(await Sender.Send(new GetArtifactVersionsQuery(id)));
    }

    [HttpGet("artifact/{id:guid}/versions/{version:int}/download")]
    public async Task<IActionResult> DownloadArtifactVersion(Guid id, int version)
    {
        var result = await Sender.Send(new DownloadArtifactVersionQuery(id, version));
        if (!result.IsSuccess || result.Value is null)
        {
            return ReturnResult(result);
        }

        return File(result.Value.Stream, result.Value.MimeType, result.Value.FileName);
    }

    [HttpGet("artifact/{id:guid}/versions/{fromVersion:int}/diff/{toVersion:int}")]
    public async Task<IActionResult> GetArtifactVersionDiff(Guid id, int fromVersion, int toVersion)
    {
        return ReturnResult(await Sender.Send(new GetArtifactVersionDiffQuery(id, fromVersion, toVersion)));
    }

    [HttpPost("artifact/{id:guid}/versions/{version:int}/restore")]
    public async Task<IActionResult> RestoreArtifactVersion(Guid id, int version, RestoreArtifactVersionRequest request)
    {
        return ReturnResult(await Sender.Send(new RestoreArtifactVersionCommand(
            id,
            version,
            request.ExpectedVersion,
            request.Comment)));
    }
}
