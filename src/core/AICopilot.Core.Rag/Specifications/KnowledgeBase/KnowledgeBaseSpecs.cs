using AICopilot.SharedKernel.Specification;
using AICopilot.Core.Rag.Ids;

namespace AICopilot.Core.Rag.Specifications.KnowledgeBase;

public sealed class KnowledgeBaseByIdWithDocumentsSpec : Specification<Aggregates.KnowledgeBase.KnowledgeBase>
{
    public KnowledgeBaseByIdWithDocumentsSpec(KnowledgeBaseId id)
    {
        FilterCondition = knowledgeBase => knowledgeBase.Id == id;
        AddInclude(knowledgeBase => knowledgeBase.Documents);
    }
}

public sealed class KnowledgeBaseByDocumentIdWithDocumentChunksSpec : Specification<Aggregates.KnowledgeBase.KnowledgeBase>
{
    public KnowledgeBaseByDocumentIdWithDocumentChunksSpec(DocumentId documentId)
    {
        FilterCondition = knowledgeBase => knowledgeBase.Documents.Any(document => document.Id == documentId);
        AddInclude("Documents.Chunks");
    }
}

public sealed class KnowledgeBasesOrderedWithDocumentsSpec : Specification<Aggregates.KnowledgeBase.KnowledgeBase>
{
    public KnowledgeBasesOrderedWithDocumentsSpec()
    {
        AddInclude(knowledgeBase => knowledgeBase.Documents);
        SetOrderBy(knowledgeBase => knowledgeBase.Name);
    }
}

public sealed class KnowledgeBasesByNamesSpec : Specification<Aggregates.KnowledgeBase.KnowledgeBase>
{
    public KnowledgeBasesByNamesSpec(IReadOnlyCollection<string> names)
    {
        FilterCondition = knowledgeBase => names.Contains(knowledgeBase.Name);
    }
}
