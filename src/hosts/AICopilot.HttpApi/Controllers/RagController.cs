using AICopilot.HttpApi.Infrastructure;
using AICopilot.RagService.Commands.Documents;
using AICopilot.RagService.Commands.KnowledgeBases;
using AICopilot.RagService.Documents;
using AICopilot.RagService.EmbeddingModels;
using AICopilot.RagService.KnowledgeBases;
using AICopilot.RagService.Queries.KnowledgeBases;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace AICopilot.HttpApi.Controllers;

[Route("/api/rag")]
[Authorize]
public class RagController(ISender sender) : ApiControllerBase(sender)
{
    public const long MaxDocumentUploadBytes = 50_000_000;

    [HttpPost("embedding-model")]
    public async Task<IActionResult> CreateEmbeddingModel(CreateEmbeddingModelCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("embedding-model")]
    public async Task<IActionResult> UpdateEmbeddingModel(UpdateEmbeddingModelCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpDelete("embedding-model")]
    public async Task<IActionResult> DeleteEmbeddingModel(DeleteEmbeddingModelCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("embedding-model")]
    public async Task<IActionResult> GetEmbeddingModel([FromQuery] GetEmbeddingModelQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("embedding-model/list")]
    public async Task<IActionResult> GetListEmbeddingModels()
    {
        return ReturnResult(await Sender.Send(new GetListEmbeddingModelsQuery()));
    }

    [HttpPost("knowledge-base")]
    public async Task<IActionResult> CreateKnowledgeBase(CreateKnowledgeBaseCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("knowledge-base")]
    public async Task<IActionResult> UpdateKnowledgeBase(UpdateKnowledgeBaseCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpDelete("knowledge-base")]
    public async Task<IActionResult> DeleteKnowledgeBase(DeleteKnowledgeBaseCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("knowledge-base")]
    public async Task<IActionResult> GetKnowledgeBase([FromQuery] GetKnowledgeBaseQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("knowledge-base/list")]
    public async Task<IActionResult> GetListKnowledgeBases()
    {
        return ReturnResult(await Sender.Send(new GetListKnowledgeBasesQuery()));
    }

    [HttpPost("document")]
    [RequestSizeLimit(MaxDocumentUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxDocumentUploadBytes)]
    public async Task<IActionResult> UploadDocument([FromForm] Guid knowledgeBaseId, IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "File is required." });
        }

        if (file.Length > MaxDocumentUploadBytes)
        {
            return BadRequest(new { error = "File exceeds the 50 MB upload limit." });
        }

        await using var stream = file.OpenReadStream();
        var command = new UploadDocumentCommand(knowledgeBaseId, new FileUploadStream(file.FileName, stream));
        return ReturnResult(await Sender.Send(command));
    }

    [HttpDelete("document")]
    public async Task<IActionResult> DeleteDocument(DeleteDocumentCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("document/list")]
    public async Task<IActionResult> GetListDocuments([FromQuery] GetListDocumentsQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search(SearchKnowledgeBaseQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }
}
