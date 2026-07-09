namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Shared by every provider that asks an AI backend for a JSON response and needs to tolerate
/// format drift — markdown fences, a sentence of prose before/after — rather than requiring an
/// exact match (<see cref="AIJudgeEvaluationProvider"/> introduced this in Phase 7;
/// <c>AIActivityClassifier</c> reuses it in Phase 9 for the same reason, just extracting an array
/// instead of an object).
/// </summary>
internal static class JsonExtraction
{
    /// <summary>
    /// Finds the first balanced JSON object (<c>{...}</c>) or array (<c>[...]</c>) substring —
    /// whichever opening character appears first — respecting quoted strings so a brace or
    /// bracket inside a string value doesn't throw off the depth count.
    /// </summary>
    public static string? ExtractJsonValue(string raw)
    {
        var start = -1;
        var open = '\0';
        var close = '\0';

        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '{' || raw[i] == '[')
            {
                start = i;
                open = raw[i];
                close = open == '{' ? '}' : ']';
                break;
            }
        }

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
            else if (c == open) depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0) return raw[start..(i + 1)];
            }
        }

        return null;
    }
}
