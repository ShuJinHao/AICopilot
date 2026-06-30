using AICopilot.Services.Contracts;

namespace AICopilot.BackendTests;

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
    [InlineData("Multiple SQL statements are not allowed.", CloudReadOnlyTextToSqlFailureCode.MultiStatement)]
    [InlineData("Sensitive fields such as passwords, tokens, keys, or connection strings are not allowed.", CloudReadOnlyTextToSqlFailureCode.SensitiveField)]
    [InlineData("System catalog metadata is not allowed in business queries.", CloudReadOnlyTextToSqlFailureCode.SystemCatalog)]
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
