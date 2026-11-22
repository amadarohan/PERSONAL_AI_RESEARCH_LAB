using Microsoft.EntityFrameworkCore;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Domain.Research;

namespace PersonalResearchLab.Application.Timeline;

/// <summary>
/// Workspace activity feed. Events are written by the services that perform the work, so the
/// timeline is a record of what actually happened rather than a separately maintained log.
/// </summary>
public sealed class TimelineService
{
    private readonly IAppDbContext _db;

    public TimelineService(IAppDbContext db) => _db = db;

    public async Task<TimelineView> GetAsync(
        Guid workspaceId,
        string? eventTypeFilter = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var query = _db.TimelineEvents.AsNoTracking().Where(e => e.WorkspaceId == workspaceId);
        if (!string.IsNullOrWhiteSpace(eventTypeFilter))
        {
            query = query.Where(e => e.EventType == eventTypeFilter);
        }

        var events = await query
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(take)
            .Select(e => new TimelineEventView(
                e.Id,
                e.EventType,
                e.Title,
                e.Detail,
                e.RelatedEntityId,
                e.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var types = await _db.TimelineEvents
            .AsNoTracking()
            .Where(e => e.WorkspaceId == workspaceId)
            .Select(e => e.EventType)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(cancellationToken);

        return new TimelineView(events, types);
    }

    public async Task AddAsync(
        Guid workspaceId,
        string eventType,
        string title,
        string? detail = null,
        Guid? relatedEntityId = null,
        CancellationToken cancellationToken = default)
    {
        _db.TimelineEvents.Add(new ResearchTimelineEvent
        {
            WorkspaceId = workspaceId,
            EventType = eventType,
            Title = title,
            Detail = detail,
            RelatedEntityId = relatedEntityId
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record TimelineView(
    IReadOnlyList<TimelineEventView> Events,
    IReadOnlyList<string> EventTypes);

public sealed record TimelineEventView(
    Guid Id,
    string EventType,
    string Title,
    string? Detail,
    Guid? RelatedEntityId,
    DateTime CreatedAtUtc);
