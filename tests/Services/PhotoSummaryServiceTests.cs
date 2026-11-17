using System.Net;
using Microsoft.Extensions.Options;
using System.Text;
using Aiursoft.DbTools.Sqlite;
using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Anduin.PhotoRanking.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using SkiaSharp;

namespace Anduin.PhotoRanking.Tests.Services;

[TestClass]
public class PhotoSummaryServiceTests
{
    private AppDbContext _context = null!;
    private string _directory = null!;
    private RecordingModel _model = null!;
    private PhotoSummaryService _service = null!;
    private IConfiguration _configuration = null!;
    private SummaryOptions _options = null!;
    private readonly SummaryProgress _progress = new();

    [TestInitialize]
    public async Task Setup()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("DataSource=:memory:").Options);
        await _context.Database.OpenConnectionAsync();
        await _context.Database.EnsureCreatedAsync();
        _directory = Path.Combine(Path.GetTempPath(), "photo-summary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        await File.WriteAllBytesAsync(Path.Combine(_directory, "test.png"), data.ToArray());
        _context.Albums.Add(new Album { AlbumId = "test", Name = "Test" });
        for (var i = 0; i < 10; i++)
            _context.Photos.Add(new Photo { AlbumId = "test", FilePath = "test.png", EstimatedScore = 6 - i * .5 });
        await _context.SaveChangesAsync();
        _model = new RecordingModel();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Summary:ApiKey"] = "synthetic-test-key",
            ["Summary:Endpoint"] = "http://model.test/api/chat",
            ["Summary:RetryDelaySeconds"] = "0",
            ["PhotoRootPath"] = _directory
        }).Build();
        _configuration = configuration;
        _options = configuration.GetSection("Summary").Get<SummaryOptions>()!;
        _service = new PhotoSummaryService(_context, _model, configuration, Options.Create(_options), NullLogger<PhotoSummaryService>.Instance, _progress);
        await Save(10);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _context.Dispose();
        _model.Dispose();
        Directory.Delete(_directory, true);
    }

    private Task Save(double percent, string lastPrompt = "Write the poem") => _service.SaveSettingsAsync(new SummarySettings
    {
        Prompt1 = "Describe the image", Prompt2 = "Interpret the image", Prompt3 = lastPrompt, TopPercent = percent
    }, CancellationToken.None);

    [TestMethod]
    public async Task ThreeRoundsKeepImageAndPreviousAnswersAndPersistEveryAnswer()
    {
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.HasCount(3, _model.Requests);
        var first = (JArray)_model.Requests[0]["messages"]!;
        var second = (JArray)_model.Requests[1]["messages"]!;
        var third = (JArray)_model.Requests[2]["messages"]!;
        Assert.HasCount(1, first);
        Assert.HasCount(3, second);
        Assert.HasCount(5, third);
        Assert.IsNotEmpty(first[0]["images"]![0]!.Value<string>()!);
        Assert.IsTrue(JToken.DeepEquals(first[0], third[0]));
        Assert.AreEqual("answer 1", third[1]["content"]!.Value<string>());
        Assert.AreEqual("answer 2", third[3]["content"]!.Value<string>());
        Assert.AreEqual("Write the poem", third[4]["content"]!.Value<string>());
        Assert.AreEqual("answer 3", await _service.GetSummaryAsync(1, CancellationToken.None));
        var responses = await _service.GetResponsesAsync(1, CancellationToken.None);
        Assert.AreEqual("answer 1", responses.Response1);
        Assert.AreEqual("answer 2", responses.Response2);
        Assert.AreEqual("answer 3", responses.Summary);
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.HasCount(3, _model.Requests, "A completed photo must not be regenerated on the next sweep.");
    }

    [TestMethod]
    public async Task RankUsesManualScoreIncludingZeroOtherwisePredictionAndRoundsUp()
    {
        await _context.Photos.Where(p => p.Id == 1).ExecuteUpdateAsync(u => u.SetProperty(p => p.IndependentScore, 0));
        await _context.Photos.Where(p => p.Id == 3).ExecuteUpdateAsync(u => u.SetProperty(p => p.IndependentScore, 6));
        await Save(.1);
        var target = await _service.GetTargetPhotosAsync(await _service.GetSettingsAsync(CancellationToken.None), CancellationToken.None);
        CollectionAssert.AreEqual(new[] { 3 }, await target.Select(p => p.Id).ToArrayAsync());
        await Save(20);
        target = await _service.GetTargetPhotosAsync(await _service.GetSettingsAsync(CancellationToken.None), CancellationToken.None);
        CollectionAssert.AreEqual(new[] { 3, 2 }, await target.Select(p => p.Id).ToArrayAsync());
    }

    [TestMethod]
    public async Task ExpandingFillsTargetAndShrinkingKeepsPoemsButPromptEditsInvalidateEverything()
    {
        await _service.RunOnceAsync(CancellationToken.None);
        var before = (await _service.GetSettingsAsync(CancellationToken.None)).SummaryPromptsModifiedAt;
        await Save(40);
        Assert.AreEqual(before, (await _service.GetSettingsAsync(CancellationToken.None)).SummaryPromptsModifiedAt);
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.AreEqual(4, await _context.Photos.CountAsync(p => p.Summary != null));
        await Save(10);
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.AreEqual(4, await _context.Photos.CountAsync(p => p.Summary != null));
        Assert.IsNotNull(await _service.GetSummaryAsync(4, CancellationToken.None));
        await Save(10, "A different poem");
        Assert.IsNull(await _service.GetSummaryAsync(1, CancellationToken.None));
        Assert.IsNull(await _service.GetSummaryAsync(4, CancellationToken.None));
        var expired = await _service.GetResponsesAsync(1, CancellationToken.None);
        Assert.IsNull(expired.Response1);
        Assert.IsNull(expired.Response2);
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.IsNotNull(await _service.GetSummaryAsync(1, CancellationToken.None));
        Assert.IsNull(await _service.GetSummaryAsync(4, CancellationToken.None));
    }

    [TestMethod]
    public async Task OldRequestFinishingAfterPromptEditCannotPublishItsPoem()
    {
        _model.OnRequest = async count =>
        {
            if (count == 3) await Save(10, "Changed while the last response was in flight");
        };
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.HasCount(3, _model.Requests);
        Assert.IsNull(await _service.GetSummaryAsync(1, CancellationToken.None));
        Assert.AreEqual(0, await _context.Photos.CountAsync(p => p.SummaryGeneratedAt != null));
        _model.OnRequest = null;
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.IsNotNull(await _service.GetSummaryAsync(1, CancellationToken.None));
    }

    [TestMethod]
    public async Task FailedPhotoDoesNotStarveLaterPhotosAndCanRetryNextSweep()
    {
        await Save(20);
        await _context.Photos.Where(p => p.Id == 1).ExecuteUpdateAsync(u => u.SetProperty(p => p.FilePath, "missing.jpg"));
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.IsNull(await _service.GetSummaryAsync(1, CancellationToken.None));
        Assert.IsNotNull(await _service.GetSummaryAsync(2, CancellationToken.None));
        await _context.Photos.Where(p => p.Id == 1).ExecuteUpdateAsync(u => u.SetProperty(p => p.FilePath, "test.png"));
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.IsNotNull(await _service.GetSummaryAsync(1, CancellationToken.None));
    }

    [TestMethod]
    public async Task RankingChangeFillsNewTargetEvenWhenEnoughOldPoemsExist()
    {
        await _service.RunOnceAsync(CancellationToken.None);
        await _context.Photos.Where(p => p.Id == 10).ExecuteUpdateAsync(u => u.SetProperty(p => p.IndependentScore, 6));
        await _context.Photos.Where(p => p.Id == 1).ExecuteUpdateAsync(u => u.SetProperty(p => p.IndependentScore, 0));
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.IsNotNull(await _service.GetSummaryAsync(10, CancellationToken.None));
        Assert.IsNotNull(await _service.GetSummaryAsync(1, CancellationToken.None));
    }

    [TestMethod]
    public async Task ZeroPercentPausesAndIdenticalPromptsKeepTheirTimestamp()
    {
        var before = (await _service.GetSettingsAsync(CancellationToken.None)).SummaryPromptsModifiedAt;
        await Save(0);
        Assert.AreEqual(before, (await _service.GetSettingsAsync(CancellationToken.None)).SummaryPromptsModifiedAt);
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.IsEmpty(_model.Requests);
    }

    [TestMethod]
    public async Task IncompleteOrEmptyModelAnswerDoesNotMarkPhotoAsCompleted()
    {
        _model.CompleteAnswer = false;
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.IsNull(await _service.GetSummaryAsync(1, CancellationToken.None));
        Assert.AreEqual(0, await _context.Photos.CountAsync(p => p.SummaryGeneratedAt != null));
    }

    [TestMethod]
    public async Task ConfiguredDatabaseCacheCannotHideNewPoemsOrReviveExpiredPoems()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAiurSqliteWithCache<AppDbContext>("DataSource=" + Path.Combine(_directory, "cached.db"), allowCache: true);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Albums.Add(new Album { AlbumId = "cached", Name = "Cached library" });
        db.Photos.Add(new Photo { FilePath = "test.png", AlbumId = "cached", EstimatedScore = 6 });
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PhotoRootPath"] = _directory, ["Summary:ApiKey"] = "synthetic-cache-test-key",
            ["Summary:Endpoint"] = "http://model.test/api/chat"
        }).Build();
        var service = new PhotoSummaryService(db, _model, config, Options.Create(config.GetSection("Summary").Get<SummaryOptions>()!), NullLogger<PhotoSummaryService>.Instance);
        await service.SaveSettingsAsync(new SummarySettings
        {
            Prompt1 = "one", Prompt2 = "two", Prompt3 = "three", TopPercent = 100
        }, CancellationToken.None);
        Assert.IsNull(await service.GetSummaryAsync(1, CancellationToken.None));
        await service.RunOnceAsync(CancellationToken.None);
        Assert.IsNotNull(await service.GetSummaryAsync(1, CancellationToken.None));
        await service.RunOnceAsync(CancellationToken.None);
        Assert.HasCount(3, _model.Requests);
        await service.SaveSettingsAsync(new SummarySettings
        {
            Prompt1 = "changed", Prompt2 = "two", Prompt3 = "three", TopPercent = 100
        }, CancellationToken.None);
        Assert.IsNull(await service.GetSummaryAsync(1, CancellationToken.None));
        await service.RunOnceAsync(CancellationToken.None);
        Assert.IsNotNull(await service.GetSummaryAsync(1, CancellationToken.None));
        Assert.HasCount(6, _model.Requests);
    }

    [TestMethod]
    public async Task EnvironmentOverridesJsonAndDrivesRealRequests()
    {
        var prefix = "PHOTO_TEST_" + Guid.NewGuid().ToString("N") + "_";
        var values = new Dictionary<string, string>
        {
            ["Summary__Endpoint"] = "https://env-model.test/custom/api/chat",
            ["Summary__Model"] = "custom-vision-model",
            ["Summary__ApiKey"] = "synthetic-env-token",
            ["Summary__TimeoutSeconds"] = "1200",
            ["Summary__MaxOutputTokens"] = "768",
            ["Summary__RetryDelaySeconds"] = "20"
        };
        try
        {
            foreach (var pair in values) Environment.SetEnvironmentVariable(prefix + pair.Key, pair.Value);
            using var json = new MemoryStream(Encoding.UTF8.GetBytes(
                """{"Summary":{"Endpoint":"https://json-model.test/api/chat","Model":"json-model","ApiKey":"json-key"}}"""));
            var configuration = new ConfigurationBuilder().AddJsonStream(json)
                .AddEnvironmentVariables(prefix).AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PhotoRootPath"] = _directory
                }).Build();
            var options = configuration.GetSection(SummaryOptions.SectionName).Get<SummaryOptions>()!;
            var service = new PhotoSummaryService(_context, _model, configuration, Options.Create(options),
                NullLogger<PhotoSummaryService>.Instance);
            _model.ExpectedModel = "custom-vision-model";
            await service.RunOnceAsync(CancellationToken.None);
            Assert.IsTrue(service.IsConfigured);
            Assert.HasCount(3, _model.Requests);
            Assert.AreEqual(values["Summary__Endpoint"], _model.LastEndpoint);
            Assert.AreEqual(values["Summary__ApiKey"], _model.LastToken);
            Assert.AreEqual(1200, options.TimeoutSeconds);
            Assert.AreEqual(20, options.RetryDelaySeconds);
            Assert.AreEqual(768, _model.Requests[0]["options"]!["num_predict"]!.Value<int>());
            Assert.IsNotNull(await service.GetSummaryAsync(1, CancellationToken.None));
        }
        finally
        {
            foreach (var key in values.Keys) Environment.SetEnvironmentVariable(prefix + key, null);
        }
    }

    [TestMethod]
    public void MissingOrInvalidConnectionSettingsKeepGenerationIdle()
    {
        Assert.IsFalse(new SummaryOptions().IsConfigured);
        Assert.IsFalse(new SummaryOptions { ApiKey = "test", Model = " " }.IsConfigured);
        Assert.IsFalse(new SummaryOptions { ApiKey = "test", Endpoint = "file:///tmp/model" }.IsConfigured);
    }

    [TestMethod]
    public async Task LegacyPoemStaysVisibleDuringBackfillAndFailureCannotPublishPartialAnswers()
    {
        var oldTime = DateTime.UtcNow;
        await _context.Photos.Where(p => p.Id == 1).ExecuteUpdateAsync(u =>
            u.SetProperty(p => p.Summary, "Existing final poem").SetProperty(p => p.SummaryGeneratedAt, oldTime));
        var legacy = await _service.GetResponsesAsync(1, CancellationToken.None);
        Assert.AreEqual("Existing final poem", legacy.Summary);
        Assert.IsNull(legacy.Response1);
        Assert.IsNull(legacy.Response2);
        _model.FailAt = 2;
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.HasCount(2, _model.Requests);
        var failed = await _service.GetResponsesAsync(1, CancellationToken.None);
        Assert.AreEqual("Existing final poem", failed.Summary);
        Assert.IsNull(failed.Response1);
        Assert.IsNull(failed.Response2);
        _model.FailAt = null;
        await _service.RunOnceAsync(CancellationToken.None);
        var complete = await _service.GetResponsesAsync(1, CancellationToken.None);
        Assert.AreEqual("answer 1", complete.Response1);
        Assert.AreEqual("answer 3", complete.Response2);
        Assert.AreEqual("answer 4", complete.Summary);
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.HasCount(4, _model.Requests);
    }

    [TestMethod]
    public async Task SharedBackendFailureBacksOffWithoutFloodingEveryPhoto()
    {
        await Save(20);
        _options.RetryDelaySeconds = 30;
        _model.FailFirst = true;
        await _service.RunOnceAsync(CancellationToken.None);
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.HasCount(1, _model.Requests);
        Assert.AreEqual(0, await _context.Photos.CountAsync(p => p.Summary != null));
        var state = _service.GetProgress((await _service.GetSettingsAsync(CancellationToken.None)).SummaryPromptsModifiedAt);
        Assert.AreEqual("retrying", state.Phase);
        StringAssert.Contains(state.Error!, "503");
        Assert.IsTrue(state.NextRetryAt > DateTime.UtcNow);
    }

    [TestMethod]
    public async Task FailedSecondRoundIsCheckpointedAndResumesInANewServiceScope()
    {
        _model.FailAt = 2;
        await _service.RunOnceAsync(CancellationToken.None);
        var saved = await _context.Photos.AsNoTracking().SingleAsync(p => p.Id == 1);
        Assert.AreEqual(1, saved.SummaryCompletedRounds);
        Assert.AreEqual("answer 1", saved.SummaryResponse1);
        Assert.IsNull(saved.SummaryGeneratedAt);
        Assert.IsNull((await _service.GetResponsesAsync(1, CancellationToken.None)).Response1);
        _model.FailAt = null;
        await using var restarted = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_context.Database.GetDbConnection()).Options);
        var service = new PhotoSummaryService(restarted, _model, _configuration, Options.Create(_options),
            NullLogger<PhotoSummaryService>.Instance, new SummaryProgress());
        await service.RunOnceAsync(CancellationToken.None);
        Assert.HasCount(4, _model.Requests, "Only rounds two and three should run after restart.");
        var messages = (JArray)_model.Requests[2]["messages"]!;
        Assert.HasCount(3, messages);
        Assert.AreEqual("answer 1", messages[1]["content"]!.Value<string>());
        Assert.IsNotEmpty(messages[0]["images"]![0]!.Value<string>()!);
        Assert.AreEqual("answer 4", await service.GetSummaryAsync(1, CancellationToken.None));
    }

    [TestMethod]
    public async Task ConfiguredDeadlineCancelsTheRoundAndPreservesEarlierAnswers()
    {
        _options.TimeoutSeconds = 1;
        _model.WaitAt = 2;
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.IsTrue(_model.CancellationObserved);
        Assert.AreEqual(1, (await _context.Photos.AsNoTracking().SingleAsync(p => p.Id == 1)).SummaryCompletedRounds);
        var status = _service.GetProgress((await _service.GetSettingsAsync(CancellationToken.None)).SummaryPromptsModifiedAt);
        StringAssert.Contains(status.Error!, "1 seconds");
        Assert.AreEqual("retrying", status.Phase);
        _model.WaitAt = null;
        _options.TimeoutSeconds = 900;
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.HasCount(4, _model.Requests);
        Assert.IsNotNull(await _service.GetSummaryAsync(1, CancellationToken.None));
    }

    [TestMethod]
    public async Task PromptEditCancelsInFlightRoundAndNeverReusesOldCheckpoints()
    {
        _model.OnRequest = async count => { if (count == 2) await Save(10, "New prompts"); };
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.IsNull(await _service.GetSummaryAsync(1, CancellationToken.None));
        var saved = await _context.Photos.AsNoTracking().SingleAsync(p => p.Id == 1);
        Assert.AreEqual("answer 1", saved.SummaryResponse1);
        Assert.AreNotEqual((await _service.GetSettingsAsync(CancellationToken.None)).SummaryPromptsModifiedAt,
            saved.SummaryCheckpointPromptsModifiedAt);
        _model.OnRequest = null;
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.HasCount(5, _model.Requests);
        Assert.HasCount(1, (JArray)_model.Requests[2]["messages"]!);
        Assert.AreEqual("answer 5", await _service.GetSummaryAsync(1, CancellationToken.None));
    }

    [TestMethod]
    public async Task OutputBudgetExhaustionCannotCompleteOrOverwriteASavedRound()
    {
        _model.LengthAt = 2;
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.IsNull(await _service.GetSummaryAsync(1, CancellationToken.None));
        var photo = await _context.Photos.AsNoTracking().SingleAsync(p => p.Id == 1);
        Assert.AreEqual(1, photo.SummaryCompletedRounds);
        Assert.IsNull(photo.SummaryResponse2);
        _model.LengthAt = null;
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.HasCount(4, _model.Requests);
        Assert.AreEqual("answer 1", (await _service.GetResponsesAsync(1, CancellationToken.None)).Response1);
    }

    [TestMethod]
    public async Task StreamingRequestsDisableReasoningAndUseConfiguredOutputBudget()
    {
        _options.MaxOutputTokens = 2048;
        await _service.RunOnceAsync(CancellationToken.None);
        foreach (var request in _model.Requests)
        {
            Assert.IsTrue(request["stream"]!.Value<bool>());
            Assert.IsFalse(request["think"]!.Value<bool>());
            Assert.AreEqual(2048, request["options"]!["num_predict"]!.Value<int>());
        }
        var status = _service.GetProgress((await _service.GetSettingsAsync(CancellationToken.None)).SummaryPromptsModifiedAt);
        Assert.AreEqual("completed", status.Phase);
        Assert.AreEqual(3, status.CompletedRounds);
        Assert.IsNotNull(status.FirstTokenAt);
        Assert.IsTrue(status.WaitingSeconds >= 0 && status.GeneratingSeconds >= 0);
    }

    [TestMethod]
    public async Task FailedThirdRoundResumesOnlyTheFinalQuestion()
    {
        _model.FailAt = 3;
        await _service.RunOnceAsync(CancellationToken.None);
        var photo = await _context.Photos.AsNoTracking().SingleAsync(p => p.Id == 1);
        Assert.AreEqual(2, photo.SummaryCompletedRounds);
        Assert.IsNull(photo.SummaryGeneratedAt);
        _model.FailAt = null;
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.HasCount(4, _model.Requests);
        Assert.HasCount(5, (JArray)_model.Requests[3]["messages"]!);
        var completed = await _service.GetResponsesAsync(1, CancellationToken.None);
        Assert.AreEqual("answer 1", completed.Response1);
        Assert.AreEqual("answer 2", completed.Response2);
        Assert.AreEqual("answer 4", completed.Summary);
    }

    private class RecordingModel : HttpMessageHandler, IHttpClientFactory
    {
        public List<JObject> Requests { get; } = [];
        public string ExpectedModel { get; set; } = "aiursoft-localization:latest";
        public string? LastEndpoint { get; private set; }
        public string? LastToken { get; private set; }
        public Func<int, Task>? OnRequest { get; set; }
        public bool FailFirst { get; set; }
        public int? FailAt { get; set; }
        public bool CompleteAnswer { get; set; } = true;
        public int? WaitAt { get; set; }
        public int? LengthAt { get; set; }
        public bool CancellationObserved { get; private set; }
        public HttpClient CreateClient(string name) => new(this, false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = JObject.Parse(await request.Content!.ReadAsStringAsync(ct));
            Requests.Add(body);
            Assert.AreEqual("Bearer", request.Headers.Authorization?.Scheme);
            Assert.AreEqual(ExpectedModel, body["model"]!.Value<string>());
            LastEndpoint = request.RequestUri?.AbsoluteUri;
            LastToken = request.Headers.Authorization?.Parameter;
            if (OnRequest != null) await OnRequest(Requests.Count);
            if (Requests.Count == WaitAt)
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { CancellationObserved = true; throw; }
            }
            if ((FailFirst && Requests.Count == 1) || Requests.Count == FailAt) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            var frames = new[]
            {
                new JObject { ["done"] = false, ["message"] = new JObject { ["content"] = "answer " } },
                new JObject { ["done"] = false, ["message"] = new JObject { ["content"] = Requests.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) } },
                new JObject
                {
                    ["done"] = CompleteAnswer,
                    ["done_reason"] = Requests.Count == LengthAt ? "length" : "stop",
                    ["message"] = new JObject { ["role"] = "assistant", ["content"] = "answer " + Requests.Count }
                }
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Join("\n", frames.Select(f => f.ToString(Newtonsoft.Json.Formatting.None))) + "\n",
                    Encoding.UTF8, "application/x-ndjson")
            };
        }
    }
}
