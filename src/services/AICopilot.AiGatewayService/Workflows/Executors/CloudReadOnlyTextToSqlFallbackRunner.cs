using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public sealed record CloudReadOnlyTextToSqlFallbackResult(
    bool Succeeded,
    string Context,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    int RowCount,
    bool IsTruncated,
    string QueryHash,
    IReadOnlyList<CloudReadOnlyTextToSqlRepairAttemptRecord> RepairAttempts,
    string SafeMessage);

public sealed class CloudReadOnlyTextToSqlFallbackRunner(
    ICloudReadOnlyTextToSqlGenerator generator,
    IDatabaseConnector databaseConnector,
    DataAnalysisAuditRecorder auditRecorder,
    IOptions<CloudReadOnlyTextToSqlOptions>? options = null)
{
    public async Task<CloudReadOnlyTextToSqlFallbackResult> RunAsync(
        BusinessDatabaseConnectionInfo database,
        string? question,
        int? requestedLimit,
        CancellationToken cancellationToken)
    {
        var resolvedOptions = options?.Value ?? new CloudReadOnlyTextToSqlOptions();
        if (!resolvedOptions.Enabled)
        {
            const string disabledMessage = "Cloud readonly Text-to-SQL fallback is disabled.";
            await auditRecorder.RecordCloudReadOnlyTextToSqlFallbackAsync(
                database,
                AuditResults.Rejected,
                disabledMessage,
                ComputeHash(question),
                string.Empty,
                0,
                false,
                [],
                cancellationToken);
            return Failed(disabledMessage, []);
        }

        var validationError = ValidateDatabase(database);
        if (validationError is not null)
        {
            await auditRecorder.RecordCloudReadOnlyTextToSqlFallbackAsync(
                database,
                AuditResults.Rejected,
                validationError,
                ComputeHash(question),
                string.Empty,
                0,
                false,
                [],
                cancellationToken);
            return Failed(validationError, []);
        }

        var limit = ResolveLimit(database, requestedLimit);
        var attempts = new List<CloudReadOnlyTextToSqlRepairAttemptRecord>();
        var safeQuestion = string.IsNullOrWhiteSpace(question) ? "Cloud readonly data query" : question.Trim();
        var maxRepairAttempts = resolvedOptions.ResolveMaxRepairAttempts();
        var queryOptions = resolvedOptions.ResolveQueryOptions();
        string? previousSqlForRepair = null;

        for (var attemptIndex = 1;
             attemptIndex <= maxRepairAttempts + 1;
             attemptIndex++)
        {
            var generationRequest = new CloudReadOnlyTextToSqlGenerationRequest(
                safeQuestion,
                limit,
                CloudReadOnlyGovernedSchema.AllowedTables,
                CloudReadOnlyGovernedSchema.AllowedColumns,
                attempts)
            {
                PreviousSqlForRepair = previousSqlForRepair
            };
            var generated = await generator.GenerateAsync(
                generationRequest,
                cancellationToken);
            previousSqlForRepair = null;

            if (!generated.IsSuccess || string.IsNullOrWhiteSpace(generated.Sql))
            {
                var draftAttempt = CloudReadOnlyTextToSqlRepairClassifier.CreateAttemptRecord(
                    attemptIndex,
                    CloudReadOnlyTextToSqlFailureStage.Draft,
                    generated.Sql,
                    generated.FailureReason ?? "Cloud readonly Text-to-SQL generation failed.",
                    maxRepairAttempts: maxRepairAttempts);
                attempts.Add(draftAttempt);
                break;
            }

            var safetyError = CloudReadOnlySemanticSqlGuard.Validate(generated.Sql);
            if (safetyError is not null)
            {
                var guardAttempt = CloudReadOnlyTextToSqlRepairClassifier.CreateAttemptRecord(
                    attemptIndex,
                    CloudReadOnlyTextToSqlFailureStage.Guard,
                    generated.Sql,
                    safetyError,
                    maxRepairAttempts: maxRepairAttempts);
                attempts.Add(guardAttempt);
                if (!guardAttempt.CanRetry)
                {
                    break;
                }

                previousSqlForRepair = generated.Sql;
                continue;
            }

            try
            {
                var queryResult = await databaseConnector.ExecuteQueryWithMetadataAsync(
                    database,
                    generated.Sql,
                    generated.Parameters,
                    options: queryOptions,
                    cancellationToken: cancellationToken);
                var rows = queryResult.Rows
                    .Select(row => row.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                var analysis = BuildAnalysis(rows, queryResult, attempts);
                var context = DataAnalysisFinalContextFormatter.FormatFreeForm(
                    analysis,
                    decision: null,
                    rows,
                    schema: null);
                var sqlHash = CloudReadOnlyTextToSqlRepairClassifier.ComputeSqlHash(generated.Sql);
                await auditRecorder.RecordCloudReadOnlyTextToSqlFallbackAsync(
                    database,
                    AuditResults.Succeeded,
                    $"Cloud readonly Text-to-SQL fallback executed. RowsObserved={queryResult.ReturnedRowCount}; Truncated={queryResult.IsTruncated}; RepairAttempts={attempts.Count}.",
                    ComputeHash(safeQuestion),
                    sqlHash,
                    queryResult.ReturnedRowCount,
                    queryResult.IsTruncated,
                    attempts,
                    cancellationToken);

                return new CloudReadOnlyTextToSqlFallbackResult(
                    true,
                    context,
                    rows,
                    queryResult.ReturnedRowCount,
                    queryResult.IsTruncated,
                    sqlHash,
                    attempts,
                    "Cloud readonly Text-to-SQL fallback executed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var runtimeAttempt = CloudReadOnlyTextToSqlRepairClassifier.CreateAttemptRecord(
                    attemptIndex,
                    CloudReadOnlyTextToSqlFailureStage.Runtime,
                    generated.Sql,
                    ex.Message,
                    maxRepairAttempts: maxRepairAttempts);
                attempts.Add(runtimeAttempt);
                if (!runtimeAttempt.CanRetry)
                {
                    break;
                }

                previousSqlForRepair = generated.Sql;
            }
        }

        var safeMessage = attempts.LastOrDefault()?.SafeErrorSummary ??
                          "Cloud readonly Text-to-SQL fallback did not produce an executable readonly query.";
        await auditRecorder.RecordCloudReadOnlyTextToSqlFallbackAsync(
            database,
            AuditResults.Rejected,
            $"Cloud readonly Text-to-SQL fallback failed. LastError={safeMessage}; RepairAttempts={attempts.Count}.",
            ComputeHash(safeQuestion),
            attempts.LastOrDefault()?.SqlHash ?? string.Empty,
            0,
            false,
            attempts,
            cancellationToken);

        return Failed(safeMessage, attempts);
    }

    private static CloudReadOnlyTextToSqlFallbackResult Failed(
        string safeMessage,
        IReadOnlyList<CloudReadOnlyTextToSqlRepairAttemptRecord> attempts)
    {
        return new CloudReadOnlyTextToSqlFallbackResult(
            false,
            string.Empty,
            [],
            0,
            false,
            string.Empty,
            attempts,
            safeMessage);
    }

    private static AnalysisDto BuildAnalysis(
        IReadOnlyList<Dictionary<string, object?>> rows,
        DatabaseQueryResult queryResult,
        IReadOnlyCollection<CloudReadOnlyTextToSqlRepairAttemptRecord> attempts)
    {
        var metadata = rows.FirstOrDefault()?.Keys
            .Select(key => new MetadataItemDto
            {
                Name = key,
                Description = key
            })
            .ToList() ?? [];
        metadata.Add(new MetadataItemDto
        {
            Name = "rowCount",
            Description = queryResult.ReturnedRowCount.ToString()
        });
        metadata.Add(new MetadataItemDto
        {
            Name = "repairAttemptCount",
            Description = attempts.Count.ToString()
        });

        return new AnalysisDto
        {
            SourceLabel = "Cloud 已有正式只读数据（DataAnalysis/Text-to-SQL 补充分析）",
            Description = queryResult.IsTruncated
                ? "Cloud 只读 Text-to-SQL 补充查询已执行，结果已截断。"
                : "Cloud 只读 Text-to-SQL 补充查询已执行。",
            Metadata = metadata
        };
    }

    private static string? ValidateDatabase(BusinessDatabaseConnectionInfo database)
    {
        if (!database.IsEnabled)
        {
            return "Cloud readonly data source is disabled.";
        }

        if (!database.IsReadOnly)
        {
            return "Cloud readonly Text-to-SQL requires a readonly data source.";
        }

        if (database.ExternalSystemType != DataSourceExternalSystemType.CloudReadOnly)
        {
            return "Cloud readonly Text-to-SQL only supports CloudReadOnly data sources.";
        }

        return database.ReadOnlyCredentialVerified
            ? null
            : "Cloud read-only data source requires a verified readonly credential before execution.";
    }

    private static int ResolveLimit(BusinessDatabaseConnectionInfo database, int? requestedLimit)
    {
        var requested = requestedLimit.GetValueOrDefault(database.DefaultQueryLimit);
        if (requested <= 0)
        {
            requested = database.DefaultQueryLimit;
        }

        return Math.Clamp(requested, 1, database.MaxQueryLimit);
    }

    private static string ComputeHash(string? value)
    {
        return CloudReadOnlyTextToSqlRepairClassifier.ComputeSqlHash(value);
    }
}

internal sealed class DisabledCloudReadOnlyTextToSqlGenerator : ICloudReadOnlyTextToSqlGenerator
{
    public Task<CloudReadOnlyTextToSqlGenerationResult> GenerateAsync(
        CloudReadOnlyTextToSqlGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CloudReadOnlyTextToSqlGenerationResult.Failure(
            "Cloud readonly Text-to-SQL generator is not configured."));
    }
}
