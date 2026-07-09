using PromptOps.Application.Evaluations;
using PromptOps.Application.Executions;
using PromptOps.Application.Metrics;
using PromptOps.Application.Providers;
using PromptOps.Domain.Executions;
using PromptOps.Domain.Metrics;
using PromptOps.Domain.Scoring;

namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Default <see cref="IScoringProvider"/> (ADR-0003). Gathers every Finished execution of a
/// prompt version, and every <see cref="EngineeringMetrics"/>/<c>HumanEvaluation</c>/
/// <c>AIEvaluation</c> row attached to those executions, into eight normalized 0-100 component
/// scores (docs/scoring.md spells out exactly how each one is derived), then combines them into
/// a single weighted average.
///
/// The normalization that makes "zero-weight and missing-input edge cases" (the phase's explicit
/// testing requirement) behave correctly: a component is included in the weighted average only if
/// its <see cref="ScoringConfig"/> weight is greater than zero <em>and</em> at least one data
/// point exists for it. Both a zero weight and a genuinely absent input have the same effect —
/// excluded, not counted as a zero score — so a config that doesn't care about Sonar, and a
/// project that's never run Sonar, produce the same (correct) result: Sonar doesn't drag the
/// overall score down just because there's nothing to say about it.
/// </summary>
public sealed class WeightedSumScoringProvider(
    IExecutionRepository executionRepository,
    IEngineeringMetricsRepository metricsRepository,
    IHumanEvaluationRepository humanEvaluationRepository,
    IAIEvaluationRepository aiEvaluationRepository) : IScoringProvider
{
    public async Task<PromptScore> ComputeAsync(Guid promptVersionId, ScoringConfig config, CancellationToken cancellationToken = default)
    {
        var executions = await executionRepository.GetByPromptVersionIdAsync(promptVersionId, cancellationToken);
        var finishedExecutionIds = executions
            .Where(e => e.Status == ExecutionStatus.Finished)
            .Select(e => e.Id)
            .ToList();

        var allMetrics = new List<EngineeringMetrics>();
        var humanRatings = new List<double>();
        var acSatisfaction = new List<double>();

        foreach (var executionId in finishedExecutionIds)
        {
            allMetrics.AddRange(await metricsRepository.GetByExecutionIdAsync(executionId, cancellationToken));

            var humanEvaluations = await humanEvaluationRepository.GetByExecutionIdAsync(executionId, cancellationToken);
            humanRatings.AddRange(humanEvaluations.Select(e => NormalizeFiveScale(e.OverallSatisfaction)));

            var aiEvaluations = await aiEvaluationRepository.GetByExecutionIdAsync(executionId, cancellationToken);
            acSatisfaction.AddRange(aiEvaluations
                .Where(e => e.SatisfiesAcceptanceCriteria.HasValue)
                .Select(e => e.SatisfiesAcceptanceCriteria!.Value ? 100.0 : 0.0));
        }

        var componentScores = new Dictionary<string, double>();
        AddIfAny(componentScores, "humanRating", humanRatings);
        AddIfAny(componentScores, "acceptanceCriteria", acSatisfaction);
        AddIfAny(componentScores, "sonar", allMetrics.Where(m => m.CollectedBy == "sonar" && m.Coverage.HasValue).Select(m => m.Coverage!.Value));
        AddIfAny(componentScores, "tests", allMetrics.Where(m => m.TestSuccess.HasValue).Select(m => m.TestSuccess!.Value ? 100.0 : 0.0));
        AddIfAny(componentScores, "build", allMetrics.Where(m => m.BuildSuccess.HasValue).Select(m => m.BuildSuccess!.Value ? 100.0 : 0.0));
        AddIfAny(componentScores, "manualFixes", allMetrics.Where(m => m.ManualEdits.HasValue).Select(m => DecayByCount(m.ManualEdits!.Value, pointsPerUnit: 10)));
        AddIfAny(componentScores, "reviewComments", allMetrics.Where(m => m.ReviewComments.HasValue).Select(m => DecayByCount(m.ReviewComments!.Value, pointsPerUnit: 5)));
        AddIfAny(componentScores, "regressionBugs", allMetrics.Where(m => m.RollbackNeeded.HasValue).Select(m => m.RollbackNeeded!.Value ? 0.0 : 100.0));

        var weightsByComponent = new (string Name, double Weight)[]
        {
            ("humanRating", config.Weights.HumanRating),
            ("sonar", config.Weights.Sonar),
            ("tests", config.Weights.Tests),
            ("build", config.Weights.Build),
            ("acceptanceCriteria", config.Weights.AcceptanceCriteria),
            ("manualFixes", config.Weights.ManualFixes),
            ("reviewComments", config.Weights.ReviewComments),
            ("regressionBugs", config.Weights.RegressionBugs)
        };

        double weightedSum = 0;
        double totalWeightUsed = 0;
        foreach (var (name, weight) in weightsByComponent)
        {
            if (weight <= 0) continue; // zero (or omitted-defaulting-to-zero) weight — excluded entirely
            if (!componentScores.TryGetValue(name, out var score)) continue; // no data for this component — excluded, not scored as 0

            weightedSum += score * weight;
            totalWeightUsed += weight;
        }

        // No component with both a positive weight and data — nothing to score yet.
        var overallScore = totalWeightUsed > 0 ? weightedSum / totalWeightUsed : 0.0;

        return PromptScore.Compute(promptVersionId, config.Id, overallScore, componentScores, finishedExecutionIds.Count);
    }

    private static void AddIfAny(Dictionary<string, double> componentScores, string name, IEnumerable<double> values)
    {
        var list = values as ICollection<double> ?? values.ToList();
        if (list.Count > 0)
            componentScores[name] = list.Average();
    }

    private static double NormalizeFiveScale(int rating) => (rating - 1) / 4.0 * 100.0;

    /// <summary>
    /// Fewer is better for counts like manual edits or review comments — each unit costs
    /// <paramref name="pointsPerUnit"/> points, floored at 0. A simple, clearly-provisional shape
    /// (docs/scoring.md) pending real calibration once these fields have real data (Phase 5's
    /// documented gap — nothing populates <c>ManualEdits</c>/<c>ReviewComments</c> yet).
    /// </summary>
    private static double DecayByCount(int count, double pointsPerUnit) => Math.Max(0, 100.0 - (count * pointsPerUnit));
}
