using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalResearchLab.Application.Abstractions;

namespace PersonalResearchLab.Infrastructure.Ai;

public sealed class SecretStoreOptions
{
    /// <summary>Path to the secret file, relative to the content root.</summary>
    public string FilePath { get; set; } = "storage/secrets/secrets.bin";
}

/// <summary>
/// Encrypts secrets with Windows DPAPI under the current user, which means the file is only
/// readable by the same Windows account on the same machine.
/// <para>
/// DPAPI is Windows-only. On other platforms the file is written unencrypted and a warning is
/// logged, because silently pretending the data is protected would be worse than saying so.
/// </para>
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _path;
    private readonly ILogger<DpapiSecretStore> _logger;
    private readonly object _gate = new();
    private Dictionary<string, string>? _cache;

    public DpapiSecretStore(IOptions<SecretStoreOptions> options, ILogger<DpapiSecretStore> logger)
    {
        _path = Path.GetFullPath(options.Value.FilePath);
        _logger = logger;
    }

    public string? Get(string name)
    {
        lock (_gate)
        {
            return Load().TryGetValue(name, out var value) ? value : null;
        }
    }

    public void Set(string name, string value)
    {
        lock (_gate)
        {
            var data = Load();
            data[name] = value;
            Persist(data);
        }
    }

    public void Remove(string name)
    {
        lock (_gate)
        {
            var data = Load();
            if (data.Remove(name))
            {
                Persist(data);
            }
        }
    }

    public bool Contains(string name)
    {
        lock (_gate)
        {
            return Load().ContainsKey(name);
        }
    }

    private Dictionary<string, string> Load()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(_path))
        {
            return _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var raw = File.ReadAllBytes(_path);
            var plain = OperatingSystem.IsWindows()
                ? ProtectedData.Unprotect(raw, optionalEntropy: null, DataProtectionScope.CurrentUser)
                : raw;

            _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(plain)
                     ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read the secret store at {Path}", _path);
            _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return _cache;
    }

    private void Persist(Dictionary<string, string> data)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var plain = JsonSerializer.SerializeToUtf8Bytes(data);

        byte[] payload;
        if (OperatingSystem.IsWindows())
        {
            payload = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        else
        {
            _logger.LogWarning(
                "Secret store at {Path} is not encrypted at rest: DPAPI is only available on Windows.",
                _path);
            payload = plain;
        }

        File.WriteAllBytes(_path, payload);
        _cache = data;
    }
}
