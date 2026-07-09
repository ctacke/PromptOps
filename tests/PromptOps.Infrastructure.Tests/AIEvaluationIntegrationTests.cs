using PromptOps.Domain.Evaluations;
using PromptOps.Infrastructure.Persistence;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

public class AIEvaluationIntegrationTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    [Fact]
    public async Task Evaluations_round_trip_through_sqlite_including_list_fields()
    {
        var executionId = Guid.NewGuid();

        using (var dbContext = fixture.CreateContext())
        {
            var repository = new AIEvaluationRepository(dbContext);

            var evaluation = AIEvaluation.Record(
                executionId, "ai-judge", "claude-sonnet-5",
                satisfiesAcceptanceCriteria: false,
                adrViolations: ["ADR-0002: layering violated"],
                ignoredRequirements: ["missing null check"],
                unnecessaryComplexityNotes: "introduced an unused interface",
                suggestedPromptImprovements: ["name the target file explicitly", "reference the ADR"],
                rawResponse: """{"satisfiesAcceptanceCriteria":false}""");
            await repository.AddAsync(evaluation);
            await repository.SaveChangesAsync();
        }

        // Reload through a brand new context to prove this round-tripped through SQLite.
        using var freshContext = fixture.CreateContext();
        var reloaded = await new AIEvaluationRepository(freshContext).GetByExecutionIdAsync(executionId);

        var evaluationRow = Assert.Single(reloaded);
        Assert.Equal("ai-judge", evaluationRow.JudgeProviderId);
        Assert.Equal("claude-sonnet-5", evaluationRow.JudgeModel);
        Assert.False(evaluationRow.SatisfiesAcceptanceCriteria);
        Assert.Equal(["ADR-0002: layering violated"], evaluationRow.AdrViolations);
        Assert.Equal(["missing null check"], evaluationRow.IgnoredRequirements);
        Assert.Equal(2, evaluationRow.SuggestedPromptImprovements.Count);
        Assert.Equal("""{"satisfiesAcceptanceCriteria":false}""", evaluationRow.RawResponse);
    }

    [Fact]
    public async Task GetByExecutionIdAsync_returns_empty_for_an_execution_with_no_evaluations()
    {
        using var dbContext = fixture.CreateContext();
        var repository = new AIEvaluationRepository(dbContext);

        var result = await repository.GetByExecutionIdAsync(Guid.NewGuid());

        Assert.Empty(result);
    }
}
