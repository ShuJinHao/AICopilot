using MediatR;
using System.Runtime.CompilerServices;

namespace AICopilot.Services.CrossCutting.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IRequestValidator<TRequest>> validators) :
    IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        await RequestValidation.ValidateAsync(request, validators, cancellationToken);
        return await next(cancellationToken);
    }
}

public sealed class ValidationStreamBehavior<TRequest, TResponse>(
    IEnumerable<IRequestValidator<TRequest>> validators) :
    IStreamPipelineBehavior<TRequest, TResponse> where TRequest : IStreamRequest<TResponse>
{
    public async IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await RequestValidation.ValidateAsync(request, validators, cancellationToken);

        await foreach (var item in next().WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }
}
