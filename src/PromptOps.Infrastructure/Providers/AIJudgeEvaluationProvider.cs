using System.Text.Json;
using PromptOps.Application.Evaluations;
using PromptOps.Application.Executions;
using PromptOps.Application.Providers;
using PromptOps.Domain.Evaluations;
using PromptOps.Domain.Executions;

namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Reference <see cref="IAIEvaluationProvider"/> (Phase 7): builds a judge prompt from an
/// execution's AC/ADR references and output, asks the configured <see cref="IAIExecutionProvider"/>
/// to judge it, and parses the response against <see cref="JudgeResponseDto"/>'s schema.
///
/// Resilience (the phase's explicit design requirement) is schema validation + retry, not brittle
/// string matching: <see cref="ExtractJsonObject"/> tolerates markdown fences or a sentence of
/// prose around the JSON rather than requiring an exact match, and a response that still doesn't
/// parse gets one more attempt — with the invalid response and the parse error fed back into the
/// prompt as a correction — up to <see cref="MaxAttempts"/> times before giving up.
/// </summary>
public sealed class AIJudgeEvaluationProvider(
    IAIExecutionProvider aiExecutionProvider,
    IExecutionRepository executionRepository) : IAIEvaluationProvider
{
    private const int MaxAttempts = 3;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string Name => "ai-judge";

    public async Task<AIEvaluation> EvaluateAsync(
        Guid executionId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        var execution = await executionRepository.GetByIdAsync(executionId, cancellationToken)
            ?? throw new ExecutionNotFoundException(executionId);

        var prompt = BuildJudgePrompt(execution);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var raw = await aiExecutionProvider.ExecuteAsync(prompt, parameters, cancellationToken);

            if (TryParseJudgeResponse(raw, out var parsed, out var parseError))
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
            prompt = AppendCorrection(prompt, raw, parseError);
        }

        throw new AIJudgeResponseInvalidException(executionId, MaxAttempts, lastError);
    }

    private static string BuildJudgePrompt(ExecutionRecord execution)
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

    private static string AppendCorrection(string prompt, string invalidResponse, Exception? parseError) => $"""
        {prompt}

        Your previous response could not be parsed as the required JSON schema (error: {parseError?.Message ?? "unknown"}). Your previous response was:
        {invalidResponse}

        Respond again with ONLY a valid JSON object matching the schema above — no markdown fences, no extra text.
        """;

    private static bool TryParseJudgeResponse(string raw, out JudgeResponseDto? parsed, out Exception? error)
    {
        parsed = null;
        error = null;

        var json = ExtractJsonObject(raw);
        if (json is null)
        {
            error = new FormatException("No JSON object found in the response.");
            return false;
        }

        try
        {
            parsed = JsonSerializer.Deserialize<JudgeResponseDto>(json, SerializerOptions);
            if (parsed is null)
            {
                error = new FormatException("Response deserialized to null.");
                return false;
            }
            return true;
        }
        catch (JsonException ex)
        {
            error = ex;
            return false;
        }
    }

    /// <summary>
    /// Finds the first balanced <c>{...}</c> substring, respecting quoted strings (so a brace
    /// inside a string value doesn't throw off the depth count) — tolerant of judges that wrap
    /// JSON in markdown fences or add a sentence of prose before/after.
    /// </summary>
    private static string? ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        if (start < 0) return null;

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < raw.Length; i++)
        {
            var c = raw[i];

            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return raw[start..(i + 1)];
            }
        }

        return null;
    }
}
