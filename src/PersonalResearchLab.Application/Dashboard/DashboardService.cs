using Microsoft.EntityFrameworkCore;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Application.Dashboard;

/// <summary>
/// Command-center metrics. Every number here is a direct count over real rows and carries a
/// definition, because a dashboard full of unexplained figures trains the user to ignore it.
/// </summary>
public sealed class DashboardService
{
    private readonly IAppDbContext _db;

    public DashboardService(IAppDbContext db) => _db = db;

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var workspaces = await _db.Workspaces
            .AsNoTracking()
            .Where(w => !w.IsArchived)
            .OrderByDescending(w => w.UpdatedAtUtc)
            .Select(w => new DashboardWorkspace(
                w.Id,
                w.Name,
                w.AccentColor,
                w.Objective,
                _db.Sources.Count(s => s.WorkspaceId == w.Id && !s.IsArchived),
                _db.Claims.Count(c => c.WorkspaceId == w.Id),
                _db.KnowledgeNodes.Count(n => n.WorkspaceId == w.Id),
                _db.ResearchRuns.Count(r => r.WorkspaceId == w.Id),
                w.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        var metrics = new DashboardMetrics(
            WorkspaceCount: workspaces.Count,
            SourceCount: await _db.Sources.CountAsync(s => !s.IsArchived, cancellationToken),
            DocumentCount: await _db.Documents.CountAsync(cancellationToken),
            ChunkCount: await _db.DocumentChunks.CountAsync(cancellationToken),
            EmbeddedChunkCount: await _db.DocumentChunks.CountAsync(c => c.IsEmbedded, cancellationToken),
            ClaimCount: await _db.Claims.CountAsync(cancellationToken),
            UnreviewedClaimCount: await _db.Claims.CountAsync(c => c.Status == ClaimStatus.Unreviewed, cancellationToken),
            ContestedClaimCount: await _db.Claims.CountAsync(
                c => c.Status == ClaimStatus.Contested || c.Status == ClaimStatus.Contradicted, cancellationToken),
            NodeCount: await _db.KnowledgeNodes.CountAsync(cancellationToken),
            RelationshipCount: await _db.KnowledgeRelationships.CountAsync(cancellationToken),
            OpenConflictCount: await _db.ClaimConflicts.CountAsync(c => !c.IsResolved, cancellationToken),
            ActiveJobCount: await _db.ProcessingJobs.CountAsync(
                j => j.Status == ProcessingStatus.Pending || j.Status == ProcessingStatus.Running, cancellationToken),
            FailedJobCount: await _db.ProcessingJobs.CountAsync(
                j => j.Status == ProcessingStatus.Failed, cancellationToken),
            ActiveRunCount: await _db.ResearchRuns.CountAsync(
                r => r.Status == ResearchRunStatus.Running || r.Status == ResearchRunStatus.AwaitingApproval,
                cancellationToken),
            ReportCount: await _db.ResearchReports.CountAsync(cancellationToken));

        var recentJobs = await _db.ProcessingJobs
            .AsNoTracking()
            .OrderByDescending(j => j.CreatedAtUtc)
            .Take(8)
            .Select(j => new DashboardJob(
                j.Id,
                j.WorkspaceId,
                j.Title,
                j.JobType,
                j.Status,
                j.ProgressPercent,
                j.CurrentStage,
                j.FailureReason,
                j.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var recentEvents = await _db.TimelineEvents
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(12)
            .Select(e => new DashboardEvent(
                e.Id,
                e.WorkspaceId,
                e.EventType,
                e.Title,
                e.Detail,
                e.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var openQuestions = await _db.ResearchQuestions
            .AsNoTracking()
            .Where(q => !q.IsAnswered)
            .OrderBy(q => q.SortOrder)
            .Take(8)
            .Select(q => new DashboardQuestion(q.Id, q.WorkspaceId, q.Text, q.IsAiGenerated))
            .ToListAsync(cancellationToken);

        return new DashboardSnapshot(metrics, workspaces, recentJobs, recentEvents, openQuestions);
    }
}

public sealed record DashboardSnapshot(
    DashboardMetrics Metrics,
    IReadOnlyList<DashboardWorkspace> Workspaces,
    IReadOnlyList<DashboardJob> RecentJobs,
    IReadOnlyList<DashboardEvent> RecentEvents,
    IReadOnlyList<DashboardQuestion> OpenQuestions);

public sealed record DashboardMetrics(
    int WorkspaceCount,
    int SourceCount,
    int DocumentCount,
    int ChunkCount,
    int EmbeddedChunkCount,
    int ClaimCount,
    int UnreviewedClaimCount,
    int ContestedClaimCount,
    int NodeCount,
    int RelationshipCount,
    int OpenConflictCount,
    int ActiveJobCount,
    int FailedJobCount,
    int ActiveRunCount,
    int ReportCount);

public sealed record DashboardWorkspace(
    Guid Id,
    string Name,
    string AccentColor,
    string? Objective,
    int SourceCount,
    int ClaimCount,
    int NodeCount,
    int RunCount,
    DateTime UpdatedAtUtc);

public sealed record DashboardJob(
    Guid Id,
    Guid WorkspaceId,
    string Title,
    string JobType,
    ProcessingStatus Status,
    int ProgressPercent,
    string? CurrentStage,
    string? FailureReason,
    DateTime CreatedAtUtc);

public sealed record DashboardEvent(
    Guid Id,
    Guid WorkspaceId,
    string EventType,
    string Title,
    string? Detail,
    DateTime CreatedAtUtc);

public sealed record DashboardQuestion(Guid Id, Guid WorkspaceId, string Text, bool IsAiGenerated);
