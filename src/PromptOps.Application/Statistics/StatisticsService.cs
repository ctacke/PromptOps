using PromptOps.Application.Evaluations;
using PromptOps.Application.Executions;
using PromptOps.Application.Prompts;
using PromptOps.Application.Scoring;

namespace PromptOps.Application.Statistics;

/// <summary>Composes a <see cref="SystemStatistics"/> snapshot from five independent, already-lightweight repository queries — run concurrently via <see cref="Task.WhenAll(Task[])"/> since none of them depend on each other.</summary>
public sealed class StatisticsService(
    IPromptRepository promptRepository,
    IExecutionRepository executionRepository,
    IPromptScoreRepository scoreRepository,
    IHumanEvaluationRepository humanEvaluationRepository,
    IAIEvaluationRepository aiEvaluationRepository)
{
    public async Task<SystemStatistics> GetAsync(CancellationToken cancellationToken = default)
    {
        var promptsTask = promptRepository.GetStatisticsAsync(cancellationToken);
        var executionsTask = executionRepository.GetStatisticsAsync(cancellationToken);
        var scoresTask = scoreRepository.GetStatisticsAsync(cancellationToken);
        var humanEvaluationCountTask = humanEvaluationRepository.GetCountAsync(cancellationToken);
        var aiEvaluationCountTask = aiEvaluationRepository.GetCountAsync(cancellationToken);

        await Task.WhenAll(promptsTask, executionsTask, scoresTask, humanEvaluationCountTask, aiEvaluationCountTask);

        return new SystemStatistics(
            await promptsTask,
            await executionsTask,
            await scoresTask,
            await humanEvaluationCountTask,
            await aiEvaluationCountTask);
    }
}
