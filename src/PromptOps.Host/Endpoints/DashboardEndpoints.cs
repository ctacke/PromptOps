using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PromptOps.Infrastructure.Persistence;
using PromptOps.Infrastructure.Persistence.Records;

namespace PromptOps.Host.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/dashboard");

        group.MapGet("/prompts", async (PromptOpsDbContext db, CancellationToken cancellationToken) =>
        {
            var prompts = await db.Prompts.AsNoTracking().Include(p => p.Versions).ToListAsync(cancellationToken);
            var scores = await db.PromptScores.AsNoTracking().ToListAsync(cancellationToken);

            var result = prompts.Select(p =>
            {
                var activeVersion = p.Versions.FirstOrDefault(v => v.Status == "Active");
                var activeVersionScore = activeVersion != null 
                    ? scores.Where(s => s.PromptVersionId == activeVersion.Id).OrderByDescending(s => s.ComputedAt).FirstOrDefault()?.OverallScore 
                    : null;

                return new
                {
                    p.Id,
                    p.Name,
                    p.CreatedAt,
                    Description = p.Metadata?.Description ?? string.Empty,
                    Tags = p.Metadata?.Tags ?? [],
                    VersionCount = p.Versions.Count,
                    ActiveVersionId = activeVersion?.Id,
                    ActiveVersionNumber = activeVersion?.VersionNumber,
                    ActiveVersionScore = activeVersionScore,
                    Versions = p.Versions.Select(v =>
                    {
                        var versionScore = scores.Where(s => s.PromptVersionId == v.Id).OrderByDescending(s => s.ComputedAt).FirstOrDefault();
                        return new
                        {
                            v.Id,
                            v.VersionNumber,
                            Status = v.Status.ToString(),
                            v.Content,
                            v.ChangelogEntry,
                            v.CreatedAt,
                            Score = versionScore?.OverallScore,
                            ComponentScores = versionScore?.ComponentScores ?? new Dictionary<string, double>()
                        };
                    }).OrderByDescending(v => v.VersionNumber).ToList()
                };
            }).ToList();

            return Results.Ok(result);
        });

        group.MapGet("/executions", async (
            PromptOpsDbContext db,
            int page = 1,
            int pageSize = 10,
            string? repository = null,
            string? status = null,
            CancellationToken cancellationToken = default) =>
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100;

            var query = db.Executions.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(repository))
            {
                query = query.Where(e => e.Repository == repository);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(e => e.Status == status);
            }

            var allFiltered = await query.ToListAsync(cancellationToken);
            var totalCount = allFiltered.Count;

            var items = allFiltered
                .OrderByDescending(e => e.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Resolve prompt names and versions
            var prompts = await db.Prompts.AsNoTracking().Include(p => p.Versions).ToListAsync(cancellationToken);
            var versionMap = prompts
                .SelectMany(p => p.Versions.Select(v => new { v.Id, PromptName = p.Name, v.VersionNumber }))
                .ToDictionary(x => x.Id, x => new { x.PromptName, x.VersionNumber });

            var itemResponses = items.Select(e =>
            {
                versionMap.TryGetValue(e.PromptVersionId, out var versionInfo);
                return new
                {
                    e.Id,
                    e.PromptVersionId,
                    PromptName = versionInfo?.PromptName ?? "Untracked",
                    PromptVersionNumber = versionInfo?.VersionNumber ?? 0,
                    e.DeveloperId,
                    e.Timestamp,
                    e.Repository,
                    e.Branch,
                    e.Commit,
                    e.TaskId,
                    e.Status,
                    e.ExecutionTimeMs,
                    FilesChangedCount = e.FilesChanged?.Count ?? 0,
                    e.LinesAdded,
                    e.LinesDeleted
                };
            }).ToList();

            return Results.Ok(new
            {
                totalCount,
                page,
                pageSize,
                items = itemResponses
            });
        });

        group.MapGet("/executions/{id:guid}", async (Guid id, PromptOpsDbContext db, CancellationToken cancellationToken) =>
        {
            var execution = await db.Executions
                .AsNoTracking()
                .Include(e => e.ToolUsage)
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

            if (execution == null) return Results.NotFound();

            var prompts = await db.Prompts.AsNoTracking().Include(p => p.Versions).ToListAsync(cancellationToken);
            var versionInfo = prompts
                .SelectMany(p => p.Versions.Select(v => new { v.Id, PromptName = p.Name, v.VersionNumber }))
                .FirstOrDefault(x => x.Id == execution.PromptVersionId);

            var humanEvaluation = await db.HumanEvaluations
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.ExecutionId == id, cancellationToken);

            var aiEvaluation = await db.AIEvaluations
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.ExecutionId == id, cancellationToken);

            var metrics = await db.EngineeringMetrics
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ExecutionId == id, cancellationToken);

            var latestScore = (await db.PromptScores
                .AsNoTracking()
                .Where(s => s.PromptVersionId == execution.PromptVersionId)
                .ToListAsync(cancellationToken))
                .OrderByDescending(s => s.ComputedAt)
                .FirstOrDefault();

            var response = new
            {
                execution.Id,
                execution.PromptVersionId,
                PromptName = versionInfo?.PromptName ?? "Untracked",
                PromptVersionNumber = versionInfo?.VersionNumber ?? 0,
                execution.DeveloperId,
                execution.Timestamp,
                execution.Repository,
                execution.Branch,
                execution.Commit,
                execution.TaskId,
                execution.ReferencedDocuments,
                execution.ReferencedADRs,
                execution.AcceptanceCriteria,
                execution.Languages,
                execution.Inputs,
                execution.Status,
                execution.Output,
                execution.ExecutionTimeMs,
                execution.AiProviderId,
                execution.Model,
                execution.ModelParameters,
                execution.FilesChanged,
                execution.LinesAdded,
                execution.LinesDeleted,
                ToolUsage = execution.ToolUsage.Select(t => new { t.Name, t.Count, t.DurationMs }).ToList(),
                HumanEvaluation = humanEvaluation != null ? new
                {
                    humanEvaluation.Correctness,
                    humanEvaluation.Helpfulness,
                    humanEvaluation.Architecture,
                    humanEvaluation.Readability,
                    humanEvaluation.Completeness,
                    humanEvaluation.Hallucinations,
                    humanEvaluation.Confidence,
                    humanEvaluation.OverallSatisfaction,
                    humanEvaluation.Notes,
                    humanEvaluation.Timestamp
                } : null,
                AIEvaluation = aiEvaluation != null ? new
                {
                    aiEvaluation.SatisfiesAcceptanceCriteria,
                    aiEvaluation.AdrViolations,
                    aiEvaluation.IgnoredRequirements,
                    aiEvaluation.UnnecessaryComplexityNotes,
                    aiEvaluation.SuggestedPromptImprovements,
                    aiEvaluation.Timestamp
                } : null,
                Metrics = metrics != null ? new
                {
                    metrics.BuildSuccess,
                    metrics.TestSuccess,
                    metrics.Coverage,
                    metrics.SonarIssues,
                    metrics.Warnings,
                    metrics.CodeSmells,
                    metrics.SecurityFindings,
                    metrics.Duplication,
                    metrics.CyclomaticComplexity,
                    metrics.ReviewComments,
                    metrics.ReviewIterations,
                    metrics.MergeTimeMinutes,
                    metrics.RollbackNeeded,
                    metrics.ManualEdits
                } : null,
                Score = latestScore != null ? new
                {
                    latestScore.OverallScore,
                    latestScore.ComponentScores,
                    latestScore.SampleSize
                } : null
            };

            return Results.Ok(response);
        });

        group.MapGet("/stats-trends", async (PromptOpsDbContext db, CancellationToken cancellationToken) =>
        {
            var executions = await db.Executions.AsNoTracking().ToListAsync(cancellationToken);
            var metrics = await db.EngineeringMetrics.AsNoTracking().ToListAsync(cancellationToken);
            var scores = await db.PromptScores.AsNoTracking().ToListAsync(cancellationToken);

            // Group by repository
            var repoCounts = executions.GroupBy(e => e.Repository)
                .Select(g => new { Repository = g.Key, Count = g.Count() })
                .ToList();

            // Build success rate over time (by week/day based on Timestamp)
            var buildTrends = metrics
                .Where(m => m.BuildSuccess.HasValue)
                .OrderBy(m => m.CollectedAt)
                .Select(m => new
                {
                    Date = m.CollectedAt.ToString("yyyy-MM-dd"),
                    Success = m.BuildSuccess!.Value ? 1 : 0
                })
                .GroupBy(m => m.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    BuildSuccessRate = Math.Round((double)g.Sum(x => x.Success) / g.Count() * 100, 1)
                })
                .ToList();

            // Score trend
            var scoreTrends = scores
                .OrderBy(s => s.ComputedAt)
                .Select(s => new
                {
                    Date = s.ComputedAt.ToString("yyyy-MM-dd"),
                    Score = s.OverallScore
                })
                .GroupBy(s => s.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    AverageScore = Math.Round(g.Average(x => x.Score), 1)
                })
                .ToList();

            return Results.Ok(new
            {
                repositories = repoCounts,
                buildSuccessTrends = buildTrends,
                scoreTrends = scoreTrends
            });
        });
    }
}
