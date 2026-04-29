using AICopilot.SharedKernel.Result;

namespace AICopilot.Services.CrossCutting.Exceptions;

public sealed class UnauthorizedProblemException : ApiProblemException
{
    public UnauthorizedProblemException(ApiProblemDescriptor problem)
        : base(401, problem)
    {
    }
}
