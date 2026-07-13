using PromptOps.Application.Prompts;
using PromptOps.Application.Refinement;
using PromptOps.Domain.Evaluations;
using PromptOps.Domain.Executions;
using PromptOps.Domain.Prompts;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>
/// Unit tests (fakes, no SQLite) for Phase 16a's <see cref="PromptRefinementService"/> — the piece
/// that turns an AI judge's <c>SuggestedPromptImprovements</c> into a Draft <c>PromptVersion</c>.
/// Covers each guard and the happy path; the detached trigger wiring is tested separately in
/// <see cref="PromptRefinementTriggerTests"/>. Shared fakes live in <see cref="FakeRefiner"/> et al.
/// (RefinementTestFakes.cs).
/// </summary>
public class PromptRefinementServiceTests
{
    [Fact]
    public async Task Drafts_A_New_Version_From_The_Suggestions()
    {
        var fixture = new Fixture();
        var (_, versionId) = await fixture.SeedActivePromptAsync("Debug Helper", "Investigate the failure.");
        var executionId = fixture.SeedExecution(versionId);
        var evaluationId = fixture.SeedEvaluation(executionId, ["Ask for a reproduction first."]);
        fixture.Refiner.Result = "Investigate the failure. First, reproduce it reliably.";

        var result = await fixture.Service.RefineFromEvaluationAsync(evaluationId, executionId);

        Assert.Equal(RefinementOutcome.Drafted, result.Outcome);
        var prompt = await fixture.Prompts.GetByIdAsync(result.PromptId!.Value);
        var draft = Assert.Single(prompt!.Versions, v => v.Status == PromptVersionStatus.Draft);
        Assert.Equal("Investigate the failure. First, reproduce it reliably.", draft.Content);
        Assert.Equal(versionId, draft.ParentVersionId);
        Assert.Equal(PromptRefinementService.RefinerActor, draft.CreatedBy);
    }

    [Fact]
    public async Task Does_Not_Draft_When_There_Are_No_Suggestions()
    {
        var fixture = new Fixture();
        var (_, versionId) = await fixture.SeedActivePromptAsync("Debug Helper", "content");
        var executionId = fixture.SeedExecution(versionId);
        var evaluationId = fixture.SeedEvaluation(executionId, []);

        var result = await fixture.Service.RefineFromEvaluationAsync(evaluationId, executionId);

        Assert.Equal(RefinementOutcome.NoSuggestions, result.Outcome);
        Assert.Equal(0, fixture.Refiner.CallCount);
    }

    [Fact]
    public async Task Does_Not_Draft_For_An_Untracked_Execution()
    {
        var fixture = new Fixture();
        var executionId = fixture.SeedExecution(Guid.Empty); // untracked
        var evaluationId = fixture.SeedEvaluation(executionId, ["something"]);

        var result = await fixture.Service.RefineFromEvaluationAsync(evaluationId, executionId);

        Assert.Equal(RefinementOutcome.Untracked, result.Outcome);
    }

    [Fact]
    public async Task Does_Not_Draft_When_The_Attributed_Version_Is_Not_Active()
    {
        var fixture = new Fixture();
        var versionId = await fixture.SeedDraftOnlyPromptAsync("Draft Only", "content"); // never activated
        var executionId = fixture.SeedExecution(versionId);
        var evaluationId = fixture.SeedEvaluation(executionId, ["something"]);

        var result = await fixture.Service.RefineFromEvaluationAsync(evaluationId, executionId);

        Assert.Equal(RefinementOutcome.VersionNotActive, result.Outcome);
    }

    [Fact]
    public async Task Does_Not_Draft_A_Second_Candidate_When_A_Draft_Already_Exists()
    {
        var fixture = new Fixture();
        var (promptId, versionId) = await fixture.SeedActivePromptAsync("Debug Helper", "content");
        await fixture.PromptService.CreateVersionAsync(promptId, "an existing draft", "alice"); // in-flight Draft
        var executionId = fixture.SeedExecution(versionId);
        var evaluationId = fixture.SeedEvaluation(executionId, ["something"]);

        var result = await fixture.Service.RefineFromEvaluationAsync(evaluationId, executionId);

        Assert.Equal(RefinementOutcome.CandidateAlreadyExists, result.Outcome);
        Assert.Equal(0, fixture.Refiner.CallCount);
    }

    [Fact]
    public async Task Does_Not_Draft_When_The_Refiner_Returns_Nothing_Usable()
    {
        var fixture = new Fixture();
        var (_, versionId) = await fixture.SeedActivePromptAsync("Debug Helper", "content");
        var executionId = fixture.SeedExecution(versionId);
        var evaluationId = fixture.SeedEvaluation(executionId, ["something"]);
        fixture.Refiner.Result = "   "; // blank

        var result = await fixture.Service.RefineFromEvaluationAsync(evaluationId, executionId);

        Assert.Equal(RefinementOutcome.NoContentChange, result.Outcome);
    }

    private sealed class Fixture
    {
        public FakeMutablePromptRepository Prompts { get; } = new();
        public FakeSeededExecutionRepository Executions { get; } = new();
        public FakeSeededAIEvaluationRepository Evaluations { get; } = new();
        public FakeRefiner Refiner { get; } = new();
        public PromptService PromptService { get; }
        public PromptRefinementService Service { get; }

        public Fixture()
        {
            PromptService = new PromptService(Prompts, new NoopEmbeddingProvider(), new NoopEmbeddingStore());
            Service = new PromptRefinementService(Evaluations, Executions, Prompts, Refiner, PromptService);
        }

        public async Task<(Guid PromptId, Guid VersionId)> SeedActivePromptAsync(string name, string content)
        {
            var prompt = await PromptService.CreatePromptAsync(name);
            var version = await PromptService.CreateVersionAsync(prompt.Id, content, "alice");
            await PromptService.ActivateVersionAsync(prompt.Id, version.Id);
            return (prompt.Id, version.Id);
        }

        public async Task<Guid> SeedDraftOnlyPromptAsync(string name, string content)
        {
            var prompt = await PromptService.CreatePromptAsync(name);
            var version = await PromptService.CreateVersionAsync(prompt.Id, content, "alice");
            return version.Id;
        }

        public Guid SeedExecution(Guid promptVersionId)
        {
            var execution = ExecutionRecord.Start(promptVersionId, "alice", new DevelopmentContext { Repository = "repo-a" });
            Executions.Seed(execution);
            return execution.Id;
        }

        public Guid SeedEvaluation(Guid executionId, IReadOnlyList<string> suggestions)
        {
            var evaluation = AIEvaluation.Record(executionId, "judge", null, true, [], [], null, suggestions, "{}");
            Evaluations.Seed(evaluation);
            return evaluation.Id;
        }
    }
}
