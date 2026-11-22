using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Application.Abstractions;

/// <summary>
/// Provider-independent chat completion. Implementations must never throw for a missing key;
/// they return a failed <see cref="AiChatResult"/> so the UI can explain what is unconfigured.
/// </summary>
public interface IChatCompletionProvider
{
    string ProviderName { get; }

    string ModelName { get; }

    DataRoute DataRoute { get; }

    Task<AiChatResult> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default);
}

public sealed record AiChatMessage(string Role, string Content);

/// <summary>
/// A chat request. <paramref name="JsonMode"/> asks the provider to return JSON only, which is
/// required for every structured extraction step.
/// </summary>
public sealed record AiChatRequest(
    IReadOnlyList<AiChatMessage> Messages,
    double Temperature = 0.2,
    int MaxOutputTokens = 2048,
    bool JsonMode = false);

public sealed record AiChatResult(
    bool Success,
    string Content,
    int PromptTokens,
    int CompletionTokens,
    string? Error = null)
{
    public static AiChatResult Fail(string error) => new(false, string.Empty, 0, 0, error);
}

public interface IEmbeddingProvider
{
    string ProviderName { get; }

    string ModelName { get; }

    int Dimensions { get; }

    DataRoute DataRoute { get; }

    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default);
}

/// <summary>Optional reranking. When absent, retrieval falls back to fused lexical + vector scores.</summary>
public interface IRerankingProvider
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<double>> RerankAsync(
        string query,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves which provider serves a capability for a workspace, honouring the workspace's
/// data-routing policy. This is the single place where "may this leave the machine?" is decided.
/// </summary>
public interface IAiProviderResolver
{
    Task<ResolvedChatProvider?> ResolveChatAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    Task<ResolvedEmbeddingProvider?> ResolveEmbeddingAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    Task<ResolvedReranker?> ResolveRerankerAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}

public sealed record ResolvedChatProvider(IChatCompletionProvider Provider, DataRoute DataRoute, string Description);

public sealed record ResolvedEmbeddingProvider(IEmbeddingProvider Provider, DataRoute DataRoute, string Description);

public sealed record ResolvedReranker(IRerankingProvider Provider, DataRoute DataRoute, string Description);

/// <summary>Web search adapter. Absence of a configured provider must be reported, never simulated.</summary>
public interface IWebSearchProvider
{
    bool IsConfigured { get; }

    string ProviderName { get; }

    Task<IReadOnlyList<WebSearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default);
}

public sealed record WebSearchHit(string Title, string Url, string Snippet, string? PublishedAt);
