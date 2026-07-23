using AICopilot.Services.Contracts;

namespace AICopilot.UnitTests;

public sealed class CloudReadOnlyTextToSqlRepairClassifierTests
{
    [Theory]
    [InlineData("syntax error at or near \"FROM\"", CloudReadOnlyTextToSqlFailureCode.Syntax)]
    [InlineData("relation \"device_log\" does not exist", CloudReadOnlyTextToSqlFailureCode.UnknownTable)]
    [InlineData("Table 'recipes' is not allowed for this data source.", CloudReadOnlyTextToSqlFailureCode.UnknownTable)]
    [InlineData("column \"device_code\" does not exist", CloudReadOnlyTextToSqlFailureCode.UnknownColumn)]
    [InlineData("Wildcard SELECT projections are not allowed in governed business queries.", CloudReadOnlyTextToSqlFailureCode.WildcardProjection)]
    public void Classifier_ShouldAllowSqlRepair_ForShapeErrors(
        string errorMessage,
        CloudReadOnlyTextToSqlFailureCode expectedCode)
    {
        var decision = CloudReadOnlyTextToSqlRepairClassifier.Classify(
            CloudReadOnlyTextToSqlFailureStage.Guard,
            errorMessage);

        decision.Code.Should().Be(expectedCode);
        decision.CanRepairSql.Should().BeTrue();
        decision.CanRetry.Should().BeTrue();
        decision.SafeSummary.Should().Be(errorMessage);
    }

    [Theory]
    [InlineData("Only SELECT statements are allowed.", CloudReadOnlyTextToSqlFailureCode.WriteSql)]
    [InlineData("安全拦截：仅允许执行 SELECT 或 WITH ... SELECT 查询。", CloudReadOnlyTextToSqlFailureCode.WriteSql)]
    [InlineData("Multiple SQL statements are not allowed.", CloudReadOnlyTextToSqlFailureCode.MultiStatement)]
    [InlineData("安全拦截：禁止在单次调用中执行多条 SQL 语句。", CloudReadOnlyTextToSqlFailureCode.MultiStatement)]
    [InlineData("Sensitive fields such as passwords, tokens, keys, or connection strings are not allowed.", CloudReadOnlyTextToSqlFailureCode.SensitiveField)]
    [InlineData("安全拦截：查询引用了当前业务数据源禁止访问的敏感字段。", CloudReadOnlyTextToSqlFailureCode.SensitiveField)]
    [InlineData("System catalog metadata is not allowed in business queries.", CloudReadOnlyTextToSqlFailureCode.SystemCatalog)]
    [InlineData("安全拦截：禁止访问数据库系统目录。", CloudReadOnlyTextToSqlFailureCode.SystemCatalog)]
    [InlineData("Cloud read-only data source requires a verified readonly credential before execution.", CloudReadOnlyTextToSqlFailureCode.Credential)]
    [InlineData("Current user is not authorized to query this business data source.", CloudReadOnlyTextToSqlFailureCode.Forbidden)]
    [InlineData("Business readonly query timed out.", CloudReadOnlyTextToSqlFailureCode.Timeout)]
    public void Classifier_ShouldBlockRepair_ForSafetyAndRuntimeBoundaryErrors(
        string errorMessage,
        CloudReadOnlyTextToSqlFailureCode expectedCode)
    {
        var decision = CloudReadOnlyTextToSqlRepairClassifier.Classify(
            CloudReadOnlyTextToSqlFailureStage.Runtime,
            errorMessage);

        decision.Code.Should().Be(expectedCode);
        decision.CanRepairSql.Should().BeFalse();
        decision.CanRetry.Should().BeFalse();
    }

    [Theory]
    [InlineData("DROP TABLE devices")]
    [InlineData("DELETE FROM devices")]
    [InlineData("UPDATE devices SET device_name = 'bad'")]
    public void Classifier_ShouldNotReparseSqlTextAsASecondGuard(string rawSqlText)
    {
        var decision = CloudReadOnlyTextToSqlRepairClassifier.Classify(
            CloudReadOnlyTextToSqlFailureStage.Guard,
            rawSqlText);

        decision.Code.Should().Be(CloudReadOnlyTextToSqlFailureCode.Unknown);
        decision.CanRepairSql.Should().BeFalse();
        decision.CanRetry.Should().BeFalse();
    }

    [Fact]
    public void Classifier_ShouldExposeOnlyGovernedTable_ForCloudReadOnlyPermissionDenied()
    {
        var decision = CloudReadOnlyTextToSqlRepairClassifier.Classify(
            CloudReadOnlyTextToSqlFailureStage.Runtime,
            "42501: permission denied for table mfg_processes; Password=secret");

        decision.Code.Should().Be(CloudReadOnlyTextToSqlFailureCode.Forbidden);
        decision.CanRepairSql.Should().BeFalse();
        decision.CanRetry.Should().BeFalse();
        decision.SafeSummary.Should().Be("CloudReadOnly permission denied for table mfg_processes.");
        decision.SafeSummary.Should().NotContain("secret");

        var disallowed = CloudReadOnlyTextToSqlRepairClassifier.Classify(
            CloudReadOnlyTextToSqlFailureStage.Runtime,
            "42501: permission denied for table pg_user");
        disallowed.SafeSummary.Should().Be("CloudReadOnly permission denied.");
        disallowed.SafeSummary.Should().NotContain("pg_user");
    }

    [Fact]
    public void AttemptRecord_ShouldStoreOnlyHashesLengthsAndSafeSummary()
    {
        const string sql = "SELECT password_hash FROM devices WHERE message = 'secret customer'";
        const string error = "Sensitive field password_hash rejected while evaluating literal 'secret customer'.";

        var record = CloudReadOnlyTextToSqlRepairClassifier.CreateAttemptRecord(
            1,
            CloudReadOnlyTextToSqlFailureStage.Guard,
            sql,
            error,
            new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero));

        record.FailureCode.Should().Be(CloudReadOnlyTextToSqlFailureCode.SensitiveField);
        record.CanRepairSql.Should().BeFalse();
        record.CanRetry.Should().BeFalse();
        record.SqlHash.Should().Be(CloudReadOnlyTextToSqlRepairClassifier.ComputeSqlHash(sql));
        record.SqlLength.Should().Be(sql.Length);
        record.SafeErrorSummary.Should().NotContain("password");
        record.SafeErrorSummary.Should().NotContain("password_hash");
        record.SafeErrorSummary.Should().NotContain("secret");
        record.SafeErrorSummary.Should().NotContain("secret customer");
        record.ToString().Should().NotContain(sql);
    }

    [Fact]
    public void AttemptRecord_ShouldCapRetryAfterConfiguredRepairAttempts()
    {
        const string error = "column \"device_code\" does not exist";

        var first = CloudReadOnlyTextToSqlRepairClassifier.CreateAttemptRecord(
            1,
            CloudReadOnlyTextToSqlFailureStage.Runtime,
            "SELECT device_code FROM devices",
            error);
        var overLimit = CloudReadOnlyTextToSqlRepairClassifier.CreateAttemptRecord(
            CloudReadOnlyTextToSqlOptions.DefaultMaxRepairAttempts + 1,
            CloudReadOnlyTextToSqlFailureStage.Runtime,
            "SELECT device_code FROM devices",
            error);

        first.CanRepairSql.Should().BeTrue();
        first.CanRetry.Should().BeTrue();
        overLimit.CanRepairSql.Should().BeTrue();
        overLimit.CanRetry.Should().BeFalse();
    }
}
