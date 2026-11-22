using Microsoft.EntityFrameworkCore;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Application.Monitoring;

/// <summary>
/// Read model for the processing monitor. Progress comes from real stage rows, so the bar
/// always reflects work actually completed rather than a timer.
/// </summary>
public sealed class JobMonitorService
{
    private readonly IAppDbContext _db;
    private readonly IBackgroundJobQueue _queue;

    public JobMonitorService(IAppDbContext db, IBackgroundJobQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    public async Task<IReadOnlyList<JobSummary>> ListAsync(
        Guid? workspaceId = null,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var query = _db.ProcessingJobs.AsNoTracking();
        if (workspaceId is not null)
        {
            query = query.Where(j => j.WorkspaceId == workspaceId);
        }

        return await query
            .OrderByDescending(j => j.CreatedAtUtc)
            .Take(take)
            .Select(j => new JobSummary(
                j.Id,
                j.WorkspaceId,
                j.Title,
                j.JobType,
                j.Status,
                j.ProgressPercent,
                j.CurrentStage,
                j.RetryCount,
                j.FailureReason,
                j.CancellationRequested,
                j.StartedAtUtc,
                j.CompletedAtUtc,
                j.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<JobDetail?> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _db.ProcessingJobs
            .AsNoTracking()
            .Include(j => j.Steps)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null)
        {
            return null;
        }

        return new JobDetail(
            job.Id,
            job.WorkspaceId,
            job.Title,
            job.JobType,
            job.Status,
            job.ProgressPercent,
            job.CurrentStage,
            job.RetryCount,
            job.FailureReason,
            job.CancellationRequested,
            job.StartedAtUtc,
            job.CompletedAtUtc,
            job.CreatedAtUtc,
            job.Steps
                .OrderBy(s => s.Order)
                .Select(s => new JobStepDetail(
                    s.Order,
                    s.Name,
                    s.Status,
                    s.StartedAtUtc,
                    s.CompletedAtUtc,
                    s.Detail))
                .ToList());
    }

    public Task CancelAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        _queue.RequestCancellationAsync(jobId, cancellationToken);
}

public sealed record JobSummary(
    Guid Id,
    Guid WorkspaceId,
    string Title,
    string JobType,
    ProcessingStatus Status,
    int ProgressPercent,
    string? CurrentStage,
    int RetryCount,
    string? FailureReason,
    bool CancellationRequested,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime CreatedAtUtc);

public sealed record JobDetail(
    Guid Id,
    Guid WorkspaceId,
    string Title,
    string JobType,
    ProcessingStatus Status,
    int ProgressPercent,
    string? CurrentStage,
    int RetryCount,
    string? FailureReason,
    bool CancellationRequested,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime CreatedAtUtc,
    IReadOnlyList<JobStepDetail> Steps);

public sealed record JobStepDetail(
    int Order,
    string Name,
    ProcessingStatus Status,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? Detail);
