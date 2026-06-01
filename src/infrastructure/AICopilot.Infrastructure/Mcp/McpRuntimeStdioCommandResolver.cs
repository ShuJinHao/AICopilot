namespace AICopilot.Infrastructure.Mcp;

internal sealed class McpRuntimeStdioCommandUnavailableException(string command, string message)
    : InvalidOperationException(message)
{
    public string Command { get; } = command;
}

internal static class McpRuntimeStdioCommandResolver
{
    public static void EnsureAvailable(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new McpRuntimeStdioCommandUnavailableException(
                command,
                "MCP stdio command is empty.");
        }

        if (ContainsDirectorySeparator(command))
        {
            if (!IsExecutableFile(command))
            {
                throw new McpRuntimeStdioCommandUnavailableException(
                    command,
                    $"MCP stdio command '{command}' does not exist or is not executable.");
            }

            return;
        }

        if (FindOnPath(command) is not null)
        {
            return;
        }

        throw new McpRuntimeStdioCommandUnavailableException(
            command,
            $"MCP stdio command '{command}' was not found on PATH.");
    }

    private static string? FindOnPath(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var candidate in CandidatePaths(directory, command))
            {
                if (IsExecutableFile(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidatePaths(string directory, string command)
    {
        yield return Path.Combine(directory, command);

        if (!OperatingSystem.IsWindows() || Path.HasExtension(command))
        {
            yield break;
        }

        var extensions = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(extensions))
        {
            extensions = ".COM;.EXE;.BAT;.CMD";
        }

        foreach (var extension in extensions.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(directory, command + extension);
        }
    }

    private static bool ContainsDirectorySeparator(string command)
    {
        return command.Contains(Path.DirectorySeparatorChar)
               || command.Contains(Path.AltDirectorySeparatorChar);
    }

    private static bool IsExecutableFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
