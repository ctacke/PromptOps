using PromptOps.Domain.Executions;

namespace PromptOps.Application.Evaluations;

/// <summary>
/// Builds the judge prompt an AI judge is asked to answer, and the follow-up correction prompt
/// when a response doesn't parse. Extracted from the original <c>AIJudgeEvaluationProvider</c>
/// (Phase 7) so both the autonomous path (<c>IAIExecutionProvider</c> answers it directly) and
/// the client-delegated path (ADR-0010/Phase 12 — the prompt is handed back to whatever MCP
/// client is in the conversation to answer itself) build against one identical prompt/schema.
/// </summary>
public static class JudgePromptBuilder
{
    /// <summary>Retry budget shared by both the autonomous and delegated judge flows.</summary>
    public const int MaxAttempts = 3;

    public static string Build(ExecutionRecord execution)
    {
        var acceptanceCriteria = execution.Context.AcceptanceCriteria.Count > 0
            ? string.Join("\n", execution.Context.AcceptanceCriteria.Select(ac => $"- {ac}"))
            : "(none given)";
        var adrs = execution.Context.ReferencedADRs.Count > 0
            ? string.Join(", ", execution.Context.ReferencedADRs)
            : "(none given)";

        var header = $"""
            You are judging whether an AI coding assistant's output satisfied its task. Be specific and honest; don't rubber-stamp.

            Repository: {execution.Context.Repository}
            Acceptance criteria:
            {acceptanceCriteria}
            Referenced ADRs: {adrs}

            --- Output to evaluate ---
            {execution.Output}
            --- end output ---

            Respond with ONLY a JSON object matching this exact schema, no other text, no markdown fences:
            """;

        // Not an interpolated literal (no leading $) — the braces below are plain JSON, not
        // interpolation holes, so they need no escaping.
        const string schema = """
            {
              "satisfiesAcceptanceCriteria": true | false | null,
              "adrViolations": ["..."],
              "ignoredRequirements": ["..."],
              "unnecessaryComplexityNotes": "..." | null,
              "suggestedPromptImprovements": ["..."]
            }
            """;

        return $"{header}\n{schema}";
    }

    public static string AppendCorrection(string prompt, string invalidResponse, Exception? parseError) => $"""
        {prompt}

        Your previous response could not be parsed as the required JSON schema (error: {parseError?.Message ?? "unknown"}). Your previous response was:
        {invalidResponse}

        Respond again with ONLY a valid JSON object matching the schema above — no markdown fences, no extra text.
        """;
}
