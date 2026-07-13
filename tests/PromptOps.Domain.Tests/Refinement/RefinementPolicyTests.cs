using PromptOps.Domain.Refinement;
using Xunit;

namespace PromptOps.Domain.Tests.Refinement;

public class RefinementPolicyTests
{
    [Fact]
    public void CreateDefault_Has_Automatic_Refinement_Off()
    {
        var policy = RefinementPolicy.CreateDefault();

        Assert.False(policy.AutoRefinementEnabled);
    }

    [Fact]
    public void Update_Sets_Fields_And_Stamps_UpdatedAt()
    {
        var policy = RefinementPolicy.CreateDefault(DateTimeOffset.UtcNow.AddDays(-1));
        var before = policy.UpdatedAt;

        policy.Update(autoRefinementEnabled: true, syntheticSampleSize: 5, minQualityDelta: 2.5, abExplorationRate: 0.1, DateTimeOffset.UtcNow);

        Assert.True(policy.AutoRefinementEnabled);
        Assert.Equal(5, policy.SyntheticSampleSize);
        Assert.Equal(2.5, policy.MinQualityDelta);
        Assert.Equal(0.1, policy.AbExplorationRate);
        Assert.True(policy.UpdatedAt > before);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -0.1)]
    [InlineData(0, 0, 1.1)]
    public void Update_Rejects_Out_Of_Range_Settings(int sampleSize, double minDelta, double explorationRate)
    {
        var policy = RefinementPolicy.CreateDefault();

        Assert.Throws<ArgumentOutOfRangeException>(() => policy.Update(true, sampleSize, minDelta, explorationRate));
    }

    [Fact]
    public void Rehydrate_Restores_Persisted_State()
    {
        var id = Guid.NewGuid();
        var updatedAt = DateTimeOffset.UtcNow.AddHours(-3);

        var policy = RefinementPolicy.Rehydrate(id, autoRefinementEnabled: true, syntheticSampleSize: 3, minQualityDelta: 1.0, abExplorationRate: 0.2, updatedAt);

        Assert.Equal(id, policy.Id);
        Assert.True(policy.AutoRefinementEnabled);
        Assert.Equal(3, policy.SyntheticSampleSize);
        Assert.Equal(1.0, policy.MinQualityDelta);
        Assert.Equal(0.2, policy.AbExplorationRate);
        Assert.Equal(updatedAt, policy.UpdatedAt);
    }
}
