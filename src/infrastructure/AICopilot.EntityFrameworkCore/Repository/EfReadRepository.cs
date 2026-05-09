using AICopilot.SharedKernel.Domain;

namespace AICopilot.EntityFrameworkCore.Repository;

public class EfReadRepository<T>(AiCopilotDbContext dbContext)
    : EfReadRepositoryBase<AiCopilotDbContext, T>(dbContext)
    where T : class, IAggregateRoot;
