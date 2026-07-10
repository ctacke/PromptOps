using PromptOps.Domain.Evaluations;
using Xunit;

namespace PromptOps.Domain.Tests.Evaluations;

public class AIEvaluationPolicyTests
{
    [Fact]
    public void CreateDefault_Has_Automatic_Evaluation_Off()
    {
        var policy = AIEvaluationPolicy.CreateDefault();

        Assert.False(policy.AutoEvaluateOnFinish);
    }

    [Fact]
    public void Update_Sets_The_Flag_And_Stamps_UpdatedAt()
    {
        var policy = AIEvaluationPolicy.CreateDefault();
        var before = DateTimeOffset.UtcNow;

        policy.Update(autoEvaluateOnFinish: true);

        Assert.True(policy.AutoEvaluateOnFinish);
        Assert.InRange(policy.UpdatedAt, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Update_Can_Turn_It_Back_Off()
    {
        var policy = AIEvaluationPolicy.CreateDefault();
        policy.Update(autoEvaluateOnFinish: true);

        policy.Update(autoEvaluateOnFinish: false);

        Assert.False(policy.AutoEvaluateOnFinish);
    }

    [Fact]
    public void Rehydrate_Reconstructs_A_Persisted_Policy()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var policy = AIEvaluationPolicy.Rehydrate(id, autoEvaluateOnFinish: true, now);

        Assert.Equal(id, policy.Id);
        Assert.True(policy.AutoEvaluateOnFinish);
        Assert.Equal(now, policy.UpdatedAt);
    }
}
