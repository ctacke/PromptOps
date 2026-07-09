using PromptOps.Domain.Scoring;
using Xunit;

namespace PromptOps.Domain.Tests.Scoring;

public class ScoringConfigTests
{
    [Fact]
    public void Create_Rejects_Empty_Name()
    {
        Assert.Throws<ArgumentException>(() => ScoringConfig.Create(" ", 1, new ScoringWeights()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_Rejects_Version_Below_One(int invalidVersion)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScoringConfig.Create("default", invalidVersion, new ScoringWeights()));
    }

    [Fact]
    public void Create_Rejects_Negative_Weight()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScoringConfig.Create(
            "default", 1, new ScoringWeights { HumanRating = -0.1 }));
    }

    [Fact]
    public void Create_Allows_Zero_Weight_For_Any_Component()
    {
        var config = ScoringConfig.Create("default", 1, new ScoringWeights { HumanRating = 0, Sonar = 0.5 });

        Assert.Equal(0, config.Weights.HumanRating);
        Assert.Equal(0.5, config.Weights.Sonar);
    }

    [Fact]
    public void Create_Sets_Fields_And_Defaults_CreatedAt_To_Now()
    {
        var before = DateTimeOffset.UtcNow;

        var config = ScoringConfig.Create("default", 3, new ScoringWeights { HumanRating = 0.5 });

        Assert.Equal("default", config.Name);
        Assert.Equal(3, config.Version);
        Assert.InRange(config.CreatedAt, before, DateTimeOffset.UtcNow);
    }
}
