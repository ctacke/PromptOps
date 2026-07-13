using PromptOps.Application.Refinement;
using PromptOps.Domain.Refinement;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>Unit tests for Phase 16c's <see cref="AbVersionSelector"/> — the ε-greedy routing between the active version and an A/B-eligible draft.</summary>
public class AbVersionSelectorTests
{
    private static readonly Guid ActiveId = Guid.NewGuid();
    private static readonly Guid DraftId = Guid.NewGuid();

    [Fact]
    public async Task Returns_Active_When_Exploration_Is_Off()
    {
        var sampler = new FakeExplorationSampler(shouldExplore: true);
        var selector = Build(rate: 0, sampler, eligibleDraft: true);

        var result = await selector.SelectVersionAsync(ActiveId);

        Assert.Equal(ActiveId, result);
        Assert.True(double.IsNaN(sampler.LastRate)); // short-circuited before the coin flip
    }

    [Fact]
    public async Task Returns_Active_When_The_Coin_Flip_Declines()
    {
        var selector = Build(rate: 0.5, new FakeExplorationSampler(shouldExplore: false), eligibleDraft: true);

        Assert.Equal(ActiveId, await selector.SelectVersionAsync(ActiveId));
    }

    [Fact]
    public async Task Returns_The_Draft_When_Exploring_And_An_Eligible_Draft_Exists()
    {
        var selector = Build(rate: 0.5, new FakeExplorationSampler(shouldExplore: true), eligibleDraft: true);

        Assert.Equal(DraftId, await selector.SelectVersionAsync(ActiveId));
    }

    [Fact]
    public async Task Returns_Active_When_Exploring_But_No_Eligible_Draft_Exists()
    {
        var selector = Build(rate: 0.5, new FakeExplorationSampler(shouldExplore: true), eligibleDraft: false);

        Assert.Equal(ActiveId, await selector.SelectVersionAsync(ActiveId));
    }

    private static AbVersionSelector Build(double rate, FakeExplorationSampler sampler, bool eligibleDraft)
    {
        var policy = RefinementPolicy.CreateDefault();
        policy.Update(autoRefinementEnabled: false, syntheticSampleSize: 0, minQualityDelta: 0, abExplorationRate: rate);

        var candidates = new FakeRefinementCandidateRepository();
        if (eligibleDraft)
        {
            var candidate = RefinementCandidate.Create(Guid.NewGuid(), DraftId, ActiveId);
            candidate.MarkEligible(70, 85);
            candidates.Candidates.Add(candidate);
        }

        return new AbVersionSelector(new FakeRefinementPolicyRepository(policy), candidates, sampler);
    }
}
