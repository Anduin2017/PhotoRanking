using Anduin.PhotoRanking.Data;
using Microsoft.EntityFrameworkCore;

namespace Anduin.PhotoRanking.Services;

/// <summary>
/// 后台推测分计算工作者
/// 策略：当用户停止评分 20 分钟后，且系统存在未同步的评分变更时，触发全量计算。
/// </summary>
public class PredictorBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<PredictorBackgroundService> logger) : BackgroundService
{
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);
    private readonly TimeSpan _quietPeriod = TimeSpan.FromMinutes(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Predictor background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
                await DoWorkAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in predictor background service.");
            }
        }
    }

    private async Task DoWorkAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scoringService = scope.ServiceProvider.GetRequiredService<ScoringService>();

        // 1. 获取全局状态
        var state = await context.SystemStates.FirstOrDefaultAsync(ct);
        if (state == null) return;

        // 2. 检查静默时间
        var timeSinceLastRating = DateTime.UtcNow - state.LastRatingAt;
        if (timeSinceLastRating < _quietPeriod)
        {
            return;
        }

        // 3. 检查是否有待更新的照片（水印对比）
        // 如果自上次打分后，还没进行过全量更新，或者存在从未更新过的照片
        bool needsUpdate = state.LastRatingAt > state.LastGlobalScoringAt ||
                          await context.Photos.AnyAsync(p => p.EstimatedScoreUpdatedAt == null, ct);

        if (!needsUpdate) return;

        logger.LogInformation("Quiet period detected. Starting batch background scoring...");

        // 4. 分批更新推测分
        int batchSize = 100;
        while (true)
        {
            // 找出所有推测分已过期的照片（或是从未计算过的）
            var photosToUpdate = await context.Photos
                .Where(p => p.EstimatedScoreUpdatedAt == null || p.EstimatedScoreUpdatedAt < state.LastRatingAt)
                .Take(batchSize)
                .ToListAsync(ct);

            if (photosToUpdate.Count == 0) break;

            logger.LogInformation("Processing batch of {Count} photos...", photosToUpdate.Count);
            
            // 执行批量计算并持久化
            await scoringService.BatchGuessScoresInternal(photosToUpdate);
            
            // 注意：BatchGuessScoresInternal 内部已调用 SaveChangesAsync
        }

        // 5. 更新全局水印
        state.LastGlobalScoringAt = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
        
        logger.LogInformation("Background scoring completed.");
    }
}
