using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AICopilot.EntityFrameworkCore;

public sealed class AiCopilotDbContextFactory : IDesignTimeDbContextFactory<AiCopilotDbContext>
{
    public AiCopilotDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ai-copilot")
                               ?? Environment.GetEnvironmentVariable("AICOPILOT__DESIGNTIME__CONNECTION")
                               ?? throw new InvalidOperationException(
                                   "Design-time migration requires ConnectionStrings__ai-copilot or AICOPILOT__DESIGNTIME__CONNECTION.");

        var optionsBuilder = new DbContextOptionsBuilder<AiCopilotDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new AiCopilotDbContext(optionsBuilder.Options);
    }
}
