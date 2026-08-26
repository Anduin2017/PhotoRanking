using Anduin.PhotoRanking.Data;
using Microsoft.EntityFrameworkCore;

namespace Anduin.PhotoRanking.Services;

/// <summary>
/// 个人化预测后台工作者。
/// 用户停止评分 20 分钟后，用每张照片的最终人工分重新训练模型，
/// 再只为尚未评分的照片刷新预测。
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
        var predictionService = scope.ServiceProvider.GetRequiredService<PersonalizedPredictionService>();

        // 1. 获取全局状态
        var state = await context.SystemStates.FirstOrDefaultAsync(ct);
        if (state == null)
        {
            state = new Anduin.PhotoRanking.Models.SystemState
            {
                LastRatingAt = await context.Photos.MaxAsync(p => p.LastRatedAt, ct) ?? DateTime.MinValue
            };
            context.SystemStates.Add(state);
            await context.SaveChangesAsync(ct);
        }

        // This is the newest rating the model can possibly include. Never advance the
        // training watermark past it: a rating may arrive while a large refresh runs.
        var ratingWatermark = state.LastRatingAt;

        // 2. 检查静默时间
        var timeSinceLastRating = DateTime.UtcNow - ratingWatermark;
        if (timeSinceLastRating < _quietPeriod)
        {
            return;
        }

        var activeModel = await predictionService.GetActiveModelMetadataAsync(ct);
        var trainingCandidateCount = await context.Photos.CountAsync(p =>
            p.IndependentScore != null && p.FeatureVector != null, ct);
        var modelIsStale = activeModel == null ||
                           activeModel.EmbeddingModel != PersonalizedPredictionService.EmbeddingModelName ||
                           !activeModel.Version.StartsWith(
                               PersonalizedPredictionService.AlgorithmVersion + "-",
                               StringComparison.Ordinal) ||
                           activeModel.TrainingRatingWatermark < ratingWatermark ||
                           activeModel.TrainingCandidatePhotoCount != trainingCandidateCount;
        if (modelIsStale)
        {
            activeModel = await predictionService.TrainAndActivateAsync(ratingWatermark, ct);
        }

        if (activeModel == null)
        {
            return;
        }

        // 只刷新未评分照片。已评分照片的旧预测必须保留，避免评分后用自己预测自己。
        var needsUpdate = await context.Photos.AnyAsync(p =>
            p.IndependentScore == null &&
            p.FeatureVector != null &&
            (p.EstimatedScoreUpdatedAt == null ||
             p.EstimatedScoreModelVersion != activeModel.Version), ct);

        if (!needsUpdate) return;

        logger.LogInformation(
            "Refreshing unrated predictions with personal model {Version}...",
            activeModel.Version);

        // 4. 分批更新推测分
        const int batchSize = 1000;
        while (true)
        {
            // 模型版本是预测缓存的唯一失效依据。
            var photosToUpdate = await context.Photos
                .Where(p => p.IndependentScore == null &&
                            p.FeatureVector != null &&
                            (p.EstimatedScoreUpdatedAt == null ||
                             p.EstimatedScoreModelVersion != activeModel.Version))
                .Take(batchSize)
                .ToListAsync(ct);

            if (photosToUpdate.Count == 0) break;

            logger.LogInformation("Processing batch of {Count} photos...", photosToUpdate.Count);
            
            await predictionService.PredictAndPersistBatchAsync(photosToUpdate, ct);
            context.ChangeTracker.Clear();
        }

        // 5. 更新全局水印
        state = await context.SystemStates.FirstAsync(ct);
        if (state.LastRatingAt <= ratingWatermark && state.LastGlobalScoringAt < ratingWatermark)
        {
            state.LastGlobalScoringAt = ratingWatermark;
        }
        await context.SaveChangesAsync(ct);
        
        logger.LogInformation("Background scoring completed.");
    }
}
