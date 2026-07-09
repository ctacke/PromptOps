using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PromptOps.Infrastructure.Persistence;

namespace PromptOps.Infrastructure.Tests;

/// <summary>
/// A real, migrated SQLite file per test class (not `:memory:`) — this is Phase 2's whole point:
/// prove the aggregate round-trips through actual on-disk SQLite, the way the daemon will use it.
/// </summary>
public sealed class SqliteFixture : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"promptops-tests-{Guid.NewGuid():N}.db");

    public SqliteFixture()
    {
        using var context = CreateContext();
        context.Database.Migrate();
    }

    public PromptOpsDbContext CreateContext(Action<DbContextOptionsBuilder<PromptOpsDbContext>>? configure = null)
    {
        var builder = new DbContextOptionsBuilder<PromptOpsDbContext>()
            .UseSqlite($"Data Source={_dbPath}");
        configure?.Invoke(builder);
        return new PromptOpsDbContext(builder.Options);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
