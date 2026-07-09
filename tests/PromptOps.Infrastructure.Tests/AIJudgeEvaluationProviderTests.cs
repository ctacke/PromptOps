using PromptOps.Application.Evaluations;
using PromptOps.Application.Executions;
using PromptOps.Application.Providers;
using PromptOps.Domain.Executions;
using PromptOps.Infrastructure.Providers;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>Pure unit tests — no SQLite involved, so a stub repository is enough (unlike the other *IntegrationTests classes in this project).</summary>
public class AIJudgeEvaluationProviderTests
{
    private static ExecutionRecord SeededExecution(FakeExecutionRepository repository)
    {
        var execution = ExecutionRecord.Start(
            Guid.NewGuid(), "alice",
            new DevelopmentContext
            {
                Repository = "github.com/ctacke/PromptOps",
                AcceptanceCriteria = ["Endpoint returns 404 for unknown ids"],
                ReferencedADRs = ["ADR-0002"]
            });
        execution.Finish("the diff", TimeSpan.FromSeconds(1), "manual", null, null, ["a.cs"], 5, 1);
        repository.Seed(execution);
        return execution;
    }

    [Fact]
    public async Task Parses_A_Valid_Response_On_The_First_Attempt()
    {
        var repository = new FakeExecutionRepository();
        var execution = SeededExecution(repository);
        var stub = new QueuedAIExecutionProvider("""{"satisfiesAcceptanceCriteria":true,"adrViolations":[],"ignoredRequirements":[],"unnecessaryComplexityNotes":null,"suggestedPromptImprovements":["be more specific"]}""");
        var provider = new AIJudgeEvaluationProvider(stub, repository);

        var evaluation = await provider.EvaluateAsync(execution.Id, new Dictionary<string, string>());

        Assert.Equal(1, stub.CallCount);
        Assert.Equal(execution.Id, evaluation.ExecutionId);
        Assert.True(evaluation.SatisfiesAcceptanceCriteria);
        Assert.Equal(["be more specific"], evaluation.SuggestedPromptImprovements);
    }

    [Fact]
    public async Task Tolerates_Markdown_Fences_And_Surrounding_Prose()
    {
        var repository = new FakeExecutionRepository();
        var execution = SeededExecution(repository);
        var stub = new QueuedAIExecutionProvider("""
            Here's my assessment:
            ```json
            {"satisfiesAcceptanceCriteria": false, "adrViolations": ["ADR-0002"]}
            ```
            Let me know if you need more detail.
            """);
        var provider = new AIJudgeEvaluationProvider(stub, repository);

        var evaluation = await provider.EvaluateAsync(execution.Id, new Dictionary<string, string>());

        Assert.False(evaluation.SatisfiesAcceptanceCriteria);
        Assert.Equal(["ADR-0002"], evaluation.AdrViolations);
    }

    [Fact]
    public async Task Missing_Optional_Array_Fields_Default_To_Empty_Rather_Than_Failing()
    {
        var repository = new FakeExecutionRepository();
        var execution = SeededExecution(repository);
        var stub = new QueuedAIExecutionProvider("""{"satisfiesAcceptanceCriteria":true}""");
        var provider = new AIJudgeEvaluationProvider(stub, repository);

        var evaluation = await provider.EvaluateAsync(execution.Id, new Dictionary<string, string>());

        Assert.Empty(evaluation.AdrViolations);
        Assert.Empty(evaluation.IgnoredRequirements);
        Assert.Empty(evaluation.SuggestedPromptImprovements);
        Assert.Null(evaluation.UnnecessaryComplexityNotes);
    }

    [Fact]
    public async Task Retries_After_A_Malformed_Response_And_Succeeds_On_The_Second_Attempt()
    {
        var repository = new FakeExecutionRepository();
        var execution = SeededExecution(repository);
        var stub = new QueuedAIExecutionProvider(
            "not json at all",
            """{"satisfiesAcceptanceCriteria":true}""");
        var provider = new AIJudgeEvaluationProvider(stub, repository);

        var evaluation = await provider.EvaluateAsync(execution.Id, new Dictionary<string, string>());

        Assert.Equal(2, stub.CallCount);
        Assert.True(evaluation.SatisfiesAcceptanceCriteria);
        // The retry prompt should carry the parse failure forward so the judge can self-correct.
        Assert.Contains("could not be parsed", stub.PromptsSeen[1]);
    }

    [Fact]
    public async Task Throws_After_Exhausting_Retries_On_Persistently_Malformed_Responses()
    {
        var repository = new FakeExecutionRepository();
        var execution = SeededExecution(repository);
        var stub = new QueuedAIExecutionProvider("garbage 1", "garbage 2", "garbage 3");
        var provider = new AIJudgeEvaluationProvider(stub, repository);

        var ex = await Assert.ThrowsAsync<AIJudgeResponseInvalidException>(
            () => provider.EvaluateAsync(execution.Id, new Dictionary<string, string>()));

        Assert.Equal(3, stub.CallCount);
        Assert.Equal(execution.Id, ex.ExecutionId);
        Assert.Equal(3, ex.Attempts);
    }

    [Fact]
    public async Task Throws_ExecutionNotFoundException_Without_Calling_The_Judge()
    {
        var repository = new FakeExecutionRepository();
        var stub = new QueuedAIExecutionProvider("""{"satisfiesAcceptanceCriteria":true}""");
        var provider = new AIJudgeEvaluationProvider(stub, repository);

        await Assert.ThrowsAsync<ExecutionNotFoundException>(
            () => provider.EvaluateAsync(Guid.NewGuid(), new Dictionary<string, string>()));

        Assert.Equal(0, stub.CallCount);
    }

    private sealed class QueuedAIExecutionProvider(params string[] responses) : IAIExecutionProvider
    {
        private readonly Queue<string> _responses = new(responses);

        public string Name => "stub-judge";
        public int CallCount { get; private set; }
        public List<string> PromptsSeen { get; } = [];

        public Task<string> ExecuteAsync(string promptContent, IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken = default)
        {
            CallCount++;
            PromptsSeen.Add(promptContent);
            if (_responses.Count == 0)
                throw new InvalidOperationException("QueuedAIExecutionProvider ran out of canned responses.");
            return Task.FromResult(_responses.Dequeue());
        }
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

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
