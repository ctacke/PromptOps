using PromptOps.Application.Prompts;
using PromptOps.Application.Providers;
using PromptOps.Application.Refinement;
using PromptOps.Domain.Prompts;
using PromptOps.Domain.Refinement;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>
/// Unit tests (fakes, no SQLite) for Phase 16b's <see cref="PromptBenchmarkService"/> — the gate that
/// promotes a draft to A/B-eligible or deprecates it based on a synthetic benchmark.
/// </summary>
public class PromptBenchmarkServiceTests
{
    [Fact]
    public async Task Marks_The_Candidate_AbEligible_When_It_Beats_The_Active_Version_By_The_Margin()
    {
        var fixture = new Fixture(sampleSize: 3, minQualityDelta: 5);
        var (promptId, activeId, draftId) = await fixture.SeedActivePlusDraftAsync();
        fixture.Benchmark.Result = new BenchmarkComparison(ActiveScore: 70, CandidateScore: 80, SampleSize: 3);

        var result = await fixture.Service.BenchmarkCandidateAsync(promptId, draftId);

        Assert.Equal(BenchmarkOutcome.AbEligible, result.Outcome);
        var candidate = Assert.Single(fixture.Candidates.Candidates);
        Assert.Equal(RefinementCandidateStatus.AbEligible, candidate.Status);
        Assert.Equal(activeId, candidate.ActiveVersionId);
        // The draft is left intact (Draft) so 16c can give it shadow traffic.
        var prompt = await fixture.Prompts.GetByIdAsync(promptId);
        Assert.Equal(PromptVersionStatus.Draft, prompt!.Versions.Single(v => v.Id == draftId).Status);
    }

    [Fact]
    public async Task Rejects_And_Deprecates_The_Draft_When_It_Regresses()
    {
        var fixture = new Fixture(sampleSize: 3, minQualityDelta: 0);
        var (promptId, _, draftId) = await fixture.SeedActivePlusDraftAsync();
        fixture.Benchmark.Result = new BenchmarkComparison(ActiveScore: 80, CandidateScore: 70, SampleSize: 3);

        var result = await fixture.Service.BenchmarkCandidateAsync(promptId, draftId);

        Assert.Equal(BenchmarkOutcome.Rejected, result.Outcome);
        Assert.Equal(RefinementCandidateStatus.Rejected, Assert.Single(fixture.Candidates.Candidates).Status);
        var prompt = await fixture.Prompts.GetByIdAsync(promptId);
        Assert.Equal(PromptVersionStatus.Deprecated, prompt!.Versions.Single(v => v.Id == draftId).Status);
    }

    [Fact]
    public async Task Fails_The_Draft_When_It_Does_Not_Clear_The_Required_Margin()
    {
        var fixture = new Fixture(sampleSize: 3, minQualityDelta: 15);
        var (promptId, _, draftId) = await fixture.SeedActivePlusDraftAsync();
        fixture.Benchmark.Result = new BenchmarkComparison(ActiveScore: 70, CandidateScore: 80, SampleSize: 3); // +10 < 15

        var result = await fixture.Service.BenchmarkCandidateAsync(promptId, draftId);

        Assert.Equal(BenchmarkOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task Leaves_The_Draft_Pending_When_Benchmarking_Is_Disabled()
    {
        var fixture = new Fixture(sampleSize: 0, minQualityDelta: 0);
        var (promptId, _, draftId) = await fixture.SeedActivePlusDraftAsync();

        var result = await fixture.Service.BenchmarkCandidateAsync(promptId, draftId);

        Assert.Equal(BenchmarkOutcome.BenchmarkingDisabled, result.Outcome);
        Assert.Equal(RefinementCandidateStatus.PendingBenchmark, Assert.Single(fixture.Candidates.Candidates).Status);
        Assert.Equal(0, fixture.Benchmark.CallCount);
        var prompt = await fixture.Prompts.GetByIdAsync(promptId);
        Assert.Equal(PromptVersionStatus.Draft, prompt!.Versions.Single(v => v.Id == draftId).Status); // untouched
    }

    [Fact]
    public async Task Leaves_The_Draft_Pending_When_The_Benchmark_Is_Inconclusive()
    {
        var fixture = new Fixture(sampleSize: 3, minQualityDelta: 0);
        var (promptId, _, draftId) = await fixture.SeedActivePlusDraftAsync();
        fixture.Benchmark.Result = null; // e.g. no scenarios could be generated

        var result = await fixture.Service.BenchmarkCandidateAsync(promptId, draftId);

        Assert.Equal(BenchmarkOutcome.Inconclusive, result.Outcome);
        Assert.Equal(RefinementCandidateStatus.PendingBenchmark, Assert.Single(fixture.Candidates.Candidates).Status);
        var prompt = await fixture.Prompts.GetByIdAsync(promptId);
        Assert.Equal(PromptVersionStatus.Draft, prompt!.Versions.Single(v => v.Id == draftId).Status); // not deprecated on no evidence
    }

    private sealed class Fixture
    {
        public FakeMutablePromptRepository Prompts { get; } = new();
        public FakeRefinementCandidateRepository Candidates { get; } = new();
        public FakeBenchmarkProvider Benchmark { get; } = new();
        public PromptService PromptService { get; }
        public PromptBenchmarkService Service { get; }

        public Fixture(int sampleSize, double minQualityDelta)
        {
            var policy = RefinementPolicy.CreateDefault();
            policy.Update(autoRefinementEnabled: true, sampleSize, minQualityDelta, abExplorationRate: 0);
            PromptService = new PromptService(Prompts, new NoopEmbeddingProvider(), new NoopEmbeddingStore());
            Service = new PromptBenchmarkService(new FakeRefinementPolicyRepository(policy), Prompts, Candidates, Benchmark, PromptService);
        }

        public async Task<(Guid PromptId, Guid ActiveVersionId, Guid DraftVersionId)> SeedActivePlusDraftAsync()
        {
            var prompt = await PromptService.CreatePromptAsync("Debug Helper");
            var active = await PromptService.CreateVersionAsync(prompt.Id, "active content", "alice");
            await PromptService.ActivateVersionAsync(prompt.Id, active.Id);
            var draft = await PromptService.CreateVersionAsync(prompt.Id, "draft content", "promptops-refinement");
            return (prompt.Id, active.Id, draft.Id);
        }
    }
}
