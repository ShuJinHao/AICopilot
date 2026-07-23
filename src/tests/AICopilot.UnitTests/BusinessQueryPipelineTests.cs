using System.Text.Json;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.DataAnalysisService.BusinessDatabases;
using AICopilot.Services.Contracts;

namespace AICopilot.UnitTests;

public sealed class BusinessQueryPipelineTests
{
    [Fact]
    public void ProfileRegistry_ShouldExposeCloudAsOnlyRealExternalSource()
    {
        var registry = new BusinessDataSourceProfileRegistry(
        [
            new CloudReadOnlyBusinessDataSourceProfileProvider(),
            new SimulationBusinessDataSourceProfileProvider(),
            new StubBusinessDataSourceProfileProvider(CreateNonCloudProfile("mes-readonly")),
            new StubBusinessDataSourceProfileProvider(CreateNonCloudProfile("erp-readonly"))
        ]);

        var profiles = registry.GetAll();

        profiles.Should().ContainSingle(profile => profile.IsRealExternalSource)
            .Which.SourceType.Should().Be(DataSourceExternalSystemType.CloudReadOnly);
        registry.GetRequired(
                "simulation-business",
                DataSourceExternalSystemType.SimulationBusiness)
            .RequiresExplicitSelection.Should().BeTrue();
        registry.TryGet(
                "mes-readonly",
                DataSourceExternalSystemType.NonCloud,
                out var mesProfile)
            .Should().BeTrue();
        mesProfile.Code.Should().Be("mes-readonly");
        mesProfile.TextToSql.Should().NotBeNull();
        mesProfile.TextToSql!.Dialect.Should().Be("PostgreSQL");
        mesProfile.TextToSql.JoinHints.Should().ContainSingle();
        registry.TryGet(
                "erp-readonly",
                DataSourceExternalSystemType.NonCloud,
                out var erpProfile)
            .Should().BeTrue();
        erpProfile.Code.Should().Be("erp-readonly");
        registry.TryGet(
                "mes-readonly",
                DataSourceExternalSystemType.CloudReadOnly,
                out _)
            .Should().BeFalse("source type is a category check and cannot redirect a confirmed source key");
    }

    [Theory]
    [InlineData(BusinessQueryOutcome.Unsupported, true)]
    [InlineData(BusinessQueryOutcome.Unavailable, true)]
    [InlineData(BusinessQueryOutcome.Success, false)]
    [InlineData(BusinessQueryOutcome.Empty, false)]
    [InlineData(BusinessQueryOutcome.NeedClarification, false)]
    [InlineData(BusinessQueryOutcome.Unauthorized, false)]
    public void FallbackPolicy_ShouldOnlyAllowEligibleSameSourceOutcomes(
        BusinessQueryOutcome outcome,
        bool expected)
    {
        var context = CreateContext(
            DataSourceExternalSystemType.CloudReadOnly,
            sourceExplicitlySelected: true);
        var pluginResult = BusinessQueryProviderResult.FromOutcome(
            context,
            "cloud-plugin",
            outcome,
            "safe");

        var decision = BusinessQueryFallbackPolicy.EvaluateSameSourceTextToSql(
            context,
            pluginResult,
            StandardBusinessDataSourceProfiles.CloudReadOnly);

        decision.IsEligible.Should().Be(expected);
        decision.RequiresModelDecision.Should().Be(expected);
    }

    [Fact]
    public void FallbackPolicy_ShouldRejectCrossSourceAndImplicitSimulation()
    {
        var cloudContext = CreateContext(
            DataSourceExternalSystemType.CloudReadOnly,
            sourceExplicitlySelected: true);
        var crossSourceResult = BusinessQueryProviderResult.FromOutcome(
            cloudContext with
            {
                SourceKey = "mes",
                SourceType = DataSourceExternalSystemType.NonCloud
            },
            "mes-plugin",
            BusinessQueryOutcome.Unsupported,
            "safe");
        BusinessQueryFallbackPolicy.EvaluateSameSourceTextToSql(
                cloudContext,
                crossSourceResult,
                StandardBusinessDataSourceProfiles.CloudReadOnly)
            .ReasonCode.Should().Be("cross_source_fallback_forbidden");

        var simulationContext = CreateContext(
            DataSourceExternalSystemType.SimulationBusiness,
            sourceExplicitlySelected: false);
        var simulationResult = BusinessQueryProviderResult.FromOutcome(
            simulationContext,
            "simulation-plugin",
            BusinessQueryOutcome.Unsupported,
            "safe");
        var simulationProfile = new SimulationBusinessDataSourceProfileProvider().Profile with
        {
            SupportsTextToSqlFallback = true
        };
        BusinessQueryFallbackPolicy.EvaluateSameSourceTextToSql(
                simulationContext,
                simulationResult,
                simulationProfile)
            .ReasonCode.Should().Be("explicit_source_selection_required");
    }

    [Fact]
    public void ConfirmedContext_ShouldReuseOnlyTheSameConfirmedScopeAndKeepFollowUpProjection()
    {
        var store = new BusinessQueryContextStore();
        var confirmedPlan = new SemanticQueryPlan(
            "Analysis.DeviceLog.List",
            SemanticQueryTarget.DeviceLog,
            SemanticQueryKind.List,
            "查询今天的警告设备日志",
            new SemanticProjection(["client_code", "level", "log_time"]),
            [
                new SemanticFilter("deviceCode", SemanticFilterOperator.Equal, "DEV-001"),
                new SemanticFilter("level", SemanticFilterOperator.Equal, "Warning")
            ],
            new SemanticTimeRange(
                "log_time",
                new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero)),
            null,
            50);
        var confirmed = CreateContext(
                DataSourceExternalSystemType.CloudReadOnly,
                sourceExplicitlySelected: true) with
            {
                Capability = BusinessDataCapability.DeviceLog,
                SemanticPlan = confirmedPlan
            };
        confirmed = confirmed.Confirm();
        store.Remember(confirmed);

        var followUpPlan = confirmedPlan with
        {
            QueryText = "只显示日志级别和时间",
            Projection = new SemanticProjection(["level", "log_time"])
        };
        var requested = confirmed with
        {
            Question = "same task follow-up",
            SourceExplicitlySelected = false,
            Confirmation = new BusinessQueryConfirmation(false, false, false, false, false),
            ConfirmedAtUtc = null,
            SemanticPlan = followUpPlan
        };

        var resolved = store.Resolve(requested);

        resolved.SourceKey.Should().Be(confirmed.SourceKey);
        resolved.DataSourceId.Should().Be(confirmed.DataSourceId);
        resolved.SourceType.Should().Be(confirmed.SourceType);
        resolved.Capability.Should().Be(BusinessDataCapability.DeviceLog);
        resolved.Question.Should().Be("same task follow-up");
        resolved.SemanticPlan.Should().BeSameAs(followUpPlan);
        resolved.IsConfirmed.Should().BeTrue();

        var changedScope = requested with
        {
            SemanticPlan = followUpPlan with
            {
                Filters =
                [
                    new SemanticFilter("deviceCode", SemanticFilterOperator.Equal, "DEV-002")
                ]
            }
        };
        var changedScopeResolved = store.Resolve(changedScope);
        changedScopeResolved.IsConfirmed.Should().BeFalse(
            "a different object/filter scope must be confirmed instead of inheriting an old scope");
        changedScopeResolved.SourceKey.Should().Be(confirmed.SourceKey);
        changedScopeResolved.DataSourceId.Should().Be(confirmed.DataSourceId);
        changedScopeResolved.Confirmation.Source.Should().BeTrue();
        changedScopeResolved.Confirmation.Capability.Should().BeTrue();
        changedScopeResolved.Confirmation.TimeRange.Should().BeTrue();
        changedScopeResolved.Confirmation.BusinessObject.Should().BeFalse();
        changedScopeResolved.Confirmation.Filters.Should().BeFalse();

        var changedTime = requested with
        {
            SemanticPlan = followUpPlan with
            {
                TimeRange = new SemanticTimeRange(
                    "log_time",
                    new DateTimeOffset(2026, 7, 22, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero))
            }
        };
        var changedTimeResolved = store.Resolve(changedTime);
        changedTimeResolved.SourceKey.Should().Be(confirmed.SourceKey);
        changedTimeResolved.Confirmation.Source.Should().BeTrue();
        changedTimeResolved.Confirmation.Capability.Should().BeTrue();
        changedTimeResolved.Confirmation.BusinessObject.Should().BeTrue();
        changedTimeResolved.Confirmation.Filters.Should().BeTrue();
        changedTimeResolved.Confirmation.TimeRange.Should().BeFalse();
        changedTimeResolved.IsConfirmed.Should().BeFalse();
    }

    [Theory]
    [InlineData(SemanticQueryTarget.Device)]
    [InlineData(SemanticQueryTarget.Process)]
    [InlineData(SemanticQueryTarget.ClientRelease)]
    public void ConfirmationPolicy_ShouldNotInferScopeFromCatalogTargetOrSessionConfidence(
        SemanticQueryTarget target)
    {
        var plan = new SemanticQueryPlan(
            "Analysis.Catalog.List",
            target,
            SemanticQueryKind.List,
            null,
            new SemanticProjection([]),
            [],
            null,
            null,
            20);

        var confirmation = BusinessQueryConfirmationPolicy.FromSemanticPlan(
            sourceConfirmed: true,
            capabilityConfirmed: true,
            confidenceConfirmed: true,
            semanticPlan: plan);

        confirmation.Source.Should().BeTrue();
        confirmation.Capability.Should().BeTrue();
        confirmation.BusinessObject.Should().BeFalse();
        confirmation.TimeRange.Should().BeFalse();
        confirmation.Filters.Should().BeFalse();
        confirmation.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void ConfirmedContext_ShouldNotOverrideAnExplicitlySelectedDifferentSource()
    {
        var store = new BusinessQueryContextStore();
        var confirmed = CreateContext(
                DataSourceExternalSystemType.CloudReadOnly,
                sourceExplicitlySelected: true)
            .Confirm();
        store.Remember(confirmed);
        var explicitlySelectedMes = confirmed with
        {
            SourceKey = "mes-readonly",
            DataSourceId = Guid.NewGuid(),
            SourceType = DataSourceExternalSystemType.NonCloud,
            SourceExplicitlySelected = true,
            Confirmation = new BusinessQueryConfirmation(false, false, false, false, false),
            ConfirmedAtUtc = null
        };

        var resolved = store.Resolve(explicitlySelectedMes);

        resolved.Should().Be(explicitlySelectedMes);
    }

    [Fact]
    public void ConfirmedContext_ShouldKeepConfirmedSourceWhenFollowUpCarriesADifferentDefaultSource()
    {
        var store = new BusinessQueryContextStore();
        var confirmedMes = CreateContext(
                DataSourceExternalSystemType.NonCloud,
                sourceExplicitlySelected: true) with
            {
                SourceKey = "mes-readonly"
            };
        confirmedMes = confirmedMes.Confirm();
        store.Remember(confirmedMes);
        var implicitCloudDefault = confirmedMes with
        {
            SourceKey = StandardBusinessDataSourceProfiles.CloudReadOnly.Code,
            DataSourceId = null,
            SourceType = DataSourceExternalSystemType.CloudReadOnly,
            SourceExplicitlySelected = false,
            Confirmation = new BusinessQueryConfirmation(false, false, false, false, false),
            ConfirmedAtUtc = null
        };

        var resolved = store.Resolve(implicitCloudDefault);

        resolved.SourceKey.Should().Be("mes-readonly");
        resolved.DataSourceId.Should().Be(confirmedMes.DataSourceId);
        resolved.SourceType.Should().Be(DataSourceExternalSystemType.NonCloud);
        resolved.IsConfirmed.Should().BeTrue();
    }

    [Theory]
    [InlineData(BusinessDataCapability.Device, "devices", true)]
    [InlineData(BusinessDataCapability.Device, "device_logs", false)]
    [InlineData(BusinessDataCapability.DeviceLog, "device_logs", true)]
    [InlineData(BusinessDataCapability.Capacity, "hourly_capacity", true)]
    [InlineData(BusinessDataCapability.ProductionRecord, "pass_station_records", true)]
    [InlineData(BusinessDataCapability.Process, "mfg_processes", true)]
    [InlineData(BusinessDataCapability.ClientRelease, "devices", false)]
    public void CloudProfile_ShouldResolveOnlyCapabilitySpecificTables(
        BusinessDataCapability capability,
        string table,
        bool expected)
    {
        var resolved = StandardBusinessDataSourceProfiles.CloudReadOnly
            .TryResolveCapabilityQueryProfile(capability, out var profile);

        if (capability == BusinessDataCapability.ClientRelease)
        {
            resolved.Should().BeFalse();
            return;
        }

        resolved.Should().BeTrue();
        profile.Capabilities.Should().ContainSingle().Which.Should().Be(capability);
        profile.QuerySecurity.AllowedTables.Contains(table).Should().Be(expected);
    }

    [Fact]
    public void ConfirmedContext_ShouldExpireAfterConfiguredTtl()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero));
        var store = new BusinessQueryContextStore(
            clock,
            TimeSpan.FromMinutes(30));
        var confirmed = CreateContext(
                DataSourceExternalSystemType.CloudReadOnly,
                sourceExplicitlySelected: true)
            .Confirm(clock.GetUtcNow());
        store.Remember(confirmed);
        var followUp = confirmed with
        {
            Confirmation = new BusinessQueryConfirmation(false, false, false, false, false),
            ConfirmedAtUtc = null
        };

        clock.UtcNow = clock.UtcNow.AddMinutes(31);

        store.Resolve(followUp).IsConfirmed.Should().BeFalse();
    }

    [Fact]
    public void PendingConfirmation_ShouldRequireExactOneTimeChallengeReply()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero));
        var store = new BusinessQueryContextStore(clock, TimeSpan.FromMinutes(30));
        var pendingPlan = new SemanticQueryPlan(
            "Analysis.DeviceLog.Range",
            SemanticQueryTarget.DeviceLog,
            SemanticQueryKind.Range,
            null,
            new SemanticProjection(["level", "log_time"]),
            [new SemanticFilter("deviceCode", SemanticFilterOperator.Equal, "DEV-001")],
            new SemanticTimeRange(
                "log_time",
                clock.UtcNow.AddDays(-1),
                clock.UtcNow),
            null,
            20);
        var pending = CreateContext(
                DataSourceExternalSystemType.CloudReadOnly,
                sourceExplicitlySelected: false) with
            {
                Capability = BusinessDataCapability.DeviceLog,
                Confirmation = new BusinessQueryConfirmation(false, false, false, false, false),
                SemanticPlan = pendingPlan
            };
        var challenge = store.BeginConfirmation(pending);

        store.TryConfirmPending(
                pending.TaskId,
                $"确认 {challenge.Token}",
                out _)
            .Should().BeFalse();
        store.TryConfirmPending(
                pending.TaskId,
                $"确认查询 {challenge.Token}",
                out var confirmed)
            .Should().BeTrue();
        confirmed.IsConfirmed.Should().BeTrue();
        confirmed.SourceExplicitlySelected.Should().BeTrue();
        confirmed.SemanticPlan.Should().BeSameAs(pendingPlan);
        store.TryConfirmPending(
                pending.TaskId,
                $"确认查询 {challenge.Token}",
                out _)
            .Should().BeFalse("the confirmation challenge is single-use");
    }

    [Fact]
    public void PendingConfirmation_ShouldExpireWithoutConfirmingContext()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero));
        var store = new BusinessQueryContextStore(clock, TimeSpan.FromMinutes(5));
        var pending = CreateContext(
                DataSourceExternalSystemType.CloudReadOnly,
                sourceExplicitlySelected: false) with
            {
                Confirmation = new BusinessQueryConfirmation(false, false, false, false, false),
                SemanticPlan = new SemanticQueryPlan(
                    "Analysis.Device.List",
                    SemanticQueryTarget.Device,
                    SemanticQueryKind.List,
                    null,
                    new SemanticProjection(["deviceCode"]),
                    [],
                    null,
                    null,
                    20)
            };
        var challenge = store.BeginConfirmation(pending);
        clock.UtcNow = clock.UtcNow.AddMinutes(6);

        store.TryConfirmPending(
                pending.TaskId,
                $"确认查询 {challenge.Token}",
                out _)
            .Should().BeFalse();
        store.Resolve(pending).IsConfirmed.Should().BeFalse();
    }

    [Fact]
    public void ProviderRegistry_ShouldRequireExplicitSimulationSelection()
    {
        var provider = new StubBusinessQueryProvider(
            "simulation-business",
            DataSourceExternalSystemType.SimulationBusiness,
            BusinessDataCapability.Device);
        var registry = new BusinessQueryProviderRegistry(
            [provider],
            CreateProfileRegistry(provider));
        var implicitContext = CreateContext(
            DataSourceExternalSystemType.SimulationBusiness,
            sourceExplicitlySelected: false);

        var implicitAction = () => registry.ResolveRequired(implicitContext);
        implicitAction.Should().Throw<InvalidOperationException>()
            .WithMessage("*requires explicit source selection*");
        registry.ResolveRequired(
                implicitContext with { SourceExplicitlySelected = true })
            .Should().BeSameAs(provider);
    }

    [Fact]
    public void CloudProviderRegistry_ShouldAcceptPluginOnlyClientReleaseAndKeepFallbackDisabled()
    {
        var provider = new AllCapabilityCloudProvider();
        var registry = new BusinessQueryProviderRegistry(
            [provider],
            new TestProfileRegistry([StandardBusinessDataSourceProfiles.CloudReadOnly]));
        var context = CreateContext(
            DataSourceExternalSystemType.CloudReadOnly,
            sourceExplicitlySelected: true) with
        {
            Capability = BusinessDataCapability.ClientRelease
        };
        var pluginResult = BusinessQueryProviderResult.FromOutcome(
            context,
            provider.ProviderCode,
            BusinessQueryOutcome.Unsupported,
            "unsupported");

        registry.ResolveRequired(context).Should().BeSameAs(provider);
        BusinessQueryFallbackPolicy.EvaluateSameSourceTextToSql(
                context,
                pluginResult,
                StandardBusinessDataSourceProfiles.CloudReadOnly)
            .ReasonCode.Should().Be("capability_fallback_disabled");
    }

    [Fact]
    public void ProviderRegistry_ShouldKeepMesAndErpProvidersIsolatedByConfirmedSourceKey()
    {
        var mesProvider = new StubBusinessQueryProvider(
            "mes-readonly",
            DataSourceExternalSystemType.NonCloud,
            BusinessDataCapability.Device);
        var erpProvider = new StubBusinessQueryProvider(
            "erp-readonly",
            DataSourceExternalSystemType.NonCloud,
            BusinessDataCapability.Device);
        var registry = new BusinessQueryProviderRegistry(
            [mesProvider, erpProvider],
            CreateProfileRegistry(mesProvider, erpProvider));
        var mesContext = CreateContext(
            DataSourceExternalSystemType.NonCloud,
            sourceExplicitlySelected: true) with
        {
            SourceKey = "mes-readonly"
        };

        registry.ResolveRequired(mesContext).Should().BeSameAs(mesProvider);
        registry.ResolveRequired(mesContext with { SourceKey = "erp-readonly" })
            .Should().BeSameAs(erpProvider);
        Action resolveUnknown = () => registry.ResolveRequired(
            mesContext with { SourceKey = "unknown-noncloud" });
        resolveUnknown.Should().Throw<InvalidOperationException>()
            .WithMessage("*No governed business query provider is registered*");
        Action resolveWrongType = () => registry.ResolveRequired(
            mesContext with { SourceType = DataSourceExternalSystemType.CloudReadOnly });
        resolveWrongType.Should().Throw<InvalidOperationException>()
            .WithMessage("*No governed business query provider is registered*");
    }

    [Fact]
    public void ProviderResultContract_ShouldRejectMismatchedIdentityEvenForSuccessAndEmpty()
    {
        var context = CreateContext(
            DataSourceExternalSystemType.CloudReadOnly,
            sourceExplicitlySelected: true);
        var provider = new StubBusinessQueryProvider(
            context.SourceKey,
            context.SourceType,
            context.Capability);

        foreach (var outcome in new[]
                 {
                     BusinessQueryOutcome.Success,
                     BusinessQueryOutcome.Empty
                 })
        {
            var mismatched = BusinessQueryProviderResult.FromOutcome(
                context with { DataSourceId = Guid.NewGuid() },
                provider.ProviderCode,
                outcome,
                "safe");

            var action = () => BusinessQueryProviderResultContract.EnsureMatches(
                context,
                provider,
                mismatched);

            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*outside the confirmed source and capability context*");
        }
    }

    [Fact]
    public void ProviderResultContract_ShouldRequireEmptyOutcomeForZeroRows()
    {
        var context = CreateContext(
            DataSourceExternalSystemType.CloudReadOnly,
            sourceExplicitlySelected: true);
        var provider = new StubBusinessQueryProvider(
            context.SourceKey,
            context.SourceType,
            context.Capability);
        var invalidSuccess = BusinessQueryProviderResult.FromOutcome(
            context,
            provider.ProviderCode,
            BusinessQueryOutcome.Success,
            "invalid zero-row success");

        var action = () => BusinessQueryProviderResultContract.EnsureMatches(
            context,
            provider,
            invalidSuccess);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*invalid structured outcome payload*");
    }

    [Fact]
    public void ProviderResultContract_ShouldAllowDeclaredMesFieldsAndRejectExtraSensitiveFields()
    {
        var context = CreateContext(
            DataSourceExternalSystemType.NonCloud,
            sourceExplicitlySelected: true) with
        {
            SourceKey = "mes-readonly",
            Capability = BusinessDataCapability.ProductionRecord
        };
        var provider = new StubBusinessQueryProvider(
            context.SourceKey,
            context.SourceType,
            context.Capability,
            allowedResultFields: new HashSet<string>(
                ["recordCode", "quantity", "payload", "operator_secret"],
                StringComparer.OrdinalIgnoreCase),
            blockedResultFieldFragments: new HashSet<string>(
                ["operator_secret"],
                StringComparer.OrdinalIgnoreCase));
        BusinessQueryProviderResult Result(Dictionary<string, object?> row) =>
            new(
                BusinessQueryOutcome.Success,
                provider.ProviderCode,
                context.SourceKey,
                context.DataSourceId,
                context.SourceType,
                context.Capability,
                [row],
                1,
                false,
                "mes-plugin",
                "MES",
                DateTimeOffset.UtcNow,
                "safe");

        var allowed = () => BusinessQueryProviderResultContract.EnsureMatches(
            context,
            provider,
            Result(new Dictionary<string, object?>
            {
                ["recordCode"] = "R-001",
                ["quantity"] = 10
            }));
        allowed.Should().NotThrow();

        var extraField = () => BusinessQueryProviderResultContract.EnsureMatches(
            context,
            provider,
            Result(new Dictionary<string, object?>
            {
                ["recordCode"] = "R-001",
                ["unexpected"] = "not-declared"
            }));
        extraField.Should().Throw<InvalidOperationException>()
            .WithMessage("*outside its declared result contract*");

        var sensitiveField = () => BusinessQueryProviderResultContract.EnsureMatches(
            context,
            provider,
            Result(new Dictionary<string, object?>
            {
                ["recordCode"] = "R-001",
                ["operator_secret"] = "hidden"
            }));
        sensitiveField.Should().Throw<InvalidOperationException>()
            .WithMessage("*outside its declared result contract*");

        var nestedSensitiveField = () => BusinessQueryProviderResultContract.EnsureMatches(
            context,
            provider,
            Result(new Dictionary<string, object?>
            {
                ["recordCode"] = "R-001",
                ["payload"] = new Dictionary<string, object?>
                {
                    ["operator_secret"] = "hidden"
                }
            }));
        nestedSensitiveField.Should().Throw<InvalidOperationException>()
            .WithMessage("*blocked or unsupported nested*");

        using var json = JsonDocument.Parse(
            """{"nested":{"operator_secret":"hidden"}}""");
        var jsonSensitiveField = () => BusinessQueryProviderResultContract.EnsureMatches(
            context,
            provider,
            Result(new Dictionary<string, object?>
            {
                ["recordCode"] = "R-001",
                ["payload"] = json.RootElement.Clone()
            }));
        jsonSensitiveField.Should().Throw<InvalidOperationException>()
            .WithMessage("*blocked or unsupported nested*");

        var unknownComplexValue = () => BusinessQueryProviderResultContract.EnsureMatches(
            context,
            provider,
            Result(new Dictionary<string, object?>
            {
                ["recordCode"] = "R-001",
                ["payload"] = new UnserializablePayload()
            }));
        unknownComplexValue.Should().Throw<InvalidOperationException>()
            .WithMessage("*blocked or unsupported nested*");
    }

    [Fact]
    public void ProviderResultContract_ShouldAllowProductionFieldSchemaDtoButRejectSensitiveAdditionalFields()
    {
        var context = CreateContext(
            DataSourceExternalSystemType.CloudReadOnly,
            sourceExplicitlySelected: true) with
        {
            Capability = BusinessDataCapability.ProductionRecord
        };
        var provider = new StubBusinessQueryProvider(
            context.SourceKey,
            context.SourceType,
            context.Capability,
            allowedResultFields: new HashSet<string>(["fieldSchema"]),
            blockedResultFieldFragments: new HashSet<string>(["secret"]));
        BusinessQueryProviderResult Result(CloudAiReadProductionFieldSchemaDto schema) =>
            new(
                BusinessQueryOutcome.Success,
                provider.ProviderCode,
                context.SourceKey,
                context.DataSourceId,
                context.SourceType,
                context.Capability,
                [new Dictionary<string, object?> { ["fieldSchema"] = new[] { schema } }],
                1,
                false,
                "cloud-plugin",
                "Cloud",
                DateTimeOffset.UtcNow,
                "safe");

        var allowedSchema = new CloudAiReadProductionFieldSchemaDto(
            "temperature",
            "温度",
            "decimal",
            "℃",
            2,
            Required: false,
            AdditionalFields: new Dictionary<string, object?>());
        var sensitiveSchema = allowedSchema with
        {
            AdditionalFields = new Dictionary<string, object?>
            {
                ["operator_secret"] = "hidden"
            }
        };

        var allowed = () => BusinessQueryProviderResultContract.EnsureMatches(
            context,
            provider,
            Result(allowedSchema));
        var denied = () => BusinessQueryProviderResultContract.EnsureMatches(
            context,
            provider,
            Result(sensitiveSchema));

        allowed.Should().NotThrow();
        denied.Should().Throw<InvalidOperationException>()
            .WithMessage("*blocked or unsupported nested*");
    }

    [Fact]
    public void ProviderRegistry_ShouldRejectMissingOrExtraCapabilityResultContracts()
    {
        var missingContract = new StubBusinessQueryProvider(
            "mes-readonly",
            DataSourceExternalSystemType.NonCloud,
            BusinessDataCapability.ProductionRecord,
            allowedResultFields: new HashSet<string>());
        var extraContract = new StubBusinessQueryProvider(
            "erp-readonly",
            DataSourceExternalSystemType.NonCloud,
            BusinessDataCapability.Capacity,
            allowedResultFields: new HashSet<string>(["quantity"]),
            extraResultContractCapability: BusinessDataCapability.Device);
        var missingProfileBlocker = new StubBusinessQueryProvider(
            "scada-readonly",
            DataSourceExternalSystemType.NonCloud,
            BusinessDataCapability.Device,
            allowedResultFields: new HashSet<string>(["deviceCode"]));
        var strictProfile = new BusinessDataSourceProfile(
            missingProfileBlocker.SourceKey,
            missingProfileBlocker.SourceType,
            DatabaseProviderType.PostgreSql,
            IsRealExternalSource: true,
            RequiresExplicitSelection: true,
            SupportsTextToSqlFallback: false,
            missingProfileBlocker.Capabilities,
            BusinessQuerySecurityProfile.TableOnly(
                new HashSet<string>(["public"], StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(["devices"], StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["devices"] = new HashSet<string>(["device_code"], StringComparer.OrdinalIgnoreCase)
                },
                new HashSet<string>(["api_secret"], StringComparer.OrdinalIgnoreCase)));

        Action buildMissing = () => new BusinessQueryProviderRegistry(
            [missingContract],
            CreateProfileRegistry(missingContract));
        Action buildExtra = () => new BusinessQueryProviderRegistry(
            [extraContract],
            CreateProfileRegistry(extraContract));
        Action buildWithoutProfileBlocker = () => new BusinessQueryProviderRegistry(
            [missingProfileBlocker],
            new TestProfileRegistry([strictProfile]));

        buildMissing.Should().Throw<InvalidOperationException>()
            .WithMessage("*non-empty result contract*");
        buildExtra.Should().Throw<InvalidOperationException>()
            .WithMessage("*non-empty result contract*");
        buildWithoutProfileBlocker.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not cover all source-profile sensitive field blockers*");
    }

    private static BusinessQueryContext CreateContext(
        DataSourceExternalSystemType sourceType,
        bool sourceExplicitlySelected)
    {
        return new BusinessQueryContext(
            Guid.NewGuid(),
            sourceType switch
            {
                DataSourceExternalSystemType.CloudReadOnly =>
                    StandardBusinessDataSourceProfiles.CloudReadOnly.Code,
                DataSourceExternalSystemType.SimulationBusiness =>
                    new SimulationBusinessDataSourceProfileProvider().Profile.Code,
                _ => sourceType.ToString()
            },
            Guid.NewGuid(),
            sourceType,
            BusinessDataCapability.Device,
            "query",
            sourceExplicitlySelected,
            BusinessQueryConfirmation.Complete);
    }

    private static IBusinessDataSourceProfileRegistry CreateProfileRegistry(
        params IBusinessQueryProvider[] providers)
    {
        return new TestProfileRegistry(providers.Select(provider =>
        {
            var blockers = provider.ResultContracts.Values
                .SelectMany(contract => contract.BlockedFieldFragments)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new BusinessDataSourceProfile(
                provider.SourceKey,
                provider.SourceType,
                DatabaseProviderType.PostgreSql,
                IsRealExternalSource:
                    provider.SourceType != DataSourceExternalSystemType.SimulationBusiness,
                RequiresExplicitSelection: true,
                SupportsTextToSqlFallback: false,
                provider.Capabilities,
                BusinessQuerySecurityProfile.TableOnly(
                    new HashSet<string>(["public"], StringComparer.OrdinalIgnoreCase),
                    new HashSet<string>(["records"], StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["records"] = new HashSet<string>(["id"], StringComparer.OrdinalIgnoreCase)
                    },
                    blockers));
        }).ToArray());
    }

    private static BusinessDataSourceProfile CreateNonCloudProfile(string code)
    {
        return new BusinessDataSourceProfile(
            code,
            DataSourceExternalSystemType.NonCloud,
            DatabaseProviderType.PostgreSql,
            IsRealExternalSource: false,
            RequiresExplicitSelection: true,
            SupportsTextToSqlFallback: true,
            new HashSet<BusinessDataCapability> { BusinessDataCapability.Device },
            BusinessQuerySecurityProfile.TableOnly(
                new HashSet<string>(["public"], StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(["device_snapshot"], StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["device_snapshot"] =
                        new HashSet<string>(["device_id"], StringComparer.OrdinalIgnoreCase)
                },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)),
            new BusinessTextToSqlProfile(
                "PostgreSQL",
                "governed-business-readonly-text-to-sql",
                new Dictionary<string, IReadOnlyDictionary<string, string>>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["device_snapshot"] = new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["device_id"] = "uuid"
                    }
                },
                new Dictionary<string, IReadOnlyDictionary<string, string>>(
                    StringComparer.OrdinalIgnoreCase),
                [new BusinessTextToSqlJoinHint(
                    "device_snapshot",
                    "device_id",
                    "device_snapshot",
                    "device_id")]));
    }

    private sealed class StubBusinessDataSourceProfileProvider(
        BusinessDataSourceProfile profile)
        : IBusinessDataSourceProfileProvider
    {
        public BusinessDataSourceProfile Profile => profile;
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class StubBusinessQueryProvider(
        string sourceKey,
        DataSourceExternalSystemType sourceType,
        BusinessDataCapability capability,
        IReadOnlySet<string>? allowedResultFields = null,
        IReadOnlySet<string>? blockedResultFieldFragments = null,
        BusinessDataCapability? extraResultContractCapability = null)
        : IBusinessQueryProvider
    {
        public string ProviderCode => "stub";

        public string SourceKey => sourceKey;

        public DataSourceExternalSystemType SourceType => sourceType;

        public IReadOnlySet<BusinessDataCapability> Capabilities { get; } =
            new HashSet<BusinessDataCapability> { capability };

        public IReadOnlyDictionary<BusinessDataCapability, BusinessQueryResultContract> ResultContracts { get; } =
            BuildContracts(
                capability,
                allowedResultFields,
                blockedResultFieldFragments,
                extraResultContractCapability);

        private static IReadOnlyDictionary<BusinessDataCapability, BusinessQueryResultContract> BuildContracts(
            BusinessDataCapability capability,
            IReadOnlySet<string>? allowedResultFields,
            IReadOnlySet<string>? blockedResultFieldFragments,
            BusinessDataCapability? extraResultContractCapability)
        {
            var contracts = new Dictionary<BusinessDataCapability, BusinessQueryResultContract>
            {
                [capability] = new(
                    allowedResultFields ??
                    new HashSet<string>(["id"], StringComparer.OrdinalIgnoreCase),
                    blockedResultFieldFragments ??
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase))
            };
            if (extraResultContractCapability is { } extra)
            {
                contracts[extra] = new BusinessQueryResultContract(
                    new HashSet<string>(["id"]),
                    new HashSet<string>());
            }

            return contracts;
        }

        public Task<BusinessQueryProviderResult> QueryAsync(
            BusinessQueryContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BusinessQueryProviderResult.FromOutcome(
                context,
                ProviderCode,
                BusinessQueryOutcome.Unsupported,
                "stub"));
        }
    }

    private sealed class TestProfileRegistry(
        IReadOnlyCollection<BusinessDataSourceProfile> profiles)
        : IBusinessDataSourceProfileRegistry
    {
        public IReadOnlyCollection<BusinessDataSourceProfile> GetAll() => profiles;

        public bool TryGet(
            string sourceKey,
            DataSourceExternalSystemType expectedSourceType,
            out BusinessDataSourceProfile profile)
        {
            profile = profiles.SingleOrDefault(candidate =>
                candidate.SourceType == expectedSourceType &&
                string.Equals(
                    candidate.Code,
                    sourceKey,
                    StringComparison.OrdinalIgnoreCase))!;
            return profile is not null;
        }

        public BusinessDataSourceProfile GetRequired(
            string sourceKey,
            DataSourceExternalSystemType expectedSourceType)
        {
            return TryGet(sourceKey, expectedSourceType, out var profile)
                ? profile
                : throw new InvalidOperationException("Profile not registered.");
        }
    }

    private sealed class AllCapabilityCloudProvider : IBusinessQueryProvider
    {
        public string ProviderCode => "all-cloud-capabilities";

        public string SourceKey => StandardBusinessDataSourceProfiles.CloudReadOnly.Code;

        public DataSourceExternalSystemType SourceType =>
            DataSourceExternalSystemType.CloudReadOnly;

        public IReadOnlySet<BusinessDataCapability> Capabilities { get; } =
            Enum.GetValues<BusinessDataCapability>().ToHashSet();

        public IReadOnlyDictionary<BusinessDataCapability, BusinessQueryResultContract>
            ResultContracts { get; } =
            Enum.GetValues<BusinessDataCapability>().ToDictionary(
                capability => capability,
                _ => new BusinessQueryResultContract(
                    new HashSet<string>(["id"], StringComparer.OrdinalIgnoreCase),
                    CloudReadOnlyGovernedSchema.BlockedFieldFragments.ToHashSet(
                        StringComparer.OrdinalIgnoreCase)));

        public Task<BusinessQueryProviderResult> QueryAsync(
            BusinessQueryContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(BusinessQueryProviderResult.FromOutcome(
                context,
                ProviderCode,
                BusinessQueryOutcome.Unsupported,
                "stub"));
    }

    private sealed class UnserializablePayload
    {
        public string Value => throw new InvalidOperationException("serialization denied");
    }
}
