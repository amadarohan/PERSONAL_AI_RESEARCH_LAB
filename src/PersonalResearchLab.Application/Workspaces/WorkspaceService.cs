using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Domain.Common;
using PersonalResearchLab.Domain.Research;

namespace PersonalResearchLab.Application.Workspaces;

/// <summary>
/// Workspace lifecycle. A workspace is the unit of scoping for sources, search, AI context and
/// data-routing policy, so creation and deletion are the only operations that touch everything.
/// </summary>
public sealed class WorkspaceService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<WorkspaceService> _logger;

    public WorkspaceService(IAppDbContext db, ILogger<WorkspaceService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WorkspaceSummary>> ListAsync(
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Workspaces.AsNoTracking();
        if (!includeArchived)
        {
            query = query.Where(w => !w.IsArchived);
        }

        return await query
            .OrderBy(w => w.IsArchived)
            .ThenByDescending(w => w.UpdatedAtUtc)
            .Select(w => new WorkspaceSummary(
                w.Id,
                w.Name,
                w.Slug,
                w.Description,
                w.Objective,
                w.AccentColor,
                w.IsArchived,
                w.DataRoute,
                w.PreferredLanguage,
                _db.Sources.Count(s => s.WorkspaceId == w.Id && !s.IsArchived),
                _db.Claims.Count(c => c.WorkspaceId == w.Id),
                _db.KnowledgeNodes.Count(n => n.WorkspaceId == w.Id),
                _db.ResearchRuns.Count(r => r.WorkspaceId == w.Id),
                w.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<ResearchWorkspace?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public async Task<WorkspaceSummary?> GetSummaryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var list = await ListAsync(includeArchived: true, cancellationToken);
        return list.FirstOrDefault(w => w.Id == id);
    }

    public async Task<ResearchWorkspace> CreateAsync(
        CreateWorkspaceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("نام فضای کاری الزامی است.", nameof(request));
        }

        var slug = await EnsureUniqueSlugAsync(request.Name, cancellationToken);

        var workspace = new ResearchWorkspace
        {
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description?.Trim(),
            Objective = request.Objective?.Trim(),
            AccentColor = string.IsNullOrWhiteSpace(request.AccentColor) ? "#22D3EE" : request.AccentColor,
            PreferredLanguage = string.IsNullOrWhiteSpace(request.PreferredLanguage) ? "fa-IR" : request.PreferredLanguage,
            DataRoute = request.DataRoute,
            AiInstructions = request.AiInstructions?.Trim()
        };

        _db.Workspaces.Add(workspace);
        _db.TimelineEvents.Add(new ResearchTimelineEvent
        {
            WorkspaceId = workspace.Id,
            EventType = "workspace.created",
            Title = $"فضای کاری ساخته شد: {workspace.Name}",
            RelatedEntityId = workspace.Id
        });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Created workspace {WorkspaceId} ({Slug})", workspace.Id, workspace.Slug);
        return workspace;
    }

    public async Task UpdateAsync(
        Guid id,
        UpdateWorkspaceRequest request,
        CancellationToken cancellationToken = default)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("فضای کاری یافت نشد.");

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            workspace.Name = request.Name.Trim();
        }

        workspace.Description = request.Description?.Trim();
        workspace.Objective = request.Objective?.Trim();
        workspace.AiInstructions = request.AiInstructions?.Trim();
        workspace.DataRoute = request.DataRoute;
        workspace.AllowCrossWorkspaceAccess = request.AllowCrossWorkspaceAccess;

        if (!string.IsNullOrWhiteSpace(request.AccentColor))
        {
            workspace.AccentColor = request.AccentColor;
        }

        if (!string.IsNullOrWhiteSpace(request.PreferredLanguage))
        {
            workspace.PreferredLanguage = request.PreferredLanguage;
        }

        workspace.Touch();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("فضای کاری یافت نشد.");

        workspace.IsArchived = archived;
        workspace.Touch();
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes a workspace and everything scoped to it. This is destructive and irreversible;
    /// callers must confirm with the user first.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("فضای کاری یافت نشد.");

        _db.Workspaces.Remove(workspace);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogWarning("Deleted workspace {WorkspaceId} ({Name})", id, workspace.Name);
    }

    private async Task<string> EnsureUniqueSlugAsync(string name, CancellationToken cancellationToken)
    {
        var baseSlug = Slugify(name);
        if (string.IsNullOrWhiteSpace(baseSlug))
        {
            baseSlug = "workspace";
        }

        var slug = baseSlug;
        var suffix = 2;
        while (await _db.Workspaces.AnyAsync(w => w.Slug == slug, cancellationToken))
        {
            slug = $"{baseSlug}-{suffix++}";
        }

        return slug;
    }

    private static string Slugify(string value)
    {
        var lowered = value.Trim().ToLowerInvariant();
        var replaced = Regex.Replace(lowered, @"[^\p{L}\p{Nd}]+", "-");
        return replaced.Trim('-');
    }
}

public sealed record WorkspaceSummary(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? Objective,
    string AccentColor,
    bool IsArchived,
    DataRoute DataRoute,
    string PreferredLanguage,
    int SourceCount,
    int ClaimCount,
    int NodeCount,
    int RunCount,
    DateTime UpdatedAtUtc);

public sealed record CreateWorkspaceRequest(
    string Name,
    string? Description = null,
    string? Objective = null,
    string? AccentColor = null,
    string? PreferredLanguage = null,
    DataRoute DataRoute = DataRoute.LocalOnly,
    string? AiInstructions = null);

public sealed record UpdateWorkspaceRequest(
    string? Name = null,
    string? Description = null,
    string? Objective = null,
    string? AccentColor = null,
    string? PreferredLanguage = null,
    DataRoute DataRoute = DataRoute.LocalOnly,
    bool AllowCrossWorkspaceAccess = false,
    string? AiInstructions = null);
