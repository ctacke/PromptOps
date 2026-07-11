namespace PromptOps.Application.Scoring;

/// <summary>Aggregate summary across every computed <c>PromptScore</c>. <see cref="AverageOverallScore"/> is <c>null</c>, not 0, when no scores exist yet — same "missing data isn't scored zero" discipline the scoring engine itself follows.</summary>
public sealed record ScoreStatistics(int Count, double? AverageOverallScore);
