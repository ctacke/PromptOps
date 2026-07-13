using PromptOps.Domain.Refinement;
using Xunit;

namespace PromptOps.Domain.Tests.Refinement;

public class RefinementCandidateTests
{
    [Fact]
    public void Create_Starts_Pending_With_No_Scores()
    {
        var candidate = RefinementCandidate.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(RefinementCandidateStatus.PendingBenchmark, candidate.Status);
        Assert.Null(candidate.ActiveScore);
        Assert.Null(candidate.CandidateScore);
        Assert.Null(candidate.EvaluatedAt);
    }

    [Fact]
    public void MarkEligible_Records_Scores_And_Status()
    {
        var candidate = RefinementCandidate.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        candidate.MarkEligible(activeScore: 70, candidateScore: 85);

        Assert.Equal(RefinementCandidateStatus.AbEligible, candidate.Status);
        Assert.Equal(70, candidate.ActiveScore);
        Assert.Equal(85, candidate.CandidateScore);
        Assert.NotNull(candidate.EvaluatedAt);
    }

    [Fact]
    public void Reject_Records_Scores_And_Status()
    {
        var candidate = RefinementCandidate.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        candidate.Reject(activeScore: 80, candidateScore: 60);

        Assert.Equal(RefinementCandidateStatus.Rejected, candidate.Status);
        Assert.Equal(80, candidate.ActiveScore);
        Assert.Equal(60, candidate.CandidateScore);
    }

    [Fact]
    public void Resolving_Twice_Throws()
    {
        var candidate = RefinementCandidate.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        candidate.MarkEligible(70, 85);

        Assert.Throws<InvalidOperationException>(() => candidate.Reject(80, 60));
    }

    [Fact]
    public void Create_Requires_Non_Empty_Ids()
    {
        Assert.Throws<ArgumentException>(() => RefinementCandidate.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => RefinementCandidate.Create(Guid.NewGuid(), Guid.Empty, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => RefinementCandidate.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty));
    }
}
