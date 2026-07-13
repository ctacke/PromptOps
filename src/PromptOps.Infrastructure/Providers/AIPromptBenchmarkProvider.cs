using System.Text.Json;
using PromptOps.Application.Evaluations;
using PromptOps.Application.Providers;

namespace PromptOps.Infrastructure.Providers;

/// <summary>
/// Reference <see cref="IPromptBenchmarkProvider"/> (Phase 16b), built on
/// <see cref="IAIExecutionProvider"/> like every other AI-backed provider here. It:
/// <list type="number">
/// <item>generates <c>sampleSize</c> synthetic task scenarios the prompt would plausibly be applied to,</item>
/// <item>runs both the active and candidate prompt on each scenario, and</item>
/// <item>grades the two outputs together (0-100 each) so the scores are directly comparable.</item>
/// </list>
/// Returns the per-version averages, or <c>null</c> when nothing usable could be produced (e.g. the
/// no-op <c>ManualAIExecutionProvider</c> generates no scenarios) — the caller treats <c>null</c> as
/// inconclusive, never as a failed candidate. Parsing tolerates markdown fences/prose via
/// <see cref="JsonExtraction"/>, same as <see cref="AIActivityClassifier"/>.
/// </summary>
public sealed class AIPromptBenchmarkProvider(IAIExecutionProvider aiExecutionProvider) : IPromptBenchmarkProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly Dictionary<string, string> NoInputs = new();

    public async Task<BenchmarkComparison?> CompareAsync(
        string activeContent,
        string candidateContent,
        IReadOnlyList<string> tags,
        int sampleSize,
        CancellationToken cancellationToken = default)
    {
        if (sampleSize <= 0)
            return null;

        var scenarios = await GenerateScenariosAsync(activeContent, tags, sampleSize, cancellationToken);
        if (scenarios.Count == 0)
            return null;

        double activeTotal = 0, candidateTotal = 0;
        var graded = 0;

        foreach (var scenario in scenarios)
        {
            var activeOutput = await aiExecutionProvider.ExecuteAsync(Compose(activeContent, scenario), NoInputs, cancellationToken);
            var candidateOutput = await aiExecutionProvider.ExecuteAsync(Compose(candidateContent, scenario), NoInputs, cancellationToken);

            var grade = await GradeAsync(scenario, activeOutput, candidateOutput, cancellationToken);
            if (grade is null)
                continue;

            activeTotal += grade.Value.Active;
            candidateTotal += grade.Value.Candidate;
            graded++;
        }

        return graded == 0 ? null : new BenchmarkComparison(activeTotal / graded, candidateTotal / graded, graded);
    }

    private async Task<IReadOnlyList<string>> GenerateScenariosAsync(string promptContent, IReadOnlyList<string> tags, int sampleSize, CancellationToken cancellationToken)
    {
        var prompt = $"""
            Generate realistic task scenarios for benchmarking a reusable developer prompt.

            The prompt (tags: {string.Join(", ", tags)}) is:
            --- prompt ---
            {promptContent}
            --- end prompt ---

            Produce exactly {sampleSize} short, distinct, realistic task descriptions a developer might give that this prompt would be applied to. Respond with ONLY a JSON array of {sampleSize} strings — no other text, no markdown fences.
            """;

        var raw = await aiExecutionProvider.ExecuteAsync(prompt, NoInputs, cancellationToken);
        var json = JsonExtraction.ExtractJsonValue(raw);
        if (json is null)
            return [];

        try
        {
            var scenarios = JsonSerializer.Deserialize<List<string>>(json, SerializerOptions);
            return scenarios?.Where(s => !string.IsNullOrWhiteSpace(s)).Take(sampleSize).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<(double Active, double Candidate)?> GradeAsync(string scenario, string activeOutput, string candidateOutput, CancellationToken cancellationToken)
    {
        var prompt = $"""
            Score both outputs for how well each accomplishes the task, on a 0-100 scale (higher is better). Judge only quality — not length or style preferences.

            --- Task ---
            {scenario}
            --- Output A (active) ---
            {activeOutput}
            --- Output B (candidate) ---
            {candidateOutput}
            --- end ---

            Respond with ONLY a JSON object with two numeric keys, "activeScore" and "candidateScore", each from 0 to 100 — no other text, no markdown fences.
            """;

        var raw = await aiExecutionProvider.ExecuteAsync(prompt, NoInputs, cancellationToken);
        var json = JsonExtraction.ExtractJsonValue(raw);
        if (json is null)
            return null;

        try
        {
            var grade = JsonSerializer.Deserialize<GradeDto>(json, SerializerOptions);
            if (grade is null)
                return null;

            return (Clamp(grade.ActiveScore), Clamp(grade.CandidateScore));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Compose(string promptContent, string scenario) => $"""
        {promptContent}

        --- Task ---
        {scenario}
        """;

    private static double Clamp(double score) => Math.Clamp(score, 0, 100);

    private sealed record GradeDto(double ActiveScore, double CandidateScore);
}
