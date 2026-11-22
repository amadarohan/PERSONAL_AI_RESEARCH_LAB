using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Infrastructure.Ai;

/// <summary>
/// Chat completion against any OpenAI-compatible endpoint (OpenAI, Azure OpenAI gateway,
/// LM Studio, vLLM, OpenRouter, and similar). Failures are returned, never thrown, so the UI
/// can explain exactly what is misconfigured.
/// </summary>
public sealed class OpenAiCompatibleChatProvider : IChatCompletionProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<OpenAiCompatibleChatProvider> _logger;

    public OpenAiCompatibleChatProvider(
        HttpClient http,
        string providerName,
        string modelName,
        string apiKey,
        DataRoute dataRoute,
        ILogger<OpenAiCompatibleChatProvider> logger)
    {
        _http = http;
        ProviderName = providerName;
        ModelName = modelName;
        _apiKey = apiKey;
        DataRoute = dataRoute;
        _logger = logger;
    }

    public string ProviderName { get; }

    public string ModelName { get; }

    public DataRoute DataRoute { get; }

    public async Task<AiChatResult> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model = ModelName,
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            temperature = request.Temperature,
            max_tokens = request.MaxOutputTokens,
            response_format = request.JsonMode ? new { type = "json_object" } : null
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload)
        };

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        }

        try
        {
            using var response = await _http.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Chat provider {Provider} returned {Status}", ProviderName, (int)response.StatusCode);
                return AiChatResult.Fail($"ارائه‌دهنده پاسخ HTTP {(int)response.StatusCode} داد: {Truncate(body, 400)}");
            }

            var parsed = JsonSerializer.Deserialize<ChatResponse>(body, JsonOptions);
            var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return AiChatResult.Fail("ارائه‌دهنده پاسخ خالی برگرداند.");
            }

            return new AiChatResult(
                true,
                content,
                parsed?.Usage?.PromptTokens ?? 0,
                parsed?.Usage?.CompletionTokens ?? 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat provider {Provider} call failed", ProviderName);
            return AiChatResult.Fail($"ارتباط با ارائه‌دهنده برقرار نشد: {ex.Message}");
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }

        [JsonPropertyName("usage")]
        public Usage? Usage { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public Message? Message { get; set; }
    }

    private sealed class Message
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private sealed class Usage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }
    }
}

/// <summary>Embeddings against an OpenAI-compatible <c>/embeddings</c> endpoint.</summary>
public sealed class OpenAiCompatibleEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<OpenAiCompatibleEmbeddingProvider> _logger;

    public OpenAiCompatibleEmbeddingProvider(
        HttpClient http,
        string providerName,
        string modelName,
        int dimensions,
        string apiKey,
        DataRoute dataRoute,
        ILogger<OpenAiCompatibleEmbeddingProvider> logger)
    {
        _http = http;
        ProviderName = providerName;
        ModelName = modelName;
        Dimensions = dimensions;
        _apiKey = apiKey;
        DataRoute = dataRoute;
        _logger = logger;
    }

    public string ProviderName { get; }

    public string ModelName { get; }

    public int Dimensions { get; }

    public DataRoute DataRoute { get; }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = JsonContent.Create(new { model = ModelName, input = inputs })
        };

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        }

        using var response = await _http.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"ارائه‌دهنده تعبیه برداری پاسخ HTTP {(int)response.StatusCode} داد: {body[..Math.Min(body.Length, 300)]}");
        }

        var parsed = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: cancellationToken);
        if (parsed?.Data is null || parsed.Data.Count != inputs.Count)
        {
            throw new InvalidOperationException("ارائه‌دهنده تعبیه برداری تعداد غیرمنتظره‌ای بردار برگرداند.");
        }

        _logger.LogDebug("Embedded {Count} input(s) with {Model}", inputs.Count, ModelName);
        return parsed.Data.OrderBy(d => d.Index).Select(d => d.Embedding).ToList();
    }

    private sealed class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingItem>? Data { get; set; }
    }

    private sealed class EmbeddingItem
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = Array.Empty<float>();
    }
}

/// <summary>
/// Ollama chat provider. Ollama is local by definition, so it is always <see cref="DataRoute.LocalOnly"/>.
/// </summary>
public sealed class OllamaChatProvider : IChatCompletionProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<OllamaChatProvider> _logger;

    public OllamaChatProvider(HttpClient http, string modelName, ILogger<OllamaChatProvider> logger)
    {
        _http = http;
        ModelName = modelName;
        _logger = logger;
    }

    public string ProviderName => "Ollama";

    public string ModelName { get; }

    public DataRoute DataRoute => DataRoute.LocalOnly;

    public async Task<AiChatResult> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model = ModelName,
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            stream = false,
            format = request.JsonMode ? "json" : null,
            options = new { temperature = request.Temperature, num_predict = request.MaxOutputTokens }
        };

        try
        {
            using var response = await _http.PostAsJsonAsync("api/chat", payload, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return AiChatResult.Fail($"Ollama پاسخ HTTP {(int)response.StatusCode} داد: {body[..Math.Min(body.Length, 300)]}");
            }

            using var document = JsonDocument.Parse(body);
            var content = document.RootElement.GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
            {
                return AiChatResult.Fail("Ollama پاسخ خالی برگرداند.");
            }

            var promptTokens = document.RootElement.TryGetProperty("prompt_eval_count", out var p) ? p.GetInt32() : 0;
            var completionTokens = document.RootElement.TryGetProperty("eval_count", out var c) ? c.GetInt32() : 0;

            return new AiChatResult(true, content, promptTokens, completionTokens);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ollama call failed");
            return AiChatResult.Fail($"ارتباط با Ollama برقرار نشد: {ex.Message}");
        }
    }
}

/// <summary>Ollama embeddings via <c>/api/embeddings</c>.</summary>
public sealed class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;

    public OllamaEmbeddingProvider(HttpClient http, string modelName, int dimensions)
    {
        _http = http;
        ModelName = modelName;
        Dimensions = dimensions;
    }

    public string ProviderName => "Ollama";

    public string ModelName { get; }

    public int Dimensions { get; }

    public DataRoute DataRoute => DataRoute.LocalOnly;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        var results = new List<float[]>(inputs.Count);
        foreach (var input in inputs)
        {
            using var response = await _http.PostAsJsonAsync(
                "api/embeddings", new { model = ModelName, prompt = input }, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"تعبیه برداری Ollama پاسخ HTTP {(int)response.StatusCode} داد: {body[..Math.Min(body.Length, 300)]}");
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var vector = document.RootElement.GetProperty("embedding")
                .EnumerateArray().Select(e => e.GetSingle()).ToArray();
            results.Add(vector);
        }

        return results;
    }
}

/// <summary>
/// Deterministic offline provider used by tests and by the demo environment when no real
/// provider is configured. It never fabricates research content: it echoes structured,
/// clearly-labelled placeholder output so the pipeline can be exercised end to end.
/// </summary>
public sealed class DeterministicFakeChatProvider : IChatCompletionProvider
{
    public string ProviderName => "Deterministic (offline)";

    public string ModelName => "fake-chat-v1";

    public DataRoute DataRoute => DataRoute.LocalOnly;

    public Task<AiChatResult> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        var lastUser = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? string.Empty;

        var content = request.JsonMode
            ? """{"note":"deterministic offline provider","items":[]}"""
            : $"[offline provider] No AI model is configured. Echo of the request: {lastUser[..Math.Min(lastUser.Length, 200)]}";

        return Task.FromResult(new AiChatResult(true, content, 0, 0));
    }
}

/// <summary>
/// Deterministic hashing embedder. Produces stable, normalized vectors so vector search is
/// exercised without any external service. Not semantically meaningful; documented as such.
/// </summary>
public sealed class DeterministicFakeEmbeddingProvider : IEmbeddingProvider
{
    public DeterministicFakeEmbeddingProvider(int dimensions = 256) => Dimensions = dimensions;

    public string ProviderName => "Deterministic (offline)";

    public string ModelName => "fake-embed-v1";

    public int Dimensions { get; }

    public DataRoute DataRoute => DataRoute.LocalOnly;

    public Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        var results = new List<float[]>(inputs.Count);
        foreach (var input in inputs)
        {
            var vector = new float[Dimensions];
            foreach (var token in input.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var hash = StableHash(token.ToLowerInvariant());
                var index = (int)(hash % (uint)Dimensions);
                vector[index] += 1f;
            }

            Normalize(vector);
            results.Add(vector);
        }

        return Task.FromResult<IReadOnlyList<float[]>>(results);
    }

    private static uint StableHash(string value)
    {
        // FNV-1a: stable across processes, unlike string.GetHashCode.
        var hash = 2166136261u;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return hash;
    }

    private static void Normalize(float[] vector)
    {
        var sum = 0.0;
        foreach (var value in vector)
        {
            sum += value * value;
        }

        var norm = Math.Sqrt(sum);
        if (norm <= 0)
        {
            return;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }
    }
}
