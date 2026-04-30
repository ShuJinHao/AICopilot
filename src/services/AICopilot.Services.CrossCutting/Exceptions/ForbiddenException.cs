using AICopilot.SharedKernel.Result;

namespace AICopilot.Services.CrossCutting.Exceptions;

public sealed class ForbiddenException : ApiProblemException
{
    public ForbiddenException(ApiProblemDescriptor problem)
        : base(403, problem)
    {
    }
}
