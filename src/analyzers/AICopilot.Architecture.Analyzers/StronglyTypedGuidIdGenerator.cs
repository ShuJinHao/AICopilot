using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AICopilot.Architecture.Analyzers;

[Generator(LanguageNames.CSharp)]
public sealed class StronglyTypedGuidIdGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName =
        "AICopilot.SharedKernel.Domain.StronglyTypedGuidIdAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var declarations = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeMetadataName,
            static (node, _) => node is RecordDeclarationSyntax,
            static (attributeContext, _) => CreateDeclaration(attributeContext));
        context.RegisterSourceOutput(declarations, static (sourceContext, declaration) =>
            sourceContext.AddSource(
                $"{declaration.Namespace}.{declaration.Name}.StronglyTypedGuidId.g.cs",
                SourceText.From(Render(declaration), Encoding.UTF8)));
    }

    private static StrongIdDeclaration CreateDeclaration(GeneratorAttributeSyntaxContext context)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var message = context.Attributes[0].ConstructorArguments[0].Value as string ??
                      "Strongly typed Guid id is required.";
        return new StrongIdDeclaration(
            symbol.ContainingNamespace.ToDisplayString(),
            symbol.Name,
            message.Replace("\\", "\\\\")
                .Replace("\"", "\\\""));
    }

    private static string Render(StrongIdDeclaration declaration) => $$"""
        namespace {{declaration.Namespace}};

        public readonly partial record struct {{declaration.Name}} : global::AICopilot.SharedKernel.Domain.IStronglyTypedGuidId
        {
            public {{declaration.Name}}(global::System.Guid value)
            {
                if (value == global::System.Guid.Empty)
                {
                    throw new global::System.ArgumentException("{{declaration.RequiredMessage}}", nameof(value));
                }

                Value = value;
            }

            public global::System.Guid Value { get; }

            public static {{declaration.Name}} New() => new(global::System.Guid.NewGuid());

            public static implicit operator global::System.Guid({{declaration.Name}} id) => id.Value;

            public override string ToString() => Value.ToString();
        }
        """;

    private sealed class StrongIdDeclaration
    {
        public StrongIdDeclaration(string @namespace, string name, string requiredMessage)
        {
            Namespace = @namespace;
            Name = name;
            RequiredMessage = requiredMessage;
        }

        public string Namespace { get; }
        public string Name { get; }
        public string RequiredMessage { get; }
    }
}
