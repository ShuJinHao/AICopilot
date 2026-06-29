using AICopilot.SharedKernel.Result;
using AICopilot.Services.CrossCutting.Exceptions;

namespace AICopilot.Services.CrossCutting.Behaviors;

public interface IRequestValidator<in TRequest>
{
    ValueTask<ApiProblemDescriptor?> ValidateAsync(TRequest request, CancellationToken cancellationToken);
}

public static class RequestValidation
{
    public static ApiProblemDescriptor Failed(string detail)
    {
        return new ApiProblemDescriptor(AppProblemCodes.RequestValidationFailed, detail);
    }

    public static async Task ValidateAsync<TRequest>(
        TRequest request,
        IEnumerable<IRequestValidator<TRequest>> validators,
        CancellationToken cancellationToken)
    {
        foreach (var validator in validators)
        {
            var problem = await validator.ValidateAsync(request, cancellationToken);
            if (problem is not null)
            {
                throw new RequestValidationException(problem);
            }
        }
    }
}
