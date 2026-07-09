using PromptOps.Plugins.BuildResult;

namespace PromptOps.Plugins.Tests;

public class BuildResultCollectorTests
{
    private const string PassingTrx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <ResultSummary outcome="Completed">
            <Counters total="10" executed="10" passed="10" failed="0" />
          </ResultSummary>
        </TestRun>
        """;

    private const string FailingTrx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <ResultSummary outcome="Completed">
            <Counters total="10" executed="10" passed="8" failed="2" />
          </ResultSummary>
        </TestRun>
        """;

    private const string Cobertura = """
        <?xml version="1.0" encoding="UTF-8"?>
        <coverage line-rate="0.855" branch-rate="0.7" version="1.9">
        </coverage>
        """;

    [Fact]
    public async Task Returns_Null_When_No_Parameters_Given()
    {
        var collector = new BuildResultCollector();

        var result = await collector.CollectAsync(Guid.NewGuid(), new Dictionary<string, string>());

        Assert.Null(result);
    }

    [Fact]
    public async Task Parses_Passing_Trx_Into_BuildSuccess_And_TestSuccess()
    {
        var collector = new BuildResultCollector();
        var executionId = Guid.NewGuid();

        var result = await collector.CollectAsync(executionId, new Dictionary<string, string> { ["trx"] = PassingTrx });

        Assert.NotNull(result);
        Assert.Equal(executionId, result!.ExecutionId);
        Assert.Equal("build-result", result.CollectedBy);
        Assert.True(result.BuildSuccess);
        Assert.True(result.TestSuccess);
    }

    [Fact]
    public async Task Parses_Failing_Trx_Into_TestSuccess_False_But_BuildSuccess_True()
    {
        var collector = new BuildResultCollector();

        var result = await collector.CollectAsync(Guid.NewGuid(), new Dictionary<string, string> { ["trx"] = FailingTrx });

        Assert.NotNull(result);
        Assert.True(result!.BuildSuccess);
        Assert.False(result.TestSuccess);
    }

    [Fact]
    public async Task Parses_Cobertura_LineRate_As_A_Percentage()
    {
        var collector = new BuildResultCollector();

        var result = await collector.CollectAsync(Guid.NewGuid(), new Dictionary<string, string> { ["cobertura"] = Cobertura });

        Assert.NotNull(result);
        Assert.Equal(85.5, result!.Coverage);
        Assert.Null(result.BuildSuccess);
    }

    [Fact]
    public async Task Combines_Trx_And_Cobertura_When_Both_Given()
    {
        var collector = new BuildResultCollector();

        var result = await collector.CollectAsync(
            Guid.NewGuid(),
            new Dictionary<string, string> { ["trx"] = PassingTrx, ["cobertura"] = Cobertura });

        Assert.NotNull(result);
        Assert.True(result!.BuildSuccess);
        Assert.True(result.TestSuccess);
        Assert.Equal(85.5, result.Coverage);
    }

    [Fact]
    public async Task Ignores_Malformed_Trx_Without_Throwing()
    {
        var collector = new BuildResultCollector();

        var result = await collector.CollectAsync(Guid.NewGuid(), new Dictionary<string, string> { ["trx"] = "<not valid xml" });

        Assert.Null(result);
    }

    [Fact]
    public async Task Still_Reports_Cobertura_When_Trx_Is_Malformed()
    {
        var collector = new BuildResultCollector();

        var result = await collector.CollectAsync(
            Guid.NewGuid(),
            new Dictionary<string, string> { ["trx"] = "<not valid xml", ["cobertura"] = Cobertura });

        Assert.NotNull(result);
        Assert.Null(result!.BuildSuccess);
        Assert.Equal(85.5, result.Coverage);
    }
}
