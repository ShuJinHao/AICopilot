using AICopilot.SharedKernel.Result;

namespace AICopilot.Services.CrossCutting.Exceptions;

public abstract class ApiProblemException : Exception
{
    protected ApiProblemException(int statusCode, ApiProblemDescriptor problem)
        : base(problem.Detail)
    {
        StatusCode = statusCode;
        Problem = problem;
    }

    public int StatusCode { get; }

    public ApiProblemDescriptor Problem { get; }
}
