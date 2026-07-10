using PromptOps.Application.Evaluations;

namespace PromptOps.Infrastructure.Tests;

public class JudgeResponseParserTests
{
    [Fact]
    public void TryParse_succeeds_on_a_well_formed_response()
    {
        var success = JudgeResponseParser.TryParse(
            """{"satisfiesAcceptanceCriteria":true,"adrViolations":[],"ignoredRequirements":[],"unnecessaryComplexityNotes":null,"suggestedPromptImprovements":["be more specific"]}""",
            out var parsed, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.True(parsed!.SatisfiesAcceptanceCriteria);
        Assert.Equal(["be more specific"], parsed.SuggestedPromptImprovements);
    }

    [Fact]
    public void TryParse_tolerates_markdown_fences_and_surrounding_prose()
    {
        var success = JudgeResponseParser.TryParse(
            """
            Here's my assessment:
            ```json
            {"satisfiesAcceptanceCriteria": false, "adrViolations": ["ADR-0002"]}
            ```
            Let me know if you need more detail.
            """,
            out var parsed, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.False(parsed!.SatisfiesAcceptanceCriteria);
        Assert.Equal(["ADR-0002"], parsed.AdrViolations);
    }

    [Fact]
    public void TryParse_tolerates_missing_optional_fields()
    {
        var success = JudgeResponseParser.TryParse("""{"satisfiesAcceptanceCriteria": true}""", out var parsed, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Null(parsed!.AdrViolations);
    }

    [Fact]
    public void TryParse_fails_when_no_json_object_is_present()
    {
        var success = JudgeResponseParser.TryParse("not json, no matter how many times you ask", out var parsed, out var error);

        Assert.False(success);
        Assert.Null(parsed);
        Assert.IsType<FormatException>(error);
    }
}
