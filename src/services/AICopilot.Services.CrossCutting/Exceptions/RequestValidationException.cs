using AICopilot.SharedKernel.Result;

namespace AICopilot.Services.CrossCutting.Exceptions;

public sealed class RequestValidationException : ApiProblemException
{
    public RequestValidationException(ApiProblemDescriptor problem)
        : base(400, problem)
    {
    }
}
