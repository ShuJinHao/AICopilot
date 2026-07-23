using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public sealed record BusinessTextToSqlFallbackResult(
    bool Succeeded,
    string Context,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    int RowCount,
    bool IsTruncated,
    string QueryHash,
    IReadOnlyList<CloudReadOnlyTextToSqlRepairAttemptRecord> RepairAttempts,
    string SafeMessage);

public interface IBusinessTextToSqlFallbackRunner
{
    Task<BusinessTextToSqlFallbackResult> RunAsync(
        BusinessQueryContext context,
        BusinessDatabaseConnectionInfo database,
        string? question,
        int? requestedLimit,
        CancellationToken cancellationToken);
}

public sealed class BusinessTextToSqlFallbackRunner(
    IBusinessTextToSqlGenerator generator,
    IDatabaseConnector databaseConnector,
    DataAnalysisAuditRecorder auditRecorder,
    IBusinessDataSourceProfileRegistry profileRegistry,
    IOptions<CloudReadOnlyTextToSqlOptions>? options = null)
    : IBusinessTextToSqlFallbackRunner
{
    public async Task<BusinessTextToSqlFallbackResult> RunAsync(
        BusinessQueryContext context,
        BusinessDatabaseConnectionInfo database,
        string? question,
        int? requestedLimit,
        CancellationToken cancellationToken)
    {
        var registeredSourceProfile = profileRegistry.GetRequired(
            context.SourceKey,
            context.SourceType);
        if (!registeredSourceProfile.TryResolveCapabilityQueryProfile(
                context.Capability,
                out var sourceProfile))
        {
            const string capabilityProfileMessage =
                "Governed Text-to-SQL has no capability-specific table and column profile for the confirmed query capability.";
            await auditRecorder.RecordBusinessTextToSqlFallbackAsync(
                database,
                AuditResults.Rejected,
                capabilityProfileMessage,
                ComputeHash(question),
                string.Empty,
                0,
                false,
                [],
                cancellationToken);
            return Failed(capabilityProfileMessage, []);
        }

        var resolvedOptions = options?.Value ?? new CloudReadOnlyTextToSqlOptions();
        if (!resolvedOptions.Enabled)
        {
            const string disabledMessage = "Governed business Text-to-SQL fallback is disabled.";
            await auditRecorder.RecordBusinessTextToSqlFallbackAsync(
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

        var validationError = ValidateDatabase(context, database, sourceProfile);
        if (validationError is not null)
        {
            await auditRecorder.RecordBusinessTextToSqlFallbackAsync(
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

        if (!sourceProfile.SupportsTextToSqlFallback ||
            sourceProfile.TextToSql is null)
        {
            const string profileMessage =
                "Governed Text-to-SQL requires a registered source profile with dialect and schema metadata.";
            await auditRecorder.RecordBusinessTextToSqlFallbackAsync(
                database,
                AuditResults.Rejected,
                profileMessage,
                ComputeHash(question),
                string.Empty,
                0,
                false,
                [],
                cancellationToken);
            return Failed(profileMessage, []);
        }

        var limit = ResolveLimit(database, requestedLimit);
        var attempts = new List<CloudReadOnlyTextToSqlRepairAttemptRecord>();
        var safeQuestion = string.IsNullOrWhiteSpace(question)
            ? "Governed readonly business data query"
            : question.Trim();
        var maxRepairAttempts = resolvedOptions.ResolveMaxRepairAttempts();
        var queryOptions = resolvedOptions.ResolveQueryOptions();
        string? previousSqlForRepair = null;

        for (var attemptIndex = 1;
             attemptIndex <= maxRepairAttempts + 1;
             attemptIndex++)
        {
            var generationRequest = new BusinessTextToSqlGenerationRequest(
                safeQuestion,
                limit,
                sourceProfile,
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
                    generated.FailureReason ?? "Governed business Text-to-SQL generation failed.",
                    maxRepairAttempts: maxRepairAttempts,
                    securityProfile: sourceProfile.QuerySecurity);
                attempts.Add(draftAttempt);
                break;
            }

            try
            {
                var queryResult = await databaseConnector.ExecuteQueryWithMetadataAsync(
                    database,
                    generated.Sql,
                    sourceProfile.QuerySecurity,
                    generated.Parameters,
                    options: queryOptions,
                    cancellationToken: cancellationToken);
                var rows = queryResult.Rows
                    .Select(row => row.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                var analysis = BuildAnalysis(database, rows, queryResult, attempts);
                var formattedContext = DataAnalysisFinalContextFormatter.FormatFreeForm(
                    analysis,
                    decision: null,
                    rows,
                    schema: null);
                var sqlHash = CloudReadOnlyTextToSqlRepairClassifier.ComputeSqlHash(generated.Sql);
                await auditRecorder.RecordBusinessTextToSqlFallbackAsync(
                    database,
                    AuditResults.Succeeded,
                    $"Governed business Text-to-SQL fallback executed. RowsObserved={queryResult.ReturnedRowCount}; Truncated={queryResult.IsTruncated}; RepairAttempts={attempts.Count}.",
                    ComputeHash(safeQuestion),
                    sqlHash,
                    queryResult.ReturnedRowCount,
                    queryResult.IsTruncated,
                    attempts,
                    cancellationToken);

                return new BusinessTextToSqlFallbackResult(
                    true,
                    formattedContext,
                    rows,
                    queryResult.ReturnedRowCount,
                    queryResult.IsTruncated,
                    sqlHash,
                    attempts,
                    "Governed business Text-to-SQL fallback executed.");
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
                    maxRepairAttempts: maxRepairAttempts,
                    securityProfile: sourceProfile.QuerySecurity);
                attempts.Add(runtimeAttempt);
                if (!runtimeAttempt.CanRetry)
                {
                    break;
                }

                previousSqlForRepair = generated.Sql;
            }
        }

        var safeMessage = attempts.LastOrDefault()?.SafeErrorSummary ??
                          "Governed business Text-to-SQL fallback did not produce an executable readonly query.";
        await auditRecorder.RecordBusinessTextToSqlFallbackAsync(
            database,
            AuditResults.Rejected,
            $"Governed business Text-to-SQL fallback failed. LastError={safeMessage}; RepairAttempts={attempts.Count}.",
            ComputeHash(safeQuestion),
            attempts.LastOrDefault()?.SqlHash ?? string.Empty,
            0,
            false,
            attempts,
            cancellationToken);

        return Failed(safeMessage, attempts);
    }

    private static BusinessTextToSqlFallbackResult Failed(
        string safeMessage,
        IReadOnlyList<CloudReadOnlyTextToSqlRepairAttemptRecord> attempts)
    {
        return new BusinessTextToSqlFallbackResult(
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
        BusinessDatabaseConnectionInfo database,
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
            SourceLabel = $"{database.Name}（受控 Text-to-SQL 补充分析）",
            Description = queryResult.IsTruncated
                ? "受控只读 Text-to-SQL 补充查询已执行，结果已截断。"
                : "受控只读 Text-to-SQL 补充查询已执行。",
            Metadata = metadata
        };
    }

    private static string? ValidateDatabase(
        BusinessQueryContext context,
        BusinessDatabaseConnectionInfo database,
        BusinessDataSourceProfile sourceProfile)
    {
        if (!context.IsConfirmed)
        {
            return "Governed Text-to-SQL requires a confirmed business query context.";
        }

        if (context.DataSourceId != database.Id ||
            context.SourceType != database.ExternalSystemType ||
            context.SourceType != sourceProfile.SourceType ||
            database.Provider != sourceProfile.DatabaseProvider ||
            !string.Equals(context.SourceKey, sourceProfile.Code, StringComparison.OrdinalIgnoreCase))
        {
            return "Governed Text-to-SQL source binding does not match the confirmed profile.";
        }

        if (!database.IsEnabled)
        {
            return "Governed readonly data source is disabled.";
        }

        if (!database.IsReadOnly)
        {
            return "Governed Text-to-SQL requires a readonly data source.";
        }

        return database.ReadOnlyCredentialVerified
            ? null
            : "Governed Text-to-SQL requires a verified readonly credential before execution.";
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

internal sealed class DisabledBusinessTextToSqlGenerator : IBusinessTextToSqlGenerator
{
    public Task<BusinessTextToSqlGenerationResult> GenerateAsync(
        BusinessTextToSqlGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(BusinessTextToSqlGenerationResult.Failure(
            "Governed business Text-to-SQL generator is not configured."));
    }
}
