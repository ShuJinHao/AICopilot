using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace AICopilot.Architecture.Analyzers;

[Generator(LanguageNames.CSharp)]
public sealed class AICopilotArchitectureEffectGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(
            context.CompilationProvider,
            static (sourceContext, compilation) =>
            {
                var source = AICopilotArchitectureAnalyzer.GenerateCrossProjectEffectSource(
                    compilation,
                    sourceContext.CancellationToken);
                if (source is not null)
                {
                    sourceContext.AddSource(
                        "AICopilot.Architecture.EffectSummary.g.cs",
                        SourceText.From(source, Encoding.UTF8));
                }
            });
    }
}
