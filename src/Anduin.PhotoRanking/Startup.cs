using Aiursoft.DbTools.Sqlite;
using Aiursoft.WebTools.Abstractions.Models;
using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Services;
using Anduin.PhotoRanking.Models;
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

        if (RankingSession.Settings.Read(configuration) is { } rankingSession)
        {
            services.AddSingleton(rankingSession);
            services.AddHttpClient<RankingSession>(client => client.Timeout = TimeSpan.FromSeconds(5))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
            services.AddAuthentication().AddCookie(RankingSession.Scheme, options =>
            {
                options.Cookie.Name = RankingSession.CookieName;
                options.Cookie.Path = "/";
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.HttpOnly = true;
                options.Cookie.MaxAge = RankingSession.Lifetime;
                options.ExpireTimeSpan = RankingSession.Lifetime;
                options.SlidingExpiration = false;
            });
        }

        services.AddScoped<ScoringService>();
        services.AddScoped<PersonalizedPredictionService>();
        services.AddScoped<SeederService>();
        services.AddScoped<ImageAnalysisService>();
        services.AddScoped<DedupService>();
        services.AddOptions<SummaryOptions>().Bind(configuration.GetSection(SummaryOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<SummaryProgress>();
        services.AddScoped<PhotoSummaryService>();
        services.Configure<TtsOptions>(configuration.GetSection(TtsOptions.SectionName));
        services.AddScoped<PhotoSpeechService>();

        services.AddHostedService<PredictorBackgroundService>();
        services.AddHostedService<SummaryBackgroundService>();

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
        if (app.Services.GetService<RankingSession.Settings>() is not null)
        {
            // Only a build with its session gate enabled listens on the public proxy's upstream port.
            app.Urls.Add("http://*:5000");
            app.Urls.Add($"http://*:{RankingSession.Port}");
            app.Use(async (context, next) =>
            {
                var session = context.RequestServices.GetRequiredService<RankingSession>();
                if (await session.AuthenticateAsync(context)) await next();
            });
        }

        // Revalidate the SPA shell so it references the assets from the current release.
        app.Use(async (context, next) =>
        {
            if (!Path.HasExtension(context.Request.Path.Value) ||
                string.Equals(Path.GetExtension(context.Request.Path.Value), ".html", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers.CacheControl = "no-cache";
            }
            await next();
        });
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
