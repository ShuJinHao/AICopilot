using System.Text.Json;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.AI;

namespace AICopilot.AiRuntime;

internal static class RuntimeToolAdapter
{
    public static ChatOptions ToChatOptions(AiChatOptions options)
    {
        return new ChatOptions
        {
            Instructions = options.Instructions,
            Temperature = options.Temperature,
            MaxOutputTokens = options.MaxOutputTokens,
            Tools = options.Tools.Select(ToAiTool).ToList()
        };
    }

    private static AITool ToAiTool(AiToolDefinition definition)
    {
        AIFunction function = definition.Method is not null
            ? AIFunctionFactory.Create(
                definition.Method,
                definition.Target,
                new AIFunctionFactoryOptions
                {
                    Name = definition.Name,
                    Description = definition.Description
                })
            : new RuntimeAIFunction(definition);

        if (!definition.RequiresApproval)
        {
            return function;
        }

#pragma warning disable MEAI001
        return new ApprovalRequiredAIFunction(function);
#pragma warning restore MEAI001
    }

    private sealed class RuntimeAIFunction(AiToolDefinition definition) : AIFunction
    {
        private static readonly JsonElement EmptySchema = CreateEmptySchema();

        public override string Name => definition.Name;

        public override string Description => definition.Description ?? string.Empty;

        public override JsonElement JsonSchema => definition.JsonSchema ?? EmptySchema;

        public override JsonElement? ReturnJsonSchema => definition.ReturnJsonSchema;

        public override IReadOnlyDictionary<string, object?> AdditionalProperties => definition.AdditionalProperties;

        public override JsonSerializerOptions JsonSerializerOptions => JsonSerializerOptions.Web;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            if (definition.InvokeAsync is null)
            {
                throw new InvalidOperationException($"Tool '{definition.Name}' does not have an invoker.");
            }

            var context = new AiToolInvocationContext(
                arguments.ToDictionary(item => item.Key, item => (object?)item.Value, StringComparer.OrdinalIgnoreCase),
                arguments.Services,
                arguments.Context?.ToDictionary(item => item.Key, item => (object?)item.Value));

            return await definition.InvokeAsync(context, cancellationToken);
        }

        private static JsonElement CreateEmptySchema()
        {
            using var document = JsonDocument.Parse("{}");
            return document.RootElement.Clone();
        }
    }
}
