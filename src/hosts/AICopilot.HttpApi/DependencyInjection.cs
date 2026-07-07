using AICopilot.AiGatewayService;
using AICopilot.DataAnalysisService;
using AICopilot.HttpApi.Infrastructure;
using AICopilot.IdentityService;
using AICopilot.Infrastructure.Authentication;
using AICopilot.McpService;
using AICopilot.RagService;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting;

namespace AICopilot.HttpApi;

public static class DependencyInjection
{
    extension(IHostApplicationBuilder builder)
    {
        public void AddApplicationService()
        {
            builder.Services.AddAICopilotMediatRPipeline();
            builder.AddIdentityService();
            builder.AddAiGatewayService();
            builder.AddRagService();
            builder.AddDataAnalysisService();
            builder.AddMcpService();
        }

        public void AddWebServices()
        {
            var options = HttpApiOptionsConfiguration.ConfigureAndValidate(builder);

            HttpApiAuthenticationConfiguration.AddHttpApiAuthentication(
                builder.Services,
                options.JwtSettings,
                options.CloudOidcOptions,
                options.CloudIdentityStatusOptions);

            HttpApiCorsConfiguration.AddHttpApiCors(builder.Services, builder.Configuration);
            builder.Services.AddScoped<ICurrentUser, CurrentUser>();
            builder.Services.AddHttpContextAccessor();
            HttpApiRateLimitingConfiguration.AddHttpApiRateLimiting(builder.Services, builder.Configuration);
            builder.Services.AddExceptionHandler<UseCaseExceptionHandler>();
            builder.Services.AddProblemDetails();
        }
    }
}
