using PromptOps.Domain.Promotion;
using Xunit;

namespace PromptOps.Domain.Tests.Promotion;

public class PromotionPolicyTests
{
    [Fact]
    public void CreateDefault_Requires_Human_Evaluation_And_Disables_Auto_Promotion()
    {
        var policy = PromotionPolicy.CreateDefault();

        Assert.True(policy.RequireHumanEvaluation);
        Assert.False(policy.AutoPromotionEnabled);
        Assert.Null(policy.MinimumScoreThreshold);
        Assert.Null(policy.MinimumMarginOverActive);
    }

    [Fact]
    public void Update_Rejects_Enabling_Auto_Promotion_While_Human_Evaluation_Is_Still_Required()
    {
        var policy = PromotionPolicy.CreateDefault();

        Assert.Throws<InvalidOperationException>(
            () => policy.Update(requireHumanEvaluation: true, autoPromotionEnabled: true, minimumScoreThreshold: 80, minimumMarginOverActive: null));
    }

    [Fact]
    public void Update_Rejects_Enabling_Auto_Promotion_With_Neither_Threshold_Nor_Margin_Set()
    {
        var policy = PromotionPolicy.CreateDefault();

        Assert.Throws<InvalidOperationException>(
            () => policy.Update(requireHumanEvaluation: false, autoPromotionEnabled: true, minimumScoreThreshold: null, minimumMarginOverActive: null));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Update_Rejects_A_Threshold_Outside_Zero_To_A_Hundred(double invalidThreshold)
    {
        var policy = PromotionPolicy.CreateDefault();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => policy.Update(requireHumanEvaluation: false, autoPromotionEnabled: false, minimumScoreThreshold: invalidThreshold, minimumMarginOverActive: null));
    }

    [Fact]
    public void Update_Rejects_A_Negative_Margin()
    {
        var policy = PromotionPolicy.CreateDefault();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => policy.Update(requireHumanEvaluation: false, autoPromotionEnabled: false, minimumScoreThreshold: null, minimumMarginOverActive: -0.1));
    }

    [Fact]
    public void Update_Accepts_A_Valid_Auto_Promotion_Configuration_And_Stamps_UpdatedAt()
    {
        var policy = PromotionPolicy.CreateDefault();
        var before = DateTimeOffset.UtcNow;

        policy.Update(requireHumanEvaluation: false, autoPromotionEnabled: true, minimumScoreThreshold: 85.0, minimumMarginOverActive: 5.0);

        Assert.False(policy.RequireHumanEvaluation);
        Assert.True(policy.AutoPromotionEnabled);
        Assert.Equal(85.0, policy.MinimumScoreThreshold);
        Assert.Equal(5.0, policy.MinimumMarginOverActive);
        Assert.InRange(policy.UpdatedAt, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Update_Allows_Turning_Auto_Promotion_Off_Again_Without_A_Threshold_Or_Margin()
    {
        var policy = PromotionPolicy.CreateDefault();
        policy.Update(requireHumanEvaluation: false, autoPromotionEnabled: true, minimumScoreThreshold: 85.0, minimumMarginOverActive: null);

        policy.Update(requireHumanEvaluation: true, autoPromotionEnabled: false, minimumScoreThreshold: null, minimumMarginOverActive: null);

        Assert.True(policy.RequireHumanEvaluation);
        Assert.False(policy.AutoPromotionEnabled);
    }
}
