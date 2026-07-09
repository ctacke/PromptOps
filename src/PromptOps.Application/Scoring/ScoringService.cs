using PromptOps.Application.Events;
using PromptOps.Application.Providers;
using PromptOps.Domain.Scoring;

namespace PromptOps.Application.Scoring;

/// <summary>Application-layer use cases for the Scoring Engine (Phase 8) — config management and both recompute paths (on-demand + the debounced scheduler's target).</summary>
public sealed class ScoringService(
    IScoringProvider scoringProvider,
    IScoringConfigRepository configRepository,
    IPromptScoreRepository scoreRepository,
    IDomainEventPublisher eventPublisher)
{
    public const string DefaultConfigName = "default";

    /// <summary>
    /// A starting point, not an empirically-tuned formula (docs/scoring.md) — human rating and
    /// AC-satisfaction weighted heaviest as the most direct "did this work" signals available
    /// today; manualFixes/reviewComments/regressionBugs weighted lightly since nothing populates
    /// their underlying <c>EngineeringMetrics</c> fields yet (Phase 5's documented gap).
    /// </summary>
    private static readonly ScoringWeights DefaultWeights = new()
    {
        HumanRating = 0.30,
        AcceptanceCriteria = 0.20,
        Sonar = 0.15,
        Tests = 0.15,
        Build = 0.10,
        ManualFixes = 0.05,
        ReviewComments = 0.025,
        RegressionBugs = 0.025
    };

    public async Task<ScoringConfig> CreateConfigAsync(string name, ScoringWeights weights, CancellationToken cancellationToken = default)
    {
        var latest = await configRepository.GetLatestByNameAsync(name, cancellationToken);
        var nextVersion = (latest?.Version ?? 0) + 1;

        var config = ScoringConfig.Create(name, nextVersion, weights);
        await configRepository.AddAsync(config, cancellationToken);
        await configRepository.SaveChangesAsync(cancellationToken);
        return config;
    }

    public Task<IReadOnlyList<ScoringConfig>> GetConfigVersionsAsync(string name, CancellationToken cancellationToken = default)
        => configRepository.GetAllByNameAsync(name, cancellationToken);

    /// <summary>
    /// Recomputes and persists a new <see cref="PromptScore"/> for <paramref name="promptVersionId"/>.
    /// Called both by the on-demand recompute endpoint and by <c>IScoreRecomputeScheduler</c> once
    /// its debounce window elapses — same code path either way, just a different trigger.
    /// </summary>
    public async Task<PromptScore> RecomputeAsync(
        Guid promptVersionId,
        Guid? scoringConfigId = null,
        string? scoringConfigName = null,
        CancellationToken cancellationToken = default)
    {
        var config = await ResolveConfigAsync(scoringConfigId, scoringConfigName, cancellationToken);
        var score = await scoringProvider.ComputeAsync(promptVersionId, config, cancellationToken);

        await scoreRepository.AddAsync(score, cancellationToken);
        await scoreRepository.SaveChangesAsync(cancellationToken);

        foreach (var domainEvent in score.DomainEvents.ToList())
        {
            await eventPublisher.PublishAsync(domainEvent, cancellationToken);
        }
        score.ClearDomainEvents();

        return score;
    }

    public Task<IReadOnlyList<PromptScore>> GetScoresAsync(Guid promptVersionId, CancellationToken cancellationToken = default)
        => scoreRepository.GetByPromptVersionIdAsync(promptVersionId, cancellationToken);

    private async Task<ScoringConfig> ResolveConfigAsync(Guid? scoringConfigId, string? scoringConfigName, CancellationToken cancellationToken)
    {
        if (scoringConfigId is { } id)
        {
            return await configRepository.GetByIdAsync(id, cancellationToken)
                ?? throw new ScoringConfigNotFoundException(id);
        }

        var name = string.IsNullOrWhiteSpace(scoringConfigName) ? DefaultConfigName : scoringConfigName;
        var existing = await configRepository.GetLatestByNameAsync(name, cancellationToken);
        if (existing is not null)
            return existing;

        // Lazy-bootstrap: the first-ever recompute request for a never-configured name creates v1
        // with sensible defaults, so a fresh daemon works without a manual setup step.
        var created = ScoringConfig.Create(name, 1, DefaultWeights);
        await configRepository.AddAsync(created, cancellationToken);
        await configRepository.SaveChangesAsync(cancellationToken);
        return created;
    }
}
