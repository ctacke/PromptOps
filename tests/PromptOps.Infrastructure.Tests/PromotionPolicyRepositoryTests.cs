using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PromptOps.Domain.Promotion;
using PromptOps.Infrastructure.Persistence;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>
/// Deliberately does *not* use the shared-per-class <see cref="SqliteFixture"/> pattern every other
/// integration test file in this project uses. <c>PromotionPolicy</c> is a true global singleton
/// with no partition key (unlike Embeddings' <c>subjectType</c>, Phase 10's fix for the same
/// class-shared-database problem — see <c>EmbeddingStoreTests</c>), so a row inserted by one
/// <c>[Fact]</c> would otherwise leak into every other test in the class, including
/// "no policy has ever been created". Each test gets its own throwaway SQLite file instead.
/// </summary>
public class PromotionPolicyRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"promptops-promotion-policy-tests-{Guid.NewGuid():N}.db");

    private PromptOpsDbContext CreateContext()
    {
        var context = new PromptOpsDbContext(new DbContextOptionsBuilder<PromptOpsDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        context.Database.Migrate();
        return context;
    }

    [Fact]
    public async Task GetAsync_Returns_Null_When_No_Policy_Has_Ever_Been_Created()
    {
        using var context = CreateContext();

        var policy = await new PromotionPolicyRepository(context).GetAsync();

        Assert.Null(policy);
    }

    [Fact]
    public async Task AddAsync_Then_GetAsync_Round_Trips_Through_SQLite()
    {
        using (var context = CreateContext())
        {
            var repository = new PromotionPolicyRepository(context);
            var policy = PromotionPolicy.CreateDefault();
            policy.Update(requireHumanEvaluation: false, autoPromotionEnabled: true, minimumScoreThreshold: 85.0, minimumMarginOverActive: 5.0);

            await repository.AddAsync(policy);
            await repository.SaveChangesAsync();
        }

        using var freshContext = CreateContext();
        var reloaded = await new PromotionPolicyRepository(freshContext).GetAsync();

        Assert.NotNull(reloaded);
        Assert.False(reloaded!.RequireHumanEvaluation);
        Assert.True(reloaded.AutoPromotionEnabled);
        Assert.Equal(85.0, reloaded.MinimumScoreThreshold);
        Assert.Equal(5.0, reloaded.MinimumMarginOverActive);
    }

    [Fact]
    public async Task UpdateAsync_Persists_Changes_To_The_Same_Row()
    {
        Guid policyId;
        using (var context = CreateContext())
        {
            var repository = new PromotionPolicyRepository(context);
            var policy = PromotionPolicy.CreateDefault();
            policyId = policy.Id;
            await repository.AddAsync(policy);
            await repository.SaveChangesAsync();

            policy.Update(requireHumanEvaluation: false, autoPromotionEnabled: true, minimumScoreThreshold: 90.0, minimumMarginOverActive: null);
            await repository.UpdateAsync(policy);
            await repository.SaveChangesAsync();
        }

        using var freshContext = CreateContext();
        var reloaded = await new PromotionPolicyRepository(freshContext).GetAsync();

        Assert.Equal(policyId, reloaded!.Id);
        Assert.True(reloaded.AutoPromotionEnabled);
        Assert.Equal(90.0, reloaded.MinimumScoreThreshold);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }
}
