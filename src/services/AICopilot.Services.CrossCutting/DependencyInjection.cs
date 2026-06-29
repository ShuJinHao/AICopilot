using AICopilot.Services.CrossCutting.Behaviors;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AICopilot.Services.CrossCutting;

public static class DependencyInjection
{
    public static IServiceCollection AddAICopilotMediatRPipeline(this IServiceCollection services)
    {
        services.TryAddScoped<IAuthorizationRequirementEvaluator, AuthorizationRequirementEvaluator>();

        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IPipelineBehavior<,>),
            typeof(TelemetryBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IPipelineBehavior<,>),
            typeof(ValidationBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IPipelineBehavior<,>),
            typeof(AuthorizationBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IStreamPipelineBehavior<,>),
            typeof(TelemetryStreamBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IStreamPipelineBehavior<,>),
            typeof(ValidationStreamBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IStreamPipelineBehavior<,>),
            typeof(AuthorizationStreamBehavior<,>)));

        return services;
    }
}
