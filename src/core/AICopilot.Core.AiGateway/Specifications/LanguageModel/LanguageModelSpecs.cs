using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.LanguageModel;

public sealed class LanguageModelByIdSpec : Specification<Aggregates.LanguageModel.LanguageModel>
{
    public LanguageModelByIdSpec(LanguageModelId id)
    {
        FilterCondition = model => model.Id == id;
    }
}

public sealed class LanguageModelsOrderedSpec : Specification<Aggregates.LanguageModel.LanguageModel>
{
    public LanguageModelsOrderedSpec()
    {
        SetOrderBy(model => model.Provider);
    }
}
