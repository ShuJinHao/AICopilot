using System.Collections.Immutable;
using AICopilot.Architecture.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace AICopilot.Architecture.AnalyzerTests;

internal static class AnalyzerTestHarness
{
    private static readonly ImmutableArray<MetadataReference> PlatformReferences =
        (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToImmutableArray<MetadataReference>();

    public static async Task<ImmutableArray<Diagnostic>> GetArchitectureDiagnosticsAsync(
        string assemblyName,
        IReadOnlyCollection<string> sources,
        params string[] directProjectReferenceNames)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var syntaxTrees = sources
            .Select((source, index) => CSharpSyntaxTree.ParseText(
                SourceText.From(source),
                parseOptions,
                path: $"Fixture{index + 1}.cs"))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            PlatformReferences,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var compilerErrors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(
            compilerErrors.Length == 0,
            $"Semantic fixture must compile before analyzer execution:{Environment.NewLine}{string.Join(Environment.NewLine, compilerErrors.Select(item => item.ToString()))}");

        var additionalFiles = directProjectReferenceNames
            .Select(name => (AdditionalText)new ProjectReferenceAdditionalText($"/fixtures/{name}/{name}.csproj"))
            .ToImmutableArray();
        var options = new AnalyzerOptions(additionalFiles);
        var diagnostics = await compilation
            .WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new AICopilotArchitectureAnalyzer()),
                options)
            .GetAnalyzerDiagnosticsAsync();

        return diagnostics
            .Where(diagnostic => diagnostic.Id.StartsWith("AIARCH", StringComparison.Ordinal))
            .OrderBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToImmutableArray();
    }

    private sealed class ProjectReferenceAdditionalText(string path) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(string.Empty);
    }
}
