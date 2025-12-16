using Aiursoft.DbTools.Sqlite;
using Aiursoft.WebTools.Abstractions.Models;
using Anduin.PhotoRanking;
using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Services;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Anduin.PhotoRanking.Tests;

public class TestStartup : IWebStartup
{
    public void ConfigureServices(IConfiguration configuration, IWebHostEnvironment environment, IServiceCollection services)
    {
        // Use in-memory SQLite database with shared cache for tests
        var connectionString = "DataSource=test-db-" + Guid.NewGuid().ToString("N") + ".db";
        services.AddAiurSqliteWithCache<AppDbContext>(
            connectionString,
            splitQuery: false,
            allowCache: false);

        // Services
        services.AddMemoryCache();
        services.AddHttpClient();

        services.AddScoped<ScoringService>();
        services.AddScoped<SeederService>();

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
