using Microsoft.Extensions.Logging.Abstractions;
using PromptOps.Application.Promotion;
using PromptOps.Domain.Promotion;
using PromptOps.Domain.Prompts;
using PromptOps.Domain.Scoring;
using PromptOps.Infrastructure.Promotion;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>Pure unit tests (fakes, no SQLite) for the Phase 11 auto-promotion decision logic — same lightweight style as <see cref="SemanticRecommendationProviderTests"/>.</summary>
public class AutoPromotionTriggerTests
{
    private static AutoPromotionTrigger CreateTrigger(
        FakeMutablePromptRepository prompts, FakePromptScoreRepository scores, FakePromotionPolicyRepository policy)
        => new(policy, prompts, scores, NullLogger<AutoPromotionTrigger>.Instance);

    private static ScoreComputed ScoreComputedFor(Guid promptVersionId, double overallScore) => new(
        Guid.NewGuid(), promptVersionId, Guid.NewGuid(), overallScore, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Does_Nothing_When_Auto_Promotion_Is_Disabled()
    {
        var prompts = new FakeMutablePromptRepository();
        var (prompt, draft) = await SeedDraftAsync(prompts, "content");
        var policy = new FakePromotionPolicyRepository(PromotionPolicy.CreateDefault()); // AutoPromotionEnabled: false

        await CreateTrigger(prompts, new FakePromptScoreRepository(), policy)
            .HandleAsync(ScoreComputedFor(draft.Id, 95.0));

        var reloaded = await prompts.GetByIdAsync(prompt.Id);
        Assert.Equal(PromptVersionStatus.Draft, reloaded!.Versions.Single().Status);
    }

    [Fact]
    public async Task Promotes_When_The_Score_Clears_The_Absolute_Threshold_With_No_Active_Version_Yet()
    {
        var prompts = new FakeMutablePromptRepository();
        var (prompt, draft) = await SeedDraftAsync(prompts, "content");
        var policy = EnabledPolicy(minimumScoreThreshold: 80.0, minimumMarginOverActive: null);

        await CreateTrigger(prompts, new FakePromptScoreRepository(), policy)
            .HandleAsync(ScoreComputedFor(draft.Id, 85.0));

        var reloaded = await prompts.GetByIdAsync(prompt.Id);
        Assert.Equal(PromptVersionStatus.Active, reloaded!.Versions.Single().Status);
    }

    [Fact]
    public async Task Does_Not_Promote_When_The_Score_Clears_Neither_Threshold_Nor_Margin()
    {
        var prompts = new FakeMutablePromptRepository();
        var (prompt, draft) = await SeedDraftAsync(prompts, "content");
        var policy = EnabledPolicy(minimumScoreThreshold: 80.0, minimumMarginOverActive: null);

        await CreateTrigger(prompts, new FakePromptScoreRepository(), policy)
            .HandleAsync(ScoreComputedFor(draft.Id, 60.0));

        var reloaded = await prompts.GetByIdAsync(prompt.Id);
        Assert.Equal(PromptVersionStatus.Draft, reloaded!.Versions.Single().Status);
    }

    [Fact]
    public async Task Promotes_When_The_Score_Beats_The_Active_Versions_Score_By_The_Margin()
    {
        var prompts = new FakeMutablePromptRepository();
        var prompt = Prompt.Create("Fix a bug");
        var activeVersion = prompt.CreateVersion("active content", "alice");
        var candidateVersion = prompt.CreateVersion("candidate content", "alice");
        await prompts.AddAsync(prompt);
        prompt.ActivateVersion(activeVersion.Id);
        await prompts.UpdateAsync(prompt);

        var scores = new FakePromptScoreRepository();
        scores.Seed(activeVersion.Id, PromptScore.Compute(activeVersion.Id, Guid.NewGuid(), 70.0, new Dictionary<string, double>(), 1));

        var policy = EnabledPolicy(minimumScoreThreshold: null, minimumMarginOverActive: 10.0);

        await CreateTrigger(prompts, scores, policy)
            .HandleAsync(ScoreComputedFor(candidateVersion.Id, 81.0)); // beats 70.0 by 11 >= margin 10

        var reloaded = await prompts.GetByIdAsync(prompt.Id);
        Assert.Equal(PromptVersionStatus.Deprecated, reloaded!.Versions.Single(v => v.Id == activeVersion.Id).Status);
        Assert.Equal(PromptVersionStatus.Active, reloaded.Versions.Single(v => v.Id == candidateVersion.Id).Status);
    }

    [Fact]
    public async Task Does_Not_Promote_When_The_Margin_Is_Not_Cleared()
    {
        var prompts = new FakeMutablePromptRepository();
        var prompt = Prompt.Create("Fix a bug");
        var activeVersion = prompt.CreateVersion("active content", "alice");
        var candidateVersion = prompt.CreateVersion("candidate content", "alice");
        await prompts.AddAsync(prompt);
        prompt.ActivateVersion(activeVersion.Id);
        await prompts.UpdateAsync(prompt);

        var scores = new FakePromptScoreRepository();
        scores.Seed(activeVersion.Id, PromptScore.Compute(activeVersion.Id, Guid.NewGuid(), 70.0, new Dictionary<string, double>(), 1));

        var policy = EnabledPolicy(minimumScoreThreshold: null, minimumMarginOverActive: 10.0);

        await CreateTrigger(prompts, scores, policy)
            .HandleAsync(ScoreComputedFor(candidateVersion.Id, 75.0)); // only beats by 5 < margin 10

        var reloaded = await prompts.GetByIdAsync(prompt.Id);
        Assert.Equal(PromptVersionStatus.Active, reloaded!.Versions.Single(v => v.Id == activeVersion.Id).Status);
    }

    [Fact]
    public async Task Is_A_No_Op_When_The_Scored_Version_Is_Already_Active()
    {
        var prompts = new FakeMutablePromptRepository();
        var (prompt, draft) = await SeedDraftAsync(prompts, "content");
        prompt.ActivateVersion(draft.Id);
        await prompts.UpdateAsync(prompt);
        var policy = EnabledPolicy(minimumScoreThreshold: 0.0, minimumMarginOverActive: null);

        // Should not throw re-deprecating/re-activating itself, and should remain Active.
        await CreateTrigger(prompts, new FakePromptScoreRepository(), policy)
            .HandleAsync(ScoreComputedFor(draft.Id, 99.0));

        var reloaded = await prompts.GetByIdAsync(prompt.Id);
        Assert.Equal(PromptVersionStatus.Active, reloaded!.Versions.Single().Status);
    }

    [Fact]
    public async Task Never_Resurrects_A_Deprecated_Version()
    {
        var prompts = new FakeMutablePromptRepository();
        var prompt = Prompt.Create("Fix a bug");
        var v1 = prompt.CreateVersion("first", "alice");
        var v2 = prompt.CreateVersion("second", "alice");
        await prompts.AddAsync(prompt);
        prompt.ActivateVersion(v1.Id);
        prompt.ActivateVersion(v2.Id); // deprecates v1
        await prompts.UpdateAsync(prompt);

        var policy = EnabledPolicy(minimumScoreThreshold: 0.0, minimumMarginOverActive: null);

        // A (hypothetical) rescore of the deprecated v1 must never reactivate it.
        await CreateTrigger(prompts, new FakePromptScoreRepository(), policy)
            .HandleAsync(ScoreComputedFor(v1.Id, 99.0));

        var reloaded = await prompts.GetByIdAsync(prompt.Id);
        Assert.Equal(PromptVersionStatus.Deprecated, reloaded!.Versions.Single(v => v.Id == v1.Id).Status);
        Assert.Equal(PromptVersionStatus.Active, reloaded.Versions.Single(v => v.Id == v2.Id).Status);
    }

    private static async Task<(Prompt Prompt, PromptVersion Draft)> SeedDraftAsync(FakeMutablePromptRepository prompts, string content)
    {
        var prompt = Prompt.Create("Fix a bug");
        var version = prompt.CreateVersion(content, "alice");
        await prompts.AddAsync(prompt);
        return (prompt, version);
    }

    private static FakePromotionPolicyRepository EnabledPolicy(double? minimumScoreThreshold, double? minimumMarginOverActive)
    {
        var policy = PromotionPolicy.CreateDefault();
        policy.Update(requireHumanEvaluation: false, autoPromotionEnabled: true, minimumScoreThreshold, minimumMarginOverActive);
        return new FakePromotionPolicyRepository(policy);
    }

    private sealed class FakePromotionPolicyRepository(PromotionPolicy? policy) : IPromotionPolicyRepository
    {
        public Task<PromotionPolicy?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(policy);
        public Task AddAsync(PromotionPolicy policy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(PromotionPolicy policy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
