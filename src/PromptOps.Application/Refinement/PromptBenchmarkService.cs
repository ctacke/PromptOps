using PromptOps.Application.Prompts;
using PromptOps.Application.Providers;
using PromptOps.Domain.Prompts;
using PromptOps.Domain.Refinement;

namespace PromptOps.Application.Refinement;

/// <summary>
/// Phase 16b: the synthetic-benchmark pre-screen. Given a freshly-drafted refinement (Phase 16a),
/// runs it and the active version against generated inputs via <see cref="IPromptBenchmarkProvider"/>
/// and gates on the result: a draft that beats the active version by the policy's margin becomes
/// <see cref="RefinementCandidateStatus.AbEligible"/> (may receive A/B shadow traffic in Phase 16c);
/// one that regresses is <see cref="RefinementCandidateStatus.Rejected"/> and its Draft version is
/// deprecated so it never reaches real developer work — the whole point of the gate.
///
/// Kept an ordinary, unit-testable application service; <c>PromptRefinementTrigger</c> chains it
/// after a successful draft, in the same detached background task.
/// </summary>
public sealed class PromptBenchmarkService(
    IRefinementPolicyRepository policyRepository,
    IPromptRepository promptRepository,
    IRefinementCandidateRepository candidateRepository,
    IPromptBenchmarkProvider benchmarkProvider,
    PromptService promptService)
{
    public async Task<BenchmarkGateResult> BenchmarkCandidateAsync(
        Guid promptId,
        Guid draftVersionId,
        CancellationToken cancellationToken = default)
    {
        var prompt = await promptRepository.GetByIdAsync(promptId, cancellationToken);
        var draft = prompt?.Versions.FirstOrDefault(v => v.Id == draftVersionId);
        if (prompt is null || draft is null || draft.Status != PromptVersionStatus.Draft)
            return new BenchmarkGateResult(BenchmarkOutcome.NotADraft);

        var active = prompt.Versions.FirstOrDefault(v => v.Status == PromptVersionStatus.Active);
        if (active is null)
            return new BenchmarkGateResult(BenchmarkOutcome.NoActiveBaseline);

        var candidate = RefinementCandidate.Create(promptId, draftVersionId, active.Id);

        var policy = await policyRepository.GetAsync(cancellationToken);
        var sampleSize = policy?.SyntheticSampleSize ?? 0;
        var minDelta = policy?.MinQualityDelta ?? 0;

        // Benchmarking disabled → leave the draft pending manual review; never auto-adopt unbenchmarked.
        if (sampleSize <= 0)
            return await PersistAsync(candidate, BenchmarkOutcome.BenchmarkingDisabled, cancellationToken);

        var comparison = await benchmarkProvider.CompareAsync(active.Content, draft.Content, prompt.Metadata.Tags, sampleSize, cancellationToken);

        // No usable benchmark (e.g. a no-op execution backend generated no scenarios) → inconclusive,
        // NOT a failure: leave the draft pending rather than deprecating it on no evidence.
        if (comparison is null)
            return await PersistAsync(candidate, BenchmarkOutcome.Inconclusive, cancellationToken);

        if (comparison.CandidateScore >= comparison.ActiveScore + minDelta)
        {
            candidate.MarkEligible(comparison.ActiveScore, comparison.CandidateScore);
            await candidateRepository.AddAsync(candidate, cancellationToken);
            await candidateRepository.SaveChangesAsync(cancellationToken);
            return new BenchmarkGateResult(BenchmarkOutcome.AbEligible, comparison);
        }

        candidate.Reject(comparison.ActiveScore, comparison.CandidateScore);
        await candidateRepository.AddAsync(candidate, cancellationToken);
        await candidateRepository.SaveChangesAsync(cancellationToken);
        // Deprecate the losing draft so it can never be recommended or promoted.
        await promptService.DeprecatePromptVersionAsync(promptId, draftVersionId, cancellationToken);
        return new BenchmarkGateResult(BenchmarkOutcome.Rejected, comparison);
    }

    private async Task<BenchmarkGateResult> PersistAsync(RefinementCandidate candidate, BenchmarkOutcome outcome, CancellationToken cancellationToken)
    {
        await candidateRepository.AddAsync(candidate, cancellationToken);
        await candidateRepository.SaveChangesAsync(cancellationToken);
        return new BenchmarkGateResult(outcome);
    }
}

/// <summary>What the benchmark gate decided for a drafted candidate.</summary>
public enum BenchmarkOutcome
{
    NotADraft,
    NoActiveBaseline,
    BenchmarkingDisabled,
    Inconclusive,
    AbEligible,
    Rejected
}

public sealed record BenchmarkGateResult(BenchmarkOutcome Outcome, BenchmarkComparison? Comparison = null);
