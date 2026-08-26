using Aiursoft.DbTools.Sqlite;
using Aiursoft.WebTools.Abstractions.Models;
using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Services;
using Aiursoft.Canon;
using Microsoft.AspNetCore.Mvc.Razor;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Net;
using System.Net.Sockets;

namespace Anduin.PhotoRanking.Tests;

public class TestStartup : IWebStartup
{
    public void ConfigureServices(IConfiguration configuration, IWebHostEnvironment environment, IServiceCollection services)
    {
        // Use in-memory SQLite database with shared cache for tests
        var connectionString = "DataSource=test-db-" + Guid.NewGuid().ToString("N") + ".db";
        services.AddAiurSqliteWithCache<AppDbContext>(
            connectionString: connectionString,
            splitQuery: false,
            allowCache: false,
            onConnectionOpen: (conn) =>
            {
                if (conn is Microsoft.Data.Sqlite.SqliteConnection sqliteConn)
                {
                    SqliteVectorFunctions.RegisterVectorDistance(sqliteConn);
                }
            });

        // Services
        services.AddMemoryCache();
        services.AddHttpClient();
        services.AddTaskCanon();

        services.AddScoped<ScoringService>();
        services.AddScoped<PersonalizedPredictionService>();
        services.AddScoped<SeederService>();
        services.AddScoped<ImageAnalysisService>();

        // Controllers and localization
        services.AddControllersWithViews()
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
                options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            })
            .AddApplicationPart(typeof(Startup).Assembly)
            .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
            .AddDataAnnotationsLocalization();
    }

    public void Configure(WebApplication app)
    {
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapDefaultControllerRoute();
        app.MapFallbackToFile("index.html");
    }
}

public static class TestPortAllocator
{
    private static readonly object AllocationLock = new();
    private static readonly HashSet<int> AllocatedPorts = [];
    private static int _nextPort = 20_000 + Environment.ProcessId % 8_000;

    public static int GetAvailablePort()
    {
        lock (AllocationLock)
        {
            // Stay below Linux's default ephemeral range (32768+) so a port released
            // after probing cannot be stolen immediately by an unrelated outbound
            // connection while the test database is being migrated.
            for (var attempt = 0; attempt < 10_000; attempt++)
            {
                var port = 20_000 + _nextPort++ % 10_000;
                if (!AllocatedPorts.Add(port)) continue;

                try
                {
                    using var listener = new TcpListener(IPAddress.Any, port);
                    listener.Start();
                    listener.Stop();
                    return port;
                }
                catch (SocketException)
                {
                    // Another process owns this candidate; retain it in AllocatedPorts
                    // and advance so this test process never races on it again.
                }
            }
        }

        throw new InvalidOperationException("Could not allocate a unique integration-test port.");
    }
}
