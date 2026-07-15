using System.Reflection;
using AICopilot.SharedKernel.Result;

namespace AICopilot.ContractFilesystemTests;

public sealed class ErrorCodeCatalogFilesystemContractTests
{
    [Fact]
    public void ContractDocument_ShouldListBackendProblemCodes()
    {
        var document = File.ReadAllText(FindContractDocumentPath());
        var problemCodes = typeof(AuthProblemCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Concat(typeof(AppProblemCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            .Concat(typeof(CloudAiReadProblemCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        problemCodes.Should().NotBeEmpty();
        foreach (var problemCode in problemCodes)
        {
            document.Should().Contain($"`{problemCode}`");
        }
    }

    private static string FindContractDocumentPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "docs",
                "frontend-integration-contract-package-2026-05-17.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("AICopilot frontend integration contract document was not found.");
    }
}
