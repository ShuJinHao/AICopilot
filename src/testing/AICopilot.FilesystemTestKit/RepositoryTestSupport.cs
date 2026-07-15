using System.Diagnostics;

namespace AICopilot.FilesystemTestKit;

internal static class RepositoryTestSupport
{
    public static string Root { get; } = FindRoot();

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort test cleanup must not hide the original test failure.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort test cleanup must not hide the original test failure.
        }
    }

    public static async Task<RepositoryCommandResult> RunAsync(
        string fileName,
        IReadOnlyCollection<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            processStartInfo.ArgumentList.Add(argument);
        }

        if (environmentVariables is not null)
        {
            foreach (var (key, value) in environmentVariables)
            {
                processStartInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = processStartInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {fileName}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The process exited between the state check and the kill request.
            }

            await process.WaitForExitAsync(CancellationToken.None);
            var timedOutStreams = await Task.WhenAll(stdoutTask, stderrTask);
            throw new TimeoutException(
                $"Command timed out: {fileName} {string.Join(' ', arguments)}{Environment.NewLine}{string.Concat(timedOutStreams)}");
        }

        var outputStreams = await Task.WhenAll(stdoutTask, stderrTask);
        return new RepositoryCommandResult(process.ExitCode, string.Concat(outputStreams));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AICopilot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate AICopilot.slnx from the test output directory.");
    }
}

internal sealed record RepositoryCommandResult(int ExitCode, string Output);
