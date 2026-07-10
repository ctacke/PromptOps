namespace PromptOps.Application.Evaluations;

/// <summary>A judge prompt returned by <see cref="DelegatedAIEvaluationService.PrepareAsync"/> for the calling MCP client to answer itself.</summary>
public sealed record DelegatedJudgePrompt(Guid CorrelationId, string Prompt);
