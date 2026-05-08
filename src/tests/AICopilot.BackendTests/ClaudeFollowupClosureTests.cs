using System.Text.RegularExpressions;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows;

namespace AICopilot.BackendTests;

public sealed class ClaudeFollowupClosureTests
{
    [Fact]
    public async Task ChatWorkflowSink_ShouldFlushWrittenChunksBeforeCompletion()
    {
        var sink = new ChatWorkflowSink();
        await sink.WriteAsync(new ChatChunk("data-analysis", ChunkType.Text, "first"), CancellationToken.None);
        await sink.WriteAsync(new ChatChunk("data-analysis", ChunkType.Text, "second"), CancellationToken.None);

        sink.Complete();

        var chunks = await ReadAllAsync(sink);

        chunks.Select(chunk => chunk.Content).Should().Equal("first", "second");
    }

    [Fact]
    public async Task ChatWorkflowSink_ShouldPropagateBranchFailureToReader()
    {
        var sink = new ChatWorkflowSink();
        var failure = new InvalidOperationException("branch failed");

        sink.Complete(failure);
        var read = async () => await ReadAllAsync(sink);

        await read.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("branch failed");
    }

    [Fact]
    public void ChatWorkflowOrchestrator_ShouldUseExplicitSinkCompletionFlow()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workflows",
            "ChatWorkflowOrchestrator.cs"));

        source.Should().NotContain(".ContinueWith(");
        source.Should().Contain("CompleteSinkWhenBranchesFinishAsync");
        source.Should().Contain("await branchTask.ConfigureAwait(false)");
        source.Should().Contain("sink.Complete(ex)");
    }

    [Fact]
    public void Authorization_ShouldNotUseJwtRoleClaimAsAuthorizationSource()
    {
        var solutionRoot = FindSolutionRoot();
        var roleAuthorizationPatterns = new[]
        {
            new Regex(@"\[Authorize\s*\([^\)]*Roles\s*=", RegexOptions.CultureInvariant),
            new Regex(@"RequireRole\s*\(", RegexOptions.CultureInvariant),
            new Regex(@"new\s+AuthorizeAttribute\s*\{[^}]*Roles\s*=", RegexOptions.CultureInvariant)
        };
        var allowedClaimTypeRoleFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizePath(Path.Combine(
                solutionRoot,
                "src",
                "hosts",
                "AICopilot.HttpApi",
                "Infrastructure",
                "CurrentUser.cs")),
            NormalizePath(Path.Combine(
                solutionRoot,
                "src",
                "infrastructure",
                "AICopilot.Infrastructure",
                "Authentication",
                "JwtTokenGenerator.cs"))
        };

        var roleAuthorizationViolations = new List<string>();
        var claimRoleViolations = new List<string>();
        foreach (var file in EnumerateProductionSources(solutionRoot))
        {
            var normalizedFile = NormalizePath(file);
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (roleAuthorizationPatterns.Any(pattern => pattern.IsMatch(line)))
                {
                    roleAuthorizationViolations.Add($"{normalizedFile}:{index + 1}: {line.Trim()}");
                }

                if (line.Contains("ClaimTypes.Role", StringComparison.Ordinal)
                    && !allowedClaimTypeRoleFiles.Contains(normalizedFile))
                {
                    claimRoleViolations.Add($"{normalizedFile}:{index + 1}: {line.Trim()}");
                }
            }
        }

        roleAuthorizationViolations.Should().BeEmpty(
            "AICopilot must not authorize with JWT role claims; permission attributes and SecurityStamp revocation are the authority.");
        claimRoleViolations.Should().BeEmpty(
            "ClaimTypes.Role is limited to token issuance and CurrentUser audit display, not authorization decisions.");
    }

    private static async Task<List<ChatChunk>> ReadAllAsync(ChatWorkflowSink sink)
    {
        var chunks = new List<ChatChunk>();
        await foreach (var chunk in sink.ReadAllAsync(CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private static IEnumerable<string> EnumerateProductionSources(string solutionRoot)
    {
        var roots = new[]
        {
            Path.Combine(solutionRoot, "src", "core"),
            Path.Combine(solutionRoot, "src", "hosts"),
            Path.Combine(solutionRoot, "src", "infrastructure"),
            Path.Combine(solutionRoot, "src", "services"),
            Path.Combine(solutionRoot, "src", "shared")
        };

        return roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AICopilot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AICopilot.slnx from the test output directory.");
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }
}
