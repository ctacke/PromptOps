using PromptOps.Domain.Scoring;
using Xunit;

namespace PromptOps.Domain.Tests.Scoring;

public class PromptScoreTests
{
    [Fact]
    public void Compute_Rejects_Empty_PromptVersionId()
    {
        Assert.Throws<ArgumentException>(() => PromptScore.Compute(
            Guid.Empty, Guid.NewGuid(), 80.0, new Dictionary<string, double>(), 3));
    }

    [Fact]
    public void Compute_Rejects_Empty_ScoringConfigId()
    {
        Assert.Throws<ArgumentException>(() => PromptScore.Compute(
            Guid.NewGuid(), Guid.Empty, 80.0, new Dictionary<string, double>(), 3));
    }

    [Fact]
    public void Compute_Rejects_Negative_SampleSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PromptScore.Compute(
            Guid.NewGuid(), Guid.NewGuid(), 80.0, new Dictionary<string, double>(), -1));
    }

    [Fact]
    public void Compute_Sets_Fields_And_Defaults_ComputedAt_To_Now()
    {
        var promptVersionId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var componentScores = new Dictionary<string, double> { ["humanRating"] = 90.0 };
        var before = DateTimeOffset.UtcNow;

        var score = PromptScore.Compute(promptVersionId, configId, 90.0, componentScores, 5);

        Assert.Equal(promptVersionId, score.PromptVersionId);
        Assert.Equal(configId, score.ScoringConfigId);
        Assert.Equal(90.0, score.OverallScore);
        Assert.Equal(5, score.SampleSize);
        Assert.Equal(90.0, score.ComponentScores["humanRating"]);
        Assert.InRange(score.ComputedAt, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Compute_Allows_Zero_SampleSize_For_A_PromptVersion_With_No_Executions_Yet()
    {
        var score = PromptScore.Compute(Guid.NewGuid(), Guid.NewGuid(), 0.0, new Dictionary<string, double>(), 0);

        Assert.Equal(0, score.SampleSize);
        Assert.Equal(0.0, score.OverallScore);
        Assert.Empty(score.ComponentScores);
    }

    [Fact]
    public void Compute_Raises_ScoreComputed()
    {
        var promptVersionId = Guid.NewGuid();
        var configId = Guid.NewGuid();

        var score = PromptScore.Compute(promptVersionId, configId, 75.0, new Dictionary<string, double>(), 2);

        var domainEvent = Assert.Single(score.DomainEvents);
        var computed = Assert.IsType<ScoreComputed>(domainEvent);
        Assert.Equal(score.Id, computed.PromptScoreId);
        Assert.Equal(promptVersionId, computed.PromptVersionId);
        Assert.Equal(configId, computed.ScoringConfigId);
        Assert.Equal(75.0, computed.OverallScore);
    }

    [Fact]
    public void Rehydrate_Does_Not_Raise_A_Domain_Event()
    {
        var score = PromptScore.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            85.0, new Dictionary<string, double> { ["sonar"] = 85.0 }, 4);

        Assert.Empty(score.DomainEvents);
    }
}
