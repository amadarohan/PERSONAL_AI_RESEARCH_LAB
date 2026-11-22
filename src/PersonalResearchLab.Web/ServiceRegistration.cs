using Microsoft.EntityFrameworkCore;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Application.Claims;
using PersonalResearchLab.Application.Companion;
using PersonalResearchLab.Application.Dashboard;
using PersonalResearchLab.Application.Ingestion;
using PersonalResearchLab.Application.Knowledge;
using PersonalResearchLab.Application.Learning;
using PersonalResearchLab.Application.Monitoring;
using PersonalResearchLab.Application.Reports;
using PersonalResearchLab.Application.Research;
using PersonalResearchLab.Application.Search;
using PersonalResearchLab.Application.Settings;
using PersonalResearchLab.Application.Sources;
using PersonalResearchLab.Application.Text;
using PersonalResearchLab.Application.Timeline;
using PersonalResearchLab.Application.Workspaces;
using PersonalResearchLab.Infrastructure.Ai;
using PersonalResearchLab.Infrastructure.Extraction;
using PersonalResearchLab.Infrastructure.Jobs;
using PersonalResearchLab.Infrastructure.Persistence;
using PersonalResearchLab.Infrastructure.Retrieval;
using PersonalResearchLab.Infrastructure.Storage;
using PersonalResearchLab.Web.Realtime;
using PersonalResearchLab.Web.Services;

namespace PersonalResearchLab.Web;

/// <summary>
/// Composition root. Registration order follows the dependency direction so a missing
/// registration fails at startup rather than at first use.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddResearchLab(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddPersistence(configuration);
        services.AddStorage(configuration, environment);
        services.AddTextProcessing();
        services.AddAi(configuration, environment);
        services.AddRetrieval();
        services.AddBackgroundWork();
        services.AddApplicationServices();
        services.AddRealtime();

        return services;
    }

    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is missing. Set ConnectionStrings:Default in appsettings.json " +
                "or the PERSONALRESEARCHLAB_CONNECTION environment variable.");

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), null);
                sql.CommandTimeout(120);
            });
        });

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
    }

    private static void AddStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<StorageOptions>(options =>
        {
            var configured = configuration["Storage:RootPath"];
            options.RootPath = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(environment.ContentRootPath, "storage", "files")
                : Path.GetFullPath(configured, environment.ContentRootPath);

            var maxBytes = configuration.GetValue<long?>("Storage:MaxFileSizeBytes");
            if (maxBytes is > 0)
            {
                options.MaxFileSizeBytes = maxBytes.Value;
            }
        });

        services.AddSingleton<IFileStorage, LocalFileStorage>();
    }

    private static void AddTextProcessing(this IServiceCollection services)
    {
        services.AddSingleton<ITextTokenizer, PersianEnglishTokenizer>();
        services.AddSingleton<ILanguageDetector, ScriptLanguageDetector>();
        services.AddSingleton<IOcrProvider, UnavailableOcrProvider>();

        services.AddSingleton<ITextExtractor, PdfTextExtractor>();
        services.AddSingleton<ITextExtractor, DocxTextExtractor>();
        services.AddSingleton<ITextExtractor, HtmlTextExtractor>();
        services.AddSingleton<ITextExtractor, PlainTextExtractor>();
        services.AddSingleton<ITextExtractor, ImageTextExtractor>();
    }

    private static void AddAi(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<SecretStoreOptions>(options =>
        {
            var configured = configuration["Secrets:FilePath"];
            options.FilePath = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(environment.ContentRootPath, "storage", "secrets", "secrets.bin")
                : Path.GetFullPath(configured, environment.ContentRootPath);
        });

        services.AddSingleton<ISecretStore, DpapiSecretStore>();

        // A single named client keeps connection pooling and resilience policy in one place.
        services.AddHttpClient("ai", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(180);
            client.DefaultRequestHeaders.Add("User-Agent", "PersonalResearchLab/1.0");
        });

        services.AddScoped<IAiProviderResolver, AiProviderResolver>();
        services.AddScoped<IEmbeddingCoordinator, EmbeddingCoordinator>();
    }

    private static void AddRetrieval(this IServiceCollection services)
    {
        services.AddScoped<IVectorIndex, SqlVectorIndex>();
        services.AddScoped<IRetrievalService, HybridRetrievalService>();
    }

    private static void AddBackgroundWork(this IServiceCollection services)
    {
        // The queue must be a singleton because it owns the channel the worker drains.
        services.AddSingleton<BackgroundJobQueue>();
        services.AddSingleton<IBackgroundJobQueue>(sp => sp.GetRequiredService<BackgroundJobQueue>());
        services.AddHostedService<IngestionWorker>();
    }

    private static void AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IngestionService>();
        services.AddScoped<WorkspaceService>();
        services.AddScoped<SourceService>();
        services.AddScoped<SearchService>();
        services.AddScoped<ResearchCompanionService>();
        services.AddScoped<DeepResearchService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<JobMonitorService>();
        services.AddScoped<ClaimsService>();
        services.AddScoped<KnowledgeGraphService>();
        services.AddScoped<ReportsService>();
        services.AddScoped<LearningService>();
        services.AddScoped<TimelineService>();
        services.AddScoped<SettingsService>();

        // UI-scoped state: the active workspace is a per-circuit concern, not a singleton.
        services.AddScoped<WorkspaceContext>();
    }

    private static void AddRealtime(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddScoped<IJobNotifier, SignalRJobNotifier>();
    }
}
