namespace PromptOps.Application.Executions;

public sealed class ExecutionNotFoundException(Guid executionId)
    : Exception($"Execution '{executionId}' was not found.")
{
    public Guid ExecutionId { get; } = executionId;
}
