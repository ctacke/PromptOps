using PromptOps.Application.Evaluations;
using PromptOps.Application.Executions;
using PromptOps.Application.Metrics;
using PromptOps.Domain.Evaluations;
using PromptOps.Domain.Executions;
using PromptOps.Domain.Metrics;
using PromptOps.Domain.Scoring;
using PromptOps.Infrastructure.Providers;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>Pure unit tests — no SQLite involved, so in-memory fakes are enough (same shape as AIJudgeEvaluationProviderTests).</summary>
public class WeightedSumScoringProviderTests
{
    private static ExecutionRecord FinishedExecution(Guid promptVersionId)
    {
        var execution = ExecutionRecord.Start(promptVersionId, "alice", new DevelopmentContext { Repository = "repo" });
        execution.Finish("output", TimeSpan.FromSeconds(1), "manual", null, null, [], 0, 0);
        return execution;
    }

    [Fact]
    public async Task Zero_Weight_Component_Is_Excluded_From_The_Weighted_Average_Even_With_Data()
    {
        var promptVersionId = Guid.NewGuid();
        var executions = new FakeExecutionRepository();
        var execution = FinishedExecution(promptVersionId);
        executions.Seed(execution);

        var humanEvaluations = new FakeHumanEvaluationRepository();
        humanEvaluations.Seed(execution.Id, HumanEvaluation.Submit(
            execution.Id, "alice", 5, 5, 5, 5, 5, false, 5, overallSatisfaction: 5)); // normalizes to 100

        var metrics = new FakeEngineeringMetricsRepository();
        metrics.Seed(execution.Id, EngineeringMetrics.Record(execution.Id, "sonar", coverage: 0.0)); // would drag score to 0 if counted

        var provider = new WeightedSumScoringProvider(executions, metrics, humanEvaluations, new FakeAIEvaluationRepository());
        // Sonar weight is 0 — even though sonar data exists (coverage 0.0), it must not affect overallScore.
        var config = ScoringConfig.Create("test", 1, new ScoringWeights { HumanRating = 1.0, Sonar = 0 });

        var score = await provider.ComputeAsync(promptVersionId, config);

        Assert.Equal(100.0, score.OverallScore);
        // Still reported in the breakdown for visibility, just excluded from the weighted average.
        Assert.Equal(0.0, score.ComponentScores["sonar"]);
    }

    [Fact]
    public async Task Missing_Input_For_A_Positively_Weighted_Component_Does_Not_Drag_The_Score_Down()
    {
        var promptVersionId = Guid.NewGuid();
        var executions = new FakeExecutionRepository();
        var execution = FinishedExecution(promptVersionId);
        executions.Seed(execution);

        var humanEvaluations = new FakeHumanEvaluationRepository();
        humanEvaluations.Seed(execution.Id, HumanEvaluation.Submit(
            execution.Id, "alice", 5, 5, 5, 5, 5, false, 5, overallSatisfaction: 5)); // 100

        // No Sonar data at all, but sonar has a positive weight below.
        var provider = new WeightedSumScoringProvider(
            executions, new FakeEngineeringMetricsRepository(), humanEvaluations, new FakeAIEvaluationRepository());
        var config = ScoringConfig.Create("test", 1, new ScoringWeights { HumanRating = 0.5, Sonar = 0.5 });

        var score = await provider.ComputeAsync(promptVersionId, config);

        // If sonar's missing data were treated as 0, overallScore would be 50, not 100.
        Assert.Equal(100.0, score.OverallScore);
        Assert.DoesNotContain("sonar", score.ComponentScores.Keys);
    }

    [Fact]
    public async Task No_Data_For_Any_Weighted_Component_Yields_A_Zero_Score_Not_An_Exception()
    {
        var promptVersionId = Guid.NewGuid();
        var executions = new FakeExecutionRepository();
        executions.Seed(FinishedExecution(promptVersionId));

        var provider = new WeightedSumScoringProvider(
            executions, new FakeEngineeringMetricsRepository(), new FakeHumanEvaluationRepository(), new FakeAIEvaluationRepository());
        var config = ScoringConfig.Create("test", 1, new ScoringWeights { HumanRating = 1.0 });

        var score = await provider.ComputeAsync(promptVersionId, config);

        Assert.Equal(0.0, score.OverallScore);
        Assert.Empty(score.ComponentScores);
    }

    [Fact]
    public async Task PromptVersion_With_No_Executions_At_All_Yields_A_Zero_Score_With_Zero_SampleSize()
    {
        var provider = new WeightedSumScoringProvider(
            new FakeExecutionRepository(), new FakeEngineeringMetricsRepository(),
            new FakeHumanEvaluationRepository(), new FakeAIEvaluationRepository());
        var config = ScoringConfig.Create("test", 1, new ScoringWeights { HumanRating = 1.0 });

        var score = await provider.ComputeAsync(Guid.NewGuid(), config);

        Assert.Equal(0.0, score.OverallScore);
        Assert.Equal(0, score.SampleSize);
    }

    [Fact]
    public async Task InProgress_Executions_Are_Excluded_From_SampleSize_And_Data_Gathering()
    {
        var promptVersionId = Guid.NewGuid();
        var executions = new FakeExecutionRepository();
        executions.Seed(ExecutionRecord.Start(promptVersionId, "alice", new DevelopmentContext { Repository = "repo" })); // still InProgress
        executions.Seed(FinishedExecution(promptVersionId));

        var provider = new WeightedSumScoringProvider(
            executions, new FakeEngineeringMetricsRepository(), new FakeHumanEvaluationRepository(), new FakeAIEvaluationRepository());
        var config = ScoringConfig.Create("test", 1, new ScoringWeights { HumanRating = 1.0 });

        var score = await provider.ComputeAsync(promptVersionId, config);

        Assert.Equal(1, score.SampleSize);
    }

    [Fact]
    public async Task Changing_Weights_Deterministically_Changes_The_Computed_Score()
    {
        var promptVersionId = Guid.NewGuid();
        var executions = new FakeExecutionRepository();
        var execution = FinishedExecution(promptVersionId);
        executions.Seed(execution);

        var humanEvaluations = new FakeHumanEvaluationRepository();
        humanEvaluations.Seed(execution.Id, HumanEvaluation.Submit(execution.Id, "alice", 5, 5, 5, 5, 5, false, 5, overallSatisfaction: 5)); // 100

        var aiEvaluations = new FakeAIEvaluationRepository();
        aiEvaluations.Seed(execution.Id, AIEvaluation.Record(
            execution.Id, "ai-judge", null, satisfiesAcceptanceCriteria: false, [], [], null, [], "{}")); // 0

        var provider = new WeightedSumScoringProvider(
            executions, new FakeEngineeringMetricsRepository(), humanEvaluations, aiEvaluations);

        var humanOnly = ScoringConfig.Create("test", 1, new ScoringWeights { HumanRating = 1.0 });
        var acOnly = ScoringConfig.Create("test", 2, new ScoringWeights { AcceptanceCriteria = 1.0 });
        var evenSplit = ScoringConfig.Create("test", 3, new ScoringWeights { HumanRating = 0.5, AcceptanceCriteria = 0.5 });

        var humanOnlyScore = await provider.ComputeAsync(promptVersionId, humanOnly);
        var acOnlyScore = await provider.ComputeAsync(promptVersionId, acOnly);
        var evenSplitScore = await provider.ComputeAsync(promptVersionId, evenSplit);

        Assert.Equal(100.0, humanOnlyScore.OverallScore);
        Assert.Equal(0.0, acOnlyScore.OverallScore);
        Assert.Equal(50.0, evenSplitScore.OverallScore);
        // Reproducibility: each score records exactly which config version produced it.
        Assert.Equal(humanOnly.Id, humanOnlyScore.ScoringConfigId);
        Assert.Equal(acOnly.Id, acOnlyScore.ScoringConfigId);
        Assert.Equal(evenSplit.Id, evenSplitScore.ScoringConfigId);
    }

    [Fact]
    public async Task Manual_Fixes_And_Review_Comments_Decay_As_Counts_Increase()
    {
        var promptVersionId = Guid.NewGuid();
        var executions = new FakeExecutionRepository();
        var execution = FinishedExecution(promptVersionId);
        executions.Seed(execution);

        var metrics = new FakeEngineeringMetricsRepository();
        metrics.Seed(execution.Id, EngineeringMetrics.Record(execution.Id, "build-result", manualEdits: 3, reviewComments: 2));

        var provider = new WeightedSumScoringProvider(
            executions, metrics, new FakeHumanEvaluationRepository(), new FakeAIEvaluationRepository());
        var config = ScoringConfig.Create("test", 1, new ScoringWeights { ManualFixes = 1.0, ReviewComments = 1.0 });

        var score = await provider.ComputeAsync(promptVersionId, config);

        Assert.Equal(70.0, score.ComponentScores["manualFixes"]); // 100 - 3*10
        Assert.Equal(90.0, score.ComponentScores["reviewComments"]); // 100 - 2*5
    }

    private sealed class FakeExecutionRepository : IExecutionRepository
    {
        private readonly Dictionary<Guid, ExecutionRecord> _executions = [];

        public void Seed(ExecutionRecord execution) => _executions[execution.Id] = execution;

        public Task AddAsync(ExecutionRecord execution, CancellationToken cancellationToken = default)
        {
            _executions[execution.Id] = execution;
            return Task.CompletedTask;
        }

        public Task<ExecutionRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_executions.GetValueOrDefault(id));

        public Task<IReadOnlyList<ExecutionRecord>> GetByPromptVersionIdAsync(Guid promptVersionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ExecutionRecord>>(_executions.Values.Where(e => e.PromptVersionId == promptVersionId).ToList());

        public Task UpdateAsync(ExecutionRecord execution, CancellationToken cancellationToken = default)
        {
            _executions[execution.Id] = execution;
            return Task.CompletedTask;
        }

        public Task<ExecutionStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeEngineeringMetricsRepository : IEngineeringMetricsRepository
    {
        private readonly Dictionary<Guid, List<EngineeringMetrics>> _byExecution = [];

        public void Seed(Guid executionId, EngineeringMetrics metrics)
            => _byExecution.GetOrAdd(executionId, () => []).Add(metrics);

        public Task AddAsync(EngineeringMetrics metrics, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<EngineeringMetrics>> GetByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EngineeringMetrics>>(_byExecution.GetValueOrDefault(executionId) ?? []);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeHumanEvaluationRepository : IHumanEvaluationRepository
    {
        private readonly Dictionary<Guid, List<HumanEvaluation>> _byExecution = [];

        public void Seed(Guid executionId, HumanEvaluation evaluation)
            => _byExecution.GetOrAdd(executionId, () => []).Add(evaluation);

        public Task AddAsync(HumanEvaluation evaluation, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<HumanEvaluation>> GetByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<HumanEvaluation>>(_byExecution.GetValueOrDefault(executionId) ?? []);

        public Task<int> GetCountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeAIEvaluationRepository : IAIEvaluationRepository
    {
        private readonly Dictionary<Guid, List<AIEvaluation>> _byExecution = [];

        public void Seed(Guid executionId, AIEvaluation evaluation)
            => _byExecution.GetOrAdd(executionId, () => []).Add(evaluation);

        public Task AddAsync(AIEvaluation evaluation, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<AIEvaluation>> GetByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AIEvaluation>>(_byExecution.GetValueOrDefault(executionId) ?? []);

        public Task<int> GetCountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

file static class DictionaryExtensions
{
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, Func<TValue> factory) where TKey : notnull
    {
        if (!dictionary.TryGetValue(key, out var value))
        {
            value = factory();
            dictionary[key] = value;
        }
        return value;
    }
}
