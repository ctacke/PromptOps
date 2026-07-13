namespace PromptOps.Application.Providers;

/// <summary>
/// Compares a candidate (refined) prompt against the active one on generated inputs (Phase 16b) —
/// the offline safety gate that keeps a draft that would regress quality away from real developer
/// work. Like the judge/classifier/refiner, the reference implementation is built on
/// <c>IAIExecutionProvider</c>: it generates synthetic task scenarios, runs both prompts on each,
/// and grades the outputs, all as ordinary prompt executions.
/// </summary>
public interface IPromptBenchmarkProvider
{
    /// <summary>
    /// Returns comparable average quality scores for the active and candidate prompt over
    /// <paramref name="sampleSize"/> generated scenarios, or <c>null</c> if no usable benchmark could
    /// be produced (e.g. no scenarios could be generated — which happens with a no-op execution
    /// backend). A <c>null</c> result must be treated as "inconclusive," never as "the candidate
    /// failed."
    /// </summary>
    Task<BenchmarkComparison?> CompareAsync(
        string activeContent,
        string candidateContent,
        IReadOnlyList<string> tags,
        int sampleSize,
        CancellationToken cancellationToken = default);
}

/// <summary>Average quality (0-100) of each prompt across the graded scenarios, plus how many scenarios were actually graded.</summary>
public sealed record BenchmarkComparison(double ActiveScore, double CandidateScore, int SampleSize);
