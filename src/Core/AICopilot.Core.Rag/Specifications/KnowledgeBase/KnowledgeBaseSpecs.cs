using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.Rag.Specifications.KnowledgeBase;

public sealed class KnowledgeBaseByIdWithDocumentsSpec : Specification<Aggregates.KnowledgeBase.KnowledgeBase>
{
    public KnowledgeBaseByIdWithDocumentsSpec(Guid id)
    {
        FilterCondition = knowledgeBase => knowledgeBase.Id == id;
        AddInclude(knowledgeBase => knowledgeBase.Documents);
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
