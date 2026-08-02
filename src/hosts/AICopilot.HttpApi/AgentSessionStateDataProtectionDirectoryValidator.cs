using System.Runtime.InteropServices;
using AICopilot.EntityFrameworkCore;

namespace AICopilot.HttpApi;

internal static class AgentSessionStateDataProtectionDirectoryValidator
{
    private const int AtFileDescriptorCurrentWorkingDirectory = -100;
    private const int AtSymlinkNoFollow = 0x100;
    private const uint StatxUid = 0x00000008;
    private const UnixFileMode RequiredOwnerMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode ForbiddenWriteMode =
        UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;

    public static void EnsureValid(IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var configuredPath = AgentSessionStateDataProtection.GetConfiguredKeyPath(
            builder.Configuration);
        if (configuredPath is null)
        {
            if (builder.Environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "AgentSessionState:DataProtectionKeyPath is required in Production.");
            }

            return;
        }

        var deploymentMode = builder.Configuration["AiGateway:Deployment:Mode"]
                             ?? "SingleInstance";
        if (!string.Equals(
                deploymentMode,
                "SingleInstance",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Persisted AgentSession state currently supports only AiGateway:Deployment:Mode=SingleInstance. A shared Data Protection key provider is required before enabling multiple instances.");
        }

        if (builder.Environment.IsProduction() &&
            !string.Equals(
                configuredPath,
                AgentSessionStateDataProtection.ProductionKeyPath,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Production AgentSession Data Protection keys must use '{AgentSessionStateDataProtection.ProductionKeyPath}'.");
        }

        EnsureSecureDirectory(configuredPath);
    }

    internal static void EnsureSecureDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new InvalidOperationException(
                "AgentSession Data Protection key directory is not configured.");
        }

        var fullPath = Path.GetFullPath(directoryPath);
        var directory = new DirectoryInfo(fullPath);
        if (!directory.Exists)
        {
            throw new InvalidOperationException(
                $"AgentSession Data Protection key directory '{fullPath}' does not exist.");
        }

        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0 ||
            directory.LinkTarget is not null)
        {
            throw new InvalidOperationException(
                "AgentSession Data Protection key directory must not be a symbolic link.");
        }

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(fullPath);
            if ((mode & RequiredOwnerMode) != RequiredOwnerMode)
            {
                throw new InvalidOperationException(
                    "AgentSession Data Protection key directory must grant read, write, and execute access to its owner.");
            }

            if ((mode & ForbiddenWriteMode) != 0)
            {
                throw new InvalidOperationException(
                    "AgentSession Data Protection key directory must not be writable by group or other users.");
            }

            if (OperatingSystem.IsLinux())
            {
                EnsureLinuxOwner(fullPath);
            }
        }

        VerifyExclusiveWriteAccess(fullPath);
    }

    private static void EnsureLinuxOwner(string fullPath)
    {
        if (Statx(
                AtFileDescriptorCurrentWorkingDirectory,
                fullPath,
                AtSymlinkNoFollow,
                StatxUid,
                out var status) != 0)
        {
            throw new InvalidOperationException(
                $"Unable to inspect ownership for AgentSession Data Protection key directory. errno={Marshal.GetLastPInvokeError()}.");
        }

        var effectiveUserId = GetEffectiveUserId();
        if (status.UserId != effectiveUserId)
        {
            throw new InvalidOperationException(
                $"AgentSession Data Protection key directory must be owned by the runtime user. directoryUid={status.UserId}; runtimeUid={effectiveUserId}.");
        }
    }

    private static void VerifyExclusiveWriteAccess(string fullPath)
    {
        var probePath = Path.Combine(
            fullPath,
            $".aicopilot-owner-probe-{Guid.NewGuid():N}");
        try
        {
            using var probe = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            probe.WriteByte(0);
            probe.Flush(flushToDisk: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "AgentSession Data Protection key directory is not writable by the runtime user.",
                exception);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatx
    {
        [FieldOffset(20)]
        public uint UserId;
    }

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(
        int directoryFileDescriptor,
        string path,
        int flags,
        uint mask,
        out LinuxStatx status);

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();
}
