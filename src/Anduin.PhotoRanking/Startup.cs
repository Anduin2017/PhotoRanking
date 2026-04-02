using Aiursoft.DbTools.Sqlite;
using Aiursoft.WebTools.Abstractions.Models;
using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Services;
using Aiursoft.Canon;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.FileProviders;
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
            connectionString: connectionString,
            splitQuery: false,
            allowCache: allowCache,
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
        services.AddScoped<SeederService>();
        services.AddScoped<ImageAnalysisService>();
        services.AddScoped<UserPreferenceService>();

        services.AddHostedService<PredictorBackgroundService>();

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
        var distPath = Path.Combine(app.Environment.WebRootPath, "dist");
        if (Directory.Exists(distPath))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(distPath),
                RequestPath = ""
            });
        }
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapDefaultControllerRoute();
        app.MapFallbackToFile(Directory.Exists(distPath) ? "dist/index.html" : "index.html");
        
        // Validation: Ensure ONNX model exists
        var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "clip-visual.onnx");
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"ONNX Model not found at {modelPath}. Please run export_onnx.py script.", modelPath);
        }
    }
}
