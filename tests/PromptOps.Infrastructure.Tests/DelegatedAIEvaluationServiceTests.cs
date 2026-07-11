using PromptOps.Application.Events;
using PromptOps.Application.Evaluations;
using PromptOps.Application.Executions;
using PromptOps.Domain;
using PromptOps.Domain.Evaluations;
using PromptOps.Domain.Executions;
using PromptOps.Infrastructure.Evaluations;

namespace PromptOps.Infrastructure.Tests;

public class DelegatedAIEvaluationServiceTests
{
    private static ExecutionRecord SeededExecution(FakeExecutionRepository repository)
    {
        var execution = ExecutionRecord.Start(
            Guid.NewGuid(), "alice",
            new DevelopmentContext
            {
                Repository = "github.com/ctacke/PromptOps",
                AcceptanceCriteria = ["Endpoint returns 404 for unknown ids"],
                ReferencedADRs = ["ADR-0010"]
            });
        execution.Finish("the diff", TimeSpan.FromSeconds(1), "manual", null, null, ["a.cs"], 5, 1);
        repository.Seed(execution);
        return execution;
    }

    private static DelegatedAIEvaluationService CreateService(
        FakeExecutionRepository? executions = null,
        IPendingDelegatedEvaluationStore? pendingStore = null,
        FakeAIEvaluationRepository? evaluations = null,
        FakeDomainEventPublisher? publisher = null) =>
        new(
            executions ?? new FakeExecutionRepository(),
            pendingStore ?? new InMemoryPendingDelegatedEvaluationStore(),
            evaluations ?? new FakeAIEvaluationRepository(),
            publisher ?? new FakeDomainEventPublisher());

    [Fact]
    public async Task PrepareAsync_returns_a_prompt_and_correlation_id()
    {
        var executions = new FakeExecutionRepository();
        var execution = SeededExecution(executions);
        var service = CreateService(executions);

        var prepared = await service.PrepareAsync(execution.Id);

        Assert.NotEqual(Guid.Empty, prepared.CorrelationId);
        Assert.Contains("github.com/ctacke/PromptOps", prepared.Prompt);
    }

    [Fact]
    public async Task PrepareAsync_throws_for_an_unknown_execution()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ExecutionNotFoundException>(() => service.PrepareAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task SubmitAsync_with_a_valid_response_records_and_returns_the_evaluation()
    {
        var executions = new FakeExecutionRepository();
        var execution = SeededExecution(executions);
        var evaluations = new FakeAIEvaluationRepository();
        var publisher = new FakeDomainEventPublisher();
        var service = CreateService(executions, evaluations: evaluations, publisher: publisher);
        var prepared = await service.PrepareAsync(execution.Id);

        var result = await service.SubmitAsync(
            prepared.CorrelationId,
            """{"satisfiesAcceptanceCriteria":true,"adrViolations":[],"ignoredRequirements":[],"unnecessaryComplexityNotes":null,"suggestedPromptImprovements":["be more specific"]}""");

        Assert.True(result.Succeeded);
        Assert.Equal("client-delegated", result.Evaluation!.JudgeProviderId);
        Assert.True(result.Evaluation.SatisfiesAcceptanceCriteria);
        Assert.Single(evaluations.Added);
        Assert.NotEmpty(publisher.Published);
    }

    [Fact]
    public async Task SubmitAsync_with_an_invalid_response_asks_for_a_retry_and_keeps_the_correlation_pending()
    {
        var executions = new FakeExecutionRepository();
        var execution = SeededExecution(executions);
        var service = CreateService(executions);
        var prepared = await service.PrepareAsync(execution.Id);

        var result = await service.SubmitAsync(prepared.CorrelationId, "not json");

        Assert.False(result.Succeeded);
        Assert.NotNull(result.RetryPrompt);
        Assert.Contains("not json", result.RetryPrompt);

        // Correlation id is still pending — a second attempt is possible.
        var second = await service.SubmitAsync(
            prepared.CorrelationId,
            """{"satisfiesAcceptanceCriteria":true}""");
        Assert.True(second.Succeeded);
    }

    [Fact]
    public async Task SubmitAsync_throws_once_every_attempt_is_exhausted()
    {
        var executions = new FakeExecutionRepository();
        var execution = SeededExecution(executions);
        var service = CreateService(executions);
        var prepared = await service.PrepareAsync(execution.Id);

        for (var i = 0; i < JudgePromptBuilder.MaxAttempts - 1; i++)
        {
            var retry = await service.SubmitAsync(prepared.CorrelationId, "still not json");
            Assert.False(retry.Succeeded);
        }

        await Assert.ThrowsAsync<AIJudgeResponseInvalidException>(
            () => service.SubmitAsync(prepared.CorrelationId, "still not json"));
    }

    [Fact]
    public async Task SubmitAsync_throws_for_an_unknown_correlation_id()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<PendingEvaluationNotFoundException>(
            () => service.SubmitAsync(Guid.NewGuid(), """{"satisfiesAcceptanceCriteria":true}"""));
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

        public Task<ExecutionRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_executions.GetValueOrDefault(id));

        public Task<IReadOnlyList<ExecutionRecord>> GetByPromptVersionIdAsync(Guid promptVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExecutionRecord>>(_executions.Values.Where(e => e.PromptVersionId == promptVersionId).ToList());

        public Task UpdateAsync(ExecutionRecord execution, CancellationToken cancellationToken = default)
        {
            _executions[execution.Id] = execution;
            return Task.CompletedTask;
        }

        public Task<ExecutionStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeAIEvaluationRepository : IAIEvaluationRepository
    {
        public List<AIEvaluation> Added { get; } = [];

        public Task AddAsync(AIEvaluation evaluation, CancellationToken cancellationToken = default)
        {
            Added.Add(evaluation);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AIEvaluation>> GetByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AIEvaluation>>(Added.Where(e => e.ExecutionId == executionId).ToList());

        public Task<int> GetCountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeDomainEventPublisher : IDomainEventPublisher
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Published.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
