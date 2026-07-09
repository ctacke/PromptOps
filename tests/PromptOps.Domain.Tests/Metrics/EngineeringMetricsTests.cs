using PromptOps.Domain.Metrics;
using Xunit;

namespace PromptOps.Domain.Tests.Metrics;

public class EngineeringMetricsTests
{
    [Fact]
    public void Record_Rejects_Empty_ExecutionId()
    {
        Assert.Throws<ArgumentException>(() => EngineeringMetrics.Record(Guid.Empty, "sonar"));
    }

    [Fact]
    public void Record_Rejects_Empty_CollectedBy()
    {
        Assert.Throws<ArgumentException>(() => EngineeringMetrics.Record(Guid.NewGuid(), " "));
    }

    [Fact]
    public void Record_Sets_Fields_And_Defaults_CollectedAt_To_Now()
    {
        var executionId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;

        var metrics = EngineeringMetrics.Record(executionId, "sonar", coverage: 87.5, codeSmells: 12);

        Assert.Equal(executionId, metrics.ExecutionId);
        Assert.Equal("sonar", metrics.CollectedBy);
        Assert.Equal(87.5, metrics.Coverage);
        Assert.Equal(12, metrics.CodeSmells);
        Assert.Null(metrics.BuildSuccess);
        Assert.InRange(metrics.CollectedAt, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Record_Raises_MetricsCollected()
    {
        var executionId = Guid.NewGuid();

        var metrics = EngineeringMetrics.Record(executionId, "build-result", buildSuccess: true);

        var domainEvent = Assert.Single(metrics.DomainEvents);
        var collected = Assert.IsType<MetricsCollected>(domainEvent);
        Assert.Equal(metrics.Id, collected.MetricsId);
        Assert.Equal(executionId, collected.ExecutionId);
        Assert.Equal("build-result", collected.CollectedBy);
    }

    [Fact]
    public void Rehydrate_Does_Not_Raise_A_Domain_Event()
    {
        var metrics = EngineeringMetrics.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), "sonar", DateTimeOffset.UtcNow,
            buildSuccess: null, testSuccess: null, coverage: 90.0, sonarIssues: 3, warnings: null,
            codeSmells: 2, securityFindings: 0, duplication: 1.5, cyclomaticComplexity: 4.2,
            reviewComments: null, reviewIterations: null, mergeTimeMinutes: null,
            rollbackNeeded: null, manualEdits: null);

        Assert.Empty(metrics.DomainEvents);
    }
}
