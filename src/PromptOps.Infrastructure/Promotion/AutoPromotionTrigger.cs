using Microsoft.Extensions.Logging;
using PromptOps.Application.Events;
using PromptOps.Application.Promotion;
using PromptOps.Application.Prompts;
using PromptOps.Application.Scoring;
using PromptOps.Domain.Prompts;
using PromptOps.Domain.Scoring;

namespace PromptOps.Infrastructure.Promotion;

/// <summary>
/// Reacts to <see cref="ScoreComputed"/> (Phase 8) — the first-ever registered handler for that
/// event; nothing consumed it before Phase 11. When the policy's <c>AutoPromotionEnabled</c> is on
/// and the newly-computed score clears the configured absolute threshold or beats the currently
/// active version's most recent score by the configured margin (either condition alone is
/// sufficient — see docs/promotion-policy.md), this activates the scored version through the same
/// <see cref="Prompt.ActivateVersion"/> domain method the manual <c>POST /prompts/.../activate</c>
/// endpoint uses, so "exactly one Active version" is enforced identically either way.
/// </summary>
public sealed class AutoPromotionTrigger(
    IPromotionPolicyRepository policyRepository,
    IPromptRepository promptRepository,
    IPromptScoreRepository scoreRepository,
    ILogger<AutoPromotionTrigger> logger) : IDomainEventHandler<ScoreComputed>
{
    public async Task HandleAsync(ScoreComputed domainEvent, CancellationToken cancellationToken = default)
    {
        var policy = await policyRepository.GetAsync(cancellationToken);
        if (policy is not { AutoPromotionEnabled: true })
            return;

        var prompt = await promptRepository.GetByVersionIdAsync(domainEvent.PromptVersionId, cancellationToken);
        var version = prompt?.Versions.FirstOrDefault(v => v.Id == domainEvent.PromptVersionId);
        if (prompt is null || version is null || version.Status != PromptVersionStatus.Draft)
            return; // already active, or a deliberately deprecated version — never resurrect

        var activeVersion = prompt.Versions.FirstOrDefault(v => v.Status == PromptVersionStatus.Active);
        double? activeScore = null;
        if (activeVersion is not null)
        {
            var scores = await scoreRepository.GetByPromptVersionIdAsync(activeVersion.Id, cancellationToken);
            activeScore = scores.OrderByDescending(s => s.ComputedAt).FirstOrDefault()?.OverallScore;
        }

        var clearsThreshold = policy.MinimumScoreThreshold is { } threshold && domainEvent.OverallScore >= threshold;
        var clearsMargin = policy.MinimumMarginOverActive is { } margin && activeScore is { } baseline && domainEvent.OverallScore - baseline >= margin;
        if (!clearsThreshold && !clearsMargin)
            return;

        prompt.ActivateVersion(version.Id);
        await promptRepository.UpdateAsync(prompt, cancellationToken);
        await promptRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Auto-promoted PromptVersion {PromptVersionId} (prompt {PromptId}) — score {Score}, clears threshold: {ClearsThreshold}, clears margin: {ClearsMargin}.",
            version.Id, prompt.Id, domainEvent.OverallScore, clearsThreshold, clearsMargin);
    }
}
