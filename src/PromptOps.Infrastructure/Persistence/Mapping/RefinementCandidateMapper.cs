using PromptOps.Domain.Refinement;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Infrastructure.Persistence.Mapping;

internal static class RefinementCandidateMapper
{
    public static RefinementCandidateEntity ToNewEntity(RefinementCandidate candidate) => new()
    {
        Id = candidate.Id,
        PromptId = candidate.PromptId,
        DraftVersionId = candidate.DraftVersionId,
        ActiveVersionId = candidate.ActiveVersionId,
        Status = candidate.Status.ToString(),
        ActiveScore = candidate.ActiveScore,
        CandidateScore = candidate.CandidateScore,
        CreatedAt = candidate.CreatedAt,
        EvaluatedAt = candidate.EvaluatedAt
    };

    public static void ApplyChanges(RefinementCandidateEntity entity, RefinementCandidate candidate)
    {
        entity.Status = candidate.Status.ToString();
        entity.ActiveScore = candidate.ActiveScore;
        entity.CandidateScore = candidate.CandidateScore;
        entity.EvaluatedAt = candidate.EvaluatedAt;
    }

    public static RefinementCandidate ToDomain(RefinementCandidateEntity entity) => RefinementCandidate.Rehydrate(
        entity.Id,
        entity.PromptId,
        entity.DraftVersionId,
        entity.ActiveVersionId,
        Enum.Parse<RefinementCandidateStatus>(entity.Status),
        entity.ActiveScore,
        entity.CandidateScore,
        entity.CreatedAt,
        entity.EvaluatedAt);
}
