namespace PromptOps.Domain.Executions;

/// <summary>
/// A single prompt execution and everything observed about it. An independent aggregate from
/// <c>Prompt</c> — <see cref="PromptVersionId"/> references a version by id only, the same way
/// <c>PromptDependency.TargetPromptVersionId</c> does, so recording an execution never requires
/// the referenced prompt/version to be loaded or even to exist in the same transaction.
/// </summary>
public sealed class ExecutionRecord : AggregateRoot
{
    private readonly List<ToolUsage> _toolUsage = [];

    public Guid Id { get; }
    public Guid PromptVersionId { get; }
    public string DeveloperId { get; }
    public DateTimeOffset Timestamp { get; }
    public DevelopmentContext Context { get; }
    public IReadOnlyDictionary<string, string> Inputs { get; }
    public ExecutionStatus Status { get; private set; }
    public string? Output { get; private set; }
    public TimeSpan? ExecutionTime { get; private set; }
    public string? AiProviderId { get; private set; }
    public string? Model { get; private set; }
    public string? ModelParameters { get; private set; }
    public IReadOnlyList<string> FilesChanged { get; private set; } = [];
    public int LinesAdded { get; private set; }
    public int LinesDeleted { get; private set; }
    public IReadOnlyList<ToolUsage> ToolUsage => _toolUsage.AsReadOnly();

    private ExecutionRecord(
        Guid id,
        Guid promptVersionId,
        string developerId,
        DateTimeOffset timestamp,
        DevelopmentContext context,
        IReadOnlyDictionary<string, string> inputs,
        ExecutionStatus status)
    {
        Id = id;
        PromptVersionId = promptVersionId;
        DeveloperId = developerId;
        Timestamp = timestamp;
        Context = context;
        Inputs = inputs;
        Status = status;
    }

    public static ExecutionRecord Start(
        Guid promptVersionId,
        string developerId,
        DevelopmentContext context,
        IReadOnlyDictionary<string, string>? inputs = null,
        DateTimeOffset? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(developerId))
            throw new ArgumentException("developerId is required.", nameof(developerId));
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.Repository))
            throw new ArgumentException("context.Repository is required.", nameof(context));

        return new ExecutionRecord(
            Guid.NewGuid(),
            promptVersionId,
            developerId,
            timestamp ?? DateTimeOffset.UtcNow,
            context,
            inputs ?? new Dictionary<string, string>(),
            ExecutionStatus.InProgress);
    }

    /// <summary>Reconstructs an execution from persisted state (e.g. by a repository).</summary>
    public static ExecutionRecord Rehydrate(
        Guid id,
        Guid promptVersionId,
        string developerId,
        DateTimeOffset timestamp,
        DevelopmentContext context,
        IReadOnlyDictionary<string, string> inputs,
        ExecutionStatus status,
        string? output,
        TimeSpan? executionTime,
        string? aiProviderId,
        string? model,
        string? modelParameters,
        IReadOnlyList<string> filesChanged,
        int linesAdded,
        int linesDeleted,
        IEnumerable<ToolUsage> toolUsage)
    {
        var record = new ExecutionRecord(id, promptVersionId, developerId, timestamp, context, inputs, status)
        {
            Output = output,
            ExecutionTime = executionTime,
            AiProviderId = aiProviderId,
            Model = model,
            ModelParameters = modelParameters,
            FilesChanged = filesChanged,
            LinesAdded = linesAdded,
            LinesDeleted = linesDeleted
        };
        record._toolUsage.AddRange(toolUsage);
        return record;
    }

    public void RecordToolUsage(string name, int count, TimeSpan duration, DateTimeOffset? recordedAt = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tool name is required.", nameof(name));
        if (Status != ExecutionStatus.InProgress)
            throw new InvalidOperationException("Cannot record tool usage after the execution has finished.");

        _toolUsage.Add(new ToolUsage(name, count, duration, recordedAt ?? DateTimeOffset.UtcNow));
    }

    public void Finish(
        string? output,
        TimeSpan executionTime,
        string? aiProviderId,
        string? model,
        string? modelParameters,
        IReadOnlyList<string> filesChanged,
        int linesAdded,
        int linesDeleted)
    {
        if (Status != ExecutionStatus.InProgress)
            throw new InvalidOperationException("Execution has already finished.");
        if (linesAdded < 0)
            throw new ArgumentOutOfRangeException(nameof(linesAdded), "Lines added cannot be negative.");
        if (linesDeleted < 0)
            throw new ArgumentOutOfRangeException(nameof(linesDeleted), "Lines deleted cannot be negative.");

        Output = output;
        ExecutionTime = executionTime;
        AiProviderId = aiProviderId;
        Model = model;
        ModelParameters = modelParameters;
        FilesChanged = filesChanged;
        LinesAdded = linesAdded;
        LinesDeleted = linesDeleted;
        Status = ExecutionStatus.Finished;

        AddDomainEvent(new ExecutionRecorded(Id, PromptVersionId, Context.Repository, Timestamp));
    }
}
