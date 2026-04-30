using AICopilot.Services.Contracts;

namespace AICopilot.DataAnalysisService.Semantics;

public sealed class NullSemanticPhysicalMappingProvider : ISemanticPhysicalMappingProvider
{
    public bool TryGetMapping(SemanticQueryTarget target, out SemanticPhysicalMapping mapping)
    {
        mapping = default!;
        return false;
    }
}
