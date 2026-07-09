using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using PromptOps.Application.Executions;
using PromptOps.Application.Providers;
using PromptOps.Domain.Metrics;

namespace PromptOps.Plugins.Sonar;

/// <summary>
/// Queries SonarQube/SonarCloud's measures Web API for the project mapped to an execution's
/// repository and turns the response into an <see cref="EngineeringMetrics"/> row. A network-
/// reaching collector (ADR-0003) — unlike <c>BuildResultCollector</c>, it doesn't need anything
/// pushed to it beyond an optional <c>projectKey</c> override in <c>parameters</c>.
/// </summary>
public sealed class SonarMetricCollector(
    HttpClient httpClient,
    IExecutionRepository executionRepository,
    ISecretProvider secretProvider,
    IOptions<SonarOptions> options) : IMetricCollector
{
    private const string MetricKeys = "violations,vulnerabilities,code_smells,coverage,duplicated_lines_density,complexity";

    public string Name => "sonar";

    public async Task<EngineeringMetrics?> CollectAsync(
        Guid executionId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = options.Value.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null; // not configured for this daemon — nothing to do, not an error

        var execution = await executionRepository.GetByIdAsync(executionId, cancellationToken);
        if (execution is null)
            return null;

        var projectKey = parameters.GetValueOrDefault("projectKey");
        if (string.IsNullOrWhiteSpace(projectKey))
            projectKey = execution.Context.Repository;
        if (string.IsNullOrWhiteSpace(projectKey))
            return null;

        var requestUri = $"{baseUrl.TrimEnd('/')}/api/measures/component?component={Uri.EscapeDataString(projectKey)}&metricKeys={MetricKeys}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        var token = await secretProvider.GetSecretAsync("sonar", "token", cancellationToken);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null; // Sonar unreachable this run — nothing collected, caller isn't blocked on it
        }

        if (!response.IsSuccessStatusCode)
            return null;

        var payload = await response.Content.ReadFromJsonAsync<SonarMeasuresResponse>(cancellationToken: cancellationToken);
        var measures = payload?.Component?.Measures;
        if (measures is null || measures.Count == 0)
            return null;

        double? Measure(string metricKey) =>
            measures.FirstOrDefault(m => m.Metric == metricKey)?.Value is { } raw
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;

        return EngineeringMetrics.Record(
            executionId,
            collectedBy: Name,
            sonarIssues: (int?)Measure("violations"),
            securityFindings: (int?)Measure("vulnerabilities"),
            codeSmells: (int?)Measure("code_smells"),
            coverage: Measure("coverage"),
            duplication: Measure("duplicated_lines_density"),
            cyclomaticComplexity: Measure("complexity"));
    }
}
