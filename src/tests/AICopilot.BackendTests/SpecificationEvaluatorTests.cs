using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.EntityFrameworkCore.Specification;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.BackendTests;

public class SpecificationEvaluatorTests
{
    [Fact]
    public void GetQuery_applies_filter_order_and_paging()
    {
        var models = new[]
            {
                new LanguageModel("OpenAI", "gpt-4.1-mini", "https://example.test", "key", new ModelParameters { MaxTokens = 4096 }),
                new LanguageModel("OpenAI", "gpt-4.1", "https://example.test", "key", new ModelParameters { MaxTokens = 8192 }),
                new LanguageModel("Local", "qwen", "https://example.test", "key", new ModelParameters { MaxTokens = 2048 })
            }
            .AsQueryable();

        var result = SpecificationEvaluator
            .GetQuery(models, new OpenAiLanguageModelsSpec(skip: 0, take: 1))
            .ToList();

        result.Should().ContainSingle();
        result[0].Name.Should().Be("gpt-4.1");
    }

    private sealed class OpenAiLanguageModelsSpec : Specification<LanguageModel>
    {
        public OpenAiLanguageModelsSpec(int skip, int take)
        {
            FilterCondition = model => model.Provider == "OpenAI";
            SetOrderBy(model => model.Name);
            SetPaging(skip, take);
        }
    }
}
