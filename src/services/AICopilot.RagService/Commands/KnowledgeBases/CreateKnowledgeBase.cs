using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.RagService.Commands.KnowledgeBases;

public record CreatedKnowledgeBaseDto(Guid Id, string Name);

[AuthorizeRequirement("Rag.CreateKnowledgeBase")]
public record CreateKnowledgeBaseCommand(
    string Name,
    string Description,
    Guid EmbeddingModelId,
    string? AccessScope = null) : ICommand<Result<CreatedKnowledgeBaseDto>>;

public class CreateKnowledgeBaseCommandHandler(
    IRepository<KnowledgeBase> kbRepo,
    IReadRepository<EmbeddingModel> modelRepo,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser)
    : ICommandHandler<CreateKnowledgeBaseCommand, Result<CreatedKnowledgeBaseDto>>
{
    public async Task<Result<CreatedKnowledgeBaseDto>> Handle(
        CreateKnowledgeBaseCommand request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        // 1. 校验嵌入模型是否存在
        // 知识库必须绑定一个具体的 Embedding 模型，因为这决定了向量的维度
        var embeddingModelId = new EmbeddingModelId(request.EmbeddingModelId);
        var embeddingModel = await modelRepo.GetByIdAsync(embeddingModelId, cancellationToken);
        if (embeddingModel == null)
        {
            return Result.NotFound("指定的嵌入模型不存在");
        }

        // 2. 创建实体
        if (!TryParseAccessScope(request.AccessScope, out var accessScope))
        {
            return Result.Invalid("Knowledge base access scope is invalid.");
        }

        var kb = new KnowledgeBase(request.Name, request.Description, embeddingModelId, userId, accessScope);

        // 3. 持久化
        kbRepo.Add(kb);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "Rag.CreateKnowledgeBase",
                "KnowledgeBase",
                kb.Id.ToString(),
                kb.Name,
                AuditResults.Succeeded,
                $"Created knowledge base: {kb.Name}; embeddingModelId={kb.EmbeddingModelId}; accessScope={kb.AccessScope}.",
                ["name", "description", "embeddingModelId", "ownerUserId", "accessScope"]),
            cancellationToken);
        await kbRepo.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreatedKnowledgeBaseDto(kb.Id, kb.Name));
    }

    private static bool TryParseAccessScope(string? value, out KnowledgeBaseAccessScope accessScope)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            accessScope = KnowledgeBaseAccessScope.OwnerOnly;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out accessScope) && Enum.IsDefined(accessScope);
    }
}
