using System.Net;
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
            ["PhotoRootPath"] = _directory
        }).Build();
        _service = new PhotoSummaryService(_context, _model, configuration, NullLogger<PhotoSummaryService>.Instance);
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
    public async Task ThreeRoundsKeepImageAndPreviousAnswersAndOnlyPersistFinalPoem()
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
        _model.FailFirst = true;
        await _service.RunOnceAsync(CancellationToken.None);
        Assert.IsNull(await _service.GetSummaryAsync(1, CancellationToken.None));
        Assert.IsNotNull(await _service.GetSummaryAsync(2, CancellationToken.None));
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
        var service = new PhotoSummaryService(db, _model, config, NullLogger<PhotoSummaryService>.Instance);
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

    private class RecordingModel : HttpMessageHandler, IHttpClientFactory
    {
        public List<JObject> Requests { get; } = [];
        public Func<int, Task>? OnRequest { get; set; }
        public bool FailFirst { get; set; }
        public bool CompleteAnswer { get; set; } = true;
        public HttpClient CreateClient(string name) => new(this, false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = JObject.Parse(await request.Content!.ReadAsStringAsync(ct));
            Requests.Add(body);
            Assert.AreEqual("Bearer", request.Headers.Authorization?.Scheme);
            Assert.AreEqual("aiursoft-thinking:latest", body["model"]!.Value<string>());
            if (OnRequest != null) await OnRequest(Requests.Count);
            if (FailFirst && Requests.Count == 1) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new JObject
                {
                    ["done"] = CompleteAnswer,
                    ["message"] = new JObject { ["role"] = "assistant", ["content"] = "answer " + Requests.Count }
                }.ToString(), Encoding.UTF8, "application/json")
            };
        }
    }
}
