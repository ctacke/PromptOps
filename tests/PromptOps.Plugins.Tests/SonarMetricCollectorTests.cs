using System.Net;
using Microsoft.Extensions.Options;
using PromptOps.Domain.Executions;
using PromptOps.Plugins.Sonar;
using PromptOps.Plugins.Tests.Fakes;

namespace PromptOps.Plugins.Tests;

public class SonarMetricCollectorTests
{
    private const string SonarMeasuresJson = """
        {
          "component": {
            "key": "my-project",
            "measures": [
              { "metric": "violations", "value": "7" },
              { "metric": "vulnerabilities", "value": "1" },
              { "metric": "code_smells", "value": "5" },
              { "metric": "coverage", "value": "87.5" },
              { "metric": "duplicated_lines_density", "value": "2.3" },
              { "metric": "complexity", "value": "42" }
            ]
          }
        }
        """;

    private static ExecutionRecord SeededExecution(string repository = "my-project")
        => ExecutionRecord.Start(Guid.NewGuid(), "alice", new DevelopmentContext { Repository = repository });

    private static SonarMetricCollector CollectorWith(HttpMessageHandler handler, FakeExecutionRepository repository, string? baseUrl = "http://fake-sonar", string? token = null)
        => new(new HttpClient(handler), repository, new FakeSecretProvider(token), Options.Create(new SonarOptions { BaseUrl = baseUrl }));

    [Fact]
    public async Task Returns_Null_When_BaseUrl_Not_Configured()
    {
        var repository = new FakeExecutionRepository();
        var execution = SeededExecution();
        repository.Seed(execution);
        var collector = CollectorWith(StubHttpMessageHandler.ReturningJson(HttpStatusCode.OK, SonarMeasuresJson), repository, baseUrl: null);

        var result = await collector.CollectAsync(execution.Id, new Dictionary<string, string>());

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_Null_When_Execution_Not_Found()
    {
        var repository = new FakeExecutionRepository();
        var collector = CollectorWith(StubHttpMessageHandler.ReturningJson(HttpStatusCode.OK, SonarMeasuresJson), repository);

        var result = await collector.CollectAsync(Guid.NewGuid(), new Dictionary<string, string>());

        Assert.Null(result);
    }

    [Fact]
    public async Task Maps_Sonar_Measures_Into_EngineeringMetrics()
    {
        var repository = new FakeExecutionRepository();
        var execution = SeededExecution();
        repository.Seed(execution);
        var collector = CollectorWith(StubHttpMessageHandler.ReturningJson(HttpStatusCode.OK, SonarMeasuresJson), repository);

        var result = await collector.CollectAsync(execution.Id, new Dictionary<string, string>());

        Assert.NotNull(result);
        Assert.Equal(execution.Id, result!.ExecutionId);
        Assert.Equal("sonar", result.CollectedBy);
        Assert.Equal(7, result.SonarIssues);
        Assert.Equal(1, result.SecurityFindings);
        Assert.Equal(5, result.CodeSmells);
        Assert.Equal(87.5, result.Coverage);
        Assert.Equal(2.3, result.Duplication);
        Assert.Equal(42, result.CyclomaticComplexity);
    }

    [Fact]
    public async Task Uses_ProjectKey_Override_From_Parameters_In_The_Request()
    {
        var repository = new FakeExecutionRepository();
        var execution = SeededExecution(repository: "default-repo");
        repository.Seed(execution);
        var handler = StubHttpMessageHandler.ReturningJson(HttpStatusCode.OK, SonarMeasuresJson);
        var collector = CollectorWith(handler, repository);

        await collector.CollectAsync(execution.Id, new Dictionary<string, string> { ["projectKey"] = "explicit-key" });

        Assert.Contains("component=explicit-key", handler.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task Returns_Null_When_Sonar_Is_Unreachable()
    {
        var repository = new FakeExecutionRepository();
        var execution = SeededExecution();
        repository.Seed(execution);
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var collector = CollectorWith(handler, repository);

        var result = await collector.CollectAsync(execution.Id, new Dictionary<string, string>());

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_Null_On_NonSuccess_StatusCode()
    {
        var repository = new FakeExecutionRepository();
        var execution = SeededExecution();
        repository.Seed(execution);
        var collector = CollectorWith(StubHttpMessageHandler.ReturningJson(HttpStatusCode.NotFound, "{}"), repository);

        var result = await collector.CollectAsync(execution.Id, new Dictionary<string, string>());

        Assert.Null(result);
    }
}
