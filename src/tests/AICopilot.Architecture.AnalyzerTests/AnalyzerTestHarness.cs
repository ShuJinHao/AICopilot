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
        .Where(path => !Path.GetFileNameWithoutExtension(path).StartsWith("AICopilot.", StringComparison.Ordinal))
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToImmutableArray<MetadataReference>();

    public static async Task<ImmutableArray<Diagnostic>> GetArchitectureDiagnosticsAsync(
        string assemblyName,
        IReadOnlyCollection<string> sources,
        params string[] directProjectReferenceNames) =>
        await GetArchitectureDiagnosticsAsync(
            assemblyName,
            sources,
            [],
            directProjectReferenceNames);

    public static async Task<ImmutableArray<Diagnostic>> GetArchitectureDiagnosticsAsync(
        string assemblyName,
        IReadOnlyCollection<string> sources,
        IReadOnlyCollection<FixtureAssemblyReference> fixtureReferences,
        params string[] directProjectReferenceNames)
    {
        var fixtureSources = sources
            .Select((source, index) => new AnalyzerFixtureSource($"Fixture{index + 1}.cs", source))
            .ToArray();
        return await GetArchitectureDiagnosticsAsync(
            assemblyName,
            fixtureSources,
            fixtureReferences,
            directProjectReferenceNames);
    }

    public static async Task<ImmutableArray<Diagnostic>> GetArchitectureDiagnosticsAsync(
        string assemblyName,
        IReadOnlyCollection<AnalyzerFixtureSource> sources,
        IReadOnlyCollection<FixtureAssemblyReference> fixtureReferences,
        params string[] directProjectReferenceNames)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compiledFixtureReferences = new List<MetadataReference>();
        foreach (var fixtureReference in fixtureReferences)
        {
            var reference = CompileFixtureReference(
                fixtureReference,
                parseOptions,
                compiledFixtureReferences);
            compiledFixtureReferences.Add(reference);
        }

        var syntaxTrees = sources
            .Select(source => CSharpSyntaxTree.ParseText(
                SourceText.From(source.Source),
                parseOptions,
                path: source.Path))
            .ToArray();
        Compilation compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            PlatformReferences.AddRange(compiledFixtureReferences),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver generatorDriver = CSharpGeneratorDriver.Create(
                new AICopilotArchitectureEffectGenerator())
            .WithUpdatedParseOptions(parseOptions);
        generatorDriver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out compilation,
            out var generatorDiagnostics);
        Assert.DoesNotContain(
            generatorDiagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

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

    public static ImmutableArray<Diagnostic> GetCompilerDiagnostics(
        string assemblyName,
        IReadOnlyCollection<string> sources,
        IReadOnlyCollection<FixtureAssemblyReference> fixtureReferences)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compiledFixtureReferences = new List<MetadataReference>();
        foreach (var fixtureReference in fixtureReferences)
        {
            compiledFixtureReferences.Add(CompileFixtureReference(
                fixtureReference,
                parseOptions,
                compiledFixtureReferences));
        }

        Compilation compilation = CSharpCompilation.Create(
            assemblyName,
            sources.Select((source, index) => CSharpSyntaxTree.ParseText(
                SourceText.From(source),
                parseOptions,
                path: $"CompilerFixture{index + 1}.cs")),
            PlatformReferences.AddRange(compiledFixtureReferences),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver generatorDriver = CSharpGeneratorDriver.Create(
                new AICopilotArchitectureEffectGenerator())
            .WithUpdatedParseOptions(parseOptions);
        generatorDriver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out compilation,
            out _);
        return compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
    }

    private static MetadataReference CompileFixtureReference(
        FixtureAssemblyReference fixtureReference,
        CSharpParseOptions parseOptions,
        IReadOnlyCollection<MetadataReference> priorFixtureReferences)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            SourceText.From(fixtureReference.Source),
            parseOptions,
            path: $"{fixtureReference.AssemblyName}.cs");
        Compilation compilation = CSharpCompilation.Create(
            fixtureReference.AssemblyName,
            [syntaxTree],
            PlatformReferences.AddRange(priorFixtureReferences),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver generatorDriver = CSharpGeneratorDriver.Create(
                new AICopilotArchitectureEffectGenerator())
            .WithUpdatedParseOptions(parseOptions);
        generatorDriver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out compilation,
            out var generatorDiagnostics);
        Assert.DoesNotContain(
            generatorDiagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            $"Fixture reference '{fixtureReference.AssemblyName}' must compile:{Environment.NewLine}{string.Join(Environment.NewLine, emit.Diagnostics)}");
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private sealed class ProjectReferenceAdditionalText(string path) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(string.Empty);
    }
}

internal sealed record AnalyzerFixtureSource(string Path, string Source);

internal sealed record FixtureAssemblyReference(string AssemblyName, string Source);
