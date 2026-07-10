namespace PromptOps.Application.Evaluations;

/// <summary>
/// The JSON schema an AI judge is asked to respond with. Deserialized with
/// <c>JsonSerializerDefaults.Web</c> (camelCase, case-insensitive), so property names here match
/// the schema in <see cref="JudgePromptBuilder"/>'s prompt without explicit attributes.
/// All fields nullable/optional — a judge that omits an array field means "nothing to report"
/// there, not a parse failure (see <see cref="JudgeResponseParser"/>'s remarks on resilience).
/// </summary>
public sealed class JudgeResponseDto
{
    public bool? SatisfiesAcceptanceCriteria { get; set; }
    public List<string>? AdrViolations { get; set; }
    public List<string>? IgnoredRequirements { get; set; }
    public string? UnnecessaryComplexityNotes { get; set; }
    public List<string>? SuggestedPromptImprovements { get; set; }
}
