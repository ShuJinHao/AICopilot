using AICopilot.RagService.Commands.Documents;
using AICopilot.Services.Contracts;
using MediatR;

namespace AICopilot.RagService.Documents;

public sealed class RagDocumentUploadBridge(IMediator mediator) : IRagDocumentUploadBridge
{
    public async Task<RagDocumentUploadBridgeResult> UploadAsync(
        RagDocumentUploadBridgeRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new UploadDocumentCommand(
                request.KnowledgeBaseId,
                new FileUploadStream(request.FileName, request.Stream, request.ContentType, request.FileSize),
                request.Classification,
                request.SourceType,
                request.IsSanitized),
            cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            var errors = result.Errors is null ? "知识库上传失败。" : string.Join("; ", result.Errors);
            throw new InvalidOperationException(errors);
        }

        return new RagDocumentUploadBridgeResult(result.Value.Id, result.Value.Status);
    }
}
