using AICopilot.SharedKernel.Specification;
using AICopilot.Core.DataAnalysis.Ids;

namespace AICopilot.Core.DataAnalysis.Specifications.BusinessDatabase;

public sealed class BusinessDatabaseByIdSpec : Specification<Aggregates.BusinessDatabase.BusinessDatabase>
{
    public BusinessDatabaseByIdSpec(BusinessDatabaseId id)
    {
        FilterCondition = database => database.Id == id;
    }
}

public sealed class BusinessDatabaseByNameSpec : Specification<Aggregates.BusinessDatabase.BusinessDatabase>
{
    public BusinessDatabaseByNameSpec(string name)
    {
        FilterCondition = database => database.Name == name;
    }
}

public sealed class BusinessDatabasesOrderedSpec : Specification<Aggregates.BusinessDatabase.BusinessDatabase>
{
    public BusinessDatabasesOrderedSpec()
    {
        SetOrderBy(database => database.Name);
    }
}

public sealed class EnabledBusinessDatabasesSpec : Specification<Aggregates.BusinessDatabase.BusinessDatabase>
{
    public EnabledBusinessDatabasesSpec()
    {
        FilterCondition = database => database.IsEnabled;
        SetOrderBy(database => database.Name);
    }
}
