namespace PromptOps.Application.Executions;

/// <summary>Aggregate counts across every execution in the shared database, computed in SQL (never materializing full <c>ExecutionRecord</c> aggregates).</summary>
public sealed record ExecutionStatistics(int TotalCount, IReadOnlyDictionary<string, int> CountByStatus, IReadOnlyDictionary<string, int> CountByRepository);
