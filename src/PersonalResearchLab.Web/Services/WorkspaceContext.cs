using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using PersonalResearchLab.Application.Workspaces;

namespace PersonalResearchLab.Web.Services;

/// <summary>
/// Holds the workspace the user is currently working in. Every page that needs a workspace
/// reads it from here instead of inventing its own selection, so the whole app stays in one
/// scope at a time and the choice survives a reload.
/// </summary>
public sealed class WorkspaceContext
{
    private const string StorageKey = "prl.activeWorkspace";
    private readonly ProtectedLocalStorage _storage;
    private readonly WorkspaceService _workspaces;
    private readonly ILogger<WorkspaceContext> _logger;

    private IReadOnlyList<WorkspaceSummary> _available = Array.Empty<WorkspaceSummary>();
    private bool _loaded;

    public WorkspaceContext(
        ProtectedLocalStorage storage,
        WorkspaceService workspaces,
        ILogger<WorkspaceContext> logger)
    {
        _storage = storage;
        _workspaces = workspaces;
        _logger = logger;
    }

    /// <summary>Raised whenever the active workspace changes so pages can refresh.</summary>
    public event Func<Task>? Changed;

    public Guid? ActiveWorkspaceId { get; private set; }

    public WorkspaceSummary? Active =>
        ActiveWorkspaceId is null ? null : _available.FirstOrDefault(w => w.Id == ActiveWorkspaceId);

    public IReadOnlyList<WorkspaceSummary> Available => _available;

    public bool IsLoaded => _loaded;

    /// <summary>Loads the workspace list once and restores the last selection if it still exists.</summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return;
        }

        _available = await _workspaces.ListAsync(includeArchived: false, cancellationToken);

        try
        {
            var stored = await _storage.GetAsync<Guid>(StorageKey);
            if (stored.Success && stored.Value != Guid.Empty && _available.Any(w => w.Id == stored.Value))
            {
                ActiveWorkspaceId = stored.Value;
            }
        }
        catch (Exception ex)
        {
            // Protected browser storage is unavailable during prerendering; that is expected.
            _logger.LogDebug(ex, "Could not read the stored workspace selection.");
        }

        ActiveWorkspaceId ??= _available.FirstOrDefault()?.Id;
        _loaded = true;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _available = await _workspaces.ListAsync(includeArchived: false, cancellationToken);

        if (ActiveWorkspaceId is not null && _available.All(w => w.Id != ActiveWorkspaceId))
        {
            ActiveWorkspaceId = _available.FirstOrDefault()?.Id;
        }

        ActiveWorkspaceId ??= _available.FirstOrDefault()?.Id;
        _loaded = true;
        await NotifyChangedAsync();
    }

    public async Task SetActiveAsync(Guid workspaceId)
    {
        if (ActiveWorkspaceId == workspaceId)
        {
            return;
        }

        ActiveWorkspaceId = workspaceId;

        try
        {
            await _storage.SetAsync(StorageKey, workspaceId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not persist the workspace selection.");
        }

        await NotifyChangedAsync();
    }

    private async Task NotifyChangedAsync()
    {
        if (Changed is not null)
        {
            await Changed.Invoke();
        }
    }
}
