using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

if (args.Length is < 3 or > 4)
{
    Console.Error.WriteLine("Usage: AICopilot.CompatibilitySymbolProbe <repository-root> <inventory-json> <output-json> [--discover]");
    return 2;
}

var repositoryRoot = Path.GetFullPath(args[0]);
var inventoryPath = Path.GetFullPath(args[1]);
var outputPath = Path.GetFullPath(args[2]);
var discover = args.Length == 4 && string.Equals(args[3], "--discover", StringComparison.Ordinal);
if (!Directory.Exists(repositoryRoot) || !File.Exists(inventoryPath))
{
    Console.Error.WriteLine("Repository root or compatibility inventory does not exist.");
    return 2;
}

using var inventoryDocument = JsonDocument.Parse(File.ReadAllText(inventoryPath));
var inventoryRoot = inventoryDocument.RootElement;
var inventoryItems = ReadInventoryItems(inventoryRoot).ToArray();
var csharpSymbols = inventoryRoot.TryGetProperty("csharpSymbols", out var csharpSymbolsElement)
    ? csharpSymbolsElement
    : default;
var producerSymbols = ReadStringMap(csharpSymbols, "producers");
var callerSymbols = ReadStringArrayMap(csharpSymbols, "callerScans");
var candidateDispositions = ReadStringMap(csharpSymbols, "candidateDispositions");
var sourceFiles = EnumerateSourceFiles(repositoryRoot).ToArray();
var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
var syntaxTrees = sourceFiles
    .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path))
    .Append(CSharpSyntaxTree.ParseText(
        "global using System; global using System.Collections.Generic; global using System.Linq; " +
        "global using System.Threading; global using System.Threading.Tasks; " +
        "namespace Microsoft.Extensions.Options { " +
        "public interface IOptions<out T> { T Value { get; } } " +
        "public sealed class OptionsBuilder<T> { } " +
        "public static class OptionsBuilderConfigurationExtensions { " +
        "public static OptionsBuilder<T> BindConfiguration<T>(this OptionsBuilder<T> builder, string section) => builder; } } " +
        "namespace Microsoft.Extensions.DependencyInjection { " +
        "public interface IServiceCollection { } " +
        "public interface IServiceScopeFactory { IServiceScope CreateScope(); } " +
        "public interface IServiceScope : System.IDisposable { System.IServiceProvider ServiceProvider { get; } } " +
        "public static class ServiceCollectionServiceExtensions { " +
        "public static Microsoft.Extensions.Options.OptionsBuilder<T> AddOptions<T>(this IServiceCollection services) => new(); " +
        "public static IServiceCollection AddSingleton<T>(this IServiceCollection services) => services; " +
        "public static IServiceCollection AddSingleton<TService,TImplementation>(this IServiceCollection services) where TImplementation : TService => services; " +
        "public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, System.Func<System.IServiceProvider,TService> factory) => services; " +
        "public static T GetRequiredService<T>(this System.IServiceProvider provider) => default!; " +
        "public static System.Collections.Generic.IEnumerable<T> GetServices<T>(this System.IServiceProvider provider) => []; } } " +
        "namespace Microsoft.Extensions.Hosting { " +
        "public interface IHostApplicationBuilder { Microsoft.Extensions.DependencyInjection.IServiceCollection Services { get; } } " +
        "public interface IHostEnvironment { bool IsDevelopment(); } }",
        parseOptions,
        Path.Combine(repositoryRoot, "<compatibility-global-usings>.g.cs")))
    .ToArray();
var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
    .Select(path => MetadataReference.CreateFromFile(path))
    .ToArray();
var compilation = CSharpCompilation.Create(
    "AICopilot.CompatibilityInventory",
    syntaxTrees,
    trustedPlatformAssemblies,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

var declarations = CollectDeclarations(repositoryRoot, compilation, syntaxTrees).ToArray();
var references = CollectReferences(repositoryRoot, compilation, syntaxTrees).ToArray();
var declarationLookup = declarations
    .GroupBy(item => item.SymbolId, StringComparer.Ordinal)
    .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

var producerChecks = new List<ProducerCheck>();
var callerCounts = new List<CallerCount>();
var discoveries = new List<Discovery>();
foreach (var item in inventoryItems)
{
    if (item.Producer.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
    {
        if (discover)
        {
            var matches = declarations
                .Where(declaration => string.Equals(declaration.Path, item.Producer.Path, StringComparison.Ordinal) &&
                    declaration.DeclarationText.Contains(item.Producer.Contains, StringComparison.Ordinal))
                .Select(declaration => declaration.SymbolId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            discoveries.Add(new Discovery(item.Id, "producer", "primary", item.Producer.Contains, matches));
        }
        else
        {
            if (!producerSymbols.TryGetValue(item.Id, out var producerSymbolId) ||
                string.IsNullOrWhiteSpace(producerSymbolId))
            {
                throw new InvalidOperationException($"{item.Id} C# producer requires an exact symbolId.");
            }

            var count = declarationLookup.TryGetValue(producerSymbolId, out var candidates)
                ? candidates.Count(candidate => string.Equals(candidate.Path, item.Producer.Path, StringComparison.Ordinal))
                : 0;
            producerChecks.Add(new ProducerCheck(item.Id, item.Producer.Path, producerSymbolId, count));
        }
    }

    foreach (var scan in item.CallerScans.Where(scan => scan.Extensions.Contains(".cs", StringComparer.OrdinalIgnoreCase)))
    {
        if (scan.CountMode.Length > 0 &&
            !string.Equals(scan.CountMode, "distinct-caller-member", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{item.Id}/{scan.Id} caller scan has unsupported countMode '{scan.CountMode}'.");
        }

        if (discover)
        {
            var matches = DiscoverCallerSymbols(references, scan)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            discoveries.Add(new Discovery(item.Id, "caller", scan.Id, scan.Contains, matches));
            continue;
        }

        var callerKey = $"{item.Id}/{scan.Id}";
        if (!callerSymbols.TryGetValue(callerKey, out var symbolIds) ||
            symbolIds.Length == 0 ||
            symbolIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException($"{item.Id}/{scan.Id} C# caller scan requires non-empty exact symbolIds.");
        }

        var targetSymbols = symbolIds.ToHashSet(StringComparer.Ordinal);
        var matchingReferences = references
            .Where(reference =>
                reference.IsCallerEvidence &&
                targetSymbols.Contains(reference.SymbolId) &&
                IsInRoots(reference.Path, scan.Roots) &&
                !scan.ExcludePaths.Contains(reference.Path, StringComparer.Ordinal))
            .ToArray();
        var count = matchingReferences
            .Select(reference => (reference.Path, reference.Position, reference.SymbolId))
            .Distinct()
            .Count();
        if (string.Equals(scan.CountMode, "distinct-caller-member", StringComparison.Ordinal))
        {
            var unresolvedCallerMembers = matchingReferences
                .Where(reference => string.IsNullOrWhiteSpace(reference.EnclosingMemberSymbolId))
                .OrderBy(reference => reference.Path, StringComparer.Ordinal)
                .ThenBy(reference => reference.Line)
                .ToArray();
            if (unresolvedCallerMembers.Length > 0)
            {
                var unresolvedEvidence = string.Join(
                    ", ",
                    unresolvedCallerMembers.Select(reference => $"{reference.Path}:{reference.Line}"));
                throw new InvalidOperationException(
                    $"{callerKey} caller scan countMode 'distinct-caller-member' requires every matched reference to resolve an enclosing member; unresolved=[{unresolvedEvidence}].");
            }

            count = matchingReferences
                .Select(reference => reference.EnclosingMemberSymbolId)
                .Distinct(StringComparer.Ordinal)
                .Count();
        }
        callerCounts.Add(new CallerCount(item.Id, scan.Id, count));
    }
}

var signalPattern = new Regex(
    "(?:Alias|Adapter|Wrapper|Fallback|Compatibility|Legacy|Shadow|DualWrite|Obsolete)",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
var referencesBySymbol = references
    .GroupBy(reference => reference.SymbolId, StringComparer.Ordinal)
    .ToDictionary(
        group => group.Key,
        group => group.ToArray(),
        StringComparer.Ordinal);
var typeDeclarationsBySymbol = declarations
    .Where(declaration => declaration.IsType)
    .GroupBy(declaration => declaration.SymbolId, StringComparer.Ordinal)
    .ToDictionary(
        group => group.Key,
        group => group.ToArray(),
        StringComparer.Ordinal);
var reachableMemberIds = ComputeReachableMemberIds(declarations, referencesBySymbol);
var signals = declarations
    .Where(declaration => declaration.IsCompatibilityCandidate &&
        (signalPattern.IsMatch(declaration.Name) || declaration.IsObsolete))
    .Select(declaration => new CandidateSignal(
        declaration.Path,
        declaration.Line,
        declaration.DeclarationText,
        declaration.SymbolId,
        CountExecutableReferences(
            declaration,
            referencesBySymbol,
            typeDeclarationsBySymbol,
            reachableMemberIds)))
    .OrderBy(signal => signal.Path, StringComparer.Ordinal)
    .ThenBy(signal => signal.Line)
    .ThenBy(signal => signal.SymbolId, StringComparer.Ordinal)
    .ToArray();
if (!discover)
{
    var csharpItemIds = inventoryItems
        .Where(item => item.Producer.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        .Select(item => item.Id)
        .ToHashSet(StringComparer.Ordinal);
    if (!producerSymbols.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(csharpItemIds))
    {
        throw new InvalidOperationException("C# producer symbol roster must exactly match all C# inventory items.");
    }

    var csharpCallerKeys = inventoryItems
        .SelectMany(item => item.CallerScans
            .Where(scan => scan.Extensions.Contains(".cs", StringComparer.OrdinalIgnoreCase))
            .Select(scan => $"{item.Id}/{scan.Id}"))
        .ToHashSet(StringComparer.Ordinal);
    if (!callerSymbols.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(csharpCallerKeys))
    {
        throw new InvalidOperationException("C# caller symbol roster must exactly match all C# caller scans.");
    }

    var signalIds = signals.Select(signal => signal.SymbolId).ToHashSet(StringComparer.Ordinal);
    if (!candidateDispositions.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(signalIds))
    {
        var missing = signalIds.Except(candidateDispositions.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var stale = candidateDispositions.Keys.Except(signalIds, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        throw new InvalidOperationException(
            $"C# compatibility signal disposition roster differs: missing=[{string.Join(", ", missing)}], stale=[{string.Join(", ", stale)}].");
    }
    var unknownDispositionItems = candidateDispositions.Values
        .Where(itemId => !csharpItemIds.Contains(itemId))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
    if (unknownDispositionItems.Length > 0)
    {
        throw new InvalidOperationException(
            $"C# compatibility dispositions reference unknown items: [{string.Join(", ", unknownDispositionItems)}].");
    }

    var itemLookup = inventoryItems.ToDictionary(item => item.Id, StringComparer.Ordinal);
    var migrationSignalPattern = new Regex(
        "(?:Legacy|Compatibility|Obsolete|Shadow|DualWrite)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    foreach (var signal in signals)
    {
        if (signal.ReferenceCount <= 0)
        {
            throw new InvalidOperationException(
                $"C# compatibility signal '{signal.SymbolId}' has no exact production references; physically delete it.");
        }

        var itemId = candidateDispositions[signal.SymbolId];
        var item = itemLookup[itemId];
        var hasExactCandidateEvidence = item.CandidateEvidence.Any(evidence =>
            string.Equals(evidence.Path, signal.Path, StringComparison.Ordinal) &&
            signal.Text.Contains(evidence.Contains, StringComparison.Ordinal));
        if (!hasExactCandidateEvidence)
        {
            throw new InvalidOperationException(
                $"C# compatibility signal '{signal.SymbolId}' is not bound to exact candidate evidence for '{itemId}'.");
        }

        if (migrationSignalPattern.IsMatch(signal.Text) &&
            !itemId.StartsWith("AI-COMPAT-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"C# migration signal '{signal.SymbolId}' must use an AI-COMPAT disposition, not '{itemId}'.");
        }
    }
}

var output = new ProbeOutput(
    ProducerChecks: producerChecks,
    CallerCounts: callerCounts,
    CandidateSignals: signals,
    Discoveries: discoveries);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
return 0;

static IEnumerable<string> EnumerateSourceFiles(string repositoryRoot)
{
    var roots = new[]
    {
        "src/core",
        "src/hosts",
        "src/infrastructure",
        "src/services",
        "src/shared",
        "src/testing",
        "scripts/tests/tools"
    };
    foreach (var relativeRoot in roots)
    {
        var root = Path.Combine(repositoryRoot, relativeRoot);
        if (!Directory.Exists(root))
        {
            continue;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = path.Replace('\\', '/');
            if (normalized.Contains("/bin/", StringComparison.Ordinal) ||
                normalized.Contains("/obj/", StringComparison.Ordinal) ||
                normalized.Contains("/Migrations/", StringComparison.Ordinal))
            {
                continue;
            }
            yield return Path.GetFullPath(path);
        }
    }
}

static IEnumerable<InventoryItem> ReadInventoryItems(JsonElement root)
{
    foreach (var propertyName in new[] { "items", "ordinaryAbstractions" })
    {
        foreach (var element in root.GetProperty(propertyName).EnumerateArray())
        {
            var id = element.GetProperty("id").GetString() ?? string.Empty;
            var producerElement = element.GetProperty("producer");
            var producer = new Producer(
                producerElement.GetProperty("path").GetString() ?? string.Empty,
                producerElement.GetProperty("contains").GetString() ?? string.Empty);
            var scanElements = element.TryGetProperty("callerScan", out var singleScan)
                ? new[] { singleScan }
                : element.GetProperty("callerScans").EnumerateArray().ToArray();
            var scans = scanElements.Select((scan, index) => new CallerScan(
                scan.TryGetProperty("id", out var scanId) ? scanId.GetString() ?? "primary" : "primary",
                scan.GetProperty("contains").GetString() ?? string.Empty,
                scan.GetProperty("roots").EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray(),
                scan.GetProperty("extensions").EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray(),
                scan.TryGetProperty("countMode", out var countMode)
                    ? countMode.GetString() ?? string.Empty
                    : string.Empty,
                scan.TryGetProperty("excludePaths", out var excluded)
                    ? excluded.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray()
                    : []))
                .ToArray();
            var candidateEvidence = element.TryGetProperty("candidateEvidence", out var evidenceArray)
                ? evidenceArray.EnumerateArray()
                    .Where(evidence =>
                        string.Equals(
                            Path.GetExtension(evidence.GetProperty("path").GetString() ?? string.Empty),
                            ".cs",
                            StringComparison.OrdinalIgnoreCase))
                    .Select(evidence => new CandidateEvidence(
                        evidence.GetProperty("path").GetString() ?? string.Empty,
                        evidence.GetProperty("contains").GetString() ?? string.Empty))
                    .ToArray()
                : [];
            yield return new InventoryItem(id, producer, scans, candidateEvidence);
        }
    }
}

static Dictionary<string, string> ReadStringMap(JsonElement parent, string propertyName)
{
    if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(propertyName, out var property))
    {
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }
    return property.EnumerateObject().ToDictionary(
        entry => entry.Name,
        entry => entry.Value.GetString() ?? string.Empty,
        StringComparer.Ordinal);
}

static Dictionary<string, string[]> ReadStringArrayMap(JsonElement parent, string propertyName)
{
    if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(propertyName, out var property))
    {
        return new Dictionary<string, string[]>(StringComparer.Ordinal);
    }
    return property.EnumerateObject().ToDictionary(
        entry => entry.Name,
        entry => entry.Value.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray(),
        StringComparer.Ordinal);
}

static IEnumerable<Declaration> CollectDeclarations(
    string repositoryRoot,
    CSharpCompilation compilation,
    IEnumerable<SyntaxTree> syntaxTrees)
{
    foreach (var tree in syntaxTrees)
    {
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        foreach (var node in root.DescendantNodesAndSelf().Where(IsDeclarationNode))
        {
            var symbol = model.GetDeclaredSymbol(node);
            var symbolId = GetSymbolId(symbol);
            if (symbol is null || string.IsNullOrWhiteSpace(symbolId))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(repositoryRoot, tree.FilePath).Replace('\\', '/');
            var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var declarationText = GetDeclarationText(node);
            var candidate = symbol is INamedTypeSymbol or IMethodSymbol or IFieldSymbol or IPropertySymbol or IEventSymbol;
            var obsolete = symbol.GetAttributes().Any(attribute =>
                string.Equals(attribute.AttributeClass?.ToDisplayString(), "System.ObsoleteAttribute", StringComparison.Ordinal));
            yield return new Declaration(
                relativePath,
                line,
                symbol.Name,
                symbolId,
                node.SpanStart,
                node.Span.End,
                declarationText,
                candidate,
                obsolete,
                symbol is INamedTypeSymbol,
                symbol is INamedTypeSymbol ? string.Empty : GetSymbolId(symbol.ContainingType),
                GetExecutableReferenceSymbolIds(symbol));
        }
    }
}

static bool IsDeclarationNode(SyntaxNode node) => node is
    BaseTypeDeclarationSyntax or
    DelegateDeclarationSyntax or
    MethodDeclarationSyntax or
    ConstructorDeclarationSyntax or
    PropertyDeclarationSyntax or
    EventDeclarationSyntax or
    LocalFunctionStatementSyntax ||
    node is VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax or EventFieldDeclarationSyntax };

static string GetDeclarationText(SyntaxNode node)
{
    var text = node switch
    {
        BaseTypeDeclarationSyntax declaration when declaration.OpenBraceToken != default =>
            declaration.SyntaxTree.GetText().ToString(TextSpan.FromBounds(declaration.SpanStart, declaration.OpenBraceToken.SpanStart)),
        MethodDeclarationSyntax declaration when declaration.Body is not null =>
            declaration.SyntaxTree.GetText().ToString(TextSpan.FromBounds(declaration.SpanStart, declaration.Body.OpenBraceToken.SpanStart)),
        MethodDeclarationSyntax declaration when declaration.ExpressionBody is not null =>
            declaration.SyntaxTree.GetText().ToString(TextSpan.FromBounds(declaration.SpanStart, declaration.ExpressionBody.SpanStart)),
        ConstructorDeclarationSyntax declaration when declaration.Body is not null =>
            declaration.SyntaxTree.GetText().ToString(TextSpan.FromBounds(declaration.SpanStart, declaration.Body.OpenBraceToken.SpanStart)),
        PropertyDeclarationSyntax declaration when declaration.AccessorList is not null =>
            declaration.SyntaxTree.GetText().ToString(TextSpan.FromBounds(declaration.SpanStart, declaration.AccessorList.OpenBraceToken.SpanStart)),
        LocalFunctionStatementSyntax declaration when declaration.Body is not null =>
            declaration.SyntaxTree.GetText().ToString(TextSpan.FromBounds(declaration.SpanStart, declaration.Body.OpenBraceToken.SpanStart)),
        _ => node.ToString(),
    };
    return Regex.Replace(text.Trim(), "\\s+", " ");
}

static string[] GetExecutableReferenceSymbolIds(ISymbol symbol)
{
    var symbols = new HashSet<string>(StringComparer.Ordinal);
    Add(symbol);
    if (symbol is IMethodSymbol method)
    {
        foreach (var explicitImplementation in method.ExplicitInterfaceImplementations)
        {
            Add(explicitImplementation);
        }

        for (var overridden = method.OverriddenMethod;
             overridden is not null;
             overridden = overridden.OverriddenMethod)
        {
            Add(overridden);
        }

        foreach (var @interface in method.ContainingType.AllInterfaces)
        {
            foreach (var member in @interface.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.ContainingType.FindImplementationForInterfaceMember(member) is IMethodSymbol implementation &&
                    SymbolEqualityComparer.Default.Equals(
                        implementation.OriginalDefinition,
                        method.OriginalDefinition))
                {
                    Add(member);
                }
            }
        }
    }

    return symbols.Order(StringComparer.Ordinal).ToArray();

    void Add(ISymbol candidate)
    {
        var id = GetSymbolId(candidate);
        if (!string.IsNullOrWhiteSpace(id))
        {
            symbols.Add(id);
        }
    }
}

static IEnumerable<SymbolReference> CollectReferences(
    string repositoryRoot,
    CSharpCompilation compilation,
    IEnumerable<SyntaxTree> syntaxTrees)
{
    foreach (var tree in syntaxTrees)
    {
        var model = compilation.GetSemanticModel(tree);
        var relativePath = Path.GetRelativePath(repositoryRoot, tree.FilePath).Replace('\\', '/');
        var root = tree.GetRoot();
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var context = GetReferenceContext(model, invocation);
            var target = model.GetSymbolInfo(invocation).Symbol ??
                         model.GetSymbolInfo(invocation).CandidateSymbols.FirstOrDefault();
            foreach (var symbol in GetInvocationReferenceSymbols(target))
            {
                var symbolId = GetSymbolId(symbol);
                if (string.IsNullOrWhiteSpace(symbolId))
                {
                    continue;
                }
                yield return new SymbolReference(
                    relativePath,
                    invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    invocation.SpanStart,
                    invocation.Expression.ToString(),
                    symbolId,
                    IsCallerEvidence: true,
                    context.MemberSymbolId,
                    context.TypeSymbolId);
            }
        }

        foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (memberAccess.Parent is InvocationExpressionSyntax)
            {
                continue;
            }
            var symbol = model.GetSymbolInfo(memberAccess).Symbol ??
                         model.GetSymbolInfo(memberAccess).CandidateSymbols.FirstOrDefault();
            if (symbol is not (IFieldSymbol or IPropertySymbol or IEventSymbol))
            {
                continue;
            }
            var context = GetReferenceContext(model, memberAccess);
            foreach (var referencedSymbol in GetMemberReferenceSymbols(symbol))
            {
                var symbolId = GetSymbolId(referencedSymbol);
                if (!string.IsNullOrWhiteSpace(symbolId))
                {
                    yield return new SymbolReference(
                        relativePath,
                        memberAccess.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        memberAccess.SpanStart,
                        memberAccess.ToString(),
                        symbolId,
                        IsCallerEvidence: true,
                        context.MemberSymbolId,
                        context.TypeSymbolId);
                }
            }
        }

        foreach (var objectCreation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var context = GetReferenceContext(model, objectCreation);
            var position = objectCreation.SpanStart;
            var line = objectCreation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var expression = objectCreation.Type.ToString();
            foreach (var symbol in new ISymbol?[]
                     {
                         model.GetSymbolInfo(objectCreation).Symbol ??
                         model.GetSymbolInfo(objectCreation).CandidateSymbols.FirstOrDefault(),
                         model.GetTypeInfo(objectCreation).Type
                     })
            {
                var symbolId = GetSymbolId(symbol);
                if (!string.IsNullOrWhiteSpace(symbolId))
                {
                    yield return new SymbolReference(
                        relativePath,
                        line,
                        position,
                        expression,
                        symbolId,
                        IsCallerEvidence: true,
                        context.MemberSymbolId,
                        context.TypeSymbolId);
                }
            }
        }

        foreach (var node in root.DescendantNodes().Where(node =>
                     node is IdentifierNameSyntax or MemberBindingExpressionSyntax))
        {
            if (node.AncestorsAndSelf().Any(ancestor => ancestor is UsingDirectiveSyntax) ||
                IsInsideNameof(node) ||
                node.Ancestors().Any(ancestor => ancestor is MemberAccessExpressionSyntax))
            {
                continue;
            }

            var symbol = model.GetSymbolInfo(node).Symbol ??
                         model.GetSymbolInfo(node).CandidateSymbols.FirstOrDefault();
            if (symbol is not (IFieldSymbol or IPropertySymbol or IEventSymbol))
            {
                continue;
            }

            var context = GetReferenceContext(model, node);
            foreach (var referencedSymbol in GetMemberReferenceSymbols(symbol))
            {
                var symbolId = GetSymbolId(referencedSymbol);
                if (!string.IsNullOrWhiteSpace(symbolId))
                {
                    yield return new SymbolReference(
                        relativePath,
                        node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        node.SpanStart,
                        node.ToString(),
                        symbolId,
                        IsCallerEvidence: true,
                        context.MemberSymbolId,
                        context.TypeSymbolId);
                }
            }
        }
    }
}

static IEnumerable<ISymbol> GetInvocationReferenceSymbols(ISymbol? target)
{
    if (target is null)
    {
        yield break;
    }

    yield return target;
    if (target.ContainingType is not null)
    {
        yield return target.ContainingType;
    }
    if (target is IMethodSymbol method)
    {
        foreach (var typeArgument in method.TypeArguments.OfType<INamedTypeSymbol>())
        {
            yield return typeArgument;
        }
    }
}

static IEnumerable<ISymbol> GetMemberReferenceSymbols(ISymbol member)
{
    yield return member;
    if (member.ContainingType is not null)
    {
        yield return member.ContainingType;
    }
}

static ReferenceContext GetReferenceContext(SemanticModel model, SyntaxNode node)
{
    var declarationNode = node.Ancestors().FirstOrDefault(IsDeclarationNode);
    var symbol = declarationNode is null ? null : model.GetDeclaredSymbol(declarationNode);
    return new ReferenceContext(
        GetSymbolId(symbol),
        GetSymbolId(symbol?.ContainingType));
}

static int CountExecutableReferences(
    Declaration declaration,
    IReadOnlyDictionary<string, SymbolReference[]> referencesBySymbol,
    IReadOnlyDictionary<string, Declaration[]> typeDeclarationsBySymbol,
    IReadOnlySet<string> reachableMemberIds)
{
    var referenceSymbolIds = declaration.ReferenceSymbolIds.AsEnumerable();
    if (declaration.IsType && declaration.SymbolId.StartsWith("T:", StringComparison.Ordinal))
    {
        var typePrefix = declaration.SymbolId[2..];
        referenceSymbolIds = referenceSymbolIds.Concat(referencesBySymbol.Keys.Where(symbolId =>
            symbolId.Length > 2 &&
            symbolId[1] == ':' &&
            symbolId.AsSpan(2).StartsWith(typePrefix.AsSpan(), StringComparison.Ordinal) &&
            symbolId.Length > typePrefix.Length + 2 &&
            symbolId[typePrefix.Length + 2] is '.' or '+'));
    }

    var references = referenceSymbolIds
        .Distinct(StringComparer.Ordinal)
        .Where(referencesBySymbol.ContainsKey)
        .SelectMany(symbolId => referencesBySymbol[symbolId]);

    if (declaration.IsType &&
        typeDeclarationsBySymbol.TryGetValue(declaration.SymbolId, out var ownTypeDeclarations))
    {
        references = references.Where(reference =>
            !ownTypeDeclarations.Any(typeDeclaration => IsInside(reference, typeDeclaration)));
    }
    else
    {
        references = references.Where(reference =>
            !string.Equals(
                reference.EnclosingTypeSymbolId,
                declaration.ContainingTypeSymbolId,
                StringComparison.Ordinal) ||
            (!string.IsNullOrWhiteSpace(reference.EnclosingMemberSymbolId) &&
             reachableMemberIds.Contains(reference.EnclosingMemberSymbolId)));
    }

    return references
        .Select(reference => (reference.Path, reference.Position, reference.SymbolId))
        .Distinct()
        .Count();
}

static HashSet<string> ComputeReachableMemberIds(
    IReadOnlyCollection<Declaration> declarations,
    IReadOnlyDictionary<string, SymbolReference[]> referencesBySymbol)
{
    var memberDeclarations = declarations
        .Where(declaration => !declaration.IsType)
        .ToArray();
    var reachable = new HashSet<string>(StringComparer.Ordinal);
    var changed = true;
    while (changed)
    {
        changed = false;
        foreach (var declaration in memberDeclarations)
        {
            if (reachable.Contains(declaration.SymbolId))
            {
                continue;
            }

            var incomingReferences = declaration.ReferenceSymbolIds
                .Where(referencesBySymbol.ContainsKey)
                .SelectMany(symbolId => referencesBySymbol[symbolId]);
            var isReachable = incomingReferences.Any(reference =>
                !string.Equals(
                    reference.EnclosingTypeSymbolId,
                    declaration.ContainingTypeSymbolId,
                    StringComparison.Ordinal) ||
                (!string.IsNullOrWhiteSpace(reference.EnclosingMemberSymbolId) &&
                 reachable.Contains(reference.EnclosingMemberSymbolId)));
            if (isReachable)
            {
                changed |= reachable.Add(declaration.SymbolId);
            }
        }
    }

    return reachable;
}

static bool IsInside(SymbolReference reference, Declaration declaration) =>
    string.Equals(reference.Path, declaration.Path, StringComparison.Ordinal) &&
    reference.Position >= declaration.SpanStart &&
    reference.Position < declaration.SpanEnd;

static bool IsInsideNameof(SyntaxNode node) => node.AncestorsAndSelf()
    .OfType<InvocationExpressionSyntax>()
    .Any(invocation =>
        invocation.Expression is IdentifierNameSyntax identifier &&
        string.Equals(identifier.Identifier.ValueText, "nameof", StringComparison.Ordinal) &&
        invocation.ArgumentList.Span.Contains(node.Span));

static string GetSymbolId(ISymbol? symbol)
{
    if (symbol is IMethodSymbol method)
    {
        symbol = method.ReducedFrom ?? method.OriginalDefinition;
    }
    else
    {
        symbol = symbol?.OriginalDefinition;
    }
    return symbol?.GetDocumentationCommentId() ?? string.Empty;
}

static IEnumerable<string> DiscoverCallerSymbols(
    IEnumerable<SymbolReference> references,
    CallerScan scan)
{
    var normalized = Regex.Replace(scan.Contains, "\\s+", string.Empty);
    var isInvocation = normalized.EndsWith('(');
    var target = isInvocation ? normalized[..^1].TrimStart('.') : normalized;
    foreach (var reference in references)
    {
        if (!reference.IsCallerEvidence ||
            !IsInRoots(reference.Path, scan.Roots) ||
            scan.ExcludePaths.Contains(reference.Path, StringComparer.Ordinal))
        {
            continue;
        }
        var expression = Regex.Replace(reference.Expression, "\\s+", string.Empty);
        var matches = isInvocation
            ? string.Equals(expression, target, StringComparison.Ordinal) ||
                expression.EndsWith($".{target}", StringComparison.Ordinal)
            : expression.Contains(target, StringComparison.Ordinal);
        if (matches)
        {
            yield return reference.SymbolId;
        }
    }
}

static bool IsInRoots(string path, IEnumerable<string> roots) => roots.Any(root =>
    path.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal));

internal sealed record Producer(string Path, string Contains);
internal sealed record CandidateEvidence(string Path, string Contains);
internal sealed record CallerScan(
    string Id,
    string Contains,
    string[] Roots,
    string[] Extensions,
    string CountMode,
    string[] ExcludePaths);
internal sealed record InventoryItem(
    string Id,
    Producer Producer,
    CallerScan[] CallerScans,
    CandidateEvidence[] CandidateEvidence);
internal sealed record Declaration(
    string Path,
    int Line,
    string Name,
    string SymbolId,
    int SpanStart,
    int SpanEnd,
    string DeclarationText,
    bool IsCompatibilityCandidate,
    bool IsObsolete,
    bool IsType,
    string ContainingTypeSymbolId,
    string[] ReferenceSymbolIds);
internal sealed record SymbolReference(
    string Path,
    int Line,
    int Position,
    string Expression,
    string SymbolId,
    bool IsCallerEvidence,
    string EnclosingMemberSymbolId,
    string EnclosingTypeSymbolId);
internal sealed record ReferenceContext(string MemberSymbolId, string TypeSymbolId);
internal sealed record ProducerCheck(string ItemId, string Path, string SymbolId, int DeclarationCount);
internal sealed record CallerCount(string ItemId, string ScanId, int Count);
internal sealed record CandidateSignal(
    string Path,
    int Line,
    string Text,
    string SymbolId,
    int ReferenceCount);
internal sealed record Discovery(string ItemId, string Kind, string ScanId, string Evidence, string[] SymbolIds);
internal sealed record ProbeOutput(
    IReadOnlyList<ProducerCheck> ProducerChecks,
    IReadOnlyList<CallerCount> CallerCounts,
    IReadOnlyList<CandidateSignal> CandidateSignals,
    IReadOnlyList<Discovery> Discoveries);
