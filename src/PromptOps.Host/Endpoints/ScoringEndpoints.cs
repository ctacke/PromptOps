using PromptOps.Application.Scoring;
using PromptOps.Domain.Scoring;

namespace PromptOps.Host.Endpoints;

/// <summary>
/// Scoring engine (Phase 8): config management plus both recompute paths — on-demand here, the
/// debounced event-driven path runs entirely inside the daemon (<c>ScoreRecomputeTrigger</c>) with
/// no HTTP surface of its own.
/// </summary>
public static class ScoringEndpoints
{
    public static void MapScoringEndpoints(this WebApplication app)
    {
        app.MapPost("/scoring-configs", async (CreateScoringConfigRequest request, ScoringService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var config = await service.CreateConfigAsync(request.Name, request.Weights.ToDomain(), cancellationToken);
                return Results.Ok(ScoringConfigResponse.From(config));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/scoring-configs", async (string name, ScoringService service, CancellationToken cancellationToken) =>
        {
            var configs = await service.GetConfigVersionsAsync(name, cancellationToken);
            return Results.Ok(configs.Select(ScoringConfigResponse.From));
        });

        var scoresGroup = app.MapGroup("/prompts/{promptVersionId:guid}/scores");

        scoresGroup.MapPost("/", async (Guid promptVersionId, RecomputeScoreRequest? request, ScoringService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var score = await service.RecomputeAsync(promptVersionId, request?.ScoringConfigId, request?.ScoringConfigName, cancellationToken);
                return Results.Ok(PromptScoreResponse.From(score));
            }
            catch (ScoringConfigNotFoundException)
            {
                return Results.NotFound();
            }
        });

        scoresGroup.MapGet("/", async (Guid promptVersionId, ScoringService service, CancellationToken cancellationToken) =>
        {
            var scores = await service.GetScoresAsync(promptVersionId, cancellationToken);
            return Results.Ok(scores.Select(PromptScoreResponse.From));
        });
    }
}

internal sealed record ScoringWeightsDto(
    double HumanRating,
    double Sonar,
    double Tests,
    double Build,
    double AcceptanceCriteria,
    double ManualFixes,
    double ReviewComments,
    double RegressionBugs)
{
    public ScoringWeights ToDomain() => new()
    {
        HumanRating = HumanRating,
        Sonar = Sonar,
        Tests = Tests,
        Build = Build,
        AcceptanceCriteria = AcceptanceCriteria,
        ManualFixes = ManualFixes,
        ReviewComments = ReviewComments,
        RegressionBugs = RegressionBugs
    };

    public static ScoringWeightsDto From(ScoringWeights weights) => new(
        weights.HumanRating, weights.Sonar, weights.Tests, weights.Build,
        weights.AcceptanceCriteria, weights.ManualFixes, weights.ReviewComments, weights.RegressionBugs);
}

internal sealed record CreateScoringConfigRequest(string Name, ScoringWeightsDto Weights);

internal sealed record RecomputeScoreRequest(Guid? ScoringConfigId, string? ScoringConfigName);

internal sealed record ScoringConfigResponse(Guid Id, string Name, int Version, ScoringWeightsDto Weights, DateTimeOffset CreatedAt)
{
    public static ScoringConfigResponse From(ScoringConfig config) => new(
        config.Id, config.Name, config.Version, ScoringWeightsDto.From(config.Weights), config.CreatedAt);
}

internal sealed record PromptScoreResponse(
    Guid Id,
    Guid PromptVersionId,
    Guid ScoringConfigId,
    DateTimeOffset ComputedAt,
    double OverallScore,
    IReadOnlyDictionary<string, double> ComponentScores,
    int SampleSize)
{
    public static PromptScoreResponse From(PromptScore score) => new(
        score.Id, score.PromptVersionId, score.ScoringConfigId, score.ComputedAt,
        score.OverallScore, score.ComponentScores, score.SampleSize);
}
