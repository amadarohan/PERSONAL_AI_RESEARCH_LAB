using Microsoft.EntityFrameworkCore;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Domain.Ai;
using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Application.Settings;

/// <summary>
/// Reads and writes AI provider configuration. This service never returns a secret value;
/// it only reports whether a secret entry exists, so the UI can show real state without
/// ever putting a key on screen.
/// </summary>
public sealed class SettingsService
{
    private readonly IAppDbContext _db;
    private readonly ISecretStore _secrets;

    public SettingsService(IAppDbContext db, ISecretStore secrets)
    {
        _db = db;
        _secrets = secrets;
    }

    public async Task<SettingsOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        // Materialize first: the secret store is not part of the query provider, so the
        // "does a secret exist" check must happen in memory.
        var providerRows = await _db.AiProviders
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Kind,
                p.BaseUrl,
                p.ApiKeySecretName,
                p.IsEnabled,
                p.DataRoute,
                p.LastValidatedAtUtc,
                p.LastValidationResult
            })
            .ToListAsync(cancellationToken);

        var providers = providerRows
            .Select(p => new ProviderView(
                p.Id,
                p.Name,
                p.Kind,
                p.BaseUrl,
                p.ApiKeySecretName,
                !string.IsNullOrWhiteSpace(p.ApiKeySecretName) && _secrets.Contains(p.ApiKeySecretName!),
                p.IsEnabled,
                p.DataRoute,
                p.LastValidatedAtUtc,
                p.LastValidationResult))
            .ToList();

        var models = await _db.AiModelConfigurations
            .AsNoTracking()
            .Include(m => m.AiProvider)
            .OrderBy(m => m.Capability)
            .ThenByDescending(m => m.IsDefault)
            .Select(m => new ModelView(
                m.Id,
                m.AiProviderId,
                m.AiProvider != null ? m.AiProvider.Name : "(حذف‌شده)",
                m.Capability,
                m.ModelName,
                m.Temperature,
                m.MaxOutputTokens,
                m.ContextWindow,
                m.TimeoutSeconds,
                m.MaxRetries,
                m.Dimensions,
                m.IsDefault))
            .ToListAsync(cancellationToken);

        return new SettingsOverview(providers, models);
    }

    public async Task<Guid> CreateProviderAsync(
        string name,
        AiProviderKind kind,
        string baseUrl,
        DataRoute dataRoute,
        string? apiKeySecretName,
        string? apiKeyValue,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("نام ارائه‌دهنده الزامی است.", nameof(name));
        }

        var provider = new AiProvider
        {
            Name = name.Trim(),
            Kind = kind,
            BaseUrl = baseUrl?.Trim() ?? string.Empty,
            DataRoute = dataRoute,
            IsEnabled = true
        };

        if (!string.IsNullOrWhiteSpace(apiKeyValue))
        {
            var secretName = string.IsNullOrWhiteSpace(apiKeySecretName)
                ? $"provider:{Guid.NewGuid():N}"
                : apiKeySecretName.Trim();

            _secrets.Set(secretName, apiKeyValue);
            provider.ApiKeySecretName = secretName;
        }
        else if (!string.IsNullOrWhiteSpace(apiKeySecretName))
        {
            provider.ApiKeySecretName = apiKeySecretName.Trim();
        }

        _db.AiProviders.Add(provider);
        await _db.SaveChangesAsync(cancellationToken);
        return provider.Id;
    }

    public async Task UpdateProviderAsync(
        Guid providerId,
        string name,
        AiProviderKind kind,
        string baseUrl,
        DataRoute dataRoute,
        bool isEnabled,
        string? apiKeySecretName,
        string? apiKeyValue,
        CancellationToken cancellationToken = default)
    {
        var provider = await _db.AiProviders.FirstOrDefaultAsync(p => p.Id == providerId, cancellationToken)
            ?? throw new InvalidOperationException("ارائه‌دهنده یافت نشد.");

        provider.Name = name.Trim();
        provider.Kind = kind;
        provider.BaseUrl = baseUrl?.Trim() ?? string.Empty;
        provider.DataRoute = dataRoute;
        provider.IsEnabled = isEnabled;

        if (!string.IsNullOrWhiteSpace(apiKeyValue))
        {
            var secretName = string.IsNullOrWhiteSpace(apiKeySecretName)
                ? provider.ApiKeySecretName ?? $"provider:{provider.Id:N}"
                : apiKeySecretName.Trim();

            _secrets.Set(secretName, apiKeyValue);
            provider.ApiKeySecretName = secretName;
        }
        else if (!string.IsNullOrWhiteSpace(apiKeySecretName))
        {
            provider.ApiKeySecretName = apiKeySecretName.Trim();
        }

        provider.Touch();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetProviderEnabledAsync(Guid providerId, bool enabled, CancellationToken cancellationToken = default)
    {
        var provider = await _db.AiProviders.FirstOrDefaultAsync(p => p.Id == providerId, cancellationToken)
            ?? throw new InvalidOperationException("ارائه‌دهنده یافت نشد.");

        provider.IsEnabled = enabled;
        provider.Touch();
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes a provider together with its model configurations. The stored secret is removed
    /// as well, because leaving an orphaned credential behind is a data-hygiene problem.
    /// </summary>
    public async Task DeleteProviderAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        var provider = await _db.AiProviders.FirstOrDefaultAsync(p => p.Id == providerId, cancellationToken)
            ?? throw new InvalidOperationException("ارائه‌دهنده یافت نشد.");

        var models = await _db.AiModelConfigurations
            .Where(m => m.AiProviderId == providerId)
            .ToListAsync(cancellationToken);

        _db.AiModelConfigurations.RemoveRange(models);
        _db.AiProviders.Remove(provider);
        await _db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(provider.ApiKeySecretName))
        {
            _secrets.Remove(provider.ApiKeySecretName);
        }
    }

    public async Task<Guid> CreateModelAsync(
        Guid providerId,
        AiCapability capability,
        string modelName,
        int dimensions,
        bool isDefault,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            throw new ArgumentException("نام مدل الزامی است.", nameof(modelName));
        }

        var providerExists = await _db.AiProviders
            .AnyAsync(p => p.Id == providerId, cancellationToken);

        if (!providerExists)
        {
            throw new InvalidOperationException("ارائه‌دهنده یافت نشد.");
        }

        if (isDefault)
        {
            await ClearDefaultAsync(capability, cancellationToken);
        }

        var model = new AiModelConfiguration
        {
            AiProviderId = providerId,
            Capability = capability,
            ModelName = modelName.Trim(),
            Dimensions = capability == AiCapability.Embedding ? Math.Max(1, dimensions) : 0,
            IsDefault = isDefault
        };

        _db.AiModelConfigurations.Add(model);
        await _db.SaveChangesAsync(cancellationToken);
        return model.Id;
    }

    public async Task SetDefaultModelAsync(Guid modelId, CancellationToken cancellationToken = default)
    {
        var model = await _db.AiModelConfigurations
            .FirstOrDefaultAsync(m => m.Id == modelId, cancellationToken)
            ?? throw new InvalidOperationException("پیکربندی مدل یافت نشد.");

        await ClearDefaultAsync(model.Capability, cancellationToken);
        model.IsDefault = true;
        model.Touch();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteModelAsync(Guid modelId, CancellationToken cancellationToken = default)
    {
        var model = await _db.AiModelConfigurations
            .FirstOrDefaultAsync(m => m.Id == modelId, cancellationToken)
            ?? throw new InvalidOperationException("پیکربندی مدل یافت نشد.");

        _db.AiModelConfigurations.Remove(model);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> SecretExistsAsync(string name) => Task.FromResult(_secrets.Contains(name));

    public Task SetSecretAsync(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("نام کلید الزامی است.", nameof(name));
        }

        _secrets.Set(name, value);
        return Task.CompletedTask;
    }

    public Task RemoveSecretAsync(string name)
    {
        _secrets.Remove(name);
        return Task.CompletedTask;
    }

    private async Task ClearDefaultAsync(AiCapability capability, CancellationToken cancellationToken)
    {
        var current = await _db.AiModelConfigurations
            .Where(m => m.Capability == capability && m.IsDefault)
            .ToListAsync(cancellationToken);

        foreach (var item in current)
        {
            item.IsDefault = false;
        }
    }
}

public sealed record SettingsOverview(
    IReadOnlyList<ProviderView> Providers,
    IReadOnlyList<ModelView> Models);

public sealed record ProviderView(
    Guid Id,
    string Name,
    AiProviderKind Kind,
    string BaseUrl,
    string? ApiKeySecretName,
    bool HasSecret,
    bool IsEnabled,
    DataRoute DataRoute,
    DateTime? LastValidatedAtUtc,
    string? LastValidationResult);

public sealed record ModelView(
    Guid Id,
    Guid AiProviderId,
    string ProviderName,
    AiCapability Capability,
    string ModelName,
    double Temperature,
    int MaxOutputTokens,
    int ContextWindow,
    int TimeoutSeconds,
    int MaxRetries,
    int Dimensions,
    bool IsDefault);
