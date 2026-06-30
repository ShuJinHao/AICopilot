using System.Data;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Services.Contracts;

namespace AICopilot.BackendTests;

public sealed class CloudReadOnlyTextToSqlFallbackRunnerTests
{
    [Fact]
    public async Task RunAsync_ShouldRepairSqlOnce_WhenGuardRejectsUnknownColumn()
    {
        var generator = new QueueTextToSqlGenerator(
            CloudReadOnlyTextToSqlGenerationResult.Success(
                "SELECT d.device_code FROM devices d LIMIT 10",
                "first draft uses a non-governed column"),
            CloudReadOnlyTextToSqlGenerationResult.Success(
                "SELECT d.client_code FROM devices d LIMIT 10",
                "repair uses governed column"));
        var connector = new RecordingConnector(new DatabaseQueryResult(
            [
                new Dictionary<string, object?>
                {
                    ["client_code"] = "DEV-001"
                }
            ],
            ReturnedRowCount: 1,
            IsTruncated: false,
            ElapsedMilliseconds: 3));
        var runner = new CloudReadOnlyTextToSqlFallbackRunner(
            generator,
            connector,
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()));

        var result = await runner.RunAsync(
            CreateCloudReadOnlyDatabase(),
            "查看设备列表",
            requestedLimit: 10,
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.RepairAttempts.Should().ContainSingle();
        result.RepairAttempts.Single().FailureCode.Should().Be(CloudReadOnlyTextToSqlFailureCode.UnknownColumn);
        generator.Requests.Should().HaveCount(2);
        generator.Requests[1].RepairHistory.Should().ContainSingle()
            .Which.FailureCode.Should().Be(CloudReadOnlyTextToSqlFailureCode.UnknownColumn);
        generator.Requests[1].PreviousSqlForRepair.Should().Contain("device_code");
        connector.ExecutedSql.Should().ContainSingle()
            .Which.Should().Contain("client_code");
        result.Context.Should().NotContain("device_code");
        result.Context.Should().NotContain("SELECT");
    }

    [Fact]
    public async Task RunAsync_ShouldNotRetry_WhenGuardRejectsWriteSql()
    {
        var generator = new QueueTextToSqlGenerator(
            CloudReadOnlyTextToSqlGenerationResult.Success(
                "DROP TABLE devices",
                "unsafe draft"));
        var connector = new RecordingConnector(new DatabaseQueryResult([], 0, false, 0));
        var runner = new CloudReadOnlyTextToSqlFallbackRunner(
            generator,
            connector,
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()));

        var result = await runner.RunAsync(
            CreateCloudReadOnlyDatabase(),
            "删除设备",
            requestedLimit: 10,
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.RepairAttempts.Should().ContainSingle();
        result.RepairAttempts.Single().FailureCode.Should().Be(CloudReadOnlyTextToSqlFailureCode.WriteSql);
        generator.Requests.Should().ContainSingle();
        connector.ExecutedSql.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ShouldPassGeneratedParameters_ToReadonlyExecutor()
    {
        var generator = new QueueTextToSqlGenerator(
            CloudReadOnlyTextToSqlGenerationResult.Success(
                "SELECT d.client_code FROM devices d WHERE d.client_code = @client_code LIMIT 10",
                "parameterized sql",
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["client_code"] = "DEV-001"
                }));
        var connector = new RecordingConnector(new DatabaseQueryResult(
            [
                new Dictionary<string, object?>
                {
                    ["client_code"] = "DEV-001"
                }
            ],
            ReturnedRowCount: 1,
            IsTruncated: false,
            ElapsedMilliseconds: 3));
        var runner = new CloudReadOnlyTextToSqlFallbackRunner(
            generator,
            connector,
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()));

        var result = await runner.RunAsync(
            CreateCloudReadOnlyDatabase(),
            "查看 DEV-001 设备",
            requestedLimit: 10,
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        connector.ExecutedParameters.Should().ContainSingle();
        connector.ExecutedParameters.Single().Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>()
            .Which.Should().ContainKey("client_code")
            .WhoseValue.Should().Be("DEV-001");
    }

    [Fact]
    public async Task RunAsync_ShouldNotRetry_WhenRuntimeTimesOut()
    {
        var generator = new QueueTextToSqlGenerator(
            CloudReadOnlyTextToSqlGenerationResult.Success(
                "SELECT d.client_code FROM devices d LIMIT 10",
                "fixed sql"));
        var connector = new ThrowingConnector(new TimeoutException("Business readonly query timed out."));
        var runner = new CloudReadOnlyTextToSqlFallbackRunner(
            generator,
            connector,
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()));

        var result = await runner.RunAsync(
            CreateCloudReadOnlyDatabase(),
            "查看设备",
            requestedLimit: 10,
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.RepairAttempts.Should().ContainSingle()
            .Which.FailureCode.Should().Be(CloudReadOnlyTextToSqlFailureCode.Timeout);
        generator.Requests.Should().ContainSingle();
        generator.Requests.Single().PreviousSqlForRepair.Should().BeNull();
    }

    private static BusinessDatabaseConnectionInfo CreateCloudReadOnlyDatabase()
    {
        return new BusinessDatabaseConnectionInfo(
            Guid.NewGuid(),
            "CloudPlatformReadonly",
            "Cloud Platform readonly business data",
            "Host=localhost;Database=cloud;Username=readonly;Password=fake-test-only",
            DatabaseProviderType.PostgreSql,
            IsEnabled: true,
            IsReadOnly: true,
            DataSourceExternalSystemType.CloudReadOnly,
            ReadOnlyCredentialVerified: true,
            DefaultQueryLimit: 10,
            MaxQueryLimit: 100);
    }

    private sealed class QueueTextToSqlGenerator(params CloudReadOnlyTextToSqlGenerationResult[] results)
        : ICloudReadOnlyTextToSqlGenerator
    {
        private readonly Queue<CloudReadOnlyTextToSqlGenerationResult> _results = new(results);

        public List<CloudReadOnlyTextToSqlGenerationRequest> Requests { get; } = [];

        public Task<CloudReadOnlyTextToSqlGenerationResult> GenerateAsync(
            CloudReadOnlyTextToSqlGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class RecordingConnector(DatabaseQueryResult result) : IDatabaseConnector
    {
        public List<string> ExecutedSql { get; } = [];

        public List<object?> ExecutedParameters { get; } = [];

        public IDbConnection GetConnection(BusinessDatabaseConnectionInfo database)
        {
            throw new NotSupportedException("The test connector does not create real database connections.");
        }

        public Task<IEnumerable<dynamic>> ExecuteQueryAsync(
            BusinessDatabaseConnectionInfo database,
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The test connector only supports metadata query execution.");
        }

        public Task<DatabaseQueryResult> ExecuteQueryWithMetadataAsync(
            BusinessDatabaseConnectionInfo database,
            string sql,
            object? parameters = null,
            DatabaseQueryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ExecutedSql.Add(sql);
            ExecutedParameters.Add(parameters);
            return Task.FromResult(result);
        }

        public Task<IEnumerable<dynamic>> GetSchemaInfoAsync(
            BusinessDatabaseConnectionInfo database,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The test connector does not read schema.");
        }
    }

    private sealed class ThrowingConnector(Exception exception) : IDatabaseConnector
    {
        public IDbConnection GetConnection(BusinessDatabaseConnectionInfo database)
        {
            throw new NotSupportedException("The test connector does not create real database connections.");
        }

        public Task<IEnumerable<dynamic>> ExecuteQueryAsync(
            BusinessDatabaseConnectionInfo database,
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The test connector only supports metadata query execution.");
        }

        public Task<DatabaseQueryResult> ExecuteQueryWithMetadataAsync(
            BusinessDatabaseConnectionInfo database,
            string sql,
            object? parameters = null,
            DatabaseQueryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<DatabaseQueryResult>(exception);
        }

        public Task<IEnumerable<dynamic>> GetSchemaInfoAsync(
            BusinessDatabaseConnectionInfo database,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The test connector does not read schema.");
        }
    }

    private sealed class NoopAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }
}
