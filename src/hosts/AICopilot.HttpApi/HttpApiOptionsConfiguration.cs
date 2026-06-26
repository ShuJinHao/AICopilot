using AICopilot.HttpApi.Infrastructure;
using AICopilot.IdentityService.Authorization;
using AICopilot.Infrastructure.Authentication;
using AICopilot.Services.Contracts;

namespace AICopilot.HttpApi;

internal sealed record HttpApiValidatedOptions(
    JwtSettings JwtSettings,
    CloudOidcOptions CloudOidcOptions,
    CloudIdentityStatusOptions CloudIdentityStatusOptions);

internal static class HttpApiOptionsConfiguration
{
    public static HttpApiValidatedOptions ConfigureAndValidate(IHostApplicationBuilder builder)
    {
        ApplyIntranetHttpOidcEnvironmentOverride(builder.Configuration);

        var configurationSection = builder.Configuration.GetSection("JwtSettings");
        var jwtSettings = configurationSection.Get<JwtSettings>();
        if (jwtSettings is null)
        {
            throw new NullReferenceException(nameof(jwtSettings));
        }

        if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
        {
            throw new InvalidOperationException("JwtSettings:SecretKey is required; configure it with user-secrets or the JwtSettings__SecretKey environment variable.");
        }

        builder.Services.Configure<JwtSettings>(configurationSection);
        builder.Services.Configure<CloudOidcOptions>(
            builder.Configuration.GetSection(CloudOidcOptions.SectionName));
        builder.Services.Configure<CloudOidcBootstrapAdminBindingOptions>(
            builder.Configuration.GetSection(CloudOidcBootstrapAdminBindingOptions.SectionName));
        builder.Services.Configure<CloudIdentityStatusOptions>(
            builder.Configuration.GetSection(CloudIdentityStatusOptions.SectionName));
        builder.Services.Configure<CloudReadonlyOptions>(
            builder.Configuration.GetSection(CloudReadonlyOptions.SectionName));
        builder.Services.Configure<CloudAiReadOptions>(
            builder.Configuration.GetSection(CloudAiReadOptions.SectionName));

        var cloudOidcOptions = builder.Configuration
            .GetSection(CloudOidcOptions.SectionName)
            .Get<CloudOidcOptions>() ?? new CloudOidcOptions();
        cloudOidcOptions.EnsureValid(builder.Environment.EnvironmentName);

        var cloudOidcBootstrapAdminBindingOptions = builder.Configuration
            .GetSection(CloudOidcBootstrapAdminBindingOptions.SectionName)
            .Get<CloudOidcBootstrapAdminBindingOptions>() ?? new CloudOidcBootstrapAdminBindingOptions();
        cloudOidcBootstrapAdminBindingOptions.EnsureValid();

        var cloudIdentityStatusSection = builder.Configuration.GetSection(CloudIdentityStatusOptions.SectionName);
        var cloudIdentityStatusOptions = cloudIdentityStatusSection.Get<CloudIdentityStatusOptions>()
            ?? new CloudIdentityStatusOptions();
        cloudIdentityStatusOptions.EnsureValid(
            builder.Environment.EnvironmentName,
            cloudOidcOptions.IsConfigured(),
            cloudIdentityStatusSection["Enabled"] is not null);

        var cloudAiReadOptions = builder.Configuration
            .GetSection(CloudAiReadOptions.SectionName)
            .Get<CloudAiReadOptions>() ?? new CloudAiReadOptions();
        cloudAiReadOptions.EnsureValid();

        var cloudReadonlyOptions = builder.Configuration
            .GetSection(CloudReadonlyOptions.SectionName)
            .Get<CloudReadonlyOptions>() ?? new CloudReadonlyOptions();
        cloudReadonlyOptions.EnsureValid(cloudAiReadOptions, builder.Environment.EnvironmentName);

        return new HttpApiValidatedOptions(jwtSettings, cloudOidcOptions, cloudIdentityStatusOptions);
    }

    private static void ApplyIntranetHttpOidcEnvironmentOverride(IConfiguration configuration)
    {
        var value = configuration[CloudOidcOptions.AllowIntranetHttpOidcEnvironmentVariable];
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        configuration[$"{CloudOidcOptions.SectionName}:{nameof(CloudOidcOptions.AllowIntranetHttpOidc)}"] = value;
    }
}
