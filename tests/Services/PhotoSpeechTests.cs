using System.Net;
using Anduin.PhotoRanking.Controllers;
using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Anduin.PhotoRanking.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Anduin.PhotoRanking.Tests.Services;

[TestClass]
public class PhotoSpeechTests
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private AppDbContext _db = null!;
    private readonly SpeechGateway _gateway = new();
    private readonly TtsOptions _options = new() { ApiKey = "synthetic-tts-key" };
    private SummaryController _controller = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        await _connection.OpenAsync();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        _db.Albums.Add(new Album { AlbumId = "test", Name = "Test" });
        _db.Photos.Add(new Photo { AlbumId = "test", FilePath = "synthetic.png", Summary = "第三轮诗歌", SummaryGeneratedAt = DateTime.UtcNow });
        _db.SystemStates.Add(new SystemState());
        await _db.SaveChangesAsync();
        var summary = new PhotoSummaryService(_db, _gateway, new ConfigurationBuilder().Build(),
            Options.Create(new SummaryOptions()), NullLogger<PhotoSummaryService>.Instance);
        _controller = new SummaryController(summary, _db, new PhotoSpeechService(_gateway, Options.Create(_options)),
            NullLogger<SummaryController>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        _connection.Dispose();
        _gateway.Dispose();
    }

    private Task<IActionResult> Speak(string text = "第三轮诗歌", int id = 1)
        => _controller.GetSpeech(id, new SummarySpeechRequest { Summary = text }, CancellationToken.None);

    [TestMethod]
    public async Task CurrentFinalAnswerUsesQueuedGatewayAndReturnsAudioWithoutCredentials()
    {
        var result = (FileContentResult)await Speak();
        Assert.AreEqual("audio/wav", result.ContentType);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, result.FileContents);
        Assert.AreEqual("Bearer synthetic-tts-key", _gateway.Authorization);
        Assert.AreEqual("https://tts.aiursoft.com/api/jobs", _gateway.Endpoint);
        StringAssert.Contains(_gateway.Form, "\"voice\":\"broadcasting\"");
        Assert.AreEqual("第三轮诗歌", System.Text.Json.JsonDocument.Parse(_gateway.Form).RootElement.GetProperty("text").GetString());
        _options.Voice = "another-profile";
        await Speak();
        StringAssert.Contains(_gateway.Form, "\"voice\":\"another-profile\"");
    }

    [TestMethod]
    public async Task RejectsMissingExpiredAndClientSuppliedTextWithoutCallingTts()
    {
        Assert.IsInstanceOfType<NotFoundResult>(await Speak(id: 99));
        Assert.IsInstanceOfType<ConflictObjectResult>(await Speak("arbitrary client text"));
        _db.SystemStates.Single().SummaryPromptsModifiedAt = DateTime.UtcNow.AddMinutes(1);
        await _db.SaveChangesAsync();
        Assert.IsInstanceOfType<NotFoundResult>(await Speak());
        Assert.AreEqual(0, _gateway.Calls);
    }

    [TestMethod]
    public async Task MissingConfigurationDoesNotCallTts()
    {
        _options.ApiKey = "";
        Assert.AreEqual(503, ((ObjectResult)await Speak()).StatusCode);
        Assert.AreEqual(0, _gateway.Calls);
    }

    [TestMethod]
    public async Task PromptEditDuringGenerationDiscardsAudio()
    {
        _gateway.BeforeResponse = async () =>
        {
            _db.SystemStates.Single().SummaryPromptsModifiedAt = DateTime.UtcNow.AddMinutes(1);
            await _db.SaveChangesAsync();
        };
        Assert.IsInstanceOfType<ConflictObjectResult>(await Speak());
    }

    [TestMethod]
    public async Task GatewayFailureReturnsGenericError()
    {
        _gateway.Status = HttpStatusCode.Unauthorized;
        var result = (ObjectResult)await Speak();
        Assert.AreEqual(502, result.StatusCode);
        Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(result.Value).Contains(_options.ApiKey));
    }

    [TestMethod]
    [DataRow("text/html", false)]
    [DataRow("audio/wav", true)]
    public async Task InvalidOrEmptyAudioIsRejected(string type, bool empty)
    {
        _gateway.ContentType = type;
        _gateway.Empty = empty;
        Assert.AreEqual(502, ((ObjectResult)await Speak()).StatusCode);
    }

    [TestMethod]
    public async Task UpstreamTimeoutReturnsRetryableError()
    {
        _gateway.BeforeResponse = () => throw new TaskCanceledException();
        Assert.AreEqual(504, ((ObjectResult)await Speak()).StatusCode);
    }

    [TestMethod]
    public async Task QueuedJobSubmissionReturnsWithoutWaitingAndCanPollAndDownload()
    {
        _gateway.JobStatus = "queued";
        var request = new SummarySpeechRequest { Summary = "第三轮诗歌" };
        var submitted = (ObjectResult)await _controller.CreateSpeechJob(1, request, CancellationToken.None);
        Assert.AreEqual(202, submitted.StatusCode);
        var job = (SpeechJob)submitted.Value!;
        Assert.AreEqual("queued", job.Status);
        Assert.AreEqual(2, job.QueuePosition);
        Assert.AreEqual(1, _gateway.Calls);
        _gateway.JobStatus = "running";
        var result = (SpeechJob)((OkObjectResult)await _controller.GetSpeechJob(1, job.Id, request, CancellationToken.None)).Value!;
        Assert.AreEqual("running", result.Status);
        Assert.AreEqual(1, result.CompletedSegments);
        Assert.AreEqual(3, result.TotalSegments);
        _gateway.JobStatus = "completed";
        Assert.IsInstanceOfType<FileContentResult>(await _controller.GetSpeechAudio(1, job.Id, request, CancellationToken.None));
    }

    [TestMethod]
    public async Task JobHandlesAreBoundToPhotoAndSummaryAndCannotContainArbitraryPaths()
    {
        var request = new SummarySpeechRequest { Summary = "第三轮诗歌" };
        var job = (SpeechJob)((ObjectResult)await _controller.CreateSpeechJob(1, request, CancellationToken.None)).Value!;
        var calls = _gateway.Calls;
        Assert.IsInstanceOfType<NotFoundResult>(await _controller.GetSpeechJob(1, "../../other", request, CancellationToken.None));
        Assert.IsInstanceOfType<NotFoundResult>(await _controller.GetSpeechAudio(1, job.Id[..^1] + "Z", request, CancellationToken.None));
        _db.Photos.Add(new Photo { AlbumId = "test", FilePath = "second.png", Summary = request.Summary, SummaryGeneratedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        Assert.IsInstanceOfType<NotFoundResult>(await _controller.GetSpeechJob(2, job.Id, request, CancellationToken.None));
        Assert.AreEqual(calls, _gateway.Calls);
    }

    [TestMethod]
    public async Task PromptChangesInvalidateJobPollingAndAudio()
    {
        var request = new SummarySpeechRequest { Summary = "第三轮诗歌" };
        var job = (SpeechJob)((ObjectResult)await _controller.CreateSpeechJob(1, request, CancellationToken.None)).Value!;
        _db.SystemStates.Single().SummaryPromptsModifiedAt = DateTime.UtcNow.AddMinutes(1);
        await _db.SaveChangesAsync();
        var calls = _gateway.Calls;
        Assert.IsInstanceOfType<NotFoundResult>(await _controller.GetSpeechJob(1, job.Id, request, CancellationToken.None));
        Assert.IsInstanceOfType<NotFoundResult>(await _controller.GetSpeechAudio(1, job.Id, request, CancellationToken.None));
        Assert.AreEqual(calls, _gateway.Calls);
    }

    [TestMethod]
    public async Task QueueCapacityAndExpiredHandlesPreserveRetryableStatuses()
    {
        var request = new SummarySpeechRequest { Summary = "第三轮诗歌" };
        _gateway.Status = HttpStatusCode.TooManyRequests;
        Assert.AreEqual(429, ((ObjectResult)await _controller.CreateSpeechJob(1, request, CancellationToken.None)).StatusCode);
        _gateway.Status = HttpStatusCode.OK;
        var job = (SpeechJob)((ObjectResult)await _controller.CreateSpeechJob(1, request, CancellationToken.None)).Value!;
        _gateway.Status = HttpStatusCode.Gone;
        Assert.AreEqual(410, ((ObjectResult)await _controller.GetSpeechAudio(1, job.Id, request, CancellationToken.None)).StatusCode);
    }

    [TestMethod]
    public async Task InvalidGatewayJobIsRejectedBeforeItCanBecomeAClientHandle()
    {
        _gateway.JobId = "../../other";
        Assert.AreEqual(502, ((ObjectResult)await _controller.CreateSpeechJob(1,
            new SummarySpeechRequest { Summary = "第三轮诗歌" }, CancellationToken.None)).StatusCode);
    }

    private sealed class SpeechGateway : HttpMessageHandler, IHttpClientFactory
    {
        public string JobStatus { get; set; } = "completed";
        public string JobId { get; set; } = new('a', 32);
        public int Calls { get; private set; }
        public string Form { get; private set; } = "";
        public string? Authorization { get; private set; }
        public string? Endpoint { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string ContentType { get; set; } = "audio/wav";
        public bool Empty { get; set; }
        public Func<Task>? BeforeResponse { get; set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Authorization = request.Headers.Authorization?.ToString();
            if (request.Method == HttpMethod.Post)
            {
                Endpoint = request.RequestUri?.AbsoluteUri;
                Form = await request.Content!.ReadAsStringAsync(ct);
            }
            if (BeforeResponse != null) await BeforeResponse();
            if (!request.RequestUri!.AbsolutePath.EndsWith("/audio", StringComparison.Ordinal))
                return new HttpResponseMessage(Status) { Content = JsonContent.Create(new
                {
                    id = JobId, status = JobStatus, queue_position = 2, completed_segments = 1, total_segments = 3
                }) };
            var result = new HttpResponseMessage(Status) { Content = new ByteArrayContent(Empty ? [] : [1, 2, 3]) };
            result.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(ContentType);
            return result;
        }
    }
}
