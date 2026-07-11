using System.Text.RegularExpressions;

namespace AICopilot.BackendTests;

[Trait("Suite", "DeploymentBehavior")]
public sealed class DeploymentPreflightBehaviorTests
{
    private static readonly string[] DirectExecutionScripts =
    [
        "deploy/enterprise-ai/build-and-push.sh",
        "deploy/enterprise-ai/deploy-release.sh",
        "deploy/enterprise-ai/harbor-retention.sh",
        "deploy/enterprise-ai/local-release.sh",
        "deploy/enterprise-ai/mirror-base-images.sh",
        "deploy/enterprise-ai/post-release-cleanup.sh",
        "deploy/enterprise-ai/runner/iiot-release-runner.sh",
        "deploy/enterprise-ai/scripts/apply-cloud-readonly-grants.sh",
        "deploy/enterprise-ai/scripts/cancel-support-reservation.sh",
        "deploy/enterprise-ai/scripts/check-cloud-readonly-grants.sh",
        "deploy/enterprise-ai/scripts/check-model-provider-openai.sh",
        "deploy/enterprise-ai/scripts/check-model-secret-migration.sh",
        "deploy/enterprise-ai/scripts/check-platform-attestation-record.sh",
        "deploy/enterprise-ai/scripts/check-release-security-attestation.sh",
        "deploy/enterprise-ai/scripts/check-release-state-access.sh",
        "deploy/enterprise-ai/scripts/check-runner-security-attestation.sh",
        "deploy/enterprise-ai/scripts/install-support-release.sh"
    ];

    private static readonly string[] BashOrSourceOnlyScripts =
    [
        "deploy/enterprise-ai/scripts/query-release-invocation.sh",
        "deploy/enterprise-ai/scripts/release-common.sh",
        "deploy/enterprise-ai/tests/deployment-behavior.sh"
    ];

    [Fact]
    public async Task DeployReleaseValidateOnly_ShouldValidateHttpOnlyOidcWithoutReleaseTagOrDocker()
    {
        var scriptPath = Path.Combine(
            RepositoryTestSupport.Root,
            "deploy",
            "enterprise-ai",
            "deploy-release.sh");
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-deploy-validate-only",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempDirectory);
        try
        {
            var validEnvPath = WriteDeployValidateEnv(
                tempDirectory,
                "valid.env",
                "http://cloud.factory.internal:81");

            var validResult = await RepositoryTestSupport.RunAsync(
                "bash",
                [scriptPath, "--validate-only"],
                environmentVariables: new Dictionary<string, string> { ["ENV_FILE"] = validEnvPath });

            validResult.ExitCode.Should().Be(0, validResult.Output);
            validResult.Output.Should().Contain("AICopilot deploy environment validation passed");

            var publicHttpOidcEnvPath = WriteDeployValidateEnv(
                tempDirectory,
                "public-http-oidc.env",
                "http://cloud.example.com:81");

            var publicHttpOidcResult = await RepositoryTestSupport.RunAsync(
                "bash",
                [scriptPath, "--validate-only"],
                environmentVariables: new Dictionary<string, string> { ["ENV_FILE"] = publicHttpOidcEnvPath });

            publicHttpOidcResult.ExitCode.Should().Be(64, publicHttpOidcResult.Output);
            publicHttpOidcResult.Output.Should().Contain("HTTP-only Cloud OIDC issuer must be loopback");
            publicHttpOidcResult.Output.Should().NotContain("docker compose");
        }
        finally
        {
            RepositoryTestSupport.TryDeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task TrackedShellScripts_ShouldMatchExecutionClassification()
    {
        var result = await RepositoryTestSupport.RunAsync(
            "git",
            ["ls-files", "--stage", "--", "deploy/enterprise-ai"]);
        result.ExitCode.Should().Be(0, result.Output);

        var trackedScripts = result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => Regex.Match(
                line,
                @"^(?<mode>\d{6}) [0-9a-f]+ \d+\t(?<path>.+\.sh)$",
                RegexOptions.CultureInvariant))
            .Where(match => match.Success)
            .ToDictionary(
                match => match.Groups["path"].Value,
                match => match.Groups["mode"].Value,
                StringComparer.Ordinal);
        var classifiedScripts = DirectExecutionScripts
            .Concat(BashOrSourceOnlyScripts)
            .ToArray();

        DirectExecutionScripts.Should().NotIntersectWith(BashOrSourceOnlyScripts);
        trackedScripts.Keys.Should().BeEquivalentTo(
            classifiedScripts,
            "every tracked enterprise-ai shell script must declare whether Git executes it directly");

        foreach (var path in DirectExecutionScripts)
        {
            trackedScripts[path].Should().Be("100755", $"{path} is invoked directly");

            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(Path.Combine(RepositoryTestSupport.Root, path));
                var executeBits = UnixFileMode.UserExecute
                                  | UnixFileMode.GroupExecute
                                  | UnixFileMode.OtherExecute;
                (mode & executeBits).Should().Be(executeBits, $"{path} must be executable after checkout");
            }
        }

        foreach (var path in BashOrSourceOnlyScripts)
        {
            trackedScripts[path].Should().Be("100644", $"{path} is called through bash or sourced");
        }
    }

    private static string WriteDeployValidateEnv(
        string directory,
        string fileName,
        string cloudOidcIssuer)
    {
        var envPath = Path.Combine(directory, fileName);
        File.WriteAllText(
            envPath,
            $$"""
COMPOSE_PROJECT_NAME=enterprise-ai-test
AICOPILOT_PUBLIC_URL=http://aicopilot.factory.internal:82
CLOUD_PLATFORM_URL=http://cloud.factory.internal:81
POSTGRES_PASSWORD=PgStrongSecretValue1234
RABBITMQ_PASSWORD=RbStrongSecretValue1234
QDRANT_KEY=QdStrongSecretValue1234
AICOPILOT_BOOTSTRAP_ADMIN_PASSWORD=AdminStrong1234
AICOPILOT_API_KEY_ENCRYPTION_KEY=EncryptionKeyValue01234567890123456789
AICOPILOT_JWT_SECRET_KEY=JwtSecretValue012345678901234567890123456789012345678901234567890123
CLOUD_READONLY_MODE=Disabled
CLOUD_READONLY_REAL_ENABLED=false
CLOUD_READONLY_REAL_ALLOW_PRODUCTION_READ=false
CLOUD_AI_READ_ENABLED=false
CLOUD_AI_READ_BASE_URL=http://cloud.factory.internal:81
CLOUD_IDENTITY_STATUS_ENABLED=false
CLOUD_IDENTITY_STATUS_BASE_URL=http://cloud.factory.internal:81
DATA_ANALYSIS_CLOUD_READONLY_ENABLED=false
AICOPILOT_MODEL_SMOKE_ENABLED=false
AICOPILOT_MODEL_SMOKE_BASE_URL=http://model.factory.internal:40034/v1
CLOUD_OIDC_ENABLED=true
CLOUD_OIDC_ISSUER={{cloudOidcIssuer}}
ALLOW_INTRANET_HTTP_OIDC=true
CLOUD_OIDC_REQUIRE_HTTPS_METADATA=false
""");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(envPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return envPath;
    }
}
