using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PromptOps.Application.Embeddings;
using PromptOps.Application.Evaluations;
using PromptOps.Application.Executions;
using PromptOps.Application.Prompts;
using PromptOps.Application.Providers;
using PromptOps.Application.Refinement;
using PromptOps.Domain.Evaluations;
using PromptOps.Domain.Executions;
using PromptOps.Domain.Refinement;
using PromptOps.Infrastructure.Refinement;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>
/// Tests the Phase 16a/16b trigger's gate + detached delegation against a real DI container of fakes
/// — same approach as <see cref="AutoAIEvaluationTriggerTests"/>, since <see cref="PromptRefinementTrigger"/>
/// resolves its services itself through <see cref="IServiceScopeFactory"/>. The drafting and
/// benchmark-gate logic are covered exhaustively by <see cref="PromptRefinementServiceTests"/> and
/// <see cref="PromptBenchmarkServiceTests"/>; here we prove the trigger chains draft → benchmark.
/// </summary>
public class PromptRefinementTriggerTests
{
    [Fact]
    public async Task Does_Nothing_When_Automatic_Refinement_Is_Disabled()
    {
        var (trigger, refiner, candidates, seed) = Build(autoRefinementEnabled: false);
        var (executionId, evaluationId) = await seed(["Reproduce the failure first."]);

        await trigger.HandleAsync(EventFor(evaluationId, executionId));
        await Task.Delay(50); // give any (unwanted) background work a chance to run

        Assert.Equal(0, refiner.CallCount);
        Assert.Empty(candidates.Candidates);
    }

    [Fact]
    public async Task Drafts_A_Refinement_And_Records_A_Candidate_When_Enabled()
    {
        var (trigger, refiner, candidates, seed) = Build(autoRefinementEnabled: true);
        var (executionId, evaluationId) = await seed(["Reproduce the failure first."]);

        await trigger.HandleAsync(EventFor(evaluationId, executionId));
        var invoked = await Task.WhenAny(refiner.Invoked.Task, Task.Delay(TimeSpan.FromSeconds(5))) == refiner.Invoked.Task;

        Assert.True(invoked, "expected the background refinement to invoke the refiner within 5 seconds");
        // The chain continues into the benchmark gate: with benchmarking disabled (default sample
        // size 0) it records a PendingBenchmark candidate for the fresh draft.
        var recorded = await WaitFor(() => candidates.Candidates.Count > 0, TimeSpan.FromSeconds(5));
        Assert.True(recorded, "expected a RefinementCandidate to be recorded by the benchmark gate");
        Assert.Equal(RefinementCandidateStatus.PendingBenchmark, candidates.Candidates[0].Status);
    }

    [Fact]
    public async Task A_Failed_Background_Refinement_Never_Propagates_To_The_Caller()
    {
        var (trigger, refiner, _, seed) = Build(autoRefinementEnabled: true);
        var (executionId, evaluationId) = await seed(["something"]);
        refiner.ShouldThrow = true;

        var exception = await Record.ExceptionAsync(() => trigger.HandleAsync(EventFor(evaluationId, executionId)));

        Assert.Null(exception);
    }

    private static AIEvaluationRecorded EventFor(Guid evaluationId, Guid executionId)
        => new(evaluationId, executionId, "judge", DateTimeOffset.UtcNow);

    private static async Task<bool> WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(25);
        }
        return condition();
    }

    private static (PromptRefinementTrigger Trigger, FakeRefiner Refiner, FakeRefinementCandidateRepository Candidates, Func<IReadOnlyList<string>, Task<(Guid ExecutionId, Guid EvaluationId)>> Seed) Build(bool autoRefinementEnabled)
    {
        var prompts = new FakeMutablePromptRepository();
        var executions = new FakeSeededExecutionRepository();
        var evaluations = new FakeSeededAIEvaluationRepository();
        var refiner = new FakeRefiner();
        var candidates = new FakeRefinementCandidateRepository();

        var policy = RefinementPolicy.CreateDefault();
        policy.Update(autoRefinementEnabled, syntheticSampleSize: 0, minQualityDelta: 0, abExplorationRate: 0);
        var policyRepository = new FakeRefinementPolicyRepository(policy);

        var services = new ServiceCollection();
        services.AddScoped<IPromptRepository>(_ => prompts);
        services.AddScoped<IExecutionRepository>(_ => executions);
        services.AddScoped<IAIEvaluationRepository>(_ => evaluations);
        services.AddScoped<IPromptRefinementProvider>(_ => refiner);
        services.AddScoped<IRefinementCandidateRepository>(_ => candidates);
        services.AddScoped<IRefinementPolicyRepository>(_ => policyRepository);
        services.AddScoped<IPromptBenchmarkProvider>(_ => new FakeBenchmarkProvider());
        services.AddScoped<IEmbeddingProvider, NoopEmbeddingProvider>();
        services.AddScoped<IEmbeddingStore, NoopEmbeddingStore>();
        services.AddScoped<PromptService>();
        services.AddScoped<PromptRefinementService>();
        services.AddScoped<PromptBenchmarkService>();
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var trigger = new PromptRefinementTrigger(scopeFactory, policyRepository, NullLogger<PromptRefinementTrigger>.Instance);

        async Task<(Guid, Guid)> Seed(IReadOnlyList<string> suggestions)
        {
            var promptService = new PromptService(prompts, new NoopEmbeddingProvider(), new NoopEmbeddingStore());
            var prompt = await promptService.CreatePromptAsync("Debug Helper");
            var version = await promptService.CreateVersionAsync(prompt.Id, "content", "alice");
            await promptService.ActivateVersionAsync(prompt.Id, version.Id);

            var execution = ExecutionRecord.Start(version.Id, "alice", new DevelopmentContext { Repository = "repo-a" });
            executions.Seed(execution);
            var evaluation = AIEvaluation.Record(execution.Id, "judge", null, true, [], [], null, suggestions, "{}");
            evaluations.Seed(evaluation);
            return (execution.Id, evaluation.Id);
        }

        return (trigger, refiner, candidates, Seed);
    }
}
