using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AICopilot.EndToEndTests;

[Collection(CoreBackendTestCollection.Name)]
public sealed class AgentPlanRejectionEndToEndTests(CoreAICopilotAppFixture fixture)
    : AgentTaskHttpScenarioTestBase(fixture)
{
    private const int MaxPlanCanonicalBytes = 262_144;
    private const int ProbeCount = 64;
    private const int BoundaryReserveBytes = 32_768;
    private const int SourcePoolSize = 1_200;
    private const int MaxSourceNameLength = 200;
    private static readonly IReadOnlySet<string> DigestExcludedRootProperties =
        new HashSet<string>(
            ["planDigest", "planKind", "isExecutable", "lifecycleSealPadding"],
            StringComparer.Ordinal);

    [Fact]
    public async Task ExactCanonicalBoundary_ShouldRoundTripAndRejectPlusOneWithoutPersistence()
    {
        await AuthenticateAsAdminAsync();

        var templateId = await CreateAgentReportTemplateAsync();
        var runKey = Guid.NewGuid().ToString("N")[..8];
        var sources = Enumerable.Range(0, SourcePoolSize)
            .Select(index => new BusinessDatabase(
                $"p0{runKey}{index:D4}",
                "P0 canonical boundary governed source.",
                "Host=localhost;Database=p0_boundary;",
                DbProviderType.PostgreSql,
                isReadOnly: true,
                externalSystemType: BusinessDataExternalSystemType.CloudReadOnly,
                readOnlyCredentialVerified: true,
                isEnabled: true,
                isSelectableInChat: false,
                isSelectableInAgent: true))
            .ToArray();

        await using var dataContext = await CreateDataAnalysisDbContextAsync();
        dataContext.BusinessDatabases.AddRange(sources);
        await dataContext.SaveChangesAsync();

        try
        {
            var probe = await CreatePlanAsync(templateId, sources, ProbeCount);
            var probePlusOne = await CreatePlanAsync(templateId, sources, ProbeCount + 1);
            var probeBytes = Encoding.UTF8.GetByteCount(probe.Task.PlanJson);
            var bytesPerSource = Encoding.UTF8.GetByteCount(probePlusOne.Task.PlanJson) - probeBytes;
            bytesPerSource.Should().BeGreaterThan(0, "the real production Plan must expose selected source bytes");

            var targetBeforePadding = MaxPlanCanonicalBytes - BoundaryReserveBytes;
            probeBytes.Should().BeLessThan(targetBeforePadding);
            var calibratedSourceCount = ProbeCount +
                ((targetBeforePadding - probeBytes) / bytesPerSource);
            calibratedSourceCount.Should().BeGreaterThan(ProbeCount + 1);
            calibratedSourceCount.Should().BeLessThanOrEqualTo(SourcePoolSize);

            var calibration = await CreatePlanAsync(templateId, sources, calibratedSourceCount);
            var calibrationBytes = Encoding.UTF8.GetByteCount(calibration.Task.PlanJson);
            calibrationBytes.Should().BeLessThan(MaxPlanCanonicalBytes);

            var selectedSources = sources.Take(calibratedSourceCount).ToArray();
            var remainingPadding = MaxPlanCanonicalBytes - calibrationBytes;
            var availablePadding = selectedSources.Sum(source => MaxSourceNameLength - source.Name.Length);
            availablePadding.Should().BeGreaterThanOrEqualTo(
                remainingPadding,
                "legal BusinessDatabase names must be able to reach the exact production boundary");

            foreach (var source in selectedSources)
            {
                if (remainingPadding == 0)
                {
                    break;
                }

                var padding = Math.Min(remainingPadding, MaxSourceNameLength - source.Name.Length);
                source.UpdateInfo(source.Name + new string('x', padding), source.Description);
                remainingPadding -= padding;
            }

            remainingPadding.Should().Be(0);
            await dataContext.SaveChangesAsync();

            var exact = await CreatePlanAsync(templateId, sources, calibratedSourceCount);
            Encoding.UTF8.GetByteCount(exact.Task.PlanJson).Should().Be(MaxPlanCanonicalBytes);
            AgentCanonicalJsonV1.MeasureCanonicalUtf8Bytes(
                    exact.Task.PlanJson,
                    MaxPlanCanonicalBytes)
                .Should()
                .Be(MaxPlanCanonicalBytes);
            exact.Task.PlanSchemaVersion.Should().Be("2.0");
            exact.Task.PlanDigest.Should().MatchRegex("^[0-9a-f]{64}$");
            exact.Task.IsPlanExecutable.Should().BeFalse();
            exact.Task.PlanIntegrityStatus.Should().Be("ValidV2");

            var restTask = await GetJsonAsync<AgentTaskDto>(
                $"/api/aigateway/agent/task?id={exact.Task.Id}");
            restTask.PlanJson.Should().Be(exact.Task.PlanJson);
            restTask.PlanDigest.Should().Be(exact.Task.PlanDigest);

            await using (var freshContext = await CreateAiGatewayDbContextAsync())
            {
                var persisted = await freshContext.AgentTasks
                    .AsNoTracking()
                    .SingleAsync(task => task.Id == new AgentTaskId(exact.Task.Id));
                persisted.PlanJson.Should().Be(exact.Task.PlanJson);
                Encoding.UTF8.GetByteCount(persisted.PlanJson).Should().Be(MaxPlanCanonicalBytes);
                RecomputePlanDigest(persisted.PlanJson).Should().Be(exact.Task.PlanDigest);
            }

            var plusOneSource = selectedSources.First(source => source.Name.Length < MaxSourceNameLength);
            plusOneSource.Name
                .All(character => character is >= '0' and <= '9' or >= 'a' and <= 'z')
                .Should()
                .BeTrue("the +1 proof must use unescaped single-byte ASCII in canonical JSON");
            CountOccurrences(exact.Task.PlanJson, plusOneSource.Name).Should().Be(
                1,
                "the selected source name must contribute exactly once to the canonical Plan");

            var predictedPlusOnePlanJson = exact.Task.PlanJson.Replace(
                plusOneSource.Name,
                plusOneSource.Name + "y",
                StringComparison.Ordinal);
            Encoding.UTF8.GetByteCount(predictedPlusOnePlanJson).Should().Be(MaxPlanCanonicalBytes + 1);
            AgentCanonicalJsonV1.MeasureCanonicalUtf8Bytes(
                    predictedPlusOnePlanJson,
                    MaxPlanCanonicalBytes)
                .Should()
                .Be(
                    MaxPlanCanonicalBytes + 1,
                    "the canonical owner must prove the real request shape is exactly 262145 bytes before dispatch");

            plusOneSource.UpdateInfo(plusOneSource.Name + "y", plusOneSource.Description);
            await dataContext.SaveChangesAsync();

            var oversizeSessionId = await CreateSessionAsync(templateId);
            var before = await ReadPersistenceSnapshotAsync(oversizeSessionId);
            var oversizeChunks = await PostPlanStreamEventsAsync(
                CreatePlanPayload(
                    oversizeSessionId,
                    selectedSources.Select(source => source.Id.Value).ToArray()));

            oversizeChunks.Should().NotContain(chunk => chunk.Type == "AgentTask");
            oversizeChunks
                .Where(chunk => chunk.Type == "AgentEvent")
                .Select(chunk => JsonSerializer.Deserialize<PlanStreamEventDto>(chunk.Content, JsonOptions)!)
                .Should()
                .ContainSingle(item => item.Code == AppProblemCodes.PlanPayloadTooLarge);
            oversizeChunks
                .Where(chunk => chunk.Type == "Error")
                .Select(chunk => JsonSerializer.Deserialize<ProblemChunkDto>(chunk.Content, JsonOptions)!)
                .Should()
                .ContainSingle(item => item.Code == AppProblemCodes.PlanPayloadTooLarge);

            var after = await ReadPersistenceSnapshotAsync(oversizeSessionId);
            after.Should().Be(
                before,
                "262145 canonical bytes must fail before AgentTask Add/Save and all durable task side effects");
            (await QueryMessageTimelineEventsAsync(oversizeSessionId)).Should().NotContain(item =>
                item.EventType == nameof(MessageEventType.AgentTaskPlanCreated));
        }
        finally
        {
            dataContext.BusinessDatabases.RemoveRange(sources);
            await dataContext.SaveChangesAsync();
        }
    }

    private async Task<(Guid SessionId, AgentTaskDto Task)> CreatePlanAsync(
        Guid templateId,
        IReadOnlyList<BusinessDatabase> sources,
        int sourceCount)
    {
        var sessionId = await CreateSessionAsync(templateId);
        var task = await PostPlanStreamAsync(CreatePlanPayload(
            sessionId,
            sources.Take(sourceCount).Select(source => source.Id.Value).ToArray()));
        return (sessionId, task);
    }

    private static object CreatePlanPayload(Guid sessionId, IReadOnlyCollection<Guid> dataSourceIds)
    {
        return new
        {
            sessionId,
            goal = "Summarize the explicitly selected governed read-only sources for the P0 boundary contract.",
            taskType = AgentTaskType.DataAnalysis,
            modelId = (Guid?)null,
            uploadIds = Array.Empty<Guid>(),
            knowledgeBaseIds = Array.Empty<Guid>(),
            dataSourceIds,
            businessDomains = Array.Empty<string>(),
            queryMode = "TextToSql",
            requiresDataApproval = false,
            artifactTargets = Array.Empty<string>(),
            forceStaticPlanner = true,
            pluginSelectionMode = "BuiltInOnly",
            selectedPluginIds = Array.Empty<Guid>(),
            capabilitySelectionMode = "ExplicitAllowlist",
            requestedCapabilityCodes = new[] { "Analysis.GovernedQuery" }
        };
    }

    private async Task<PlanPersistenceSnapshot> ReadPersistenceSnapshotAsync(Guid sessionId)
    {
        await using var context = await CreateAiGatewayDbContextAsync();
        var sessionTaskIds = await context.AgentTasks
            .Where(task => task.SessionId == new SessionId(sessionId))
            .Select(task => task.Id)
            .ToArrayAsync();
        var timeline = await QueryMessageTimelineEventsAsync(sessionId);

        var approvalCount = sessionTaskIds.Length == 0
            ? 0
            : await context.ApprovalRequests.CountAsync(item => sessionTaskIds.Contains(item.TaskId));
        var workspaceCount = sessionTaskIds.Length == 0
            ? 0
            : await context.ArtifactWorkspaces.CountAsync(item => sessionTaskIds.Contains(item.TaskId));
        var artifactCount = sessionTaskIds.Length == 0
            ? 0
            : await context.ArtifactWorkspaces
                .Where(item => sessionTaskIds.Contains(item.TaskId))
                .SelectMany(item => item.Artifacts)
                .CountAsync();
        var runAttemptCount = sessionTaskIds.Length == 0
            ? 0
            : await context.AgentTaskRunAttempts.CountAsync(item => sessionTaskIds.Contains(item.TaskId));
        var queueItemCount = sessionTaskIds.Length == 0
            ? 0
            : await context.AgentTaskRunQueueItems.CountAsync(item => sessionTaskIds.Contains(item.TaskId));
        var toolExecutionCount = sessionTaskIds.Length == 0
            ? 0
            : await context.ToolExecutionRecords.CountAsync(item => sessionTaskIds.Contains(item.TaskId));

        return new PlanPersistenceSnapshot(
            sessionTaskIds.Length,
            approvalCount,
            workspaceCount,
            artifactCount,
            runAttemptCount,
            queueItemCount,
            toolExecutionCount,
            timeline.Count(item => item.EventType == nameof(MessageEventType.AgentTaskPlanCreated)),
            await ReadAgentPlanAuditCountAsync());
    }

    private async Task<long> ReadAgentPlanAuditCountAsync()
    {
        // CoreBackendTestCollection disables parallelization and its Aspire fixture owns
        // non-persistent test containers, so this action-scoped count cannot be changed
        // by another test while the oversized request is in flight.
        var connectionString = await _fixture.GetConnectionStringAsync();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE action_code = 'Agent.Plan';";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var startIndex = 0;
        while ((startIndex = value.IndexOf(token, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += token.Length;
        }

        return count;
    }

    private static string RecomputePlanDigest(string planJson)
    {
        AgentCanonicalJsonV1.Canonicalize(planJson).Should().Be(planJson);
        using var document = JsonDocument.Parse(planJson);
        var declaredDigest = document.RootElement.GetProperty("planDigest").GetString();
        declaredDigest.Should().MatchRegex("^[0-9a-f]{64}$");

        var digestSource = AgentCanonicalJsonV1.Canonicalize(
            planJson,
            DigestExcludedRootProperties);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digestSource)))
            .ToLowerInvariant();
        digest.Should().Be(declaredDigest);
        return digest;
    }

    private sealed record PlanStreamEventDto(string Stage, string? Code, string Detail);

    private sealed record PlanPersistenceSnapshot(
        int AgentTaskCount,
        int ApprovalCount,
        int WorkspaceCount,
        int ArtifactCount,
        int RunAttemptCount,
        int QueueItemCount,
        int ToolExecutionCount,
        int PlanCreatedTimelineCount,
        long AuditCount);
}
