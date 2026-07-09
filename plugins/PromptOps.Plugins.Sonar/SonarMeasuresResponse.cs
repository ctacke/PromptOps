using System.Text.Json.Serialization;

namespace PromptOps.Plugins.Sonar;

/// <summary>Shape of SonarQube/SonarCloud's <c>GET /api/measures/component</c> response — only the fields this collector reads.</summary>
internal sealed record SonarMeasuresResponse([property: JsonPropertyName("component")] SonarComponent? Component);

internal sealed record SonarComponent([property: JsonPropertyName("measures")] List<SonarMeasure>? Measures);

internal sealed record SonarMeasure(
    [property: JsonPropertyName("metric")] string? Metric,
    [property: JsonPropertyName("value")] string? Value);
