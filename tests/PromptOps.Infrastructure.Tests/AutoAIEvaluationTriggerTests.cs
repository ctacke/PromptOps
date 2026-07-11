using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PromptOps.Application.Evaluations;
using PromptOps.Application.Events;
using PromptOps.Application.Providers;
using PromptOps.Domain;
using PromptOps.Domain.Evaluations;
using PromptOps.Domain.Executions;
using PromptOps.Infrastructure.Evaluations;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>
/// Pure unit tests against a real DI container of fakes (needed because <see cref="AutoAIEvaluationTrigger"/>
/// resolves <see cref="AIEvaluationService"/> itself via <see cref="IServiceScopeFactory"/>, the same
/// "escape the request scope" mechanism <c>DebouncedScoreRecomputeScheduler</c> uses) — not the
/// lightweight hand-rolled fakes used elsewhere in this file, since there's a real scope boundary
/// to exercise here.
/// </summary>
public class AutoAIEvaluationTriggerTests
{
    private static ExecutionRecorded EventFor(Guid executionId) => new(executionId, Guid.NewGuid(), "repo-a", DateTimeOffset.UtcNow);

    private static (AutoAIEvaluationTrigger Trigger, FakeAIEvaluationProvider Provider, FakePolicyRepository Policy) Build(
        bool autoEvaluateOnFinish, AutoEvaluationMechanism mechanism = AutoEvaluationMechanism.Daemon)
    {
        var provider = new FakeAIEvaluationProvider();
        var services = new ServiceCollection();
        services.AddScoped<AIEvaluationService>();
        services.AddScoped<IAIEvaluationProvider>(_ => provider);
        services.AddScoped<IAIEvaluationRepository, FakeAIEvaluationRepository>();
        services.AddScoped<IDomainEventPublisher, NoopDomainEventPublisher>();
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var policy = AIEvaluationPolicy.CreateDefault();
        policy.Update(autoEvaluateOnFinish, mechanism);
        var policyRepository = new FakePolicyRepository(policy);

        var trigger = new AutoAIEvaluationTrigger(scopeFactory, policyRepository, NullLogger<AutoAIEvaluationTrigger>.Instance);
        return (trigger, provider, policyRepository);
    }

    [Fact]
    public async Task Does_Nothing_When_Auto_Evaluate_Is_Disabled()
    {
        var (trigger, provider, _) = Build(autoEvaluateOnFinish: false);

        await trigger.HandleAsync(EventFor(Guid.NewGuid()));
        await Task.Delay(50); // give any (unwanted) background work a chance to run

        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task Does_Nothing_When_Mechanism_Is_ClientHook_Even_If_Auto_Evaluate_Is_Enabled()
    {
        var (trigger, provider, _) = Build(autoEvaluateOnFinish: true, mechanism: AutoEvaluationMechanism.ClientHook);

        await trigger.HandleAsync(EventFor(Guid.NewGuid()));
        await Task.Delay(50); // give any (unwanted) background work a chance to run

        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task Runs_The_Evaluation_When_Auto_Evaluate_Is_Enabled()
    {
        var (trigger, provider, _) = Build(autoEvaluateOnFinish: true);
        var executionId = Guid.NewGuid();

        await trigger.HandleAsync(EventFor(executionId));
        var completed = await Task.WhenAny(provider.Invoked.Task, Task.Delay(TimeSpan.FromSeconds(5))) == provider.Invoked.Task;

        Assert.True(completed, "expected the background evaluation to run within 5 seconds");
        Assert.Equal(executionId, provider.LastExecutionId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Before_The_Background_Evaluation_Completes()
    {
        var (trigger, provider, _) = Build(autoEvaluateOnFinish: true);
        provider.Gate = new TaskCompletionSource();

        var handleTask = trigger.HandleAsync(EventFor(Guid.NewGuid()));
        var handleCompleted = await Task.WhenAny(handleTask, Task.Delay(TimeSpan.FromSeconds(2))) == handleTask;

        Assert.True(handleCompleted, "HandleAsync must not block on the LLM call");
        Assert.Equal(0, provider.CallCount); // still gated, hasn't actually run the (fake) judge yet

        provider.Gate.SetResult();
        await Task.WhenAny(provider.Invoked.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task A_Failed_Background_Evaluation_Never_Propagates_To_The_Caller()
    {
        var (trigger, provider, _) = Build(autoEvaluateOnFinish: true);
        provider.ShouldThrow = true;

        var exception = await Record.ExceptionAsync(() => trigger.HandleAsync(EventFor(Guid.NewGuid())));

        Assert.Null(exception);
    }

    private sealed class FakePolicyRepository(AIEvaluationPolicy policy) : IAIEvaluationPolicyRepository
    {
        public Task<AIEvaluationPolicy?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<AIEvaluationPolicy?>(policy);
        public Task AddAsync(AIEvaluationPolicy policy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(AIEvaluationPolicy policy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeAIEvaluationProvider : IAIEvaluationProvider
    {
        public string Name => "fake";
        public int CallCount { get; private set; }
        public Guid LastExecutionId { get; private set; }
        public bool ShouldThrow { get; set; }
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource Invoked { get; } = new();

        public async Task<AIEvaluation> EvaluateAsync(Guid executionId, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
        {
            if (Gate is not null)
                await Gate.Task;

            CallCount++;
            LastExecutionId = executionId;
            Invoked.TrySetResult();

            if (ShouldThrow)
                throw new InvalidOperationException("simulated judge failure");

            return AIEvaluation.Record(executionId, Name, null, true, [], [], null, [], "{}");
        }
    }

    private sealed class FakeAIEvaluationRepository : IAIEvaluationRepository
    {
        public Task AddAsync(AIEvaluation evaluation, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AIEvaluation>> GetByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AIEvaluation>>([]);
        public Task<int> GetCountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopDomainEventPublisher : IDomainEventPublisher
    {
        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
