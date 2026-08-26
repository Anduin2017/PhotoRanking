using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms;
using System.Text;

namespace Anduin.PhotoRanking.Services;

/// <summary>
/// 从每张照片的最终人工分训练个人化回归模型。
/// 历史评分事件、评分次数、相册分和旧综合分都不会进入训练集。
/// </summary>
public sealed class PersonalizedPredictionService(
    AppDbContext context,
    IMemoryCache cache,
    ILogger<PersonalizedPredictionService> logger)
{
    public const string EmbeddingModelName = "openai/clip-vit-base-patch32";
    public const string AlgorithmVersion = "personal-sdca-recent-ensemble-coverage-v4";
    private const string ModelCacheKeyPrefix = "personal-score-model:";
    private const int FeatureDimension = 512;
    private const int MinimumTrainingPhotos = 20;
    private const int MaximumRegressionTrainingPhotos = 8_000;
    private const int UncertaintyMemberCount = 5;
    private const int MaximumCoverageCentroids = 128;
    private const float LinearL2Regularization = 0.000625f;
    private const string ModelBundleMagic = "PRS3";

    private readonly MLContext _mlContext = new(seed: 20260826);

    public async Task<PredictionModel?> TrainAndActivateAsync(
        DateTime? ratingWatermark = null,
        CancellationToken cancellationToken = default)
    {
        var trainingCutoff = ratingWatermark ?? DateTime.UtcNow;
        var samples = await context.Photos
            .AsNoTracking()
            .Where(p => p.IndependentScore != null &&
                        p.FeatureVector != null &&
                        (p.LastRatedAt == null || p.LastRatedAt <= trainingCutoff))
            .OrderBy(p => p.LastRatedAt)
            .Select(p => new TrainingRow(p.FeatureVector!, p.IndependentScore!.Value))
            .ToListAsync(cancellationToken);

        var validSamples = samples
            .Select(ToModelInput)
            .Where(x => x != null)
            .Cast<ModelInput>()
            .ToList();

        if (validSamples.Count < MinimumTrainingPhotos)
        {
            logger.LogWarning(
                "Only {Count} valid final ratings are available. At least {Minimum} are required to train a personal model.",
                validSamples.Count,
                MinimumTrainingPhotos);
            return null;
        }

        // The user's taste and scoring calibration drift over time. Production
        // chronological replays showed that the latest 8,000 final judgments predict
        // future ratings better than treating every old preference as equally current.
        // All valid anchors are still retained below for visual coverage modeling.
        var regressionSamples = validSamples
            .TakeLast(MaximumRegressionTrainingPhotos)
            .ToList();

        double? validationMae = null;
        if (regressionSamples.Count >= 100)
        {
            var validationCount = Math.Max(20, regressionSamples.Count / 5);
            var trainingRows = regressionSamples.Take(regressionSamples.Count - validationCount).ToList();
            var validationRows = regressionSamples.Skip(regressionSamples.Count - validationCount).ToList();
            var validationModel = TrainModel(trainingRows, out _);
            var validationData = _mlContext.Data.LoadFromEnumerable(validationRows);
            var metrics = _mlContext.Regression.Evaluate(validationModel.Transform(validationData));
            validationMae = metrics.MeanAbsoluteError;
        }

        var trainedModels = new List<TrainedModel>();
        var fullModel = TrainModel(regressionSamples, out var trainingSchema);
        trainedModels.Add(new TrainedModel(fullModel, trainingSchema));
        for (var member = 0; member < UncertaintyMemberCount; member++)
        {
            var bootstrapRows = CreateBootstrapRows(regressionSamples, member);
            var bootstrapModel = TrainModel(bootstrapRows, out var bootstrapSchema);
            trainedModels.Add(new TrainedModel(bootstrapModel, bootstrapSchema));
        }

        var coverageCentroidCount = Math.Clamp(
            validSamples.Count / 10,
            2,
            MaximumCoverageCentroids);
        var coverageModel = TrainCoverageModel(validSamples, coverageCentroidCount, out var coverageSchema);
        var trainedCoverageModel = new TrainedModel(coverageModel, coverageSchema);
        var modelData = SaveModelBundle(trainedModels, trainedCoverageModel);
        var modelBundle = new ModelBundle(
            trainedModels.Select(x => x.Model).ToList(),
            coverageModel);

        var trainedAt = DateTime.UtcNow;
        var version = $"{AlgorithmVersion}-{trainedAt:yyyyMMddHHmmssfff}";
        var storedModel = await context.PredictionModels.FindAsync([1], cancellationToken);
        if (storedModel == null)
        {
            storedModel = new PredictionModel
            {
                Id = 1,
                Version = version,
                EmbeddingModel = EmbeddingModelName,
                ModelData = modelData,
                TrainedAt = trainedAt,
                TrainingRatingWatermark = trainingCutoff,
                TrainingPhotoCount = regressionSamples.Count,
                TrainingCandidatePhotoCount = samples.Count,
                CoverageTrainingPhotoCount = validSamples.Count,
                EnsembleSize = UncertaintyMemberCount,
                CoverageCentroidCount = coverageCentroidCount,
                ValidationMeanAbsoluteError = validationMae
            };
            context.PredictionModels.Add(storedModel);
        }
        else
        {
            cache.Remove(ModelCacheKeyPrefix + storedModel.Version);
            storedModel.Version = version;
            storedModel.EmbeddingModel = EmbeddingModelName;
            storedModel.ModelData = modelData;
            storedModel.TrainedAt = trainedAt;
            storedModel.TrainingRatingWatermark = trainingCutoff;
            storedModel.TrainingPhotoCount = regressionSamples.Count;
            storedModel.TrainingCandidatePhotoCount = samples.Count;
            storedModel.CoverageTrainingPhotoCount = validSamples.Count;
            storedModel.EnsembleSize = UncertaintyMemberCount;
            storedModel.CoverageCentroidCount = coverageCentroidCount;
            storedModel.ValidationMeanAbsoluteError = validationMae;
        }

        await context.SaveChangesAsync(cancellationToken);
        cache.Set(ModelCacheKeyPrefix + version, modelBundle);

        logger.LogInformation(
            "Activated personal prediction model {Version} from {RegressionCount} recent final ratings and {CoverageCount} coverage anchors with {CentroidCount} centroids. Validation MAE: {Mae}",
            version,
            regressionSamples.Count,
            validSamples.Count,
            coverageCentroidCount,
            validationMae);

        return storedModel;
    }

    public async Task<PredictionResult?> PredictAsync(
        byte[] featureVector,
        CancellationToken cancellationToken = default)
    {
        var input = ToModelInput(new TrainingRow(featureVector, 0));
        if (input == null) return null;

        var activeModel = await LoadActiveModelAsync(cancellationToken);
        if (activeModel == null) return null;

        var scores = activeModel.Bundle.Models
            .Select(model =>
            {
                var engine = _mlContext.Model.CreatePredictionEngine<ModelInput, ModelOutput>(model);
                return ClampScore(engine.Predict(input).Score);
            })
            .ToList();

        return new PredictionResult(
            scores[0],
            CalculateUncertainty(scores.Skip(1)),
            CalculateNovelty(activeModel.Bundle.CoverageModel, input),
            activeModel.Metadata.Version);
    }

    public async Task<bool> PredictAndPersistBatchAsync(
        IReadOnlyList<Photo> photos,
        CancellationToken cancellationToken = default)
    {
        var activeModel = await LoadActiveModelAsync(cancellationToken);
        if (activeModel == null) return false;

        var now = DateTime.UtcNow;
        var predictablePhotos = new List<Photo>();
        var inputs = new List<ModelInput>();

        foreach (var photo in photos)
        {
            if (photo.IndependentScore != null)
            {
                continue;
            }

            var input = photo.FeatureVector == null
                ? null
                : ToModelInput(new TrainingRow(photo.FeatureVector, 0));

            photo.EstimatedScoreUpdatedAt = now;
            photo.EstimatedScoreModelVersion = activeModel.Metadata.Version;
            if (input == null)
            {
                photo.EstimatedScore = null;
                photo.PredictionUncertainty = null;
                photo.PredictionNovelty = null;
                continue;
            }

            predictablePhotos.Add(photo);
            inputs.Add(input);
        }

        if (inputs.Count > 0)
        {
            var data = _mlContext.Data.LoadFromEnumerable(inputs);
            var outputsByModel = activeModel.Bundle.Models
                .Select(model => _mlContext.Data
                    .CreateEnumerable<ModelOutput>(model.Transform(data), reuseRowObject: false)
                    .Select(output => ClampScore(output.Score))
                    .ToList())
                .ToList();
            var coverageOutputs = activeModel.Bundle.CoverageModel == null
                ? null
                : _mlContext.Data
                    .CreateEnumerable<CoverageOutput>(activeModel.Bundle.CoverageModel.Transform(data), reuseRowObject: false)
                    .ToList();

            for (var i = 0; i < predictablePhotos.Count; i++)
            {
                predictablePhotos[i].EstimatedScore = outputsByModel[0][i];
                var memberScores = new double[outputsByModel.Count - 1];
                for (var member = 1; member < outputsByModel.Count; member++)
                {
                    memberScores[member - 1] = outputsByModel[member][i];
                }
                predictablePhotos[i].PredictionUncertainty = CalculateUncertainty(memberScores);
                predictablePhotos[i].PredictionNovelty = coverageOutputs == null
                    ? null
                    : CalculateNovelty(coverageOutputs[i].Distances);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<PredictionModel?> GetActiveModelMetadataAsync(CancellationToken cancellationToken = default) =>
        context.PredictionModels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);

    private ITransformer TrainModel(IReadOnlyCollection<ModelInput> rows, out DataViewSchema schema)
    {
        var data = _mlContext.Data.LoadFromEnumerable(rows);
        schema = data.Schema;

        // CLIP already emits L2-normalized embeddings. Per-coordinate variance
        // normalization made the ridge penalty badly scaled and overfit rare dimensions
        // in production chronological replays, so the regression head consumes the raw
        // unit embedding directly.
        return _mlContext.Regression.Trainers.Sdca(
                labelColumnName: nameof(ModelInput.Label),
                featureColumnName: nameof(ModelInput.Features),
                l2Regularization: LinearL2Regularization,
                maximumNumberOfIterations: 100)
            .Fit(data);
    }

    private ITransformer TrainCoverageModel(
        IReadOnlyCollection<ModelInput> rows,
        int centroidCount,
        out DataViewSchema schema)
    {
        var data = _mlContext.Data.LoadFromEnumerable(rows);
        schema = data.Schema;
        // CLIP exports unit vectors today. Normalize again in this dedicated pipeline so
        // novelty remains cosine-like if an older or future embedding source drifts in scale.
        var pipeline = _mlContext.Transforms
            .NormalizeLpNorm(
                nameof(ModelInput.Features),
                norm: LpNormNormalizingEstimatorBase.NormFunction.L2)
            .Append(_mlContext.Clustering.Trainers.KMeans(
                featureColumnName: nameof(ModelInput.Features),
                numberOfClusters: centroidCount));
        return pipeline
            .Fit(data);
    }

    private static List<ModelInput> CreateBootstrapRows(IReadOnlyList<ModelInput> rows, int member)
    {
        var random = new Random(20260826 + member * 7919);
        var result = new List<ModelInput>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            result.Add(rows[random.Next(rows.Count)]);
        }
        return result;
    }

    private byte[] SaveModelBundle(
        IReadOnlyList<TrainedModel> models,
        TrainedModel coverageModel)
    {
        using var bundleStream = new MemoryStream();
        using var writer = new BinaryWriter(bundleStream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes(ModelBundleMagic));
        writer.Write(models.Count);

        foreach (var trainedModel in models)
        {
            var bytes = SaveTransformer(trainedModel);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        var coverageBytes = SaveTransformer(coverageModel);
        writer.Write(coverageBytes.Length);
        writer.Write(coverageBytes);

        writer.Flush();
        return bundleStream.ToArray();
    }

    private ModelBundle LoadModelBundle(byte[] modelData)
    {
        if (modelData.Length >= 8 &&
            Encoding.ASCII.GetString(modelData, 0, ModelBundleMagic.Length) == ModelBundleMagic)
        {
            using var bundleStream = new MemoryStream(modelData, writable: false);
            using var reader = new BinaryReader(bundleStream, Encoding.UTF8, leaveOpen: false);
            reader.ReadBytes(ModelBundleMagic.Length);
            var count = reader.ReadInt32();
            if (count is < 1 or > 32)
            {
                throw new InvalidDataException($"Invalid personal model bundle member count: {count}.");
            }

            var models = new List<ITransformer>(count);
            for (var i = 0; i < count; i++)
            {
                var length = reader.ReadInt32();
                if (length <= 0 || length > bundleStream.Length - bundleStream.Position)
                {
                    throw new InvalidDataException("Invalid personal model bundle payload length.");
                }

                using var modelStream = new MemoryStream(reader.ReadBytes(length), writable: false);
                models.Add(_mlContext.Model.Load(modelStream, out _));
            }

            var coverageLength = reader.ReadInt32();
            if (coverageLength <= 0 || coverageLength > bundleStream.Length - bundleStream.Position)
            {
                throw new InvalidDataException("Invalid coverage model payload length.");
            }
            using var coverageStream = new MemoryStream(reader.ReadBytes(coverageLength), writable: false);
            var coverageModel = _mlContext.Model.Load(coverageStream, out _);
            return new ModelBundle(models, coverageModel);
        }

        // Compatibility fallback for the original single-model storage format. It cannot
        // produce active-learning metadata and will be replaced on the next training run.
        using var legacyStream = new MemoryStream(modelData, writable: false);
        var legacyModel = _mlContext.Model.Load(legacyStream, out _);
        return new ModelBundle([legacyModel], null);
    }

    private byte[] SaveTransformer(TrainedModel trainedModel)
    {
        using var modelStream = new MemoryStream();
        _mlContext.Model.Save(trainedModel.Model, trainedModel.Schema, modelStream);
        return modelStream.ToArray();
    }

    private async Task<LoadedModel?> LoadActiveModelAsync(CancellationToken cancellationToken)
    {
        var metadata = await context.PredictionModels
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (metadata == null) return null;

        var cacheKey = ModelCacheKeyPrefix + metadata.Version;
        if (!cache.TryGetValue<ModelBundle>(cacheKey, out var bundle) || bundle == null)
        {
            bundle = LoadModelBundle(metadata.ModelData);
            cache.Set(cacheKey, bundle);
        }

        return new LoadedModel(bundle, metadata);
    }

    private static ModelInput? ToModelInput(TrainingRow row)
    {
        var features = ImageAnalysisService.ByteArrayToFloatArray(row.FeatureVector);
        if (features.Length != FeatureDimension) return null;

        return new ModelInput
        {
            Features = features,
            Label = (float)row.Score
        };
    }

    private static double ClampScore(float score) => Math.Clamp(score, 0f, 6f);

    private static double? CalculateUncertainty(IEnumerable<double> memberScores)
    {
        var scores = memberScores.ToArray();
        if (scores.Length == 0) return null;

        var mean = scores.Average();
        var variance = scores.Sum(score => Math.Pow(score - mean, 2)) / scores.Length;
        return Math.Sqrt(Math.Max(0, variance));
    }

    private double? CalculateNovelty(ITransformer? coverageModel, ModelInput input)
    {
        if (coverageModel == null) return null;
        var engine = _mlContext.Model.CreatePredictionEngine<ModelInput, CoverageOutput>(coverageModel);
        return CalculateNovelty(engine.Predict(input).Distances);
    }

    private static double? CalculateNovelty(float[]? distances)
    {
        if (distances == null || distances.Length == 0) return null;
        return Math.Sqrt(Math.Max(0, distances.Min()));
    }

    private sealed record TrainingRow(byte[] FeatureVector, double Score);
    private sealed record TrainedModel(ITransformer Model, DataViewSchema Schema);
    private sealed record ModelBundle(IReadOnlyList<ITransformer> Models, ITransformer? CoverageModel);
    private sealed record LoadedModel(ModelBundle Bundle, PredictionModel Metadata);

    private sealed class ModelInput
    {
        [VectorType(FeatureDimension)]
        public required float[] Features { get; init; }

        public float Label { get; init; }
    }

    private sealed class ModelOutput
    {
        [ColumnName("Score")]
        public float Score { get; init; }
    }

    private sealed class CoverageOutput
    {
        [ColumnName("Score")]
        public float[] Distances { get; init; } = [];
    }
}

public sealed record PredictionResult(double Score, double? Uncertainty, double? Novelty, string ModelVersion);
