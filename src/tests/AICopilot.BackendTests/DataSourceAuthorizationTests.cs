using System.Data;
using System.Linq.Expressions;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Ids;
using AICopilot.Core.DataAnalysis.Specifications.BusinessDatabase;
using AICopilot.DataAnalysisService.BusinessDatabases;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.BackendTests;

[Trait("Suite", "DataSourceAuthorization")]
public sealed class DataSourceAuthorizationTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public async Task DataSourceQuery_ShouldRejectUserWithoutSpecificDataSourceGrant()
    {
        var database = CreateDatabase("sim-a");
        var databaseRepository = new TestRepository<BusinessDatabase>(database);
        var grantRepository = new TestRepository<DataSourcePermissionGrant>();
        var accessService = new BusinessDatabaseAccessService(
            grantRepository,
            new TestCurrentUser(UserId, role: "Analyst"));
        var connector = new RecordingDatabaseConnector();
        var audit = new CapturingAuditLogWriter();
        var executor = new BusinessReadonlyQueryExecutor(
            databaseRepository,
            connector,
            accessService,
            audit);

        var result = await executor.ExecuteAsync(
            database.Id,
            "SELECT employee_id FROM employees",
            limit: 10,
            requireSimulationBusiness: true,
            SimulationBusinessQuerySchema.SafetySchema,
            auditAction: "DataSource.Query",
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Forbidden);
        connector.ExecuteCount.Should().Be(0);
        audit.Requests.Should().ContainSingle(request =>
            request.Result == AuditResults.Rejected &&
            request.Summary.Contains("not authorized", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DataSourceCandidates_ShouldOnlyReturnAuthorizedSources()
    {
        var authorized = CreateDatabase("sim-authorized");
        var denied = CreateDatabase("sim-denied");
        var databaseRepository = new TestRepository<BusinessDatabase>(authorized, denied);
        var grantRepository = new TestRepository<DataSourcePermissionGrant>(
            new DataSourcePermissionGrant(
                authorized.Id,
                DataSourcePermissionGrantTargetType.Role,
                "Analyst",
                canQuery: true,
                canSchemaView: false));
        var readService = new BusinessDatabaseReadService(
            databaseRepository,
            new BusinessDatabaseAccessService(
                grantRepository,
                new TestCurrentUser(UserId, role: "Analyst")));

        var candidates = await readService.ListEnabledAsync(CancellationToken.None);

        candidates.Should().ContainSingle();
        candidates.Single().Name.Should().Be("sim-authorized");
    }

    private static BusinessDatabase CreateDatabase(string name)
    {
        return new BusinessDatabase(
            name,
            "AI independent simulation business database",
            "Host=localhost;Database=aicopilot_sim_business;Username=readonly;Password=readonly;",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            BusinessDataExternalSystemType.SimulationBusiness,
            isEnabled: true,
            category: "Simulation",
            tags: ["production"],
            ownerDepartment: "AI Platform",
            businessDomain: "Production");
    }

    private sealed class RecordingDatabaseConnector : IDatabaseConnector
    {
        public int ExecuteCount { get; private set; }

        public IDbConnection GetConnection(BusinessDatabaseConnectionInfo database)
        {
            throw new NotSupportedException();
        }

        public Task<IEnumerable<dynamic>> ExecuteQueryAsync(
            BusinessDatabaseConnectionInfo database,
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            return Task.FromResult<IEnumerable<dynamic>>([]);
        }

        public Task<DatabaseQueryResult> ExecuteQueryWithMetadataAsync(
            BusinessDatabaseConnectionInfo database,
            string sql,
            object? parameters = null,
            DatabaseQueryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            return Task.FromResult(new DatabaseQueryResult(
                [new Dictionary<string, object?> { ["employee_id"] = "E0001" }],
                ReturnedRowCount: 1,
                IsTruncated: false,
                ElapsedMilliseconds: 1));
        }

        public Task<IEnumerable<dynamic>> GetSchemaInfoAsync(
            BusinessDatabaseConnectionInfo database,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<dynamic>>([]);
        }
    }

    private sealed class CapturingAuditLogWriter : IAuditLogWriter
    {
        public List<AuditLogWriteRequest> Requests { get; } = [];

        public Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Requests.Count);
        }
    }

    private sealed class TestRepository<T> : IRepository<T>
        where T : class, AICopilot.SharedKernel.Domain.IEntity, AICopilot.SharedKernel.Domain.IAggregateRoot
    {
        private readonly List<T> entities;

        public TestRepository(params T[] entities)
        {
            this.entities = [..entities];
        }

        public T Add(T entity)
        {
            entities.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
        }

        public void Delete(T entity)
        {
            entities.Remove(entity);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        public Task<List<T>> ListAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).ToList());
        }

        public Task<T?> FirstOrDefaultAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).FirstOrDefault());
        }

        public Task<int> CountAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).Count());
        }

        public Task<bool> AnyAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).Any());
        }

        public Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            return Task.FromResult(entities.FirstOrDefault(entity => Equals(ReadId(entity), id)));
        }

        public Task<List<T>> GetListAsync(Expression<Func<T, bool>> expression, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(entities.AsQueryable().Where(expression).ToList());
        }

        public Task<int> GetCountAsync(Expression<Func<T, bool>> expression, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(entities.AsQueryable().Count(expression));
        }

        public Task<T?> GetAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(entities.AsQueryable().FirstOrDefault(expression));
        }

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(entities.AsQueryable().Where(expression).ToList());
        }

        private IQueryable<T> Apply(ISpecification<T>? specification)
        {
            var query = entities.AsQueryable();
            if (specification?.FilterCondition is not null)
            {
                query = query.Where(specification.FilterCondition);
            }

            if (specification?.OrderBy is not null)
            {
                query = query.OrderBy(specification.OrderBy);
            }

            if (specification?.OrderByDescending is not null)
            {
                query = query.OrderByDescending(specification.OrderByDescending);
            }

            return query;
        }

        private static object? ReadId(T entity)
        {
            return typeof(T).GetProperty("Id")?.GetValue(entity);
        }
    }
}
