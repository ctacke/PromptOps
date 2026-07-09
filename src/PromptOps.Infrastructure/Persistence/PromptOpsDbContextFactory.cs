using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PromptOps.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> tooling create the context for migrations without running the Host.</summary>
public sealed class PromptOpsDbContextFactory : IDesignTimeDbContextFactory<PromptOpsDbContext>
{
    public PromptOpsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PromptOpsDbContext>();
        optionsBuilder.UseSqlite("Data Source=promptops.db");
        return new PromptOpsDbContext(optionsBuilder.Options);
    }
}
