using PromptOps.Application.Providers;
using PromptOps.Infrastructure.Providers;
using Xunit;

namespace PromptOps.Infrastructure.Tests;

/// <summary>Pure unit tests — no SQLite, same shape as AIJudgeEvaluationProviderTests. Canned task descriptions and expected tag categories, matching Phase 7's classifier testing pattern.</summary>
public class AIActivityClassifierTests
{
    [Fact]
    public async Task Classifies_A_Stack_Trace_Description_Into_Debugging_Flavored_Tags()
    {
        var stub = new QueuedAIExecutionProvider("""["debugging", "csharp"]""");
        var classifier = new AIActivityClassifier(stub);

        var tags = await classifier.ClassifyAsync(
            "Getting a NullReferenceException at LoginService.cs:42, stack trace points into the auth middleware",
            new Dictionary<string, string>());

        Assert.Equal(1, stub.CallCount);
        Assert.Contains("debugging", tags);
        Assert.Contains("csharp", tags);
    }

    [Fact]
    public async Task Tolerates_Markdown_Fences_And_Surrounding_Prose()
    {
        var stub = new QueuedAIExecutionProvider("""
            Based on the description, here are the tags:
            ```json
            ["testing", "code-authoring"]
            ```
            """);
        var classifier = new AIActivityClassifier(stub);

        var tags = await classifier.ClassifyAsync("write unit tests for the new endpoint", new Dictionary<string, string>());

        Assert.Equal(["testing", "code-authoring"], tags);
    }

    [Fact]
    public async Task Retries_After_A_Malformed_Response_And_Succeeds_On_The_Second_Attempt()
    {
        var stub = new QueuedAIExecutionProvider("not json at all", """["refactoring"]""");
        var classifier = new AIActivityClassifier(stub);

        var tags = await classifier.ClassifyAsync("clean up the duplicated validation logic", new Dictionary<string, string>());

        Assert.Equal(2, stub.CallCount);
        Assert.Equal(["refactoring"], tags);
        Assert.Contains("could not be parsed", stub.PromptsSeen[1]);
    }

    [Fact]
    public async Task Falls_Back_To_An_Empty_Tag_List_After_Exhausting_Retries_Rather_Than_Throwing()
    {
        var stub = new QueuedAIExecutionProvider("garbage 1", "garbage 2", "garbage 3");
        var classifier = new AIActivityClassifier(stub);

        var tags = await classifier.ClassifyAsync("do something", new Dictionary<string, string>());

        Assert.Equal(3, stub.CallCount);
        Assert.Empty(tags); // graceful degradation, not an exception — see AIActivityClassifier's docs
    }

    [Fact]
    public async Task Filters_Out_Blank_Tags_From_The_Response()
    {
        var stub = new QueuedAIExecutionProvider("""["debugging", "", "  ", "performance"]""");
        var classifier = new AIActivityClassifier(stub);

        var tags = await classifier.ClassifyAsync("investigate slow response times", new Dictionary<string, string>());

        Assert.Equal(["debugging", "performance"], tags);
    }

    private sealed class QueuedAIExecutionProvider(params string[] responses) : IAIExecutionProvider
    {
        private readonly Queue<string> _responses = new(responses);

        public string Name => "stub-classifier";
        public int CallCount { get; private set; }
        public List<string> PromptsSeen { get; } = [];

        public Task<string> ExecuteAsync(string promptContent, IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken = default)
        {
            CallCount++;
            PromptsSeen.Add(promptContent);
            if (_responses.Count == 0)
                throw new InvalidOperationException("QueuedAIExecutionProvider ran out of canned responses.");
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
