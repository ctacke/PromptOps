using System.Globalization;
using System.Xml.Linq;
using PromptOps.Application.Providers;
using PromptOps.Domain.Metrics;

namespace PromptOps.Plugins.BuildResult;

/// <summary>
/// Parses trx (.NET/VSTest) test results and Cobertura coverage XML pushed via the ingestion API
/// (ADR-0005 §9: the daemon has no filesystem access to CI artifacts any more than it does to a
/// repo's working directory — the caller pushes file *content*, not a path). Reads
/// <c>parameters["trx"]</c> and/or <c>parameters["cobertura"]</c>; either, both, or neither may be
/// present in a given call, since they typically arrive from different CI steps. Returns
/// <c>null</c> only when neither parses to anything — JUnit XML is a natural, structurally similar
/// follow-up (same "count total/failed testcases" shape) not built out this phase.
/// </summary>
public sealed class BuildResultCollector : IMetricCollector
{
    public string Name => "build-result";

    public Task<EngineeringMetrics?> CollectAsync(
        Guid executionId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        bool? buildSuccess = null;
        bool? testSuccess = null;
        double? coverage = null;

        if (parameters.GetValueOrDefault("trx") is { Length: > 0 } trx)
        {
            var (total, failed) = TryParseTrx(trx);
            if (total is not null)
            {
                // A trx file exists only because the build that produced it completed — its
                // presence is itself evidence the build succeeded, independent of whether the
                // tests it ran passed.
                buildSuccess = true;
                testSuccess = failed == 0 && total > 0;
            }
        }

        if (parameters.GetValueOrDefault("cobertura") is { Length: > 0 } cobertura)
        {
            coverage = TryParseCoberturaCoveragePercent(cobertura);
        }

        if (buildSuccess is null && testSuccess is null && coverage is null)
            return Task.FromResult<EngineeringMetrics?>(null);

        return Task.FromResult<EngineeringMetrics?>(EngineeringMetrics.Record(
            executionId,
            collectedBy: Name,
            buildSuccess: buildSuccess,
            testSuccess: testSuccess,
            coverage: coverage));
    }

    /// <summary>Returns (total, failed) parsed from a trx's &lt;Counters&gt; element, or (null, null) if unparseable.</summary>
    private static (int? Total, int? Failed) TryParseTrx(string trxXml)
    {
        try
        {
            var document = XDocument.Parse(trxXml);
            var counters = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Counters");
            if (counters is null)
                return (null, null);

            var total = (int?)counters.Attribute("total");
            var failed = (int?)counters.Attribute("failed");
            return total is null ? (null, null) : (total, failed ?? 0);
        }
        catch (System.Xml.XmlException)
        {
            return (null, null);
        }
    }

    /// <summary>Returns Cobertura's root &lt;coverage line-rate="0.0-1.0"&gt; as a 0-100 percentage, or null if unparseable.</summary>
    private static double? TryParseCoberturaCoveragePercent(string coberturaXml)
    {
        try
        {
            var document = XDocument.Parse(coberturaXml);
            var root = document.Root;
            if (root is null || root.Name.LocalName != "coverage")
                return null;

            var lineRate = (double?)root.Attribute("line-rate");
            return lineRate.HasValue ? Math.Round(lineRate.Value * 100, 2) : null;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}
