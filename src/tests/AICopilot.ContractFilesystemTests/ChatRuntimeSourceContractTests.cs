namespace AICopilot.ContractFilesystemTests;

public sealed class ChatRuntimeSourceContractTests
{
    [Fact]
    public void FinalAgentRunExecutor_ShouldTimeoutOnlyModelChunkWaitAndSuspendOnApproval()
    {
        var source = ReadSource(
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workflows",
            "Executors",
            "FinalAgentRunExecutor.cs");

        source.Should().Contain(".MoveNextAsync()");
        source.Should().Contain("CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)");
        source.Should().Contain("modelResponseTimeoutCts.CancelAfter(ModelResponseTimeout)");
        source.Should().Contain(".WaitAsync(modelResponseTimeoutCts.Token)");
        source.Should().Contain("AppProblemCodes.ModelRequestTimeout");
        source.Should().Contain("agentContext.FunctionApprovalRequestContents.Count > 0");
        source.Should().Contain("suspendForApproval = true");
        source.Should().Contain("break;");
    }

    [Fact]
    public void ProviderRetryHandler_ShouldRetryOnlyTransientProviderFailures()
    {
        var source = ReadSource(
            "src",
            "infrastructure",
            "AICopilot.Infrastructure",
            "AiGateway",
            "AiProviderRetryHandler.cs");

        source.Should().Contain("MaxRetries = 2");
        source.Should().Contain("HttpStatusCode.TooManyRequests");
        source.Should().Contain("(int)statusCode == 529");
        source.Should().Contain("HttpStatusCode.InternalServerError");
        source.Should().Contain("HttpStatusCode.BadGateway");
        source.Should().Contain("HttpStatusCode.ServiceUnavailable");
        source.Should().Contain("HttpStatusCode.GatewayTimeout");
        source.Should().Contain("HttpRequestException");
        source.Should().NotContain("InvalidOperationException");
    }

    private static string ReadSource(params string[] segments)
    {
        return File.ReadAllText(Path.Combine([FindSolutionRoot(), .. segments]));
    }

    private static string FindSolutionRoot()
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

        throw new InvalidOperationException("Cannot find AICopilot solution root.");
    }
}
