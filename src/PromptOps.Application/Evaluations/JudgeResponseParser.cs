using System.Text.Json;

namespace PromptOps.Application.Evaluations;

/// <summary>
/// Parses a judge's raw response against <see cref="JudgeResponseDto"/>'s schema, tolerating
/// format drift (markdown fences, surrounding prose) via <see cref="JsonExtraction"/> rather than
/// requiring an exact match. Extracted from the original <c>AIJudgeEvaluationProvider</c> (Phase
/// 7) so the autonomous and client-delegated (ADR-0010/Phase 12) judge flows parse identically.
/// Missing optional fields (<c>adrViolations</c>, <c>ignoredRequirements</c>,
/// <c>suggestedPromptImprovements</c>) don't fail the parse — only malformed/unparseable JSON does.
/// </summary>
public static class JudgeResponseParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static bool TryParse(string raw, out JudgeResponseDto? parsed, out Exception? error)
    {
        parsed = null;
        error = null;

        var json = JsonExtraction.ExtractJsonValue(raw);
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
}
