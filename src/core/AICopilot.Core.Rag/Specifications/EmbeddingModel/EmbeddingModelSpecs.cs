using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.Rag.Specifications.EmbeddingModel;

public sealed class EmbeddingModelByIdSpec : Specification<Aggregates.EmbeddingModel.EmbeddingModel>
{
    public EmbeddingModelByIdSpec(Guid id)
    {
        FilterCondition = model => model.Id == id;
    }
}

public sealed class EmbeddingModelsOrderedSpec : Specification<Aggregates.EmbeddingModel.EmbeddingModel>
{
    public EmbeddingModelsOrderedSpec()
    {
        SetOrderBy(model => model.Name);
    }
}
