using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Domain.Ai;
using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Infrastructure.Ai;

/// <summary>
/// Resolves which provider serves a capability for a workspace.
/// <para>
/// This is the only place in the system that decides whether content may leave the machine.
/// The rule is deliberately conservative: a cloud provider is used only when the workspace
/// explicitly allows cloud routing <em>and</em> the provider itself is marked cloud-capable.
/// When nothing satisfies the policy, resolution returns null and the caller must degrade
/// honestly rather than silently falling back to a cloud call.
/// </para>
/// </summary>
public sealed class AiProviderResolver : IAiProviderResolver
{
    private readonly IAppDbContext _db;
    private readonly ISecretStore _secrets;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<AiProviderResolver> _logger;

    public AiProviderResolver(
        IAppDbContext db,
        ISecretStore secrets,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        ILogger<AiProviderResolver> logger)
    {
        _db = db;
        _secrets = secrets;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public async Task<ResolvedChatProvider?> ResolveChatAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var policy = await GetWorkspaceRouteAsync(workspaceId, cancellationToken);
        var config = await FindConfigurationAsync(AiCapability.Chat, policy, cancellationToken);
        if (config is null)
        {
            return null;
        }

        var provider = BuildChatProvider(config);
        if (provider is null)
        {
            return null;
        }

        return new ResolvedChatProvider(
            provider,
            config.Provider.DataRoute,
            $"{config.Provider.Name} / {config.Model.ModelName}");
    }

    public async Task<ResolvedEmbeddingProvider?> ResolveEmbeddingAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var policy = await GetWorkspaceRouteAsync(workspaceId, cancellationToken);
        var config = await FindConfigurationAsync(AiCapability.Embedding, policy, cancellationToken);
        if (config is null)
        {
            return null;
        }

        var provider = BuildEmbeddingProvider(config);
        if (provider is null)
        {
            return null;
        }

        return new ResolvedEmbeddingProvider(
            provider,
            config.Provider.DataRoute,
            $"{config.Provider.Name} / {config.Model.ModelName}");
    }

    public Task<ResolvedReranker?> ResolveRerankerAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        // No reranking provider is implemented yet. Returning null is the honest answer and
        // makes retrieval fall back to fused lexical + vector scores.
        return Task.FromResult<ResolvedReranker?>(null);
    }

    private async Task<DataRoute> GetWorkspaceRouteAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var route = await _db.Workspaces
            .Where(w => w.Id == workspaceId)
            .Select(w => (DataRoute?)w.DataRoute)
            .FirstOrDefaultAsync(cancellationToken);

        return route ?? DataRoute.LocalOnly;
    }

    private async Task<ProviderConfig?> FindConfigurationAsync(
        AiCapability capability,
        DataRoute workspacePolicy,
        CancellationToken cancellationToken)
    {
        var candidates = await _db.AiModelConfigurations
            .Include(c => c.AiProvider)
            .Where(c => c.Capability == capability && c.AiProvider != null && c.AiProvider.IsEnabled)
            .OrderByDescending(c => c.IsDefault)
            .ThenBy(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            var provider = candidate.AiProvider!;

            // A local-only workspace must never be served by a cloud provider.
            if (workspacePolicy == DataRoute.LocalOnly && provider.DataRoute == DataRoute.CloudAllowed)
            {
                _logger.LogDebug(
                    "Skipping cloud provider {Provider} for local-only workspace", provider.Name);
                continue;
            }

            return new ProviderConfig(candidate, provider);
        }

        return null;
    }

    private IChatCompletionProvider? BuildChatProvider(ProviderConfig config)
    {
        var provider = config.Provider;
        var apiKey = ResolveApiKey(provider);

        return provider.Kind switch
        {
            AiProviderKind.OpenAiCompatible => new OpenAiCompatibleChatProvider(
                CreateHttpClient(provider),
                provider.Name,
                config.Model.ModelName,
                apiKey,
                provider.DataRoute,
                _loggerFactory.CreateLogger<OpenAiCompatibleChatProvider>()),

            AiProviderKind.Ollama => new OllamaChatProvider(
                CreateHttpClient(provider),
                config.Model.ModelName,
                _loggerFactory.CreateLogger<OllamaChatProvider>()),

            AiProviderKind.DeterministicFake => new DeterministicFakeChatProvider(),

            _ => LogUnsupported<IChatCompletionProvider>(provider)
        };
    }

    private IEmbeddingProvider? BuildEmbeddingProvider(ProviderConfig config)
    {
        var provider = config.Provider;
        var apiKey = ResolveApiKey(provider);
        var dimensions = config.Model.Dimensions > 0 ? config.Model.Dimensions : 1536;

        return provider.Kind switch
        {
            AiProviderKind.OpenAiCompatible => new OpenAiCompatibleEmbeddingProvider(
                CreateHttpClient(provider),
                provider.Name,
                config.Model.ModelName,
                dimensions,
                apiKey,
                provider.DataRoute,
                _loggerFactory.CreateLogger<OpenAiCompatibleEmbeddingProvider>()),

            AiProviderKind.Ollama => new OllamaEmbeddingProvider(
                CreateHttpClient(provider),
                config.Model.ModelName,
                dimensions),

            AiProviderKind.DeterministicFake => new DeterministicFakeEmbeddingProvider(dimensions),

            _ => LogUnsupported<IEmbeddingProvider>(provider)
        };
    }

    private T? LogUnsupported<T>(AiProvider provider) where T : class
    {
        _logger.LogWarning("Provider kind {Kind} is not supported for this capability", provider.Kind);
        return null;
    }

    private string ResolveApiKey(AiProvider provider)
    {
        if (string.IsNullOrWhiteSpace(provider.ApiKeySecretName))
        {
            return string.Empty;
        }

        return _secrets.Get(provider.ApiKeySecretName) ?? string.Empty;
    }

    private HttpClient CreateHttpClient(AiProvider provider)
    {
        var client = _httpClientFactory.CreateClient("ai");
        if (!string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            var baseUrl = provider.BaseUrl.EndsWith('/') ? provider.BaseUrl : provider.BaseUrl + "/";
            client.BaseAddress = new Uri(baseUrl);
        }

        return client;
    }

    private sealed record ProviderConfig(AiModelConfiguration Model, AiProvider Provider);
}
