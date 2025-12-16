using Aiursoft.DbTools.Sqlite;
using Aiursoft.WebTools.Abstractions.Models;
using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Services;
using Microsoft.AspNetCore.Mvc.Razor;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Anduin.PhotoRanking;

public class Startup : IWebStartup
{
    public void ConfigureServices(IConfiguration configuration, IWebHostEnvironment environment, IServiceCollection services)
    {
        // Relational database
        var connectionString = configuration.GetConnectionString("DefaultConnection")!;
        var allowCache = configuration.GetSection("ConnectionStrings:AllowCache").Get<bool>();
        services.AddAiurSqliteWithCache<AppDbContext>(
            connectionString,
            splitQuery: false,
            allowCache: allowCache);

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
                options.SerializerSettings.ContractResolver = new DefaultContractResolver();
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
