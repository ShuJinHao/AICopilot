using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.RagService.KnowledgeBases;

internal static class KnowledgeBaseAccessPolicy
{
    public static bool IsAdmin(ICurrentUser currentUser)
    {
        return string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanRead(KnowledgeBase knowledgeBase, Guid userId, bool isAdmin)
    {
        if (isAdmin)
        {
            return true;
        }

        return knowledgeBase.AccessScope == KnowledgeBaseAccessScope.AuthenticatedUsers ||
               knowledgeBase.OwnerUserId == userId;
    }

    public static bool CanWrite(KnowledgeBase knowledgeBase, Guid userId, bool isAdmin)
    {
        return isAdmin || knowledgeBase.OwnerUserId == userId;
    }
}

public sealed class KnowledgeBaseAccessChecker(IReadRepository<KnowledgeBase> repository)
    : IKnowledgeBaseAccessChecker
{
    public async Task<bool> CanReadAsync(
        Guid knowledgeBaseId,
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var knowledgeBase = await repository.GetByIdAsync(new KnowledgeBaseId(knowledgeBaseId), cancellationToken);
        return knowledgeBase is not null && KnowledgeBaseAccessPolicy.CanRead(knowledgeBase, userId, isAdmin);
    }

    public async Task<bool> CanWriteAsync(
        Guid knowledgeBaseId,
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var knowledgeBase = await repository.GetByIdAsync(new KnowledgeBaseId(knowledgeBaseId), cancellationToken);
        return knowledgeBase is not null && KnowledgeBaseAccessPolicy.CanWrite(knowledgeBase, userId, isAdmin);
    }
}
