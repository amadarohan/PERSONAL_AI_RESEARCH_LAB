# AI providers and data routing

## 1. The one rule that matters

**A `LocalOnly` workspace is never served by a `CloudAllowed` provider.**

This is enforced in exactly one place — `AiProviderResolver.FindConfigurationAsync` — and no
caller can bypass it, because every AI call goes through the resolver.

```csharp
foreach (var candidate in candidates)
{
    var provider = candidate.AiProvider!;

    // A local-only workspace must never be served by a cloud provider.
    if (workspacePolicy == DataRoute.LocalOnly && provider.DataRoute == DataRoute.CloudAllowed)
    {
        _logger.LogDebug("Skipping cloud provider {Provider} for local-only workspace", provider.Name);
        continue;
    }

    return new ProviderConfig(candidate, provider);
}

return null;
```

When nothing satisfies the policy, resolution returns `null`. The caller must then degrade
honestly — the companion refuses, retrieval falls back to lexical-only. There is no silent
cloud fallback anywhere in the codebase.

---

## 2. Provider kinds

| Kind | Chat | Embedding | `DataRoute` | Notes |
| --- | --- | --- | --- | --- |
| `OpenAiCompatible` | yes | yes | as configured | Any `/v1/chat/completions` and `/v1/embeddings` endpoint |
| `Ollama` | yes | yes | always `LocalOnly` | Local daemon, default `http://localhost:11434/` |
| `AzureOpenAi` | **no** | **no** | as configured | Falls through to `LogUnsupported` — not implemented |
| `DeterministicFake` | yes | yes | always `LocalOnly` | Offline, deterministic, clearly labelled |

`AzureOpenAi` is a declared enum value with no implementation. Selecting it produces a
warning log and a `null` resolution, which surfaces as an honest refusal rather than a
confusing failure. This is stated on the settings page.

### `DeterministicFake`

The offline provider exists so the whole product can be exercised with no network and no
credentials. It is not a stub that pretends to be intelligent:

- `ProviderName` is `"Deterministic (offline)"`
- `ModelName` is `"fake-chat-v1"` / `"fake-embed-v1"`
- `DataRoute` is `LocalOnly`
- Output is deterministic and labelled as such in the UI

It is the recommended first-run choice.

---

## 3. Resolution order

`FindConfigurationAsync` orders candidates by:

1. `IsDefault` descending — an explicit default wins
2. `CreatedAtUtc` ascending — oldest first as a stable tiebreak

Then it walks the list and returns the first candidate whose provider is enabled and whose
`DataRoute` is compatible with the workspace policy.

`IsEnabled = false` removes a provider from consideration entirely, without deleting its
configuration. That is what the enable/disable toggle on the settings page does.

---

## 4. Secret handling

### The database never holds a key

`AiProvider.ApiKeySecretName` is a **reference**, not a value. The key itself lives in the
secret store, keyed by that name.

```
AiProvider.ApiKeySecretName = "provider:3f2a..."
ISecretStore.Get("provider:3f2a...") -> the actual key
```

`SettingsService` reports `HasSecret` (a boolean) and never returns the value. The settings
page shows "موجود" / "ناموجود", never the key.

### The secret store

`Infrastructure/Ai/SecretStore.cs` implements `ISecretStore` (declared in
`Application/Abstractions/ISecretStore.cs`).

| Platform | Behaviour |
| --- | --- |
| Windows | `ProtectedData.Protect` / `Unprotect` with `DataProtectionScope.CurrentUser` |
| Other | Plaintext, with a `LogWarning` on every write |

The container is a single file at `Secrets:FilePath` (default
`storage/secrets/secrets.bin`), holding a serialized dictionary. It is cached in memory after
first read.

`DataProtectionScope.CurrentUser` means the file is decryptable only by the Windows account
that wrote it. Copying it to another machine or another user produces an unreadable file, and
the store logs `LogError` and returns an empty dictionary rather than throwing — so a
corrupted secret store degrades to "no keys configured" instead of a startup crash.

### Why the interface lives in Application

`Application` must not reference `Infrastructure`. `SettingsService` needs to write secrets,
so the abstraction has to be visible to `Application`. Hence:

- interface: `src/PersonalResearchLab.Application/Abstractions/ISecretStore.cs`
- implementation: `src/PersonalResearchLab.Infrastructure/Ai/SecretStore.cs`

This is the canonical example of the dependency-inversion rule in this codebase.

---

## 5. HTTP client configuration

A single named client, `"ai"`, is registered in `AddAi`:

```csharp
services.AddHttpClient("ai", client =>
{
    client.Timeout = TimeSpan.FromSeconds(180);
    client.DefaultRequestHeaders.Add("User-Agent", "PersonalResearchLab/1.0");
});
```

`AiProviderResolver.CreateHttpClient` sets `BaseAddress` from `provider.BaseUrl`, appending a
trailing slash if missing. A single named client keeps connection pooling and any resilience
policy in one place.

`Microsoft.Extensions.Http.Resilience` is referenced for retry and circuit-breaker support.
Per-model `MaxRetries` (default 2) and `TimeoutSeconds` (default 120) are stored on
`AiModelConfiguration` for future use; the current providers rely on the client-level timeout.

---

## 6. Model configuration

`AiModelConfiguration` holds per-capability settings:

| Field | Default | Meaning |
| --- | --- | --- |
| `Capability` | — | `Chat`, `Embedding`, `Reranking`, `Ocr`, `Transcription` |
| `ModelName` | — | Provider-specific model identifier |
| `Temperature` | 0.2 | Low by default: research answers should be stable |
| `MaxOutputTokens` | 2048 | |
| `ContextWindow` | 8192 | |
| `TimeoutSeconds` | 120 | |
| `MaxRetries` | 2 | |
| `Dimensions` | 0 | Only meaningful for `Embedding`; 0 means "use 1536" |
| `IsDefault` | false | Exactly one default per capability |

`SettingsService.ClearDefaultAsync` enforces the single-default invariant: setting a new
default clears the previous one for the same capability in the same transaction.

`Dimensions` is forced to 0 for non-embedding capabilities in `CreateModelAsync`, because a
chat model has no vector width and a stray value would be misleading.

---

## 7. Audit trail

Every provider call can be recorded in `AiExecution`:

| Field | Purpose |
| --- | --- |
| `Purpose` | What the call was for |
| `ProviderName`, `ModelName` | Which model served it |
| `DataRoute` | **Proof of where the data went** |
| `Status` | Outcome |
| `PromptTokens`, `CompletionTokens` | Cost accounting |
| `DurationMs` | Latency |
| `Error` | Failure detail |
| `StartedAtUtc`, `CompletedAtUtc` | Timing |

`DataRoute` on the execution row is what lets you answer "did any content leave this machine,
and when" without trusting a log file.

---

## 8. Adding a provider kind

1. Add the value to `AiProviderKind` in `Domain/Common/Enums.cs`.
2. Implement `IChatCompletionProvider` and/or `IEmbeddingProvider` in
   `Infrastructure/Ai/AiProviders.cs`.
3. Add a case to `BuildChatProvider` and `BuildEmbeddingProvider` in `AiProviderResolver`.
4. Add a Persian label to `Labels.ProviderKindLabel`.
5. Document the `DataRoute` the provider reports.

Do not add a provider that reports `LocalOnly` while sending data off the machine. The
`DataRoute` value is a promise the resolver relies on.

---

## 9. What is not implemented

| Capability | Status | Where it is stated |
| --- | --- | --- |
| Reranking | `ResolveRerankerAsync` always returns `null` | Settings page, `docs/RETRIEVAL.md` |
| OCR | `UnavailableOcrProvider.IsAvailable => false` | Image extraction returns a warning |
| Transcription | Enum value exists, no provider | — |
| Azure OpenAI | Enum value exists, no implementation | Settings page |
| Web search | No `IWebSearchProvider` registered | Research page states evidence is local-only |
| Streaming responses | Not implemented; calls are request/response | — |
| Token counting | Stored when a provider reports it; not estimated locally | — |
