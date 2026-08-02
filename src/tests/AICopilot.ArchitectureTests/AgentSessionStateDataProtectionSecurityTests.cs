using AICopilot.HttpApi;
using Microsoft.Extensions.Hosting;

namespace AICopilot.ArchitectureTests;

public sealed class AgentSessionStateDataProtectionSecurityTests
{
    [Fact]
    public void ProductionConfiguration_ShouldRequireTheFixedKeyDirectory()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production
        });
        builder.Configuration["AiGateway:Deployment:Mode"] = "SingleInstance";
        builder.Configuration["AgentSessionState:DataProtectionKeyPath"] =
            Path.Combine(Path.GetTempPath(), "wrong-key-path");

        var action = () => AgentSessionStateDataProtectionDirectoryValidator
            .EnsureValid(builder);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*must use '/var/lib/aicopilot/data-protection-keys'*");
    }

    [Fact]
    public void Configuration_ShouldRejectMultipleInstancesWithoutSharedKeyProvider()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });
        builder.Configuration["AiGateway:Deployment:Mode"] = "MultiInstance";
        builder.Configuration["AgentSessionState:DataProtectionKeyPath"] =
            Path.GetTempPath();

        var action = () => AgentSessionStateDataProtectionDirectoryValidator
            .EnsureValid(builder);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*supports only AiGateway:Deployment:Mode=SingleInstance*");
    }

    [Fact]
    public void SecureDirectory_ShouldAcceptRuntimeOwnerOnlyWriteAccess()
    {
        var directoryPath = CreateTemporaryDirectory();
        try
        {
            AgentSessionStateDataProtectionDirectoryValidator
                .EnsureSecureDirectory(directoryPath);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public void SecureDirectory_ShouldRejectWriteAccessForOtherUsers()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var directoryPath = CreateTemporaryDirectory();
        try
        {
            File.SetUnixFileMode(
                directoryPath,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute |
                UnixFileMode.GroupWrite);

            var action = () => AgentSessionStateDataProtectionDirectoryValidator
                .EnsureSecureDirectory(directoryPath);

            action.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*must not be writable by group or other users*");
        }
        finally
        {
            File.SetUnixFileMode(
                directoryPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"aicopilot-data-protection-security-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                directoryPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return directoryPath;
    }
}
