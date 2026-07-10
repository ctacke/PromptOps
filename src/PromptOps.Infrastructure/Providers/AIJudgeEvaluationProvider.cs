using PromptOps.Application.Evaluations;
using PromptOps.Application.Executions;
using PromptOps.Application.Providers;
using PromptOps.Domain.Evaluations;

namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Reference <see cref="IAIEvaluationProvider"/> (Phase 7): loads the execution, asks the
/// configured <see cref="IAIExecutionProvider"/> to answer a judge prompt built by
/// <see cref="JudgePromptBuilder"/>, and parses the response with <see cref="JudgeResponseParser"/>
/// — retrying with a correction up to <see cref="JudgePromptBuilder.MaxAttempts"/> times before
/// giving up. This is the *autonomous* judge path — the daemon owns the model call end to end.
/// The client-delegated path (ADR-0010/Phase 12, <see cref="DelegatedAIEvaluationService"/>) reuses
/// the same prompt-building/parsing logic but hands the prompt back to the calling MCP client to
/// answer instead of calling <see cref="IAIExecutionProvider"/> itself.
/// </summary>
public sealed class AIJudgeEvaluationProvider(
    IAIExecutionProvider aiExecutionProvider,
    IExecutionRepository executionRepository) : IAIEvaluationProvider
{
    public string Name => "ai-judge";

    public async Task<AIEvaluation> EvaluateAsync(
        Guid executionId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        var execution = await executionRepository.GetByIdAsync(executionId, cancellationToken)
            ?? throw new ExecutionNotFoundException(executionId);

        var prompt = JudgePromptBuilder.Build(execution);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= JudgePromptBuilder.MaxAttempts; attempt++)
        {
            var raw = await aiExecutionProvider.ExecuteAsync(prompt, parameters, cancellationToken);

            if (JudgeResponseParser.TryParse(raw, out var parsed, out var parseError))
            {
                return AIEvaluation.Record(
                    executionId,
                    judgeProviderId: aiExecutionProvider.Name,
                    judgeModel: null,
                    satisfiesAcceptanceCriteria: parsed!.SatisfiesAcceptanceCriteria,
                    adrViolations: parsed.AdrViolations ?? [],
                    ignoredRequirements: parsed.IgnoredRequirements ?? [],
                    unnecessaryComplexityNotes: parsed.UnnecessaryComplexityNotes,
                    suggestedPromptImprovements: parsed.SuggestedPromptImprovements ?? [],
                    rawResponse: raw);
            }

            lastError = parseError;
            prompt = JudgePromptBuilder.AppendCorrection(prompt, raw, parseError);
        }

        throw new AIJudgeResponseInvalidException(executionId, JudgePromptBuilder.MaxAttempts, lastError);
    }
}
