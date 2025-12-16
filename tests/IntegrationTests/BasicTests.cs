using System.Net;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Anduin.PhotoRanking.Data;
using static Aiursoft.WebTools.Extends;

[assembly:DoNotParallelize]

namespace Anduin.PhotoRanking.Tests.IntegrationTests;

[TestClass]
public class BasicTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public BasicTests()
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false
        };
        _port = Network.GetAvailablePort();
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://localhost:{_port}")
        };
    }

    [TestInitialize]
    public async Task CreateServer()
    {
        _server = await AppAsync<TestStartup>([], port: _port);
        await _server.UpdateDbAsync<AppDbContext>();
        await _server.StartAsync();
    }

    [TestCleanup]
    public async Task CleanServer()
    {
        if (_server == null) return;
        await _server.StopAsync();
        _server.Dispose();
        
        // Clean up any test database files
        var testDbFiles = Directory.GetFiles(".", "test-db-*.db*");
        foreach (var file in testDbFiles)
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [TestMethod]
    public async Task GetAlbumsApi()
    {
        var response = await _http.GetAsync("/api/albums");
        response.EnsureSuccessStatusCode();
        
        // Verify the response is JSON
        var content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.StartsWith("[") || content.StartsWith("{"), 
            "Response should be JSON");
    }
    
    [TestMethod]
    public async Task DatabaseIsAccessible()
    {
        // This test verifies that the database was properly migrated and can be queried
        var response = await _http.GetAsync("/api/albums");
        response.EnsureSuccessStatusCode();
        
        // Even with empty database, we should get a successful empty array response
        var content = await response.Content.ReadAsStringAsync();
        Assert.IsNotNull(content, "Response content should not be null");
    }
}
